using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using STS2_Things.Config;
using STS2_Things.Events;

namespace STS2_Things.Hooks;

/// <summary>
/// Event catalog grouped by Act and gated by <see cref="ThingsModConfig"/>,
/// sorted deterministically so host and client pools remain identical.
/// </summary>
internal static class ThingsEventCatalog
{
    public static IEnumerable<EventModel> AddOvergrowthAndUnderdocksEvents(IEnumerable<EventModel> source)
    {
        return Add(source,
            (ThingsModConfig.EventRobberyFakeMerchantEnabled, ModelDb.Event<ThingsRobberyFakeMerchant>()),
            (ThingsModConfig.EventBackroomsEnabled, ModelDb.Event<ThingsBackrooms>()),
            (ThingsModConfig.EventCuttingItCloseEnabled, ModelDb.Event<ThingsCuttingItClose>()),
            (ThingsModConfig.EventBlindCrabInkwellEnabled, ModelDb.Event<ThingsBlindCrabInkwell>()));
    }

    public static IEnumerable<EventModel> AddOvergrowthEvents(IEnumerable<EventModel> source)
    {
        return Add(AddOvergrowthAndUnderdocksEvents(source),
            (ThingsModConfig.EventMossRuneLoomEnabled, ModelDb.Event<ThingsMossRuneLoom>()),
            (ThingsModConfig.EventForestPlinkoEnabled, ModelDb.Event<ThingsForestPlinko>()),
            (ThingsModConfig.EventLendingVineEnabled, ModelDb.Event<ThingsLendingVine>()));
    }

    public static IEnumerable<EventModel> AddUnderdocksEvents(IEnumerable<EventModel> source)
    {
        return Add(AddOvergrowthAndUnderdocksEvents(source),
            (ThingsModConfig.EventBilgeMinesEnabled, ModelDb.Event<ThingsBilgeMines>()),
            (ThingsModConfig.EventClamClawEnabled, ModelDb.Event<ThingsClamClaw>()));
    }

    public static IEnumerable<EventModel> AddHiveEvents(IEnumerable<EventModel> source)
    {
        return Add(source,
            (ThingsModConfig.EventMedusaEnabled, ModelDb.Event<ThingsMedusa>()),
            (ThingsModConfig.EventWarmRoyalCellEnabled, ModelDb.Event<ThingsWarmRoyalCell>()),
            (ThingsModConfig.EventAntSortingTableEnabled, ModelDb.Event<ThingsAntSortingTable>()),
            (ThingsModConfig.EventOverstuffedBroodEnabled, ModelDb.Event<ThingsOverstuffedBrood>()),
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()));
    }

    public static IEnumerable<EventModel> AddDepthsEvents(IEnumerable<EventModel> source)
    {
        return Add(source,
            (ThingsModConfig.EventRealityAlignedHousesEnabled, ModelDb.Event<ThingsRealityAlignedHouses>()),
            (ThingsModConfig.EventShadowCloakroomEnabled, ModelDb.Event<ThingsShadowCloakroom>()),
            (ThingsModConfig.EventEchoingWellEnabled, ModelDb.Event<ThingsEchoingWell>()),
            (ThingsModConfig.EventPoliteMawEnabled, ModelDb.Event<ThingsPoliteMaw>()),
            (ThingsModConfig.EventMycelialBankEnabled, ModelDb.Event<ThingsMycelialBank>()),
            (ThingsModConfig.EventUnlitFireEnabled, ModelDb.Event<ThingsUnlitFire>()),
            (ThingsModConfig.EventRelicWorkshopEnabled, ModelDb.Event<ThingsRelicWorkshop>()),
            (ThingsModConfig.EventPotionTastingEnabled, ModelDb.Event<ThingsPotionTasting>()),
            (ThingsModConfig.EventNarrowGateEnabled, ModelDb.Event<ThingsNarrowGate>()),
            (ThingsModConfig.EventBitingChestEnabled, ModelDb.Event<ThingsBitingChest>()),
            (ThingsModConfig.EventCrowdedWardEnabled, ModelDb.Event<ThingsCrowdedWard>()),
            (ThingsModConfig.EventReturnsCounterEnabled, ModelDb.Event<ThingsReturnsCounter>()),
            (ThingsModConfig.EventHotWaxBoxEnabled, ModelDb.Event<ThingsHotWaxBox>()),
            (ThingsModConfig.EventSpliceBoothEnabled, ModelDb.Event<ThingsSpliceBooth>()),
            (ThingsModConfig.EventGildedPressEnabled, ModelDb.Event<ThingsGildedPress>()),
            (ThingsModConfig.EventCrookedCounterweightEnabled, ModelDb.Event<ThingsCrookedCounterweight>()),
            (ThingsModConfig.EventGrudgeVendingMachineEnabled, ModelDb.Event<ThingsGrudgeVendingMachine>()),
            (ThingsModConfig.EventCrackedBrazierEnabled, ModelDb.Event<ThingsCrackedBrazier>()),
            (ThingsModConfig.EventSiltWinchEnabled, ModelDb.Event<ThingsSiltWinch>()),
            (ThingsModConfig.EventBronzeCauldronEnabled, ModelDb.Event<ThingsBronzeCauldron>()),
            (ThingsModConfig.EventWallCrackLedgerEnabled, ModelDb.Event<ThingsWallCrackLedger>()),
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()));
    }

    public static IEnumerable<EventModel> AddGloryEvents(IEnumerable<EventModel> source)
    {
        return Add(source,
            (ThingsModConfig.EventChronometerRigEnabled, ModelDb.Event<ThingsChronometerRig>()),
            (ThingsModConfig.EventSilveredGalleryEnabled, ModelDb.Event<ThingsSilveredGallery>()),
            (ThingsModConfig.EventScalesOfVanityEnabled, ModelDb.Event<ThingsScalesOfVanity>()),
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()));
    }

    private static IEnumerable<EventModel> Add(
        IEnumerable<EventModel> source,
        params (string EnabledKey, EventModel Event)[] candidates)
    {
        return DeterministicContentOrder.SortBaseThenMods(
            source.Concat(candidates
                    .Where(candidate => ThingsModConfig.IsEnabled(candidate.EnabledKey))
                    .Select(candidate => candidate.Event))
                .Distinct());
    }
}

[HarmonyPatch(typeof(Overgrowth), "get_AllEvents")]
public static class OvergrowthAllEventsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = ThingsEventCatalog.AddOvergrowthEvents(__result);
    }
}

[HarmonyPatch(typeof(Underdocks), "get_AllEvents")]
public static class UnderdocksAllEventsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = ThingsEventCatalog.AddUnderdocksEvents(__result);
    }
}

[HarmonyPatch(typeof(Hive), "get_AllEvents")]
public static class HiveAllEventsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = ThingsEventCatalog.AddHiveEvents(__result);
    }
}

[HarmonyPatch(typeof(Glory), "get_AllEvents")]
public static class GloryAllEventsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = ThingsEventCatalog.AddGloryEvents(__result);
    }
}
