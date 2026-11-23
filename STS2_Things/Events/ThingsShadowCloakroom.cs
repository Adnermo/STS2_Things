using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

/// <summary>A per-player deposit and coat swap cloakroom.</summary>
public sealed class ThingsShadowCloakroom : ThingsEventModel
{
    public const int DepositCost = 45;
    public const int SwapCost = 70;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(DepositCost), new GoldVar("SwapGold", SwapCost), new CardsVar(1)];

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "DEPOSIT", "SWAP_COAT", "LEAVE");

    private static bool CanDeposit(CardModel card) => card.IsRemovable && card.IsUpgradable &&
        card.Type is CardType.Attack or CardType.Skill or CardType.Power;

    private static bool HasEligibleCard(Player player) =>
        player.Deck.Cards.Count > 1 && player.Deck.Cards.Any(CanDeposit);

    private static bool CanSwap(CardModel card) => card.Type != CardType.Quest && card.IsTransformable;

    private static bool HasSwappableCard(Player player) => player.Deck.Cards.Any(CanSwap);

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.GetRelic<ThingsShadowClaimTicket>() == null
            && ((player.Gold >= DepositCost && HasEligibleCard(player))
                || (player.Gold >= SwapCost && HasSwappableCard(player))));

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        string depositKey = EventOwner.GetRelic<ThingsShadowClaimTicket>() != null ? "ALREADY_STORED" :
            !HasEligibleCard(EventOwner) ? "NO_CARD" :
            EventOwner.Gold < DepositCost ? "NO_GOLD" : "DEPOSIT";
        string swapKey = !HasSwappableCard(EventOwner) ? "SWAP_NO_CARD" :
            EventOwner.Gold < SwapCost ? "SWAP_NO_GOLD" : "SWAP_COAT";
        return
        [
            new EventOption(this, depositKey == "DEPOSIT" ? Deposit : null, InitialOptionKey(depositKey),
                ModelDb.Relic<ThingsShadowClaimTicket>().HoverTips),
            new EventOption(this, swapKey == "SWAP_COAT" ? SwapCoat : null, InitialOptionKey(swapKey),
                HoverTipFactory.Static(StaticHoverTip.Transform)),
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private async Task Deposit()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        var selected = (await CardSelectCmd.FromDeckForRemoval(player,
            new CardSelectorPrefs(L10NLookup("THINGS_SHADOW_CLOAKROOM.selectionPrompt"), 1)
                { Cancelable = true, RequireManualConfirmation = true },
            CanDeposit)).FirstOrDefault();

        // Revalidate after the synchronized selection; nothing is charged on cancel.
        if (selected == null || player.Creature.IsDead || !HasEligibleCard(player) ||
            !player.Deck.Cards.Contains(selected) || !CanDeposit(selected) ||
            player.Gold < DepositCost || player.GetRelic<ThingsShadowClaimTicket>() != null)
        {
            _settling = false;
            if (player.Creature.IsDead) Finish("ABORTED");
            else SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        var ticket = (ThingsShadowClaimTicket)ModelDb.Relic<ThingsShadowClaimTicket>().ToMutable();
        ticket.Store(selected, player.RunState.CurrentActIndex);
        await RelicCmd.Obtain(ticket, player);
        await CardPileCmd.RemoveFromDeck(selected);
        await PlayerCmd.LoseGold(DepositCost, player, GoldLossType.Spent);
        Finish("DEPOSITED");
    }

    private async Task SwapCoat()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        var selected = (await CardSelectCmd.FromDeckForTransformation(player,
            new CardSelectorPrefs(L10NLookup("THINGS_SHADOW_CLOAKROOM.swapPrompt"), 1)
                { Cancelable = true, RequireManualConfirmation = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !HasSwappableCard(player) ||
            !player.Deck.Cards.Contains(selected) || !CanSwap(selected) || player.Gold < SwapCost)
        {
            _settling = false;
            if (player.Creature.IsDead) Finish("ABORTED");
            else SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        CardModel replacement = CardFactory.CreateRandomCardForTransform(selected, isInCombat: false, Rng);
        if (replacement.IsUpgradable) CardCmd.Upgrade(replacement);
        await CardCmd.Transform(selected, replacement, CardPreviewStyle.EventLayout);
        await PlayerCmd.LoseGold(SwapCost, player, GoldLossType.Spent);
        Finish("SWAP_COAT");
    }

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        Finish("LEAVE");
        return Task.CompletedTask;
    }

    private void Finish(string page)
    {
        if (!IsFinished) SetEventFinished(L10NLookup($"THINGS_SHADOW_CLOAKROOM.pages.{page}.description"));
    }
}
