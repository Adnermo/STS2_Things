using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Enchantments;

/// <summary>
/// Overclocked: permanently reduces a card's energy cost by 1 (min 0),
/// at the cost of losing 3 HP whenever that card is played.
/// </summary>
public sealed class ThingsOverclocked : EnchantmentModel
{
    public const int CostReduction = 1;
    public const int HpLossAmount = 3;

    public override bool HasExtraCardText => true;

    public override bool ShouldGlowRed =>
        HasCard && !Card.IsCanonical && Card.Owner?.Creature != null && Card.Owner.Creature.CurrentHp <= HpLossAmount;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new HpLossVar(HpLossAmount)];

    public override bool CanEnchant(CardModel card) =>
        base.CanEnchant(card)
        && !card.EnergyCost.CostsX
        && card.EnergyCost.GetWithModifiers(CostModifiers.None) >= 2;

    protected override void OnEnchant()
    {
        Card.EnergyCost.UpgradeBy(-CostReduction);
    }

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay)
    {
        await Sts2VersionSeam.LoseUnblockableHpFromCardAsync(
            choiceContext,
            Card.Owner.Creature,
            DynamicVars.HpLoss.BaseValue,
            Card,
            cardPlay);
    }
}
