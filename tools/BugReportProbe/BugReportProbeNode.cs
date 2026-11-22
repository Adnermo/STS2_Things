using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Cards;
using STS2_Things.Diagnostics;

public partial class BugReportProbeNode : Node
{
    private static int _checks;
    private static readonly Assembly ModAssembly = typeof(NThingsStartupNotice).Assembly;
    private static readonly Type Recorder = ModAssembly.GetType("STS2_Things.Diagnostics.BugRunRecorder")!;
    private static string DirectoryPath => Path.Combine(OS.GetUserDataDir(), "mod_data/STS2_Things/bug_reports");

    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        _ = Run();
    }

    private static object? Invoke(string method, params object?[] args) =>
        Recorder.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static JsonObject Current()
    {
        // Reading the durable artifact is an explicit barrier in this probe.
        // Production event callbacks deliberately do not force per-event IO.
        Invoke("DrainRuntimeErrors");
        Recorder.GetMethod("FlushJournal", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, [true]);
        return JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "current-run.json")))!.AsObject();
    }

    private static void Check(bool test, string name)
    {
        if (!test) throw new Exception(name);
        _checks++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProbePatchedFaultTarget()
    {
        var inner = new ArgumentException("Inner failure from 10.20.30.40:7777");
        throw new InvalidOperationException(
            "Outer failure in C:\\Users\\ProbeSecret\\SavedGames\\run.json for 76561198011111111",
            inner);
    }

    private static void ProbeHarmonyNoopPrefix() { }

    private async Task Run()
    {
        try
        {
            TestMode.TurnOnInternal();
            Check(OS.GetUserDataDir().Contains("ReportTest"), "probe uses independent user directory");
            ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK")
                ?? "D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck");
            InitializeModelDb();
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();

            if (OS.GetCmdlineUserArgs().Contains("--performance"))
            {
                await VerifyRecorderPerformance();
                GetTree().Quit();
                return;
            }

            // Startup notice Schema v4 / 2.0 assertions
            Check(NThingsStartupNotice.MajorVersion == "2.0"
                && NThingsStartupNotice.NoticeAckVirtualPath == "user://STS2_Things/diagnostics/notice-ack.json",
                "startup notice uses 2.0 major-version acknowledgment path");
            bool suppressed = (bool)typeof(NThingsStartupNotice)
                .GetMethod("IsSuppressed", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            Check(suppressed, "startup notice automatically suppressed under TestMode/headless");

            var first = await Scenario("report-first");
            Invoke("Initialize");
            var initial = Current();
            string firstId = initial["client_run_id"]!.GetValue<string>();
            Check(initial["schema"]!.GetValue<int>() == 4, "Schema v4 diagnostic journal");
            Check(initial["game_code_mvid"]!.GetValue<string>().Length == 32, "actual game code identity recorded");
            Check(initial["state"]!["local_player_index"]!.GetValue<int>() == 0, "local identity is an anonymous player slot");
            Check(initial["state"]!["rng"]?["rngs"] is JsonObject || initial["state"]!["rng"]?["counters"] is JsonObject, "full current RNG streams recorded");

            foreach (var act in first.Run.Acts) act.GenerateRooms(first.Run.Rng.UpFront, first.Run.UnlockState, false);
            var replay = ModAssembly.GetType("STS2_Things.Diagnostics.BugReplayCapture")!;
            var captureRoom = replay.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)!;
            captureRoom.Invoke(null, [first.Run.CurrentRoom]);

            var checkpoint = Current()["checkpoint"]!.AsObject();
            Check(checkpoint["available"]!.GetValue<bool>(), "native pre-combat checkpoint can be captured");
            Check(checkpoint["schema"]!.GetValue<int>() == 2 && checkpoint["schema_version"]!.GetValue<int>() == 2, "checkpoint uses Schema 2");
            Check(checkpoint["entry_kind"]?.GetValue<string>() == "combat_entry"
                && checkpoint["room_type"]?.GetValue<string>() == "Monster"
                && checkpoint["encounter_id"]?.GetValue<string>() == "ENCOUNTER.SLIMES_WEAK",
                "Checkpoint Schema 2 records combat room metadata");

            using (var zipped = new MemoryStream(Convert.FromBase64String(checkpoint["data"]!.GetValue<string>())))
            using (var gzip = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                string native = reader.ReadToEnd();
                var save = JsonNode.Parse(native)!;
                Check(save["rng"]!["seed"]!.GetValue<string>() == "report-first", "checkpoint preserves original seed");
                Check(save["players"]![0]!["net_id"]!.GetValue<ulong>() == 1, "checkpoint contains portable local identity");
                Check(save["rng"]?["rngs"] is JsonObject || save["rng"]?["counters"] is JsonObject, "checkpoint contains native RNG state");
                var parsed = System.Text.Json.JsonSerializer.Deserialize(native, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
                var restored = RunState.FromSerializable(parsed);
                var restoredRng = System.Text.Json.JsonSerializer.SerializeToNode(
                    restored.Rng.ToSerializable(),
                    JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet>());
                Check(JsonNode.DeepEquals(restoredRng, save["rng"]), "native load restores full RNG bits and counters exactly");
                Check(restored.Players[0].Deck.Cards.Count == first.Player.Deck.Cards.Count, "native load restores actual deck");
            }

            // Universal pre-entry room checkpoints (non-combat Event & Merchant rooms)
            var sampleEvent = ModelDb.AllEvents.First(e => e is not AncientEventModel);
            captureRoom.Invoke(null, [new EventRoom(sampleEvent)]);
            var eventCp = Current()["checkpoint"]!.AsObject();
            Check(eventCp["available"]!.GetValue<bool>()
                && eventCp["schema"]!.GetValue<int>() == 2
                && eventCp["entry_kind"]?.GetValue<string>() == "event_entry"
                && eventCp["room_type"]?.GetValue<string>() == "Event"
                && eventCp["event_id"]?.GetValue<string>() == sampleEvent.Id.ToString(),
                "Checkpoint Schema 2 captures non-combat EventRoom pre-entry save");

            captureRoom.Invoke(null, [new MerchantRoom()]);
            var merchantCp = Current()["checkpoint"]!.AsObject();
            Check(merchantCp["available"]!.GetValue<bool>()
                && merchantCp["schema"]!.GetValue<int>() == 2
                && merchantCp["entry_kind"]?.GetValue<string>() == "merchant_entry"
                && merchantCp["room_type"]?.GetValue<string>() == "Shop",
                "Checkpoint Schema 2 captures non-combat MerchantRoom pre-entry save safely");

            // Native anonymized CombatReplay encoding
            var sampleReplay = new CombatReplay
            {
                version = "v2.0.0-probe",
                gitCommit = "probe",
                modelIdHash = 0x1234abcd,
                serializableRun = RunManager.Instance.ToSave(null),
            };
            var encodedReplay = (JsonObject)replay.GetMethod("EncodeCombatReplayInstance", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [sampleReplay])!;
            Check(encodedReplay["available"]?.GetValue<bool>() == true
                && encodedReplay["format"]?.GetValue<string>() == "sts2-combat-replay"
                && encodedReplay["sha256"]?.GetValue<string>().Length == 64,
                "native anonymized CombatReplay encodes to sts2-combat-replay envelope");

            var hand = initial["state"]!["players"]![0]!["combat"]!["hand"]!.AsArray();
            Check(hand.Count == 2, "actual model hand captured");
            Check(hand[0]!["instance_id"]!.GetValue<string>() != hand[1]!["instance_id"]!.GetValue<string>(), "same-name cards have distinct IDs");
            Check(hand[0]!["energy"]!.GetValue<int>() == 3, "native temporary cost recorded");
            Check(initial["state"]!["players"]![0]!["powers"]![0]!["amount"]!.GetValue<int>() == 3, "Power amounts recorded");

            var oldExecutor = RunManager.Instance.ActionExecutor;
            var action = new PlayCardAction(first.Player.PlayerCombatState!.Hand.Cards[0], null);
            Invoke("BeforeAction", action);
            Check(Current()["events"]!.AsArray().Any(e => e?["kind"]?.GetValue<string>() == "action_started"), "unfinished actions have a starting event");
            Invoke("BeforeChoice", action);
            Check(Current()["events"]!.AsArray().Any(e => e?["kind"]?.GetValue<string>() == "action_waiting_for_choice"), "waiting choice is retained before completion");

            var second = await Scenario("report-second");
            Invoke("OnStarted", second.Run);
            var current = Current();
            Check(current["client_run_id"]!.GetValue<string>() != firstId, "second run gets a distinct ID without recursive serialization");
            var previous = JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "last-run.json")))!;
            Check(previous["client_run_id"]!.GetValue<string>() == firstId, "previous run is archived correctly");
            var handler = typeof(ActionExecutor).GetField("BeforeActionExecuted", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(oldExecutor) as Delegate;
            Check(handler?.GetInvocationList().All(d => d.Method.DeclaringType != Recorder) != false, "old executor is detached");
            Check(current["initial_state"]!["seed"]!.GetValue<string>() == "report-second", "new initial snapshot belongs to second run");

            // Room/turn startup tasks run outside ActionExecutor. Exercise the actual
            // game's error logger while no action is running, just like a faulted
            // fire-and-forget combat-start task.
            Log.Error("System.MissingMethodException: Method not found: 'UInt32 MegaCrit.Sts2.Core.Runs.RunRngSet.get_Seed()'.\n"
                + "   at ExampleMod.CombatStart.BeforeHandDraw(Player player) in C:\\PrivatePlayer\\Game\\test.cs:line 42\n"
                + "Authorization: Bearer PRIVATE_TEST_CREDENTIAL\nSteamId: 76561199999999999");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Invoke("PollFailedAction");
            var faults = Current()["events"]!.AsArray().Where(e => e?["kind"]?.GetValue<string>() == "runtime_error").ToArray();
            Check(faults.Length == 1, "startup error outside action queue must be recorded");
            string serialized = faults[0]!.ToJsonString();
            Check(serialized.Contains("MissingMethodException") && serialized.Contains("ExampleMod.CombatStart.BeforeHandDraw"), "fault type and useful frames retained");
            Check(!serialized.Contains("PrivatePlayer") && !serialized.Contains("PRIVATE_TEST_CREDENTIAL") && !serialized.Contains("76561199999999999"), "paths, auth and Steam identity excluded");
            Check(faults[0]?["detail"]?["member"]?.GetValue<string>()?.Contains("get_Seed") == true, "missing API signature retained");

            GD.PushError("System.InvalidOperationException: private engine error\n at ExampleSkin.Orb.UpdateVisuals() in C:\\PrivatePlayer\\skin.cs:line 9");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Invoke("PollFailedAction");
            Check(Current()["events"]!.AsArray().Any(e => e?["kind"]?.GetValue<string>() == "runtime_error" && e?["detail"]?["type"]?.GetValue<string>() == "System.InvalidOperationException"), "native Godot errors also recorded");

            try
            {
                await MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Task.FromException(new MissingMethodException("Method not found: 'Example.Mod.Draw()'.")));
            }
            catch (MissingMethodException)
            {
                // Expected probe exception.
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Invoke("PollFailedAction");
            Check(Current()["runtime_faults"]!.AsArray().Any(e => e?["member"]?.GetValue<string>() == "Example.Mod.Draw()"), "real native fire-and-forget task fault reaches runtime_faults");

            // Scrubbed real exception chain + Harmony patch owner attribution + awaited fingerprints + assemblies MVID + desync
            var probeHarmony = new Harmony("things.probe.harmony.owner");
            try
            {
                probeHarmony.Patch(
                    AccessTools.Method(typeof(BugReportProbeNode), nameof(ProbePatchedFaultTarget)),
                    prefix: new HarmonyMethod(typeof(BugReportProbeNode), nameof(ProbeHarmonyNoopPrefix)));

                Exception? capturedEx = null;
                try { ProbePatchedFaultTarget(); }
                catch (Exception ex) { capturedEx = ex; }

                ModAssembly.GetType("STS2_Things.Diagnostics.BugRuntimeFaults")!
                    .GetMethod("CaptureException", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [capturedEx, 0]);
                Invoke("PollFailedAction");

                var runtimeFaults = Current()["runtime_faults"]!.AsArray();
                var chainRecord = runtimeFaults.OfType<JsonObject>().FirstOrDefault(f =>
                    f["message"]?.GetValue<string>()?.Contains("<USER_HOME>/SavedGames/run.json") == true);
                Check(chainRecord is not null, "real Exception message preserved with <USER_HOME>/ scrubbing");
                Check(!chainRecord!.ToJsonString().Contains("ProbeSecret") && !chainRecord.ToJsonString().Contains("76561198011111111"),
                    "user home directory name and SteamID scrubbed from Exception chain");
                Check(chainRecord["inner_exceptions"] is JsonArray inners && inners.Count == 1
                    && inners[0]?["message"]?.GetValue<string>()?.Contains("<IP_ADDR>") == true,
                    "InnerException message preserved with IPv4 scrubbing");
                Check(chainRecord["harmony_owners"] is JsonArray hOwners
                    && hOwners.Select(o => o?.GetValue<string>()).Contains("things.probe.harmony.owner"),
                    "Harmony owner attributed on patched stack frame");

                // LogSanitizer res:// / user:// / https:// URI preservation regression (must not mangle res:// into re<LOCAL_PATH>)
                MethodInfo scrubMethod = ModAssembly.GetType("STS2_Things.Diagnostics.LogSanitizer", throwOnError: true)!
                    .GetMethod("Scrub", BindingFlags.NonPublic | BindingFlags.Static)!;
                string scrubbedRes = (string)scrubMethod.Invoke(null,
                    ["ResourceFormatImporter::load - res://scenes/creature_visuals/soul_roes.tscn:1", 2000])!;
                Check(scrubbedRes.Contains("res://scenes/creature_visuals/soul_roes.tscn:1", StringComparison.Ordinal)
                    && !scrubbedRes.Contains("re<LOCAL_PATH>", StringComparison.Ordinal),
                    "LogSanitizer preserves res:// resource paths without mangling into re<LOCAL_PATH>");
                string scrubbedMixed = (string)scrubMethod.Invoke(null,
                    ["user://mod_configs/STS2_Things.cfg https://example.com/cb?token=secret_tok C:\\Users\\Alice\\foo.txt D:\\Private\\bar.cs E:/secret/baz.txt", 2000])!;
                Check(scrubbedMixed.Contains("user://mod_configs/STS2_Things.cfg", StringComparison.Ordinal)
                    && scrubbedMixed.Contains("https://example.com/cb?token=<REDACTED>", StringComparison.Ordinal)
                    && scrubbedMixed.Contains("<USER_HOME>/foo.txt", StringComparison.Ordinal)
                    && scrubbedMixed.Contains("<LOCAL_PATH>/bar.cs", StringComparison.Ordinal)
                    && scrubbedMixed.Contains("<LOCAL_PATH>/baz.txt", StringComparison.Ordinal)
                    && !scrubbedMixed.Contains("Alice", StringComparison.Ordinal)
                    && !scrubbedMixed.Contains("Private", StringComparison.Ordinal)
                    && !scrubbedMixed.Contains("secret_tok", StringComparison.Ordinal),
                    "LogSanitizer preserves URI schemes while still scrubbing Windows drive paths and tokens");

                // Multiplayer state divergence capture
                var localNetState = NetFullCombatState.FromRun(second.Run, null);
                second.Player.Creature.SetCurrentHpInternal(41);
                var remoteNetState = NetFullCombatState.FromRun(second.Run, null);
                second.Player.Creature.SetCurrentHpInternal(70);
                Recorder.GetMethod("RecordStateDivergence", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [localNetState, remoteNetState, (uint?)9u, "ProbeDivergenceTest", (uint?)0x11112222u, (uint?)0x33334444u]);
                var desyncObj = Current()["desync"]?.AsObject();
                Check(desyncObj?["detected"]?.GetValue<bool>() == true
                    && (desyncObj["field_diffs"] as JsonArray)?.Count > 0,
                    "multiplayer state divergence recorded with field diffs");

                // Awaited CaptureSnapshotAsync (FlushPendingAsync + assemblies MVID + harmony_patches + Schema v4 validation)
                var snapshotTask = (Task<JsonObject>)Recorder.GetMethod("CaptureSnapshotAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [1500])!;
                var snapshotJournal = await snapshotTask;
                Check(snapshotJournal["game_file"]?["state"]?.GetValue<string>() == "complete",
                    "CaptureSnapshotAsync awaits FlushPendingAsync so sts2.dll hash is complete");
                Check(snapshotJournal["things_files"] is JsonArray tf && tf.Count > 0
                    && tf.All(f => f?["fingerprint"]?["state"]?.GetValue<string>() == "complete"),
                    "STS2_Things.dll/pck hashes are never pending in captured snapshot");

                var assemblies = snapshotJournal["assemblies"]!.AsArray();
                var thingsAsm = assemblies.OfType<JsonObject>().FirstOrDefault(a => a["name"]?.GetValue<string>() == "STS2_Things");
                Check(thingsAsm is not null
                    && Guid.TryParseExact(thingsAsm["mvid"]?.GetValue<string>(), "D", out _)
                    && thingsAsm["sha256"]?.GetValue<string>()?.Length == 64,
                    "loaded assemblies record MVID (GUID D) and mod DLL SHA-256");

                var patches = snapshotJournal["harmony_patches"]!.AsArray();
                Check(patches.OfType<JsonObject>().Any(p =>
                    p["owners"] is JsonArray owners && owners.Select(o => o?.GetValue<string>()).Contains("things.probe.harmony.owner")),
                    "harmony_patches lists active Harmony patch targets and owners");

                var schemaValidator = ModAssembly.GetType("STS2_Things.Diagnostics.BugReportSchemaV4")!
                    .GetMethod("ValidateReportPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
                var fullEnvelope = new JsonObject
                {
                    ["schema"] = 4,
                    ["source"] = "sts2_things",
                    ["client_report_id"] = Guid.NewGuid().ToString("N"),
                    ["description"] = "Native probe Schema v4 validation check",
                    ["run"] = snapshotJournal,
                };
                object?[] valArgs = [fullEnvelope, null];
                bool validEnvelope = (bool)schemaValidator.Invoke(null, valArgs)!;
                Check(validEnvelope, $"captured native Schema v4 snapshot passes Worker v2 validation ({valArgs[1]})");
            }
            finally
            {
                probeHarmony.UnpatchAll(probeHarmony.Id);
            }

            using (var image = Image.CreateEmpty(1200, 700, false, Image.Format.Rgb8))
            {
                image.Fill(Colors.CornflowerBlue);
                using var png = new MemoryStream(image.SavePngToBuffer());
                var screenshot = ModAssembly.GetType("STS2_Things.Diagnostics.BugScreenshotCapture")!
                    .GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [png]) as JsonObject;
                Check(screenshot?["available"]?.GetValue<bool>() == true && screenshot["bytes"]!.GetValue<int>() <= 60_000, "native F2 image encodes a bounded screenshot");
                Check(screenshot!["width"]!.GetValue<int>() == 960, "F2 thumbnail preserves aspect ratio within native size limit");
            }

            foreach (string name in new[]
            {
                "ThingsFeedbackCategoryPatch",
                "ThingsFeedbackSelectionPatch",
                "ThingsFeedbackSendPatch",
                "ThingsFeedbackReceiptPatch",
                "ThingsFeedbackFailurePatch",
                "ThingsReportCombatCheckpointPatch",
                "ThingsReportPreRoomSavePatch",
                "ThingsStartupNoticePatch",
            })
            {
                Check(ModAssembly.GetType("STS2_Things.Diagnostics." + name) is not null, "shipping diagnostic patch exists: " + name);
            }

            var send = typeof(MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NSendFeedbackScreen)
                .GetMethod("SendFeedback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
#if STS2_V111_1
            Check(send.GetParameters().Length == 3, "v111.1 native F2 signature includes both owned streams");
#else
            Check(send.GetParameters().Length == 5 && send.GetParameters()[1].ParameterType == typeof(byte[]),
                "v113.1 native F2 signature carries screenshot bytes and owned FeedbackLogFiles");
#endif
            // Verify ThingsFeedbackSelectionPatch handles both v111.1 (NMegaTextEdit : TextEdit) and v113.1 (NMegaTextEdit : Control wrapper) without InvalidCastException.
            {
                var screen = new MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NSendFeedbackScreen();
                var dropdown = new MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NFeedbackCategoryDropdown();
                var megaInput = new MegaCrit.Sts2.Core.Nodes.GodotExtensions.NMegaTextEdit();
                TextEdit innerTextEdit;
#if STS2_V111_1
                innerTextEdit = megaInput;
#else
                innerTextEdit = new TextEdit { Name = "TextEdit" };
                megaInput.AddChild(innerTextEdit);
                megaInput.TextEdit = innerTextEdit;
#endif
                innerTextEdit.PlaceholderText = "ORIGINAL_PLACEHOLDER";
                bool textChangedEmitted = false;
                innerTextEdit.Connect(TextEdit.SignalName.TextChanged, Callable.From(() => textChangedEmitted = true));

                screen.AddChild(megaInput);
                screen.AddChild(dropdown);
                typeof(MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NSendFeedbackScreen)
                    .GetField("_descriptionInput", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(screen, megaInput);
                typeof(MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NFeedbackCategoryDropdown)
                    .GetField("_categories", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dropdown, new[] { "bug", "things_bug", "things_bug_retry" });
                var currentIdxField = typeof(MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NFeedbackCategoryDropdown)
                    .GetField("_currentCategoryIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var postfix = ModAssembly.GetType("STS2_Things.Diagnostics.ThingsFeedbackSelectionPatch")!
                    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!;

                currentIdxField.SetValue(dropdown, 1); // things_bug
                postfix.Invoke(null, [dropdown]);
                Check(innerTextEdit.PlaceholderText != "ORIGINAL_PLACEHOLDER", "selecting things_bug updates feedback placeholder without cast error");

                currentIdxField.SetValue(dropdown, 0); // bug
                postfix.Invoke(null, [dropdown]);
                Check(innerTextEdit.PlaceholderText == "ORIGINAL_PLACEHOLDER", "switching back to vanilla category restores original placeholder");

                currentIdxField.SetValue(dropdown, 2); // things_bug_retry
                postfix.Invoke(null, [dropdown]);
                Check(innerTextEdit.Text == "Retry pending Things reports" && textChangedEmitted,
                    "selecting things_bug_retry populates text and emits TextChanged on inner TextEdit");

                screen.Free();
            }
            Invoke("DetachExecutor");
            DeactivateSyntheticCombat();
            GD.Print($"BugReport native probe: PASS ({_checks} assertions)");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            GetTree().Quit(1);
        }
    }

    private static async Task<(RunState Run, Player Player)> Scenario(string seed)
    {
        DeactivateSyntheticCombat();
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = ProbeRuntimeApi.CreateForNewRun(
            [player],
            ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(),
            PerformanceModifiers(),
            GameMode.Standard,
            false,
            0,
            seed);
        RunManager.Instance.PlayerChoiceSynchronizer?.Dispose();
        RunManager.Instance.RewardsSetSynchronizer?.Dispose();
        var netService = new NetSingleplayerGameService();
        var locationBuffer = new RunLocationTargetedMessageBuffer(netService);
        typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, netService);
        typeof(RunManager).GetProperty("RunLocationTargetedBuffer")!.SetValue(RunManager.Instance, locationBuffer);
        typeof(RunManager).GetProperty("PlayerChoiceSynchronizer")!.SetValue(RunManager.Instance, new PlayerChoiceSynchronizer(netService, run));
        typeof(RunManager).GetProperty("RewardsSetSynchronizer")!.SetValue(RunManager.Instance, new RewardsSetSynchronizer(locationBuffer, netService, run, netService.NetId));
        typeof(RunManager).GetProperty("ActionExecutor")!.SetValue(RunManager.Instance, new ActionExecutor(new ActionQueueSet([player])));
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(
            RunManager.Instance,
            typeof(AscensionManager).GetConstructor([typeof(int)]) is { } legacyAscension
                ? legacyAscension.Invoke([0])
                : typeof(AscensionManager).GetConstructor([typeof(GameMode), typeof(int)])!.Invoke([GameMode.Standard, 0]));
        foreach (var modifier in run.Modifiers) modifier.OnRunCreated(run);
        var encounter = ModelDb.AllEncounters.First(e => e.Id.Entry == "SLIMES_WEAK").ToMutable();
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);
        room.CombatState.AddPlayer(player);
        player.ResetCombatState();
        player.Creature.SetMaxHpInternal(70);
        player.Creature.SetCurrentHpInternal(70);
        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            var creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(creature);
            monster.SetUpForCombat();
            monster.RollMove([player.Creature]);
        }
        room.CombatState.CurrentSide = CombatSide.Player;
        ActivateSyntheticCombat(room.CombatState);
        var card = room.CombatState.CreateCard<ThingsLeechParasite>(player);
        card.EnergyCost.SetUntilPlayed(3);
        player.PlayerCombatState!.Hand.AddInternal(card, silent: true);
        player.PlayerCombatState.Hand.AddInternal(room.CombatState.CreateCard<ThingsLeechParasite>(player), silent: true);
        player.PlayerCombatState.Energy = 3;
        player.PlayerCombatState.Phase = PlayerTurnPhase.Play;
        NetCombatCardDb.Instance.StartCombat([player]);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), player.Creature, 3, player.Creature, null, silent: true);
        return (run, player);
    }
}
