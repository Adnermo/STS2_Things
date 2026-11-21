using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Captures scrubbed runtime faults, nested inner exception chains, Harmony owner attribution,
/// and deduplicated fault signatures across the current and previous game session.
/// </summary>
internal static class BugRuntimeFaults
{
    internal const int MaxRetainedRecords = 32;

    private static readonly ConcurrentQueue<JsonObject> Pending = new();
    private static readonly ConcurrentQueue<JsonObject> Recent = new();
    private static readonly object RecordsLock = new();
    private static readonly Dictionary<string, JsonObject> Repeated = new(StringComparer.Ordinal);
    private static readonly Dictionary<JsonObject, string> RecordKeys = new(ReferenceEqualityComparer.Instance);
    private static long _next;
    private static int _dropped;
    private static readonly string Session = Guid.NewGuid().ToString("N")[..8];
    private static readonly object DiskLock = new();
    private static string? _path;
    private static int _writer, _dirty;

    internal static int Dropped => Volatile.Read(ref _dropped);

    /// <summary>
    /// Optional hook wired by BugRunRecorder to attribute Harmony patch owners touching fault stack frames.
    /// </summary>
    internal static Func<IReadOnlyList<string>, IReadOnlyList<string>>? HarmonyOwnerResolver { get; set; }

    internal static void Capture(string text, int skipFrames, IEnumerable<string>? engineFunctions = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.StartsWith("[Things BugReport]", StringComparison.Ordinal)) return;

        var parsed = LogSanitizer.ParseLogText(text, skipFrames + 1, engineFunctions);
        RecordParsedFault(parsed);
    }

    internal static void CaptureException(Exception exception, int skipFrames = 0)
    {
        if (exception is null) return;
        var parsed = LogSanitizer.ParseException(exception, skipFrames + 1);
        RecordParsedFault(parsed);
    }

    private static void RecordParsedFault(ParsedFaultInfo parsed)
    {
        string nowIso = DateTimeOffset.UtcNow.ToString("O");
        lock (RecordsLock)
        {
            if (RecordRepeatedLocked(parsed.Signature, nowIso)) return;
        }

        var allFramesForAttribution = new List<string>(parsed.Frames);
        foreach (var inner in parsed.InnerExceptions)
        {
            foreach (string frame in inner.Frames)
            {
                if (!allFramesForAttribution.Contains(frame)) allFramesForAttribution.Add(frame);
            }
        }

        IReadOnlyList<string> harmonyOwners = [];
        if (HarmonyOwnerResolver is { } resolver && allFramesForAttribution.Count > 0)
        {
            try
            {
                harmonyOwners = resolver(allFramesForAttribution);
            }
            catch (Exception)
            {
                // Harmony attribution must never fail fault capture.
                harmonyOwners = [];
            }
        }

        var detail = BuildFaultObject(parsed, harmonyOwners, nowIso);

        lock (RecordsLock)
        {
            if (RecordRepeatedLocked(parsed.Signature, nowIso)) return;

            Repeated[parsed.Signature] = detail;
            RecordKeys[detail] = parsed.Signature;
            Pending.Enqueue(detail);
            Recent.Enqueue(detail);

            while (Pending.Count > MaxRetainedRecords) Pending.TryDequeue(out _);
            while (Recent.Count > MaxRetainedRecords)
            {
                if (Recent.TryDequeue(out JsonObject? old))
                {
                    if (RecordKeys.Remove(old, out string? oldKey)) Repeated.Remove(oldKey);
                    Interlocked.Increment(ref _dropped);
                }
            }
        }

        SchedulePersist();
    }

    private static JsonObject BuildFaultObject(ParsedFaultInfo parsed, IReadOnlyList<string> harmonyOwners, string nowIso)
    {
        var frameNodes = parsed.Frames.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray();
        var stackNodes = parsed.Frames.Select(f => (JsonNode?)JsonValue.Create("at " + f)).ToArray();

        var detail = new JsonObject
        {
            ["fault_id"] = "fault-" + Session + "-" + Interlocked.Increment(ref _next),
            ["signature"] = parsed.Signature,
            ["exception_type"] = parsed.ExceptionType,
            ["type"] = parsed.ExceptionType,
            ["message"] = parsed.Message,
            ["top_frame"] = parsed.TopFrame,
            ["frames"] = new JsonArray(frameNodes),
            ["stack"] = new JsonArray(stackNodes),
            ["code"] = parsed.Code,
            ["count"] = 1,
            ["occurrences"] = 1,
            ["first_at"] = nowIso,
            ["first_seen_utc"] = nowIso,
            ["observed_at"] = nowIso,
            ["last_at"] = nowIso,
            ["last_seen_utc"] = nowIso,
            ["last_observed_at"] = nowIso,
            ["session"] = "current",
        };

        if (!string.IsNullOrEmpty(parsed.MissingMember))
        {
            detail["member"] = parsed.MissingMember;
        }

        if (harmonyOwners.Count > 0)
        {
            var ownerNodes = harmonyOwners
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => LogSanitizer.Cap(o.Trim(), 300))
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .Select(o => (JsonNode?)JsonValue.Create(o))
                .ToArray();
            detail["harmony_owners"] = new JsonArray(ownerNodes);
        }
        else
        {
            detail["harmony_owners"] = new JsonArray();
        }

        if (parsed.InnerExceptions.Count > 0)
        {
            var innerObjects = new JsonArray();
            foreach (var inner in parsed.InnerExceptions.Take(LogSanitizer.MaxInnerExceptions))
            {
                var innerFrames = inner.Frames.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray();
                innerObjects.Add(new JsonObject
                {
                    ["exception_type"] = inner.ExceptionType,
                    ["type"] = inner.ExceptionType,
                    ["message"] = inner.Message,
                    ["frames"] = new JsonArray(innerFrames),
                });
            }
            detail["inner_exceptions"] = innerObjects;

            var distinctChain = new[] { parsed.ExceptionType }
                .Concat(parsed.InnerExceptions.Select(i => i.ExceptionType))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .Select(t => (JsonNode?)JsonValue.Create(t))
                .ToArray();
            if (distinctChain.Length > 1)
            {
                detail["exception_types"] = new JsonArray(distinctChain);
            }
        }

        return detail;
    }

    private static bool RecordRepeatedLocked(string signature, string nowIso)
    {
        if (!Repeated.TryGetValue(signature, out JsonObject? record)) return false;
        int nextCount = (record["occurrences"]?.GetValue<int>() ?? record["count"]?.GetValue<int>() ?? 1) + 1;
        record["occurrences"] = nextCount;
        record["count"] = nextCount;
        record["last_at"] = nowIso;
        record["last_seen_utc"] = nowIso;
        record["last_observed_at"] = nowIso;
        SchedulePersist();
        return true;
    }

    internal static bool TryTake(out JsonObject? detail)
    {
        lock (RecordsLock)
        {
            bool found = Pending.TryDequeue(out JsonObject? record);
            detail = (JsonObject?)record?.DeepClone();
            return found;
        }
    }

    internal static JsonArray Snapshot()
    {
        lock (RecordsLock) return new(Recent.Select(e => e.DeepClone()).ToArray());
    }

    internal static void InitializePersistence(string path)
    {
        _path = path;
        try
        {
            var prior = BugReportStore.ReadObject(path);
            JsonArray? records = prior?["faults"] as JsonArray ?? prior?["errors"] as JsonArray;
            lock (RecordsLock)
            {
                if (records is not null)
                {
                    foreach (var record in records.OfType<JsonObject>().TakeLast(MaxRetainedRecords))
                    {
                        var restored = (JsonObject)record.DeepClone();
                        restored["from_previous_session"] = true;
                        restored["session"] = "previous";
                        Recent.Enqueue(restored);
                    }
                    while (Recent.Count > MaxRetainedRecords)
                    {
                        if (Recent.TryDequeue(out JsonObject? old) && RecordKeys.Remove(old, out string? oldKey))
                        {
                            Repeated.Remove(oldKey);
                        }
                    }
                }
            }
        }
        catch (IOException)
        {
            // Startup fault restoration is best-effort when disk is locked.
        }
        catch (UnauthorizedAccessException)
        {
            // Startup fault restoration is best-effort under restricted permissions.
        }
    }

    private static void SchedulePersist()
    {
        if (_path is null) return;
        Interlocked.Exchange(ref _dirty, 1);
        if (Interlocked.CompareExchange(ref _writer, 1, 0) != 0) return;
        // Pure structural JSON and file IO; no Godot or game objects on this worker.
        _ = Task.Run(async () =>
        {
            try
            {
                do
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    if (Interlocked.Exchange(ref _dirty, 0) == 1) Persist();
                } while (Volatile.Read(ref _dirty) == 1);
            }
            finally
            {
                Interlocked.Exchange(ref _writer, 0);
                if (Volatile.Read(ref _dirty) == 1) SchedulePersist();
            }
        });
    }

    internal static void Persist()
    {
        if (_path is not { } path) return;
        Interlocked.Exchange(ref _dirty, 0);
        try
        {
            var snapshot = Snapshot();
            var errorsAlias = new JsonArray(snapshot.Select(n => n?.DeepClone()).ToArray());
            lock (DiskLock)
            {
                BugReportStore.WriteAtomic(
                    path,
                    new JsonObject
                    {
                        ["schema"] = BugReportSchemaV4.SchemaVersion,
                        ["faults"] = snapshot,
                        ["errors"] = errorsAlias,
                        ["dropped"] = Dropped,
                    }.ToJsonString());
            }
        }
        catch (Exception)
        {
            // Persistence must not turn an existing failure into another fault.
        }
    }

    internal static void BackfillStartupLog(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 128_000), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            var blocks = Regex.Matches(text, @"(?m)^(?:ERROR:|SCRIPT ERROR:|\[ERROR\])[^\n]*(?:\n(?!ERROR:|SCRIPT ERROR:|\[ERROR\]|\[INFO\]|\[WARN\])[^\n]*){0,35}");
            foreach (Match block in blocks.Cast<Match>().TakeLast(24))
            {
                Capture(block.Value, 0);
            }
        }
        catch (IOException)
        {
            // Missing or locked startup log is ignored safely.
        }
        catch (UnauthorizedAccessException)
        {
            // Inaccessible startup log is ignored safely.
        }
    }

    internal static void ResetForTests()
    {
        lock (RecordsLock)
        {
            while (Pending.TryDequeue(out _)) { }
            while (Recent.TryDequeue(out _)) { }
            Repeated.Clear();
            RecordKeys.Clear();
            Interlocked.Exchange(ref _dropped, 0);
            Interlocked.Exchange(ref _next, 0);
            _path = null;
        }
    }
}
