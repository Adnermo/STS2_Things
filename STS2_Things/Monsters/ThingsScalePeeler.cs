using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// Scale-Peeler Wyrm (刮甲石蚓): Burrowing Depths crystal worm that retains its own Block,
/// siphons all remaining player Block at the end of each enemy turn, and scales Plated Slam
/// off accumulated armor.
/// </summary>
public sealed class ThingsScalePeeler : ThingsSpineMonster
{
    private const float ChiselContact = 0.46f;
    private const float SlamContact = 0.58f;
    private const float FlurryContact0 = 0.36f;
    private const float FlurryContact1 = 0.72f;
    private const float SiphonPeakMoment = 0.72f;
    private const string StoneImpactSfx = "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 60, 56);
    public override int MaxInitialHp => MinInitialHp + 4;

    public int OpeningBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6);
    private int ChiselDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9);
    private int ChiselBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6);
    private int SlamBaseDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);

    public int PlatedSlamBonus => (int)Math.Ceiling(
        (decimal)Creature.Block / Math.Max(1, CombatState?.Players.Count ?? 1));

    public int PlatedSlamDamage => SlamBaseDamage + PlatedSlamBonus;

    private int ScrapeDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);
    private int ScrapeHits => 2;
    private int ScrapeBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 10, 8);

    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/burrowing_bug/burrowing_bug_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/burrowing_bug/burrowing_bug_burrow";
    private string SlamSfx => "event:/sfx/enemy/enemy_attacks/burrowing_bug/burrowing_bug_hidden_attack";
    public override string HurtSfx => "event:/sfx/enemy/enemy_attacks/burrowing_bug/burrowing_bug_hurt";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/burrowing_bug/burrowing_bug_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Stone;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat(
    [
        ModelDb.Power<ThingsArmorSiphonPower>().ResolvedBigIconPath,
    ]);

    public override async Task BeforeCombatStart()
    {
        if (!Creature.IsAlive)
            return;

        await PowerCmd.Apply<ThingsArmorSiphonPower>(
            new ThrowingPlayerChoiceContext(), Creature, 1, Creature, null);
        await CreatureCmd.GainBlock(Creature, OpeningBlock, ValueProp.Move, null);
    }

    protected override void AddExtraAnimationStates(CreatureAnimator animator, AnimState idle)
    {
        animator.AddAnyState("Slam", new AnimState("slam") { NextState = idle });
        animator.AddAnyState("Flurry", new AnimState("flurry") { NextState = idle });
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var chiselBite = new MoveState(
            "CHISEL_BITE_MOVE",
            ChiselBiteMove,
            new SingleAttackIntent(ChiselDamage),
            new DefendIntent());
        var platedSlam = new MoveState(
            "PLATED_SLAM_MOVE",
            PlatedSlamMove,
            new SingleAttackIntent(() => PlatedSlamDamage));
        var burrowScrape = new MoveState(
            "BURROW_SCRAPE_MOVE",
            BurrowScrapeMove,
            new MultiAttackIntent(ScrapeDamage, ScrapeHits),
            new DefendIntent());

        chiselBite.FollowUpState = platedSlam;
        platedSlam.FollowUpState = burrowScrape;
        burrowScrape.FollowUpState = chiselBite;
        return new MonsterMoveStateMachine([chiselBite, platedSlam, burrowScrape], chiselBite);
    }

    internal async Task BeginArmorSiphonAsync(IReadOnlyList<Creature> blockedPlayers)
    {
        if (!Creature.IsAlive)
            return;

        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0f);
        NArmorSiphonVfx.PlaySiphonStream(Creature, blockedPlayers);
        await WaitForPose("cast", SiphonPeakMoment, SiphonPeakMoment * 0.72f);
    }

    internal async Task FinishArmorSiphonAsync()
    {
        await WaitForPose("cast", 1.38f, 0.25f);
    }

    private async Task ChiselBiteMove(IReadOnlyList<Creature> targets)
    {
        await StrikeAsync(ChiselDamage, "Attack", "attack", [ChiselContact], 0.90f, AttackSfx, "vfx/vfx_attack_slash");
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
            await CreatureCmd.GainBlock(Creature, ChiselBlock, ValueProp.Move, null);
        await WaitForPose("attack", 1.04f, 0f);
    }

    private async Task PlatedSlamMove(IReadOnlyList<Creature> targets)
    {
        await StrikeAsync(PlatedSlamDamage, "Slam", "slam", [SlamContact], 0.92f, SlamSfx, "vfx/vfx_attack_blunt");
        SfxCmd.Play(StoneImpactSfx);
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding && Creature.Block > 0)
        {
            int recoilLoss = Creature.Block / 2;
            if (recoilLoss > 0)
            {
                await Sts2VersionSeam.LoseBlock(new ThrowingPlayerChoiceContext(), Creature, recoilLoss, Creature);
                NArmorSiphonVfx.SpawnShatterBurst(Creature, 6);
            }
        }
        await WaitForPose("slam", 1.24f, 0f);
    }

    private async Task BurrowScrapeMove(IReadOnlyList<Creature> targets)
    {
        await StrikeAsync(
            ScrapeDamage,
            "Flurry",
            "flurry",
            [FlurryContact0, FlurryContact1],
            0.96f,
            AttackSfx,
            "vfx/vfx_attack_slash");
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
            await CreatureCmd.GainBlock(Creature, ScrapeBlock, ValueProp.Move, null);
        await WaitForPose("flurry", 1.30f, 0f);
    }

    private async Task StrikeAsync(
        int damage,
        string trigger,
        string animation,
        float[] contacts,
        float duration,
        string sfx,
        string hitFx)
    {
        int hit = 0;
        await DamageCmd.Attack(damage).WithHitCount(contacts.Length).FromMonster(this)
            .WithNoAttackerAnim()
            .AfterAttackerAnim(async () =>
            {
                if (hit == 0)
                    await CreatureCmd.TriggerAnim(Creature, trigger, 0f);
                float moment = contacts[Math.Min(hit, contacts.Length - 1)];
                float wait = hit == 0 ? moment : Math.Max(0.18f, moment - contacts[hit - 1]);
                await WaitForPose(animation, moment, wait);
                hit++;
            })
            .WithAttackerFx(null, sfx)
            .WithHitFx(hitFx)
            .Execute(null);
        await WaitForPose(animation, duration, 0f);
    }

    private Task WaitForPose(string animation, float moment, float fallback)
        => WaitForSpineMomentAsync(animation, moment, fallback);
}
