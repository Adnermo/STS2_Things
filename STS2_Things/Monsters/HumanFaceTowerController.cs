using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// Per-combat turntable &amp; multi-entity state coordinator for 人面塔 (The Visage Tower).
/// Coordinates the 4 independent stone face <see cref="Creature"/> entities across:
/// <list type="bullet">
///   <item>Single-face center visibility &amp; 2-turn stay rotation (<c>90°/180°/270°</c> skipping shattered blind sockets)</item>
///   <item>8-turn Full-Cycle Resonance (<c>LivingCount == 4</c>) with isolated enchantment placeholder</item>
///   <item>Post-break Rotational 2-Hit Combo (<c>LivingCount is 2 or 3</c>): Face A strikes -> whip-rotates -> Face B strikes immediately</item>
///   <item>Mid-turn shatter emergency rotation + 1-turn Stun window on the incoming face</item>
///   <item>Final Stand (<c>LivingCount == 1</c>): last face locks in center without rotating</item>
/// </list>
/// </summary>
public sealed class HumanFaceTowerController
{
    public const int FullCycleActiveTurns = 8;
    public const string WrathSlot = "wrath";
    public const string LamentSlot = "lament";
    public const string BastionSlot = "bastion";
    public const string MockerySlot = "mockery";

    private readonly List<ThingsHumanFaceTowerFace> _faces = [];
    private readonly HashSet<int> _shatteredSlots = [];
    private bool _combatStartSynced;
    private bool _rotatedDuringMovesThisRound;
    private bool _emergencyStunPending;
    private bool _stunnedTurnJustPerformed;

    private HumanFaceTowerController()
    {
    }

    public IReadOnlyList<ThingsHumanFaceTowerFace> Faces => _faces;

    public ThingsHumanFaceTowerFace? ActiveFace { get; private set; }

    public int StayTurnIndex { get; private set; }

    public int CycleActiveTurns { get; private set; }

    public int LivingCount => _faces.Count(f => f.IsRegisteredInCombat ? f.Creature.IsAlive : !_shatteredSlots.Contains(f.FaceSlotIndex));

    public int TurnsUntilRotation => LivingCount <= 1 ? 0 : Math.Max(1, 2 - StayTurnIndex);

    public int TurnsUntilFullCycleClimax => Math.Max(1, FullCycleActiveTurns - CycleActiveTurns);

    public bool IsUpcomingFullCycleClimaxTurn =>
        LivingCount == 4 && StayTurnIndex >= 1 && CycleActiveTurns == FullCycleActiveTurns - 1;

    public bool ShouldUseComboRotateOnTurn2 =>
        LivingCount is >= 2 and < 4;

    private ThingsHumanFaceTowerFace? PrimaryLivingFace =>
        _faces.FirstOrDefault(f => f.IsRegisteredInCombat && f.Creature.IsAlive);

    public static IReadOnlyList<(MonsterModel Monster, string? Slot)> CreateInitial()
    {
        var controller = new HumanFaceTowerController();
        var wrath = (ThingsHumanFaceTowerWrath)ModelDb.Monster<ThingsHumanFaceTowerWrath>().ToMutable();
        var lament = (ThingsHumanFaceTowerLament)ModelDb.Monster<ThingsHumanFaceTowerLament>().ToMutable();
        var bastion = (ThingsHumanFaceTowerBastion)ModelDb.Monster<ThingsHumanFaceTowerBastion>().ToMutable();
        var mockery = (ThingsHumanFaceTowerMockery)ModelDb.Monster<ThingsHumanFaceTowerMockery>().ToMutable();

        ThingsHumanFaceTowerFace[] ordered = [wrath, lament, bastion, mockery];
        foreach (ThingsHumanFaceTowerFace face in ordered)
        {
            face.AttachController(controller);
            controller._faces.Add(face);
        }

        controller.ActiveFace = wrath;
        controller.StayTurnIndex = 0;
        controller.CycleActiveTurns = 0;

        return
        [
            (wrath, WrathSlot),
            (lament, LamentSlot),
            (bastion, BastionSlot),
            (mockery, MockerySlot)
        ];
    }

    public static HumanFaceTowerController ForStandalone(ThingsHumanFaceTowerFace standaloneFace)
    {
        var controller = new HumanFaceTowerController();
        standaloneFace.AttachController(controller);
        controller._faces.Add(standaloneFace);
        controller.ActiveFace = standaloneFace;
        controller.StayTurnIndex = 0;
        controller.CycleActiveTurns = 0;
        return controller;
    }

    public ThingsHumanFaceTowerFace? PeekNextLivingFace(ThingsHumanFaceTowerFace? from = null)
    {
        ThingsHumanFaceTowerFace? anchor = from ?? ActiveFace;
        if (anchor == null || _faces.Count <= 1)
            return null;

        int startIdx = _faces.IndexOf(anchor);
        if (startIdx < 0)
            startIdx = 0;

        for (int step = 1; step < _faces.Count; step++)
        {
            ThingsHumanFaceTowerFace candidate = _faces[(startIdx + step) % _faces.Count];
            bool alive = candidate.IsRegisteredInCombat
                ? candidate.Creature.IsAlive
                : !_shatteredSlots.Contains(candidate.FaceSlotIndex);
            if (alive && candidate != anchor)
                return candidate;
        }

        return null;
    }

    public bool ConsumeEmergencyStunForMoveRoll(ThingsHumanFaceTowerFace face)
    {
        if (_emergencyStunPending && ActiveFace == face)
        {
            _emergencyStunPending = false;
            return true;
        }

        return false;
    }

    public void NotifyStunnedTurnPerformed(ThingsHumanFaceTowerFace face)
    {
        if (ActiveFace == face)
        {
            _emergencyStunPending = false;
            _stunnedTurnJustPerformed = true;
        }
    }

    public async Task EnsureFaceAddedAsync(ThingsHumanFaceTowerFace face)
    {
        face.AllowInternalPowerApplication = true;
        try
        {
            if (!face.Creature.HasPower<ThingsTowerPivotPower>())
            {
                await PowerCmd.Apply<ThingsTowerPivotPower>(
                    new ThrowingPlayerChoiceContext(),
                    face.Creature,
                    Math.Max(1, TurnsUntilRotation),
                    face.Creature,
                    null,
                    silent: true);
            }

            await face.ApplyUniquePassivePowerAsync();
        }
        finally
        {
            face.AllowInternalPowerApplication = false;
        }

        Sts2VersionSeam.SetTowerFaceNodeActive(face.Creature, face == ActiveFace && face.Creature.IsAlive);
        SyncBackgroundImmediate();
        SyncPivotDisplays();

        if (face == ActiveFace)
        {
            await face.OnEnteredCenterAsync(isInitialCombatStart: true);
        }
    }

    public Task EnsureCombatStartSyncedAsync()
    {
        if (_combatStartSynced)
            return Task.CompletedTask;

        _combatStartSynced = true;
        SyncAllFaceVisibility();
        SyncBackgroundImmediate();
        SyncPivotDisplays();
        return Task.CompletedTask;
    }

    public async Task OnEnemySideTurnEndedAsync(ThingsHumanFaceTowerFace caller, PlayerChoiceContext choiceContext)
    {
        _ = choiceContext;
        if (caller != PrimaryLivingFace || ActiveFace == null || LivingCount <= 0)
            return;

        if (_stunnedTurnJustPerformed)
        {
            _stunnedTurnJustPerformed = false;
            _rotatedDuringMovesThisRound = false;
            StayTurnIndex = 0;
            SyncPivotDisplays();
            return;
        }

        if (_rotatedDuringMovesThisRound)
        {
            _rotatedDuringMovesThisRound = false;
            StayTurnIndex = 0;
            SyncPivotDisplays();
            return;
        }

        CycleActiveTurns = (CycleActiveTurns + 1) % FullCycleActiveTurns;
        StayTurnIndex++;

        if (LivingCount <= 1)
        {
            StayTurnIndex %= 2;
            SyncPivotDisplays();
            return;
        }

        if (StayTurnIndex >= 2)
        {
            ThingsHumanFaceTowerFace? next = PeekNextLivingFace(ActiveFace);
            StayTurnIndex = 0;
            if (next != null && next != ActiveFace)
            {
                await RotateToFaceAsync(next, comboWhip: false, waitAnimation: true);
            }
        }

        SyncPivotDisplays();
    }

    public async Task ExecuteComboRotationAndFollowUpAsync(
        ThingsHumanFaceTowerFace starterFace,
        IReadOnlyList<Creature> targets)
    {
        if (!starterFace.Creature.IsAlive || ActiveFace != starterFace || CombatManager.Instance.IsOverOrEnding || LivingCount < 2)
            return;

        ThingsHumanFaceTowerFace? next = PeekNextLivingFace(starterFace);
        if (next == null || next == starterFace || !next.Creature.IsAlive)
            return;

        CycleActiveTurns = (CycleActiveTurns + 1) % FullCycleActiveTurns;
        _rotatedDuringMovesThisRound = true;
        StayTurnIndex = 0;

        await RotateToFaceAsync(next, comboWhip: true, waitAnimation: true);
        SyncPivotDisplays();

        if (!CombatManager.Instance.IsOverOrEnding && next.Creature.IsAlive && ActiveFace == next)
        {
            await next.ExecuteComboFollowUpAsync(targets);
        }
    }

    public async Task ExecuteFullCycleClimaxAsync(
        ThingsHumanFaceTowerFace casterFace,
        IReadOnlyList<Creature> targets,
        int climaxDamage)
    {
        if (!casterFace.Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot($"climax_cycle_roar_{casterFace.FaceSlotIndex}", casterFace.IdleAnimationName);
        SfxCmd.Play(casterFace.StoneRoarSfx);
        await Cmd.Wait(0.52f);

        if (!casterFace.Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
        if (NCombatRoom.Instance != null && casterFace.CombatState != null)
        {
            VfxCmd.PlayOnCreatureCenters(
                casterFace.CombatState.PlayerCreatures.Where(c => c.IsAlive).ToList(),
                "vfx/vfx_rock_shatter");
        }

        await DamageCmd.Attack(climaxDamage)
            .FromMonster(casterFace)
            .WithNoAttackerAnim()
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);

        if (casterFace.Creature.IsAlive && ActiveFace == casterFace && !CombatManager.Instance.IsOverOrEnding)
        {
            await ApplyFullCycleEnchantmentPlaceholderAsync(casterFace, targets);
            await Cmd.Wait(0.35f);
        }
    }

    /// <summary>
    /// Isolated placeholder for the Full-Cycle Resonance enchantment (Q5).
    /// Currently grants every living stone face permanent +2(+3) Strength and 10(12) Block.
    /// </summary>
    public async Task ApplyFullCycleEnchantmentPlaceholderAsync(
        ThingsHumanFaceTowerFace casterFace,
        IReadOnlyList<Creature> _)
    {
        int strengthBonus = AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2);
        int blockBonus = AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 12, 10);
        var context = new ThrowingPlayerChoiceContext();

        foreach (ThingsHumanFaceTowerFace face in _faces.Where(f => f.IsRegisteredInCombat && f.Creature.IsAlive))
        {
            face.AllowInternalPowerApplication = true;
            try
            {
                await PowerCmd.Apply<StrengthPower>(
                    context,
                    face.Creature,
                    strengthBonus,
                    casterFace.Creature,
                    null);
            }
            finally
            {
                face.AllowInternalPowerApplication = false;
            }

            if (face == ActiveFace)
            {
                await CreatureCmd.GainBlock(face.Creature, blockBonus, ValueProp.Move, null);
            }
        }
    }

    public async Task HandleFaceDeathAsync(ThingsHumanFaceTowerFace deadFace, PlayerChoiceContext choiceContext)
    {
        _shatteredSlots.Add(deadFace.FaceSlotIndex);
        await Sts2VersionSeam.DislodgeImpaledArrowsAsync(deadFace.Creature, choiceContext);
        Sts2VersionSeam.SetTowerFaceNodeActive(deadFace.Creature, false);

        float shatterDuration = NHumanFaceTowerBossBackground.Current?.PlayFaceShatter(deadFace.FaceSlotIndex) ?? 0.85f;
        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);

        ThingsHumanFaceTowerFace? next = PeekNextLivingFace(deadFace);
        if (next == null || LivingCount <= 0)
        {
            ActiveFace = null;
            await Cmd.Wait(Math.Min(shatterDuration, 0.65f), ignoreCombatEnd: true);
            return;
        }

        await Cmd.Wait(Math.Clamp(shatterDuration * 0.55f, 0.35f, 0.65f));
        if (CombatManager.Instance.IsOverOrEnding)
            return;

        if (ActiveFace == deadFace || ActiveFace == null || !ActiveFace.Creature.IsAlive)
        {
            StayTurnIndex = 0;
            bool diedOnPlayerTurn = deadFace.CombatState?.CurrentSide == CombatSide.Player;

            await RotateToFaceAsync(next, comboWhip: true, waitAnimation: true);
            NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot(
                $"face_stun_{next.FaceSlotIndex}",
                next.IdleAnimationName);

            if (diedOnPlayerTurn)
            {
                _emergencyStunPending = false;
                await CreatureCmd.Stun(
                    next.Creature,
                    next.RecoverFromStunAsync,
                    ThingsHumanFaceTowerFace.MoveRouterId);
            }
            else
            {
                // Died during Enemy Turn (e.g., Thorns / Doom): keep next face inert for the rest of
                // the current Enemy Turn loop and roll EMERGENCY_STUN_MOVE on the upcoming Player Turn.
                _rotatedDuringMovesThisRound = true;
                _emergencyStunPending = true;
            }
        }

        SyncPivotDisplays();
    }

    public async Task ForceRotateToForTestAsync(ThingsHumanFaceTowerFace targetFace, bool comboWhip = false)
    {
        StayTurnIndex = 0;
        await RotateToFaceAsync(targetFace, comboWhip, waitAnimation: false);
        SyncPivotDisplays();
    }

    private async Task RotateToFaceAsync(ThingsHumanFaceTowerFace nextFace, bool comboWhip, bool waitAnimation)
    {
        ThingsHumanFaceTowerFace? previous = ActiveFace;
        int fromSlot = previous?.FaceSlotIndex ?? nextFace.FaceSlotIndex;

        if (previous != null && previous != nextFace && previous.IsRegisteredInCombat)
        {
            await Sts2VersionSeam.DislodgeImpaledArrowsAsync(previous.Creature);
        }

        previous?.OnRotatedAway();
        if (previous != null && previous.IsRegisteredInCombat)
        {
            Sts2VersionSeam.SetTowerFaceNodeActive(previous.Creature, false);
        }

        ActiveFace = nextFace;
        if (nextFace.IsRegisteredInCombat && nextFace.Creature.IsAlive)
        {
            Sts2VersionSeam.SetTowerFaceNodeActive(nextFace.Creature, true);
        }

        float animSeconds = NHumanFaceTowerBossBackground.Current?.PlayRotation(
            fromSlot,
            nextFace.FaceSlotIndex,
            comboWhip,
            nextFace.IdleAnimationName) ?? (comboWhip ? 0.45f : 0.75f);

        if (comboWhip)
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
        else
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);

        if (waitAnimation && NHumanFaceTowerBossBackground.Current != null)
        {
            await Cmd.Wait(Math.Clamp(animSeconds * 0.72f, 0.30f, 1.45f));
        }

        if (nextFace.IsRegisteredInCombat && nextFace.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await nextFace.OnEnteredCenterAsync(isInitialCombatStart: false);
        }
    }

    private void SyncAllFaceVisibility()
    {
        foreach (ThingsHumanFaceTowerFace face in _faces.Where(f => f.IsRegisteredInCombat))
        {
            bool visible = face == ActiveFace && face.Creature.IsAlive;
            Sts2VersionSeam.SetTowerFaceNodeActive(face.Creature, visible);
        }
    }

    private void SyncBackgroundImmediate()
    {
        if (ActiveFace != null)
        {
            NHumanFaceTowerBossBackground.Current?.SetActiveSlotImmediate(
                ActiveFace.FaceSlotIndex,
                ActiveFace.IdleAnimationName,
                _shatteredSlots);
        }
    }

    public void SyncPivotDisplays()
    {
        int countdown = Math.Max(1, TurnsUntilRotation);
        foreach (ThingsHumanFaceTowerFace face in _faces.Where(f => f.IsRegisteredInCombat && f.Creature.IsAlive))
        {
            if (face.Creature.GetPower<ThingsTowerPivotPower>() is { } pivot)
            {
                if (pivot.Amount != countdown)
                    pivot.SetAmount(countdown, silent: true);
                pivot.RefreshPivotDisplay();
            }

            if (face == ActiveFace)
            {
                Sts2VersionSeam.RefreshMonsterIntentsUi(face.Creature);
            }
        }
    }
}
