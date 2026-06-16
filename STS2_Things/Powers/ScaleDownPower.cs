using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Powers;

/// <summary>
///     缩小 — 玩家 Debuff，每层缩 1%，伤害减 1%
///     模仿 ShrinkPower，支持层数叠加
/// </summary>
public sealed class ScaleDownPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("PercentPerStack", 1m) };

    private float ScaleFactor =>
        (float)Math.Pow(1.0 - (double)DynamicVars["PercentPerStack"].BaseValue / 100.0, Amount);

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount,
        Creature? applier, CardModel? cardSource)
    {
        if (power == this) ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        // 每回合开始时重新应用缩放，确保视觉与层数同步
        ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        NCombatRoom.Instance?.GetCreatureNode(oldOwner)?.ScaleTo(1f, 0.75);
        return Task.CompletedTask;
    }

    private void ApplyScale()
    {
        NCombatRoom.Instance?.GetCreatureNode(Owner)?.ScaleTo(ScaleFactor, 0.75);
    }

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource)
    {
        if (Owner != dealer || !props.IsPoweredAttack())
            return 1m;
        return (100m - DynamicVars["PercentPerStack"].BaseValue * Amount) / 100m;
    }
}