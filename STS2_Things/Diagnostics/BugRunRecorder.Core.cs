using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_Things.Diagnostics;

/// <summary>Main-thread read-only journal. Only explicit F2 submissions perform network I/O.</summary>
internal static class BugRunRecorder
{
    private static readonly System.Net.Http.HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(18) };
    private static readonly Uri Endpoint = new("https://reports.adnermo.online/api/reports");
    private static BugReportStore? _store;
    private static BugReportOutbox? _outbox;
    private static RunState? _run;
    private static ActionExecutor? _executor;
    private static readonly HashSet<GameAction> TrackedActions = [];
    private static bool _ready;
    private static long _lastErrorPoll;
    private static long _lastSnapshotAt;
    private static bool _snapshotDirty, _journalDirty;
    private const int SnapshotIntervalMs = 500;
    private static GameAction? _reportedErrorAction;
    private static long _combatCheckpointAt;
    private static bool _startupWaitRecorded;
    private static BugEngineLogger? _engineLogger;
    [ThreadStatic] private static bool _drainingRuntimeErrors;
    internal static ReportSubmission? LastSubmission { get; private set; }
    private static string DataDir => Path.Combine(OS.GetUserDataDir(), "mod_data", "STS2_Things", "bug_reports");
    private static BugReportStore Store => _store ??= new(DataDir);
    private static BugReportOutbox Outbox => _outbox ??= new(DataDir, Client, ReportEndpoint());

    private static Uri ReportEndpoint()
    {
        string? value = System.Environment.GetEnvironmentVariable("STS2_THINGS_REPORT_TEST_URL");
        if (OS.GetUserDataDir().Contains("ReportTest", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "http" && uri.Host == "127.0.0.1") return uri;
        return Endpoint;
    }

    internal static void Initialize()
    {
        if (_ready) return;
        RunManager.Instance.RunStarted += OnStarted;
        RunManager.Instance.RoomEntered += () => Track("room_entered", persist: true);
#if STS2_V111
        CombatManager.Instance.CombatBegan += _ => Track("combat_started", persist: true);
#endif
        CombatManager.Instance.TurnStarted += _ => Track("turn_started", persist: true);
        CombatManager.Instance.CombatEnded += _ => Track("combat_ended", persist: true);
        // Start-of-room/turn fire-and-forget failures do not belong to ActionExecutor.
        // Use the original logging callback; do not catch, replace or suppress tasks.
        BugRuntimeFaults.InitializePersistence(Path.Combine(DataDir, "runtime-errors.json"));
        Log.LogCallback += OnNativeLog;
        _engineLogger = new BugEngineLogger();
        OS.AddLogger(_engineLogger);
        // A few native import errors occur before our initializer can subscribe.
        string[] args = OS.GetCmdlineArgs();
        int logArg = Array.IndexOf(args, "--log-file");
        string startupLog = logArg >= 0 && logArg + 1 < args.Length ? args[logArg + 1]
            : args.FirstOrDefault(arg => arg.StartsWith("--log-file=", StringComparison.Ordinal))?[11..]
                ?? Path.Combine(OS.GetUserDataDir(), "logs", "godot.log");
        BugRuntimeFaults.BackfillStartupLog(startupLog);
        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        {
            try
            {
                if (failure.ExceptionObject is Exception exception) BugRuntimeFaults.Capture(exception.ToString(), 0);
                BugRuntimeFaults.Persist();
            }
            catch (Exception) { }
        };
        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame += PollFailedAction;
        _ready = true;
        var current = RunManager.Instance.DebugOnlyGetState();
        if (current is not null) OnStarted(current);
        Log.Info("[Things BugReport] Local journal enabled. Explicit F2 submissions only.");
    }

    private static string RunKey(RunState run)
    {
        long started = (long)(AccessTools.Field(typeof(RunManager), "_startTime").GetValue(RunManager.Instance) ?? 0L);
        int profile;
        try { profile = SaveManager.Instance.CurrentProfileId; } catch (InvalidOperationException) { profile = 0; }
        string input = profile + "|" + started + "|" + run.Rng.StringSeed + "|" + run.GameMode + "|" + run.AscensionLevel
            + "|" + string.Join(',', run.Players.Select(p => p.Character.Id.ToString()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static void OnStarted(RunState run)
    {
        try
        {
            if (ReferenceEquals(_run, run)) return;
            if (_run is not null) Store.Archive(); // Pure frozen serialization; no live state reads.
            DetachExecutor();
            _reportedErrorAction = null;
            _combatCheckpointAt = 0; _startupWaitRecorded = false;
            var metadata = RuntimeMetadata();
            bool resumed = Store.Begin(RunKey(run), metadata, BugSnapshot.Capture(run));
            _run = run;
            _executor = RunManager.Instance.ActionExecutor;
            if (_executor is not null)
            {
                _executor.BeforeActionExecuted += BeforeAction;
                _executor.AfterActionExecuted += AfterAction;
            }
            Track(resumed ? "run_resumed" : "run_first_observed", persist: true);
            FlushJournal(force: true);
        }
        catch (Exception ex) { Log.Warn("[Things BugReport] Start journal: " + ex.GetType().Name); }
    }

    private static JsonObject RuntimeMetadata()
    {
        var metadata = BugSnapshot.Object(new {
                game_version = GameVersion(),
                game_code_mvid = typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
                rng_seed_type = typeof(RunRngSet).GetProperty("Seed")?.PropertyType.FullName,
                mod_version = typeof(BugRunRecorder).Assembly.GetName().Version?.ToString() ?? "unknown",
                active_mods = ModManager.GetLoadedMods().Select((m, index) => new {
                    id = m.manifest?.id ?? "unknown", version = m.version?.ToString() ?? "unknown",
                    name = m.manifest?.name,
                    workshop_id = ModWorkshopId(m),
                    load_index = index, source = m.modSource.ToString(),
                    code = ModCode(m)
                }).ToArray()
            });
        metadata["runtime_errors"] = BugRuntimeFaults.Snapshot();
        metadata["game_file"] = BugFileFingerprints.Describe(typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly.Location);
        metadata["mod_files"] = ModFiles();
        // Only our fixed boolean/integer gameplay settings; never arbitrary mod config files.
        metadata["things_config"] = BugSnapshot.Object(new {
            session_active = Config.MultiplayerConfig.IsActive, session_locked = Config.MultiplayerConfig.IsLocked,
            local = Config.ThingsModConfig.Entries.ToDictionary(e => e.Key, e => Config.ThingsModConfig.GetLocalValue(e.Key)),
            effective = Config.ThingsModConfig.Entries.ToDictionary(e => e.Key,
                e => Config.MultiplayerConfig.GetOverride(e.Key) ?? Config.ThingsModConfig.GetLocalValue(e.Key))
        });
        return metadata;
    }

    internal static void RecordCheckpoint(JsonObject checkpoint)
    {
        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null) return;
            if (!ReferenceEquals(_run, run)) OnStarted(run);
            if (Store.Current is not null)
            {
                Store.Current["checkpoint"] = checkpoint.DeepClone();
                _combatCheckpointAt = System.Environment.TickCount64;
                _startupWaitRecorded = false;
                Store.Track(new JsonObject { ["kind"] = "combat_checkpoint", ["at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["detail"] = new JsonObject { ["available"] = checkpoint["available"]?.DeepClone(),
                        ["encounter"] = checkpoint["encounter"]?.DeepClone() } });
                _journalDirty = true;
                FlushJournal(force: true);
            }
        }
        catch (Exception) { /* Checkpoint observation must never interrupt room entry. */ }
    }

    private static string? ModWorkshopId(Mod mod)
    {
#if STS2_V111
        if (mod.workshopId is ulong id) return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
        // 107.1 has no workshopId field. Infer only the public item ID from its
        // original Steam cache layout; the local path itself is never reported.
        if (mod.modSource != ModSource.SteamWorkshop) return null;
        var directory = new DirectoryInfo(mod.path);
        for (; directory.Parent is not null; directory = directory.Parent)
        {
            if (directory.Parent.Name == "2868840" && directory.Parent.Parent?.Name == "content"
                && ulong.TryParse(directory.Name, out ulong item) && item != 0)
                return item.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static object[] ModCode(Mod mod)
    {
#if STS2_V111
        var assemblies = mod.assemblies;
#else
        var assemblies = mod.assembly is { } assembly ? new[] { assembly } : [];
#endif
        return assemblies.Where(a => !a.IsDynamic).Select(a => (object)new {
            name = a.GetName().Name,
            mvid = a.ManifestModule.ModuleVersionId.ToString("N"),
            version = a.GetName().Version?.ToString()
        }).ToArray();
    }

    private static JsonArray ModFiles()
    {
        var result = new JsonArray();
        foreach (var mod in ModManager.GetLoadedMods())
        {
            var files = new JsonArray();
            try
            {
                if (!Directory.Exists(mod.path)) continue;
                foreach (string path in Directory.EnumerateFiles(mod.path).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".dll" or ".pck").Order(StringComparer.OrdinalIgnoreCase).Take(20))
                    files.Add(new JsonObject { ["file"] = Path.GetFileName(path), ["fingerprint"] = BugFileFingerprints.Describe(path) });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            result.Add(new JsonObject { ["id"] = mod.manifest?.id, ["files"] = files });
        }
        return result;
    }

    private static string GameVersion()
    {
        try { return MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "unknown"; }
        catch (Exception) { return "unknown"; }
    }

    private static void DetachExecutor()
    {
        if (_executor is not null)
        {
            _executor.BeforeActionExecuted -= BeforeAction;
            _executor.AfterActionExecuted -= AfterAction;
        }
        foreach (var action in TrackedActions) DetachAction(action);
        TrackedActions.Clear(); _executor = null;
    }

    private static void DetachAction(GameAction action)
    {
        action.BeforePausedForPlayerChoice -= BeforeChoice;
        action.BeforeResumedAfterPlayerChoice -= AfterChoice;
        action.BeforeCancelled -= Cancelled;
    }

    private static JsonObject ActionRecord(GameAction action) => BugSnapshot.Object(new {
        action_id = action.Id, action_type = action.GetType().Name, action_state = action.State.ToString(),
        card = action is PlayCardAction card ? card.CardModelId.ToString() : null,
        target = action is PlayCardAction targetCard ? targetCard.TargetId : action is UsePotionAction potion ? potion.TargetId : null,
        potion_slot = action is UsePotionAction use ? (int?)use.PotionIndex : null,
        player_index = _run?.Players.ToList().FindIndex(p => p.NetId == action.OwnerId), error = BugSnapshot.Error(action.Exception)
    });

    private static void BeforeAction(GameAction action)
    {
        if (TrackedActions.Add(action))
        {
            action.BeforePausedForPlayerChoice += BeforeChoice;
            action.BeforeResumedAfterPlayerChoice += AfterChoice;
            action.BeforeCancelled += Cancelled;
        }
        Track("action_started", ActionRecord(action), persist: true);
    }
    private static void BeforeChoice(GameAction action) => Track("action_waiting_for_choice", ActionRecord(action), persist: true);
    private static void AfterChoice(GameAction action) => Track("action_resumed", ActionRecord(action), persist: true);
    private static void Cancelled(GameAction action)
    {
        Track("action_cancelled", ActionRecord(action), persist: true);
        DetachAction(action); TrackedActions.Remove(action);
    }
    private static void AfterAction(GameAction action)
    {
        Track("action_completed", ActionRecord(action), persist: true);
        DetachAction(action); TrackedActions.Remove(action);
    }

    private static void PollFailedAction()
    {
        if (System.Environment.TickCount64 - _lastErrorPoll < 250) return;
        _lastErrorPoll = System.Environment.TickCount64;
        DrainRuntimeErrors();
        if (RunManager.Instance.DebugOnlyGetState() is null && _run is not null)
        {
            try { Store.Archive(); } catch (IOException) { }
            DetachExecutor(); _run = null; return;
        }
        var action = _executor?.CurrentlyRunningAction;
        var currentRun = RunManager.Instance.DebugOnlyGetState();
        if (!_startupWaitRecorded && _combatCheckpointAt > 0
            && System.Environment.TickCount64 - _combatCheckpointAt > 15_000
            && currentRun?.CurrentRoom is MegaCrit.Sts2.Core.Rooms.CombatRoom
            && currentRun.Players.Any(p => p.PlayerCombatState?.Phase.ToString() == "Start"))
        {
            _startupWaitRecorded = true;
            Track("combat_start_wait", new JsonObject { ["elapsed_ms"] = System.Environment.TickCount64 - _combatCheckpointAt,
                ["note"] = "observation_only_not_a_bug_verdict" }, persist: true);
        }
        if (action?.Exception is not null && !ReferenceEquals(action, _reportedErrorAction))
        {
            _reportedErrorAction = action; Track("action_failed", ActionRecord(action), persist: true);
        }
        try { FlushJournal(); }
        catch (Exception ex) { Log.Warn("[Things BugReport] Journal snapshot: " + ex.GetType().Name); }
    }

    private static void OnNativeLog(LogLevel level, string text, int skipFrames)
    {
        if (_drainingRuntimeErrors || level != LogLevel.Error) return;
        try { BugRuntimeFaults.Capture(text, skipFrames); }
        catch (Exception) { /* Diagnostics must never make a native error fatal. */ }
    }

    private static void DrainRuntimeErrors()
    {
        _drainingRuntimeErrors = true;
        try
        {
            for (int i = 0; i < 32 && BugRuntimeFaults.TryTake(out var detail); i++)
                if (RunManager.Instance.DebugOnlyGetState() is not null)
                    Track("runtime_error", detail, persist: true);
        }
        finally { _drainingRuntimeErrors = false; }
    }

    private static void Track(string kind, JsonObject? detail = null, bool persist = false)
    {
        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null) return;
            if (!ReferenceEquals(_run, run)) OnStarted(run);
            if (!ReferenceEquals(_run, run)) return;
            Store.Track(new JsonObject {
                ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["kind"] = kind, ["detail"] = detail?.DeepClone(),
                ["act_index"] = run.CurrentActIndex, ["floor"] = run.ActFloor, ["room"] = run.CurrentRoom?.GetType().Name
            });
            _snapshotDirty = true;
            _journalDirty |= persist;
        }
        catch (Exception ex) { Log.Warn("[Things BugReport] Journal event: " + ex.GetType().Name); }
    }

    private static void FlushJournal(bool force = false)
    {
        if (_run is null || (!force && !_journalDirty)) return;
        long now = System.Environment.TickCount64;
        if (!force && now - _lastSnapshotAt < SnapshotIntervalMs) return;
        if (_snapshotDirty || force)
        {
            Store.Update(BugSnapshot.Capture(_run, TrackedActions.LastOrDefault()));
            if (Store.Current is not null) Store.Current["runtime_errors"] = BugRuntimeFaults.Snapshot();
        }
        _lastSnapshotAt = now;
        _snapshotDirty = false;
        _journalDirty = false;
        if (force) Store.Persist();
        else _ = Store.QueuePersist();
    }

    internal static async Task<bool> SubmitAsync(string description, bool retryPending = false, JsonObject? screenshot = null)
    {
        try
        {
            var outbox = Outbox;
            string? report = null;
            if (!retryPending)
            {
                string text = description.Trim();
                if (text.Length < 2) { LastSubmission = new(false, null, "description_too_short", outbox.PendingCount); return false; }
                DrainRuntimeErrors();
                Track("report_requested", persist: true);
                FlushJournal(force: true);
                var journal = Store.JournalForUpload();
                foreach (var item in RuntimeMetadata()) journal[item.Key] = item.Value?.DeepClone();
                if (screenshot is not null) journal["screenshot"] = screenshot.DeepClone();
                journal["dropped_runtime_errors"] = BugRuntimeFaults.Dropped;
                journal["report_context"] = RunManager.Instance.DebugOnlyGetState() is null ? "last_recorded_run_or_menu" : "active_run";
                journal["capture_capabilities"] = new JsonObject { ["native_checkpoint"] = true, ["native_errors"] = true,
                    ["mod_fingerprints"] = true, ["network_state"] = true, ["screenshot"] = true, ["raw_network_packets"] = false,
                    ["third_party_config"] = "no_standard_export", ["performance_monitors"] = true,
                    ["automatic_snapshot_interval_ms"] = SnapshotIntervalMs, ["playability_evaluated"] = false,
                    ["energy_cost_scope"] = "local; displayed hand cost also captured from UI" };
                BugReplayCodec.MakeJsonSafe(journal);
                report = outbox.Enqueue(journal, text);
            }
            LastSubmission = await outbox.SendAsync(report).ConfigureAwait(false);
            return LastSubmission.Success;
        }
        catch (Exception ex)
        {
            LastSubmission = new(false, null, "local_storage_error", _outbox?.PendingCount ?? 0);
            Log.Warn("[Things BugReport] Submission retained locally: " + ex.GetType().Name); return false;
        }
    }
}
