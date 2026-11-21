using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Encounters;
using STS2_Things.Platform;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// Gravetide Slug, the enlarged Corpse Slug boss of the Underdocks.
/// </summary>
public sealed class ThingsGravetideSlug : GravetideSlugBase
{
    private const int SummonCount = 2;
    private const int GoopCorpseCount = 1;

    internal const string GravetideDevourSfx =
        "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_devour";
    internal const string GravetideDevourEndSfx =
        "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_devour_end";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 230, 220) + (Sts2VersionSeam.IsDread() ? 35 : 0);

    public override int MaxInitialHp => MinInitialHp;

    protected override int WhipSlapDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 6, 5) + (Sts2VersionSeam.IsDread() ? 3 : 0);

    protected override int GlompDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 14, 12) + (Sts2VersionSeam.IsDread() ? 3 : 0);

    protected override string LightAttackSfx =>
        "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_attack_light";

    protected override string AttackSfx =>
        "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_attack";

    public override string DeathSfx =>
        "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_die";

    protected override string VisualsPath =>
        SceneHelper.GetScenePath("creature_visuals/gravetide_slug");

    public override float HpBarSizeReduction => 140f;

    public override Vector2 ExtraDeathVfxPadding => new(1.8f, 1.8f);

    public override IEnumerable<string> AssetPaths => base.AssetPaths
        .Concat(ModelDb.Monster<ThingsGravetideCorpseSlug>().AssetPaths)
        .Concat(ModelDb.Monster<ThingsGravetideSlugCorpse>().AssetPaths)
        .Append(ModelDb.Power<ThingsGravetideDigestionPower>().ResolvedBigIconPath)
        .Append(ModelDb.Power<RavenousPower>().ResolvedBigIconPath)
        .Append(ModelDb.Power<StrengthPower>().ResolvedBigIconPath)
        .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();

        int healingAmount = (int)decimal.Ceiling(Creature.MaxHp * 0.05m);
        await PowerCmd.Apply<ThingsGravetideDigestionPower>(
            new ThrowingPlayerChoiceContext(),
            Creature,
            healingAmount,
            Creature,
            null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var whipSlap = new MoveState(
            "WHIP_SLAP_MOVE",
            WhipSlapMove,
            new MultiAttackIntent(WhipSlapDamage, WhipSlapRepeat));
        var glomp = new MoveState(
            "GLOMP_MOVE",
            GlompMove,
            new SingleAttackIntent(GlompDamage));
        var goop = new MoveState(
            "GOOP_MOVE",
            GoopAndCreateCorpseMove,
            new DebuffIntent());
        var summon = new MoveState(
            "SUMMON_CORPSE_SLUGS_MOVE",
            SummonCorpseSlugsMove,
            new SummonIntent());
        var growth = new MoveState(
            "GROW_MOVE",
            GrowMove,
            new DefendIntent(), new BuffIntent());
        var summonBranch = new ConditionalBranchState("SUMMON_CORPSE_SLUGS_BRANCH");

        // The branch keeps the summon intent out of the timeline when all six
        // dedicated slug slots are occupied.  Its fallback resumes the normal
        // damage cycle instead of spending an empty enemy turn.
        summonBranch.AddState(summon, HasVacantCorpseSlugSlot);
        summonBranch.AddState(whipSlap, () => true);

        whipSlap.FollowUpState = glomp;
        glomp.FollowUpState = goop;
        goop.FollowUpState = summonBranch;
        summon.FollowUpState = growth;
        growth.FollowUpState = whipSlap;

        return new MonsterMoveStateMachine(
            [whipSlap, glomp, goop, summon, growth, summonBranch],
            whipSlap);
    }

    private bool HasVacantCorpseSlugSlot()
    {
        return GetVacantCorpseSlugSlots().Count > 0;
    }

    private IReadOnlyList<string?> GetVacantCorpseSlugSlots()
    {
        IReadOnlyList<string>? encounterSlots = CombatState.Encounter?.Slots;
        bool hasEncounterSlots = encounterSlots != null &&
            Enumerable.Range(0, ThingsGravetideSlugBossEncounter.CorpseSlugSlotCount)
                .Select(ThingsGravetideSlugBossEncounter.GetCorpseSlugSlotName)
                .All(slot => encounterSlots.Contains(slot));
        if (hasEncounterSlots)
        {
            return ThingsGravetideSlugBossEncounter
                .GetVacantCorpseSlugSlots(CombatState)
                .Select(slot => encounterSlots!.Contains(slot) ? slot : null)
                .ToArray();
        }

        int occupiedCount = CombatState.Enemies.Count(enemy =>
            enemy.IsAlive &&
            enemy.Monster is (ThingsGravetideCorpseSlug or ThingsGravetideSlugCorpse));
        int vacantCount = Math.Max(0, ThingsGravetideSlugBossEncounter.CorpseSlugSlotCount - occupiedCount);
        return new string?[vacantCount];
    }

    private async Task GoopAndCreateCorpseMove(IReadOnlyList<Creature> targets)
    {
        await GoopMove(targets);
        if (!CombatState.IsLiveCombat())
            return;

        string?[] slots = GetVacantCorpseSlugSlots()
            .Take(GoopCorpseCount)
            .ToArray();
        foreach (string? candidateSlot in slots)
        {
            string? slot = candidateSlot != null && CombatState.Encounter?.Slots.Contains(candidateSlot) == true
                ? candidateSlot
                : null;
            var corpse = (ThingsGravetideSlugCorpse)ModelDb
                .Monster<ThingsGravetideSlugCorpse>()
                .ToMutable();
            corpse.DelayDigestionUntilNextEnemyTurn = true;
            await CreatureCmd.Add(corpse, CombatState, slotName: slot);
        }
    }

    private async Task SummonCorpseSlugsMove(IReadOnlyList<Creature> _)
    {
        if (!CombatState.IsLiveCombat())
            return;

        string?[] slots = GetVacantCorpseSlugSlots()
            .Take(SummonCount)
            .ToArray();
        if (slots.Length == 0)
            return;

        await CreatureCmd.TriggerAnim(Creature, "Attack", 0.2f);
        foreach (string? candidateSlot in slots)
        {
            string? slot = candidateSlot != null && CombatState.Encounter?.Slots.Contains(candidateSlot) == true
                ? candidateSlot
                : null;
            await CreatureCmd.Add<ThingsGravetideCorpseSlug>(CombatState, slot);
        }
    }

    private async Task GrowMove(IReadOnlyList<Creature> _)
    {
        if (!CombatState.IsLiveCombat())
            return;

        await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0.5f);
        await PowerCmd.Apply<StrengthPower>(
            new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        foreach (Creature slug in LivingCorpseSlugsInSlotOrder().ToArray())
        {
            await PowerCmd.Apply<StrengthPower>(
                new ThrowingPlayerChoiceContext(), slug, 1m, Creature, null);
        }
        await CreatureCmd.GainBlock(Creature, 10m, ValueProp.Move, null);
    }

    private IEnumerable<Creature> LivingCorpseSlugsInSlotOrder()
    {
        var yielded = new HashSet<Creature>();
        foreach (string slot in Enumerable.Range(
                     0, ThingsGravetideSlugBossEncounter.CorpseSlugSlotCount)
                 .Select(ThingsGravetideSlugBossEncounter.GetCorpseSlugSlotName))
        {
            Creature? slug = CombatState.Enemies.FirstOrDefault(enemy =>
                enemy.SlotName == slot &&
                enemy.IsAlive &&
                enemy.Monster is ThingsGravetideCorpseSlug);
            if (slug != null && yielded.Add(slug))
                yield return slug;
        }

        foreach (Creature slug in CombatState.Enemies.Where(enemy =>
                     enemy.IsAlive &&
                     enemy.Monster is ThingsGravetideCorpseSlug))
        {
            if (yielded.Add(slug))
                yield return slug;
        }
    }
}
