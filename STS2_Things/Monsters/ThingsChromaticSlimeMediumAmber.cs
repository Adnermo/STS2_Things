using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// Medium Amber Chromatic Slime. Enters combat armored with mineral Block and reinforces itself
/// while trading blows, interrupting into a half-HP split into 2 Small Amber slimes inheriting its remaining HP.
/// </summary>
public sealed class ThingsChromaticSlimeMediumAmber : ThingsChromaticSlimeMediumBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 50, 46);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 56, 52);

    public override ChromaticStrain? OwnStrain => ChromaticStrain.Amber;

    internal override int OpeningBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 9, 8);
    protected override string? PreferredSkinName => "amber";
    protected override float? VariantBoundsTop => -178f;
    protected override Vector2? VariantCenterPos => new(0f, -88f);
    protected override Vector2? VariantIntentPos => new(-4f, -214f);
    protected override string? PreferredAttackAnimation => "attack_amber";
    protected override string? PreferredPowerUpAnimation => "power_amber";
    private int BashDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 8, 7);
    private int BashBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 6, 5);
    private int ShellDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);
    private int ShellBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 9, 8);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var amberBash = new MoveState(
            "AMBER_BASH_MOVE",
            AmberBashMove,
            new SingleAttackIntent(BashDamage),
            new DefendIntent());
        var stoneShell = new MoveState(
            "STONE_SHELL_MOVE",
            StoneShellMove,
            new SingleAttackIntent(ShellDamage),
            new DefendIntent());
        MoveState splitState = CreateSplitMoveState();

        amberBash.FollowUpState = stoneShell;
        stoneShell.FollowUpState = amberBash;
        var initial = StartMovePhase % 2 == 0 ? amberBash : stoneShell;
        return new MonsterMoveStateMachine([amberBash, stoneShell, splitState], initial);
    }

    private async Task AmberBashMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(BashDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.36f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await CreatureCmd.GainBlock(Creature, BashBlock, ValueProp.Move, null);
    }

    private async Task StoneShellMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0.45f);
        if (!CanAct)
            return;

        await CreatureCmd.GainBlock(Creature, ShellBlock, ValueProp.Move, null);
        await DamageCmd.Attack(ShellDamage)
            .FromMonster(this)
            .OnlyPlayAnimOnce()
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
    }
}
