using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Visuals;

namespace STS2_Things.Hooks;

/// <summary>
/// Attaches <see cref="NThingsBackgroundViewportFitter"/> to custom STS2_Things 2D combat backgrounds
/// (Depths caves and non-Spine boss arenas) so they render at true full-frame framing rather than
/// stretched 1.25x-1.44x overscan boxes. Vanilla backgrounds, Spine-rigged bosses (Cave God,
/// Human-Faced Tower), and the vanilla Fake Merchant Spine room are untouched.
/// </summary>
[HarmonyPatch(typeof(NCombatBackground), nameof(NCombatBackground.Create))]
internal static class CombatBackgroundFramingPatch
{
    private const string FitterNodeName = "ThingsBackgroundViewportFitter";

    private static readonly Vector2 SixteenByNineBossSpan = new(2401.28f, 1350.72f);
    private static readonly Vector2 GravetideSlugSpan = new(2768.0f, 1296.0f);
    private static readonly Vector2 DepthsCaveSpan = new(2764.8f, 1296.0f);

    [HarmonyPostfix]
    private static void AfterCreate(BackgroundAssets? bg, NCombatBackground? __result)
    {
        if (bg == null || __result == null || !GodotObject.IsInstanceValid(__result))
            return;

        if (!TryResolveNominalArtworkSpan(bg.BackgroundScenePath, out Vector2 nominalSpan))
            return;

        if (__result.GetNodeOrNull<NThingsBackgroundViewportFitter>(FitterNodeName) != null)
            return;

        var fitter = new NThingsBackgroundViewportFitter { Name = FitterNodeName };
        fitter.Configure(nominalSpan);
        __result.AddChild(fitter);
    }

    internal static bool TryResolveNominalArtworkSpan(string? scenePath, out Vector2 nominalSpan)
    {
        nominalSpan = default;
        if (string.IsNullOrEmpty(scenePath))
            return false;

        if (scenePath.Contains("/things_cave_god_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_human_face_tower_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_abyssal_inkmaw_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_robbery_fake_merchant_encounter/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (scenePath.Contains("/things_bowlbug_progenitor_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_origin_fogmog_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_scale_beetle_boss_encounter/", StringComparison.OrdinalIgnoreCase) ||
            scenePath.Contains("/things_the_legacy_boss_encounter/", StringComparison.OrdinalIgnoreCase))
        {
            nominalSpan = SixteenByNineBossSpan;
            return true;
        }

        if (scenePath.Contains("/things_gravetide_slug_boss_encounter/", StringComparison.OrdinalIgnoreCase))
        {
            nominalSpan = GravetideSlugSpan;
            return true;
        }

        if (scenePath.StartsWith("res://scenes/backgrounds/things_", StringComparison.OrdinalIgnoreCase) ||
            scenePath.StartsWith("res://scenes/backgrounds/cave_", StringComparison.OrdinalIgnoreCase) ||
            scenePath.StartsWith("res://scenes/backgrounds/hollow_grotto", StringComparison.OrdinalIgnoreCase) ||
            scenePath.StartsWith("res://scenes/backgrounds/sunken_bell_vault/", StringComparison.OrdinalIgnoreCase))
        {
            nominalSpan = DepthsCaveSpan;
            return true;
        }

        return false;
    }
}
