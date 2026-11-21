using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Managers;
using STS2_Things.Config;
using STS2_Things.Encounters;

namespace STS2_Things.Hooks;

/// <summary>
/// V110/V113 会自动发现所有具体 MonsterModel/EncounterModel。Act 的候选遭遇仍是原版
/// 硬编码列表，因此只在这些消费点追加 canonical EncounterModel，并保持原版前缀
/// 顺序不变、对所有 Mod 后缀使用官方 ContentSorter 排序。
///
/// 可配置门控（ThingsModConfig）：
///   - enabled=false 的遭遇/Boss 从对应 Act 的候选池移除；
///   - 强制/仅模组 Boss 筛选作用于实际候选池，发现顺序不会覆盖自定义权重；
///   - 槽位冲突裁决（同槽位多个强制）由 ThingsModConfig 在加载/写入时完成。
/// </summary>
internal static class MonsterEncounterCatalog
{
    private sealed class EncounterSlotCache
    {
        public IEnumerable<EncounterModel>? SourceRef;
        public EncounterModel[]? SourceSnapshot;
        public CachedEncounterEnumerable? Wrapper;
        public int FilteredConfigVersion = -1;
        public EncounterModel[]? FilteredResult;
        public EncounterModel[]? UnfilteredResult;
    }

    private sealed class BossDiscoveryCache
    {
        public required IEnumerable<EncounterModel> SourceRef { get; init; }
        public required EncounterModel[] SourceSnapshot { get; init; }
        public required int ConfigVersion { get; init; }
        public required bool Unfiltered { get; init; }
        public required EncounterModel[] Result { get; init; }
    }

    /// <summary>
    /// Deferred live view cached inside <see cref="ActModel.AllEncounters"/> so later
    /// <see cref="ThingsModConfig"/> changes and <see cref="IsUnfilteredCatalogScope"/> scopes
    /// are reflected immediately while repeated enumerations hit an O(1) materialized array.
    /// </summary>
    private sealed class CachedEncounterEnumerable : IEnumerable<EncounterModel>
    {
        private readonly EncounterSlotCache _cache;
        private readonly string _slot;
        private readonly EncounterModel[] _sourceSnapshot;
        private readonly (string EnabledKey, EncounterModel Encounter)[] _candidates;

        public CachedEncounterEnumerable(
            EncounterSlotCache cache,
            string slot,
            EncounterModel[] sourceSnapshot,
            (string EnabledKey, EncounterModel Encounter)[] candidates)
        {
            _cache = cache;
            _slot = slot;
            _sourceSnapshot = sourceSnapshot;
            _candidates = candidates;
        }

        private EncounterModel[] Resolve()
        {
            if (IsUnfilteredCatalogScope)
            {
                return _cache.UnfilteredResult ??= BuildUnfiltered(_sourceSnapshot, _candidates);
            }

            int configVersion = ThingsModConfig.Version;
            if (_cache.FilteredResult != null && _cache.FilteredConfigVersion == configVersion)
                return _cache.FilteredResult;

            EncounterModel[] built = BuildFiltered(_sourceSnapshot, _slot, _candidates);
            _cache.FilteredConfigVersion = configVersion;
            _cache.FilteredResult = built;
            return built;
        }

        public IEnumerator<EncounterModel> GetEnumerator() =>
            ((IEnumerable<EncounterModel>)Resolve()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Resolve().GetEnumerator();
    }

    [ThreadStatic]
    private static int _unfilteredCatalogDepth;

    private static readonly EncounterSlotCache OvergrowthEncounterCache = new();
    private static readonly EncounterSlotCache UnderdocksEncounterCache = new();
    private static readonly EncounterSlotCache HiveEncounterCache = new();

    private static BossDiscoveryCache? _overgrowthBossCache;
    private static BossDiscoveryCache? _underdocksBossCache;
    private static BossDiscoveryCache? _hiveBossCache;

    private static (string EnabledKey, EncounterModel Encounter)[]? _overgrowthEncounterCandidates;
    private static (string EnabledKey, EncounterModel Encounter)[]? _underdocksEncounterCandidates;
    private static (string EnabledKey, EncounterModel Encounter)[]? _hiveEncounterCandidates;

    private static (string EnabledKey, string ForcedKey, EncounterModel Encounter)[]? _overgrowthBossCandidates;
    private static (string EnabledKey, string ForcedKey, EncounterModel Encounter)[]? _underdocksBossCandidates;
    private static (string EnabledKey, string ForcedKey, EncounterModel Encounter)[]? _hiveBossCandidates;
    private static EncounterModel[]? _forcedCaveGodBoss;

    internal static bool IsUnfilteredCatalogScope => _unfilteredCatalogDepth > 0;

    internal static void EnterUnfilteredCatalogScope() => _unfilteredCatalogDepth++;

    internal static void ExitUnfilteredCatalogScope()
    {
        _unfilteredCatalogDepth--;
        if (_unfilteredCatalogDepth < 0)
            _unfilteredCatalogDepth = 0;
    }

    public static IEnumerable<EncounterModel> AddOvergrowthEncounters(IEnumerable<EncounterModel> source)
    {
        _overgrowthEncounterCandidates ??=
        [
            (ThingsModConfig.BossOriginFogmogEnabled, ModelDb.Encounter<ThingsOriginFogmogBossEncounter>()),
            (ThingsModConfig.BossScaleBeetleEnabled, ModelDb.Encounter<ThingsScaleBeetleBossEncounter>()),
        ];
        return WrapCachedEncounters(OvergrowthEncounterCache, source, ThingsModConfig.SlotOvergrowth, _overgrowthEncounterCandidates);
    }

    public static IEnumerable<EncounterModel> AddUnderdocksEncounters(IEnumerable<EncounterModel> source)
    {
        _underdocksEncounterCandidates ??=
        [
            (ThingsModConfig.BossGravetideSlugEnabled, ModelDb.Encounter<ThingsGravetideSlugBossEncounter>()),
            (ThingsModConfig.EncounterSoulRoesEnabled, ModelDb.Encounter<ThingsSoulRoesEncounter>()),
            (ThingsModConfig.BossTheLegacyEnabled, ModelDb.Encounter<ThingsTheLegacyBossEncounter>()),
        ];
        return WrapCachedEncounters(UnderdocksEncounterCache, source, ThingsModConfig.SlotUnderdocks, _underdocksEncounterCandidates);
    }

    public static IEnumerable<EncounterModel> AddHiveEncounters(IEnumerable<EncounterModel> source)
    {
        // Quirky Hopper 是 Act 2 走廊遭遇，位于原生 ThievingHopperWeak 旁。IsWeak
        // 控制早期走廊子集；它仍是 RoomType.Monster，不进入精英/Boss 目录。
        _hiveEncounterCandidates ??=
        [
            (ThingsModConfig.EncounterQuirkyHopperEnabled, ModelDb.Encounter<ThingsQuirkyHopperWeak>()),
            (ThingsModConfig.BossBowlbugProgenitorEnabled, ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>()),
            (ThingsModConfig.BossCaveGodEnabled, ModelDb.Encounter<ThingsCaveGodBossEncounter>()),
        ];
        return WrapCachedEncounters(HiveEncounterCache, source, ThingsModConfig.SlotHive, _hiveEncounterCandidates);
    }

    public static IEnumerable<EncounterModel> AddOvergrowthBosses(IEnumerable<EncounterModel> source)
    {
        _overgrowthBossCandidates ??=
        [
            (ThingsModConfig.BossOriginFogmogEnabled, ThingsModConfig.BossOriginFogmogForced,
                ModelDb.Encounter<ThingsOriginFogmogBossEncounter>()),
            (ThingsModConfig.BossScaleBeetleEnabled, ThingsModConfig.BossScaleBeetleForced,
                ModelDb.Encounter<ThingsScaleBeetleBossEncounter>()),
        ];
        return AddBossesCached(ref _overgrowthBossCache, source, ThingsModConfig.SlotOvergrowth, _overgrowthBossCandidates);
    }

    public static IEnumerable<EncounterModel> AddUnderdocksBosses(IEnumerable<EncounterModel> source)
    {
        _underdocksBossCandidates ??=
        [
            (ThingsModConfig.BossGravetideSlugEnabled, ThingsModConfig.BossGravetideSlugForced,
                ModelDb.Encounter<ThingsGravetideSlugBossEncounter>()),
            (ThingsModConfig.BossTheLegacyEnabled, ThingsModConfig.BossTheLegacyForced,
                ModelDb.Encounter<ThingsTheLegacyBossEncounter>()),
        ];
        return AddBossesCached(ref _underdocksBossCache, source, ThingsModConfig.SlotUnderdocks, _underdocksBossCandidates);
    }

    public static IEnumerable<EncounterModel> AddHiveBosses(IEnumerable<EncounterModel> source)
    {
        // Cave God already owns Depths discovery; keep Hive tutorial discovery for Hive-only
        // Bowlbug Progenitor unless Cave God is explicitly forced in Hive's slot.
        if (ThingsModConfig.IsForced(ThingsModConfig.BossCaveGodForced))
            return _forcedCaveGodBoss ??= [ModelDb.Encounter<ThingsCaveGodBossEncounter>()];
        _hiveBossCandidates ??=
        [
            (ThingsModConfig.BossBowlbugProgenitorEnabled, ThingsModConfig.BossBowlbugProgenitorForced,
                ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>()),
        ];
        return AddBossesCached(ref _hiveBossCache, source, ThingsModConfig.SlotHive, _hiveBossCandidates);
    }

    private static bool MatchesSource(
        IEnumerable<EncounterModel>? cachedRef,
        EncounterModel[]? cachedSnapshot,
        IEnumerable<EncounterModel> source)
    {
        if (cachedSnapshot == null)
            return false;

        if (ReferenceEquals(cachedRef, source))
            return source is not ICollection<EncounterModel> col || col.Count == cachedSnapshot.Length;

        if (source is IReadOnlyList<EncounterModel> list)
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

        return false;
    }

    private static IEnumerable<EncounterModel> WrapCachedEncounters(
        EncounterSlotCache cache,
        IEnumerable<EncounterModel> source,
        string slot,
        (string EnabledKey, EncounterModel Encounter)[] candidates)
    {
        if (cache.Wrapper != null && MatchesSource(cache.SourceRef, cache.SourceSnapshot, source))
            return cache.Wrapper;

        EncounterModel[] snapshot = source as EncounterModel[] ?? source.ToArray();
        cache.SourceRef = source;
        cache.SourceSnapshot = snapshot;
        cache.FilteredConfigVersion = -1;
        cache.FilteredResult = null;
        cache.UnfilteredResult = null;
        cache.Wrapper = new CachedEncounterEnumerable(cache, slot, snapshot, candidates);
        return cache.Wrapper;
    }

    private static EncounterModel[] BuildUnfiltered(
        EncounterModel[] source,
        (string EnabledKey, EncounterModel Encounter)[] candidates)
    {
        return DeterministicContentOrder.SortBaseThenMods(
            source.Concat(candidates.Select(candidate => candidate.Encounter)).Distinct()).ToArray();
    }

    private static EncounterModel[] BuildFiltered(
        EncounterModel[] source,
        string slot,
        (string EnabledKey, EncounterModel Encounter)[] candidates)
    {
        EncounterModel[] all = DeterministicContentOrder.SortBaseThenMods(
            source.Where(EncounterSelectionPolicy.IsAvailable).Concat(candidates
                    .Where(candidate => ThingsModConfig.IsEnabled(candidate.EnabledKey))
                    .Select(candidate => candidate.Encounter)
                    .Where(EncounterSelectionPolicy.IsAvailable))
                .Distinct()).ToArray();
        return FilterBossPoolMaterialized(all, slot);
    }

    private static EncounterModel[] FilterBossPoolMaterialized(EncounterModel[] all, string slot)
    {
        string? forced = ThingsModConfig.GetForcedBossKeyForSlot(slot);
        List<EncounterModel> bosses = all.Where(encounter => encounter.RoomType == RoomType.Boss).ToList();
        List<EncounterModel> selected = forced is not null
            ? bosses.Where(encounter => EncounterSelectionPolicy.ForcedKey(encounter) == forced).ToList()
            : ThingsModConfig.GetBool(ThingsModConfig.BossOnlyModBosses)
                ? bosses.Where(EncounterSelectionPolicy.IsOwnedBoss).ToList()
                : bosses;
        // A contradictory all-disabled / only-mod configuration must stay playable.
        if (selected.Count == 0)
            selected = bosses;

        var result = new List<EncounterModel>(all.Length);
        foreach (EncounterModel encounter in all)
        {
            if (encounter.RoomType != RoomType.Boss || selected.Contains(encounter))
                result.Add(encounter);
        }
        return result.ToArray();
    }

    private static IEnumerable<EncounterModel> AddBossesCached(
        ref BossDiscoveryCache? cache,
        IEnumerable<EncounterModel> source,
        string slot,
        (string EnabledKey, string ForcedKey, EncounterModel Encounter)[] bosses)
    {
        int configVersion = ThingsModConfig.Version;
        bool unfiltered = IsUnfilteredCatalogScope;
        BossDiscoveryCache? cached = cache;
        if (cached != null &&
            cached.ConfigVersion == configVersion &&
            cached.Unfiltered == unfiltered &&
            MatchesSource(cached.SourceRef, cached.SourceSnapshot, source))
        {
            return cached.Result;
        }

        EncounterModel[] sourceSnapshot = source as EncounterModel[] ?? source.ToArray();
        EncounterModel[] result = ComputeBosses(sourceSnapshot, slot, bosses, unfiltered);
        cache = new BossDiscoveryCache
        {
            SourceRef = source,
            SourceSnapshot = sourceSnapshot,
            ConfigVersion = configVersion,
            Unfiltered = unfiltered,
            Result = result,
        };
        return result;
    }

    private static EncounterModel[] ComputeBosses(
        EncounterModel[] source,
        string slot,
        (string EnabledKey, string ForcedKey, EncounterModel Encounter)[] bosses,
        bool unfiltered)
    {
        string? forced = ThingsModConfig.GetForcedBossKeyForSlot(slot);
        if (forced is not null)
        {
            foreach ((_, string forcedKey, EncounterModel encounter) in bosses)
            {
                if (forcedKey == forced)
                    return [encounter];
            }

            // 防御：配置被外部破坏（强制键不属于该槽位）。退回常规过滤，避免空候选。
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                $"STS2_Things config: forced boss key '{forced}' does not belong to slot '{slot}'; ignoring force.");
        }

        // Otherwise an unseen boss replaces the weighted draw during tutorial
        // discovery. Preserve vanilla discovery only with the default selection.
        if (ThingsModConfig.GetBool(ThingsModConfig.BossOnlyModBosses) || ThingsModConfig.HasBossWeightOverrides(slot))
            return Array.Empty<EncounterModel>();

        var pairs = new (string EnabledKey, EncounterModel Encounter)[bosses.Length];
        for (int i = 0; i < bosses.Length; i++)
            pairs[i] = (bosses[i].EnabledKey, bosses[i].Encounter);

        return unfiltered
            ? BuildUnfiltered(source, pairs)
            : BuildFiltered(source, slot, pairs);
    }
}

[HarmonyPatch(typeof(Overgrowth), nameof(Overgrowth.GenerateAllEncounters))]
internal static class OvergrowthEncounterPoolPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddOvergrowthEncounters(__result);
    }
}

[HarmonyPatch(typeof(Underdocks), nameof(Underdocks.GenerateAllEncounters))]
internal static class UnderdocksEncounterPoolPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddUnderdocksEncounters(__result);
    }
}

[HarmonyPatch(typeof(Hive), nameof(Hive.GenerateAllEncounters))]
internal static class HiveEncounterPoolPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddHiveEncounters(__result);
    }
}

[HarmonyPatch(typeof(Overgrowth), nameof(Overgrowth.BossDiscoveryOrder), MethodType.Getter)]
internal static class OvergrowthBossDiscoveryPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddOvergrowthBosses(__result);
    }
}

[HarmonyPatch(typeof(Underdocks), nameof(Underdocks.BossDiscoveryOrder), MethodType.Getter)]
internal static class UnderdocksBossDiscoveryPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddUnderdocksBosses(__result);
    }
}

[HarmonyPatch(typeof(Hive), nameof(Hive.BossDiscoveryOrder), MethodType.Getter)]
internal static class HiveBossDiscoveryPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = MonsterEncounterCatalog.AddHiveBosses(__result);
    }
}

[HarmonyPatch]
internal static class UnfilteredEncounterCatalogScopePatches
{
    [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
    [HarmonyPrefix]
    private static void BeforeBossEpochCheck() => MonsterEncounterCatalog.EnterUnfilteredCatalogScope();

    [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
    [HarmonyFinalizer]
    private static void AfterBossEpochCheck() => MonsterEncounterCatalog.ExitUnfilteredCatalogScope();

    [HarmonyPatch(typeof(ProgressSaveManager), "GetEliteEncounters")]
    [HarmonyPrefix]
    private static void BeforeEliteCatalogCheck() => MonsterEncounterCatalog.EnterUnfilteredCatalogScope();

    [HarmonyPatch(typeof(ProgressSaveManager), "GetEliteEncounters")]
    [HarmonyFinalizer]
    private static void AfterEliteCatalogCheck() => MonsterEncounterCatalog.ExitUnfilteredCatalogScope();

    [HarmonyPatch(typeof(NBestiary), "CreateEntries")]
    [HarmonyPrefix]
    private static void BeforeBestiaryEntries() => MonsterEncounterCatalog.EnterUnfilteredCatalogScope();

    [HarmonyPatch(typeof(NBestiary), "CreateEntries")]
    [HarmonyFinalizer]
    private static void AfterBestiaryEntries() => MonsterEncounterCatalog.ExitUnfilteredCatalogScope();

    [HarmonyPatch(typeof(NMapPointHistoryEntry), nameof(NMapPointHistoryEntry.AssetPaths), MethodType.Getter)]
    [HarmonyPostfix]
    private static void WrapHistoryEntryAssetPaths(ref IEnumerable<string> __result)
    {
        __result = EnumerateUnfiltered(__result);
    }

    private static IEnumerable<string> EnumerateUnfiltered(IEnumerable<string> inner)
    {
        MonsterEncounterCatalog.EnterUnfilteredCatalogScope();
        try
        {
            foreach (string path in inner)
                yield return path;
        }
        finally
        {
            MonsterEncounterCatalog.ExitUnfilteredCatalogScope();
        }
    }
}
