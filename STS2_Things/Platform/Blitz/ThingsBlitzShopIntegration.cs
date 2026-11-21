#if STS2_V113_1
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Blitz;
using MegaCrit.Sts2.Core.Rewards;
using STS2_Things.Enchantments;
using STS2_Things.Relics;

namespace STS2_Things.Platform.Blitz;

public static class ThingsBlitzShopIntegration
{
    public const float TriviaRelicOfferChance = 0.35f;

    public static void EnsureModShopEnchantments()
    {
        List<(EnchantmentModel, int)>? options = BlitzRunModel.shopEnchantmentOptions;
        if (options is null)
            return;

        (EnchantmentModel Enchantment, int Amount)[] additions =
        [
            (ModelDb.Enchantment<ThingsDisperse>(), 1),
            (ModelDb.Enchantment<ThingsOverclocked>(), 1),
            (ModelDb.Enchantment<ThingsWeighted>(), 1),
        ];

        foreach ((EnchantmentModel enchantment, int amount) in additions)
        {
            if (!options.Any(existing => existing.Item1.Id == enchantment.Id))
                options.Add((enchantment, amount));
        }
    }
}

public sealed class ThingsBlitzTriviaRelicEntry : BlitzMerchantEntry
{
    public override BlitzMerchantEntryType MerchantEntryType => BlitzMerchantEntryType.Relic;

    public override LocString Title => new LocString("merchant_room", "MERCHANT.blitz.things_trivia_relic_entry.title");

    public override LocString Description => new LocString("merchant_room", "MERCHANT.blitz.things_trivia_relic_entry.description");

    public ThingsBlitzTriviaRelicEntry(Player player)
        : base(player)
    {
    }

    public static IReadOnlyList<RelicModel> GetEligibleTriviaRelics(Player player)
    {
        RelicModel[] pool =
        [
            ModelDb.Relic<ThingsAlmondWater>(),
            ModelDb.Relic<ThingsMedusaHair>(),
            ModelDb.Relic<ThingsAmberCombCore>(),
            ModelDb.Relic<ThingsMagicGlove>(),
            ModelDb.Relic<ThingsWhiteFlag>(),
            ModelDb.Relic<ThingsCurseRemover>(),
        ];

        return pool
            .Where(relic => !player.Relics.Any(owned => owned.Id == relic.Id) && relic.IsAllowed(player.RunState))
            .OrderBy(relic => relic.Id)
            .ToList();
    }

    protected override async Task<bool> OnTrySelect()
    {
        IReadOnlyList<RelicModel> candidates = GetEligibleTriviaRelics(_player);
        RelicModel? relic = candidates.Count > 0
            ? _player.PlayerRng.Shops.NextItem(candidates)
            : null;
        relic ??= RelicFactory.PullNextRelicFromBack(
            _player,
            RelicFactory.RollRarity(_player.PlayerRng.Shops),
            static _ => true);

        await RewardsCmd.OfferCustom(_player, [new RelicReward(relic.ToMutable(), _player)]);
        return true;
    }
}

[HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForBlitzMerchant))]
internal static class ThingsBlitzShopEnchantmentPatch
{
    private static void Prefix() => ThingsBlitzShopIntegration.EnsureModShopEnchantments();
}

[HarmonyPatch(typeof(MerchantInventory), "GetBlitzEntries", [typeof(Player)])]
internal static class ThingsBlitzShopTriviaRelicPatch
{
    private static void Postfix(Player player, ref List<BlitzMerchantEntry> __result)
    {
        if (player is null || __result is null || __result.Count == 0)
            return;

        if (ThingsBlitzTriviaRelicEntry.GetEligibleTriviaRelics(player).Count == 0)
            return;

        if (player.PlayerRng.Shops.NextFloat() >= ThingsBlitzShopIntegration.TriviaRelicOfferChance)
            return;

        int replaceIndex = -1;
        for (int i = 0; i < __result.Count; i++)
        {
            if (!__result[i].IsCursed && __result[i] is BlitzMerchantRelicEntry)
            {
                replaceIndex = i;
                break;
            }
        }

        if (replaceIndex < 0)
        {
            List<int> nonRareSlots = [];
            List<int> nonCursedSlots = [];
            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i].IsCursed)
                    continue;
                nonCursedSlots.Add(i);
                if (!__result[i].IsRare)
                    nonRareSlots.Add(i);
            }

            List<int> candidateSlots = nonRareSlots.Count > 0 ? nonRareSlots : nonCursedSlots;
            if (candidateSlots.Count == 0)
                return;

            replaceIndex = candidateSlots[player.PlayerRng.Shops.NextInt(candidateSlots.Count)];
        }

        __result[replaceIndex] = new ThingsBlitzTriviaRelicEntry(player);
    }
}
#endif
