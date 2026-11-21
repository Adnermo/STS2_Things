using System.Text.Json.Nodes;
using STS2_Things.Diagnostics;

static partial class Tests
{
    private static void FaultTests(string root)
    {
        BugRuntimeFaults.ResetForTests();

        string text = "System.AggregateException: Fault at C:\\Users\\AliceSecret\\SavedGames\\run.json for 76561199999999999 from 192.168.10.42:7777\n"
            + "---> System.NullReferenceException: Object reference not set in /Users/BobSecret/mods/test.dll\n"
            + " [0] void ExampleSkin.Orb.UpdateVisuals() in C:\\PrivatePlayer\\skin.cs:line 9\n"
            + "Authorization: Bearer PRIVATE_TEST_CREDENTIAL\n76561199999999999";
        BugRuntimeFaults.Capture(text, 0);
        var fault = BugRuntimeFaults.Snapshot().Last()!.AsObject();
        string captured = fault.ToJsonString();
        Check(captured.Contains("ExampleSkin.Orb.UpdateVisuals"), "Godot numbered backtrace is retained");
        Check(fault["message"]!.GetValue<string>().Contains("<USER_HOME>/SavedGames/run.json"), "real outer exception message retained with <USER_HOME>/ scrubbing");
        Check(fault["message"]!.GetValue<string>().Contains("<STEAM_ID>") && fault["message"]!.GetValue<string>().Contains("<IP_ADDR>"), "Steam ID and IPv4 scrubbed in message");
        Check(!captured.Contains("AliceSecret")
            && !captured.Contains("BobSecret")
            && !captured.Contains("PrivatePlayer")
            && !captured.Contains("PRIVATE_TEST_CREDENTIAL")
            && !captured.Contains("76561199999999999")
            && !captured.Contains("192.168.10.42"),
            "native trace never includes raw user paths, IPs, Steam IDs, or credentials");
        Check(fault["exception_types"]!.AsArray().Count == 2, "nested exception types preserved");
        Check(fault["inner_exceptions"]!.AsArray().Count == 1, "inner_exceptions array populated");
        Check(fault["inner_exceptions"]![0]!["message"]!.GetValue<string>().Contains("<USER_HOME>/mods/test.dll"), "inner exception message retained and scrubbed");

        string log = Path.Combine(root, "startup.log");
        File.WriteAllText(log, "INFO: private startup\nERROR: Index p_index out of bounds\n [0] void Example.Loader.Import() in C:\\PrivatePlayer\\file.cs:line 1\n[INFO] end\n");
        BugRuntimeFaults.BackfillStartupLog(log);
        Check(BugRuntimeFaults.Snapshot().Last()!["code"]!.GetValue<string>() == "index_out_of_bounds", "pre-initializer engine error backfilled");
        Check(BugRuntimeFaults.Snapshot().Last()!.ToJsonString().Contains("Example.Loader.Import"), "startup backfill retains actual failing method");

        for (int i = 0; i < 1000; i++) BugRuntimeFaults.Capture("same repeating error", 0, ["Example.RepeatingError"]);
        var repeated = BugRuntimeFaults.Snapshot().Last()!.AsObject();
        Check(repeated["occurrences"]?.GetValue<int>() == 1000 && repeated["count"]?.GetValue<int>() == 1000, "repeated errors keep occurrences and count without evicting unrelated faults");
        Check(repeated["first_seen_utc"] is not null && repeated["last_seen_utc"] is not null, "deduplicated fault tracks first_seen_utc and last_seen_utc");
        Check(BugRuntimeFaults.Snapshot().Count == 3, "one repeated signature uses one bounded error record");

        for (int i = 0; i < 40; i++) BugRuntimeFaults.Capture("runtime error " + i, 0, ["Example.BoundedError"]);
        Check(BugRuntimeFaults.Snapshot().Count == 32 && BugRuntimeFaults.Dropped > 0, "error storm is bounded and drops are declared");

        string recovery = Path.Combine(root, "runtime-errors.json");
        BugRuntimeFaults.InitializePersistence(recovery);
        BugRuntimeFaults.Persist();
        var persistedDoc = BugReportStore.ReadObject(recovery);
        Check(persistedDoc?["schema"]?.GetValue<int>() == 4 && persistedDoc["faults"]?.AsArray().Count == 32 && persistedDoc["errors"]?.AsArray().Count == 32,
            "faults are durable under Schema v4 without a running combat");
        BugRuntimeFaults.InitializePersistence(recovery);
        Check(BugRuntimeFaults.Snapshot().Any(e => e?["from_previous_session"]?.GetValue<bool>() == true && e?["session"]?.GetValue<string>() == "previous"),
            "restart retains earlier faults with explicit session markers");
    }

    private static async Task FingerprintTests(string root)
    {
        BugFileFingerprints.ClearForTests();
        string file = Path.Combine(root, "mod.dll");
        string pck = Path.Combine(root, "STS2_Things.pck");
        File.WriteAllText(file, "version one");
        File.WriteAllText(pck, "packed-resource-bundle");

        BugFileFingerprints.PrimeMany([file, pck]);
        bool flushed = await BugFileFingerprints.FlushPendingAsync(1500);
        Check(flushed, "FlushPendingAsync completes background SHA256 hashing within timeout");

        var first = BugFileFingerprints.Describe(file);
        var pckDesc = BugFileFingerprints.Describe(pck);
        Check(first["state"]!.GetValue<string>() == "complete" && pckDesc["state"]!.GetValue<string>() == "complete",
            "after FlushPendingAsync, DLL and PCK fingerprints are immediately complete (never pending)");
        Check(first["bytes"]!.GetValue<long>() == 11 && !first.ToJsonString().Contains(root), "disk fingerprint contains hash and size, no local path");
        Check(BugFileFingerprints.TryGetSha256(pck)?.Length == 64, "TryGetSha256 returns 64-hex digest after flush");

        File.WriteAllText(file, "version two with changed size");
        BugFileFingerprints.Prime(file);
        Check(await BugFileFingerprints.FlushPendingAsync(1500), "FlushPendingAsync awaits updated file hash");
        var second = BugFileFingerprints.Describe(file);
        Check(second["state"]!.GetValue<string>() == "complete" && first["sha256"]!.GetValue<string>() != second["sha256"]!.GetValue<string>(),
            "fingerprint updates when same installed path changes");
    }
}
