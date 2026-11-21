using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 磐岩护体（Granite Bulwark）：当双臂均未倒下时，活体巨岩本体受到的伤害变为 25%。
/// 击倒任意一臂会解除本效果，直到巨岩苏醒恢复双臂。
/// </summary>
public sealed class ThingsCaveGodAncientCorePower : ThingsPowerModel
{
    public const decimal ProtectedDamageMultiplier = 0.25m;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (target != Owner)
            return 1m;
        if (Owner.Monster is ThingsCaveGodBody { BothArmsHealthy: false })
            return 1m;
        return ProtectedDamageMultiplier;
    }

    public override Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        Flash();
        return Task.CompletedTask;
    }
}
