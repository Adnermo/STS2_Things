using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// Terminal Small Azure Chromatic Slimelet (does not split further). Alternates Slimed droplets
/// and Weakening splashes.
/// </summary>
public sealed class ThingsChromaticSlimeSmallAzure : ThingsChromaticSlimeSmallBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 20, 18);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 24, 22);

    public override ChromaticStrain? OwnStrain => ChromaticStrain.Azure;

    protected override string? PreferredSkinName => "azure";
    protected override float? VariantBoundsTop => -120f;
    protected override Vector2? VariantCenterPos => new(-2f, -58f);
    protected override Vector2? VariantIntentPos => new(-3f, -150f);

    protected override string? PreferredAttackAnimation => "attack_azure";
    protected override string? PreferredCastAnimation => "cast_azure";

    private int SpitDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);
    private int DropDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var bubbleSpit = new MoveState(
            "BUBBLE_SPIT_MOVE",
            BubbleSpitMove,
            new SingleAttackIntent(SpitDamage),
            new StatusIntent(1));
        var acidDrop = new MoveState(
            "ACID_DROP_MOVE",
            AcidDropMove,
            new SingleAttackIntent(DropDamage),
            new DebuffIntent());

        bubbleSpit.FollowUpState = acidDrop;
        acidDrop.FollowUpState = bubbleSpit;
        var initial = StartMovePhase % 2 == 0 ? bubbleSpit : acidDrop;
        return new MonsterMoveStateMachine([bubbleSpit, acidDrop], initial);
    }

    private async Task BubbleSpitMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(SpitDamage)
            .FromMonster(this)
            .WithAttackerAnim("Cast", 0.44f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, CastSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 1, null);
    }

    private async Task AcidDropMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(DropDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.34f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets, 1m, Creature, null);
    }
}
