using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Godot;

namespace STS2_Things.Config;

/// <summary>
/// 单一配置源。文件为 <c>user://mod_configs/STS2_Things.cfg</c>（JSON），与 BaseLib 桥
/// 写入的路径、文件名与键名完全一致；RitsuLib 互操作提供器也读写同一文件。
/// </summary>
public static class ThingsModConfig
{
    public enum Category
    {
        Act,
        Boss,
        Encounter,
        Event,
        Card,
        Feature,
        NeowRelic,
        Audio,
        Stability,
    }

    public const string CurrentSchemaVersion = "3";

    /// <summary>Boss 槽位（冲突分组）。同一槽位只能有一个强制 Boss。</summary>
    public const string SlotOvergrowth = "overgrowth";
    public const string SlotUnderdocks = "underdocks";
    public const string SlotHive = "hive";
    public const string SlotDepths = "depths";

    public sealed record Entry(
        string Key,
        Category Category,
        object Default,
        string? Slot = null,
        int Min = 0,
        int Max = 1000);

    // ===== 键常量（单一来源：BaseLib 桥属性名、RitsuLib schema 与门控代码都引用这里）=====
    public const string SchemaVersion = "SchemaVersion";
    public const string FeatureCustomBgmEnabled = "FeatureCustomBgmEnabled";
    public const string FeatureGuaranteedDepthsEnabled = "FeatureGuaranteedDepthsEnabled";
    public const string FeatureDefensiveEngineGuardsEnabled = "FeatureDefensiveEngineGuardsEnabled";
    public const string BossOnlyModBosses = "BossOnlyModBosses";
    public const string BossOriginFogmogWeightPercent = "BossOriginFogmogWeightPercent";
    public const string BossScaleBeetleWeightPercent = "BossScaleBeetleWeightPercent";
    public const string BossGravetideSlugWeightPercent = "BossGravetideSlugWeightPercent";
    public const string BossTheLegacyWeightPercent = "BossTheLegacyWeightPercent";
    public const string BossBowlbugProgenitorWeightPercent = "BossBowlbugProgenitorWeightPercent";
    public const string BossCaveGodWeightPercent = "BossCaveGodWeightPercent";
    public const string BossHumanFaceTowerWeightPercent = "BossHumanFaceTowerWeightPercent";
    public const string BossAbyssalInkmawWeightPercent = "BossAbyssalInkmawWeightPercent";
    public const string EncounterSoulRoesWeightPercent = "EncounterSoulRoesWeightPercent";

    // ---- 遭遇战：Boss ----
    public const string BossOriginFogmogEnabled = "BossOriginFogmogEnabled";
    public const string BossOriginFogmogForced = "BossOriginFogmogForced";
    public const string BossScaleBeetleEnabled = "BossScaleBeetleEnabled";
    public const string BossScaleBeetleForced = "BossScaleBeetleForced";
    public const string BossGravetideSlugEnabled = "BossGravetideSlugEnabled";
    public const string BossGravetideSlugForced = "BossGravetideSlugForced";
    public const string BossTheLegacyEnabled = "BossTheLegacyEnabled";
    public const string BossTheLegacyForced = "BossTheLegacyForced";
    public const string BossBowlbugProgenitorEnabled = "BossBowlbugProgenitorEnabled";
    public const string BossBowlbugProgenitorForced = "BossBowlbugProgenitorForced";
    public const string BossCaveGodEnabled = "BossCaveGodEnabled";
    public const string BossCaveGodForced = "BossCaveGodForced";
    public const string BossHumanFaceTowerEnabled = "BossHumanFaceTowerEnabled";
    public const string BossHumanFaceTowerForced = "BossHumanFaceTowerForced";
    public const string BossAbyssalInkmawEnabled = "BossAbyssalInkmawEnabled";
    public const string BossAbyssalInkmawForced = "BossAbyssalInkmawForced";

    // ---- 遭遇战：非 Boss ----
    public const string EncounterSoulRoesEnabled = "EncounterSoulRoesEnabled";
    public const string EncounterQuirkyHopperEnabled = "EncounterQuirkyHopperEnabled";

    // ---- 事件 ----
    public const string EventRobberyFakeMerchantEnabled = "EventRobberyFakeMerchantEnabled";
    public const string EventBackroomsEnabled = "EventBackroomsEnabled";
    public const string EventMedusaEnabled = "EventMedusaEnabled";
    public const string EventCuttingItCloseEnabled = "EventCuttingItCloseEnabled";
    public const string EventRealityAlignedHousesEnabled = "EventRealityAlignedHousesEnabled";
    public const string EventShadowCloakroomEnabled = "EventShadowCloakroomEnabled";
    public const string EventEchoingWellEnabled = "EventEchoingWellEnabled";
    public const string EventPoliteMawEnabled = "EventPoliteMawEnabled";
    public const string EventMycelialBankEnabled = "EventMycelialBankEnabled";
    public const string EventUnlitFireEnabled = "EventUnlitFireEnabled";
    public const string EventRelicWorkshopEnabled = "EventRelicWorkshopEnabled";
    public const string EventPotionTastingEnabled = "EventPotionTastingEnabled";
    public const string EventNarrowGateEnabled = "EventNarrowGateEnabled";
    public const string EventBitingChestEnabled = "EventBitingChestEnabled";
    public const string EventCrowdedWardEnabled = "EventCrowdedWardEnabled";
    public const string EventReturnsCounterEnabled = "EventReturnsCounterEnabled";
    public const string EventHotWaxBoxEnabled = "EventHotWaxBoxEnabled";
    public const string EventWarmRoyalCellEnabled = "EventWarmRoyalCellEnabled";
    public const string EventSpliceBoothEnabled = "EventSpliceBoothEnabled";
    public const string EventGildedPressEnabled = "EventGildedPressEnabled";
    public const string EventCrookedCounterweightEnabled = "EventCrookedCounterweightEnabled";
    public const string EventAntSortingTableEnabled = "EventAntSortingTableEnabled";
    public const string EventGrudgeVendingMachineEnabled = "EventGrudgeVendingMachineEnabled";
    public const string EventBlindCrabInkwellEnabled = "EventBlindCrabInkwellEnabled";
    public const string EventChronometerRigEnabled = "EventChronometerRigEnabled";
    public const string EventSilveredGalleryEnabled = "EventSilveredGalleryEnabled";
    public const string EventCrackedBrazierEnabled = "EventCrackedBrazierEnabled";
    public const string EventSiltWinchEnabled = "EventSiltWinchEnabled";
    public const string EventBilgeMinesEnabled = "EventBilgeMinesEnabled";
    public const string EventMossRuneLoomEnabled = "EventMossRuneLoomEnabled";
    public const string EventClamClawEnabled = "EventClamClawEnabled";
    public const string EventForestPlinkoEnabled = "EventForestPlinkoEnabled";
    public const string EventBronzeCauldronEnabled = "EventBronzeCauldronEnabled";
    public const string EventLendingVineEnabled = "EventLendingVineEnabled";
    public const string EventOverstuffedBroodEnabled = "EventOverstuffedBroodEnabled";
    public const string EventScalesOfVanityEnabled = "EventScalesOfVanityEnabled";
    public const string EventWallCrackLedgerEnabled = "EventWallCrackLedgerEnabled";
    public const string EventSpikedDoorEnabled = "EventSpikedDoorEnabled";

    // ---- 卡牌 ----
    public const string CardsEnabled = "CardsEnabled";
    public const string CardCollisionEnabled = "CardCollisionEnabled";
    public const string CardReuseEnabled = "CardReuseEnabled";
    public const string CardSoulfyshDiseaseEnabled = "CardSoulfyshDiseaseEnabled";
    public const string CardRecallEnabled = "CardRecallEnabled";
    public const string CardPackUpEnabled = "CardPackUpEnabled";

    // ---- 商人猜拳 ----
    public const string FeatureMerchantBargainEnabled = "FeatureMerchantBargainEnabled";

    // ---- 涅奥起始遗物 ----
    public const string NeowRelicCurseRemoverEnabled = "NeowRelicCurseRemoverEnabled";
    public const string NeowRelicWhiteFlagEnabled = "NeowRelicWhiteFlagEnabled";
    public const string NeowRelicMagicGloveEnabled = "NeowRelicMagicGloveEnabled";

    // Legacy v1.x key aliases supported during one-time migration to SchemaVersion 3.
    private static readonly IReadOnlyDictionary<string, string[]> LegacyKeyAliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [FeatureCustomBgmEnabled] = ["CustomBgmEnabled", "EnableCustomBgm"],
            [FeatureMerchantBargainEnabled] = ["MerchantBargainEnabled", "EnableMerchantBargain"],
            [FeatureDefensiveEngineGuardsEnabled] = ["DefensiveEngineGuardsEnabled"],
        };

    /// <summary>规范顺序即冲突裁决顺序：同槽位第一个强制项生效。</summary>
    public static readonly IReadOnlyList<Entry> Entries =
    [
        new(FeatureCustomBgmEnabled, Category.Audio, Default: true),
        new(FeatureGuaranteedDepthsEnabled, Category.Act, Default: false),
        new(BossOnlyModBosses, Category.Boss, Default: false),
        new(BossOriginFogmogEnabled, Category.Boss, Default: true, Slot: SlotOvergrowth),
        new(BossOriginFogmogForced, Category.Boss, Default: false, Slot: SlotOvergrowth),
        new(BossOriginFogmogWeightPercent, Category.Boss, Default: 100, Slot: SlotOvergrowth),
        new(BossScaleBeetleEnabled, Category.Boss, Default: true, Slot: SlotOvergrowth),
        new(BossScaleBeetleForced, Category.Boss, Default: false, Slot: SlotOvergrowth),
        new(BossScaleBeetleWeightPercent, Category.Boss, Default: 100, Slot: SlotOvergrowth),
        new(BossGravetideSlugEnabled, Category.Boss, Default: true, Slot: SlotUnderdocks),
        new(BossGravetideSlugForced, Category.Boss, Default: false, Slot: SlotUnderdocks),
        new(BossGravetideSlugWeightPercent, Category.Boss, Default: 100, Slot: SlotUnderdocks),
        new(BossTheLegacyEnabled, Category.Boss, Default: true, Slot: SlotUnderdocks),
        new(BossTheLegacyForced, Category.Boss, Default: false, Slot: SlotUnderdocks),
        new(BossTheLegacyWeightPercent, Category.Boss, Default: 100, Slot: SlotUnderdocks),
        new(BossBowlbugProgenitorEnabled, Category.Boss, Default: true, Slot: SlotHive),
        new(BossBowlbugProgenitorForced, Category.Boss, Default: false, Slot: SlotHive),
        new(BossBowlbugProgenitorWeightPercent, Category.Boss, Default: 100, Slot: SlotHive),
        new(BossCaveGodEnabled, Category.Boss, Default: true, Slot: SlotHive),
        new(BossCaveGodForced, Category.Boss, Default: false, Slot: SlotHive),
        new(BossCaveGodWeightPercent, Category.Boss, Default: 100, Slot: SlotHive),
        new(BossHumanFaceTowerEnabled, Category.Boss, Default: true, Slot: SlotDepths),
        new(BossHumanFaceTowerForced, Category.Boss, Default: false, Slot: SlotDepths),
        new(BossHumanFaceTowerWeightPercent, Category.Boss, Default: 100, Slot: SlotDepths),
        new(BossAbyssalInkmawEnabled, Category.Boss, Default: true, Slot: SlotDepths),
        new(BossAbyssalInkmawForced, Category.Boss, Default: false, Slot: SlotDepths),
        new(BossAbyssalInkmawWeightPercent, Category.Boss, Default: 100, Slot: SlotDepths),
        new(EncounterSoulRoesEnabled, Category.Encounter, Default: true),
        new(EncounterSoulRoesWeightPercent, Category.Encounter, Default: 100),
        new(EncounterQuirkyHopperEnabled, Category.Encounter, Default: true),
        new(EventRobberyFakeMerchantEnabled, Category.Event, Default: true),
        new(EventBackroomsEnabled, Category.Event, Default: true),
        new(EventMedusaEnabled, Category.Event, Default: true),
        new(EventCuttingItCloseEnabled, Category.Event, Default: true),
        new(EventRealityAlignedHousesEnabled, Category.Event, Default: true),
        new(EventShadowCloakroomEnabled, Category.Event, Default: true),
        new(EventEchoingWellEnabled, Category.Event, Default: true),
        new(EventPoliteMawEnabled, Category.Event, Default: true),
        new(EventMycelialBankEnabled, Category.Event, Default: true),
        new(EventUnlitFireEnabled, Category.Event, Default: true),
        new(EventRelicWorkshopEnabled, Category.Event, Default: true),
        new(EventPotionTastingEnabled, Category.Event, Default: true),
        new(EventNarrowGateEnabled, Category.Event, Default: true),
        new(EventBitingChestEnabled, Category.Event, Default: true),
        new(EventCrowdedWardEnabled, Category.Event, Default: true),
        new(EventReturnsCounterEnabled, Category.Event, Default: true),
        new(EventHotWaxBoxEnabled, Category.Event, Default: true),
        new(EventWarmRoyalCellEnabled, Category.Event, Default: true),
        new(EventSpliceBoothEnabled, Category.Event, Default: true),
        new(EventGildedPressEnabled, Category.Event, Default: true),
        new(EventCrookedCounterweightEnabled, Category.Event, Default: true),
        new(EventAntSortingTableEnabled, Category.Event, Default: true),
        new(EventGrudgeVendingMachineEnabled, Category.Event, Default: true),
        new(EventBlindCrabInkwellEnabled, Category.Event, Default: true),
        new(EventChronometerRigEnabled, Category.Event, Default: true),
        new(EventSilveredGalleryEnabled, Category.Event, Default: true),
        new(EventCrackedBrazierEnabled, Category.Event, Default: true),
        new(EventSiltWinchEnabled, Category.Event, Default: true),
        new(EventBilgeMinesEnabled, Category.Event, Default: true),
        new(EventMossRuneLoomEnabled, Category.Event, Default: true),
        new(EventClamClawEnabled, Category.Event, Default: true),
        new(EventForestPlinkoEnabled, Category.Event, Default: true),
        new(EventBronzeCauldronEnabled, Category.Event, Default: true),
        new(EventLendingVineEnabled, Category.Event, Default: true),
        new(EventOverstuffedBroodEnabled, Category.Event, Default: true),
        new(EventScalesOfVanityEnabled, Category.Event, Default: true),
        new(EventWallCrackLedgerEnabled, Category.Event, Default: true),
        new(EventSpikedDoorEnabled, Category.Event, Default: true),
        new(CardsEnabled, Category.Card, Default: true),
        new(CardCollisionEnabled, Category.Card, Default: true),
        new(CardReuseEnabled, Category.Card, Default: true),
        new(CardSoulfyshDiseaseEnabled, Category.Card, Default: true),
        new(CardRecallEnabled, Category.Card, Default: true),
        new(CardPackUpEnabled, Category.Card, Default: true),
        new(FeatureMerchantBargainEnabled, Category.Feature, Default: true),
        new(NeowRelicCurseRemoverEnabled, Category.NeowRelic, Default: true),
        new(NeowRelicWhiteFlagEnabled, Category.NeowRelic, Default: true),
        new(NeowRelicMagicGloveEnabled, Category.NeowRelic, Default: true),
        new(FeatureDefensiveEngineGuardsEnabled, Category.Stability, Default: true),
    ];

    private static readonly Dictionary<string, object> Values =
        Entries.ToDictionary(entry => entry.Key, entry => entry.Default);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int _version;
    private static volatile bool _defensiveEngineGuardsEnabled = true;
    private static volatile bool _allCustomCardsEnabled = true;
    private static volatile bool _customBgmEnabled = true;
    private static volatile bool _guaranteedDepthsEnabled;
    private static volatile bool _bossOnlyModBosses;
    private static volatile bool _merchantBargainEnabled = true;
    private static volatile bool _cardsEnabled = true;

    public static int Version => Volatile.Read(ref _version);

    public static bool DefensiveEngineGuardsEnabled => _defensiveEngineGuardsEnabled;

    public static bool AllCustomCardsEnabled => _allCustomCardsEnabled;

    public static bool CustomBgmEnabled => _customBgmEnabled;

    public static bool GuaranteedDepthsEnabled => _guaranteedDepthsEnabled;

    public static bool BossOnlyModBossesEnabled => _bossOnlyModBosses;

    public static bool MerchantBargainEnabled => _merchantBargainEnabled;

    /// <summary>配置发生变化（含强制冲突裁决后的降级）时触发。</summary>
    public static event Action? Changed;

    internal static event Action? LocalChanged;

    public static bool IsUsingSession => MultiplayerConfig.IsActive;

    public static bool CanEdit(string key) => MultiplayerConfig.CanEdit(key);

    internal static void NotifyEffectiveChanged()
    {
        RefreshHotCaches();
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    internal static object? GetLocalValue(string key) => Values.GetValueOrDefault(key);

    public static string ConfigPath =>
        Path.Combine(OS.GetUserDataDir(), "mod_configs", "STS2_Things.cfg");

    public static bool IsEnabled(string key) => GetBool(key);

    public static bool IsForced(string key) => GetBool(key);

    public static bool IsIndividualCardKey(string key) =>
        key is CardCollisionEnabled
            or CardReuseEnabled
            or CardSoulfyshDiseaseEnabled
            or CardRecallEnabled
            or CardPackUpEnabled;

    public static bool IsCustomCardEnabled(string key) =>
        _cardsEnabled && GetBool(key);

    /// <summary>读取任意布尔键（含 Forced 键）。</summary>
    public static bool GetBool(string key)
    {
        if (ReferenceEquals(key, FeatureDefensiveEngineGuardsEnabled))
            return _defensiveEngineGuardsEnabled;
        if (ReferenceEquals(key, FeatureCustomBgmEnabled))
            return _customBgmEnabled;
        if (ReferenceEquals(key, FeatureGuaranteedDepthsEnabled))
            return _guaranteedDepthsEnabled;
        if (ReferenceEquals(key, BossOnlyModBosses))
            return _bossOnlyModBosses;
        if (ReferenceEquals(key, FeatureMerchantBargainEnabled))
            return _merchantBargainEnabled;
        if (ReferenceEquals(key, CardsEnabled))
            return _cardsEnabled;
        return GetBoolCore(key);
    }

    private static bool GetBoolCore(string key) =>
        MultiplayerConfig.TryGetBoolOverride(key, out bool overridden)
            ? overridden
            : GetLocalValue(key) is true;

    public static int GetInt(string key) =>
        MultiplayerConfig.TryGetIntOverride(key, out int overridden)
            ? overridden
            : GetLocalValue(key) is int integer ? integer : 0;

    private static void RefreshHotCaches()
    {
        _defensiveEngineGuardsEnabled = GetBoolCore(FeatureDefensiveEngineGuardsEnabled);
        _customBgmEnabled = GetBoolCore(FeatureCustomBgmEnabled);
        _guaranteedDepthsEnabled = GetBoolCore(FeatureGuaranteedDepthsEnabled);
        _bossOnlyModBosses = GetBoolCore(BossOnlyModBosses);
        _merchantBargainEnabled = GetBoolCore(FeatureMerchantBargainEnabled);
        bool cardsEnabled = GetBoolCore(CardsEnabled);
        _cardsEnabled = cardsEnabled;
        _allCustomCardsEnabled = cardsEnabled
            && GetBoolCore(CardCollisionEnabled)
            && GetBoolCore(CardReuseEnabled)
            && GetBoolCore(CardSoulfyshDiseaseEnabled)
            && GetBoolCore(CardRecallEnabled)
            && GetBoolCore(CardPackUpEnabled);
    }

    public static bool HasBossWeightOverrides(string slot) =>
        Entries.Any(entry => entry.Slot == slot && entry.Default is int && GetInt(entry.Key) != (int)entry.Default);

    public static string? GetForcedBossKeyForSlot(string slot)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Slot != slot || !entry.Key.EndsWith("Forced", StringComparison.Ordinal))
                continue;
            if (IsForced(entry.Key))
                return entry.Key;
        }

        return null;
    }

    public static bool CanForce(string forcedKey)
    {
        Entry? entry = Entries.FirstOrDefault(candidate => candidate.Key == forcedKey);
        if (entry?.Slot is not { } slot)
            return false;
        string? current = GetForcedBossKeyForSlot(slot);
        return current is null || current == forcedKey;
    }

    public static void Load()
    {
        Dictionary<string, object> previous = new(Values);
        Values.Clear();
        foreach (Entry entry in Entries)
            Values[entry.Key] = entry.Default;

        string path = ConfigPath;
        bool needsSave = !File.Exists(path);
        if (File.Exists(path))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Configuration root must be an object.");

                needsSave |= document.RootElement.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.String);
                if (!document.RootElement.TryGetProperty(SchemaVersion, out JsonElement schemaElem) ||
                    schemaElem.ValueKind != JsonValueKind.String ||
                    schemaElem.GetString() != CurrentSchemaVersion)
                {
                    needsSave = true;
                }

                foreach (Entry entry in Entries)
                {
                    if (TryReadEntryElement(document.RootElement, entry.Key, out JsonElement element, out bool usedAlias) &&
                        TryNormalize(entry, element, out object normalized))
                    {
                        Values[entry.Key] = normalized;
                        needsSave |= usedAlias;
                    }
                    else
                    {
                        needsSave = true;
                    }
                }
            }
            catch (Exception exception)
            {
                needsSave = true;
                foreach (Entry entry in Entries)
                    Values[entry.Key] = entry.Default;
                MegaCrit.Sts2.Core.Logging.Log.Warn(
                    $"STS2_Things config '{path}' is unreadable ({exception.Message}); backing it up and using defaults.");
                try
                {
                    File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
                }
                catch (IOException copyEx)
                {
                    MegaCrit.Sts2.Core.Logging.Log.Debug($"STS2_Things corrupt config backup skipped: {copyEx.Message}");
                }
            }
        }
        needsSave |= ResolveConflicts(logWarnings: true);
        if (needsSave)
            Save();
        bool anyChanged = Values.Any(pair => !previous.TryGetValue(pair.Key, out object? old) || !Equals(old, pair.Value));
        if (anyChanged)
            LocalChanged?.Invoke();
        RefreshHotCaches();
        Interlocked.Increment(ref _version);
        if (anyChanged)
            Changed?.Invoke();
    }

    public static void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(directory);
            var payload = new Dictionary<string, string>(Entries.Count + 1, StringComparer.Ordinal)
            {
                [SchemaVersion] = CurrentSchemaVersion,
            };
            foreach (Entry entry in Entries)
            {
                object val = Values.TryGetValue(entry.Key, out object? current) ? current : entry.Default;
                payload[entry.Key] = Convert.ToString(val, CultureInfo.InvariantCulture)!;
            }
            string temporary = ConfigPath + ".things-new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporary, ConfigPath, overwrite: true);
        }
        catch (Exception exception)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                $"STS2_Things config could not be saved: {exception.Message}");
        }
    }

    public static object? GetValue(string key)
    {
        return MultiplayerConfig.GetOverride(key) ?? GetLocalValue(key);
    }

    public static void SetValue(string key, object? value)
    {
        SetValues(new Dictionary<string, object?> { [key] = value });
    }

    public static void SetValues(IDictionary<string, object?> changes)
    {
        bool changed = false;
        foreach ((string key, object? value) in changes)
        {
            Entry? entry = Entries.FirstOrDefault(candidate => candidate.Key == key);
            if (entry is null || !CanEdit(key) || !TryNormalize(entry, value, out object normalized) || Equals(Values[key], normalized))
                continue;
            Values[key] = normalized;
            changed = true;
        }
        if (!changed)
            return;
        ResolveConflicts(logWarnings: true);
        LocalChanged?.Invoke();
        RefreshHotCaches();
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    private static bool TryReadEntryElement(
        JsonElement root,
        string canonicalKey,
        out JsonElement element,
        out bool usedAlias)
    {
        usedAlias = false;
        if (root.TryGetProperty(canonicalKey, out element))
            return true;
        if (LegacyKeyAliases.TryGetValue(canonicalKey, out string[]? aliases))
        {
            foreach (string alias in aliases)
            {
                if (root.TryGetProperty(alias, out element))
                {
                    usedAlias = true;
                    return true;
                }
            }
        }
        element = default;
        return false;
    }

    private static bool TryNormalize(Entry entry, object? value, out object normalized)
    {
        normalized = entry.Default;
        if (value is JsonElement element)
        {
            value = element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetDouble(out double number) => number,
                _ => null,
            };
        }
        if (entry.Default is bool)
        {
            if (value is bool boolean)
                normalized = boolean;
            else if (value is string text && bool.TryParse(text, out boolean))
                normalized = boolean;
            else
                return false;
            return true;
        }
        if (value is not (byte or short or int or long or float or double or decimal or string))
            return false;
        if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) || !double.IsFinite(numeric))
            return false;
        normalized = (int)Math.Round(Math.Clamp(numeric, entry.Min, entry.Max), MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool ResolveConflicts(bool logWarnings)
    {
        bool changed = false;

        // First demote duplicate Forced entries within the same slot so a demoted second boss
        // does not have its Enabled=false state unnecessarily flipped to true below.
        foreach (string slot in new[] { SlotOvergrowth, SlotUnderdocks, SlotHive, SlotDepths })
        {
            string? first = null;
            foreach (Entry entry in Entries)
            {
                if (entry.Slot != slot || !entry.Key.EndsWith("Forced", StringComparison.Ordinal))
                    continue;
                if (Values[entry.Key] is not true)
                    continue;
                if (first is null)
                {
                    first = entry.Key;
                    continue;
                }

                Values[entry.Key] = false;
                changed = true;
                if (logWarnings)
                {
                    MegaCrit.Sts2.Core.Logging.Log.Warn(
                        $"STS2_Things config: '{entry.Key}' was demoted because '{first}' is already " +
                        $"forced for the same act boss slot ({slot}).");
                }
            }
        }

        foreach (Entry entry in Entries)
        {
            if (entry.Key.EndsWith("Enabled", StringComparison.Ordinal) &&
                entry.Slot is { } slot && Values.GetValueOrDefault(ForcedKeyFor(slot, entry.Key)) is true && Values[entry.Key] is false)
            {
                Values[entry.Key] = true;
                changed = true;
            }
        }

        return changed;
    }

    private static string ForcedKeyFor(string slot, string enabledKey)
    {
        _ = slot;
        string prefix = enabledKey[..^"Enabled".Length];
        return prefix + "Forced";
    }
}
