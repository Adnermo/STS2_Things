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
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths event offering Tezcatara's Ember (0 cost + Eternal + +3 Attack damage) for 10 Max HP + Eternal Burn,
/// 9-HP Removal that grants a Common Relic if the burned card was Rare, or a 10-HP Heal.
/// </summary>
public sealed class ThingsCrackedBrazier : ThingsEventModel
{
    public const int TemperMaxHpLoss = 10;
    public const int BurnDamage = 9;
    public const int WarmHeal = 10;
    internal const string EternalKeywordSavedPropertyName = nameof(ThingsEternalKeyword);

    private bool _settling;

    // Registered with ModelIdSerializationCache so SavedProperties can carry added
    // CardKeyword.Eternal on non-Eternal cards (such as Eternal Burn) over JSON and MP packets.
    // Always false on the event instance so it is never written to event save data.
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    private bool ThingsEternalKeyword
    {
        get => false;
        set { _ = value; }
    }

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
        new LocalizedStringVar("Burn", static _ => new LocString("cards", "BURN.title").GetFormattedText()),
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
            .Concat(ModelDb.Card<Burn>().AllPortraitPaths)
            .Distinct();

    private static bool CanTemper(CardModel? card) =>
        card != null
        && !card.EnergyCost.CostsX
        && ModelDb.Enchantment<TezcatarasEmber>().CanEnchant(card);

    private static CardModel CreateEternalBurnPreview()
    {
        CardModel preview = ModelDb.Card<Burn>().ToMutable();
        preview.AddKeyword(CardKeyword.Eternal);
        return preview;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool lowMaxHp = EventOwner.Creature.MaxHp <= TemperMaxHpLoss;
        bool hasTemperCard = EventOwner.Deck.Cards.Any(CanTemper);
        CardModel previewBurn = CreateEternalBurnPreview();
        IEnumerable<IHoverTip> temperHoverTips = IHoverTip.RemoveDupes(
            HoverTipFactory.FromEnchantment<TezcatarasEmber>()
                .Append(HoverTipFactory.FromCard(previewBurn))
                .Concat(previewBurn.HoverTips));
        EventOption temperOption = (lowMaxHp, hasTemperCard) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("TEMPER_LOW_HP"),
                temperHoverTips),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("TEMPER_NO_CARD"),
                temperHoverTips),
            _ => new EventOption(
                this,
                TemperCard,
                InitialOptionKey("TEMPER"),
                temperHoverTips)
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
        CardModel burn = player.RunState.CreateCard<Burn>(player);
        burn.AddKeyword(CardKeyword.Eternal);
        CardPileAddResult burnResult = await CardPileCmd.Add(burn, PileType.Deck);
        CardCmd.PreviewCardPileAdd([burnResult], 2f);
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
