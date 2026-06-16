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
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

public sealed class OriginEyeWithTeeth : MonsterModel
{
    private const int _distractAmount = 2;

    private readonly int HealAmount = 10;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/eye_with_teeth"); //直接用原版
    public override int MinInitialHp => 12;

    public override int MaxInitialHp => MinInitialHp;

    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;

    public override bool ShouldDisappearFromDoom => false;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmd.Apply<IllusionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();
        var HealBoss = new MoveState("HEAL_BOSS_MOVE", HealBossMove, new HealIntent());
        var GiveCard = new MoveState("DISTRACT_MOVE", DistractMove, new StatusIntent(_distractAmount));
        GiveCard.FollowUpState = HealBoss;
        HealBoss.FollowUpState = GiveCard;
        list.Add(GiveCard);
        list.Add(HealBoss);
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