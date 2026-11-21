using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace STS2_Things.Diagnostics;

/// <summary>Immutable Schema v4 JSON snapshots and durable files; never queries live game state.</summary>
internal sealed class BugReportStore(string directory)
{
    internal const int SchemaVersion = BugReportSchemaV4.SchemaVersion;
    internal const int MaxEvents = 700;

    internal string DirectoryPath { get; } = directory;
    internal JsonObject? Current { get; private set; }
    private string CurrentPath => Path.Combine(DirectoryPath, "current-run.json");
    private string LastRunPath => Path.Combine(DirectoryPath, "last-run.json");
    private readonly object _writeGate = new();
    private string? _pendingCurrentWrite;
    private string? _pendingArchiveWrite;
    private Task _writeTask = Task.CompletedTask;
    private bool _writing;
    private Exception? _writeFailure;

    internal static void WriteAtomic(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value, new UTF8Encoding(false));
            if (File.Exists(path) && HasCompleteJsonEnvelope(path))
            {
                // A torn/truncated primary must not overwrite the good recovery copy.
                File.Copy(path, path + ".backup", true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool HasCompleteJsonEnvelope(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= 2) return false;

            Span<byte> head = stackalloc byte[(int)Math.Min(32, stream.Length)];
            int headRead = stream.Read(head);
            byte first = 0;
            for (int i = 0; i < headRead; i++)
            {
                byte b = head[i];
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                if (i == 0 && headRead >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                {
                    i = 2;
                    continue;
                }
                first = b;
                break;
            }
            if (first is not ((byte)'{' or (byte)'[')) return false;

            int tailLen = (int)Math.Min(32, stream.Length);
            stream.Seek(-tailLen, SeekOrigin.End);
            Span<byte> tail = stackalloc byte[tailLen];
            int tailRead = stream.Read(tail);
            byte last = 0;
            for (int i = tailRead - 1; i >= 0; i--)
            {
                byte b = tail[i];
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                last = b;
                break;
            }
            return (first == (byte)'{' && last == (byte)'}') || (first == (byte)'[' && last == (byte)']');
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static JsonObject? ReadObject(string path)
    {
        foreach (string candidate in new[] { path, path + ".backup" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                return JsonNode.Parse(File.ReadAllText(candidate, Encoding.UTF8)) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                // Try backup candidate if primary is torn or invalid JSON.
            }
        }
        return null;
    }

    internal bool Begin(string runKey, JsonObject metadata, JsonObject snapshot, bool syncArchive = true)
    {
        if (Current?["local_run_key"]?.GetValue<string>() == runKey) return true;
        // Capture/serialize is deliberately pure. Archiving cannot initialize another run.
        bool hadCurrent = Current is not null;
        if (hadCurrent)
        {
            if (syncArchive) Archive();
            else _ = ArchiveAsync();
        }

        // Only inspect current-run.json when starting from a cold store (process boot / restore).
        // When switching runs in-process, Current already held the previous run and was just archived.
        JsonObject? disk = hadCurrent ? null : ReadObject(CurrentPath);
        bool restored = disk?["local_run_key"]?.GetValue<string>() == runKey;
        if (restored)
        {
            Current = disk;
        }
        else
        {
            if (disk is not null)
            {
                string diskJson = disk.ToJsonString();
                if (syncArchive) WriteAtomic(LastRunPath, diskJson);
                else _ = EnqueueWrite(currentJson: null, archiveJson: diskJson);
            }
            Current = new JsonObject
            {
                ["schema"] = SchemaVersion,
                ["schema_version"] = SchemaVersion,
                ["client_run_id"] = Guid.NewGuid().ToString("N"),
                ["local_run_key"] = runKey,
                ["started_at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["events"] = new JsonArray(),
                ["initial_state"] = snapshot.DeepClone(),
                ["initial_state_kind"] = "first_observed",
                ["state"] = null,
                ["dropped_events"] = 0,
            };
        }
        foreach (var item in metadata) Current![item.Key] = item.Value?.DeepClone();
        Current!["schema"] = SchemaVersion;
        Current["schema_version"] = SchemaVersion;
        if (Current["events"] is not JsonArray) Current["events"] = new JsonArray();
        Update(snapshot, clone: false);
        return restored;
    }

    internal void Update(JsonObject snapshot, bool clone = true)
    {
        if (Current is null) return;
        Current["state"] = clone || snapshot.Parent is not null ? snapshot.DeepClone() : snapshot;
        Current["updated_at"] = DateTimeOffset.UtcNow.ToString("O");
    }

    internal void Track(JsonObject record) => AppendEvent(record.DeepClone());

    /// <summary>Appends a freshly constructed event record without redundant DeepClone overhead.</summary>
    internal void TrackOwned(JsonObject record) =>
        AppendEvent(record.Parent is not null ? record.DeepClone() : record);

    private void AppendEvent(JsonNode record)
    {
        if (Current is null) return;
        var events = Current["events"] as JsonArray ?? new JsonArray();
        Current["events"] = events;
        events.Add(record);
        while (events.Count > MaxEvents)
        {
            events.RemoveAt(0);
            Current["dropped_events"] = (Current["dropped_events"]?.GetValue<int>() ?? 0) + 1;
        }
    }

    internal void Persist()
    {
        QueuePersist().GetAwaiter().GetResult();
        lock (_writeGate)
        {
            if (_writeFailure is { } failure) throw new IOException("Unable to persist the local report journal.", failure);
        }
    }

    internal Task QueuePersist()
    {
        // Serialize structural JSON once on the caller thread without cloning 50k+ JsonNodes.
        // The worker never reads game/Godot objects, and superseded pending writes coalesce.
        if (Current is null) return Task.CompletedTask;
        string frozenJson = Current.ToJsonString();
        return EnqueueWrite(currentJson: frozenJson, archiveJson: null);
    }

    internal Task ArchiveAsync()
    {
        if (Current is null) return Task.CompletedTask;
        string frozenJson = Current.ToJsonString();
        return EnqueueWrite(currentJson: frozenJson, archiveJson: frozenJson);
    }

    internal async Task FlushPendingWritesAsync(int timeoutMs = 1500)
    {
        Task pending;
        lock (_writeGate)
        {
            pending = _writeTask;
        }
        if (pending.IsCompleted) return;
        try
        {
            await pending
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 50, 15_000)))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Bounded barrier: never stall caller indefinitely on slow disks.
        }
    }

    private Task EnqueueWrite(string? currentJson, string? archiveJson)
    {
        lock (_writeGate)
        {
            if (archiveJson is not null)
            {
                _pendingArchiveWrite = archiveJson;
            }
            if (currentJson is not null)
            {
                _pendingCurrentWrite = currentJson;
            }
            if (!_writing && (_pendingArchiveWrite is not null || _pendingCurrentWrite is not null))
            {
                _writing = true;
                _writeTask = Task.Run(WritePending);
            }
            return _writeTask;
        }
    }

    private void WritePending()
    {
        while (true)
        {
            string? archiveJson = null;
            string? currentJson = null;
            lock (_writeGate)
            {
                if (_pendingArchiveWrite is not null)
                {
                    archiveJson = _pendingArchiveWrite;
                    _pendingArchiveWrite = null;
                }
                else if (_pendingCurrentWrite is not null)
                {
                    currentJson = _pendingCurrentWrite;
                    _pendingCurrentWrite = null;
                }
                else
                {
                    _writing = false;
                    return;
                }
            }
            try
            {
                if (archiveJson is not null)
                {
                    WriteAtomic(LastRunPath, archiveJson);
                }
                else if (currentJson is not null)
                {
                    WriteAtomic(CurrentPath, currentJson);
                }
                lock (_writeGate) _writeFailure = null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                lock (_writeGate) _writeFailure = failure;
            }
        }
    }

    internal void Archive()
    {
        if (Current is null) return;
        ArchiveAsync().GetAwaiter().GetResult();
        lock (_writeGate)
        {
            if (_writeFailure is { } failure) throw new IOException("Unable to archive the local report journal.", failure);
        }
    }

    internal JsonObject JournalForUpload()
    {
        var journal = (JsonObject)(Current?.DeepClone()
            ?? ReadObject(CurrentPath)
            ?? ReadObject(LastRunPath)
            ?? new JsonObject { ["schema"] = SchemaVersion, ["events"] = new JsonArray(), ["state"] = null });
        journal.Remove("local_run_key");
        journal["schema"] = SchemaVersion;
        journal["schema_version"] = SchemaVersion;
        if (journal["events"] is not JsonArray) journal["events"] = new JsonArray();
        return journal;
    }
}

internal sealed record ReportSubmission(bool Success, string? ReportId, string Reason, int Pending);

/// <summary>Each explicit submission has an independent, immutable retry body and ID.</summary>
internal sealed class BugReportOutbox(string directory, HttpClient client, Uri endpoint)
{
    internal const int MaxBytes = 1_200_000;
    private readonly SemaphoreSlim _sending = new(1);
    private string PendingDir => Path.Combine(directory, "pending");
    internal int PendingCount => Directory.Exists(PendingDir) ? Directory.GetFiles(PendingDir, "*.json").Length : 0;

    internal string Enqueue(JsonObject journal, string description)
    {
        string trimmed = description.Trim();
        string normalized = trimmed[..Math.Min(4000, trimmed.Length)];
        if (Directory.Exists(PendingDir))
        {
            foreach (string file in Directory.GetFiles(PendingDir, "*.json"))
            {
                var prior = ReadPending(file);
                if (prior?["description"]?.GetValue<string>() == normalized
                    && prior["run"]?["client_run_id"]?.GetValue<string>() == journal["client_run_id"]?.GetValue<string>())
                {
                    return prior["client_report_id"]!.GetValue<string>();
                }
            }
        }

        string id = Guid.NewGuid().ToString("N");
        var runCopy = (JsonObject)journal.DeepClone();
        runCopy["schema"] = BugReportStore.SchemaVersion;
        runCopy["schema_version"] = BugReportStore.SchemaVersion;
        if (runCopy["events"] is not JsonArray) runCopy["events"] = new JsonArray();

        var payload = new JsonObject
        {
            ["schema"] = BugReportStore.SchemaVersion,
            ["schema_version"] = BugReportStore.SchemaVersion,
            ["source"] = "sts2_things",
            ["client_report_id"] = id,
            ["reported_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["description"] = trimmed,
            ["run"] = runCopy,
        };

        string complete = payload.ToJsonString();
        // Always preserve the full description/evidence before applying wire-size limits.
        BugReportStore.WriteAtomic(Path.Combine(directory, "reports", id + ".json"), complete);

        payload["description"] = normalized;
        if (trimmed.Length > 4000) payload["description_truncated"] = true;
        Reduce(payload);
        BugReportStore.WriteAtomic(Path.Combine(PendingDir, id + ".json"), payload.ToJsonString());
        return id;
    }

    /// <summary>
    /// Saves a complete local diagnostic bundle without enqueuing a duplicate network upload.
    /// Used by the 5-minute duplicate F2 submission guard.
    /// </summary>
    internal string SaveLocalOnly(JsonObject journal, string description, string reason, string? priorReportId = null)
    {
        string id = Guid.NewGuid().ToString("N");
        var runCopy = (JsonObject)journal.DeepClone();
        runCopy["schema"] = BugReportStore.SchemaVersion;
        runCopy["schema_version"] = BugReportStore.SchemaVersion;
        if (runCopy["events"] is not JsonArray) runCopy["events"] = new JsonArray();

        var payload = new JsonObject
        {
            ["schema"] = BugReportStore.SchemaVersion,
            ["schema_version"] = BugReportStore.SchemaVersion,
            ["source"] = "sts2_things",
            ["client_report_id"] = id,
            ["reported_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["description"] = description.Trim(),
            ["local_only"] = true,
            ["local_only_reason"] = reason,
            ["duplicate_of_report_id"] = priorReportId,
            ["run"] = runCopy,
        };
        BugReportStore.WriteAtomic(Path.Combine(directory, "reports", id + ".local.json"), payload.ToJsonString());
        return id;
    }

    private static JsonObject? ReadPending(string path)
    {
        try
        {
            return BugReportStore.ReadObject(path);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool ExceedsWireBudget(JsonObject payload) =>
        BugReportSchemaV4.ExceedsJsonNodeLimit(payload, 55_000)
        || Encoding.UTF8.GetByteCount(payload.ToJsonString()) > MaxBytes;

    private static void RemoveLeadingItems(JsonArray array, int count)
    {
        if (count <= 0 || array.Count == 0) return;
        int actual = Math.Min(count, array.Count);
        if (actual >= array.Count)
        {
            array.Clear();
            return;
        }
        if (actual < 16)
        {
            for (int i = 0; i < actual; i++) array.RemoveAt(0);
            return;
        }
        int keepCount = array.Count - actual;
        var kept = new JsonNode?[keepCount];
        for (int i = 0; i < keepCount; i++)
        {
            kept[i] = array[actual + i];
        }
        array.Clear();
        for (int i = 0; i < kept.Length; i++)
        {
            array.Add(kept[i]);
        }
    }

    private static void RemoveLeadingEvents(JsonObject run, JsonArray events, int count)
    {
        if (count <= 0 || events.Count == 0) return;
        int actual = Math.Min(count, events.Count);
        RemoveLeadingItems(events, actual);
        run["dropped_events"] = (run["dropped_events"]?.GetValue<int>() ?? 0) + actual;
    }

    private static void Reduce(JsonObject payload)
    {
        var run = (JsonObject)payload["run"]!;
        bool clamped = BugReportSchemaV4.EnforceBoundedTree(payload);
        bool exceeds = ExceedsWireBudget(payload);
        if (!clamped && !exceeds) return;

        var changes = run["upload_truncation"] as JsonArray ?? new JsonArray();
        run["upload_truncation"] = changes;
        if (clamped) changes.Add("bounded_tree_field_clamps");
        if (!exceeds) return;

        if (run["initial_state"] is JsonObject initial)
        {
            initial.Remove("route");
            initial.Remove("ui");
            changes.Add("initial_state.route/ui");
        }

        var events = run["events"] as JsonArray;
        bool droppedEvents = false;
        exceeds = ExceedsWireBudget(payload);
        while (exceeds && events is { Count: > 100 })
        {
            int excess = events.Count - 100;
            // Drop excess events in batches (halving remaining excess, minimum 25) instead of
            // 1 event per full 1.2 MB ToJsonString() + 55k-node walk.
            int drop = excess <= 25 ? excess : Math.Max(25, (excess + 1) / 2);
            RemoveLeadingEvents(run, events, drop);
            droppedEvents = true;
            exceeds = ExceedsWireBudget(payload);
        }
        if (droppedEvents) changes.Add("oldest events if needed");
        if (!exceeds) return;

        if (run["state"] is JsonObject state && state["route"] is JsonArray route)
        {
            foreach (var act in route.OfType<JsonObject>())
            {
                if (act["points"] is JsonArray points && points.Count > 15)
                {
                    RemoveLeadingItems(points, points.Count - 15);
                }
            }
            changes.Add("oldest route points");
        }

        if (ExceedsWireBudget(payload))
        {
            run.Remove("initial_state");
            if (run["state"] is JsonObject lastState) lastState.Remove("route");
            changes.Add("initial_state/full route retained in local reports file");
        }

        if (ExceedsWireBudget(payload) && run["state"] is JsonObject largeState)
        {
            // Preserve the native checkpoint and latest faults before secondary duplicate history.
            if (events is { Count: > 10 })
            {
                RemoveLeadingEvents(run, events, events.Count - 10);
            }
            foreach (var player in (largeState["players"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (player["combat"] is JsonObject combat)
                {
                    foreach (string pile in new[] { "draw_pile", "discard_pile", "exhaust_pile", "play_pile" })
                    {
                        if (combat[pile] is JsonArray cards && cards.Count > 50)
                        {
                            combat[pile + "_total"] = cards.Count;
                            while (cards.Count > 50) cards.RemoveAt(cards.Count - 1);
                        }
                    }
                }
            }
            if (run["harmony_patches"] is JsonArray patches && patches.Count > 120)
            {
                while (patches.Count > 120) patches.RemoveAt(patches.Count - 1);
            }
            if (run["assemblies"] is JsonArray assemblies && assemblies.Count > 150)
            {
                while (assemblies.Count > 150) assemblies.RemoveAt(assemblies.Count - 1);
            }
            changes.Add("older events and oversized duplicate live piles; native checkpoint preserved");
        }

        if (ExceedsWireBudget(payload) && run["screenshot"] is JsonObject image)
        {
            image.Remove("data");
            image["available"] = false;
            image["reason"] = "upload_size_limit";
            changes.Add("screenshot retained in local reports file");
        }

        if (ExceedsWireBudget(payload) && run["combat_replay"] is JsonObject replay)
        {
            replay.Remove("data");
            replay["available"] = false;
            replay["reason"] = "upload_size_limit";
            changes.Add("combat_replay retained in local reports file");
        }

        BugReportSchemaV4.EnforceBoundedTree(payload);
    }

    internal async Task<ReportSubmission> SendAsync(string? requestedId = null)
    {
        await _sending.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(PendingDir);
            Directory.CreateDirectory(Path.Combine(directory, "reports"));
            // Migration keeps the previous version's unsent report instead of overwriting it.
            string legacy = Path.Combine(directory, "pending-report.json");
            var old = BugReportStore.ReadObject(legacy);
            string? oldId = old?["client_report_id"]?.GetValue<string>();
            if (oldId is not null && System.Text.RegularExpressions.Regex.IsMatch(oldId, "^[a-fA-F0-9]{32}$"))
            {
                string target = Path.Combine(PendingDir, oldId + ".json");
                if (!File.Exists(target)) BugReportStore.WriteAtomic(target, old!.ToJsonString());
                File.Move(legacy, legacy + ".migrated", true);
            }

            var files = Directory.GetFiles(PendingDir, "*.json").OrderBy(File.GetCreationTimeUtc).ToArray();
            if (requestedId is not null) files = files.Where(p => Path.GetFileNameWithoutExtension(p) == requestedId).ToArray();
            if (files.Length == 0) return new(true, null, "nothing_pending", PendingCount);

            ReportSubmission? last = null;
            foreach (string file in files)
            {
                string body = File.ReadAllText(file, Encoding.UTF8);
                if (Encoding.UTF8.GetByteCount(body) > MaxBytes)
                {
                    return new(false, null, "too_large_saved_locally", PendingCount);
                }
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(endpoint, content).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return new(false, null, "http_" + (int)response.StatusCode, PendingCount);
                    }
                    string receiptText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var receipt = JsonNode.Parse(receiptText) as JsonObject;
                    string? reportId = receipt?["report_id"]?.GetValue<string>();
                    if (receipt?["ok"]?.GetValue<bool>() != true || !Guid.TryParseExact(reportId, "D", out _))
                    {
                        return new(false, null, "invalid_receipt", PendingCount);
                    }
                    BugReportStore.WriteAtomic(Path.Combine(directory, "last-receipt.json"), receiptText);
                    File.Move(file, Path.Combine(directory, "reports", Path.GetFileNameWithoutExtension(file) + ".sent.json"), true);
                    last = new(true, reportId, "uploaded", PendingCount);
                }
                catch (HttpRequestException)
                {
                    return new(false, null, "network_error", PendingCount);
                }
                catch (TaskCanceledException)
                {
                    return new(false, null, "timeout", PendingCount);
                }
                catch (System.Text.Json.JsonException)
                {
                    return new(false, null, "invalid_receipt", PendingCount);
                }
                catch (InvalidOperationException)
                {
                    return new(false, null, "invalid_receipt", PendingCount);
                }
            }
            return last!;
        }
        finally
        {
            _sending.Release();
        }
    }
}
