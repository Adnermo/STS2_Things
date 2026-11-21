using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using STS2_Things.Diagnostics;

static partial class Tests
{
    private static async Task SchemaV4Tests(string root)
    {
        // 1. Thorough LogSanitizer privacy scrubbing (Windows/macOS/Linux homes, SteamID, IPv4/IPv6, tokens, Version preservation)
        string dirtyLog = string.Join("\n",
            "Failed loading C:\\Users\\JaneDoe\\AppData\\Roaming\\SlayTheSpire2\\mods\\STS2_Things.dll",
            "macOS path /Users/john_appleseed/Library/Application Support/SlayTheSpire2/save.json",
            "Linux path /home/steamdeck/.local/share/SlayTheSpire2/logs/godot.log",
            "Peer Steam64=76561198012345678 Steam3=[U:1:554433] IP4=203.0.113.42:9001 IP6=2001:0db8:85a3:0000:0000:8a2e:0370:7334",
            "Assembly STS2_Things, Version=2.0.0.0, Culture=neutral",
            "api_key=super_secret_value_123 https://example.com/cb?token=secret_query_tok user@example.org");
        string scrubbed = LogSanitizer.Scrub(dirtyLog, 4000);
        Check(!scrubbed.Contains("JaneDoe") && !scrubbed.Contains("john_appleseed") && !scrubbed.Contains("steamdeck"),
            "all OS user home directory usernames scrubbed");
        Check(scrubbed.Contains("<USER_HOME>/AppData/Roaming/SlayTheSpire2/mods/STS2_Things.dll")
            && scrubbed.Contains("<USER_HOME>/Library/Application Support/SlayTheSpire2/save.json")
            && scrubbed.Contains("<USER_HOME>/.local/share/SlayTheSpire2/logs/godot.log"),
            "relative paths beneath <USER_HOME>/ are preserved cleanly");
        Check(!scrubbed.Contains("76561198012345678") && !scrubbed.Contains("[U:1:554433]") && scrubbed.Contains("<STEAM_ID>"),
            "Steam64 and Steam3 IDs replaced with <STEAM_ID>");
        Check(!scrubbed.Contains("203.0.113.42") && !scrubbed.Contains("2001:0db8:85a3") && scrubbed.Contains("<IP_ADDR>"),
            "IPv4 and IPv6 addresses replaced with <IP_ADDR>");
        Check(scrubbed.Contains("Version=2.0.0.0"), "assembly Version=2.0.0.0 metadata is not mangled by IPv4 scrubber");
        Check(!scrubbed.Contains("super_secret_value_123") && !scrubbed.Contains("secret_query_tok") && !scrubbed.Contains("user@example.org"),
            "API keys, URL tokens, and email addresses scrubbed");

        // 2. Real Exception + InnerException / AggregateException chain (capped at 4) + HarmonyOwnerResolver attribution
        BugRuntimeFaults.ResetForTests();
        BugRuntimeFaults.HarmonyOwnerResolver = frames =>
            frames.Any(f => f.Contains("Tests.", StringComparison.Ordinal)) ? ["adner.sts2.things", "other.mod.harmony"] : [];

        Exception? capturedChain = null;
        try
        {
            ThrowDeepExceptionChain();
        }
        catch (Exception ex)
        {
            capturedChain = ex;
        }
        BugRuntimeFaults.CaptureException(capturedChain!);
        var chainFault = BugRuntimeFaults.Snapshot().Single()!.AsObject();
        Check(chainFault["exception_type"]!.GetValue<string>() == "System.AggregateException", "outer AggregateException type captured");
        Check(chainFault["message"]!.GetValue<string>().Contains("<USER_HOME>/profile.json"), "outer Exception.Message preserved and scrubbed");
        var inners = chainFault["inner_exceptions"]!.AsArray();
        Check(inners.Count == 4, "nested InnerException / AggregateException chain capped at 4 entries");
        Check(inners[0]!["message"]!.GetValue<string>().Contains("Inner #1") && !inners[0]!["message"]!.GetValue<string>().Contains("76561198099999999"),
            "inner exception messages preserved and scrubbed");
        Check(chainFault["harmony_owners"]!.AsArray().Select(n => n!.GetValue<string>()).Contains("adner.sts2.things"),
            "HarmonyOwnerResolver attributes active patch owners on stack frames");

        BugRuntimeFaults.HarmonyOwnerResolver = null;

        // 3. Full Schema v4 payload construction & 100% validation against Worker v2 contract
        string v4Dir = Path.Combine(root, "v4-e2e");
        Directory.CreateDirectory(v4Dir);
        var store = new BugReportStore(v4Dir);
        var meta = new JsonObject
        {
            ["game_version"] = "v0.113.1",
            ["mod_version"] = "2.0.0",
            ["active_mods"] = new JsonArray(new JsonObject { ["id"] = "STS2_Things", ["version"] = "2.0.0" }),
            ["runtime_faults"] = BugRuntimeFaults.Snapshot(),
            ["assemblies"] = new JsonArray(
                BugReportSchemaV4.BuildAssemblyEntry(
                    name: "STS2_Things",
                    version: "2.0.0.0",
                    mvid: Guid.NewGuid().ToString("D"),
                    isDynamic: false,
                    locationKind: "mod_dll",
                    sha256: new string('a', 64),
                    modId: "STS2_Things"),
                BugReportSchemaV4.BuildAssemblyEntry(
                    name: "sts2",
                    version: "0.1.0.0",
                    mvid: Guid.NewGuid().ToString("D"),
                    isDynamic: false,
                    locationKind: "game_dll",
                    sha256: new string('b', 64))),
            ["harmony_patches"] = new JsonArray(
                BugReportSchemaV4.BuildHarmonyPatchEntry(
                    declaringType: "MegaCrit.Sts2.Core.Rooms.AbstractRoom",
                    methodName: "Enter",
                    owners: ["STS2_Things"],
                    prefixes: new JsonArray(new JsonObject
                    {
                        ["owner"] = "STS2_Things",
                        ["assembly"] = "STS2_Things",
                        ["method"] = "STS2_Things.Diagnostics.ThingsReportCombatCheckpointPatch.Prefix",
                        ["priority"] = 400,
                    }))),
            ["modifiers"] = new JsonArray("MODIFIER.THINGS_CROSSROADS"),
            ["config"] = new JsonObject { ["session_active"] = false },
            ["network"] = new JsonObject { ["type"] = "Host", ["connected"] = true, ["player_count"] = 2 },
        };

        store.Begin("v4-run", meta, Snapshot("v4-seed", 68));
        var desync = BugReportSchemaV4.BuildDesyncReport(
            combatRound: 3,
            stateId: 17,
            localChecksum: "0x11223344",
            remoteChecksum: "0x99887766",
            modelCacheHash: "0xabcdef01",
            context: "PlayCardAction CARD.THINGS_COLLISION",
            localStateDump: "Round=3\nPlayer[1].Hp=52\nEnemy[0].Hp=14",
            remoteStateDump: "Round=3\nPlayer[1].Hp=52\nEnemy[0].Hp=19");
        Check((desync["field_diffs"] as JsonArray)?.Count == 1 && (desync["diff_lines"] as JsonArray)?.Count == 1,
            "desync diff compares local vs remote NetFullCombatState dumps");
        store.Current!["desync"] = desync;
        store.Current["checkpoint"] = BugReplayCodec.Encode(
            new JsonObject
            {
                ["schema_version"] = 20,
                ["acts"] = new JsonArray(),
                ["players"] = new JsonArray(new JsonObject { ["net_id"] = 1 }),
                ["rng"] = new JsonObject { ["seed"] = "v4-seed" },
            },
            [1],
            new CheckpointMetadata(EntryKind: "merchant_entry", RoomType: "Shop", ActId: "ACT.THINGS_DEPTHS", ActIndex: 0, Floor: 5, Seed: "v4-seed"));
        store.Current["combat_replay"] = BugReplayCodec.EncodeCombatReplay("REPLAY_BYTES_V4"u8);

        var uploadJournal = store.JournalForUpload();
        byte[] fakeJpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        uploadJournal["screenshot"] = new JsonObject
        {
            ["available"] = true,
            ["format"] = "things-screenshot",
            ["encoding"] = "jpeg-base64",
            ["width"] = 960,
            ["height"] = 540,
            ["bytes"] = fakeJpeg.Length,
            ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fakeJpeg)).ToLowerInvariant(),
            ["data"] = Convert.ToBase64String(fakeJpeg),
        };

        var v4Handler = new Transport { Mode = "success" };
        using var v4Http = new HttpClient(v4Handler);
        var v4Outbox = new BugReportOutbox(v4Dir, v4Http, new Uri("http://127.0.0.1/api/reports"));
        string v4ReportId = v4Outbox.Enqueue(uploadJournal, "联机商店与战斗不同步测试报告");
        string v4WireText = File.ReadAllText(Path.Combine(v4Dir, "pending", v4ReportId + ".json"), Encoding.UTF8);
        if (System.Environment.GetEnvironmentVariable("STS2_THINGS_DUMP_SAMPLE_V4_JSON") is { Length: > 0 } sampleOutPath)
        {
            File.WriteAllText(sampleOutPath, v4WireText, new UTF8Encoding(false));
        }
        var v4WireJson = JsonNode.Parse(v4WireText)!.AsObject();

        Check(BugReportSchemaV4.ValidateReportPayload(v4WireJson, out string? fullValidationErr),
            $"complete Schema v4 payload satisfies 100% of validation.js rules ({fullValidationErr})");
        Check((await v4Outbox.SendAsync(v4ReportId)).Success, "complete Schema v4 report uploads cleanly");

        // 4. 5-Minute Duplicate F2 Submission Guard
        var guard = new BugSubmissionGuard(TimeSpan.FromMinutes(5));
        DateTimeOffset t0 = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        Check(!guard.ShouldThrottleDuplicate(uploadJournal, t0, out _), "initial F2 submission is allowed");
        guard.RecordSubmitted(uploadJournal, "11111111-2222-3333-4444-555555555555", t0);

        Check(guard.ShouldThrottleDuplicate(uploadJournal, t0.AddMinutes(2), out string? priorId)
            && priorId == "11111111-2222-3333-4444-555555555555",
            "identical room+fault state within 5 minutes is throttled with prior report ID");

        string localBundleId = v4Outbox.SaveLocalOnly(uploadJournal, "重复点击 F2", "duplicate_throttled_5m", priorId);
        Check(v4Outbox.PendingCount == 0 && File.Exists(Path.Combine(v4Dir, "reports", localBundleId + ".local.json")),
            "throttled duplicate saves local diagnostic bundle without queuing network spam");

        var updatedFaultJournal = (JsonObject)uploadJournal.DeepClone();
        updatedFaultJournal["runtime_faults"]![0]!["count"] = 2;
        updatedFaultJournal["runtime_faults"]![0]!["occurrences"] = 2;
        Check(!guard.ShouldThrottleDuplicate(updatedFaultJournal, t0.AddMinutes(2).AddSeconds(5), out _),
            "fault count increment immediately overrides 5-minute duplicate guard");

        var movedRoomJournal = (JsonObject)uploadJournal.DeepClone();
        movedRoomJournal["state"]!["current_floor"] = 4;
        Check(!guard.ShouldThrottleDuplicate(movedRoomJournal, t0.AddMinutes(3), out _),
            "changing floor/room immediately allows new F2 submission");

        Check(!guard.ShouldThrottleDuplicate(uploadJournal, t0.AddMinutes(5).AddSeconds(1), out _),
            "identical state is allowed again after 5-minute cooldown expires");

        // 5. StartupNoticePolicy ("2.0" ack & headless/TestMode/NonInteractive/MCP/env suppression)
        string ackFile = Path.Combine(v4Dir, "STS2_Things", "diagnostics", "notice-ack.json");
        Check(!StartupNoticePolicy.IsAcknowledged(ackFile, "2.0"), "unacknowledged install shows 2.0 notice");
        StartupNoticePolicy.RecordAcknowledged(ackFile, "1.0");
        Check(!StartupNoticePolicy.IsAcknowledged(ackFile, "2.0"), "1.0 ack does not suppress 2.0 notice");
        StartupNoticePolicy.RecordAcknowledged(ackFile, "2.0");
        Check(StartupNoticePolicy.IsAcknowledged(ackFile, "2.0"), "2.0 ack persists in notice-ack.json");

        Check(StartupNoticePolicy.ShouldSuppressEnvironment("headless", false, false, false, null), "headless DisplayServer suppresses notice");
        Check(StartupNoticePolicy.ShouldSuppressEnvironment("windows", true, false, false, null), "TestMode suppresses notice");
        Check(StartupNoticePolicy.ShouldSuppressEnvironment("windows", false, true, false, null), "NonInteractiveMode suppresses notice");
        Check(StartupNoticePolicy.ShouldSuppressEnvironment("windows", false, false, true, null), "STS2_MCP_Bridge suppresses notice");
        Check(StartupNoticePolicy.ShouldSuppressEnvironment("windows", false, false, false, "1"), "STS2_THINGS_SUPPRESS_NOTICE=1 suppresses notice");
        Check(!StartupNoticePolicy.ShouldSuppressEnvironment("windows", false, false, false, "0"), "normal interactive desktop session allows notice");
    }

    private static void ThrowDeepExceptionChain()
    {
        var leaf5 = new IOException("Inner #5 deepest error");
        var leaf4 = new InvalidDataException("Inner #4 bad stream at /home/alice/secret.bin", leaf5);
        var leaf3 = new ArgumentException("Inner #3 invalid card", leaf4);
        var leaf2 = new NullReferenceException("Inner #2 null creature", leaf3);
        var branchA = new InvalidOperationException("Inner #1 peer 76561198099999999 failed", leaf2);
        throw new AggregateException("Outer failure reading C:\\Users\\Developer\\profile.json", branchA);
    }
}
