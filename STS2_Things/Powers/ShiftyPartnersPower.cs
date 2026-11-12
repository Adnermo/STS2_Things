using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Powers;

/// <summary>
/// The Shifty Shop's two native Fake Merchants hit softer than the vanilla one, so the pair fights
/// like a single Act 1 elite (docs/depths-balance-20261110.md). Native intents include the reduction
/// through the same hook. A buff, not a debuff, so debuff-counting cards gain nothing from it.
/// </summary>
public sealed class ShiftyPartnersPower : PowerModel
{
    public const decimal DamageMultiplier = 0.6m;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override bool IsVisibleInternal => false;
#if STS2_V107_1
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource) =>
#else
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay) =>
#endif
        dealer == Owner && props.IsPoweredAttack() ? DamageMultiplier : 1m;
}
