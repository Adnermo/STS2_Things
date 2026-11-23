using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 甲壳幼触 (Carapace Tendril) — 渊墨巨蛸的护主防御型破桥小触手变种。
/// 交替施放【岩壳护主】（为 Boss 本体与自身叠格挡）与【重壳拍击】（拍击 + 为 Boss 本体叠格挡）；
/// 在 Boss 蓄力回合将状态机延后 1 回合、插入【岩壳覆护】为本体施加 1 回合临时减伤（15% / A20 20%）。
/// </summary>
public sealed class ThingsAbyssalShellTentacle : ThingsAbyssalSmallTentacleBase
{
    public const string ShellGuardId = "SHELL_GUARD";
    public const string ShellSlapId = "SHELL_SLAP";

    public override string VariantSkinName => "shell";
    protected override string FirstNormalMoveId => ShellGuardId;
    protected override string SecondNormalMoveId => ShellSlapId;
    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_shell_tentacle.tscn";

    public int BossGuardBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 13, 10) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int SelfGuardBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 4, 3) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int ShellSlapDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int ShellSlapBossBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int ChargeBossTempWardPercent => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 20, 15);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsAbyssalTempWardPower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var shellGuard = new MoveState(
            ShellGuardId,
            ShellGuardMove,
            new DefendIntent());

        var shellSlap = new MoveState(
            ShellSlapId,
            ShellSlapMove,
            new SingleAttackIntent(() => ShellSlapDamage),
            new DefendIntent());

        return BuildSyncedStateMachine(shellGuard, shellSlap, new BuffIntent());
    }

    protected override async Task ApplyChargeBuffToBossAsync(ThingsAbyssalInkmaw boss)
    {
        await PowerCmd.Apply<ThingsAbyssalTempWardPower>(
            new ThrowingPlayerChoiceContext(),
            boss.Creature,
            ChargeBossTempWardPercent,
            Creature,
            null);
    }

    private async Task ShellGuardMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "guard_brace");
        await WaitForCombatBeatAsync(0.56f);

        await CreatureCmd.GainBlock(Creature, SelfGuardBlock, ValueProp.Move, null);
        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await CreatureCmd.GainBlock(boss.Creature, BossGuardBlock, ValueProp.Move, null);
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task ShellSlapMove(IReadOnlyList<Creature> targets)
    {
        NormalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "slam_attack");
        await WaitForCombatBeatAsync(0.46f);

        await DamageCmd.Attack(ShellSlapDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);

        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await CreatureCmd.GainBlock(boss.Creature, ShellSlapBossBlock, ValueProp.Move, null);
        }

        await WaitForCombatBeatAsync(0.38f);
    }
}
