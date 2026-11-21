using Godot;
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
/// Terminal Small Crimson Chromatic Slimelet (does not split further). Attacks fiercely and
/// sharpens its horn with Strength.
/// </summary>
public sealed class ThingsChromaticSlimeSmallCrimson : ThingsChromaticSlimeSmallBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 11, 10);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 13, 12);

    protected override string? PreferredSkinName => "crimson";
    protected override float? VariantBoundsTop => -142f;
    protected override Vector2? VariantCenterPos => new(-2f, -68f);
    protected override Vector2? VariantIntentPos => new(-3f, -172f);

    protected override string? PreferredAttackAnimation => "attack_crimson";

    private int PrickDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var rubyPrick = new MoveState(
            "RUBY_PRICK_MOVE",
            RubyPrickMove,
            new SingleAttackIntent(PrickDamage),
            new BuffIntent());
        rubyPrick.FollowUpState = rubyPrick;
        return new MonsterMoveStateMachine([rubyPrick], rubyPrick);
    }

    private async Task RubyPrickMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(PrickDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.34f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
    }
}
