using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Hooks;

/// <summary>
/// Elevates creature HP bars, power strips (<c>%HealthBar</c>), and enemy intents (<c>%Intents</c>) up to
/// relative <c>ZIndex = 4</c> (effective <c>-6</c> inside <c>CombatSceneContainer</c>'s <c>-10</c>)
/// for standard <c>STS2_Things</c> monsters, minions, and players inside standard <c>STS2_Things</c> encounters.
///
/// Effective Z is always clamped strictly below <c>0</c> (<c>&lt;= -1</c>) by walking the live
/// <see cref="CanvasItem"/> ancestor chain so modded characters, third-party <see cref="NCreature._Ready"/>
/// or <see cref="NCombatRoom.AddCreature"/> postfixes, or reparented containers with
/// <c>ZAsRelative = false</c> / effective <c>ZIndex &gt;= 0</c> always clamp any previously elevated
/// <c>%HealthBar</c>/<c>%Intents</c> back down to <c>0</c> and never render over <c>CombatUi</c>,
/// <c>GlobalUi</c>, or ESC pause/settings overlays. Elevated HUD Z-indices are also reset to <c>0</c>
/// when combat ends. Monumental proxy bosses (absolute <c>ZIndex = 0</c>) and
/// <see cref="ThingsHumanFaceColumn"/> (custom side-UI Z) are intentionally skipped.
/// </summary>
[HarmonyPatch(typeof(NCreature), "UpdateBounds", typeof(Node))]
internal static class BossStateDisplayLayerPatch
{
    private const int RelativeHudZIndex = 4;

    private static bool IsMonumentalBossEncounter(EncounterModel? encounter) =>
        encounter is ThingsCaveGodBossEncounter
            or ThingsHumanFaceTowerBossEncounter
            or ThingsAbyssalInkmawBossEncounter;

    /// <summary>
    /// Computes the effective Godot 2D Z-index of <paramref name="item"/> by walking <see cref="Node.GetParent"/>
    /// while <see cref="CanvasItem.ZAsRelative"/> is <c>true</c>, stopping at the first node with
    /// <c>!ZAsRelative</c> or a non-<see cref="CanvasItem"/> parent.
    /// </summary>
    internal static int ComputeEffectiveZIndex(CanvasItem item)
    {
        int effective = 0;
        CanvasItem? current = item;
        while (current != null && GodotObject.IsInstanceValid(current))
        {
            effective += current.ZIndex;
            if (!current.ZAsRelative)
                break;
            current = current.GetParent() as CanvasItem;
        }

        return effective;
    }

    internal static void Apply(NCreature creature)
    {
        if (creature == null || !GodotObject.IsInstanceValid(creature))
            return;

        if (creature.Entity == null)
            return;

        if (NCombatRoom.Instance is { Mode: CombatRoomMode.FinishedCombat })
            return;

        Assembly modAssembly = typeof(STS2_ThingsInit).Assembly;
        EncounterModel? encounter = creature.Entity.CombatState?.Encounter;
        bool isModEncounter = encounter?.GetType().Assembly == modAssembly;

        if (creature.Entity is { IsEnemy: true, Monster: { } monster })
        {
            if (monster is ThingsHumanFaceColumn)
                return;

            if (creature.Visuals is NCaveGodHandCreatureVisuals or NHumanFaceTowerFaceVisuals or NAbyssalInkmawProxyVisuals)
                return;

            bool isModMonster = monster.GetType().Assembly == modAssembly;
            if (!isModMonster && !isModEncounter)
                return;
        }
        else if (creature.Entity.IsPlayer)
        {
            if (!isModEncounter || IsMonumentalBossEncounter(encounter))
                return;
        }
        else
        {
            return;
        }

        int effectiveCreatureZ = ComputeEffectiveZIndex(creature);
        int maxSafeRelativeZ = -1 - effectiveCreatureZ;

        NCreatureStateDisplay? stateDisplay = creature.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
        if (stateDisplay != null && GodotObject.IsInstanceValid(stateDisplay) && stateDisplay.ZAsRelative)
        {
            int stateParentZ = stateDisplay.GetParent() is CanvasItem sp
                ? ComputeEffectiveZIndex(sp)
                : effectiveCreatureZ;
            int safeStateZ = Math.Min(maxSafeRelativeZ, -1 - stateParentZ);
            if (safeStateZ <= 0)
            {
                if (stateDisplay.ZIndex > 0)
                    stateDisplay.ZIndex = 0;
            }
            else
            {
                stateDisplay.ZAsRelative = true;
                stateDisplay.ZIndex = Math.Min(RelativeHudZIndex, safeStateZ);
            }
        }

        if (creature.Entity.IsEnemy)
        {
            Control? intentContainer = creature.IntentContainer ?? creature.GetNodeOrNull<Control>("%Intents");
            if (intentContainer != null && GodotObject.IsInstanceValid(intentContainer) && intentContainer.ZAsRelative)
            {
                int intentParentZ = intentContainer.GetParent() is CanvasItem ip
                    ? ComputeEffectiveZIndex(ip)
                    : effectiveCreatureZ;
                int safeIntentZ = Math.Min(maxSafeRelativeZ, -1 - intentParentZ);
                if (safeIntentZ <= 0)
                {
                    if (intentContainer.ZIndex > 0)
                        intentContainer.ZIndex = 0;
                }
                else
                {
                    intentContainer.ZAsRelative = true;
                    intentContainer.ZIndex = Math.Min(RelativeHudZIndex, safeIntentZ);
                }
            }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(NCreature __instance) => Apply(__instance);
}

/// <summary>
/// Closes the mid-combat summon lifecycle race against third-party <see cref="NCreature._Ready"/> and
/// <see cref="NCombatRoom.AddCreature"/> postfixes (for example, mods that set <c>ZAsRelative = false</c>
/// or raise <c>ZIndex</c> after <c>UpdateBounds</c> already elevated <c>%HealthBar</c>/<c>%Intents</c>
/// inside <c>NCreature._Ready</c>). Running at <see cref="Priority.Last"/> clamps synchronously after
/// other <c>_Ready</c> postfixes, and a single deferred pass catches mutations applied later in the same
/// frame by <c>AddCreature</c> postfixes or <c>AfterAddedToRoom</c>.
/// </summary>
[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class BossStateDisplayCreatureReadyPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NCreature __instance)
    {
        if (__instance == null || !GodotObject.IsInstanceValid(__instance))
            return;

        BossStateDisplayLayerPatch.Apply(__instance);
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(__instance))
                BossStateDisplayLayerPatch.Apply(__instance);
        }).CallDeferred();
    }
}

/// <summary>
/// <see cref="NCombatRoom._Ready"/> spawns ally and enemy <see cref="NCreature"/> nodes BEFORE setting
/// <c>SceneContainer.ZIndex = -10</c>. Re-applying safe HUD elevation once <see cref="NCombatRoom._Ready"/>
/// completes ensures initial creatures inside a standard <c>-10</c> scene container receive safe elevation,
/// while creatures whose ancestor chain remains at effective Z <c>&gt;= -1</c> are never elevated.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
internal static class BossStateDisplayCombatRoomReadyPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NCombatRoom __instance)
    {
        if (__instance == null || !GodotObject.IsInstanceValid(__instance) || __instance.Mode != CombatRoomMode.ActiveCombat)
            return;

        foreach (NCreature creature in __instance.CreatureNodes)
        {
            BossStateDisplayLayerPatch.Apply(creature);
        }
    }
}

/// <summary>
/// Resets any elevated <c>%HealthBar</c> and <c>%Intents</c> relative Z-index back to <c>0</c> when combat
/// ends so creature HUD overlays are strictly back at vanilla Z during post-combat reward screens.
/// </summary>
[HarmonyPatch(typeof(NCreature), "OnCombatEnded", typeof(CombatRoom))]
internal static class BossStateDisplayCombatEndResetPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCreature __instance)
    {
        if (__instance == null || !GodotObject.IsInstanceValid(__instance))
            return;

        NCreatureStateDisplay? stateDisplay = __instance.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
        if (stateDisplay != null && GodotObject.IsInstanceValid(stateDisplay) && stateDisplay.ZAsRelative && stateDisplay.ZIndex > 0)
        {
            stateDisplay.ZIndex = 0;
        }

        Control? intentContainer = __instance.IntentContainer ?? __instance.GetNodeOrNull<Control>("%Intents");
        if (intentContainer != null && GodotObject.IsInstanceValid(intentContainer) && intentContainer.ZAsRelative && intentContainer.ZIndex > 0)
        {
            intentContainer.ZIndex = 0;
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
