using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Platform;

/// <summary>
/// Seamlessly migrates legacy STS2_Things ModelIds (114 renamed + 22 retired models = 136 entries)
/// to current v2.0.0 THINGS_* ModelIds across progress.save, current_run.save, and run_history/*.run.
/// Inactive during ModelDb discovery and automatically prunes any key registered by vanilla or another mod.
/// </summary>
public static class LegacyIdMigrator
{
    public readonly record struct LegacyMapping(string Category, string LegacyEntry, string NewEntry, bool IsRetired);

    private static readonly LegacyMapping[] DeclaredMappings =
    [
        new("POWER", "ABSORBENT_SPONGE_POWER", "THINGS_ABSORBENT_SPONGE_POWER", false),
        new("RELIC", "ANESTHETIC_CHART", "THINGS_ANESTHETIC_CHART", false),
        new("EVENT", "BITING_CHEST", "THINGS_BITING_CHEST", false),
        new("RELIC", "BORROWED_EMBER", "THINGS_BORROWED_EMBER", false),
        new("POWER", "BORROWED_SHADOW_POWER", "THINGS_BORROWED_SHADOW_POWER", false),
        new("RELIC", "BOTTLED_ECHO", "THINGS_BOTTLED_ECHO", false),
        new("MONSTER", "BOWLBUG_PROGENITOR", "THINGS_BOWLBUG_PROGENITOR", false),
        new("ENCOUNTER", "BOWLBUG_PROGENITOR_BOSS_ENCOUNTER", "THINGS_BOWLBUG_PROGENITOR_BOSS_ENCOUNTER", false),
        new("POWER", "BOWLBUG_PROGENITOR_POWER", "THINGS_BOWLBUG_PROGENITOR_POWER", false),
        new("POWER", "BOWLBUG_SEQUENCE_POWER", "THINGS_BOWLBUG_SEQUENCE_POWER", false),
        new("POWER", "CAVE_GOD_ARCANE_POWER", "THINGS_CAVE_GOD_ANCIENT_CORE_POWER", true),
        new("CARD", "CAVE_GOD_ARCANE_TRIAL", "THINGS_SNAIL_CRYSTAL_CHIP", true),
        new("ENCOUNTER", "CAVE_GOD_BOSS_ENCOUNTER", "THINGS_CAVE_GOD_BOSS_ENCOUNTER", false),
        new("POWER", "CAVE_GOD_BROKEN_BLADE_POWER", "THINGS_CAVE_GOD_BROKEN_BLADE_POWER", false),
        new("CARD", "CAVE_GOD_BROKEN_BLADE_TRIAL", "THINGS_CAVE_GOD_BROKEN_BLADE_TRIAL", false),
        new("POWER", "CAVE_GOD_MARTIAL_POWER", "THINGS_CAVE_GOD_ANCIENT_CORE_POWER", true),
        new("CARD", "CAVE_GOD_MARTIAL_TRIAL", "THINGS_SNAIL_CRYSTAL_CHIP", true),
        new("POWER", "CAVE_GOD_PENDING_TRIAL_POWER", "THINGS_CAVE_GOD_PENDING_TRIAL_POWER", false),
        new("POWER", "CAVE_GOD_SHATTERED_SHIELD_POWER", "THINGS_CAVE_GOD_SHATTERED_SHIELD_POWER", false),
        new("CARD", "CAVE_GOD_SHATTERED_SHIELD_TRIAL", "THINGS_CAVE_GOD_SHATTERED_SHIELD_TRIAL", false),
        new("MONSTER", "CAVE_MAW", "THINGS_CAVE_MAW", false),
        new("POWER", "CAVE_MAW_APPETITE_POWER", "THINGS_CAVE_MAW_APPETITE_POWER", false),
        new("ENCOUNTER", "CAVE_MAW_ENCOUNTER", "THINGS_CAVE_MAW_LANTERN_ENCOUNTER", true),
        new("ENCOUNTER", "CAVE_MAW_LANTERN_ENCOUNTER", "THINGS_CAVE_MAW_LANTERN_ENCOUNTER", false),
        new("ENCOUNTER", "CAVE_MAW_SNAIL_ENCOUNTER", "THINGS_CAVE_MAW_LANTERN_ENCOUNTER", true),
        new("ENCOUNTER", "CAVE_MAW_WEAK", "THINGS_CAVE_MAW_WEAK", false),
        new("EVENT", "CROWDED_WARD", "THINGS_CROWDED_WARD", false),
        new("MONSTER", "CRYSTAL_SNAIL", "THINGS_CRYSTAL_SNAIL", false),
        new("EVENT", "CUTTING_IT_CLOSE", "THINGS_CUTTING_IT_CLOSE", false),
        new("ACT", "DEPTHS", "THINGS_DEPTHS", false),
        new("EVENT", "ECHOING_WELL", "THINGS_ECHOING_WELL", false),
        new("MONSTER", "FLEETING_ECHO", "THINGS_FLEETING_ECHO", false),
        new("ENCOUNTER", "FLEETING_ECHO_WEAK", "THINGS_FLEETING_ECHO_WEAK", false),
        new("POWER", "FLEETING_FADE_POWER", "THINGS_FLEETING_FADE_POWER", false),
        new("MONSTER", "GRAVETIDE_CORPSE_SLUG", "THINGS_GRAVETIDE_CORPSE_SLUG", false),
        new("POWER", "GRAVETIDE_DIGESTION_POWER", "THINGS_GRAVETIDE_DIGESTION_POWER", false),
        new("POWER", "GRAVETIDE_MINION_POWER", "THINGS_GRAVETIDE_MINION_POWER", false),
        new("MONSTER", "GRAVETIDE_SLUG", "THINGS_GRAVETIDE_SLUG", false),
        new("ENCOUNTER", "GRAVETIDE_SLUG_BOSS_ENCOUNTER", "THINGS_GRAVETIDE_SLUG_BOSS_ENCOUNTER", false),
        new("MONSTER", "GRAVETIDE_SLUG_CORPSE", "THINGS_GRAVETIDE_SLUG_CORPSE", false),
        new("MONSTER", "GREAT_SILK_MOTH", "THINGS_GREAT_SILK_MOTH", false),
        new("MONSTER", "HUMAN_FACE_COLUMN", "THINGS_HUMAN_FACE_COLUMN", false),
        new("ENCOUNTER", "HUMAN_FACE_COLUMN_ENCOUNTER", "THINGS_HUMAN_FACE_COLUMN_ENCOUNTER", false),
        new("POWER", "HUMAN_FACE_COLUMN_POWER", "THINGS_HUMAN_FACE_COLUMN_POWER", false),
        new("ENCOUNTER", "HUMAN_FACE_COLUMN_WEAK", "THINGS_HUMAN_FACE_COLUMN_ENCOUNTER", true),
        new("POWER", "LANTERN_BLINDNESS_POWER", "THINGS_LANTERN_BLINDNESS_POWER", false),
        new("MONSTER", "LANTERN_FISH", "THINGS_LANTERN_FISH", false),
        new("ENCOUNTER", "LANTERN_FISH_ENCOUNTER", "THINGS_LANTERN_FISH_ENCOUNTER", false),
        new("ENCOUNTER", "LANTERN_FISH_WEAK", "THINGS_LANTERN_FISH_WEAK", false),
        new("ENCOUNTER", "LANTERN_MOTH_ENCOUNTER", "THINGS_LANTERN_SPONGE_ENCOUNTER", true),
        new("ENCOUNTER", "LANTERN_SPONGE_ENCOUNTER", "THINGS_LANTERN_SPONGE_ENCOUNTER", false),
        new("POWER", "LEECH_INFESTATION_POWER", "THINGS_LEECH_INFESTATION_POWER", false),
        new("MONSTER", "LEECH_MOTHER", "THINGS_LEECH_MOTHER", false),
        new("ENCOUNTER", "LEECH_MOTHER_ENCOUNTER", "THINGS_LEECH_MOTHER_ENCOUNTER", false),
        new("POWER", "LEECH_MOTHER_SLUMBER_POWER", "THINGS_LEECH_MOTHER_SLUMBER_POWER", false),
        new("CARD", "LEECH_PARASITE", "THINGS_LEECH_PARASITE", false),
        new("POWER", "LEGACY_BEAT_OF_DEATH_POWER", "THINGS_LEGACY_BEAT_OF_DEATH_POWER", false),
        new("POWER", "LEGACY_PROGRESS_POWER", "THINGS_LEGACY_PROGRESS_POWER", false),
        new("EVENT", "MYCELIAL_BANK", "THINGS_MYCELIAL_BANK", false),
        new("RELIC", "MYCELIAL_DEPOSIT", "THINGS_MYCELIAL_DEPOSIT", false),
        new("POWER", "MYCORRHIZAL_BOND_POWER", "THINGS_MYCORRHIZAL_BOND_POWER", false),
        new("MONSTER", "MYCORRHIZAL_BULWARK", "THINGS_MYCORRHIZAL_BULWARK", false),
        new("POWER", "MYCORRHIZAL_FURY_POWER", "THINGS_MYCORRHIZAL_FURY_POWER", false),
        new("ENCOUNTER", "MYCORRHIZAL_TWINS_ELITE", "THINGS_MYCORRHIZAL_TWINS_ELITE", false),
        new("MONSTER", "MYCORRHIZAL_VANGUARD", "THINGS_MYCORRHIZAL_VANGUARD", false),
        new("EVENT", "NARROW_GATE", "THINGS_NARROW_GATE", false),
        new("MONSTER", "ORIGIN_EYE_WITH_TEETH", "THINGS_ORIGIN_EYE_WITH_TEETH", false),
        new("MONSTER", "ORIGIN_FOGMOG", "THINGS_ORIGIN_FOGMOG", false),
        new("ENCOUNTER", "ORIGIN_FOGMOG_BOSS_ENCOUNTER", "THINGS_ORIGIN_FOGMOG_BOSS_ENCOUNTER", false),
        new("POWER", "ORIGIN_GAIN_ENERGY_POWER", "THINGS_ORIGIN_GAIN_ENERGY_POWER", false),
        new("EVENT", "POLITE_MAW", "THINGS_POLITE_MAW", false),
        new("EVENT", "POTION_TASTING", "THINGS_POTION_TASTING", false),
        new("POWER", "PROTECT_THE_MASTER_POWER", "THINGS_PROTECT_THE_MASTER_POWER", false),
        new("POWER", "QUIRKY_FLUTTER_POWER", "THINGS_QUIRKY_FLUTTER_POWER", false),
        new("MONSTER", "QUIRKY_HOPPER", "THINGS_QUIRKY_HOPPER", false),
        new("MODIFIER", "QUIRKY_HOPPER_REWARD_POLICY", "THINGS_QUIRKY_HOPPER_REWARD_POLICY", false),
        new("ENCOUNTER", "QUIRKY_HOPPER_WEAK", "THINGS_QUIRKY_HOPPER_WEAK", false),
        new("MONSTER", "RADIO_JELLYFISH", "THINGS_RADIO_JELLYFISH", false),
        new("ENCOUNTER", "RADIO_JELLYFISH_ELITE", "THINGS_RADIO_JELLYFISH_ELITE", false),
        new("POWER", "RADIO_RECEPTION_POWER", "THINGS_RADIO_RECEPTION_POWER", false),
        new("POWER", "RADIO_RECEPTION_STATE_POWER", "THINGS_RADIO_RECEPTION_STATE_POWER", false),
        new("EVENT", "REALITY_ALIGNED_HOUSES", "THINGS_REALITY_ALIGNED_HOUSES", false),
        new("EVENT", "RELIC_WORKSHOP", "THINGS_RELIC_WORKSHOP", false),
        new("POWER", "REVERSE_CURRENT_POWER", "THINGS_REVERSE_CURRENT_POWER", false),
        new("MONSTER", "REVERSE_SALAMANDER", "THINGS_REVERSE_SALAMANDER", false),
        new("ENCOUNTER", "REVERSE_SALAMANDER_ELITE", "THINGS_REVERSE_SALAMANDER_ELITE", false),
        new("EVENT", "ROBBERY_FAKE_MERCHANT", "THINGS_ROBBERY_FAKE_MERCHANT", false),
        new("ENCOUNTER", "ROBBERY_FAKE_MERCHANT_ENCOUNTER", "THINGS_ROBBERY_FAKE_MERCHANT_ENCOUNTER", false),
        new("POWER", "ROCK_CRAWL_STATE_POWER", "THINGS_ROCK_CRAWL_STATE_POWER", false),
        new("MONSTER", "ROCK_SNAIL", "THINGS_ROCK_SNAIL", false),
        new("MONSTER", "SANGUINE_LEECH", "THINGS_SANGUINE_LEECH", false),
        new("ENCOUNTER", "SANGUINE_LEECH_ENCOUNTER", "THINGS_SANGUINE_LEECH_ENCOUNTER", false),
        new("ENCOUNTER", "SANGUINE_LEECH_WEAK", "THINGS_SANGUINE_LEECH_WEAK", false),
        new("ENCOUNTER", "SCALE_BEETLE_BOSS_ENCOUNTER", "THINGS_SCALE_BEETLE_BOSS_ENCOUNTER", false),
        new("RELIC", "SHADOW_CLAIM_TICKET", "THINGS_SHADOW_CLAIM_TICKET", false),
        new("EVENT", "SHADOW_CLOAKROOM", "THINGS_SHADOW_CLOAKROOM", false),
        new("POWER", "SHIFTY_PARTNERS_POWER", "THINGS_SHIFTY_PARTNERS_POWER", false),
        new("AFFLICTION", "SILK_BOUND", "THINGS_SILK_BOUND", false),
        new("AFFLICTION", "SILK_LEAD", "THINGS_SILK_LEAD", false),
        new("MONSTER", "SILK_MOTH", "THINGS_SILK_MOTH", false),
        new("ENCOUNTER", "SILK_MOTH_ENCOUNTER", "THINGS_SILK_MOTH_TRIO", true),
        new("ENCOUNTER", "SILK_MOTH_TRIO", "THINGS_SILK_MOTH_TRIO", false),
        new("ENCOUNTER", "SILK_MOTH_WEAK", "THINGS_SNAIL_TRIO_WEAK", true),
        new("ENCOUNTER", "SILK_SNAIL_ENCOUNTER", "THINGS_SILK_SNAIL_ENCOUNTER", false),
        new("POWER", "SILK_THREAD_POWER", "THINGS_SILK_THREAD_POWER", false),
        new("MONSTER", "SLIME_SNAIL", "THINGS_SLIME_SNAIL", false),
        new("CARD", "SNAIL_CRYSTAL_CHIP", "THINGS_SNAIL_CRYSTAL_CHIP", false),
        new("POWER", "SNAIL_SHELL_POWER", "THINGS_SNAIL_SHELL_POWER", false),
        new("ENCOUNTER", "SNAIL_TRIO_WEAK", "THINGS_SNAIL_TRIO_WEAK", false),
        new("MONSTER", "SOUL_ROE", "THINGS_SOUL_ROE", false),
        new("MONSTER", "SOUL_ROES", "THINGS_SOUL_ROES", false),
        new("ENCOUNTER", "SOUL_ROES_ENCOUNTER", "THINGS_SOUL_ROES_ENCOUNTER", false),
        new("POWER", "SOUL_ROES_POWER", "THINGS_SOUL_ROES_POWER", false),
        new("CARD", "SOULFYSH_DISEASE", "THINGS_SOULFYSH_DISEASE", false),
        new("POWER", "SOULFYSH_DISEASE_POWER", "THINGS_SOULFYSH_DISEASE_POWER", false),
        new("MODIFIER", "SPENT_EMBER_REST_POLICY", "THINGS_SPENT_EMBER_REST_POLICY", false),
        new("POWER", "SPONGE_DAMAGE_PROGRESS_POWER", "THINGS_SPONGE_DAMAGE_PROGRESS_POWER", false),
        new("ENCOUNTER", "SPONGE_LEECH_ENCOUNTER", "THINGS_SPONGE_LEECH_ENCOUNTER", false),
        new("ENCOUNTER", "SPONGE_LEECH_WEAK", "THINGS_SANGUINE_LEECH_WEAK", true),
        new("POWER", "SPONGE_RESERVOIR_POWER", "THINGS_SPONGE_RESERVOIR_POWER", false),
        new("POWER", "SPONGE_RINSE_POWER", "THINGS_SPONGE_RINSE_POWER", false),
        new("ENCOUNTER", "SPONGE_SNAIL_ENCOUNTER", "THINGS_SPONGE_LEECH_ENCOUNTER", true),
        new("ENCOUNTER", "THE_LEGACY_BOSS_ENCOUNTER", "THINGS_THE_LEGACY_BOSS_ENCOUNTER", false),
        new("CARD", "THINGS_CAVE_GOD_CRYSTAL_SHARD", "THINGS_SNAIL_CRYSTAL_CHIP", true),
        new("POWER", "THINGS_CAVE_GOD_CRYSTAL_VEIN_POWER", "THINGS_CAVE_GOD_AGING_POWER", true),
        new("POWER", "THINGS_CAVE_GOD_FISSURE_POWER", "THINGS_CAVE_GOD_ANCIENT_CORE_POWER", true),
        new("ENCOUNTER", "THINGS_CAVE_MAW_ENCOUNTER", "THINGS_CAVE_MAW_LANTERN_ENCOUNTER", true),
        new("ENCOUNTER", "THINGS_CAVE_MAW_SNAIL_ENCOUNTER", "THINGS_CAVE_MAW_LANTERN_ENCOUNTER", true),
        new("ENCOUNTER", "THINGS_LANTERN_MOTH_ENCOUNTER", "THINGS_LANTERN_SPONGE_ENCOUNTER", true),
        new("ENCOUNTER", "THINGS_SILK_MOTH_ENCOUNTER", "THINGS_SILK_MOTH_TRIO", true),
        new("ENCOUNTER", "THINGS_SILK_MOTH_WEAK", "THINGS_SNAIL_TRIO_WEAK", true),
        new("ENCOUNTER", "THINGS_SPONGE_LEECH_WEAK", "THINGS_SANGUINE_LEECH_WEAK", true),
        new("ENCOUNTER", "THINGS_SPONGE_SNAIL_ENCOUNTER", "THINGS_SPONGE_LEECH_ENCOUNTER", true),
        new("EVENT", "UNLIT_FIRE", "THINGS_UNLIT_FIRE", false),
        new("AFFLICTION", "UPSTREAM_RECALL", "THINGS_UPSTREAM_RECALL", false),
        new("MONSTER", "WATER_SPONGE", "THINGS_WATER_SPONGE", false),
    ];

    private static FrozenDictionary<(string Category, string Entry), string> _activeMap =
        FrozenDictionary<(string Category, string Entry), string>.Empty;
    private static readonly List<LegacyMapping> _prunedCollisions = [];
    private static volatile bool _active;

    public static IReadOnlyList<LegacyMapping> AllDeclaredMappings => DeclaredMappings;
    public static IReadOnlyList<LegacyMapping> PrunedCollisions => _prunedCollisions;
    public static int TotalDeclaredCount => DeclaredMappings.Length;
    public static int ActiveMigrationCount => _activeMap.Count;
    public static bool IsActive => _active;

    public static void RebuildAndActivate(Func<string, string, bool>? isRegisteredCollision = null)
    {
        _active = false;
        _prunedCollisions.Clear();
        var check = isRegisteredCollision ?? IsRegisteredInModelDb;
        var dict = new Dictionary<(string Category, string Entry), string>(DeclaredMappings.Length);

        foreach (LegacyMapping mapping in DeclaredMappings)
        {
            if (check(mapping.Category, mapping.LegacyEntry))
            {
                _prunedCollisions.Add(mapping);
                Log.Warn($"[STS2_Things] LegacyIdMigrator skipped {mapping.Category}.{mapping.LegacyEntry} because a live model owns that ID.");
                continue;
            }
            dict[(mapping.Category, mapping.LegacyEntry)] = mapping.NewEntry;
        }

        _activeMap = dict.ToFrozenDictionary();
        _active = true;
    }

    public static bool TryMigrate(string category, ref string entry)
    {
        if (!_active || string.IsNullOrEmpty(entry) || string.IsNullOrEmpty(category))
            return false;
        if (_activeMap.TryGetValue((category, entry), out string? mapped))
        {
            entry = mapped;
            return true;
        }
        return false;
    }

    public static ModelId MigrateModelId(ModelId id)
    {
        string entry = id.Entry;
        return TryMigrate(id.Category, ref entry) ? new ModelId(id.Category, entry) : id;
    }

    private static bool IsRegisteredInModelDb(string category, string entry)
    {
        // _active is false here, so new ModelId(category, entry) is never rewritten.
        return ModelDb.GetByIdOrNull<AbstractModel>(new ModelId(category, entry)) != null;
    }

    public static void Deactivate() => _active = false;

    public static void ActivateIfModelDbReady()
    {
        if (_active)
            return;
        try
        {
            if (ModelDb.GetByIdOrNull<AbstractModel>(new ModelId("CARD", "THINGS_COLLISION")) != null)
                RebuildAndActivate();
        }
        catch (Exception)
        {
            // ModelDb is not initialized yet; ModelDbInitIdsPatch.Postfix will activate migration.
        }
    }

    [HarmonyPatch]
    private static class ModelDbInitPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
            => AccessTools.FirstMethod(typeof(ModelDb), static m => m.Name == nameof(ModelDb.Init));

        private static void Prefix() => Deactivate();
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Inject), [typeof(Type)])]
    private static class ModelDbInjectPatch
    {
        private static void Prefix(out bool __state)
        {
            __state = _active;
            _active = false;
        }

        private static void Postfix(Type type, bool __state)
        {
            if (__state)
                RebuildAndActivate();
            else
                _ = type;
        }
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
    private static class ModelDbInitIdsPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            RebuildAndActivate();
        }
    }

    [HarmonyPatch(typeof(ModelId), MethodType.Constructor, [typeof(string), typeof(string)])]
    private static class ModelIdConstructorPatch
    {
        private static void Prefix(string category, ref string entry)
        {
            TryMigrate(category, ref entry);
        }
    }
}
