using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Single-worker sequential background SHA-256 file fingerprinting with bounded async flush barrier
/// so startup avoids I/O and ThreadPool storms while submitted reports never emit "pending" hashes
/// for STS2_Things.dll, STS2_Things.pck, or active mod files.
/// </summary>
internal static class BugFileFingerprints
{
    private sealed class Entry(string fullPath)
    {
        internal string FullPath { get; } = fullPath;
        internal TaskCompletionSource<JsonObject> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<Entry> WorkQueue = new();
    private static readonly ConcurrentQueue<Func<IReadOnlyList<string>>> DiscoveryQueue = new();
    private static readonly object WorkerGate = new();
    private static Task _workerTask = Task.CompletedTask;
    private static bool _workerRunning;

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

    internal static void PrimeDeferred(Func<IReadOnlyList<string>> discoverPaths)
    {
        if (discoverPaths is null) return;
        DiscoveryQueue.Enqueue(discoverPaths);
        EnsureWorkerStarted();
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
            if (!Cache.TryGetValue(key, out Entry? entry))
            {
                var created = new Entry(file.FullName);
                if (Cache.TryAdd(key, created))
                {
                    entry = created;
                    WorkQueue.Enqueue(created);
                    EnsureWorkerStarted();
                }
                else
                {
                    Cache.TryGetValue(key, out entry);
                }
            }

            if (entry is null) return new JsonObject { ["state"] = "pending" };
            var task = entry.Completion.Task;
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
    /// Awaits all in-flight sequential background SHA-256 computations up to <paramref name="timeoutMs"/>.
    /// Returns true if all pending computations completed within the timeout.
    /// </summary>
    internal static async Task<bool> FlushPendingAsync(int timeoutMs = 1500, CancellationToken cancellationToken = default)
    {
        int clampedMs = Math.Clamp(timeoutMs, 50, 15_000);
        long deadline = System.Environment.TickCount64 + clampedMs;

        try
        {
            while (true)
            {
                Task worker;
                bool idle;
                lock (WorkerGate)
                {
                    worker = _workerTask;
                    idle = !_workerRunning && DiscoveryQueue.IsEmpty && WorkQueue.IsEmpty;
                }

                if (idle && Cache.Values.All(e => e.Completion.Task.IsCompleted))
                {
                    return true;
                }

                long remainingMs = deadline - System.Environment.TickCount64;
                if (remainingMs <= 0) return false;

                if (!worker.IsCompleted)
                {
                    await worker
                        .WaitAsync(TimeSpan.FromMilliseconds(remainingMs), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    if (!DiscoveryQueue.IsEmpty || !WorkQueue.IsEmpty)
                    {
                        EnsureWorkerStarted();
                        continue;
                    }
                    var pending = Cache.Values.Select(e => e.Completion.Task).Where(t => !t.IsCompleted).ToArray();
                    if (pending.Length == 0) return true;
                    await Task.WhenAll(pending)
                        .WaitAsync(TimeSpan.FromMilliseconds(remainingMs), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
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

    internal static void ClearForTests()
    {
        Cache.Clear();
        while (DiscoveryQueue.TryDequeue(out _)) { }
        while (WorkQueue.TryDequeue(out _)) { }
    }

    private static void EnsureWorkerStarted()
    {
        lock (WorkerGate)
        {
            if (!_workerRunning)
            {
                _workerRunning = true;
                _workerTask = Task.Run(WorkerLoop);
            }
        }
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            if (DiscoveryQueue.TryDequeue(out var discover))
            {
                try
                {
                    IReadOnlyList<string>? paths = discover();
                    if (paths is not null)
                    {
                        PrimeMany(paths);
                    }
                }
                catch (Exception)
                {
                    // Deferred path discovery is best-effort.
                }
                continue;
            }

            if (WorkQueue.TryDequeue(out Entry? entry))
            {
                try
                {
                    entry.Completion.TrySetResult(Hash(entry.FullPath));
                }
                catch (Exception)
                {
                    entry.Completion.TrySetResult(new JsonObject { ["state"] = "unavailable" });
                }
                continue;
            }

            lock (WorkerGate)
            {
                if (DiscoveryQueue.IsEmpty && WorkQueue.IsEmpty)
                {
                    _workerRunning = false;
                    return;
                }
            }
        }
    }

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
