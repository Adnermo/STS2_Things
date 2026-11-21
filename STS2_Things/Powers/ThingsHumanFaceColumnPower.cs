using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>One native counter displays shared layers and height-dependent protection.</summary>
public sealed class ThingsHumanFaceColumnPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool ShouldPlayVfx => false;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Stun)];
    public void RefreshHeightDisplay() => InvokeDisplayAmountChanged();
    private int Level => IsMutable && Owner?.Monster is ThingsHumanFaceColumn column ? column.Level : 0;
    protected override string SmartDescriptionLocKey => !IsMutable ? base.SmartDescriptionLocKey :
        Level switch
        {
            2 => "THINGS_HUMAN_FACE_COLUMN_POWER.topDescription",
            1 => "THINGS_HUMAN_FACE_COLUMN_POWER.middleDescription",
            _ => "THINGS_HUMAN_FACE_COLUMN_POWER.bottomDescription",
        };

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target != Owner ? 1m : Level >= 2 ? 0m : Level == 1 ? .5m : 1m;

    protected override decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target == Owner && Level >= 2 ? 0m : decimal.MaxValue;

    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource) => target == Owner && Level >= 2 ? 0m : amount;
}
