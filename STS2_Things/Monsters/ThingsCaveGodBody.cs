using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 活体巨岩（Living Megalith）的核心：唯一的招式循环持有者，双臂只投射本模型的下一招。
/// 本体生命条默认可见并可直接受击；当双臂均未倒下时拥有「磐岩护体」（受伤变为 25%），
/// 击倒任意一臂即解除护体并进入两回合破防窗口；生命降至 50% 时当场卡住阈值并锁定无限血 ∞ 与 ? 意图，
/// 当回合苏醒为熔岩狂暴姿态（Phase 2）并恢复至精确 50% 生命。
/// </summary>
public sealed class ThingsCaveGodBody : ThingsMonsterModel
{
    // Move ids double as localization keys: THINGS_CAVE_GOD_BODY.moves.<id>.title.
    public const string GrabId = "P2_REST_GRAB";
    public const string AirSlamId = "P2_REST_SLAM";
    public const string SweepId = "P1_REST_SWEEP";
    public const string RockSweepId = "ROCK_SWEEP";
    public const string JabsId = "ALTERNATING_JABS";
    public const string HeavyPunchId = "HEAVY_PUNCH";
    public const string GuardId = "MOUNTAIN_GUARD";
    public const string CrushId = "DOUBLE_FIST_CRUSH";
    public const string AngrySlamId = "P2_CENTRAL_SLAM";
    public const string Exposed1Id = "WEAK_PRONE_1";
    public const string RecoverId = "WEAK_UNKNOWN_3";
    public const string EnrageId = "ENRAGE_ROAR";
    public const string EnrageRecoverId = "ENRAGE_RECOVER";

    public int Phase { get; private set; } = 1;
    public bool IsWeakPhase { get; private set; }
    public bool IsLeftArmBroken { get; private set; }
    public bool IsDefeated { get; private set; }

    private bool _isEnrageLocked;
    private bool _applyingEnrageHp;
    private int _savedEnrageHp;
    private int _savedEnrageMaxHp;

    public bool IsEnrageLocked => _isEnrageLocked;
    public bool IsRightLeading => _rightLeads;
    public int EnrageHpThreshold => ((_savedEnrageMaxHp > 0 ? _savedEnrageMaxHp : Creature.MaxHp) + 1) / 2;
    public bool ShouldTriggerEnrage =>
        Phase == 1 && !IsDefeated && Creature.IsAlive && (_isEnrageLocked || Creature.CurrentHp <= EnrageHpThreshold);
    public bool ShouldTremble =>
        Phase >= 2 && !IsDefeated && !_isEnrageLocked && Creature.IsAlive && Creature.CurrentHp * 5 <= Creature.MaxHp;
    public bool BothArmsHealthy => LeftHand is { IsDown: false } && RightHand is { IsDown: false };
    public bool HasGrabbingHand => Hands.Any(h => h.IsGrabbing);

    private string _resumeMove = GrabId;
    private bool _deferExposure;
    private bool _rightLeads;
    private bool _leadInitialized;
    private bool _releasingCaptives;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        Phase = 1;
        IsWeakPhase = false;
        IsLeftArmBroken = false;
        IsDefeated = false;
        _isEnrageLocked = false;
        _applyingEnrageHp = false;
        _savedEnrageHp = 0;
        _savedEnrageMaxHp = 0;
        _resumeMove = GrabId;
        _deferExposure = false;
        _rightLeads = false;
        _leadInitialized = false;
        _releasingCaptives = false;
    }

    public void SetRightLeadsForTest(bool rightLeads)
    {
        _leadInitialized = true;
        _rightLeads = rightLeads;
        ProjectHandIntents();
    }

    private void EnsureLeadInitialized()
    {
        if (!_leadInitialized)
        {
            _leadInitialized = true;
            if (RunRng?.MonsterAi != null)
                _rightLeads = RunRng.MonsterAi.NextBool();
        }
    }

    public ThingsCaveGodLeftHand? LeftHand => CombatState?.Enemies.Select(c => c.Monster).OfType<ThingsCaveGodLeftHand>().FirstOrDefault();
    public ThingsCaveGodRightHand? RightHand => CombatState?.Enemies.Select(c => c.Monster).OfType<ThingsCaveGodRightHand>().FirstOrDefault();
    private IEnumerable<ThingsCaveGodHand> Hands => CombatState?.Enemies.Select(c => c.Monster).OfType<ThingsCaveGodHand>() ?? [];
    public NCaveGodBossBackground? Background => (NCombatRoom.Instance?.Background ?? NBestiary.Instance?.Layout)?.GetNodeOrNull<NCaveGodBossBackground>("%CaveGod");

    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Stone;
    public override string DeathSfx => "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone";
    protected override string VisualsPath => "res://scenes/creature_visuals/things_cave_god.tscn";
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 7.20f;
    public override float HpBarSizeReduction => 120f;
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 270, 250) + (Sts2VersionSeam.IsDread() ? 20 : 0);
    public override int MaxInitialHp => MinInitialHp;

    private static readonly AccessTools.FieldRef<MonsterModel, Creature?>? CreatureFieldRef =
        AccessTools.Field(typeof(MonsterModel), "_creature") != null
            ? AccessTools.FieldRefAccess<MonsterModel, Creature?>("_creature")
            : null;

    private Creature? TryGetCreature() => IsMutable && CreatureFieldRef != null ? CreatureFieldRef(this) : null;

    protected override bool IsMonsterInteractable => !IsDefeated && (TryGetCreature() is not { } creature || creature.IsAlive);
    public override bool ShouldAllowHitting(Creature creature) => creature != Creature || (!IsDefeated && Creature.IsAlive);
    public override bool ShouldAllowTargeting(Creature target) => target != Creature || (!IsDefeated && Creature.IsAlive);

    private int JabDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);
    private int GuardDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 15, 13);
    private int GuardBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 18, 15);
    private int CrushDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 9) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    private int AngrySlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 22, 19) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    private int StrengthGain => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    private bool CanAttack => !IsWeakPhase && !IsDefeated && !_isEnrageLocked && Creature.IsAlive;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState grab = new(GrabId, GrabCycleMove);
        MoveState airSlam = new(AirSlamId, AirSlamCycleMove);
        MoveState sweep = new(SweepId, SweepCycleMove);
        MoveState rockSweep = new(RockSweepId, RockSweepCycleMove);
        MoveState jabs = new(JabsId, JabsMove, new MultiAttackIntent(JabDamage, 3));
        MoveState heavyPunch = new(HeavyPunchId, HeavyPunchCycleMove);
        MoveState guard = new(GuardId, GuardMove, new SingleAttackIntent(GuardDamage), new DefendIntent(), new BuffIntent());
        MoveState crush = new(CrushId, CrushMove, new MultiAttackIntent(CrushDamage, 2), new DebuffIntent());
        MoveState angrySlam = new(AngrySlamId, AngrySlamMove, new SingleAttackIntent(AngrySlamDamage), new BuffIntent());
        MoveState weak1 = new(Exposed1Id, RestMove);
        MoveState recover = new(RecoverId, RecoverMove, new HealIntent(), new BuffIntent());
        MoveState enrage = new(EnrageId, EnrageMove, new UnknownIntent());
        MoveState enrageRecover = new(EnrageRecoverId, EnrageRecoverMove, new UnknownIntent());

        MoveState[] cycleStates = [grab, airSlam, sweep, rockSweep, jabs, heavyPunch, guard, crush, angrySlam];

        ConditionalBranchState afterGrab = new("AFTER_GRAB");
        afterGrab.AddState(enrage, () => ShouldTriggerEnrage);
        afterGrab.AddState(airSlam, () => HasGrabbingHand);
        afterGrab.AddState(sweep, () => Phase == 1);
        afterGrab.AddState(rockSweep, () => true);
        grab.FollowUpState = afterGrab;

        ConditionalBranchState afterAirSlam = new("AFTER_AIR_SLAM");
        afterAirSlam.AddState(enrage, () => ShouldTriggerEnrage);
        afterAirSlam.AddState(sweep, () => Phase == 1);
        afterAirSlam.AddState(rockSweep, () => true);
        airSlam.FollowUpState = afterAirSlam;

        ConditionalBranchState afterSweep = new("AFTER_SWEEP");
        afterSweep.AddState(enrage, () => ShouldTriggerEnrage);
        afterSweep.AddState(rockSweep, () => Phase == 1);
        afterSweep.AddState(angrySlam, () => true);
        sweep.FollowUpState = afterSweep;

        ConditionalBranchState afterRockSweep = new("AFTER_ROCK_SWEEP");
        afterRockSweep.AddState(enrage, () => ShouldTriggerEnrage);
        afterRockSweep.AddState(jabs, () => Phase == 1);
        afterRockSweep.AddState(sweep, () => true);
        rockSweep.FollowUpState = afterRockSweep;

        ConditionalBranchState afterJabs = new("AFTER_JABS");
        afterJabs.AddState(enrage, () => ShouldTriggerEnrage);
        afterJabs.AddState(heavyPunch, () => true);
        jabs.FollowUpState = afterJabs;

        ConditionalBranchState afterHeavyPunch = new("AFTER_HEAVY_PUNCH");
        afterHeavyPunch.AddState(enrage, () => ShouldTriggerEnrage);
        afterHeavyPunch.AddState(guard, () => Phase == 1);
        afterHeavyPunch.AddState(grab, () => true);
        heavyPunch.FollowUpState = afterHeavyPunch;

        ConditionalBranchState afterGuard = new("AFTER_GUARD");
        afterGuard.AddState(enrage, () => ShouldTriggerEnrage);
        afterGuard.AddState(grab, () => true);
        guard.FollowUpState = afterGuard;

        crush.FollowUpState = heavyPunch;
        angrySlam.FollowUpState = crush;

        ConditionalBranchState exposedNext = new("EXPOSED_RECOVER_BRANCH");
        exposedNext.AddState(enrageRecover, () => ShouldTriggerEnrage);
        exposedNext.AddState(recover, () => true);
        weak1.FollowUpState = exposedNext;

        ConditionalBranchState resume = new("RESUME_CYCLE");
        resume.AddState(enrage, () => ShouldTriggerEnrage);
        foreach (MoveState state in cycleStates)
            resume.AddState(state, () => _resumeMove == state.Id);
        resume.AddState(grab, () => true);
        recover.FollowUpState = resume;

        enrage.FollowUpState = crush;
        enrageRecover.FollowUpState = crush;

        foreach (MoveState state in new[] { weak1, recover, enrage, enrageRecover })
            state.MustPerformOnceBeforeTransitioning = true;

        List<MonsterState> allStates =
        [
            .. cycleStates,
            weak1,
            recover,
            enrage,
            enrageRecover,
            afterGrab,
            afterAirSlam,
            afterSweep,
            afterRockSweep,
            afterJabs,
            afterHeavyPunch,
            afterGuard,
            exposedNext,
            resume
        ];
        return new MonsterMoveStateMachine(allStates, grab);
    }

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Creature || Phase != 1 || IsDefeated || amount <= 0m)
            return amount;
        if (_isEnrageLocked)
            return 0m;
        int threshold = EnrageHpThreshold;
        if (target.CurrentHp - amount <= threshold)
            return Math.Max(0m, target.CurrentHp - threshold);
        return amount;
    }

    public override bool ShouldDie(Creature creature)
    {
        if (creature == Creature && Phase == 1 && !IsDefeated)
            return false;
        return base.ShouldDie(creature);
    }

    public override async Task AfterPreventingDeath(Creature creature)
    {
        if (creature == Creature && Phase == 1 && !IsDefeated)
            await EnterEnrageLockAsync();
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (_applyingEnrageHp || target != Creature || Phase != 1 || _isEnrageLocked || IsDefeated || !Creature.IsAlive)
            return;
        if (Creature.CurrentHp <= EnrageHpThreshold)
            await EnterEnrageLockAsync();
    }

    private async Task EnterEnrageLockAsync()
    {
        if (_isEnrageLocked || Phase != 1 || IsDefeated)
            return;

        _savedEnrageMaxHp = Creature.MaxHp;
        _savedEnrageHp = EnrageHpThreshold;
        _isEnrageLocked = true;
        _deferExposure = false;
        Creature.HpDisplay = HpDisplay.InfiniteWithoutNumbers;

        _applyingEnrageHp = true;
        try
        {
            await CreatureCmd.SetCurrentHp(Creature, _savedEnrageMaxHp);
        }
        finally
        {
            _applyingEnrageHp = false;
        }

        RefreshBodyHealthBarUi();
        SyncTremorVisual();
        if (!IsPerformingMove)
            SetMoveImmediate(Move(IsWeakPhase ? EnrageRecoverId : EnrageId), forceTransition: true);
        ProjectHandIntents();

        if (HasGrabbingHand || Background?.HasCapturedPlayers == true)
            await ReleaseCaptives(stun: false);
    }

    private async Task ExitEnrageLockAsync()
    {
        int restoreHp = _savedEnrageHp > 0 ? _savedEnrageHp : EnrageHpThreshold;
        _isEnrageLocked = false;
        Creature.HpDisplay = HpDisplay.Normal;

        _applyingEnrageHp = true;
        try
        {
            await CreatureCmd.SetCurrentHp(Creature, restoreHp);
        }
        finally
        {
            _applyingEnrageHp = false;
        }

        RefreshBodyHealthBarUi();
        SyncTremorVisual();
    }

    private void SyncTremorVisual() => Background?.SetCriticalTremor(ShouldTremble);

    private void RefreshBodyHealthBarUi()
    {
        NCreature? node = Sts2VersionSeam.GetCreatureNode(Creature) ?? NCombatRoom.Instance?.GetCreatureNode(Creature);
        if (node == null || !GodotObject.IsInstanceValid(node))
            return;

        NCreatureStateDisplay? stateDisplay = node.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
        NHealthBar? hpBar = stateDisplay?.GetNodeOrNull<NHealthBar>("%HealthBar")
            ?? stateDisplay?.FindChild("HealthBar", recursive: true, owned: false) as NHealthBar
            ?? node.FindChild("HealthBar", recursive: true, owned: false) as NHealthBar;
        if (hpBar != null && GodotObject.IsInstanceValid(hpBar))
            hpBar.RefreshValues();
    }

    public async Task EnsureBulwarkPower()
    {
        if (TryGetCreature() is not { IsAlive: true } creature || IsDefeated || IsWeakPhase || !BothArmsHealthy)
            return;
        if (!creature.HasPower<ThingsCaveGodAncientCorePower>())
        {
            await PowerCmd.Apply<ThingsCaveGodAncientCorePower>(
                new ThrowingPlayerChoiceContext(), creature, 1m, creature, null);
        }
    }

    private void ConfigureBodyNode()
    {
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(Creature);
        if (creatureNode != null && GodotObject.IsInstanceValid(creatureNode))
        {
            creatureNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
            creatureNode.ZAsRelative = false;
            creatureNode.ToggleIsInteractable(!IsDefeated && Creature.IsAlive);
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        Background?.AlignStageAndPlayers();
        SyncTremorVisual();
        ConfigureBodyNode();
        await EnsureBulwarkPower();
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Enemy && Hands.Any(h => h.IsGrabbing && h.NextMove.Intents.Any(i => i is StunIntent)))
            await ReleaseCaptives(stun: false);
        if (side == CombatSide.Player && !IsDefeated)
            await EnsureBulwarkPower();
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Player && !IsDefeated)
        {
            EnsureLeadInitialized();
            if (Phase == 1 && !_isEnrageLocked && Creature.IsAlive && Creature.CurrentHp <= EnrageHpThreshold)
            {
                await EnterEnrageLockAsync();
                return;
            }
            if (_isEnrageLocked && Phase == 1)
            {
                _deferExposure = false;
                string targetEnrageMove = IsWeakPhase ? EnrageRecoverId : EnrageId;
                if (NextMove.Id != targetEnrageMove)
                    SetMoveImmediate(Move(targetEnrageMove), forceTransition: true);
                ProjectHandIntents();
                return;
            }
            if (_deferExposure && IsWeakPhase)
            {
                _deferExposure = false;
                SetMoveImmediate(Move(Exposed1Id), forceTransition: true);
            }
            ProjectHandIntents();
        }
    }

    private MoveState Move(string id) => (MoveState)MoveStateMachine!.States[id];

    public void RefreshHandIntents() => ProjectHandIntents();

    private void ProjectHandIntents()
    {
        EnsureLeadInitialized();
        ThingsCaveGodHand? lead = _rightLeads ? RightHand : LeftHand;
        lead ??= Hands.FirstOrDefault(h => !h.IsDown && h.Creature.IsAlive);
        bool isPhase2Combo = Phase >= 2;
        foreach (ThingsCaveGodHand hand in Hands)
        {
            ThingsCaveGodHand.Action action = ThingsCaveGodHand.Action.Rest;
            if (IsDefeated)
            {
                action = ThingsCaveGodHand.Action.Down;
            }
            else if (NextMove.Id is EnrageId or EnrageRecoverId)
            {
                action = hand.IsDown
                    ? ThingsCaveGodHand.Action.Recover
                    : (IsWeakPhase ? ThingsCaveGodHand.Action.WeakStun : ThingsCaveGodHand.Action.Rest);
            }
            else if (hand.IsDown)
            {
                action = NextMove.Id == RecoverId ? ThingsCaveGodHand.Action.Recover : ThingsCaveGodHand.Action.Down;
            }
            else if (IsWeakPhase)
            {
                action = NextMove.Id == Exposed1Id ? ThingsCaveGodHand.Action.WeakStun : ThingsCaveGodHand.Action.WeakAttack;
            }
            else if (NextMove.Id == GrabId && hand == lead)
            {
                action = ThingsCaveGodHand.Action.Grab;
            }
            else if (NextMove.Id == AirSlamId && hand.IsGrabbing)
            {
                action = ThingsCaveGodHand.Action.Slam;
            }
            else if (NextMove.Id == SweepId && (isPhase2Combo || hand == lead))
            {
                action = ThingsCaveGodHand.Action.Sweep;
            }
            else if (NextMove.Id == RockSweepId && (isPhase2Combo || hand == lead))
            {
                action = ThingsCaveGodHand.Action.CleaveSweep;
            }
            else if (NextMove.Id == HeavyPunchId && (isPhase2Combo || hand == lead))
            {
                action = ThingsCaveGodHand.Action.HeavyPunch;
            }
            hand.Plan(action);
        }
    }

    internal async Task ExposeCore(ThingsCaveGodHand broken)
    {
        if (IsWeakPhase || IsDefeated || !Creature.IsAlive)
        {
            await PowerCmd.Remove<ThingsCaveGodAncientCorePower>(Creature);
            ProjectHandIntents();
            return;
        }

        if (_isEnrageLocked && Phase == 1)
        {
            _resumeMove = CrushId;
            IsWeakPhase = true;
            IsLeftArmBroken = broken.IsLeft;
            _deferExposure = false;
            if (!IsPerformingMove)
                SetMoveImmediate(Move(EnrageRecoverId), forceTransition: true);
            ProjectHandIntents();
            await ReleaseCaptives(stun: false);
            await PowerCmd.Remove<ThingsCaveGodAncientCorePower>(Creature);
            if (Background != null)
                await Background.PlayWeakEnterAnim(IsLeftArmBroken);
            return;
        }

        if (NextMove.Id == AirSlamId)
        {
            _rightLeads = !_rightLeads;
            _resumeMove = Phase == 1 ? SweepId : RockSweepId;
        }
        else if (NextMove.Id is EnrageId or EnrageRecoverId)
        {
            _resumeMove = CrushId;
        }
        else
        {
            _resumeMove = NextMove.Id;
        }

        IsWeakPhase = true;
        IsLeftArmBroken = broken.IsLeft;
        _deferExposure = CombatState?.CurrentSide == CombatSide.Enemy;
        if (!IsPerformingMove)
            SetMoveImmediate(Move(Exposed1Id), forceTransition: true);
        ProjectHandIntents();
        await ReleaseCaptives(stun: false);
        await PowerCmd.Remove<ThingsCaveGodAncientCorePower>(Creature);
        if (Background != null)
            await Background.PlayWeakEnterAnim(IsLeftArmBroken);
    }

    private Task RecoverMove(IReadOnlyList<Creature> _) => AwakenAsync(enrage: ShouldTriggerEnrage);
    private Task EnrageRecoverMove(IReadOnlyList<Creature> _) => AwakenAsync(enrage: true);

    private async Task AwakenAsync(bool enrage)
    {
        if (!IsWeakPhase || IsDefeated || !Creature.IsAlive) return;
        IsWeakPhase = false;
        _deferExposure = false;
        bool transitioningToPhase2 = enrage && Phase == 1;
        if (Background != null)
        {
            await Background.PlayWeakRecoverAnim(IsLeftArmBroken);
            if (transitioningToPhase2)
                await Background.PlayPhaseTransitionAnim();
        }
        if (transitioningToPhase2)
        {
            Phase = 2;
            _resumeMove = CrushId;
            await ExitEnrageLockAsync();
        }
        await EnsureHandsPresentAsync();
        foreach (ThingsCaveGodHand hand in Hands.ToList())
        {
            if (transitioningToPhase2)
                await ThingsCaveGodHand.ClearDebuffs(hand.Creature);
            await hand.Restore();
        }
        await EnsureBulwarkPower();
        SyncTremorVisual();
        await Grow();
    }

    private async Task EnrageMove(IReadOnlyList<Creature> _)
    {
        if (IsDefeated || !Creature.IsAlive) return;
        _deferExposure = false;
        await ReleaseCaptives(stun: false);
        bool wasPhase1 = Phase == 1;
        if (Background != null && wasPhase1)
            await Background.PlayPhaseTransitionAnim();
        Phase = 2;
        _resumeMove = CrushId;
        if (wasPhase1 || _isEnrageLocked)
            await ExitEnrageLockAsync();
        await EnsureHandsPresentAsync();
        foreach (ThingsCaveGodHand hand in Hands.ToList())
        {
            await ThingsCaveGodHand.ClearDebuffs(hand.Creature);
            await hand.Restore();
        }
        await EnsureBulwarkPower();
        SyncTremorVisual();
        await Grow();
    }

    private async Task EnsureHandsPresentAsync()
    {
        if (CombatState == null || IsDefeated) return;
        bool added = false;
        if (LeftHand == null)
        {
            string? validSlot = CombatState.Encounter?.Slots.Contains(ThingsCaveGodBossEncounter.LeftHandSlot) == true
                ? ThingsCaveGodBossEncounter.LeftHandSlot
                : null;
            await CreatureCmd.Add<ThingsCaveGodLeftHand>(CombatState, validSlot);
            added = validSlot != null;
        }
        if (RightHand == null)
        {
            string? validSlot = CombatState.Encounter?.Slots.Contains(ThingsCaveGodBossEncounter.RightHandSlot) == true
                ? ThingsCaveGodBossEncounter.RightHandSlot
                : null;
            await CreatureCmd.Add<ThingsCaveGodRightHand>(CombatState, validSlot);
            added = added || validSlot != null;
        }
        if (added)
            CombatState.SortEnemiesBySlotName();
    }

    internal async Task ReleaseCaptives(bool stun, bool slam = false)
    {
        if (_releasingCaptives) return;
        _releasingCaptives = true;
        try
        {
            foreach (ThingsCaveGodHand hand in Hands)
                if (hand.IsGrabbing) hand.CancelGrab(stun);
            if (CombatState != null)
            {
                foreach (Creature captive in CombatState.Enemies.Where(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive).ToList())
                {
                    ((ThingsCaveGodCaptiveClaw)captive.Monster!).IsSlamResolved = true;
                    await CreatureCmd.Kill(captive, force: true);
                }

                foreach (Creature playerCreature in CombatState.PlayerCreatures.ToList())
                {
                    if (playerCreature.HasPower<ThingsCaveGodPendingTrialPower>())
                        await PowerCmd.Remove<ThingsCaveGodPendingTrialPower>(playerCreature);
                    if (playerCreature.HasPower<ThingsCaveGodBrokenBladePower>())
                        await PowerCmd.Remove<ThingsCaveGodBrokenBladePower>(playerCreature);
                    if (playerCreature.HasPower<ThingsCaveGodShatteredShieldPower>())
                        await PowerCmd.Remove<ThingsCaveGodShatteredShieldPower>(playerCreature);
                }
            }
            if (Background != null && Background.HasCapturedPlayers)
                await Background.DropPlayersToGround(isSlam: slam);
        }
        finally
        {
            _releasingCaptives = false;
        }
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature != Creature || wasRemovalPrevented || IsDefeated) return;
        IsDefeated = true;
        _isEnrageLocked = false;
        Creature.HpDisplay = HpDisplay.Normal;
        Background?.SetCriticalTremor(false);
        foreach (ThingsCaveGodHand hand in Hands) hand.Disable();
        await ReleaseCaptives(stun: false);
        foreach (ThingsCaveGodHand hand in Hands) await hand.ReleaseStolenCards();
        Background?.PlayBodyDeathAnim();
        foreach (ThingsCaveGodHand hand in Hands.ToList())
            if (hand.Creature.IsAlive) await CreatureCmd.Kill(hand.Creature, force: true);
    }

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (_applyingEnrageHp || creature != Creature)
            return;
        SyncTremorVisual();
        if (delta >= 0 || IsDefeated || !Creature.IsAlive)
            return;
        Background?.PlayHurtAnim(forceGroan: false, hitPos: creature.GetCreatureNode()?.VfxSpawnPosition);
        if (Phase == 1 && !_isEnrageLocked && Creature.CurrentHp <= EnrageHpThreshold)
        {
            await EnterEnrageLockAsync();
            return;
        }
        if (IsWeakPhase && NextMove.Id == RecoverId && ShouldTriggerEnrage)
        {
            SetMoveImmediate(Move(EnrageRecoverId), forceTransition: true);
            ProjectHandIntents();
        }
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        Background?.SetCriticalTremor(false);
        Background?.RestoreCapturedPlayersInstantly();
        Background?.ClearAllStolenCards();
        foreach (ThingsCaveGodHand hand in Hands) await hand.ReleaseStolenCards();
    }

    public override void BeforeRemovedFromRoom()
    {
        Background?.SetCriticalTremor(false);
        Background?.RestoreCapturedPlayersInstantly();
        Background?.ClearAllStolenCards();
    }

    private static Task RestMove(IReadOnlyList<Creature> _) => Task.CompletedTask;

    private Task GrabCycleMove(IReadOnlyList<Creature> _)
    {
        if (CanAttack && !HasGrabbingHand)
            _rightLeads = !_rightLeads;
        return Task.CompletedTask;
    }

    private Task AirSlamCycleMove(IReadOnlyList<Creature> _)
    {
        if (CanAttack)
            _rightLeads = !_rightLeads;
        return Task.CompletedTask;
    }

    private Task SweepCycleMove(IReadOnlyList<Creature> _)
    {
        if (CanAttack && Phase == 1)
            _rightLeads = !_rightLeads;
        return Task.CompletedTask;
    }

    private Task RockSweepCycleMove(IReadOnlyList<Creature> _)
    {
        if (CanAttack && Phase == 1)
            _rightLeads = !_rightLeads;
        return Task.CompletedTask;
    }

    private Task HeavyPunchCycleMove(IReadOnlyList<Creature> _)
    {
        if (CanAttack && Phase == 1)
            _rightLeads = !_rightLeads;
        return Task.CompletedTask;
    }

    private async Task JabsMove(IReadOnlyList<Creature> _)
    {
        if (!CanAttack) return;
        bool rightLeadJabs = _rightLeads;
        Background?.StartAttackAnim("alternating_jabs", isFlipped: rightLeadJabs);
        string jabsClip = rightLeadJabs ? "alternating_jabs_right" : "alternating_jabs";
        float previous = 0f;
        int hitIndex = 0;
        foreach (float hit in new[] { CaveGodAnimTiming.JabsHit1, CaveGodAnimTiming.JabsHit2, CaveGodAnimTiming.JabsHit3 })
        {
            float delta = hit - previous;
            bool afterDamage = previous > 0f;
            if (Background is { } background)
                await background.WaitForAttackFrame(jabsClip, hit, delta, subtractPreviousDamageWait: afterDamage);
            else if (afterDamage)
                await Cmd.CustomScaledWait(Math.Max(0f, delta - 0.1f), Math.Max(0f, delta - 0.2f));
            else
                await Cmd.Wait(delta);
            previous = hit;
            if (!CanAttack) return;
            bool isFinisher = hitIndex == 2;
            if (isFinisher)
            {
                NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
                if (NCombatRoom.Instance != null && CombatState != null)
                    VfxCmd.PlayOnCreatureCenters(CombatState.PlayerCreatures.Where(c => c.IsAlive).ToList(), "vfx/vfx_rock_shatter");
            }
            await ExecuteBodyAttack(DamageCmd.Attack(JabDamage).FromMonster(this).WithNoAttackerAnim()
                .SpawningHitVfxOnEachCreature()
                .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: isFinisher ? "heavy_attack.mp3" : "blunt_attack.mp3"));
            hitIndex++;
        }
        if (CanAttack && Phase == 1)
            _rightLeads = !_rightLeads;
        await Cmd.Wait(CaveGodAnimTiming.ImpactSettle);
    }

    private async Task CentralSlam(int damage)
    {
        if (!CanAttack) return;
        Background?.StartAttackAnim("central_slam");
        if (Background is { } background)
            await background.WaitForAttackFrame("central_slam", CaveGodAnimTiming.CentralSlamHit, CaveGodAnimTiming.CentralSlamHit);
        else
            await Cmd.Wait(CaveGodAnimTiming.CentralSlamHit);
        if (!CanAttack) return;
        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
        if (NCombatRoom.Instance != null && CombatState != null)
            VfxCmd.PlayOnCreatureCenters(CombatState.PlayerCreatures.Where(c => c.IsAlive).ToList(), "vfx/vfx_rock_shatter");
        await ExecuteBodyAttack(DamageCmd.Attack(damage).FromMonster(this).WithNoAttackerAnim()
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3"));
        await Cmd.Wait(CaveGodAnimTiming.ImpactSettle);
    }

    private async Task GuardMove(IReadOnlyList<Creature> _)
    {
        if (!CanAttack) return;
        Background?.StartAttackAnim("double_fist_crush");
        if (Background is { } background)
            await background.WaitForAttackFrame("double_fist_crush", CaveGodAnimTiming.DoubleFistCrushHit, CaveGodAnimTiming.DoubleFistCrushHit);
        else
            await Cmd.Wait(CaveGodAnimTiming.DoubleFistCrushHit);
        if (!CanAttack) return;
        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
        await ExecuteBodyAttack(DamageCmd.Attack(GuardDamage).FromMonster(this).WithNoAttackerAnim()
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3"));
        if (!CanAttack) return;
        foreach (ThingsCaveGodHand hand in Hands.Where(h => !h.IsDown && h.Creature.IsAlive))
            await CreatureCmd.GainBlock(hand.Creature, GuardBlock, ValueProp.Move, null);
        await Grow();
        await Cmd.Wait(CaveGodAnimTiming.ImpactSettle);
    }

    private async Task CrushMove(IReadOnlyList<Creature> targets)
    {
        if (!CanAttack) return;
        Background?.StartAttackAnim("double_fist_crush");
        if (Background is { } background)
            await background.WaitForAttackFrame("double_fist_crush", CaveGodAnimTiming.DoubleFistCrushHit, CaveGodAnimTiming.DoubleFistCrushHit);
        else
            await Cmd.Wait(CaveGodAnimTiming.DoubleFistCrushHit);
        for (int i = 0; i < 2; i++)
        {
            if (!CanAttack) return;
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
            if (NCombatRoom.Instance != null && CombatState != null)
                VfxCmd.PlayOnCreatureCenters(CombatState.PlayerCreatures.Where(c => c.IsAlive).ToList(), "vfx/vfx_rock_shatter");
            await ExecuteBodyAttack(DamageCmd.Attack(CrushDamage).FromMonster(this).WithNoAttackerAnim()
                .SpawningHitVfxOnEachCreature()
                .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3"));
            if (i == 0) await Cmd.Wait(0.25f);
        }
        if (CanAttack)
            await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets.Where(c => c.IsAlive), 2m, Creature, null);
        await Cmd.Wait(1.80f);
    }

    private async Task AngrySlamMove(IReadOnlyList<Creature> _)
    {
        await CentralSlam(AngrySlamDamage);
        if (CanAttack)
            await Grow();
    }

    private static Task ExecuteBodyAttack(AttackCommand command) => command.Execute(null);

    private async Task Grow()
    {
        if (IsDefeated || !Creature.IsAlive) return;
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, StrengthGain, Creature, null);
        foreach (ThingsCaveGodHand hand in Hands.Where(h => h.Creature.IsAlive))
            await hand.GainStrength(StrengthGain, Creature);
    }
}
