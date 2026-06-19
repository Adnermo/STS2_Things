using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 护巢本能 — 计数型Buff，玩家造成伤害即计数减1，计数归零时召唤2只盛碗虫并重置
/// 初始计数 = 最大HP * 20%
/// 召唤顺序固定：卵→蜜→岩→丝 循环（与 BowlbugProgenitor.NextBowlbugIndex 同步）
/// </summary>
public sealed class BowlbugProgenitorPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => false;

    // 初始计数（MaxHp * 20%），用于归零后重置
    private int _initialAmount;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StringVar("NextBowlbug1"),
        new StringVar("NextBowlbug2")
    };

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        _initialAmount = (int)Amount;
        UpdateBowlbugVars();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 召唤后刷新描述显示，实时更新预告
    /// </summary>
    public void RefreshDescription()
    {
        UpdateBowlbugVars();
        InvokeDisplayAmountChanged();
    }

    private void UpdateBowlbugVars()
    {
        var progenitor = Owner?.Monster as BowlbugProgenitor;
        if (progenitor != null)
        {
            int idx = progenitor.NextBowlbugIndex;
            ((StringVar)DynamicVars["NextBowlbug1"]).StringValue = GetBowlbugTitle(idx);
            ((StringVar)DynamicVars["NextBowlbug2"]).StringValue = GetBowlbugTitle((idx + 1) % 4);
        }
    }

    private static string GetBowlbugTitle(int idx) => idx switch
    {
        0 => ModelDb.Monster<BowlbugEgg>().Title.GetFormattedText(),
        1 => ModelDb.Monster<BowlbugNectar>().Title.GetFormattedText(),
        2 => ModelDb.Monster<BowlbugRock>().Title.GetFormattedText(),
        _ => ModelDb.Monster<BowlbugSilk>().Title.GetFormattedText()
    };

    /// <summary>
    /// Boss受到伤害时，按玩家造成的总伤害（含被盾挡的）计数减1，归零则召唤2只并重置
    /// </summary>
    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner) return;
        // 只计算来自玩家的伤害
        if (dealer == null || dealer.Side == CombatSide.Enemy) return;

        int damage = result.TotalDamage;
        if (damage <= 0 || Amount <= 0) return;

        // 单次伤害最多触发一次召唤；扣减 Amount 后归零才召唤 2 只并重置
        int reduce = Math.Min(damage, Amount);
        SetAmount(Amount - reduce);
        if (Amount <= 0)
        {
            Flash();
            // 召唤2只盛碗虫（固定顺序）
            await SummonNextBowlbug();
            await SummonNextBowlbug();
            // 重置计数为初始值
            SetAmount(_initialAmount);
        }
    }

    private async Task SummonNextBowlbug()
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

        // 从 BowlbugProgenitor 读取当前召唤索引
        var progenitor = Owner.Monster as BowlbugProgenitor;
        int idx = progenitor?.NextBowlbugIndex ?? 0;

        Creature creature = idx switch
        {
            0 => await CreatureCmd.Add<BowlbugEgg>(CombatState, slot),
            1 => await CreatureCmd.Add<BowlbugNectar>(CombatState, slot),
            2 => await CreatureCmd.Add<BowlbugRock>(CombatState, slot),
            _ => await CreatureCmd.Add<BowlbugSilk>(CombatState, slot)
        };

        // 推进索引
        if (progenitor != null)
            progenitor.NextBowlbugIndex = (progenitor.NextBowlbugIndex + 1) % 4;

        if (creature != null)
        {
            // 爪牙属性
            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), creature, 1m, Owner, null);
            // 召唤的盛碗虫只有原版50%的最大生命值
            int newMaxHp = (int)(creature.MaxHp * 0.5);
            creature.SetMaxHpInternal(newMaxHp);
            creature.SetCurrentHpInternal(newMaxHp);
            // 新召唤的盛碗虫初始意图为眩晕
            await CreatureCmd.Stun(creature);
        }

        // 召唤后刷新描述，实时更新预告
        RefreshDescription();
    }
}
