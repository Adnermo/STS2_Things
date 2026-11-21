using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Monsters;

/// <summary>
/// Terminal Small Amber Chromatic Slimelet (does not split further). Enters with mineral Block
/// and rams while bracing its stone cap.
/// </summary>
public sealed class ThingsChromaticSlimeSmallAmber : ThingsChromaticSlimeSmallBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 13, 12);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 15, 14);

    internal override int OpeningBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 6, 5);
    protected override string? PreferredSkinName => "amber";
    protected override float? VariantBoundsTop => -126f;
    protected override Vector2? VariantCenterPos => new(-2f, -60f);
    protected override Vector2? VariantIntentPos => new(-3f, -156f);
    protected override string? PreferredAttackAnimation => "attack_amber";
    private int TackleDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3);
    private int TackleBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 5, 4);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var geoTackle = new MoveState(
            "GEO_TACKLE_MOVE",
            GeoTackleMove,
            new SingleAttackIntent(TackleDamage),
            new DefendIntent());
        geoTackle.FollowUpState = geoTackle;
        return new MonsterMoveStateMachine([geoTackle], geoTackle);
    }

    private async Task GeoTackleMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(TackleDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.34f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await CreatureCmd.GainBlock(Creature, TackleBlock, ValueProp.Move, null);
    }
}
