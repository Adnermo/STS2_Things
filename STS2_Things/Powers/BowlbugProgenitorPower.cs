using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Powers;

/// <summary>
/// 护巢本能 — 计数型Buff，每受1点伤害计数减1，计数归零时召唤1只随机盛碗虫并重置
/// 初始计数 = 最大HP * 10%
/// </summary>
public sealed class BowlbugProgenitorPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => false;

    // 初始计数（MaxHp * 10%），用于归零后重置
    private int _initialAmount;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new IHoverTip[]
        {
            HoverTipFactory.FromPower<WeakPower>()
        };

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        _initialAmount = (int)Amount;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Boss受到伤害时，每1点伤害计数减1，归零则召唤并重置
    /// </summary>
    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner) return;
        // 只计算来自玩家的伤害
        if (dealer == null || dealer.Side == CombatSide.Enemy) return;

        int damage = result.UnblockedDamage;
        while (damage > 0 && Amount > 0)
        {
            int reduce = Math.Min(damage, Amount);
            damage -= reduce;
            SetAmount(Amount - reduce);
            if (Amount <= 0)
            {
                Flash();
                await SummonRandomBowlbug();
                // 重置计数为初始值
                SetAmount(_initialAmount);
            }
        }
    }

    private async Task SummonRandomBowlbug()
    {
        if (!CombatState.IsLiveCombat()) return;

        // 盛碗虫数量上限16
        int bowlbugCount = CombatState.Enemies.Where(e => e.IsAlive &&
            (e.Monster is BowlbugSilk || e.Monster is BowlbugRock ||
             e.Monster is BowlbugNectar || e.Monster is BowlbugEgg)).Count();
        if (bowlbugCount >= 16) return;

        // 获取下一个空闲的盛碗虫站位
        string slot = CombatState.Encounter?.GetNextSlot(CombatState) ?? "";
        if (string.IsNullOrEmpty(slot)) return;

        var idx = Rng.Chaotic.NextInt(4);
        Creature creature = idx switch
        {
            0 => await CreatureCmd.Add<BowlbugSilk>(CombatState, slot),
            1 => await CreatureCmd.Add<BowlbugRock>(CombatState, slot),
            2 => await CreatureCmd.Add<BowlbugNectar>(CombatState, slot),
            _ => await CreatureCmd.Add<BowlbugEgg>(CombatState, slot)
        };

        if (creature != null)
        {
            // 爪牙属性
            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), creature, 1m, Owner, null);
            // 召唤的盛碗虫只有原版60%的最大生命值
            int newMaxHp = (int)(creature.MaxHp * 0.6);
            creature.SetMaxHpInternal(newMaxHp);
            creature.SetCurrentHpInternal(newMaxHp);
            // 新召唤的盛碗虫初始意图为眩晕
            await CreatureCmd.Stun(creature);
        }
    }
}
