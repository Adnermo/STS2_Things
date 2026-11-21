using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using STS2_Things.Cards;
using STS2_Things.Platform;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// 人面塔 Slot 1 — 悲叹面 (Lament Visage).
/// Status erosion &amp; Weak control: inflicts 2 <see cref="ThingsLamentTear"/> (哀泪) into Draw pile
/// and 2 directly into Hand, punishing unplayed Tears at Turn End with permanent <see cref="ThingsLamentSorrowPower"/> stacks.
/// </summary>
public sealed class ThingsHumanFaceTowerLament : ThingsHumanFaceTowerFace
{
    public const int TearDrawCount = 2;
    public const int TearHandCount = 2;

    public override int FaceSlotIndex => 1;
    public override string IdleAnimationName => "lament_idle";
    public override string StarterAttackAnimName => "lament_cast_wail";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 125, 115) + (Sts2VersionSeam.IsDread() ? 15 : 0);
    public override int MaxInitialHp => MinInitialHp;

    public int WeatheredWailDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 8);
    public int WeatheredWailWeak => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 2, 1);
    public int BloodRainDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);
    public override int ComboStarterDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 8);
    public override int ComboFollowUpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Card<ThingsLamentTear>().PortraitPath,
        ModelDb.Power<ThingsLamentSighPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsLamentSorrowPower>().ResolvedBigIconPath,
        ModelDb.Power<WeakPower>().ResolvedBigIconPath
    ]);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var weatheredWail = new MoveState(
            "WEATHERED_WAIL_MOVE",
            WeatheredWailMoveAsync,
            new SingleAttackIntent(() => WeatheredWailDamage),
            new DebuffIntent(),
            new StatusIntent(TearDrawCount + TearHandCount));
        var bloodRain = new MoveState(
            "BLOOD_RAIN_MOVE",
            BloodRainMoveAsync,
            new MultiAttackIntent(BloodRainDamage, 3));

        return BuildTowerStateMachine(
            weatheredWail,
            bloodRain,
            "HIDDEN_MOVE",
            "EMERGENCY_STUN_MOVE",
            "CYCLE_CLIMAX_MOVE",
            "COMBO_ROTATE_MOVE");
    }

    internal override async Task ApplyUniquePassivePowerAsync()
    {
        if (!Creature.HasPower<ThingsLamentSighPower>())
        {
            await PowerCmd.Apply<ThingsLamentSighPower>(
                new ThrowingPlayerChoiceContext(),
                Creature,
                1,
                Creature,
                null,
                silent: true);
        }
    }

    internal override async Task ExecuteComboFollowUpAsync(IReadOnlyList<Creature> targets)
    {
        await ExecuteStoneSingleAttackAsync("lament_cast_wail", 0.42f, ComboFollowUpDamage);
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            Creature[] aliveTargets = targets.Where(c => c.IsAlive).ToArray();
            if (aliveTargets.Length > 0)
            {
                await PowerCmd.Apply<WeakPower>(
                    new ThrowingPlayerChoiceContext(),
                    aliveTargets,
                    1,
                    Creature,
                    null);
            }
        }
    }

    private async Task WeatheredWailMoveAsync(IReadOnlyList<Creature> targets)
    {
        await ExecuteStoneSingleAttackAsync("lament_cast_wail", 0.46f, WeatheredWailDamage);
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        Creature[] aliveTargets = targets.Where(c => c.IsAlive).ToArray();
        if (aliveTargets.Length > 0)
        {
            await PowerCmd.Apply<WeakPower>(
                new ThrowingPlayerChoiceContext(),
                aliveTargets,
                WeatheredWailWeak,
                Creature,
                null);

            await CardPileCmd.AddToCombatAndPreview<ThingsLamentTear>(
                aliveTargets,
                PileType.Draw,
                TearDrawCount,
                null,
                CardPilePosition.Random);

            Creature[] handTargets = aliveTargets.Where(c => c.IsAlive).ToArray();
            if (handTargets.Length > 0)
            {
                await CardPileCmd.AddToCombatAndPreview<ThingsLamentTear>(
                    handTargets,
                    PileType.Hand,
                    TearHandCount,
                    null);
            }
        }
    }

    private Task BloodRainMoveAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneMultiAttackAsync("lament_attack_rain", 0.36f, BloodRainDamage, 3);
}
