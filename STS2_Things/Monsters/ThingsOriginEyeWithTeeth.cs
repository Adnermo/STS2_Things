using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
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
using HarmonyLib;
using STS2_Things.Core.Models;
using STS2_Things.Platform;
using STS2_Things.Powers;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Monsters;

public sealed class ThingsOriginEyeWithTeeth : ThingsMonsterModel
{
    public const string DistractMoveId = "DISTRACT_MOVE";
    public const string BiteMoveId = "BITE_MOVE";
    public const string HealBossMoveId = "HEAL_BOSS_MOVE";
    public const string StunAfterReviveMoveId = "STUN_AFTER_REVIVE";
    private const int BiteRepeat = 2;
    private const int NourishStrength = 1;
    private bool _startsWithBite;

    private int DistractDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 6, 5);

    private int DistractDazedCount => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 3, 2);

    private int BiteDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 5, 4);

    private int HealAmountPerPlayer => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 8, 6);

    public bool StartsWithBite
    {
        get => _startsWithBite;
        set
        {
            AssertMutable();
            _startsWithBite = value;
        }
    }

    protected override string AttackSfx =>
        "event:/sfx/enemy/enemy_attacks/eye_with_teeth/eye_with_teeth_attack";
    // EyeWithTeeth has no death event in the V110 SFX bank. The Obscura
    // hologram event is the native illusion-disappearance equivalent.
    public override string DeathSfx =>
        "event:/sfx/enemy/enemy_attacks/obscura/obscura_hologram_die";

    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/origin_eye_with_teeth");

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 16, 14) + (IsDread() ? 2 : 0);

    public override int MaxInitialHp => MinInitialHp;

    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;

    public override float HpBarSizeReduction => 30f;

    public override bool ShouldDisappearFromDoom => false;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await ApplyIllusionPower();
    }

    /// <summary>
    /// 施加 IllusionPower 并设定复活后先眩晕一回合，再衔接对应起手招式。
    /// CreatureCmd.Add 会调用 AfterAddedToRoom，因此动态召唤无需重复施加。
    /// </summary>
    private async Task ApplyIllusionPower()
    {
        await PowerCmd.Apply<IllusionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        var illusion = Creature.GetPower<IllusionPower>();
        if (illusion != null)
        {
            illusion.FollowUpStateId = StunAfterReviveMoveId;
        }
    }

    protected override bool ShouldShowMoveInBestiary(string moveStateId) =>
        moveStateId != StunAfterReviveMoveId && base.ShouldShowMoveInBestiary(moveStateId);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();
        var distract = new MoveState(
            DistractMoveId,
            DistractMove,
            new SingleAttackIntent(DistractDamage),
            new StatusIntent(DistractDazedCount));
        var bite = new MoveState(
            BiteMoveId,
            BiteMove,
            new MultiAttackIntent(BiteDamage, BiteRepeat));
        var healBoss = new MoveState(
            HealBossMoveId,
            HealBossMove,
            new HealIntent(),
            new BuffIntent());

        distract.FollowUpState = bite;
        bite.FollowUpState = healBoss;
        healBoss.FollowUpState = distract;

        // 二阶段召唤的 illusion2 从双噬起步，与开场 illusion1 错峰配合。
        // 留空 MustPerformOnceBeforeTransitioning，确保玩家在复活眩晕回合再次击杀幻象时
        // 原生 IllusionPower.AfterDeath 可直接切换回 REVIVE_MOVE。
        var initialMove = StartsWithBite ? bite : distract;
        var stunAfterRevive = new MoveState(
            StunAfterReviveMoveId,
            _ => Task.CompletedTask,
            new StunIntent())
        {
            FollowUpState = initialMove
        };

        list.Add(distract);
        list.Add(bite);
        list.Add(healBoss);
        list.Add(stunAfterRevive);

        return new MonsterMoveStateMachine(list, initialMove);
    }

    private async Task DistractMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(DistractDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.55f)
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
        await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, DistractDazedCount, null);
    }

    private async Task BiteMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(BiteDamage)
            .WithHitCount(BiteRepeat)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.25f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
    }

    private async Task HealBossMove(IReadOnlyList<Creature> targets)
    {
        var combatState = Creature.CombatState;
        if (combatState == null) return;

        SfxCmd.Play(AttackSfx);
        await CreatureCmd.TriggerAnim(Creature, "Attack", 0.45f);

        var boss = combatState.GetTeammatesOf(Creature)
            .FirstOrDefault(c => c.IsPrimaryEnemy && c.IsAlive);
        if (boss != null)
        {
            // 多人游戏时回血按玩家数 scaling，与 OriginFogmog 的 HealMove 保持一致
            var healAmount = HealAmountPerPlayer * MultiplayerCompat.GetEffectivePlayerCount(combatState);
            await CreatureCmd.Heal(boss, healAmount);
            // 仅赋予始源雾菇本体（固定 Slots[0] 先手行动）+1 力量，避免同回合内前序眼给后序攻击眼加力导致意图失真
            await PowerCmd.Apply<StrengthPower>(
                new ThrowingPlayerChoiceContext(),
                boss,
                NourishStrength,
                Creature,
                null);
        }
    }

    public override void SetupSkins(MegaSprite spine, MegaSkeleton skeleton)
    {
        base.SetupSkins(spine, skeleton);
        if (spine.BoundObject is CanvasItem visuals)
            // Preserve the shipped hand-painted value range.  The old saturated
            // Preserve the shipped Eye atlas palette exactly.  Illusion identity
            // comes from its native powers/VFX rather than a destructive tint.
            visuals.Modulate = Colors.White;
    }

    /// <summary>
    /// Origin-exclusive Eye rig (animations/monsters/origin_eye_with_teeth, source in
    /// tools/OriginEyeRig).  Besides the native Attack/Dead triggers it maps the
    /// IllusionPower lifecycle: StunTrigger holds the collapsed "die" pose while the
    /// illusion waits to revive, WakeUpTrigger plays "revive", Hit plays "hurt".
    /// </summary>
    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var animState = new AnimState("idle_loop", true);
        var animState2 = new AnimState("attack");
        var hurt = new AnimState("hurt");
        var revive = new AnimState("revive");
        var state = new AnimState("die");
        var downed = new AnimState("die");
        animState2.NextState = animState;
        hurt.NextState = animState;
        revive.NextState = animState;
        var creatureAnimator = new CreatureAnimator(animState, controller);
        creatureAnimator.AddAnyState("Attack", animState2);
        creatureAnimator.AddAnyState(CreatureAnimator.hitTrigger, hurt);
        creatureAnimator.AddAnyState(IllusionPower.stunTrigger, downed);
        creatureAnimator.AddAnyState(IllusionPower.wakeUpTrigger, revive);
        creatureAnimator.AddAnyState("Dead", state,
            () => !CombatState.GetTeammatesOf(Creature).Any(t => t != null && t.IsPrimaryEnemy && t.IsAlive));
        return creatureAnimator;
    }
}

/// <summary>
/// 保证两个一次性强制状态不会被已有的 MustPerformOnce 状态静默吞掉。
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.SetMoveImmediate))]
public static class RequiredMonsterMoveTransitionPatch
{
    private static void Prefix(MonsterModel __instance, MoveState state, ref bool forceTransition)
    {
        if (__instance is ThingsOriginEyeWithTeeth && state.StateId == "REVIVE_MOVE")
            forceTransition = true;

        // Fogmog 跨半血时 ThingsOriginPower 只触发一次；如果当前正被其他来源眩晕，
        // 普通 SetMoveImmediate 会拒绝新的 STUNNED 并永久跳过幻象阶段。
        if (__instance is ThingsOriginFogmog
            && state.StateId == MonsterModel.stunnedMoveId
            && state.FollowUpStateId == ThingsOriginFogmog.SwipeMoveId)
            forceTransition = true;
    }
}
