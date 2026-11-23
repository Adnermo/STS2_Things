using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Hooks;

/// <summary>
/// Routes the three monumental background-driven Act 2 Boss encounters (14 Bestiary entries)
/// away from vanilla <see cref="NBestiaryLayoutDefault"/> — whose creature_visuals scenes are
/// invisible targeting/HUD proxies — to dedicated Spine-backed <see cref="NBestiaryLayout"/>
/// presenters matching vanilla's <c>KaiserCrabBoss</c> / <c>DecimillipedeElite</c> architecture.
/// </summary>
[HarmonyPatch(typeof(BestiaryEntry))]
internal static class BestiaryBossLayoutPatch
{
    private static bool IsCaveGodEntry(BestiaryEntry entry) =>
        entry.encounterModel is ThingsCaveGodBossEncounter
        || entry.monsterModel is ThingsCaveGodBody or ThingsCaveGodHand or ThingsCaveGodCaptiveClaw;

    private static bool IsAbyssalInkmawEntry(BestiaryEntry entry) =>
        entry.encounterModel is ThingsAbyssalInkmawBossEncounter
        || entry.monsterModel is ThingsAbyssalInkmaw or ThingsAbyssalCrushingTentacle or ThingsAbyssalInkTentacle or ThingsAbyssalSmallTentacleBase;

    private static bool IsHumanFaceTowerEntry(BestiaryEntry entry) =>
        entry.encounterModel is ThingsHumanFaceTowerBossEncounter
        || entry.monsterModel is ThingsHumanFaceTowerFace;

    [HarmonyPatch(nameof(BestiaryEntry.CanReuseLayout))]
    [HarmonyPrefix]
    private static bool CanReuseLayoutPrefix(BestiaryEntry __instance, NBestiaryLayout? layout, ref bool __result)
    {
        if (IsCaveGodEntry(__instance))
        {
            __result = layout is NBestiaryLayoutCaveGod;
            return false;
        }

        if (IsAbyssalInkmawEntry(__instance))
        {
            __result = layout is NBestiaryLayoutAbyssalInkmaw;
            return false;
        }

        if (IsHumanFaceTowerEntry(__instance))
        {
            __result = layout is NBestiaryLayoutHumanFaceTower;
            return false;
        }

        return true;
    }

    [HarmonyPatch(nameof(BestiaryEntry.CreateLayoutNode))]
    [HarmonyPrefix]
    private static bool CreateLayoutNodePrefix(BestiaryEntry __instance, ref NBestiaryLayout? __result)
    {
        if (IsCaveGodEntry(__instance))
        {
            __result = NBestiaryLayoutCaveGod.Create();
            return false;
        }

        if (IsAbyssalInkmawEntry(__instance))
        {
            __result = NBestiaryLayoutAbyssalInkmaw.Create();
            return false;
        }

        if (IsHumanFaceTowerEntry(__instance))
        {
            __result = NBestiaryLayoutHumanFaceTower.Create();
            return false;
        }

        return true;
    }
}
