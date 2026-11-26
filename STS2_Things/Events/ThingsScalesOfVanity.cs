using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Glory (Act 3) vanity-punishing choice event ("虚荣天平 / Scales of Vanity").
/// - Empty Your Purse: lose ALL Gold (requires >= 250 Gold) -> gain 1 Rare Relic + remove up to 2 cards.
/// - Weigh Your Blood: lose 35% of current HP -> choose 1 of 3 Upgraded class Rare cards.
/// - Duck Under the Scale: randomly Transform 1 non-basic card (fallback: any transformable card).
/// </summary>
public sealed class ThingsScalesOfVanity : ThingsEventModel
{
    public const int MinGoldRequired = 250;
    public const int RemoveCardCount = 2;
    public const int BloodPercent = 35;
    public const int RareOfferCount = 3;
    public const int RareRelicFallbackGold = 150;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar("MinGold", MinGoldRequired),
        new CardsVar("RemoveCount", RemoveCardCount),
        new DamageVar("BloodDamage", 1m, ValueProp.Unblockable | ValueProp.Unpowered),
        new IntVar("BloodPercent", BloodPercent),
        new CardsVar("OfferCount", RareOfferCount),
        new GoldVar("FallbackGold", RareRelicFallbackGold),
    ];

    public static int ComputeBloodDamage(Player player) =>
        Math.Max(1, (int)Math.Floor(player.Creature.CurrentHp * (BloodPercent / 100m)));

    public override void CalculateVars()
    {
        if (Owner == null)
            return;

        DynamicVars["BloodDamage"].BaseValue = ComputeBloodDamage(EventOwner);
    }

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && ((player.Gold >= MinGoldRequired
                    && player.Deck.Cards.Count(card => card.IsRemovable) >= RemoveCardCount)
                || player.Creature.CurrentHp > 1));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "EMPTY_PURSE", "BLOOD_WEIGHT", "CRAWL_UNDER");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        int bloodDamage = ComputeBloodDamage(EventOwner);
        DynamicVars["BloodDamage"].BaseValue = bloodDamage;

        EventOption purseOption = EventOwner.Gold >= MinGoldRequired
            ? new EventOption(
                this,
                EmptyPurseAsync,
                InitialOptionKey(ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Rare)
                    ? "EMPTY_PURSE"
                    : "EMPTY_PURSE_GOLD"))
            : new EventOption(this, null, InitialOptionKey("EMPTY_PURSE_LOCKED"));

        EventOption bloodOption = new EventOption(
                this,
                BloodWeightAsync,
                InitialOptionKey("BLOOD_WEIGHT"))
            .ThatDoesDamage(bloodDamage);

        EventOption crawlOption = new EventOption(
            this,
            CrawlUnderAsync,
            InitialOptionKey("CRAWL_UNDER"),
            HoverTipFactory.Static(StaticHoverTip.Transform));

        return [purseOption, bloodOption, crawlOption];
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

    private async Task EmptyPurseAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (player.Gold < MinGoldRequired)
        {
            CancelToInitial();
            return;
        }

        int removableCount = player.Deck.Cards.Count(card => card.IsRemovable);
        int removeTarget = Math.Min(RemoveCardCount, removableCount);
        List<CardModel> toRemove = [];

        if (removeTarget > 0)
        {
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, removeTarget)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            };
            toRemove = (await CardSelectCmd.FromDeckForRemoval(player, prefs)).ToList();
            if (toRemove.Count == 0 || player.Creature.IsDead || player.Gold < MinGoldRequired)
            {
                CancelToInitial();
                return;
            }
        }

        await PlayerCmd.LoseGold(player.Gold, player, GoldLossType.Spent);
        if (toRemove.Count > 0)
            await CardPileCmd.RemoveFromDeck(toRemove);

        if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
            await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Rare);
        else
            await PlayerCmd.GainGold(RareRelicFallbackGold, player);

        Finish("EMPTY_PURSE.description");
    }

    private async Task BloodWeightAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        int damage = ComputeBloodDamage(player);
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, damage);
        if (player.Creature.IsDead)
        {
            Finish("BLOOD_WEIGHT.loss");
            return;
        }

        CardCreationOptions rareOptions = CardCreationOptions.ForNonCombatWithUniformOdds(
                [player.Character.CardPool],
                card => card.Rarity == CardRarity.Rare)
            .WithFlags(CardCreationFlags.NoRarityModification);
        List<CardCreationResult> offers = CardFactory.CreateForReward(player, RareOfferCount, rareOptions).ToList();

        foreach (CardCreationResult offer in offers)
        {
            if (offer.Card.IsUpgradable)
                CardCmd.Upgrade(offer.Card, CardPreviewStyle.None);
        }

        if (offers.Count > 0)
        {
            var prefs = new CardSelectorPrefs(
                L10NLookup($"{Id.Entry}.pages.BLOOD_WEIGHT.selectionScreenPrompt"),
                1);
            List<CardModel> chosen = (await CardSelectCmd.FromSimpleGridForRewards(
                new BlockingPlayerChoiceContext(),
                offers,
                player,
                prefs)).ToList();

            foreach (CardModel card in chosen)
            {
                CardPileAddResult added = await CardPileCmd.Add(card, PileType.Deck);
                CardCmd.PreviewCardPileAdd(added, 1.4f, CardPreviewStyle.EventLayout);
            }

            PlayerMapPointHistoryEntry? historyEntry = player.RunState.CurrentMapPointHistoryEntry?.GetEntry(player.NetId);
            if (historyEntry != null)
            {
                foreach (CardCreationResult skipped in offers.Where(o => !chosen.Contains(o.Card)))
                    historyEntry.CardChoices.Add(new CardChoiceHistoryEntry(skipped.Card, wasPicked: false));
            }
        }

        Finish("BLOOD_WEIGHT.description");
    }

    private async Task CrawlUnderAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        List<CardModel> candidates = player.Deck.Cards
            .Where(card => card.IsTransformable && card.Rarity != CardRarity.Basic)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = player.Deck.Cards
                .Where(card => card.IsTransformable)
                .ToList();
        }

        if (candidates.Count == 0 || Rng.NextItem(candidates) is not { } victim)
        {
            Finish("CRAWL_UNDER_EMPTY.description");
            return;
        }

        await CardCmd.TransformToRandom(victim, Rng, CardPreviewStyle.EventLayout);
        Finish("CRAWL_UNDER.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
