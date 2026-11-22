using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// Large Prismatic Slime (Act 2 Regular pool anchor). Carries three visible elemental cores and splits
/// on death into 2 Medium Chromatic Slimes matching the final blow.
/// </summary>
public sealed class ThingsChromaticSlimeLarge : ThingsChromaticSlimeBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 36, 34);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 40, 38);

    private int PrismSplashDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);
    private int ChromaticSlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 10);
    private int RefractiveBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 7, 6);

    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_slime";
    private string BuffSfx => "event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_buff";
    public override string HurtSfx => "event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_hurt";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_die";

    public override IEnumerable<string> AssetPaths => base.AssetPaths
        .Concat(
        [
            ModelDb.Power<ThingsChromaticFissionPower>().ResolvedBigIconPath,
            SceneHelper.GetScenePath("vfx/vfx_slime_impact"),
        ])
        .Concat(ModelDb.Card<Slimed>().AllPortraitPaths)
        .Concat(ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>().AssetPaths)
        .Concat(ModelDb.Monster<ThingsChromaticSlimeMediumAmber>().AssetPaths)
        .Concat(ModelDb.Monster<ThingsChromaticSlimeMediumAzure>().AssetPaths);

    public override async Task BeforeCombatStart()
    {
        if (!Creature.IsAlive)
            return;

        await PowerCmd.Apply<ThingsChromaticFissionPower>(
            new ThrowingPlayerChoiceContext(), Creature, 2m, Creature, null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var prismSplash = new MoveState(
            "PRISM_SPLASH_MOVE",
            PrismSplashMove,
            new SingleAttackIntent(PrismSplashDamage),
            new StatusIntent(1));
        var chromaticSlam = new MoveState(
            "CHROMATIC_SLAM_MOVE",
            ChromaticSlamMove,
            new SingleAttackIntent(ChromaticSlamDamage));
        var refractiveGel = new MoveState(
            "REFRACTIVE_GEL_MOVE",
            RefractiveGelMove,
            new DefendIntent(),
            new BuffIntent());

        prismSplash.FollowUpState = chromaticSlam;
        chromaticSlam.FollowUpState = refractiveGel;
        refractiveGel.FollowUpState = prismSplash;
        return new MonsterMoveStateMachine([prismSplash, chromaticSlam, refractiveGel], prismSplash);
    }

    private async Task PrismSplashMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(PrismSplashDamage)
            .FromMonster(this)
            .WithAttackerAnim("Cast", 0.56f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, CastSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
        if (!CanAct)
            return;

        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 1, null);
    }

    private async Task ChromaticSlamMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(ChromaticSlamDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.44f)
            .OnlyPlayAnimOnce()
            .WithAttackerFx(null, AttackSfx)
            .WithHitFx("vfx/vfx_slime_impact")
            .Execute(null);
    }

    private async Task RefractiveGelMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(BuffSfx);
        await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0.48f);
        if (!CanAct)
            return;

        await CreatureCmd.GainBlock(Creature, RefractiveBlock, ValueProp.Move, null);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
    }
}
