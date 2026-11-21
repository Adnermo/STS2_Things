using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using STS2_Things.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Cards;

/// <summary>
/// Exhaustible 0-cost Status card that increases the cost of another random card
/// in hand by 1 for the current turn when drawn.
/// </summary>
public sealed class ThingsStickyBeeswax : ThingsCardModel
{
    public const int CostIncrease = 1;

    private bool _pendingPenalty;

    public ThingsStickyBeeswax() : base(0, CardType.Status, CardRarity.Status, TargetType.None) { }

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();

    protected override IEnumerable<string> ExtraRunAssetPaths => [PortraitPath];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(CostIncrease)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [base.EnergyHoverTip];

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _pendingPenalty = false;
    }

    private static bool IsEligiblePenaltyTarget(CardModel self, CardModel candidate) =>
        candidate != self && !candidate.EnergyCost.CostsX && candidate.EnergyCost.Canonical >= 0;

    private void ApplyPenalty(CardModel target)
    {
        target.EnergyCost.AddThisTurnOrUntilPlayed(DynamicVars.Energy.IntValue);
        Sts2VersionSeam.PlayCardCostFeedback(target);
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (Owner == null || Owner.Creature.IsDead || CombatState == null || CombatManager.Instance.IsOverOrEnding)
            return Task.CompletedTask;
        if (card.Owner != Owner || Pile?.Type != PileType.Hand)
            return Task.CompletedTask;

        if (card == this)
        {
            List<CardModel> candidates = PileType.Hand.GetPile(Owner).Cards
                .Where(candidate => IsEligiblePenaltyTarget(this, candidate))
                .ToList();
            if (candidates.Count > 0 && Owner.RunState.Rng.CombatEnergyCosts.NextItem(candidates) is { } target)
            {
                _pendingPenalty = false;
                ApplyPenalty(target);
            }
            else
            {
                // Drawn before other cards in the same batch reached Hand; tag the next eligible draw.
                _pendingPenalty = true;
            }
            return Task.CompletedTask;
        }

        if (_pendingPenalty && card.Pile?.Type == PileType.Hand && IsEligiblePenaltyTarget(this, card))
        {
            _pendingPenalty = false;
            ApplyPenalty(card);
        }

        return Task.CompletedTask;
    }

    public override Task BeforeFlush(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player == Owner)
            _pendingPenalty = false;
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (Owner != null && side == Owner.Creature.Side)
            _pendingPenalty = false;
        return Task.CompletedTask;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? source)
    {
        if (card == this && Pile?.Type != PileType.Hand)
            _pendingPenalty = false;
        return Task.CompletedTask;
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;
}
