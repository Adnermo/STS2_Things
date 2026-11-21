using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Background SHA-256 file fingerprinting with bounded async flush barrier so submitted reports
/// never emit "pending" hashes for STS2_Things.dll, STS2_Things.pck, or active mod files.
/// </summary>
internal static class BugFileFingerprints
{
    private static readonly ConcurrentDictionary<string, Task<JsonObject>> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static void Prime(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = Describe(path);
        }
    }

    internal static void PrimeMany(IEnumerable<string?> paths)
    {
        foreach (string? path in paths)
        {
            Prime(path);
        }
    }

    internal static JsonObject Describe(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new JsonObject { ["state"] = "not_on_disk" };
            }
            var file = new FileInfo(path);
            string key = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            var task = Cache.GetOrAdd(key, _ => Task.Run(() => Hash(file.FullName)));
            return task.IsCompletedSuccessfully
                ? (JsonObject)task.Result.DeepClone()
                : new JsonObject { ["state"] = task.IsFaulted ? "unavailable" : "pending" };
        }
        catch (IOException)
        {
            return new JsonObject { ["state"] = "unavailable" };
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonObject { ["state"] = "unavailable" };
        }
        catch (ArgumentException)
        {
            return new JsonObject { ["state"] = "not_on_disk" };
        }
    }

    internal static string? TryGetSha256(string? path)
    {
        var desc = Describe(path);
        return desc["state"]?.GetValue<string>() == "complete"
            ? desc["sha256"]?.GetValue<string>()
            : null;
    }

    /// <summary>
    /// Awaits all in-flight background SHA-256 computations up to <paramref name="timeoutMs"/>.
    /// Returns true if all pending computations completed within the timeout.
    /// </summary>
    internal static async Task<bool> FlushPendingAsync(int timeoutMs = 1500, CancellationToken cancellationToken = default)
    {
        int clampedMs = Math.Clamp(timeoutMs, 50, 15_000);
        var pending = Cache.Values.Where(t => !t.IsCompleted).ToArray();
        if (pending.Length == 0) return true;

        try
        {
            await Task.WhenAll(pending)
                .WaitAsync(TimeSpan.FromMilliseconds(clampedMs), cancellationToken)
                .ConfigureAwait(false);
            return Cache.Values.All(t => t.IsCompleted);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            // Individual Hash() tasks catch IO exceptions; guard against unexpected task faults.
            return false;
        }
    }

    internal static void ClearForTests() => Cache.Clear();

    private static JsonObject Hash(string path)
    {
        try
        {
            var before = new FileInfo(path);
            long size = before.Length;
            DateTime modified = before.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var after = new FileInfo(path);
            if (after.Length != size || after.LastWriteTimeUtc != modified)
            {
                return new JsonObject { ["state"] = "changed_during_capture" };
            }
            return new JsonObject { ["state"] = "complete", ["bytes"] = size, ["sha256"] = hash };
        }
        catch (IOException)
        {
            return new JsonObject { ["state"] = "unavailable" };
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonObject { ["state"] = "unavailable" };
        }
    }
}
