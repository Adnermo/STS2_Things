using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 荧光幼触 (Biolume Tendril) — 渊墨巨蛸的强化增幅型破桥小触手变种。
/// 交替施放【荧脉滋养】（为 Boss 本体提供力量增益 + 自身格挡）与【荧触轻拍】（拍击 + 为 Boss 提供 1 点力量）；
/// 在 Boss 蓄力回合将状态机延后 1 回合、插入【荧脉蓄能】为本体施加原版活力（Vigor），直接增幅下回合高压渊墨扫射并在命中后自动消耗。
/// </summary>
public sealed class ThingsAbyssalGlandTentacle : ThingsAbyssalSmallTentacleBase
{
    public const string GlandSurgeId = "GLAND_SURGE";
    public const string GlandSlapId = "GLAND_SLAP";

    public override string VariantSkinName => "gland";
    protected override string FirstNormalMoveId => GlandSurgeId;
    protected override string SecondNormalMoveId => GlandSlapId;
    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_gland_tentacle.tscn";

    public int BossStrengthBuff => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 2, 1) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int SelfBuffBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 7, 5) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int GlandSlapDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public const int GlandSlapBossStrength = 1;
    public int ChargeBossVigor => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4) + (Sts2VersionSeam.IsDread() ? 1 : 0);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath,
        ModelDb.Power<VigorPower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var glandSurge = new MoveState(
            GlandSurgeId,
            GlandSurgeMove,
            new BuffIntent(),
            new DefendIntent());

        var glandSlap = new MoveState(
            GlandSlapId,
            GlandSlapMove,
            new SingleAttackIntent(() => GlandSlapDamage),
            new BuffIntent());

        return BuildSyncedStateMachine(glandSurge, glandSlap, new BuffIntent());
    }

    protected override async Task ApplyChargeBuffToBossAsync(ThingsAbyssalInkmaw boss)
    {
        await PowerCmd.Apply<VigorPower>(
            new ThrowingPlayerChoiceContext(),
            boss.Creature,
            ChargeBossVigor,
            Creature,
            null);
        Sts2VersionSeam.RefreshMonsterIntentsUi(boss.Creature);
    }

    private async Task GlandSurgeMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "buff_pulse");
        await WaitForCombatBeatAsync(0.58f);

        await CreatureCmd.GainBlock(Creature, SelfBuffBlock, ValueProp.Move, null);
        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), boss.Creature, BossStrengthBuff, Creature, null);
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task GlandSlapMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "slam_attack");
        await WaitForCombatBeatAsync(0.46f);

        await DamageCmd.Attack(GlandSlapDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);

        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), boss.Creature, GlandSlapBossStrength, Creature, null);
        }

        await WaitForCombatBeatAsync(0.38f);
    }
}
