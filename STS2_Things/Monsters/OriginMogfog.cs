using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

public class OriginFogmog : MonsterModel
{
    // 对应CustomBgm "act1_boss_the_kin" 的FMOD参数
    private const string _trackName = "queen_progress";
    private bool _hasTransformed = false; //默认未转阶段
    public MoveState illusionMove;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/fallback");

    // 复用Fogmog（同源生物）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/fogmog/fogmog_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/fogmog/fogmog_summon";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/fogmog/fogmog_die";

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Append("res://images/monsters/origin_fogmog.png");

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 260, 254);

    public override int MaxInitialHp => MinInitialHp;

    //高低进阶血量伤害
    private int SwipeDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 17, 10);

    private int HeadbuttDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 22, 18);

    private int BlockAmount => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 15, 10);

    private decimal HealAmount => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 13m, 10m);

    private int TripleDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 7, 6);

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        return base.GenerateAnimator(controller);
    }


    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        // 初始化专属音乐参数（CustomBgm = act1_boss_the_kin）
        NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 1f);
        await PowerCmd.Apply<OriginPower>(new ThrowingPlayerChoiceContext(), Creature, MinInitialHp / 2m, Creature,
            null);
    }

    public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature == Creature)
        {
            // 死亡升调（queen_progress=5 触发FMOD升调自动化）
            NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 5f);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 二阶段转场：当血量低于50%且尚未转阶段时触发音乐切换+转场音效+插入召唤意图。
    /// </summary>
    private void CheckPhaseTransition()
    {
        if (!_hasTransformed && Creature.CurrentHp <= Creature.MaxHp / 2m)
        {
            _hasTransformed = true;
            Log.Info("[OriginFogmog] Phase2 transition triggered!");
            // 转场音效
            SfxCmd.Play("event:/sfx/enemy/enemy_attacks/queen/queen_cast");
            // 音乐推进到第二阶段
            NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 2f);

            // 插入一个召唤意图，执行完后回到原状态机位置
            var originalStateId = Creature.Monster.MoveStateMachine.StateLog.Last().Id;
            var summonOnce = new MoveState(
                "PHASE2_SUMMON",
                async targets =>
                {
                    SfxCmd.Play(CastSfx);
                    await CreatureCmd.TriggerAnim(Creature, "Summon", 0.75f);
                    var slotName = CombatState.Encounter.Slots.FirstOrDefault(
                        s => s.StartsWith("illusion") && CombatState.Enemies.All(c => c.SlotName != s),
                        string.Empty);
                    var eye = await CreatureCmd.Add<OriginEyeWithTeeth>(CombatState, slotName);
                    if (eye != null)
                    {
                        await PowerCmd.Apply<OriginGainEnergyPower>(new ThrowingPlayerChoiceContext(), eye, 1m, Creature, null);
                    }
                },
                new SummonIntent()
            )
            {
                FollowUpStateId = originalStateId,
                MustPerformOnceBeforeTransitioning = true
            };
            Creature.Monster.SetMoveImmediate(summonOnce);
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();

        // 第一招：召唤（开局只召唤一次）
        illusionMove = new MoveState("ILLUSION_MOVE", IllusionMove,
            new SummonIntent());
        // 第二招：挥击 + 给自己加力量
        var swipeMove = new MoveState("SWIPE_MOVE", SwipeMove,
            new SingleAttackIntent(SwipeDamage), new BuffIntent());
        // 分支 A：挥击
        var swipeBlockMove = new MoveState("SWIPE_RANDOM_MOVE", SwipeBlockMove,
            new SingleAttackIntent(SwipeDamage), new DefendIntent());
        // 分支 B：头槌
        var headbuttMove = new MoveState("HEADBUTT_MOVE", HeadbuttMove,
            new SingleAttackIntent(HeadbuttDamage));
        var tripleMove = new MoveState("TRIPLE_MOVE", TripleMove,
            new MultiAttackIntent(TripleDamage, 3));
        var healMove = new MoveState("HEAL_MOVE", TripleMove,
            new HealIntent());
        // 随机分支
        var branch = new RandomBranchState("BRANCH");
        branch.AddBranch(tripleMove, MoveRepeatType.CannotRepeat, () => 0.4f);
        branch.AddBranch(headbuttMove, MoveRepeatType.CannotRepeat, () => 0.6f);

        illusionMove.FollowUpState = swipeMove; //召唤完先力量
        healMove.FollowUpState = swipeMove;
        swipeMove.FollowUpState = branch;
        tripleMove.FollowUpState = swipeBlockMove;
        headbuttMove.FollowUpState = swipeBlockMove;
        swipeBlockMove.FollowUpState = healMove;


        list.Add(illusionMove);
        list.Add(swipeMove);
        list.Add(swipeBlockMove);
        list.Add(branch);
        list.Add(headbuttMove);
        list.Add(healMove);
        list.Add(tripleMove);
        return new MonsterMoveStateMachine(list, illusionMove);
    }


    private async Task IllusionMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx); // fogmog_summon
        await CreatureCmd.TriggerAnim(Creature, "Summon", 0.75f);
        var slotName = CombatState.Encounter.Slots.FirstOrDefault(
            s => s.StartsWith("illusion") && CombatState.Enemies.All(c => c.SlotName != s),
            string.Empty);

        var eye = await CreatureCmd.Add<OriginEyeWithTeeth>(CombatState, slotName);
        if (eye != null)
        {
            await PowerCmd.Apply<OriginGainEnergyPower>(new ThrowingPlayerChoiceContext(), eye, 1m, Creature, null);
        }
    }


    private async Task SwipeMove(IReadOnlyList<Creature> targets)
    {
        CheckPhaseTransition(); // 检测二阶段转场
        await DamageCmd.Attack(SwipeDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 3m, Creature, null);
    }

    private async Task SwipeBlockMove(IReadOnlyList<Creature> targets)
    {
        CheckPhaseTransition();
        await DamageCmd.Attack(SwipeDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
        await CreatureCmd.GainBlock(Creature, BlockAmount * Creature.CombatState.Players.Count, ValueProp.Move, null);
    }

    private async Task HeadbuttMove(IReadOnlyList<Creature> targets)
    {
        CheckPhaseTransition();
        await DamageCmd.Attack(HeadbuttDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
    }

    private async Task TripleMove(IReadOnlyList<Creature> targets)
    {
        CheckPhaseTransition();
        await DamageCmd.Attack(TripleDamage)
            .FromMonster(this)
            .WithHitCount(3)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
    }

    private async Task HealMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.Heal(Creature, HealAmount * Creature.CombatState.Players.Count);
    }
}