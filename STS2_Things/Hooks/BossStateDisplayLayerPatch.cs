using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Hooks;

/// <summary>
/// Elevates creature HP bars, power strips (<c>%HealthBar</c>), and enemy intents (<c>%Intents</c>) to
/// relative <c>ZIndex = 4</c> (effective <c>-6</c> inside <c>CombatSceneContainer</c>'s <c>-10</c>)
/// for standard <c>STS2_Things</c> monsters, minions, and players inside standard <c>STS2_Things</c> encounters.
///
/// Effective <c>-6</c> renders strictly above both creature <c>Visuals</c> (effective <c>-10</c>) and
/// <see cref="NThingsCombatBackground"/> <c>Foreground</c> props (<c>ZIndex = 3</c>, effective <c>-7</c>)
/// so neither swarm minions nor foreground rocks/coral/vines ever occlude HP bars or powers, while still
/// staying strictly below <c>CombatUi</c> and <c>GlobalUi</c> overlays (absolute <c>0</c>). Monumental
/// proxy bosses (absolute <c>ZIndex = 0</c>) and <see cref="ThingsHumanFaceColumn"/> (custom side-UI Z)
/// are intentionally skipped.
/// </summary>
[HarmonyPatch(typeof(NCreature), "UpdateBounds", typeof(Node))]
internal static class BossStateDisplayLayerPatch
{
    private const int RelativeHudZIndex = 4;

    private static bool IsMonumentalBossEncounter(EncounterModel? encounter) =>
        encounter is ThingsCaveGodBossEncounter
            or ThingsHumanFaceTowerBossEncounter
            or ThingsAbyssalInkmawBossEncounter;

    [HarmonyPostfix]
    private static void Postfix(NCreature __instance)
    {
        if (__instance == null || !GodotObject.IsInstanceValid(__instance))
            return;

        if (!__instance.ZAsRelative)
            return;

        if (__instance.Entity == null)
            return;

        Assembly modAssembly = typeof(STS2_ThingsInit).Assembly;
        EncounterModel? encounter = __instance.Entity.CombatState?.Encounter;
        bool isModEncounter = encounter?.GetType().Assembly == modAssembly;

        if (__instance.Entity is { IsEnemy: true, Monster: { } monster })
        {
            if (monster is ThingsHumanFaceColumn)
                return;

            if (__instance.Visuals is NCaveGodHandCreatureVisuals or NHumanFaceTowerFaceVisuals or NAbyssalInkmawProxyVisuals)
                return;

            bool isModMonster = monster.GetType().Assembly == modAssembly;
            if (!isModMonster && !isModEncounter)
                return;
        }
        else if (__instance.Entity.IsPlayer)
        {
            if (!isModEncounter || IsMonumentalBossEncounter(encounter))
                return;
        }
        else
        {
            return;
        }

        NCreatureStateDisplay? stateDisplay = __instance.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
        if (stateDisplay != null && GodotObject.IsInstanceValid(stateDisplay) && stateDisplay.ZAsRelative)
        {
            stateDisplay.ZAsRelative = true;
            stateDisplay.ZIndex = RelativeHudZIndex;
        }

        if (__instance.Entity.IsEnemy)
        {
            Control? intentContainer = __instance.IntentContainer ?? __instance.GetNodeOrNull<Control>("%Intents");
            if (intentContainer != null && GodotObject.IsInstanceValid(intentContainer) && intentContainer.ZAsRelative)
            {
                intentContainer.ZAsRelative = true;
                intentContainer.ZIndex = RelativeHudZIndex;
            }
        }
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.ScaleTo), typeof(float), typeof(double))]
internal static class ScaleBeetleIntentSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCreature __instance, float size, double duration)
    {
        Powers.ThingsScaleUpPower.SyncIntentPositionForNode(__instance, size, duration);
    }
}
