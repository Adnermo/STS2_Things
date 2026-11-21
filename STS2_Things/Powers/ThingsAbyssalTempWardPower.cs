using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
/// 岩壳临时减伤 (Abyssal Carapace Ward) — 甲壳幼触在渊墨巨蛸蓄力回合施加给本体的 1 回合临时减伤。
/// 在接下来的玩家回合内，使持有者受到的攻击伤害降低 <c>Amount%</c>（上限 50%），
/// 在玩家回合结束（<c>Owner.Side != side</c>）时自动消退。
/// </summary>
public sealed class ThingsAbyssalTempWardPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredAttack())
            return 1m;
        if (dealer == null || dealer.Side == CombatSide.Enemy)
            return 1m;

        int pct = Math.Clamp(Amount, 0, 50);
        return pct > 0 ? Math.Max(0.50m, 1m - pct / 100m) : 1m;
    }

    public override Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        if (Amount > 0)
            Flash();
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (Owner.Side == side)
            return;

        Flash();
        await PowerCmd.Remove(this);
    }
}
