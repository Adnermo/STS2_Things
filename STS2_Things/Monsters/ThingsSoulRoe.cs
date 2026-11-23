using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Monsters;

/// <summary>
///     灵魂鱼子 — 固定3回合循环的脆皮小怪（原生 Spine 4.2 环绕微籽骨骼）
///     支持从任意招式开始循环（由调用方设置 StartMovePhase）
///     支持入场眩晕（StartStunned = true）
///     招式循环: Intangible → Beckon → Damage → Intangible → ...
///     Phase 0 = Intangible: 珠光点亮鱼籽并给自己一层无实体（power_up）
///     Phase 1 = Beckon:     暖金点亮鱼籽并给玩家弃牌堆塞一张 Beckon（cast）
///     Phase 2 = Damage:     离心加速将周围漂浮微籽甩出造成 6 点伤害（attack）
/// </summary>
public sealed class ThingsSoulRoe : ThingsSpineMonster
{
    private const int _damage = 6;
    public const float BiteFlingContact = 0.42f;
    public const float BeckonBrightPeak = 0.50f;
    public const float IntangibleBrightPeak = 0.52f;

    protected override string AttackSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_attack";
    protected override string CastSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_beckon";
    public override string DeathSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override Vector2 ExtraDeathVfxPadding => new(2.2f, 5.0f);
    public override float HpBarSizeReduction => 48f;

    private static readonly string[] _visualVariants =
    [
        SceneHelper.GetScenePath("creature_visuals/soul_roe"),
        SceneHelper.GetScenePath("creature_visuals/soul_roe_2"),
        SceneHelper.GetScenePath("creature_visuals/soul_roe_3")
    ];
    private int _startMovePhase;

    // ========== 可配置属性（必须在 ToMutable() 后设置） ==========

    private bool _startStunned;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _startMovePhase = 0;
        _startStunned = false;
    }

    // 变体选择只依赖同步的起始阶段；所有变体都进入 AssetPaths，避免动态召唤时临时加载。
    protected override string VisualsPath =>
        _visualVariants[Math.Abs(_startMovePhase % _visualVariants.Length)];

    public override IEnumerable<string> AssetPaths =>
        base.AssetPaths.Concat(_visualVariants).Distinct();

    // ========== 血量 ==========
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 5, 3);

    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 6, 5);

    /// <summary>入场第一回合是否眩晕（死亡生成时设为 true）</summary>
    public bool StartStunned
    {
        get => _startStunned;
        set
        {
            AssertMutable();
            _startStunned = value;
        }
    }

    /// <summary>
    ///     起始招式阶段:
    ///     0 = IntangibleMove（给自己无实体）
    ///     1 = BeckonMove（给玩家 Beckon）
    ///     2 = DamageMove（造成 6 伤害）
    /// </summary>
    public int StartMovePhase
    {
        get => _startMovePhase;
        set
        {
            AssertMutable();
            _startMovePhase = value;
        }
    }

    private Task WaitForPose(string animation, float moment, float fallback)
        => WaitForSpineMomentAsync(animation, moment, fallback);

    private async Task BeginMotion(string trigger, string animation, float contact)
    {
        await CreatureCmd.TriggerAnim(Creature, trigger, 0f);
        await WaitForPose(animation, contact, contact);
    }

    private async Task FinishMotion(string animation)
    {
        var sprite = Creature.GetCreatureNode()?.Visuals?.SpineBody;
        float end;
        using (TrackEntryScope(sprite?.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? track))
        {
            if (track == null || track.GetAnimationName() != animation) return;
            end = track.GetAnimationEnd();
        }
        await WaitForPose(animation, end, 0f);
    }

    // ========== 状态机 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();

        // --- 三招招式 ---
        var intangibleMove = new MoveState(
            "INTANGIBLE_MOVE", IntangibleMove, new BuffIntent());
        var beckonMove = new MoveState(
            "BECKON_MOVE", BeckonMove, new StatusIntent(1));
        var damageMove = new MoveState(
            "DAMAGE_MOVE", DamageMove, new SingleAttackIntent(_damage));

        // --- 眩晕招式（入场第一回合什么都不做） ---
        var stunnedMove = new MoveState(
            "STUNNED_MOVE", StunnedMove, new StunIntent());

        // --- 三招固定循环 ---
        intangibleMove.FollowUpState = beckonMove;
        beckonMove.FollowUpState = damageMove;
        damageMove.FollowUpState = intangibleMove;

        // --- 条件分支：根据 StartMovePhase 选择起始招式 ---
        var initBranch = new ConditionalBranchState("INIT_BRANCH");
        initBranch.AddState(intangibleMove, () => _startMovePhase == 0);
        initBranch.AddState(beckonMove, () => _startMovePhase == 1);
        initBranch.AddState(damageMove, () => _startMovePhase == 2);

        // 眩晕后进入条件分支
        stunnedMove.FollowUpState = initBranch;

        list.Add(intangibleMove);
        list.Add(beckonMove);
        list.Add(damageMove);
        list.Add(stunnedMove);
        list.Add(initBranch);

        var initialState = _startStunned
            ? stunnedMove
            : (MonsterState)initBranch;

        return new MonsterMoveStateMachine(list, initialState);
    }

    // ========== Stunned: 空操作 ==========
    private Task StunnedMove(IReadOnlyList<Creature> targets)
    {
        return Task.CompletedTask;
    }

    // ========== Intangible: 珠光点亮自身 +1 无实体 ==========
    private async Task IntangibleMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await BeginMotion("PowerUp", "power_up", IntangibleBrightPeak);
        await PowerCmd.Apply<IntangiblePower>(new ThrowingPlayerChoiceContext(),
            Creature, 2m, Creature, null);
        await FinishMotion("power_up");
    }

    // ========== Beckon: 暖金点亮鱼籽 + 给玩家弃牌堆 +1 虚无消耗呼唤 ==========
    private async Task BeckonMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await BeginMotion("Cast", "cast", BeckonBrightPeak);
        await ThingsSoulRoes.AddFleetingBeckonsAndPreviewAsync(targets, 1);
        await FinishMotion("cast");
    }

    // ========== Damage: 离心加速甩出漂浮微籽造成 6 点伤害 ==========
    private async Task DamageMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(_damage)
            .FromMonster(this)
            .WithNoAttackerAnim()
            .AfterAttackerAnim(() => BeginMotion("Attack", "attack", BiteFlingContact))
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
        await FinishMotion("attack");
    }
}
