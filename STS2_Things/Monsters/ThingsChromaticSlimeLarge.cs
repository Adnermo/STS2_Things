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
/// Large Prismatic Slime (Act 2 Regular pool anchor). Cycles an original 3-move prismatic rhythm
/// (<c>PRISM_SPLASH</c> -&gt; <c>REFRACTIVE_GEL</c> -&gt; <c>CHROMATIC_SLAM</c>) and immediately interrupts
/// into <c>SPLIT_MOVE</c> at &lt;= 50% HP, splitting into an asymmetric pair of Medium Chromatic Slimes
/// that inherit its remaining HP.
/// </summary>
public sealed class ThingsChromaticSlimeLarge : ThingsChromaticSlimeBase
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 120, 112);
    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 128, 120);

    private int PrismSplashDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9);
    private const int PrismSplashSlimedCount = 2;
    private int RefractiveBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 14, 12);
    private int RefractiveStrength => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2);
    private int ChromaticSlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 27, 24);
    private const int ChromaticSlamSlimedCount = 1;

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

        int splitThreshold = Math.Max(1, Creature.MaxHp / 2);
        await PowerCmd.Apply<ThingsChromaticFissionPower>(
            new ThrowingPlayerChoiceContext(), Creature, splitThreshold, Creature, null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var prismSplash = new MoveState(
            "PRISM_SPLASH_MOVE",
            PrismSplashMove,
            new SingleAttackIntent(PrismSplashDamage),
            new StatusIntent(PrismSplashSlimedCount));
        var refractiveGel = new MoveState(
            "REFRACTIVE_GEL_MOVE",
            RefractiveGelMove,
            new DefendIntent(),
            new BuffIntent());
        var chromaticSlam = new MoveState(
            "CHROMATIC_SLAM_MOVE",
            ChromaticSlamMove,
            new SingleAttackIntent(ChromaticSlamDamage),
            new StatusIntent(ChromaticSlamSlimedCount));
        MoveState splitState = CreateSplitMoveState();

        prismSplash.FollowUpState = refractiveGel;
        refractiveGel.FollowUpState = chromaticSlam;
        chromaticSlam.FollowUpState = prismSplash;
        return new MonsterMoveStateMachine([prismSplash, refractiveGel, chromaticSlam, splitState], prismSplash);
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

        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, PrismSplashSlimedCount, null);
    }

    private async Task RefractiveGelMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(BuffSfx);
        await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0.48f);
        if (!CanAct)
            return;

        await CreatureCmd.GainBlock(Creature, RefractiveBlock, ValueProp.Move, null);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, RefractiveStrength, Creature, null);
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
        if (!CanAct)
            return;

        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, ChromaticSlamSlimedCount, null);
    }
}
