using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths event offering Tezcatara's Ember (0 cost + Eternal + +3 Attack damage) for 10 Max HP,
/// 9-HP Removal that grants a Common Relic if the burned card was Rare, or a 10-HP Heal.
/// </summary>
public sealed class ThingsCrackedBrazier : ThingsEventModel
{
    public const int TemperMaxHpLoss = 10;
    public const int BurnDamage = 9;
    public const int WarmHeal = 10;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new MaxHpVar(TemperMaxHpLoss),
        new DamageVar("BurnDamage", BurnDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new HealVar("WarmHeal", WarmHeal),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.MaxHp > TemperMaxHpLoss
            && player.Deck.Cards.Any(CanTemper));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "TEMPER", "BURN_CARD", "WARM_HANDS");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<TezcatarasEmber>().IconPath])
            .Distinct();

    private static bool CanTemper(CardModel? card) =>
        card != null
        && !card.EnergyCost.CostsX
        && ModelDb.Enchantment<TezcatarasEmber>().CanEnchant(card);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool lowMaxHp = EventOwner.Creature.MaxHp <= TemperMaxHpLoss;
        bool hasTemperCard = EventOwner.Deck.Cards.Any(CanTemper);
        EventOption temperOption = (lowMaxHp, hasTemperCard) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("TEMPER_LOW_HP"),
                HoverTipFactory.FromEnchantment<TezcatarasEmber>()),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("TEMPER_NO_CARD"),
                HoverTipFactory.FromEnchantment<TezcatarasEmber>()),
            _ => new EventOption(
                this,
                TemperCard,
                InitialOptionKey("TEMPER"),
                HoverTipFactory.FromEnchantment<TezcatarasEmber>())
                .ThatDecreasesMaxHp(TemperMaxHpLoss),
        };

        bool hasRemovable = EventOwner.Deck.Cards.Any(card => card.IsRemovable);
        EventOption burnOption = hasRemovable
            ? new EventOption(this, BurnCard, InitialOptionKey("BURN_CARD"))
                .ThatDoesDamage(BurnDamage)
            : new EventOption(this, null, InitialOptionKey("BURN_NO_CARD"));

        return
        [
            temperOption,
            burnOption,
            new EventOption(this, WarmHands, InitialOptionKey("WARM_HANDS")),
        ];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private void CancelToInitial()
    {
        _settling = false;
        if (EventOwner.Creature.IsDead)
            Finish("ABORTED.description");
        else
            SetEventState(InitialDescription, GenerateInitialOptions());
    }

    private async Task TemperCard()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (player.Creature.MaxHp <= TemperMaxHpLoss || !player.Deck.Cards.Any(CanTemper))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = await ThingsEventHelpers.SelectCardForEnchantmentAsync<TezcatarasEmber>(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.temperPrompt"), 1) { Cancelable = true, RequireManualConfirmation = true },
            CanTemper);

        if (selected == null || player.Creature.IsDead || player.Creature.MaxHp <= TemperMaxHpLoss ||
            !player.Deck.Cards.Contains(selected) || !CanTemper(selected))
        {
            CancelToInitial();
            return;
        }

        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), TemperMaxHpLoss);
        if (player.Creature.IsDead)
        {
            Finish("TEMPER.loss");
            return;
        }

        CardCmd.Enchant<TezcatarasEmber>(selected, 1m);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish("TEMPER.description");
    }

    private async Task BurnCard()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (!player.Deck.Cards.Any(card => card.IsRemovable))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForRemoval(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.burnPrompt"), 1) { Cancelable = true, RequireManualConfirmation = true },
            card => card.IsRemovable)).FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !player.Deck.Cards.Contains(selected) || !selected.IsRemovable)
        {
            CancelToInitial();
            return;
        }

        bool wasRare = selected.Rarity == CardRarity.Rare;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, BurnDamage);
        if (player.Creature.IsDead)
        {
            Finish("BURN_CARD.loss");
            return;
        }

        await CardPileCmd.RemoveFromDeck([selected]);
        if (wasRare && ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
        {
            await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Common);
            Finish("BURN_RARE.description");
            return;
        }

        Finish("BURN_CARD.description");
    }

    private async Task WarmHands()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Heal(EventOwner.Creature, WarmHeal);
        Finish("WARM_HANDS.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
