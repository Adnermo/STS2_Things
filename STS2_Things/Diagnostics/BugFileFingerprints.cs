using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace STS2_Things.Diagnostics;

/// <summary>File IO only on workers. No game objects or local paths enter the report.</summary>
internal static class BugFileFingerprints
{
    private static readonly ConcurrentDictionary<string, Task<JsonObject>> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static JsonObject Describe(string path)
    {
        try
        {
            if (!File.Exists(path)) return new JsonObject { ["state"] = "not_on_disk" };
            var file = new FileInfo(path);
            string key = path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            var task = Cache.GetOrAdd(key, _ => Task.Run(() => Hash(path)));
            return task.IsCompletedSuccessfully ? (JsonObject)task.Result.DeepClone()
                : new JsonObject { ["state"] = task.IsFaulted ? "unavailable" : "pending" };
        }
        catch (IOException) { return new JsonObject { ["state"] = "unavailable" }; }
        catch (UnauthorizedAccessException) { return new JsonObject { ["state"] = "unavailable" }; }
    }
    private static JsonObject Hash(string path)
    {
        try
        {
            var before = new FileInfo(path);
            long size = before.Length; DateTime modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var after = new FileInfo(path);
            if (after.Length != size || after.LastWriteTimeUtc != modified) return new JsonObject { ["state"] = "changed_during_capture" };
            return new JsonObject { ["state"] = "complete", ["bytes"] = size, ["sha256"] = hash };
        }
        catch (IOException) { return new JsonObject { ["state"] = "unavailable" }; }
        catch (UnauthorizedAccessException) { return new JsonObject { ["state"] = "unavailable" }; }
    }
}
