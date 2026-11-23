using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Acts;

namespace STS2_Things.Audio;

/// <summary>
/// Routes mod-owned res:// music through Godot. The native controller requests
/// the custom track while combat is still being assembled, so actual playback
/// waits for its post-setup track update to avoid room-transition cleanup.
/// Playback fades out when combat ends and hard-stops on room exit, act change,
/// or run music shutdown.
/// </summary>
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
internal static class CustomMusicPlayPatch
{
    internal static float CombatEndFadeOutSeconds { get; set; } = 1.5f;

    internal const string GravetideTheme =
        "res://music/gravetide_slug/gravetide_slug_boss_theme.wav";

    internal const string HumanFaceTowerTheme =
        "res://music/human_face_tower/human_face_tower_boss_theme.wav";

    internal const string OriginFogmogTheme =
        "res://music/origin_fogmog/origin_fogmog_boss_theme.wav";

    internal const string ScaleBeetleTheme =
        "res://music/scale_beetle/scale_beetle_boss_theme.wav";

    internal const string CaveGodTheme =
        "res://music/cave_god/cave_god_boss_theme.wav";

    internal const string BowlbugProgenitorTheme =
        "res://music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav";

    internal const string AbyssalInkmawTheme =
        "res://music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav";

    private static bool _startAfterCombatSetup;
    private static string? _pendingTrack;
    private static WeakReference<CombatState>? _pendingCombatState;

    internal static string? PendingTrack => _pendingTrack;
    internal static bool SuppressedNativeTrackForModMusic { get; set; }

    internal static bool TryGetAuthoredVolumeDb(string? customMusic, out float volumeDb)
    {
        if (string.Equals(customMusic, GravetideTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, HumanFaceTowerTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, OriginFogmogTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, ScaleBeetleTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, CaveGodTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, BowlbugProgenitorTheme, StringComparison.Ordinal) ||
            string.Equals(customMusic, AbyssalInkmawTheme, StringComparison.Ordinal))
        {
            // Dedicated Boss WAVs are mastered uniformly to -20.0 dBFS RMS; -4.5 dB aligns
            // playback loudness to ~-24.5 dBFS so combat SFX remain crisp over each Boss theme.
            volumeDb = -4.5f;
            return true;
        }

        volumeDb = 0f;
        return false;
    }

    internal static bool IsModMusicTrack(string? customMusic)
        => TryGetAuthoredVolumeDb(customMusic, out _);

    internal static bool StartAfterCombatSetup
    {
        get => _startAfterCombatSetup;
        set
        {
            _startAfterCombatSetup = value;
            if (!value)
            {
                _pendingTrack = null;
                _pendingCombatState = null;
            }
            else
            {
                CombatState? current = CombatManager.Instance.DebugOnlyGetState();
                _pendingCombatState = current != null ? new WeakReference<CombatState>(current) : null;
            }
        }
    }

    internal static bool IsPendingForActiveCombat()
    {
        if (!_startAfterCombatSetup || string.IsNullOrEmpty(_pendingTrack) || !CombatManager.Instance.IsInProgress)
            return false;
        if (_pendingCombatState == null)
            return true;
        CombatState? active = CombatManager.Instance.DebugOnlyGetState();
        return active != null &&
               _pendingCombatState.TryGetTarget(out CombatState? expected) &&
               ReferenceEquals(expected, active);
    }

    internal static void StopModMusicImmediate(bool clearSuppressedNativeTrack, bool stopSequentialLoop = false)
    {
        StartAfterCombatSetup = false;
        if (clearSuppressedNativeTrack)
            SuppressedNativeTrackForModMusic = false;
        if (stopSequentialLoop)
            NativeSfxPlayer.StopSequentialLoop();
        NativeSfxPlayer.StopMusic();
    }

    [HarmonyPrefix]
    private static bool Prefix(NRunMusicController __instance, string customMusic)
    {
        try
        {
            if (!IsModMusicTrack(customMusic))
            {
                StopModMusicImmediate(clearSuppressedNativeTrack: true);
                return true;
            }

            if (!ModMusicPolicy.Enabled)
            {
                StopModMusicImmediate(clearSuppressedNativeTrack: true);
                return false;
            }

            _pendingTrack = customMusic;
            StartAfterCombatSetup = true;
            SuppressedNativeTrackForModMusic = true;
            __instance.GetNodeOrNull<Node>("Proxy")?.Call("stop_music");
            NativeSfxPlayer.StopMusic();
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"[CustomMusicPlayPatch] Prefix error: {ex.Message}");
            return true;
        }
    }
}

#if STS2_V113_1
[HarmonyPatch(
    typeof(NRunMusicController),
    nameof(NRunMusicController.UpdateTrack),
    new[] { typeof(RoomType) })]
#else
[HarmonyPatch(
    typeof(NRunMusicController),
    nameof(NRunMusicController.UpdateTrack),
    new Type[] { })]
#endif
internal static class CustomMusicStartAfterCombatSetupPatch
{
    [HarmonyPrefix]
    private static void Prefix(NRunMusicController __instance)
    {
        try
        {
            if (!CustomMusicPlayPatch.SuppressedNativeTrackForModMusic ||
                CustomMusicPlayPatch.StartAfterCombatSetup ||
                NativeSfxPlayer.ActiveMusicPath != null)
                return;

            IRunState? runState = RunManager.Instance.DebugOnlyGetState();
            AbstractRoom? room = runState?.CurrentRoom;
            if (room == null)
                return;

            if (room.IsVictoryRoom)
            {
                // The Architect victory room stays silent like vanilla.
                CustomMusicPlayPatch.SuppressedNativeTrackForModMusic = false;
                NativeSfxPlayer.StopMusic();
                return;
            }

            if (room is MapRoom || room is CombatRoom { Encounter.HasBgm: true })
                return;

            // Leaving a mod-BGM boss room into another room in the same Act: restore the native Act track
            // before UpdateTrack applies the new room's Progress parameter.
            CustomMusicPlayPatch.SuppressedNativeTrackForModMusic = false;
            NativeSfxPlayer.StopMusic();
            if (!(runState!.Act is ThingsDepths && ModMusicPolicy.Enabled))
                __instance.StopCustomMusic();
        }
        catch (Exception ex)
        {
            Log.Warn($"[CustomMusicStartAfterCombatSetupPatch] Prefix error: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(NRunMusicController __instance)
    {
        try
        {
            if (!ModMusicPolicy.Enabled)
            {
                CustomMusicPlayPatch.StartAfterCombatSetup = false;
                if (CustomMusicPlayPatch.IsModMusicTrack(NativeSfxPlayer.ActiveMusicPath))
                    NativeSfxPlayer.StopMusic();
                return;
            }

            if (!CustomMusicPlayPatch.IsPendingForActiveCombat())
            {
                if (CustomMusicPlayPatch.StartAfterCombatSetup && !CombatManager.Instance.IsInProgress)
                    CustomMusicPlayPatch.StartAfterCombatSetup = false;

                if (!CombatManager.Instance.IsInProgress &&
                    CustomMusicPlayPatch.IsModMusicTrack(NativeSfxPlayer.ActiveMusicPath))
                {
                    NativeSfxPlayer.FadeOutAndStopMusic(CustomMusicPlayPatch.CombatEndFadeOutSeconds);
                    DepthsActMusicController.NotifyCustomMusicStopped(__instance);
                }
                return;
            }

            string? pendingTrack = CustomMusicPlayPatch.PendingTrack;
            CustomMusicPlayPatch.StartAfterCombatSetup = false;
            if (CustomMusicPlayPatch.TryGetAuthoredVolumeDb(pendingTrack, out float volumeDb))
            {
                NativeSfxPlayer.PlayMusic(pendingTrack!, "Master", volumeDb);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[CustomMusicStartAfterCombatSetupPatch] Postfix error: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
internal static class CustomMusicStopCustomPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CustomMusicPlayPatch.StopModMusicImmediate(clearSuppressedNativeTrack: true);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
internal static class CustomMusicStopPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CustomMusicPlayPatch.StopModMusicImmediate(clearSuppressedNativeTrack: true, stopSequentialLoop: true);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController._ExitTree))]
internal static class CustomMusicExitTreePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CustomMusicPlayPatch.StopModMusicImmediate(clearSuppressedNativeTrack: true, stopSequentialLoop: true);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic))]
internal static class CustomMusicUpdateActPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CustomMusicPlayPatch.StopModMusicImmediate(clearSuppressedNativeTrack: true);
    }
}
