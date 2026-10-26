using System.Text.Json.Nodes;
using STS2_Things.Diagnostics;

static partial class Tests
{
    private static void FaultTests(string root)
    {
        string text = "System.AggregateException: private\n---> System.NullReferenceException: private\n"
            + " [0] void ExampleSkin.Orb.UpdateVisuals() in C:\\PrivatePlayer\\skin.cs:line 9\n"
            + "Authorization: Bearer PRIVATE_TEST_CREDENTIAL\n76561199999999999";
        BugRuntimeFaults.Capture(text, 0);
        var fault = BugRuntimeFaults.Snapshot().Last()!;
        string captured = fault.ToJsonString();
        Check(captured.Contains("ExampleSkin.Orb.UpdateVisuals"), "Godot numbered backtrace is retained");
        Check(!captured.Contains("PrivatePlayer") && !captured.Contains("PRIVATE_TEST_CREDENTIAL") && !captured.Contains("76561199999999999"), "native trace never includes paths or credentials");
        Check(fault["exception_types"]!.AsArray().Count == 2, "nested exception types preserved");
        string log = Path.Combine(root, "startup.log");
        File.WriteAllText(log, "INFO: private startup\nERROR: Index p_index out of bounds\n [0] void Example.Loader.Import() in C:\\PrivatePlayer\\file.cs:line 1\n[INFO] end\n");
        BugRuntimeFaults.BackfillStartupLog(log);
        Check(BugRuntimeFaults.Snapshot().Last()!["code"]!.GetValue<string>() == "index_out_of_bounds", "pre-initializer engine error backfilled");
        Check(BugRuntimeFaults.Snapshot().Last()!.ToJsonString().Contains("Example.Loader.Import"), "startup backfill retains actual failing method");
        for (int i = 0; i < 1000; i++) BugRuntimeFaults.Capture("same repeating error", 0, ["Example.RepeatingError"]);
        var repeated = BugRuntimeFaults.Snapshot().Last()!;
        Check(repeated["occurrences"]?.GetValue<int>() == 1000, "repeated errors keep a count without evicting unrelated faults");
        Check(BugRuntimeFaults.Snapshot().Count == 3, "one repeated stack uses one bounded error record");
        for (int i = 0; i < 40; i++) BugRuntimeFaults.Capture("runtime error " + i, 0, ["Example.BoundedError"]);
        Check(BugRuntimeFaults.Snapshot().Count == 32 && BugRuntimeFaults.Dropped > 0, "error storm is bounded and drops are declared");
        string recovery = Path.Combine(root,"runtime-errors.json");
        BugRuntimeFaults.InitializePersistence(recovery);BugRuntimeFaults.Persist();
        Check(BugReportStore.ReadObject(recovery)?["errors"]?.AsArray().Count==32,"faults are durable without a running combat");
        BugRuntimeFaults.InitializePersistence(recovery);
        Check(BugRuntimeFaults.Snapshot().Any(e=>e?["from_previous_session"]?.GetValue<bool>()==true),"restart retains earlier faults with an explicit session marker");
    }

    private static async Task FingerprintTests(string root)
    {
        string file = Path.Combine(root, "mod.dll"); File.WriteAllText(file,"version one");
        async Task<JsonObject> Ready()
        {
            for (int i = 0; i < 200; i++)
            {
                var value = BugFileFingerprints.Describe(file);
                if (value["state"]!.GetValue<string>() == "complete") return value;
                await Task.Delay(10);
            }
            throw new Exception("fingerprint timeout");
        }
        var first = await Ready();
        Check(first["bytes"]!.GetValue<long>() == 11 && !first.ToJsonString().Contains(root), "disk fingerprint contains hash and size, no local path");
        File.WriteAllText(file,"version two with changed size");
        var second = await Ready();
        Check(first["sha256"]!.GetValue<string>() != second["sha256"]!.GetValue<string>(), "fingerprint updates when same installed path changes");
    }
}
