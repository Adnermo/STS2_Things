using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
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
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

public sealed class OriginEyeWithTeeth : MonsterModel
{
    private const int _distractAmount = 2;

    private readonly int HealAmount = 6;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/eye_with_teeth"); //直接用原版
    public override int MinInitialHp => 9;

    public override int MaxInitialHp => MinInitialHp;

    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;

    public override bool ShouldDisappearFromDoom => false;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await ApplyIllusionPower();
    }

    /// <summary>
    /// 施加IllusionPower并设置复活后跳转眩晕意图。
    /// AfterAddedToRoom在Encounter生成时调用，动态召唤时需手动调用。
    /// </summary>
    public async Task ApplyIllusionPower()
    {
        await PowerCmd.Apply<IllusionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        var illusion = Creature.GetPower<IllusionPower>();
        if (illusion != null)
        {
            illusion.FollowUpStateId = "STUN_AFTER_REVIVE";
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();
        var HealBoss = new MoveState("HEAL_BOSS_MOVE", HealBossMove, new HealIntent());
        var GiveCard = new MoveState("DISTRACT_MOVE", DistractMove, new StatusIntent(_distractAmount));
        // 复活后眩晕状态，执行完后回到正常循环
        var StunAfterRevive = new MoveState("STUN_AFTER_REVIVE", _ => Task.CompletedTask, new StunIntent())
        {
            FollowUpState = GiveCard,
            MustPerformOnceBeforeTransitioning = true
        };
        GiveCard.FollowUpState = HealBoss;
        HealBoss.FollowUpState = GiveCard;
        list.Add(GiveCard);
        list.Add(HealBoss);
        list.Add(StunAfterRevive);
        return new MonsterMoveStateMachine(list, GiveCard);
    }

    private async Task HealBossMove(IReadOnlyList<Creature> targets)
    {
        var boss = Creature.CombatState.GetTeammatesOf(Creature)
            .First(c => c.IsPrimaryEnemy);

        await CreatureCmd.Heal(boss, HealAmount);
    }

    private async Task DistractMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(AttackSfx);
        await CreatureCmd.TriggerAnim(Creature, "Attack", 0.7f);
        VfxCmd.PlayOnCreatureCenters(targets, "vfx/vfx_attack_slash");
        await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, _distractAmount, null);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var animState = new AnimState("idle_loop", true);
        var animState2 = new AnimState("attack");
        var state = new AnimState("die");
        animState2.NextState = animState;
        var creatureAnimator = new CreatureAnimator(animState, controller);
        creatureAnimator.AddAnyState("Attack", animState2);
        creatureAnimator.AddAnyState("Dead", state,
            () => !CombatState.GetTeammatesOf(Creature).Any(t => t != null && t.IsPrimaryEnemy && t.IsAlive));
        return creatureAnimator;
    }
}

/// <summary>
/// 修复IllusionPower复活意图被非可转换状态（如眩晕）阻断的bug：
/// 当SetMoveImmediate传入REVIVE_MOVE时，强制forceTransition=true，
/// 确保怪物在任何状态下被杀都能正常切换到复活意图。
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.SetMoveImmediate))]
public static class IllusionReviveForceTransitionPatch
{
    static void Prefix(MoveState state, ref bool forceTransition)
    {
        if (state.StateId == "REVIVE_MOVE")
            forceTransition = true;
    }
}