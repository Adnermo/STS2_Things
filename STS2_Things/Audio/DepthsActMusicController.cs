using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2_Things.Acts;
using STS2_Things.Platform;

namespace STS2_Things.Audio;

/// <summary>
/// Seamless multi-stage Act music engine for <see cref="ThingsDepths"/>, replicating native FMOD
/// <c>Progress</c> state transitions (Explore/Init, Combat, Muffled Low-Pass CombatEnd, Event/Unknown,
/// Treasure, Campfire Silence, and native FMOD delegation for Merchant, Elite, and Boss encounters)
/// across two seeded Depths suites (<c>depths_a1</c> and <c>depths_a2</c>).
/// </summary>
public sealed partial class DepthsActMusicController : Node
{
    public enum Stage
    {
        Inactive,
        YieldToCustom,
        ExploreInit,
        CombatFull,
        CombatEndMuffled,
        ExploreUnknown,
        ExploreTreasure,
        RestSilent,
        DelegateFmodMerchant,
        DelegateFmodElite,
    }

    internal static readonly (string Explore, string Combat)[] Suites =
    [
        ("res://music/depths/depths_a1_explore.wav", "res://music/depths/depths_a1_combat.wav"),
        ("res://music/depths/depths_a2_explore.wav", "res://music/depths/depths_a2_combat.wav"),
    ];

    private const string ExploreBusName = "ThingsDepthsExplore";
    private const string CombatBusName = "ThingsDepthsCombat";
    private const int LowerDepthsFloorThreshold = 8;
    private const int BlitzLowerDepthsFloorThreshold = 4;

    private const float MaxCutoffHz = 20500f;
    private const float CombatEndCutoffHz = 920f;
    private const float CombatEndGain = 0.473f; // -6.5 dB matching native act2_a1 CombatEnd duck

    private static DepthsActMusicController? _instance;
    private static bool _customEventMusicActive;
    private static bool _eliteSecondPhase;

    private AudioStreamPlayer? _explorePlayer;
    private AudioStreamPlayer? _combatPlayer;
    private AudioEffectLowPassFilter? _exploreFilter;
    private AudioEffectLowPassFilter? _combatFilter;

    private int _loadedExploreSuite = -1;
    private int _loadedCombatSuite = -1;
    private Stage _currentStage = Stage.Inactive;
    private bool _fmodActSuppressed;

    private float _curExploreGain;
    private float _targetExploreGain;
    private float _curCombatGain;
    private float _targetCombatGain;

    private float _curExploreCutoffHz = MaxCutoffHz;
    private float _targetExploreCutoffHz = MaxCutoffHz;
    private float _curCombatCutoffHz = MaxCutoffHz;
    private float _targetCombatCutoffHz = MaxCutoffHz;

    private float _fadeSpeedPerSec = 1f;

    public static Stage CurrentStage => _instance?._currentStage ?? Stage.Inactive;
    public static int ActiveCombatSuite => _instance?._loadedCombatSuite ?? -1;
    public static int ActiveExploreSuite => _instance?._loadedExploreSuite ?? -1;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EnsureBusesAndPlayers();
    }

    public override void _Process(double delta)
    {
        float dt = (float)Math.Clamp(delta, 0.0, 0.1);
        if (dt <= 0f)
            return;

        float step = Math.Clamp(_fadeSpeedPerSec * dt, 0.001f, 1f);
        _curExploreGain = Mathf.MoveToward(_curExploreGain, _targetExploreGain, step);
        _curCombatGain = Mathf.MoveToward(_curCombatGain, _targetCombatGain, step);

        float filterLerp = 1f - MathF.Exp(-4.5f * _fadeSpeedPerSec * dt);
        _curExploreCutoffHz = InterpCutoffLog(_curExploreCutoffHz, _targetExploreCutoffHz, filterLerp);
        _curCombatCutoffHz = InterpCutoffLog(_curCombatCutoffHz, _targetCombatCutoffHz, filterLerp);

        if (_exploreFilter != null)
            _exploreFilter.CutoffHz = _curExploreCutoffHz;
        if (_combatFilter != null)
            _combatFilter.CutoffHz = _curCombatCutoffHz;

        float settingsScale = ResolveSettingsGainLinear();
        ApplyDeckOutput(_explorePlayer, _curExploreGain, _targetExploreGain, settingsScale);
        ApplyDeckOutput(_combatPlayer, _curCombatGain, _targetCombatGain, settingsScale);
    }

    private static float InterpCutoffLog(float currentHz, float targetHz, float t)
    {
        float c = Math.Clamp(currentHz, 120f, MaxCutoffHz);
        float g = Math.Clamp(targetHz, 120f, MaxCutoffHz);
        if (MathF.Abs(c - g) < 2f)
            return g;
        float logC = MathF.Log(c);
        float logG = MathF.Log(g);
        return Math.Clamp(MathF.Exp(logC + (logG - logC) * Math.Clamp(t, 0f, 1f)), 120f, MaxCutoffHz);
    }

    private static float ResolveSettingsGainLinear()
    {
        if (SaveManager.Instance?.SettingsSave is not { } settings)
            return 0.89f; // -1 dB nominal default
        float product = Math.Clamp(settings.VolumeMaster, 0f, 1f) * Math.Clamp(settings.VolumeBgm, 0f, 1f);
        if (product <= 0.0001f)
            return 0f;
        // SettingsSave defaults to 0.5 Master * 0.5 BGM = 0.25 -> ~0.89 (-1.0 dB); cap upward boost at 1.12 (+1 dB).
        return Math.Min(1.12f, 0.89f * (product / 0.25f));
    }

    private static void ApplyDeckOutput(
        AudioStreamPlayer? player,
        float currentGain,
        float targetGain,
        float settingsScale)
    {
        if (player == null || !IsInstanceValid(player))
            return;

        if (currentGain <= 0.0005f && targetGain <= 0.0005f)
        {
            player.VolumeLinear = 0f;
            if (player.Playing && !player.StreamPaused)
                player.StreamPaused = true;
            return;
        }

        // Gentle equal-power perceptual shaping near silence while keeping 1.0 unity accurate.
        float shaped = currentGain * (0.35f + 0.65f * currentGain);
        player.VolumeLinear = Math.Max(0f, shaped * settingsScale);
    }

    // Keep this single NonPublic static signature unambiguous for DepthsMusicStageProbe reflection.
    internal static int ResolveTargetSuiteIndex(ulong seed, int actFloor) =>
        ResolveTargetSuiteIndex(seed, actFloor, LowerDepthsFloorThreshold);

    public static int ResolveTargetSuiteIndex(ulong seed, int actFloor, int floorThreshold)
    {
        // SplitMix64 salted with "depths_bg_music" FNV-1a constant for cross-version determinism.
        ulong z = seed ^ 0x9E3779B97F4A7C15UL ^ 0xD3B745F1A9C28E41UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        int initial = (int)(z & 1UL);
        return actFloor >= floorThreshold ? (1 - initial) : initial;
    }

    internal static void SyncWithCurrentRun(NRunMusicController? controller)
    {
        if (NonInteractiveMode.IsActive)
            return;

        Callable.From(() =>
        {
            try
            {
                SyncOnMainThread(controller ?? NRunMusicController.Instance);
            }
            catch (Exception ex)
            {
                Log.Warn($"[DepthsActMusicController] Sync error: {ex.Message}");
            }
        }).CallDeferred();
    }

    internal static void NotifyCustomMusicStarted(string? customMusic)
    {
        if (NonInteractiveMode.IsActive || string.IsNullOrEmpty(customMusic))
            return;

        bool isModTrack = CustomMusicPlayPatch.IsModMusicTrack(customMusic);
        if (isModTrack && !ModMusicPolicy.Enabled)
            return;

        _customEventMusicActive = true;
        SyncWithCurrentRun(NRunMusicController.Instance);
    }

    internal static void NotifyCustomMusicStopped()
    {
        _customEventMusicActive = false;
        SyncWithCurrentRun(NRunMusicController.Instance);
    }

    internal static void NotifyEliteSecondPhase()
    {
        _eliteSecondPhase = true;
        SyncWithCurrentRun(NRunMusicController.Instance);
    }

    internal static void NotifyRunMusicStopped()
    {
        _customEventMusicActive = false;
        _eliteSecondPhase = false;
        if (NonInteractiveMode.IsActive)
            return;

        Callable.From(() =>
        {
            try
            {
                _instance?.StopAllImmediate();
            }
            catch (Exception ex)
            {
                Log.Warn($"[DepthsActMusicController] StopAll error: {ex.Message}");
            }
        }).CallDeferred();
    }

    private static DepthsActMusicController? EnsureInstance()
    {
        if (_instance != null && IsInstanceValid(_instance) && _instance.IsInsideTree())
            return _instance;

        if (Engine.GetMainLoop() is not SceneTree { Root: { } root } || !IsInstanceValid(root))
            return null;

        var existing = root.GetNodeOrNull<DepthsActMusicController>("ThingsDepthsActMusicController");
        if (existing != null && IsInstanceValid(existing))
        {
            _instance = existing;
            _instance.EnsureBusesAndPlayers();
            return _instance;
        }

        var created = new DepthsActMusicController { Name = "ThingsDepthsActMusicController" };
        root.AddChild(created);
        _instance = created;
        created.EnsureBusesAndPlayers();
        return created;
    }

    private void EnsureBusesAndPlayers()
    {
        _exploreFilter ??= EnsureLowPassBus(ExploreBusName);
        _combatFilter ??= EnsureLowPassBus(CombatBusName);

        if (_explorePlayer == null || !IsInstanceValid(_explorePlayer))
        {
            _explorePlayer = new AudioStreamPlayer
            {
                Name = "DepthsExploreDeck",
                Bus = ExploreBusName,
                VolumeLinear = 0f,
                ProcessMode = ProcessModeEnum.Always,
            };
            AddChild(_explorePlayer);
            _explorePlayer.Finished += () =>
            {
                if (_explorePlayer != null && IsInstanceValid(_explorePlayer) && _targetExploreGain > 0.001f)
                    _explorePlayer.Play();
            };
        }

        if (_combatPlayer == null || !IsInstanceValid(_combatPlayer))
        {
            _combatPlayer = new AudioStreamPlayer
            {
                Name = "DepthsCombatDeck",
                Bus = CombatBusName,
                VolumeLinear = 0f,
                ProcessMode = ProcessModeEnum.Always,
            };
            AddChild(_combatPlayer);
            _combatPlayer.Finished += () =>
            {
                if (_combatPlayer != null && IsInstanceValid(_combatPlayer) && _targetCombatGain > 0.001f)
                    _combatPlayer.Play();
            };
        }
    }

    private static AudioEffectLowPassFilter EnsureLowPassBus(string busName)
    {
        int idx = AudioServer.GetBusIndex(busName);
        if (idx < 0)
        {
            idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, busName);
            AudioServer.SetBusSend(idx, "Master");
            AudioServer.SetBusVolumeDb(idx, 0f);
        }

        for (int i = 0; i < AudioServer.GetBusEffectCount(idx); i++)
        {
            if (AudioServer.GetBusEffect(idx, i) is AudioEffectLowPassFilter existing)
                return existing;
        }

        var lpf = new AudioEffectLowPassFilter
        {
            CutoffHz = MaxCutoffHz,
            Resonance = 0.36f,
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        };
        AudioServer.AddBusEffect(idx, lpf);
        return lpf;
    }

    private static void SyncOnMainThread(NRunMusicController? controller)
    {
        IRunState? runState = RunManager.Instance.DebugOnlyGetState();
        bool inDepths = runState?.Act is ThingsDepths;

        if (!inDepths || !ModMusicPolicy.Enabled)
        {
            if (_instance != null && IsInstanceValid(_instance))
            {
                _instance.TransitionToInactive(
                    controller,
                    restoreNativeActMusic: inDepths && !ModMusicPolicy.Enabled,
                    runState);
            }
            return;
        }

        DepthsActMusicController? inst = EnsureInstance();
        inst?.ApplyRunState(controller, runState!);
    }

    private void TransitionToInactive(
        NRunMusicController? controller,
        bool restoreNativeActMusic,
        IRunState? runState)
    {
        _currentStage = Stage.Inactive;
        _targetExploreGain = 0f;
        _targetCombatGain = 0f;
        _fadeSpeedPerSec = 2.0f;

        if (restoreNativeActMusic && _fmodActSuppressed)
        {
            int progress = ComputeNativeProgressIndex(runState?.CurrentRoom);
            RestoreFmodActMusic(controller, progress);
        }
        else if (!restoreNativeActMusic)
        {
            _fmodActSuppressed = false;
        }
    }

    private void StopAllImmediate()
    {
        _currentStage = Stage.Inactive;
        _fmodActSuppressed = false;
        _curExploreGain = _targetExploreGain = 0f;
        _curCombatGain = _targetCombatGain = 0f;
        _curExploreCutoffHz = _targetExploreCutoffHz = MaxCutoffHz;
        _curCombatCutoffHz = _targetCombatCutoffHz = MaxCutoffHz;

        if (_explorePlayer != null && IsInstanceValid(_explorePlayer))
        {
            _explorePlayer.VolumeLinear = 0f;
            if (_explorePlayer.Playing)
                _explorePlayer.Stop();
            _explorePlayer.StreamPaused = false;
        }

        if (_combatPlayer != null && IsInstanceValid(_combatPlayer))
        {
            _combatPlayer.VolumeLinear = 0f;
            if (_combatPlayer.Playing)
                _combatPlayer.Stop();
            _combatPlayer.StreamPaused = false;
        }
    }

    private void ApplyRunState(NRunMusicController? controller, IRunState state)
    {
        EnsureBusesAndPlayers();

        AbstractRoom? room = state.CurrentRoom;
        if (room is not CombatRoom { RoomType: RoomType.Elite })
            _eliteSecondPhase = false;

        Stage desired = ResolveStage(room);
        int floorThreshold = Sts2VersionSeam.IsBlitz(state)
            ? BlitzLowerDepthsFloorThreshold
            : LowerDepthsFloorThreshold;
        int desiredSuite = ResolveTargetSuiteIndex(state.Rng.Seed, state.ActFloor, floorThreshold);

        switch (desired)
        {
            case Stage.YieldToCustom:
                // Boss or custom-music event owns playback; fade our WAV decks out without touching FMOD Proxy.
                _targetExploreGain = 0f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 1.8f;
                break;

            case Stage.DelegateFmodMerchant:
            {
                _targetExploreGain = 0f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 1.0f;
                int shopProgress = (NMapScreen.Instance != null && NMapScreen.Instance.IsVisible()) ? 9 : 2;
                RestoreFmodActMusic(controller, shopProgress);
                break;
            }

            case Stage.DelegateFmodElite:
            {
                _targetExploreGain = 0f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 1.35f;
                int eliteProgress = _eliteSecondPhase ? 8 : 6;
                RestoreFmodActMusic(controller, eliteProgress);
                break;
            }

            case Stage.ExploreInit:
                SuppressFmodActMusic(controller);
                EnsureExplorePlaying(desiredSuite, restartIfNewSuite: true);
                _targetExploreGain = 0.90f;
                _targetExploreCutoffHz = 16500f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 0.70f; // ~1.4s fade
                break;

            case Stage.ExploreUnknown:
                SuppressFmodActMusic(controller);
                EnsureExplorePlaying(desiredSuite, restartIfNewSuite: true);
                _targetExploreGain = 1.00f;
                _targetExploreCutoffHz = 15500f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 0.50f; // ~2.0s smooth event crossfade
                break;

            case Stage.ExploreTreasure:
                SuppressFmodActMusic(controller);
                EnsureExplorePlaying(desiredSuite, restartIfNewSuite: true);
                _targetExploreGain = 0.85f;
                _targetExploreCutoffHz = 11500f;
                _targetCombatGain = 0f;
                _fadeSpeedPerSec = 0.55f; // ~1.8s chest room transition
                break;

            case Stage.RestSilent:
                SuppressFmodActMusic(controller);
                _targetExploreGain = 0f;
                _targetExploreCutoffHz = 1800f;
                _targetCombatGain = 0f;
                _targetCombatCutoffHz = 900f;
                _fadeSpeedPerSec = 0.36f; // ~2.8s fade to campfire crackle silence
                break;

            case Stage.CombatFull:
            {
                SuppressFmodActMusic(controller);
                bool alreadyInSameCombat =
                    _currentStage == Stage.CombatFull &&
                    _combatPlayer is { Playing: true, StreamPaused: false };

                bool wasRollingMuffled =
                    _currentStage == Stage.CombatEndMuffled &&
                    _combatPlayer is { Playing: true, StreamPaused: false } &&
                    _curCombatGain > 0.02f;

                if (alreadyInSameCombat)
                {
                    // Idempotent UpdateTrack call inside the same combat: do not restart playback.
                    _combatPlayer!.StreamPaused = false;
                }
                else if (wasRollingMuffled)
                {
                    // Signature native STS2 transition: keep continuous combat timeline and sweep LPF open!
                    _combatPlayer!.StreamPaused = false;
                    _fadeSpeedPerSec = 0.95f; // ~1.05s filter & gain open
                }
                else
                {
                    EnsureCombatPlaying(desiredSuite, restartFromTop: true);
                    _curCombatCutoffHz = MaxCutoffHz;
                    _fadeSpeedPerSec = 0.80f; // ~1.25s crossfade from explore/silence into downbeat
                }

                _targetExploreGain = 0f;
                _targetCombatGain = 1.00f;
                _targetCombatCutoffHz = MaxCutoffHz;
                break;
            }

            case Stage.CombatEndMuffled:
            {
                SuppressFmodActMusic(controller);
                bool combatWasActive =
                    _combatPlayer != null &&
                    IsInstanceValid(_combatPlayer) &&
                    _combatPlayer.Playing &&
                    !_combatPlayer.StreamPaused &&
                    _curCombatGain > 0.05f;

                if (combatWasActive)
                {
                    // Keep the exact combat track rolling, ducking -6.5 dB + 920 Hz LPF for Rewards & Map!
                    _targetExploreGain = 0f;
                    _targetCombatGain = CombatEndGain;
                    _targetCombatCutoffHz = CombatEndCutoffHz;
                    _fadeSpeedPerSec = 0.55f; // ~1.8s smooth muffled transition
                }
                else
                {
                    // Post-Elite / Post-Boss loot fallback: bring in warm low-passed Explore bed.
                    EnsureExplorePlaying(desiredSuite, restartIfNewSuite: true);
                    _targetExploreGain = 0.72f;
                    _targetExploreCutoffHz = 2600f;
                    _targetCombatGain = 0f;
                    _fadeSpeedPerSec = 0.55f;
                }
                break;
            }
        }

        _currentStage = desired;
    }

    private Stage ResolveStage(AbstractRoom? room)
    {
        if (room is CombatRoom combatRoom)
        {
            EncounterModel? enc = combatRoom.Encounter;
            if (enc is { HasBgm: true })
            {
                bool isModTrack = CustomMusicPlayPatch.IsModMusicTrack(enc.CustomBgm);
                bool customActive = isModTrack ? ModMusicPolicy.Enabled : true;
                if (customActive && (CombatManager.Instance.IsInProgress || combatRoom.RoomType == RoomType.Boss))
                    return Stage.YieldToCustom;
            }

            if (_customEventMusicActive)
                return Stage.YieldToCustom;

            if (!CombatManager.Instance.IsInProgress)
                return Stage.CombatEndMuffled;

            if (combatRoom.RoomType == RoomType.Elite || combatRoom.RoomType == RoomType.Boss)
                return Stage.DelegateFmodElite;

            return Stage.CombatFull;
        }

        if (_customEventMusicActive)
            return Stage.YieldToCustom;

        if (room == null)
            return Stage.ExploreInit;

        return room.RoomType switch
        {
            RoomType.Shop => Stage.DelegateFmodMerchant,
            RoomType.RestSite => Stage.RestSilent,
            RoomType.Treasure => Stage.ExploreTreasure,
            RoomType.Monster => Stage.CombatEndMuffled,
            RoomType.Elite => Stage.CombatEndMuffled,
            RoomType.Boss => Stage.CombatEndMuffled,
            RoomType.Event => room is EventRoom er && er.CanonicalEvent is AncientEventModel
                ? Stage.ExploreInit
                : Stage.ExploreUnknown,
            _ => Stage.ExploreInit,
        };
    }

    private static int ComputeNativeProgressIndex(AbstractRoom? room)
    {
        if (room == null)
            return 0;
        if (room.RoomType.IsCombatRoom() && !CombatManager.Instance.IsInProgress)
            return 7;
        return room.RoomType switch
        {
            RoomType.Shop => (NMapScreen.Instance != null && NMapScreen.Instance.IsVisible()) ? 9 : 2,
            RoomType.RestSite => 3,
            RoomType.Treasure => 5,
            RoomType.Monster => 1,
            RoomType.Event => room is EventRoom er && er.CanonicalEvent is AncientEventModel ? 0 : 4,
            RoomType.Elite => _eliteSecondPhase ? 8 : 6,
            RoomType.Boss => 6,
            _ => 0,
        };
    }

    private void EnsureExplorePlaying(int suiteIndex, bool restartIfNewSuite)
    {
        if (_explorePlayer == null || !IsInstanceValid(_explorePlayer))
            return;

        int clamped = Math.Clamp(suiteIndex, 0, Suites.Length - 1);
        bool canSwitchSuite = _curExploreGain <= 0.02f || !_explorePlayer.Playing || _explorePlayer.StreamPaused;
        if (_loadedExploreSuite != clamped && (canSwitchSuite || _explorePlayer.Stream == null))
        {
            AudioStream? stream = NativeSfxPlayer.LoadLoopingMusicStream(Suites[clamped].Explore);
            if (stream != null)
            {
                _explorePlayer.Stream = stream;
                _loadedExploreSuite = clamped;
                _explorePlayer.StreamPaused = false;
                _explorePlayer.Play(0f);
                return;
            }
        }

        if (_explorePlayer.Stream == null)
            return;

        if (_explorePlayer.StreamPaused)
        {
            _explorePlayer.StreamPaused = false;
            if (!_explorePlayer.Playing)
                _explorePlayer.Play();
        }
        else if (!_explorePlayer.Playing)
        {
            _explorePlayer.Play(restartIfNewSuite ? 0f : _explorePlayer.GetPlaybackPosition());
        }
    }

    private void EnsureCombatPlaying(int suiteIndex, bool restartFromTop)
    {
        if (_combatPlayer == null || !IsInstanceValid(_combatPlayer))
            return;

        int clamped = Math.Clamp(suiteIndex, 0, Suites.Length - 1);
        bool canSwitchSuite = _curCombatGain <= 0.02f || !_combatPlayer.Playing || _combatPlayer.StreamPaused;
        if ((_loadedCombatSuite != clamped && canSwitchSuite) || _combatPlayer.Stream == null)
        {
            AudioStream? stream = NativeSfxPlayer.LoadLoopingMusicStream(Suites[clamped].Combat);
            if (stream != null)
            {
                _combatPlayer.Stream = stream;
                _loadedCombatSuite = clamped;
                _combatPlayer.StreamPaused = false;
                _combatPlayer.Play(0f);
                return;
            }
        }

        _combatPlayer.StreamPaused = false;
        if (restartFromTop || !_combatPlayer.Playing)
            _combatPlayer.Play(0f);
    }

    private void SuppressFmodActMusic(NRunMusicController? controller)
    {
        Node? proxy = controller?.GetNodeOrNull<Node>("Proxy");
        if (proxy == null || !IsInstanceValid(proxy))
            return;

        // Only stop _musicEv if it is currently instantiated; leaves _ambienceEv and loaded banks intact.
        Variant ev = proxy.Get("_musicEv");
        if (ev.VariantType != Variant.Type.Nil && ev.AsGodotObject() != null)
            proxy.Call("stop_music");

        _fmodActSuppressed = true;
    }

    private void RestoreFmodActMusic(NRunMusicController? controller, int progressIndex)
    {
        Node? proxy = controller?.GetNodeOrNull<Node>("Proxy");
        if (proxy == null || !IsInstanceValid(proxy))
            return;

        Variant ev = proxy.Get("_musicEv");
        bool hasActiveEvent = ev.VariantType != Variant.Type.Nil && ev.AsGodotObject() != null;
        if (!hasActiveEvent)
        {
            string track = proxy.Get("_currentTrack").AsString();
            if (!string.IsNullOrEmpty(track))
                proxy.Call("update_music", track);
        }

        proxy.Call("update_global_parameter", "Progress", progressIndex);
        _fmodActSuppressed = false;
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic))]
internal static class DepthsMusicUpdateActPatch
{
    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        DepthsActMusicController.NotifyCustomMusicStopped();
        DepthsActMusicController.SyncWithCurrentRun(__instance);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new Type[] { })]
internal static class DepthsMusicUpdateTrackPatch
{
    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        DepthsActMusicController.SyncWithCurrentRun(__instance);
    }
}

#if STS2_V113_1
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new[] { typeof(RoomType) })]
internal static class DepthsMusicUpdateTrackByRoomTypePatch
{
    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        DepthsActMusicController.SyncWithCurrentRun(__instance);
    }
}
#endif

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
internal static class DepthsMusicYieldOnCustomPatch
{
    [HarmonyPostfix]
    private static void Postfix(string customMusic)
    {
        DepthsActMusicController.NotifyCustomMusicStarted(customMusic);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
internal static class DepthsMusicResumeAfterCustomPatch
{
    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        DepthsActMusicController.NotifyCustomMusicStopped();
        DepthsActMusicController.SyncWithCurrentRun(__instance);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.ToggleMerchantTrack))]
internal static class DepthsMusicMerchantTogglePatch
{
    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        DepthsActMusicController.SyncWithCurrentRun(__instance);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.TriggerEliteSecondPhase))]
internal static class DepthsMusicElitePhasePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        DepthsActMusicController.NotifyEliteSecondPhase();
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
internal static class DepthsMusicStopPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        DepthsActMusicController.NotifyRunMusicStopped();
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController._ExitTree))]
internal static class DepthsMusicExitTreePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        DepthsActMusicController.NotifyRunMusicStopped();
    }
}
