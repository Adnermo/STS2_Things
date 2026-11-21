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
    private sealed class CachedEventCatalog
    {
        public required IEnumerable<EventModel> SourceRef { get; init; }
        public required EventModel[] SourceSnapshot { get; init; }
        public required int ConfigVersion { get; init; }
        public required EventModel[] Result { get; init; }
    }

    private static (string EnabledKey, EventModel Event)[]? _sharedAct1Candidates;
    private static (string EnabledKey, EventModel Event)[]? _overgrowthCandidates;
    private static (string EnabledKey, EventModel Event)[]? _underdocksCandidates;
    private static (string EnabledKey, EventModel Event)[]? _hiveCandidates;
    private static (string EnabledKey, EventModel Event)[]? _depthsCandidates;
    private static (string EnabledKey, EventModel Event)[]? _gloryCandidates;

    private static CachedEventCatalog? _sharedAct1Cache;
    private static CachedEventCatalog? _overgrowthCache;
    private static CachedEventCatalog? _underdocksCache;
    private static CachedEventCatalog? _hiveCache;
    private static CachedEventCatalog? _depthsCache;
    private static CachedEventCatalog? _gloryCache;

    private static (string EnabledKey, EventModel Event)[] GetSharedAct1Candidates() =>
        _sharedAct1Candidates ??=
        [
            (ThingsModConfig.EventRobberyFakeMerchantEnabled, ModelDb.Event<ThingsRobberyFakeMerchant>()),
            (ThingsModConfig.EventBackroomsEnabled, ModelDb.Event<ThingsBackrooms>()),
            (ThingsModConfig.EventCuttingItCloseEnabled, ModelDb.Event<ThingsCuttingItClose>()),
            (ThingsModConfig.EventBlindCrabInkwellEnabled, ModelDb.Event<ThingsBlindCrabInkwell>()),
        ];

    public static IEnumerable<EventModel> AddOvergrowthAndUnderdocksEvents(IEnumerable<EventModel> source)
    {
        return AddCached(ref _sharedAct1Cache, source, GetSharedAct1Candidates());
    }

    public static IEnumerable<EventModel> AddOvergrowthEvents(IEnumerable<EventModel> source)
    {
        _overgrowthCandidates ??=
        [
            .. GetSharedAct1Candidates(),
            (ThingsModConfig.EventMossRuneLoomEnabled, ModelDb.Event<ThingsMossRuneLoom>()),
            (ThingsModConfig.EventForestPlinkoEnabled, ModelDb.Event<ThingsForestPlinko>()),
            (ThingsModConfig.EventLendingVineEnabled, ModelDb.Event<ThingsLendingVine>()),
        ];
        return AddCached(ref _overgrowthCache, source, _overgrowthCandidates);
    }

    public static IEnumerable<EventModel> AddUnderdocksEvents(IEnumerable<EventModel> source)
    {
        _underdocksCandidates ??=
        [
            .. GetSharedAct1Candidates(),
            (ThingsModConfig.EventBilgeMinesEnabled, ModelDb.Event<ThingsBilgeMines>()),
            (ThingsModConfig.EventClamClawEnabled, ModelDb.Event<ThingsClamClaw>()),
        ];
        return AddCached(ref _underdocksCache, source, _underdocksCandidates);
    }

    public static IEnumerable<EventModel> AddHiveEvents(IEnumerable<EventModel> source)
    {
        _hiveCandidates ??=
        [
            (ThingsModConfig.EventMedusaEnabled, ModelDb.Event<ThingsMedusa>()),
            (ThingsModConfig.EventWarmRoyalCellEnabled, ModelDb.Event<ThingsWarmRoyalCell>()),
            (ThingsModConfig.EventAntSortingTableEnabled, ModelDb.Event<ThingsAntSortingTable>()),
            (ThingsModConfig.EventOverstuffedBroodEnabled, ModelDb.Event<ThingsOverstuffedBrood>()),
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()),
        ];
        return AddCached(ref _hiveCache, source, _hiveCandidates);
    }

    public static IEnumerable<EventModel> AddDepthsEvents(IEnumerable<EventModel> source)
    {
        _depthsCandidates ??=
        [
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
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()),
        ];
        return AddCached(ref _depthsCache, source, _depthsCandidates);
    }

    public static IEnumerable<EventModel> AddGloryEvents(IEnumerable<EventModel> source)
    {
        _gloryCandidates ??=
        [
            (ThingsModConfig.EventChronometerRigEnabled, ModelDb.Event<ThingsChronometerRig>()),
            (ThingsModConfig.EventSilveredGalleryEnabled, ModelDb.Event<ThingsSilveredGallery>()),
            (ThingsModConfig.EventScalesOfVanityEnabled, ModelDb.Event<ThingsScalesOfVanity>()),
            (ThingsModConfig.EventSpikedDoorEnabled, ModelDb.Event<ThingsSpikedDoor>()),
        ];
        return AddCached(ref _gloryCache, source, _gloryCandidates);
    }

    private static bool MatchesSource(
        IEnumerable<EventModel> cachedRef,
        EventModel[] cachedSnapshot,
        IEnumerable<EventModel> source)
    {
        if (ReferenceEquals(cachedRef, source))
            return source is not ICollection<EventModel> col || col.Count == cachedSnapshot.Length;

        if (source is IReadOnlyList<EventModel> list)
        {
            if (list.Count != cachedSnapshot.Length)
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], cachedSnapshot[i]))
                    return false;
            }
            return true;
        }

        int index = 0;
        foreach (EventModel item in source)
        {
            if (index >= cachedSnapshot.Length || !ReferenceEquals(item, cachedSnapshot[index]))
                return false;
            index++;
        }
        return index == cachedSnapshot.Length;
    }

    private static IEnumerable<EventModel> AddCached(
        ref CachedEventCatalog? cache,
        IEnumerable<EventModel> source,
        (string EnabledKey, EventModel Event)[] candidates)
    {
        int configVersion = ThingsModConfig.Version;
        CachedEventCatalog? cached = cache;
        if (cached != null &&
            cached.ConfigVersion == configVersion &&
            MatchesSource(cached.SourceRef, cached.SourceSnapshot, source))
        {
            return cached.Result;
        }

        EventModel[] sourceSnapshot = source as EventModel[] ?? source.ToArray();
        EventModel[] result = DeterministicContentOrder.SortBaseThenMods(
            sourceSnapshot.Concat(candidates
                    .Where(candidate => ThingsModConfig.IsEnabled(candidate.EnabledKey))
                    .Select(candidate => candidate.Event))
                .Distinct()).ToArray();

        cache = new CachedEventCatalog
        {
            SourceRef = source,
            SourceSnapshot = sourceSnapshot,
            ConfigVersion = configVersion,
            Result = result,
        };
        return result;
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
