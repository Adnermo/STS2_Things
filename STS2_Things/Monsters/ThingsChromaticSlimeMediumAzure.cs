using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_Things.Monsters;

/// <summary>
/// Medium Azure Chromatic Slime (spawned when killed by non-Attack sources). Disrupts decks and
/// tempo with Slimed and Weak, splitting into 2 Smalls on death.
/// </summary>
public sealed class ThingsChromaticSlimeMediumAzure : ThingsChromaticSlimeMediumBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 20, 18);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 23, 21);

    protected override string? PreferredSkinName => "azure";
    protected override float? VariantBoundsTop => -188f;
    protected override Vector2? VariantCenterPos => new(0f, -92f);
    protected override Vector2? VariantIntentPos => new(-4f, -224f);

    protected override string? PreferredAttackAnimation => "attack_azure";
    protected override string? PreferredCastAnimation => "cast_azure";

    private int DrizzleDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);
    private int TideDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var causticDrizzle = new MoveState(
            "CAUSTIC_DRIZZLE_MOVE",
            CausticDrizzleMove,
            new SingleAttackIntent(DrizzleDamage),
            new StatusIntent(1));
        var tideMist = new MoveState(
            "TIDE_MIST_MOVE",
            TideMistMove,
            new SingleAttackIntent(TideDamage),
            new DebuffIntent());

        causticDrizzle.FollowUpState = tideMist;
        tideMist.FollowUpState = causticDrizzle;
        var initial = StartMovePhase % 2 == 0 ? causticDrizzle : tideMist;
        return new MonsterMoveStateMachine([causticDrizzle, tideMist], initial);
    }

    private async Task CausticDrizzleMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(DrizzleDamage)
            .FromMonster(this)
            .WithAttackerAnim("Cast", 0.48f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, CastSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 1, null);
    }

    private async Task TideMistMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(TideDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.36f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets, 1m, Creature, null);
    }
}
