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
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 尖刺幼触 (Barbed Tendril) — 渊墨巨蛸的攻击型破桥小触手变种。
/// 交替施放【棘拍蚀防】（拍击 + 1 层脆弱）与【疾鞭双拍】（快速 2 段拍桥）；
/// 在 Boss 蓄力回合将状态机延后 1 回合、插入【棘刺灌注】为本体施加 1 回合临时荆棘。
/// </summary>
public sealed class ThingsAbyssalSpikeTentacle : ThingsAbyssalSmallTentacleBase
{
    public const string SpikeSlamId = "SPIKE_SLAM";
    public const string DoubleSlapId = "DOUBLE_SLAP";

    public override string VariantSkinName => "spike";
    protected override string FirstNormalMoveId => SpikeSlamId;
    protected override string SecondNormalMoveId => DoubleSlapId;
    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_spike_tentacle.tscn";

    public int SpikeSlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int DoubleSlapDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);
    public const int DoubleSlapHits = 2;
    public int ChargeBossTempThorns => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3) + (Sts2VersionSeam.IsDread() ? 1 : 0);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<FrailPower>().ResolvedBigIconPath,
        ModelDb.Power<ThornsPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsInkBlackoutPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempThornsPower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var spikeSlam = new MoveState(
            SpikeSlamId,
            SpikeSlamMove,
            new SingleAttackIntent(() => SpikeSlamDamage),
            new DebuffIntent());

        var doubleSlap = new MoveState(
            DoubleSlapId,
            DoubleSlapMove,
            new MultiAttackIntent(DoubleSlapDamage, DoubleSlapHits));

        return BuildSyncedStateMachine(spikeSlam, doubleSlap, new BuffIntent());
    }

    protected override async Task ApplyChargeBuffToBossAsync(ThingsAbyssalInkmaw boss)
    {
        await PowerCmd.Apply<ThingsAbyssalTempThornsPower>(
            new ThrowingPlayerChoiceContext(),
            boss.Creature,
            ChargeBossTempThorns,
            Creature,
            null);
    }

    private async Task SpikeSlamMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "slam_attack");
        await WaitForCombatBeatAsync(0.46f);

        await DamageCmd.Attack(SpikeSlamDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);

        foreach (Creature target in targets)
        {
            if (target.IsAlive)
            {
                await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), target, 1m, Creature, null);
            }
        }

        await WaitForCombatBeatAsync(0.38f);
    }

    private async Task DoubleSlapMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "double_slap");
        await WaitForCombatBeatAsync(0.38f);

        await DamageCmd.Attack(DoubleSlapDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await WaitForCombatBeatAsync(0.42f);

        await DamageCmd.Attack(DoubleSlapDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await WaitForCombatBeatAsync(0.35f);
    }
}
