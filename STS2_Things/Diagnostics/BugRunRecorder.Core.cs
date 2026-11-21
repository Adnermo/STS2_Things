using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2_Things.Platform;

namespace STS2_Things.Diagnostics;

/// <summary>Main-thread read-only Schema v4 journal. Only explicit F2 submissions perform network I/O.</summary>
internal static class BugRunRecorder
{
    private sealed record TrackedLocalChecksum(uint Id, uint Checksum, string Context, NetFullCombatState State);

    private static readonly System.Net.Http.HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(18) };
    private static readonly Uri Endpoint = new("https://reports.adnermo.online/api/reports");
    private static readonly BugSubmissionGuard SubmissionGuard = new();
    private static readonly object ChecksumGate = new();
    private static readonly List<TrackedLocalChecksum> RecentChecksums = [];

    private static BugReportStore? _store;
    private static BugReportOutbox? _outbox;
    private static RunState? _run;
    private static ActionExecutor? _executor;
    private static ChecksumTracker? _checksumTracker;
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
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == "http"
            && uri.Host == "127.0.0.1")
        {
            return uri;
        }
        return Endpoint;
    }

    internal static void Initialize()
    {
        if (_ready) return;

        BugRuntimeFaults.HarmonyOwnerResolver = BugHarmonyInspector.ResolveOwnersForFrames;
        BugFileFingerprints.PrimeMany(BugHarmonyInspector.CollectAllFingerprintPaths());

        RunManager.Instance.RunStarted += OnStarted;
        RunManager.Instance.RoomEntered += () => Track("room_entered", persist: true);
        CombatManager.Instance.CombatBegan += _ => Track("combat_started", persist: true);
        CombatManager.Instance.TurnStarted += _ => Track("turn_started", persist: true);
        CombatManager.Instance.CombatEnded += OnCombatEnded;

        // Start-of-room/turn fire-and-forget failures do not belong to ActionExecutor.
        // Use the original logging callback; do not catch, replace or suppress tasks.
        BugRuntimeFaults.InitializePersistence(Path.Combine(DataDir, "runtime-errors.json"));
        Log.LogCallback += OnNativeLog;
        _engineLogger = new BugEngineLogger();
        OS.AddLogger(_engineLogger);

        // A few native import errors occur before our initializer can subscribe.
        string[] args = OS.GetCmdlineArgs();
        int logArg = Array.IndexOf(args, "--log-file");
        string startupLog = logArg >= 0 && logArg + 1 < args.Length
            ? args[logArg + 1]
            : args.FirstOrDefault(arg => arg.StartsWith("--log-file=", StringComparison.Ordinal))?[11..]
                ?? Path.Combine(OS.GetUserDataDir(), "logs", "godot.log");
        BugRuntimeFaults.BackfillStartupLog(startupLog);

        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
        {
            try
            {
                if (failure.ExceptionObject is Exception exception)
                {
                    BugRuntimeFaults.CaptureException(exception, 0);
                }
                BugRuntimeFaults.Persist();
            }
            catch (Exception)
            {
                // Unhandled exception hook must never throw secondary exceptions.
            }
        };

        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame += PollFailedAction;
        _ready = true;

        var current = RunManager.Instance.DebugOnlyGetState();
        if (current is not null) OnStarted(current);
        Log.Info("[Things BugReport] Local Schema v4 journal enabled. Explicit F2 submissions only.");
    }

    private static void OnCombatEnded(MegaCrit.Sts2.Core.Rooms.CombatRoom _)
    {
        try
        {
            if (Store.Current is not null)
            {
                var replay = BugReplayCapture.CaptureCombatReplay();
                if (replay["available"]?.GetValue<bool>() == true)
                {
                    Store.Current["combat_replay"] = replay.DeepClone();
                }
            }
        }
        catch (Exception)
        {
            // Combat replay capture is optional and must never disrupt post-combat transitions.
        }
        Track("combat_ended", persist: true);
    }

    private static string RunKey(RunState run)
    {
        long started = (long)(AccessTools.Field(typeof(RunManager), "_startTime").GetValue(RunManager.Instance) ?? 0L);
        int profile;
        try
        {
            profile = SaveManager.Instance.CurrentProfileId;
        }
        catch (InvalidOperationException)
        {
            profile = 0;
        }
        bool isCustom = Sts2VersionSeam.IsCustomGame(run);
        string? dominion = Sts2VersionSeam.GetActiveDominionId(run);
        string input = profile + "|" + started + "|" + run.Rng.StringSeed + "|" + run.GameMode + "|" + run.AscensionLevel
            + "|" + string.Join(',', run.Players.Select(p => p.Character.Id.ToString()));
        if (isCustom || !string.IsNullOrEmpty(dominion))
        {
            input += "|" + (isCustom ? "custom" : "normal") + "|" + (dominion ?? string.Empty);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static void OnStarted(RunState run)
    {
        try
        {
            if (ReferenceEquals(_run, run)) return;
            if (_run is not null) Store.Archive(); // Pure frozen serialization; no live state reads.
            DetachExecutor();
            BugReplayCapture.ClearRunState();
            _reportedErrorAction = null;
            _combatCheckpointAt = 0;
            _startupWaitRecorded = false;

            BugFileFingerprints.PrimeMany(BugHarmonyInspector.CollectAllFingerprintPaths());
            var metadata = RuntimeMetadata(run);
            bool resumed = Store.Begin(RunKey(run), metadata, BugSnapshot.Capture(run));
            _run = run;

            _executor = RunManager.Instance.ActionExecutor;
            if (_executor is not null)
            {
                _executor.BeforeActionExecuted += BeforeAction;
                _executor.AfterActionExecuted += AfterAction;
            }
            AttachChecksumTracker(RunManager.Instance.ChecksumTracker);

            Track(resumed ? "run_resumed" : "run_first_observed", persist: true);
            FlushJournal(force: true);
        }
        catch (Exception ex)
        {
            Log.Warn("[Things BugReport] Start journal: " + ex.GetType().Name);
        }
    }

    private static void AttachChecksumTracker(ChecksumTracker? tracker)
    {
        if (ReferenceEquals(_checksumTracker, tracker)) return;
        DetachChecksumTracker();
        if (tracker is null) return;

        _checksumTracker = tracker;
        _checksumTracker.ChecksumGenerated += OnChecksumGenerated;
        _checksumTracker.StateDiverged += OnStateDiverged;
    }

    private static void DetachChecksumTracker()
    {
        if (_checksumTracker is not null)
        {
            _checksumTracker.ChecksumGenerated -= OnChecksumGenerated;
            _checksumTracker.StateDiverged -= OnStateDiverged;
            _checksumTracker = null;
        }
        lock (ChecksumGate)
        {
            RecentChecksums.Clear();
        }
    }

    private static void OnChecksumGenerated(NetChecksumData data, string context, NetFullCombatState fullState)
    {
        lock (ChecksumGate)
        {
            RecentChecksums.Add(new TrackedLocalChecksum(data.id, data.checksum, context ?? string.Empty, fullState));
            while (RecentChecksums.Count > 24)
            {
                RecentChecksums.RemoveAt(0);
            }
        }
    }

    private static void OnStateDiverged(ulong _, NetFullCombatState remoteState) => HandleNativeStateDiverged(remoteState);

    private static void HandleNativeStateDiverged(NetFullCombatState remoteState)
    {
        try
        {
            TrackedLocalChecksum? latestLocal;
            lock (ChecksumGate)
            {
                latestLocal = RecentChecksums.Count > 0 ? RecentChecksums[^1] : null;
            }

            var run = RunManager.Instance.DebugOnlyGetState();
            NetFullCombatState? localState = latestLocal?.State
                ?? (run is not null ? NetFullCombatState.FromRun(run, null) : null);
            if (localState is null) return;

            uint? localHash = latestLocal?.Checksum ?? _checksumTracker?.GenerateChecksum(localState);
            uint? remoteHash = _checksumTracker?.GenerateChecksum(remoteState);
            RecordStateDivergence(
                localState,
                remoteState,
                latestLocal?.Id,
                latestLocal?.Context,
                localHash,
                remoteHash);
        }
        catch (Exception)
        {
            // Divergence observation must never crash multiplayer disconnect cleanup.
        }
    }

    internal static JsonObject RecordStateDivergence(
        NetFullCombatState localState,
        NetFullCombatState remoteState,
        uint? stateId = null,
        string? context = null,
        uint? localChecksumOverride = null,
        uint? remoteChecksumOverride = null)
    {
        var tracker = _checksumTracker ?? RunManager.Instance.ChecksumTracker;
        uint? localHash = localChecksumOverride;
        uint? remoteHash = remoteChecksumOverride;
        if (tracker is not null)
        {
            try
            {
                localHash ??= tracker.GenerateChecksum(localState);
                remoteHash ??= tracker.GenerateChecksum(remoteState);
            }
            catch (Exception)
            {
                // Packet serialization fallback if test state omits full serializer setup.
            }
        }

        string localDump;
        string remoteDump;
        try
        {
            localDump = localState.Anonymized().ToString();
            remoteDump = remoteState.Anonymized().ToString();
        }
        catch (Exception)
        {
            localDump = localState.ToString();
            remoteDump = remoteState.ToString();
        }

        int? combatRound = CombatManager.Instance.DebugOnlyGetState()?.RoundNumber;
        string? modelCacheHash = null;
        try
        {
            modelCacheHash = $"0x{ModelIdSerializationCache.Hash:x8}";
        }
        catch (Exception)
        {
            // ModelIdSerializationCache may be uninitialized in minimal contexts.
        }

        var desync = BugReportSchemaV4.BuildDesyncReport(
            combatRound: combatRound,
            stateId: stateId,
            localChecksum: localHash.HasValue ? $"0x{localHash.Value:x8}" : null,
            remoteChecksum: remoteHash.HasValue ? $"0x{remoteHash.Value:x8}" : null,
            modelCacheHash: modelCacheHash,
            context: context,
            localStateDump: localDump,
            remoteStateDump: remoteDump);

        var run = RunManager.Instance.DebugOnlyGetState();
        if (run is not null && !ReferenceEquals(_run, run))
        {
            OnStarted(run);
        }
        if (Store.Current is not null)
        {
            Store.Current["desync"] = desync.DeepClone();
            Track("state_diverged", new JsonObject
            {
                ["combat_round"] = combatRound,
                ["state_id"] = stateId,
                ["local_checksum"] = desync["local_checksum"]?.DeepClone(),
                ["remote_checksum"] = desync["remote_checksum"]?.DeepClone(),
                ["diff_count"] = (desync["field_diffs"] as JsonArray)?.Count ?? 0,
            }, persist: true);
            FlushJournal(force: true);
        }
        return desync;
    }

    private static JsonObject RuntimeMetadata(RunState? activeRun = null)
    {
        var faultsSnapshot = BugRuntimeFaults.Snapshot();
        var errorsAlias = new JsonArray(faultsSnapshot.Select(n => n?.DeepClone()).ToArray());
        var priorityFrames = ExtractFaultFrames(faultsSnapshot);
        var configObj = BugSnapshot.Object(new
        {
            session_active = Config.MultiplayerConfig.IsActive,
            session_locked = Config.MultiplayerConfig.IsLocked,
            local = Config.ThingsModConfig.Entries.ToDictionary(e => e.Key, e => Config.ThingsModConfig.GetLocalValue(e.Key)),
            effective = Config.ThingsModConfig.Entries.ToDictionary(
                e => e.Key,
                e => Config.MultiplayerConfig.GetOverride(e.Key) ?? Config.ThingsModConfig.GetLocalValue(e.Key)),
        });

        var metadata = BugSnapshot.Object(new
        {
            game_version = GameVersion(),
            game_code_mvid = typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
            rng_seed_type = typeof(RunRngSet).GetProperty("Seed")?.PropertyType.FullName,
            mod_version = typeof(BugRunRecorder).Assembly.GetName().Version?.ToString() ?? "unknown",
            active_mods = ModManager.GetLoadedMods().Take(300).Select((m, index) => new
            {
                id = m.manifest?.id ?? "unknown",
                version = m.version?.ToString() ?? "unknown",
                name = m.manifest?.name,
                workshop_id = ModWorkshopId(m),
                load_index = index,
                source = m.modSource.ToString(),
                code = ModCode(m),
            }).ToArray(),
        });

        metadata["runtime_faults"] = faultsSnapshot;
        metadata["runtime_errors"] = errorsAlias;
        metadata["assemblies"] = BugHarmonyInspector.CaptureLoadedAssemblies();
        metadata["harmony_patches"] = BugHarmonyInspector.CaptureHarmonyPatches(priorityFrames);
        metadata["game_file"] = BugFileFingerprints.Describe(
            BugHarmonyInspector.ResolveAssemblyDiskPath(typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly));
        metadata["things_files"] = ThingsOwnFiles();
        metadata["mod_files"] = ModFiles();
        metadata["things_config"] = configObj;
        metadata["config"] = configObj.DeepClone();

        var run = activeRun ?? RunManager.Instance.DebugOnlyGetState();
        if (run is not null)
        {
            metadata["game_mode"] = run.GameMode.ToString();
            metadata["is_custom_game"] = Sts2VersionSeam.IsCustomGame(run);
            metadata["dominion_id"] = Sts2VersionSeam.GetActiveDominionId(run);
            metadata["run_models"] = new JsonArray(
                Sts2VersionSeam.GetRunModelIds(run).Take(64).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            metadata["modifiers"] = new JsonArray(
                run.Modifiers.Take(200).Select(m => (JsonNode?)JsonValue.Create(m.Id.ToString())).ToArray());
            metadata["network"] = BugSnapshot.Object(new
            {
                type = RunManager.Instance.NetService.Type.ToString(),
                connected = RunManager.Instance.NetService.IsConnected,
                loading = RunManager.Instance.NetService.IsGameLoading,
                platform = RunManager.Instance.NetService.Platform.ToString(),
                player_count = run.Players.Count,
            });
        }

        return metadata;
    }

    private static List<string> ExtractFaultFrames(JsonArray faults)
    {
        var frames = new List<string>();
        foreach (var fault in faults.OfType<JsonObject>())
        {
            if (fault["frames"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item?.GetValue<string>() is { Length: > 0 } f) frames.Add(f);
                }
            }
        }
        return frames;
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
                bool isCombatEntry = checkpoint["entry_kind"]?.GetValue<string>() is "combat_entry" or "boss_entry" or "event_combat_entry";
                _combatCheckpointAt = isCombatEntry ? System.Environment.TickCount64 : 0;
                _startupWaitRecorded = false;

                string eventKind = isCombatEntry ? "combat_checkpoint" : "room_checkpoint";
                Store.Track(new JsonObject
                {
                    ["kind"] = eventKind,
                    ["at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["detail"] = new JsonObject
                    {
                        ["available"] = checkpoint["available"]?.DeepClone(),
                        ["schema"] = checkpoint["schema"]?.DeepClone(),
                        ["entry_kind"] = checkpoint["entry_kind"]?.DeepClone(),
                        ["room_type"] = checkpoint["room_type"]?.DeepClone(),
                        ["encounter_id"] = checkpoint["encounter_id"]?.DeepClone(),
                        ["event_id"] = checkpoint["event_id"]?.DeepClone(),
                    },
                });
                _journalDirty = true;
                FlushJournal(force: true);
            }
        }
        catch (Exception)
        {
            // Checkpoint observation must never interrupt room entry.
        }
    }

    private static string? ModWorkshopId(Mod mod)
    {
        if (mod.workshopId is ulong id) return id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Fallback: infer only the public item ID from the Steam Workshop cache
        // layout; the local path itself is never reported.
        if (mod.modSource != ModSource.SteamWorkshop) return null;
        if (string.IsNullOrWhiteSpace(mod.path)) return null;
        try
        {
            var directory = new DirectoryInfo(mod.path);
            for (; directory.Parent is not null; directory = directory.Parent)
            {
                if (directory.Parent.Name == "2868840" && directory.Parent.Parent?.Name == "content"
                    && ulong.TryParse(directory.Name, out ulong item) && item != 0)
                {
                    return item.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception)
        {
            // Ignore non-filesystem mod paths.
        }
        return null;
    }

    private static object[] ModCode(Mod mod)
    {
        return mod.assemblies.Where(a => !a.IsDynamic).Select(a => (object)new
        {
            name = a.GetName().Name,
            mvid = a.ManifestModule.ModuleVersionId.ToString("D"),
            version = a.GetName().Version?.ToString(),
        }).ToArray();
    }

    private static JsonArray ThingsOwnFiles()
    {
        var files = new JsonArray();
        try
        {
            string? dllPath = BugHarmonyInspector.ResolveAssemblyDiskPath(typeof(BugRunRecorder).Assembly);
            if (!string.IsNullOrWhiteSpace(dllPath) && File.Exists(dllPath))
            {
                files.Add(new JsonObject
                {
                    ["file"] = Path.GetFileName(dllPath),
                    ["fingerprint"] = BugFileFingerprints.Describe(dllPath),
                });
                string? dir = Path.GetDirectoryName(dllPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    string pckPath = Path.Combine(dir, "STS2_Things.pck");
                    if (File.Exists(pckPath))
                    {
                        files.Add(new JsonObject
                        {
                            ["file"] = "STS2_Things.pck",
                            ["fingerprint"] = BugFileFingerprints.Describe(pckPath),
                        });
                    }
                }
            }
        }
        catch (Exception)
        {
            // Self-file inspection is best-effort.
        }
        return files;
    }

    private static JsonArray ModFiles()
    {
        var result = new JsonArray();
        foreach (var mod in ModManager.GetLoadedMods())
        {
            var files = new JsonArray();
            try
            {
                if (!string.IsNullOrWhiteSpace(mod.path) && Directory.Exists(mod.path))
                {
                    foreach (string path in Directory.EnumerateFiles(mod.path)
                        .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".dll" or ".pck")
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .Take(20))
                    {
                        files.Add(new JsonObject
                        {
                            ["file"] = Path.GetFileName(path),
                            ["fingerprint"] = BugFileFingerprints.Describe(path),
                        });
                    }
                }
            }
            catch (IOException)
            {
                // Locked mod directories are skipped safely.
            }
            catch (UnauthorizedAccessException)
            {
                // Restricted mod directories are skipped safely.
            }
            catch (ArgumentException)
            {
                // Synthetic probe URIs such as probe://STS2_Things are skipped safely.
            }
            result.Add(new JsonObject { ["id"] = mod.manifest?.id, ["files"] = files });
        }
        return result;
    }

    private static string GameVersion()
    {
        try
        {
            return MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static void DetachExecutor()
    {
        if (_executor is not null)
        {
            _executor.BeforeActionExecuted -= BeforeAction;
            _executor.AfterActionExecuted -= AfterAction;
        }
        foreach (var action in TrackedActions) DetachAction(action);
        TrackedActions.Clear();
        _executor = null;
        DetachChecksumTracker();
    }

    private static void DetachAction(GameAction action)
    {
        action.BeforePausedForPlayerChoice -= BeforeChoice;
        action.BeforeResumedAfterPlayerChoice -= AfterChoice;
        action.BeforeCancelled -= Cancelled;
    }

    private static JsonObject ActionRecord(GameAction action) => BugSnapshot.Object(new
    {
        action_id = action.Id,
        action_type = action.GetType().Name,
        action_state = action.State.ToString(),
        card = action is PlayCardAction card ? card.CardModelId.ToString() : null,
        target = action is PlayCardAction targetCard ? targetCard.TargetId : action is UsePotionAction potion ? potion.TargetId : null,
        potion_slot = action is UsePotionAction use ? (int?)use.PotionIndex : null,
        player_index = _run?.Players.ToList().FindIndex(p => p.NetId == action.OwnerId),
        error = BugSnapshot.Error(action.Exception),
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
        DetachAction(action);
        TrackedActions.Remove(action);
    }

    private static void AfterAction(GameAction action)
    {
        Track("action_completed", ActionRecord(action), persist: true);
        DetachAction(action);
        TrackedActions.Remove(action);
    }

    private static void PollFailedAction()
    {
        if (System.Environment.TickCount64 - _lastErrorPoll < 250) return;
        _lastErrorPoll = System.Environment.TickCount64;
        DrainRuntimeErrors();

        var currentRun = RunManager.Instance.DebugOnlyGetState();
        if (currentRun is null && _run is not null)
        {
            try
            {
                Store.Archive();
            }
            catch (IOException)
            {
                // Ignore transient archive contention on shutdown/return to menu.
            }
            DetachExecutor();
            _run = null;
            return;
        }

        if (currentRun is not null && !ReferenceEquals(_checksumTracker, RunManager.Instance.ChecksumTracker))
        {
            AttachChecksumTracker(RunManager.Instance.ChecksumTracker);
        }

        var action = _executor?.CurrentlyRunningAction;
        if (!_startupWaitRecorded && _combatCheckpointAt > 0
            && System.Environment.TickCount64 - _combatCheckpointAt > 15_000
            && currentRun?.CurrentRoom is MegaCrit.Sts2.Core.Rooms.CombatRoom
            && currentRun.Players.Any(p => p.PlayerCombatState?.Phase.ToString() == "Start"))
        {
            _startupWaitRecorded = true;
            Track("combat_start_wait", new JsonObject
            {
                ["elapsed_ms"] = System.Environment.TickCount64 - _combatCheckpointAt,
                ["note"] = "observation_only_not_a_bug_verdict",
            }, persist: true);
        }

        if (action?.Exception is not null && !ReferenceEquals(action, _reportedErrorAction))
        {
            _reportedErrorAction = action;
            Track("action_failed", ActionRecord(action), persist: true);
        }

        try
        {
            FlushJournal();
        }
        catch (Exception ex)
        {
            Log.Warn("[Things BugReport] Journal snapshot: " + ex.GetType().Name);
        }
    }

    private static void OnNativeLog(LogLevel level, string text, int skipFrames)
    {
        if (_drainingRuntimeErrors || level != LogLevel.Error) return;
        try
        {
            BugRuntimeFaults.Capture(text, skipFrames);
        }
        catch (Exception)
        {
            // Diagnostics must never make a native error fatal.
        }
    }

    private static void DrainRuntimeErrors()
    {
        _drainingRuntimeErrors = true;
        try
        {
            for (int i = 0; i < 32 && BugRuntimeFaults.TryTake(out var detail); i++)
            {
                if (RunManager.Instance.DebugOnlyGetState() is not null)
                {
                    Track("runtime_error", detail, persist: true);
                }
            }
        }
        finally
        {
            _drainingRuntimeErrors = false;
        }
    }

    private static void Track(string kind, JsonObject? detail = null, bool persist = false)
    {
        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null) return;
            if (!ReferenceEquals(_run, run)) OnStarted(run);
            if (!ReferenceEquals(_run, run)) return;
            Store.Track(new JsonObject
            {
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["kind"] = kind,
                ["detail"] = detail?.DeepClone(),
                ["act_index"] = run.CurrentActIndex,
                ["floor"] = run.ActFloor,
                ["room"] = run.CurrentRoom?.GetType().Name,
            });
            _snapshotDirty = true;
            _journalDirty |= persist;
        }
        catch (Exception ex)
        {
            Log.Warn("[Things BugReport] Journal event: " + ex.GetType().Name);
        }
    }

    private static void FlushJournal(bool force = false)
    {
        if (_run is null || (!force && !_journalDirty)) return;
        long now = System.Environment.TickCount64;
        if (!force && now - _lastSnapshotAt < SnapshotIntervalMs) return;
        if (_snapshotDirty || force)
        {
            Store.Update(BugSnapshot.Capture(_run, TrackedActions.LastOrDefault()), clone: false);
            if (Store.Current is not null)
            {
                var faults = BugRuntimeFaults.Snapshot();
                Store.Current["runtime_faults"] = faults;
                Store.Current["runtime_errors"] = new JsonArray(faults.Select(n => n?.DeepClone()).ToArray());
            }
        }
        _lastSnapshotAt = now;
        _snapshotDirty = false;
        _journalDirty = false;
        if (force) Store.Persist();
        else _ = Store.QueuePersist();
    }

    /// <summary>
    /// Captures a complete Schema v4 upload journal on the calling thread, then awaits any in-flight
    /// background SHA-256 computations so game/mod DLL and PCK fingerprints are never "pending".
    /// </summary>
    internal static async Task<JsonObject> CaptureSnapshotAsync(int fingerprintTimeoutMs = 1500)
    {
        DrainRuntimeErrors();
        FlushJournal(force: true);

        var activeRun = RunManager.Instance.DebugOnlyGetState();
        var journal = Store.JournalForUpload();
        var combatReplay = BugReplayCapture.CaptureCombatReplay();
        var preAwaitMeta = RuntimeMetadata(activeRun);
        var fingerprintPaths = BugHarmonyInspector.CollectAllFingerprintPaths();

        BugFileFingerprints.PrimeMany(fingerprintPaths);
        bool allFingerprintsReady = await BugFileFingerprints.FlushPendingAsync(fingerprintTimeoutMs);

        foreach (var item in preAwaitMeta)
        {
            journal[item.Key] = item.Value?.DeepClone();
        }

        // Re-read file-backed SHA-256 descriptors now that FlushPendingAsync has completed.
        journal["game_file"] = BugFileFingerprints.Describe(
            BugHarmonyInspector.ResolveAssemblyDiskPath(typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly));
        journal["things_files"] = ThingsOwnFiles();
        journal["mod_files"] = ModFiles();
        journal["assemblies"] = BugHarmonyInspector.CaptureLoadedAssemblies();
        journal["combat_replay"] = combatReplay;
        journal["dropped_runtime_errors"] = BugRuntimeFaults.Dropped;
        journal["report_context"] = activeRun is null ? "last_recorded_run_or_menu" : "active_run";
        journal["capture_capabilities"] = new JsonObject
        {
            ["schema_version"] = BugReportSchemaV4.SchemaVersion,
            ["native_checkpoint"] = true,
            ["universal_room_checkpoint_v2"] = true,
            ["combat_replay"] = true,
            ["scrubbed_exception_messages"] = true,
            ["inner_exception_chains"] = true,
            ["harmony_patch_attribution"] = true,
            ["loaded_assembly_mvids"] = true,
            ["awaited_file_fingerprints"] = allFingerprintsReady,
            ["multiplayer_desync_capture"] = true,
            ["native_errors"] = true,
            ["mod_fingerprints"] = true,
            ["network_state"] = true,
            ["screenshot"] = true,
            ["raw_network_packets"] = false,
            ["third_party_config"] = "no_standard_export",
            ["performance_monitors"] = true,
            ["automatic_snapshot_interval_ms"] = SnapshotIntervalMs,
            ["playability_evaluated"] = false,
            ["energy_cost_scope"] = "local; displayed hand cost also captured from UI",
        };

        BugReplayCodec.MakeJsonSafe(journal);
        return journal;
    }

    internal static async Task<bool> SubmitAsync(string description, bool retryPending = false, JsonObject? screenshot = null)
    {
        try
        {
            var outbox = Outbox;
            string? report = null;
            JsonObject? submittedJournal = null;

            if (!retryPending)
            {
                string text = description.Trim();
                if (text.Length < 2)
                {
                    LastSubmission = new(false, null, "description_too_short", outbox.PendingCount);
                    return false;
                }

                Track("report_requested", persist: true);
                var journal = await CaptureSnapshotAsync().ConfigureAwait(false);
                if (screenshot is not null)
                {
                    journal["screenshot"] = screenshot.DeepClone();
                }

                if (SubmissionGuard.ShouldThrottleDuplicate(journal, DateTimeOffset.UtcNow, out string? priorReportId))
                {
                    string localId = outbox.SaveLocalOnly(journal, text, "duplicate_throttled_5m", priorReportId);
                    LastSubmission = new(true, priorReportId ?? localId, "duplicate_throttled_saved_locally", outbox.PendingCount);
                    return true;
                }

                submittedJournal = journal;
                report = outbox.Enqueue(journal, text);
            }

            LastSubmission = await outbox.SendAsync(report).ConfigureAwait(false);
            if (LastSubmission.Success && submittedJournal is not null)
            {
                SubmissionGuard.RecordSubmitted(submittedJournal, LastSubmission.ReportId ?? report, DateTimeOffset.UtcNow);
            }
            return LastSubmission.Success;
        }
        catch (Exception ex)
        {
            LastSubmission = new(false, null, "local_storage_error", _outbox?.PendingCount ?? 0);
            Log.Warn("[Things BugReport] Submission retained locally: " + ex.GetType().Name);
            return false;
        }
    }
}
