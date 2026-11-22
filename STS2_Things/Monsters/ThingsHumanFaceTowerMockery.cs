using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
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

namespace STS2_Things.Monsters;

/// <summary>
/// 人面塔 Slot 3 — 诡笑面 (Mockery Visage).
/// Card-sequencing disruptor: playing two consecutive cards of the same CardType grants Mockery
/// 4(5) Block and +1 permanent Strength.
/// </summary>
public sealed class ThingsHumanFaceTowerMockery : ThingsHumanFaceTowerFace
{
    public override int FaceSlotIndex => 3;
    public override string IdleAnimationName => "mockery_idle";
    public override string StarterAttackAnimName => "mockery_attack_twin";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 108, 99) + (Sts2VersionSeam.IsDread() ? 13 : 0);
    public override int MaxInitialHp => MinInitialHp;

    public int TauntBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 5, 4) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int SneerStabDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int WhirlingFangDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 18, 15) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public override int ComboStarterDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 12, 10) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public override int ComboFollowUpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public override int ComboFollowUpHits => 2;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsMockeryEchoPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var sneerStab = new MoveState(
            "SNEER_STAB_MOVE",
            SneerStabMoveAsync,
            new MultiAttackIntent(SneerStabDamage, 2),
            new DebuffIntent());
        var whirlingFang = new MoveState(
            "WHIRLING_FANG_MOVE",
            WhirlingFangMoveAsync,
            new SingleAttackIntent(() => WhirlingFangDamage));

        return BuildTowerStateMachine(
            sneerStab,
            whirlingFang,
            "HIDDEN_MOVE",
            "EMERGENCY_STUN_MOVE",
            "CYCLE_CLIMAX_MOVE",
            "COMBO_ROTATE_MOVE");
    }

    internal override async Task ApplyUniquePassivePowerAsync()
    {
        if (!Creature.HasPower<ThingsMockeryEchoPower>())
        {
            await PowerCmd.Apply<ThingsMockeryEchoPower>(
                new ThrowingPlayerChoiceContext(),
                Creature,
                1,
                Creature,
                null,
                silent: true);
        }
    }

    internal override void OnRotatedAway()
    {
        if (IsRegisteredInCombat)
        {
            Creature.GetPower<ThingsMockeryEchoPower>()?.ResetTrackingOnRotation();
        }
    }

    internal override Task ExecuteComboFollowUpAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneMultiAttackAsync("mockery_attack_twin", 0.34f, ComboFollowUpDamage, ComboFollowUpHits);

    private async Task SneerStabMoveAsync(IReadOnlyList<Creature> targets)
    {
        await ExecuteStoneMultiAttackAsync("mockery_attack_twin", 0.35f, SneerStabDamage, 2);
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        Creature[] aliveTargets = targets.Where(c => c.IsAlive).ToArray();
        if (aliveTargets.Length > 0)
        {
            await PowerCmd.Apply<VulnerablePower>(
                new ThrowingPlayerChoiceContext(),
                aliveTargets,
                1,
                Creature,
                null);
        }
    }

    private Task WhirlingFangMoveAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneSingleAttackAsync("mockery_attack_fang", 0.42f, WhirlingFangDamage);
}
