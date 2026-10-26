using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

/// <summary>Only structural error information crosses the report boundary; never raw log lines.</summary>
internal static class BugRuntimeFaults
{
    private static readonly ConcurrentQueue<JsonObject> Pending = new();
    private static readonly ConcurrentQueue<JsonObject> Recent = new();
    private static readonly object RecordsLock = new();
    private static readonly Dictionary<string, JsonObject> Repeated = new(StringComparer.Ordinal);
    private static readonly Dictionary<JsonObject, string> RecordKeys = new();
    private static long _next;
    private static int _dropped;
    private static readonly string Session = Guid.NewGuid().ToString("N")[..8];
    private static readonly object DiskLock = new();
    private static string? _path;
    private static int _writer, _dirty;
    internal static int Dropped => _dropped;
    private static readonly Regex ExceptionName = new(@"(?:^|\n|--->\s*)([A-Za-z_][\w.+`]*Exception)\s*:", RegexOptions.CultureInvariant);
    private static readonly Regex FrameName = new(@"^[\w.+<>`]+$", RegexOptions.CultureInvariant);

    internal static void Capture(string text, int skipFrames, IEnumerable<string>? engineFunctions = null)
    {
        if (text.StartsWith("[Things BugReport]", StringComparison.Ordinal)) return;
        text = text[..Math.Min(16000, text.Length)];
        string[] functions = engineFunctions?.Take(20).ToArray() ?? [];
        // Keep this private in-memory key bounded by the 32 retained records.
        // It is never included in reports, disk snapshots or network payloads.
        string key = text + "\n" + string.Join('\n', functions);
        bool suppliedStack = functions.Length > 0 || text.Contains("\n", StringComparison.Ordinal);
        if (suppliedStack)
            lock (RecordsLock)
                if (RecordRepeated(key)) return;
        var frames = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            string frame = line.Trim();
            if (frame.StartsWith("at ", StringComparison.Ordinal)) frame = frame[3..];
            else if (frame.StartsWith("在 ", StringComparison.Ordinal)) frame = frame[2..];
            else if (Regex.IsMatch(frame, @"^\[\d+\]\s", RegexOptions.CultureInvariant))
                frame = Regex.Replace(frame, @"^\[\d+\]\s*", "", RegexOptions.CultureInvariant);
            else continue;
            int args = frame.IndexOf('(');
            if (args >= 0) frame = frame[..args];
            // Godot C# backtraces include the return type before the method.
            if (frame.Contains(' ')) frame = frame[(frame.LastIndexOf(' ') + 1)..];
            if (frame.Length <= 250 && FrameName.IsMatch(frame) && frames.Count < 20) frames.Add(frame);
        }
        foreach (string function in functions)
        {
            string frame = function.Split('(')[0].Trim();
            if (frame.Contains(' ')) frame = frame[(frame.LastIndexOf(' ') + 1)..];
            if (frame.Length is > 0 and <= 250 && FrameName.IsMatch(frame) && frames.Count < 20 && !frames.Contains(frame)) frames.Add(frame);
        }
        // Some game errors are logged as a sentence; their caller still identifies
        // the failing component without uploading the sentence or any user data.
        if (frames.Count == 0)
            frames.AddRange(new StackTrace(Math.Clamp(skipFrames, 0, 28) + 2).GetFrames()?.Take(12).Select(f =>
                f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name) ?? []);
        if (!suppliedStack) key += "\n" + string.Join('\n', frames);
        string type = ExceptionName.Match(text) is { Success: true } match ? match.Groups[1].Value : "LoggedError";
        var detail = new JsonObject { ["type"] = type, ["frames"] = new JsonArray(frames.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()) };
        var chain = ExceptionName.Matches(text).Select(m => m.Groups[1].Value).Distinct().Take(8).ToArray();
        if (chain.Length > 1) detail["exception_types"] = new JsonArray(chain.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
        detail["code"] = text.Contains("Method not found", StringComparison.Ordinal) ? "missing_method"
            : text.Contains("not in a scene tree", StringComparison.Ordinal) ? "node_outside_tree"
            : text.Contains("out of bounds", StringComparison.Ordinal) ? "index_out_of_bounds"
            : type.EndsWith("NullReferenceException", StringComparison.Ordinal) ? "null_reference" : "runtime_error";
        // Preserve the missing API signature, never an arbitrary exception message.
        var member = Regex.Match(text, @"Method not found:\s*'([A-Za-z0-9_.+<>`\[\], ()]{1,500})'", RegexOptions.CultureInvariant);
        if (member.Success) detail["member"] = member.Groups[1].Value;
        detail["fault_id"] = "fault-" + Session + "-" + Interlocked.Increment(ref _next);
        detail["observed_at"] = DateTimeOffset.UtcNow.ToString("O");
        lock (RecordsLock)
        {
            if (RecordRepeated(key)) return;
            detail["occurrences"] = 1;
            Repeated[key] = detail;
            RecordKeys[detail] = key;
            Pending.Enqueue(detail);
            Recent.Enqueue(detail);
            while (Pending.Count > 32) Pending.TryDequeue(out _);
            while (Recent.Count > 32)
                if (Recent.TryDequeue(out JsonObject? old))
                {
                    if (RecordKeys.Remove(old, out string? oldKey)) Repeated.Remove(oldKey);
                    Interlocked.Increment(ref _dropped);
                }
        }
        SchedulePersist();
    }

    private static bool RecordRepeated(string key)
    {
        if (!Repeated.TryGetValue(key, out JsonObject? record)) return false;
        record["occurrences"] = record["occurrences"]!.GetValue<int>() + 1;
        record["last_observed_at"] = DateTimeOffset.UtcNow.ToString("O");
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
            lock (RecordsLock)
            if (prior?["errors"] is JsonArray records)
            {
                foreach (var record in records.OfType<JsonObject>().TakeLast(32))
                {
                    var restored = (JsonObject)record.DeepClone();
                    restored["from_previous_session"] = true;
                    Recent.Enqueue(restored);
                }
                while (Recent.Count > 32)
                    if (Recent.TryDequeue(out JsonObject? old) && RecordKeys.Remove(old, out string? oldKey)) Repeated.Remove(oldKey);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
            lock (DiskLock)
                BugReportStore.WriteAtomic(path, new JsonObject { ["schema"] = 1, ["errors"] = Snapshot(), ["dropped"] = Dropped }.ToJsonString());
        }
        catch (Exception) { /* Persistence must not turn an existing failure into another fault. */ }
    }

    internal static void BackfillStartupLog(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 128_000), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            var blocks = Regex.Matches(text, @"(?m)^(?:ERROR:|SCRIPT ERROR:|\[ERROR\])[^\n]*(?:\n(?!ERROR:|SCRIPT ERROR:|\[ERROR\]|\[INFO\]|\[WARN\])[^\n]*){0,35}");
            foreach (Match block in blocks.Cast<Match>().TakeLast(24)) Capture(block.Value, 0);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
