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
using STS2_Things.Platform;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// 人面塔 Slot 0 — 嗔怒面 (Wrath Visage).
/// High-pressure punisher: gains +2 permanent Strength whenever an attack deals unblocked HP damage.
/// </summary>
public sealed class ThingsHumanFaceTowerWrath : ThingsHumanFaceTowerFace
{
    public override int FaceSlotIndex => 0;
    public override string IdleAnimationName => "wrath_idle";
    public override string StarterAttackAnimName => "wrath_attack_heavy";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 120, 110) + (Sts2VersionSeam.IsDread() ? 15 : 0);
    public override int MaxInitialHp => MinInitialHp;

    public int HeavyMalletDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int DoubleJawDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);
    public override int ComboStarterDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public override int ComboFollowUpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 12, 10) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsWrathGlarePower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var heavyMallet = new MoveState(
            "HEAVY_MALLET_MOVE",
            HeavyMalletMoveAsync,
            new SingleAttackIntent(() => HeavyMalletDamage));
        var doubleJaw = new MoveState(
            "DOUBLE_JAW_MOVE",
            DoubleJawMoveAsync,
            new MultiAttackIntent(DoubleJawDamage, 2));

        return BuildTowerStateMachine(
            heavyMallet,
            doubleJaw,
            "HIDDEN_MOVE",
            "EMERGENCY_STUN_MOVE",
            "CYCLE_CLIMAX_MOVE",
            "COMBO_ROTATE_MOVE");
    }

    internal override async Task ApplyUniquePassivePowerAsync()
    {
        if (!Creature.HasPower<ThingsWrathGlarePower>())
        {
            await PowerCmd.Apply<ThingsWrathGlarePower>(
                new ThrowingPlayerChoiceContext(),
                Creature,
                1,
                Creature,
                null,
                silent: true);
        }
    }

    internal override async Task ExecuteComboFollowUpAsync(IReadOnlyList<Creature> _)
    {
        await ExecuteStoneSingleAttackAsync("wrath_attack_heavy", 0.38f, ComboFollowUpDamage, strongShake: true);
    }

    private Task HeavyMalletMoveAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneSingleAttackAsync("wrath_attack_heavy", 0.44f, HeavyMalletDamage, strongShake: true);

    private Task DoubleJawMoveAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneMultiAttackAsync("wrath_attack_double", 0.36f, DoubleJawDamage, 2);
}
