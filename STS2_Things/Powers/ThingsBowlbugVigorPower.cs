using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Powers;

/// <summary>
/// 虫群活力 — 怪物兼容版活力（原版 VigorPower 在 BeforeAttack 中限制仅 CardModel 来源攻击触发消耗，
/// 怪物招式攻击不会消耗原版活力）。
/// 使持有者的下一次受力量加成攻击（PoweredAttack）额外造成等同层数的伤害，并在该次攻击结算后移除。
/// </summary>
public sealed class ThingsBowlbugVigorPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (Owner.IsAlive)
        {
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        if (oldOwner.IsAlive)
        {
            Sts2VersionSeam.RefreshMonsterIntentsUi(oldOwner);
        }
        return Task.CompletedTask;
    }

    protected override decimal OnModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
        => dealer == Owner && props.IsPoweredAttack() ? Amount : 0m;

    public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
    {
        if (command.Attacker == Owner && command.DamageProps.IsPoweredAttack() && Owner.IsAlive)
        {
            Flash();
            await PowerCmd.Remove(this);
        }
    }
}
