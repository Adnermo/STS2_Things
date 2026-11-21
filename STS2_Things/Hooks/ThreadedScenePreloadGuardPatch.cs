using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;

namespace STS2_Things.Hooks;

/// <summary>
/// Routes <c>STS2_Things</c> combat/creature/background <c>.tscn</c> scenes through
/// <see cref="AssetLoadingSession"/>'s sequential <c>_vfxScenes</c> queue so Godot 4.5.1's
/// threaded <see cref="ResourceLoader"/> never loads multiple mod scenes sharing external
/// resources (C# scripts, Spine skeleton data, materials, or textures) concurrently on
/// <see cref="WorkerThreadPool"/> threads.
/// </summary>
/// <remarks>
/// Patches the <see cref="AssetLoadingSession"/> constructor (rather than <c>IsVfxScene</c>)
/// so .NET JIT inlining of <c>IsVfxScene</c> during startup preloads can never bypass the guard.
/// Vanilla scenes are left completely untouched.
/// </remarks>
[HarmonyPatch]
internal static class ThreadedScenePreloadGuardPatch
{
    private const string CreatureVisualsPrefix = "res://scenes/creature_visuals/";
    private const string BackgroundsPrefix = "res://scenes/backgrounds/";
    private const string EncountersPrefix = "res://scenes/encounters/";

    private static readonly FieldInfo? ToLoadField = AccessTools.Field(typeof(AssetLoadingSession), "_toLoad");
    private static readonly FieldInfo? VfxScenesField = AccessTools.Field(typeof(AssetLoadingSession), "_vfxScenes");

    private static readonly HashSet<string> ModCreatureSceneFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "bowlbug_progenitor.tscn",
        "cave_god_boss_setup.tscn",
        "cave_maw.tscn",
        "chromatic_slime_large.tscn",
        "chromatic_slime_medium.tscn",
        "chromatic_slime_small.tscn",
        "crystal_snail.tscn",
        "fleeting_echo.tscn",
        "gravetide_slug.tscn",
        "gravetide_slug_corpse.tscn",
        "great_silk_moth.tscn",
        "human_face_column.tscn",
        "lantern_fish.tscn",
        "leech_mother.tscn",
        "mycorrhizal_bulwark.tscn",
        "mycorrhizal_vanguard.tscn",
        "origin_eye_with_teeth.tscn",
        "origin_fogmog.tscn",
        "quirky_hopper.tscn",
        "radio_jellyfish.tscn",
        "reverse_salamander.tscn",
        "rock_snail.tscn",
        "sanguine_leech.tscn",
        "scale_peeler.tscn",
        "silk_moth.tscn",
        "slime_snail.tscn",
        "soul_roe.tscn",
        "soul_roe_2.tscn",
        "soul_roe_3.tscn",
        "soul_roes.tscn",
        "water_sponge.tscn",
    };

    private static readonly string[] ModBackgroundDirPrefixes =
    [
        "things_",
        "cave_quartz/",
        "cave_riverbend/",
        "cave_rootfungus/",
        "cave_skylight/",
        "hollow_grotto/",
        "hollow_grotto_ember/",
        "hollow_grotto_moss/",
        "hollow_grotto_violet/",
        "sunken_bell_vault/",
    ];

    internal static bool Prepare() => ResolveTarget() != null && ToLoadField != null && VfxScenesField != null;

    internal static MethodBase? TargetMethod() => ResolveTarget();

    private static ConstructorInfo? ResolveTarget() => AccessTools.Constructor(
        typeof(AssetLoadingSession),
        [typeof(string), typeof(IEnumerable<string>), typeof(ConcurrentDictionary<string, Resource>), typeof(AssetCache)]);

    internal static bool ShouldSerializeScene(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.StartsWith(CreatureVisualsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string file = path[CreatureVisualsPrefix.Length..];
            return file.StartsWith("things_", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("chromatic_slime_", StringComparison.OrdinalIgnoreCase)
                || ModCreatureSceneFiles.Contains(file);
        }

        if (path.StartsWith(BackgroundsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> rest = path.AsSpan(BackgroundsPrefix.Length);
            foreach (string prefix in ModBackgroundDirPrefixes)
            {
                if (rest.StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        if (path.StartsWith(EncountersPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path.AsSpan(EncountersPrefix.Length)
                .StartsWith("things_".AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void Postfix(AssetLoadingSession __instance)
    {
        if (ToLoadField?.GetValue(__instance) is not Queue<string> toLoad
            || VfxScenesField?.GetValue(__instance) is not Queue<string> vfxScenes
            || toLoad.Count == 0)
        {
            return;
        }

        int count = toLoad.Count;
        for (int i = 0; i < count; i++)
        {
            string path = toLoad.Dequeue();
            if (ShouldSerializeScene(path))
                vfxScenes.Enqueue(path);
            else
                toLoad.Enqueue(path);
        }
    }
}
