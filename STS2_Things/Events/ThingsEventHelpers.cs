using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Events;

internal static class ThingsEventHelpers
{
    public static bool HasGrabBagRelic(Player player, RelicRarity rarity) =>
        ModelDb.AllRelics.Any(relic =>
            relic.Rarity == rarity
            && relic.IsAllowed(player.RunState)
            && player.RelicGrabBag.Contains(relic)
            && player.Relics.All(owned => owned.Id != relic.Id));

    public static async Task ObtainGrabBagRelicAsync(Player player, RelicRarity rarity)
    {
        RelicModel relic = RelicFactory.PullNextRelicFromFront(
            player,
            rarity,
            candidate => player.Relics.All(owned => owned.Id != candidate.Id));
        await RelicCmd.Obtain(relic.ToMutable(), player);
    }

    public static bool CanReceivePotion(Player player, PotionModel? potion) =>
        potion != null
        && player.HasOpenPotionSlots
        && Hook.ShouldProcurePotion(player.RunState, player.Creature.CombatState, potion.ToMutable(), player);

    public static Task DealUnblockableDamageAsync(Player player, decimal amount) =>
        CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            player.Creature,
            amount,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

    public static async Task<CardModel?> SelectCardForEnchantmentAsync<TEnchantment>(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel?, bool>? filter = null) where TEnchantment : EnchantmentModel
    {
        EnchantmentModel canonical = ModelDb.Enchantment<TEnchantment>();
        CardSelectorPrefs effectivePrefs = prefs.Cancelable && prefs.MaxSelect == 1
            ? new CardSelectorPrefs(prefs.Prompt, 0, 1)
            {
                Cancelable = true,
                UnpoweredPreviews = prefs.UnpoweredPreviews,
                PretendCardsCanBePlayed = prefs.PretendCardsCanBePlayed,
            }
            : prefs;
        return (await CardSelectCmd.FromDeckForEnchantment(
            player,
            canonical,
            1,
            card => card != null && (filter?.Invoke(card) ?? canonical.CanEnchant(card)),
            effectivePrefs)).FirstOrDefault();
    }
}
