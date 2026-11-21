using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_Things.Monsters;

/// <summary>
/// Medium Crimson Chromatic Slime (spawned on Overkill Attack &gt;= 5). Aggressive glass-cannon
/// that multi-strikes and escalates Strength, splitting into 2 Smalls on death.
/// </summary>
public sealed class ThingsChromaticSlimeMediumCrimson : ThingsChromaticSlimeMediumBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 19, 18);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 22, 20);

    protected override string? PreferredAttackAnimation => "attack_crimson";
    protected override string? PreferredPowerUpAnimation => "power_crimson";

    private int RendDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);
    private const int RendHits = 2;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var crimsonRend = new MoveState(
            "CRIMSON_REND_MOVE",
            CrimsonRendMove,
            new MultiAttackIntent(RendDamage, RendHits));
        var crystalSurge = new MoveState(
            "CRYSTAL_SURGE_MOVE",
            CrystalSurgeMove,
            new BuffIntent());

        crimsonRend.FollowUpState = crystalSurge;
        crystalSurge.FollowUpState = crimsonRend;
        var initial = StartMovePhase % 2 == 0 ? crimsonRend : crystalSurge;
        return new MonsterMoveStateMachine([crimsonRend, crystalSurge], initial);
    }

    private async Task CrimsonRendMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(RendDamage)
            .WithHitCount(RendHits)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.36f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
    }

    private async Task CrystalSurgeMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0.45f);
        if (!CanAct)
            return;

        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null);
    }
}
