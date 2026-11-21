using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Enchantments;

/// <summary>
/// Weighted: permanently increases a card's energy cost by 1 while doubling
/// its attack damage and gained block.
/// </summary>
public sealed class ThingsWeighted : EnchantmentModel
{
    public const int CostIncrease = 1;
    public const decimal EffectMultiplier = 2m;

    public override bool HasExtraCardText => false;

    public override bool CanEnchantCardType(CardType cardType) =>
        cardType is CardType.Attack or CardType.Skill;

    public override bool CanEnchant(CardModel card) =>
        base.CanEnchant(card)
        && !card.EnergyCost.CostsX
        && !card.Keywords.Contains(CardKeyword.Unplayable)
        && (card.GainsBlock || HasPoweredDamageVar(card));

    protected override void OnEnchant()
    {
        Card.EnergyCost.UpgradeBy(CostIncrease);
    }

    public override decimal EnchantDamageMultiplicative(decimal originalDamage, ValueProp props) =>
        props.IsPoweredAttack() ? EffectMultiplier : 1m;

    public override decimal EnchantBlockMultiplicative(decimal originalBlock) =>
        EffectMultiplier;

    private static bool HasPoweredDamageVar(CardModel card) =>
        card.DynamicVars.Values.Any(static v => v switch
        {
            DamageVar dv => dv.Props.IsPoweredAttack(),
            CalculatedDamageVar cdv => cdv.Props.IsPoweredAttack(),
            OstyDamageVar odv => odv.Props.IsPoweredAttack(),
            _ => false,
        });
}
