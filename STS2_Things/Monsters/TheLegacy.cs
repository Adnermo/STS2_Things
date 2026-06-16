using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using STS2_Things.Audio;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
///     腐化之遗 — Underdocks Boss
///     开局: BeatOfDeathPower (玩家出牌扣2血) + HardenedShellPower (HP/4 层)
///     意图循环:
///     衰竭 → (回音 or 血弹 随机) → 另一招 → 强化 → 循环
///     每轮随机决定先回音还是先血弹，保证不连续重复
/// </summary>
public sealed class TheLegacy : MonsterModel
{
    // 对应CustomBgm "act1_boss_vantom" 的FMOD参数
    private const string _trackName = "vantom_progress";
    private const int BloodDamage = 2;

    private const int BloodCount = 6;

    // 复用Vantom（暗黑Boss）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_inky_lance";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_buff";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember";

    // 硬化外壳的 HP 除数: 开局用 [0]=4, 强化后用 [count]
    private static readonly int[] _shellDivisors = { 4, 5, 6, 7, 8, 9, 10 };

    private bool _randomPickEcho;
    private int _strengthenCount;
    private volatile bool _heartbeatActive;

    protected override string VisualsPath =>
        SceneHelper.GetScenePath("creature_visuals/fallback");

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 298, 285);

    public override int MaxInitialHp => MinInitialHp;

    private int EchoDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 13, 10);

    // ========== 入场 ==========
    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        // 初始化专属音乐参数（CustomBgm = act1_boss_vantom）
        NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 1f);
        // 心跳循环：血量越低跳得越快
        _heartbeatActive = true;
        _ = HeartbeatLoop();
        // 死亡律动: 玩家每出一张牌扣 2 血
        await PowerCmd.Apply<BeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null);
        // 硬化外壳: 层数 = 最大 HP / 4
        await PowerCmd.Apply<HardenedShellPower>(new ThrowingPlayerChoiceContext(), Creature,
            Creature.MaxHp / 4m, Creature, null);
    }

    // ========== 状态机 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        List<MonsterState> list = new();

        // 衰竭: 给玩家 Burn + Dazed + Slimed 各一张
        var exhaustMove = new MoveState("EXHAUST_MOVE", ExhaustMove,
            new StatusIntent(6));

        // 回音(在先): 15/17 伤害 → 下接血弹
        var echo1Move = new MoveState("ECHO1_MOVE", EchoMove,
            new SingleAttackIntent(EchoDamage));
        // 回音(在后): 15/17 伤害 → 下接强化
        var echo2Move = new MoveState("ECHO2_MOVE", EchoMove,
            new SingleAttackIntent(EchoDamage));

        // 血弹(在先): 3×4 → 下接回音
        var blood1Move = new MoveState("BLOOD1_MOVE", BloodMove,
            new MultiAttackIntent(BloodDamage, BloodCount));
        // 血弹(在后): 3×4 → 下接强化
        var blood2Move = new MoveState("BLOOD2_MOVE", BloodMove,
            new MultiAttackIntent(BloodDamage, BloodCount));

        // 强化: +1 力量
        var strengthenMove = new MoveState("STRENGTHEN_MOVE", StrengthenMove,
            new BuffIntent());

        // 条件分支: 衰竭后随机选回音或血弹
        var firstDamage = new ConditionalBranchState("FIRST_DAMAGE");
        firstDamage.AddState(echo1Move, () => _randomPickEcho);
        firstDamage.AddState(blood1Move, () => true);

        // 连线
        exhaustMove.FollowUpState = firstDamage;
        echo1Move.FollowUpState = blood2Move; // 回音1 → 血弹2 → 强化
        blood1Move.FollowUpState = echo2Move; // 血弹1 → 回音2 → 强化
        blood2Move.FollowUpState = strengthenMove;
        echo2Move.FollowUpState = strengthenMove;
        strengthenMove.FollowUpState = firstDamage; // 强化 → 回音/血弹（衰竭仅开局一次）

        list.Add(exhaustMove);
        list.Add(echo1Move);
        list.Add(echo2Move);
        list.Add(blood1Move);
        list.Add(blood2Move);
        list.Add(strengthenMove);
        list.Add(firstDamage);

        return new MonsterMoveStateMachine(list, exhaustMove);
    }

    // ========== 招式 ==========

    private async Task ExhaustMove(IReadOnlyList<Creature> targets)
    {
        // 随机决定本轮先出哪招
        _randomPickEcho = Random.Shared.Next(2) == 0;

        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        await CardPileCmd.AddToCombatAndPreview<Burn>(targets, PileType.Discard, 2, null);
        await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, 2, null);
        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 2, null);
    }

    private async Task EchoMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(EchoDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task BloodMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(BloodDamage)
            .FromMonster(this)
            .WithHitCount(BloodCount)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task StrengthenMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        // 力量 +1
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null);

        // 硬化外壳层数递减: HP/4 → HP/5 → HP/6 → ... → HP/10(封顶)
        _strengthenCount++;
        var idx = Math.Min(_strengthenCount, _shellDivisors.Length - 1);
            var shell = Creature.Powers.OfType<HardenedShellPower>().FirstOrDefault();
        if (shell != null)
        {
            var target = Creature.MaxHp / (decimal)_shellDivisors[idx];
            await PowerCmd.ModifyAmount(new ThrowingPlayerChoiceContext(), shell,
                target - shell.Amount, Creature, null);
        }
    }

    // ========== 死亡升调 ==========
    public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature == Creature)
        {
            _heartbeatActive = false;
            var ctrl = NRunMusicController.Instance;
            // 死亡升调（vantom_progress=5 触发FMOD升调自动化）
            NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 5f);
        }
        return Task.CompletedTask;
    }

    public override void BeforeRemovedFromRoom()
    {
        _heartbeatActive = false;
    }

    // ========== 心跳循环（半血后加速） ==========

    private async Task HeartbeatLoop()
    {
        var files = new[]
        {
            "res://sfx/the_legacy/HeartBeat_1.ogg",
            "res://sfx/the_legacy/HeartBeat_2.ogg"
        };
        var index = 0;

        while (_heartbeatActive && Creature is { IsAlive: true })
        {
            NativeSfxPlayer.Play(files[index], volumeDb: -10f);
            index = (index + 1) % files.Length;
            // 半血后心跳加速: 0.75s vs 正常 1.5s
            float delay = Creature.CurrentHp <= Creature.MaxHp / 2 ? 0.75f : 1.5f;
            await Task.Delay((int)(delay * 1000));
        }
    }
}