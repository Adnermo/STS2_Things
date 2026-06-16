using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
///     放缩巨甲虫 — Overgrowth Boss
///     开局: 重构化（玩家 ScaleDown×1 + 自身 ScaleUp×1）
///     循环: 撕咬 → 触角鞭打 → 蜕壳 → 缩小 → 循环
///     ScaleDown: 每层缩小 15%，伤害 −15%
///     ScaleUp:   每层放大 15%，伤害 +15%
/// </summary>
public sealed class ScaleBeetle : MonsterModel
{
    // 对应CustomBgm "act1_boss_vantom" 的FMOD参数
    private const string _trackName = "vantom_progress";
    private const int MoltBlock = 26;

    protected override string VisualsPath =>
        SceneHelper.GetScenePath("creature_visuals/fallback");

    // 复用KaiserCrab（重甲巨型Boss）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/kaiser_crab/kaiser_crab_attack_slam";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/kaiser_crab/kaiser_crab_left_attack_scoop";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/kaiser_crab/kaiser_crab_left_attack_scoop";

    public override bool CanChangeScale => true;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        // 初始化专属音乐参数（CustomBgm = act1_boss_vantom）
        NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 1f);
    }

    public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature == Creature)
        {
            // 死亡升调（vantom_progress=5 触发FMOD升调自动化）
            NRunMusicController.Instance?.UpdateMusicParameter(_trackName, 5f);
        }
        return Task.CompletedTask;
    }

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 220, 210);

    public override int MaxInitialHp => MinInitialHp;

    private int BiteDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 18, 16);

    private int WhipDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 11, 10);

    private int ShrinkDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 12, 10);

    // ========== 状态机 ==========

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        List<MonsterState> list = new();

        // 重构化: 开局仅一次
        var reconstructMove = new MoveState("RECONSTRUCT_MOVE", ReconstructMove,
            new DebuffIntent(), new BuffIntent());

        // 撕咬
        var biteMove = new MoveState("BITE_MOVE", BiteMove,
            new SingleAttackIntent(BiteDamage));

        // 触角鞭打 ×2
        var whipMove = new MoveState("WHIP_MOVE", WhipMove,
            new MultiAttackIntent(WhipDamage, 2));

        // 蜕壳: 26 防御 + ScaleUp 再叠一层
        var moltMove = new MoveState("MOLT_MOVE", MoltMove,
            new DefendIntent(), new BuffIntent());

        // 缩小: 伤害 + 玩家 ScaleDown 再叠一层
        var shrinkMove = new MoveState("SHRINK_MOVE", ShrinkMove,
            new SingleAttackIntent(ShrinkDamage), new DebuffIntent());

        // 连线: 重构化(仅一次) → 撕咬 → 鞭打 → 蜕壳 → 缩小 → 撕咬...
        reconstructMove.FollowUpState = biteMove;
        biteMove.FollowUpState = whipMove;
        whipMove.FollowUpState = moltMove;
        moltMove.FollowUpState = shrinkMove;
        shrinkMove.FollowUpState = biteMove;

        list.Add(reconstructMove);
        list.Add(biteMove);
        list.Add(whipMove);
        list.Add(moltMove);
        list.Add(shrinkMove);

        return new MonsterMoveStateMachine(list, reconstructMove);
    }

    // ========== 招式 ==========

    private async Task ReconstructMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        await PowerCmd.Apply<ScaleDownPower>(new ThrowingPlayerChoiceContext(),
            targets, 15m, Creature, null);
        await PowerCmd.Apply<ScaleUpPower>(new ThrowingPlayerChoiceContext(),
            Creature, 15m, Creature, null);
    }

    private async Task BiteMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(BiteDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task WhipMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(WhipDamage)
            .FromMonster(this)
            .WithHitCount(2)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task MoltMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        await CreatureCmd.GainBlock(Creature, MoltBlock, ValueProp.Unpowered, null);
        await PowerCmd.Apply<ScaleUpPower>(new ThrowingPlayerChoiceContext(),
            Creature, 10m, Creature, null);
    }

    private async Task ShrinkMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(ShrinkDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
        await PowerCmd.Apply<ScaleDownPower>(new ThrowingPlayerChoiceContext(),
            targets, 10m, Creature, null);
    }
}