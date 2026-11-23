using System;
using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2_Things.Acts;
using STS2_Things.Compatibility.Mods;
using STS2_Things.Config;

namespace STS2_Things.Hooks;

/// <summary>
/// Host-authoritative Act 2 anti-streak pity flag sent on the reliable session channel
/// immediately before <see cref="ThingsConfigSnapshotMessage"/> so every peer evaluates
/// <see cref="ActModel.GetRandomList"/> identically without altering user config toggles.
/// </summary>
public struct ThingsDepthsPityMessage : INetMessage
{
    public uint Revision;
    public bool ForceDepths;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt(Revision);
        writer.WriteBool(ForceDepths);
    }

    public void Deserialize(PacketReader reader)
    {
        if (reader.Buffer.Length * 8 - reader.BitPosition < 33)
        {
            Revision = 0;
            ForceDepths = false;
            return;
        }

        Revision = reader.ReadUInt();
        ForceDepths = reader.ReadBool();
    }
}

/// <summary>
/// Tracks consecutive non-<see cref="ThingsDepths"/> Act 2 entries locally and guarantees
/// <see cref="ThingsDepths"/> after <see cref="PityMissThreshold"/> consecutive non-Depths
/// Act 2 visits. Commits only when a run actually enters Act 2 (so Act 1 deaths/restarts
/// never waste pity) and follows the host's synced flag in multiplayer sessions.
/// </summary>
public static class DepthsActPityTracker
{
    public const int PityMissThreshold = 2;

    private const int CurrentStateVersion = 1;
    private const int Act2Index = 1;

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly AccessTools.FieldRef<RunManager, long>? StartTimeField =
        AccessTools.Field(typeof(RunManager), "_startTime") != null
            ? AccessTools.FieldRefAccess<RunManager, long>("_startTime")
            : null;

    private static bool _loaded;
    private static int _consecutiveNonDepthsAct2Visits;
    private static string? _lastRecordedRunKey;
    private static WeakReference<RunState>? _lastRecordedRunState;
    private static GameMode? _activeRunStartGameMode;

    private static bool _testOverrideEnabled;
    private static string? _testStateFilePath;
    private static bool _testAllowUnsavedRuns;

    internal readonly record struct RunStartScope(bool Pushed, GameMode? Previous);

    public static string StatePath
    {
        get
        {
            lock (Sync)
            {
                if (_testOverrideEnabled && _testStateFilePath is not null)
                    return _testStateFilePath;
            }

            return Path.Combine(OS.GetUserDataDir(), "mod_configs", "STS2_Things.depths_pity.json");
        }
    }

    public static int ConsecutiveNonDepthsAct2Visits
    {
        get
        {
            lock (Sync)
            {
                if (!IsTrackingEnabledLocked())
                    return 0;

                EnsureLoadedLocked();
                return _consecutiveNonDepthsAct2Visits;
            }
        }
    }

    public static bool IsPityTriggered => ConsecutiveNonDepthsAct2Visits >= PityMissThreshold;

    internal static RunStartScope PushRunStartGameMode(GameMode gameMode)
    {
        lock (Sync)
        {
            GameMode? previous = _activeRunStartGameMode;
            _activeRunStartGameMode = gameMode;
            return new RunStartScope(Pushed: true, Previous: previous);
        }
    }

    internal static void PopRunStartGameMode(RunStartScope scope)
    {
        if (!scope.Pushed)
            return;

        lock (Sync)
        {
            _activeRunStartGameMode = scope.Previous;
        }
    }

    internal static bool ShouldHostActivatePity(GameMode gameMode)
    {
        if (gameMode == GameMode.Daily || ActLikeIt2Compatibility.IsInstalled)
            return false;

        return IsPityTriggered;
    }

    internal static bool ShouldForceDepthsInActRoll(bool isMultiplayer)
    {
        if (ActLikeIt2Compatibility.IsInstalled)
            return false;

        GameMode? activeMode;
        lock (Sync)
        {
            activeMode = _activeRunStartGameMode;
        }

        if (activeMode == GameMode.Daily)
            return false;

        if (MultiplayerConfig.IsActive)
            return MultiplayerConfig.SessionDepthsPityActive;

        if (isMultiplayer)
            return false;

        return IsPityTriggered;
    }

    internal static void RecordAct2Entry(RunManager runManager, int actIndex)
    {
        if (actIndex != Act2Index)
            return;

        bool allowUnsaved;
        lock (Sync)
        {
            if (!IsTrackingEnabledLocked())
                return;
            allowUnsaved = _testAllowUnsavedRuns;
        }

        RunState? state = runManager.DebugOnlyGetState();
        if (state is null || state.Acts.Count <= Act2Index)
            return;

        if (state.GameMode == GameMode.Daily)
            return;

        if (runManager.NetService?.Type == NetGameType.Replay)
            return;

        if (!runManager.ShouldSave && !allowUnsaved)
            return;

        long startTime = StartTimeField is not null ? StartTimeField(runManager) : 0L;
        string? runKey = BuildRunKey(state, startTime);
        RecordAct2Outcome(state.Acts[Act2Index], runKey, state);
    }

    internal static bool RecordAct2Outcome(ActModel act2, string? runKey, RunState? runState = null)
    {
        int updatedCount;
        bool enteredDepths = act2 is ThingsDepths;

        lock (Sync)
        {
            if (!IsTrackingEnabledLocked())
                return false;

            EnsureLoadedLocked();

            if (runState is not null &&
                _lastRecordedRunState is not null &&
                _lastRecordedRunState.TryGetTarget(out RunState? lastState) &&
                ReferenceEquals(lastState, runState))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(runKey) &&
                string.Equals(runKey, _lastRecordedRunKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (enteredDepths)
            {
                _consecutiveNonDepthsAct2Visits = 0;
            }
            else if (_consecutiveNonDepthsAct2Visits < int.MaxValue)
            {
                _consecutiveNonDepthsAct2Visits++;
            }

            _lastRecordedRunKey = string.IsNullOrEmpty(runKey) ? null : runKey;
            _lastRecordedRunState = runState is not null ? new WeakReference<RunState>(runState) : null;
            updatedCount = _consecutiveNonDepthsAct2Visits;

            SaveLocked();
        }

        Log.Info(enteredDepths
            ? "STS2_Things: Act 2 Depths entered; reset Depths anti-streak pity counter to 0."
            : $"STS2_Things: Act 2 non-Depths ({act2.Id}) entered; Depths anti-streak misses = {updatedCount}/{PityMissThreshold}.");
        return true;
    }

    internal static void ConfigureForTest(
        bool enabled,
        string? stateFilePath = null,
        int initialMisses = 0,
        string? lastRecordedRunKey = null,
        bool allowUnsavedRuns = true)
    {
        lock (Sync)
        {
            _testOverrideEnabled = enabled;
            _testStateFilePath = stateFilePath;
            _testAllowUnsavedRuns = allowUnsavedRuns;
            _loaded = true;
            _consecutiveNonDepthsAct2Visits = Math.Max(0, initialMisses);
            _lastRecordedRunKey = string.IsNullOrEmpty(lastRecordedRunKey) ? null : lastRecordedRunKey;
            _lastRecordedRunState = null;
            _activeRunStartGameMode = null;
        }
    }

    internal static void ReloadFromDiskForTest()
    {
        lock (Sync)
        {
            _loaded = false;
            _consecutiveNonDepthsAct2Visits = 0;
            _lastRecordedRunKey = null;
            _lastRecordedRunState = null;
        }
    }

    internal static void ResetForTest()
    {
        lock (Sync)
        {
            _testOverrideEnabled = false;
            _testStateFilePath = null;
            _testAllowUnsavedRuns = false;
            _loaded = false;
            _consecutiveNonDepthsAct2Visits = 0;
            _lastRecordedRunKey = null;
            _lastRecordedRunState = null;
            _activeRunStartGameMode = null;
        }
    }

    private static bool IsTrackingEnabledLocked() => !TestMode.IsOn || _testOverrideEnabled;

    private static bool ShouldUseDiskLocked() =>
        !TestMode.IsOn || (_testOverrideEnabled && _testStateFilePath is not null);

    private static string ResolveStatePathLocked() =>
        _testOverrideEnabled && _testStateFilePath is not null
            ? _testStateFilePath
            : Path.Combine(OS.GetUserDataDir(), "mod_configs", "STS2_Things.depths_pity.json");

    private static string? BuildRunKey(RunState state, long startTime)
    {
        string seed = state.Rng?.StringSeed ?? string.Empty;
        if (string.IsNullOrEmpty(seed) && startTime <= 0)
            return null;

        return startTime > 0 ? $"{seed}|{startTime}" : seed;
    }

    private static void EnsureLoadedLocked()
    {
        if (_loaded)
            return;

        _loaded = true;
        if (!ShouldUseDiskLocked())
            return;

        string path = ResolveStatePathLocked();
        if (!File.Exists(path))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Depths pity state root must be an object.");

            int misses = 0;
            if (document.RootElement.TryGetProperty("ConsecutiveNonDepthsAct2Visits", out JsonElement missesElem) &&
                missesElem.ValueKind == JsonValueKind.Number &&
                missesElem.TryGetInt32(out int parsed))
            {
                misses = Math.Max(0, parsed);
            }

            string? lastKey = null;
            if (document.RootElement.TryGetProperty("LastRecordedRunKey", out JsonElement keyElem) &&
                keyElem.ValueKind == JsonValueKind.String)
            {
                string? rawKey = keyElem.GetString();
                if (!string.IsNullOrEmpty(rawKey))
                    lastKey = rawKey;
            }

            _consecutiveNonDepthsAct2Visits = misses;
            _lastRecordedRunKey = lastKey;
        }
        catch (Exception ex)
        {
            _consecutiveNonDepthsAct2Visits = 0;
            _lastRecordedRunKey = null;
            Log.Warn($"STS2_Things: Depths pity state '{path}' is unreadable ({ex.Message}); resetting counter to 0.");
        }
    }

    private static void SaveLocked()
    {
        if (!ShouldUseDiskLocked())
            return;

        string path = ResolveStatePathLocked();
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var payload = new PersistedState(
                Version: CurrentStateVersion,
                ConsecutiveNonDepthsAct2Visits: _consecutiveNonDepthsAct2Visits,
                LastRecordedRunKey: _lastRecordedRunKey ?? string.Empty);

            string temporary = path + ".things-new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"STS2_Things: Depths pity state could not be saved: {ex.Message}");
        }
    }

    private sealed record PersistedState(
        int Version,
        int ConsecutiveNonDepthsAct2Visits,
        string LastRecordedRunKey);
}
