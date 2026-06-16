using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// 盛碗虫族母 — Hive Act Boss
/// 状态机循环：召唤 → 干扰/激励(交替) → 准备 → 休息 → 召唤...
/// 护巢本能：累计受到>=20%最大HP伤害时召唤随机盛碗虫
/// </summary>
public sealed class BowlbugProgenitor : MonsterModel
{
    // 对应CustomBgm "act1_b_boss_soul_fysh" 的FMOD参数
    private const string _trackName = "soulfysh_progress";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 280, 260);
    public override int MaxInitialHp => MinInitialHp;

    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/fallback");

    // 复用SoulFysh（Hive Boss）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_summon";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Insect;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 1f);
        // 护巢本能：计数=10%最大HP，每受1点伤害减1，归零召唤并重置
        await PowerCmd.Apply<BowlbugProgenitorPower>(
            new ThrowingPlayerChoiceContext(), Creature, Creature.MaxHp * 0.1m, Creature, null);
    }

    public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature == Creature)
        {
            NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 5f);
        }
        return Task.CompletedTask;
    }

    // ========== 状态机 ==========

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var states = new List<MonsterState>();

        // 1. 召唤：召唤1只盛碗虫 + 获得(玩家数*20)防御
        var summon = new MoveState("SUMMON_MOVE", SummonMove,
            new SummonIntent(), new DefendIntent());

        // 2a. 干扰：获得(玩家数*16)防御 + 给所有玩家2层虚弱
        var disrupt = new MoveState("DISRUPT_MOVE", DisruptMove,
            new DefendIntent(), new DebuffIntent());

        // 2b. 激励：召唤1只盛碗虫 + 全体怪物+3力量
        var rally = new MoveState("RALLY_MOVE", RallyMove,
            new SummonIntent(), new BuffIntent());

        // 3. 准备：什么都不做
        var prepare = new MoveState("PREPARE_MOVE", PrepareMove,
            new UnknownIntent());

        // 4. 休息：获得10层再生 + 回复(玩家数*10)生命
        var rest = new MoveState("REST_MOVE", RestMove,
            new BuffIntent(), new HealIntent());

        // 使用RandomBranchState + CannotRepeat实现干扰/激励交替
        var branch = new RandomBranchState("DISRUPT_RALLY_BRANCH");
        branch.AddBranch(disrupt, 0, MoveRepeatType.CannotRepeat, 1f);
        branch.AddBranch(rally, 0, MoveRepeatType.CannotRepeat, 1f);

        // 循环：召唤 → 随机分支(干扰/激励交替) → 准备 → 休息 → 召唤...
        summon.FollowUpState = branch;
        disrupt.FollowUpState = prepare;
        rally.FollowUpState = prepare;
        prepare.FollowUpState = rest;
        rest.FollowUpState = summon;

        states.Add(summon);
        states.Add(branch);
        states.Add(disrupt);
        states.Add(rally);
        states.Add(prepare);
        states.Add(rest);

        return new MonsterMoveStateMachine(states, summon);
    }

    // ========== 招式实现 ==========

    private int PlayerCount => CombatState.Players.Count;

    /// <summary>随机召唤1只盛碗虫（4种中随机）并眩晕，场上盛碗虫达到16只时取消</summary>
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

        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.75f);

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
            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), creature, 1m, Creature, null);
            // 召唤的盛碗虫只有原版60%的最大生命值
            int newMaxHp = (int)(creature.MaxHp * 0.6);
            creature.SetMaxHpInternal(newMaxHp);
            creature.SetCurrentHpInternal(newMaxHp);
            // 新召唤的盛碗虫初始意图为眩晕
            await CreatureCmd.Stun(creature);
        }
    }

    /// <summary>1. 召唤：召唤1只盛碗虫 + 获得(玩家数*20)防御</summary>
    private async Task SummonMove(IReadOnlyList<Creature> targets)
    {
        await SummonRandomBowlbug();
        await CreatureCmd.GainBlock(Creature, PlayerCount * 20, ValueProp.Unpowered, null, fast: true);
    }

    /// <summary>2a. 干扰：获得(玩家数*16)防御 + 给所有玩家2层虚弱</summary>
    private async Task DisruptMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.GainBlock(Creature, PlayerCount * 16, ValueProp.Unpowered, null, fast: true);
        await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(),
            CombatState.Players.Select(p => p.Creature).ToArray(), 2m, Creature, null);
    }

    /// <summary>2b. 激励：召唤1只盛碗虫 + 全体怪物+3力量</summary>
    private async Task RallyMove(IReadOnlyList<Creature> targets)
    {
        await SummonRandomBowlbug();
        // 全体怪物获得+3力量
        foreach (var enemy in CombatState.Enemies.Where(e => e.IsAlive))
        {
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(),
                enemy, 3m, Creature, null);
        }
    }

    /// <summary>3. 准备：休息1回合</summary>
    private Task PrepareMove(IReadOnlyList<Creature> targets)
    {
        return Task.CompletedTask;
    }

    /// <summary>4. 休息：获得10层再生 + 回复(玩家数*10)生命</summary>
    private async Task RestMove(IReadOnlyList<Creature> targets)
    {
        await PowerCmd.Apply<RegenPower>(new ThrowingPlayerChoiceContext(),
            Creature, 10m, Creature, null);
        await CreatureCmd.Heal(Creature, PlayerCount * 10);
    }
}
