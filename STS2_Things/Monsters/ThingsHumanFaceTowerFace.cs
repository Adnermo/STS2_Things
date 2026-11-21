using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Intents;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// Shared base class for the 4 stone relief faces of 人面塔 (The Visage Tower):
/// 嗔怒面 (<see cref="ThingsHumanFaceTowerWrath"/>),
/// 悲叹面 (<see cref="ThingsHumanFaceTowerLament"/>),
/// 缄默面 (<see cref="ThingsHumanFaceTowerBastion"/>),
/// and 诡笑面 (<see cref="ThingsHumanFaceTowerMockery"/>).
/// </summary>
public abstract class ThingsHumanFaceTowerFace : ThingsMonsterModel
{
    public const string MoveRouterId = "MOVE_ROUTER";

    private bool _deathHandled;

    public abstract int FaceSlotIndex { get; }
    public abstract string IdleAnimationName { get; }
    public abstract string StarterAttackAnimName { get; }
    public abstract int ComboStarterDamage { get; }
    public abstract int ComboFollowUpDamage { get; }
    public virtual int ComboFollowUpHits => 1;

    public HumanFaceTowerController? Controller { get; private set; }
    internal bool AllowInternalPowerApplication { get; set; }
    internal bool IsRegisteredInCombat => IsMutable && MoveStateMachine != null;

    public bool IsActiveFace => Controller == null || Controller.ActiveFace == this;

    public int ClimaxDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 17, 15);

    public string StoneAttackSfx => "event:/sfx/enemy/enemy_attacks/workbug_rock/workbug_rock_attack";
    public string StoneRoarSfx => "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/workbug_rock/workbug_rock_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Stone;
    public override bool CanChangeScale => false;
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;

    protected override string AssetSlug => "things_human_face_tower_face";

    protected override bool IsMonsterInteractable => IsActiveFace && (!IsRegisteredInCombat || Creature.IsAlive);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsTowerPivotPower>().ResolvedBigIconPath,
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath
    ]);

    protected override void AfterCloned()
    {
        base.AfterCloned();
        Controller = null;
        AllowInternalPowerApplication = false;
        _deathHandled = false;
    }

    internal void AttachController(HumanFaceTowerController controller)
    {
        AssertMutable();
        Controller = controller;
    }

    public ThingsHumanFaceTowerFace? PeekComboFollowUpFace() => Controller?.PeekNextLivingFace(this);

    public override bool ShouldAllowTargeting(Creature target)
        => target != Creature || (IsActiveFace && Creature.IsAlive);

    public override bool ShouldAllowHitting(Creature creature)
        => creature != Creature || AllowInternalPowerApplication || (IsActiveFace && Creature.IsAlive);

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target == Creature && !IsActiveFace ? 0m : 1m;

    protected override decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target == Creature && !IsActiveFace ? 0m : decimal.MaxValue;

    public override decimal ModifyHpLostAfterOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => target == Creature && !IsActiveFace ? 0m : amount;

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        Controller ??= HumanFaceTowerController.ForStandalone(this);
        await Controller.EnsureFaceAddedAsync(this);
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        if (Controller != null && IsActiveFace)
        {
            await Controller.EnsureCombatStartSyncedAsync();
        }
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Creature && Creature.IsAlive && IsActiveFace && result.TotalDamage > 0)
        {
            NHumanFaceTowerBossBackground.Current?.PlayFaceHurt(FaceSlotIndex, IdleAnimationName);
        }

        return Task.CompletedTask;
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        await BaseAfterDeathAsync(choiceContext, creature, source, wasRemovalPrevented, deathAnimLength);
        if (creature != Creature || wasRemovalPrevented || creature.IsAlive || _deathHandled)
            return;

        _deathHandled = true;
        if (Controller != null)
        {
            await Controller.HandleFaceDeathAsync(this, choiceContext);
        }
    }

    internal abstract Task ApplyUniquePassivePowerAsync();

    internal virtual Task OnEnteredCenterAsync(bool isInitialCombatStart) => Task.CompletedTask;

    internal virtual void OnRotatedAway()
    {
    }

    internal abstract Task ExecuteComboFollowUpAsync(IReadOnlyList<Creature> targets);

    internal async Task RecoverFromStunAsync(IReadOnlyList<Creature> _)
    {
        Controller?.NotifyStunnedTurnPerformed(this);
        NHumanFaceTowerBossBackground.Current?.SetFaceIdle(IdleAnimationName, 0.18f);
        await WaitForCombatBeatAsync(0.25f);
    }

    protected MonsterMoveStateMachine BuildTowerStateMachine(
        MoveState turn1Move,
        MoveState turn2StandardMove,
        string hiddenMoveId,
        string emergencyStunMoveId,
        string cycleClimaxMoveId,
        string comboRotateMoveId)
    {
        var hiddenMove = new MoveState(hiddenMoveId, _ => Task.CompletedTask);
        var emergencyStunMove = new MoveState(emergencyStunMoveId, RecoverFromStunAsync, new StunIntent());
        var climaxMove = new MoveState(
            cycleClimaxMoveId,
            ExecuteClimaxMoveAsync,
            new SingleAttackIntent(() => ClimaxDamage),
            new BuffIntent());
        var comboRotateMove = new MoveState(
            comboRotateMoveId,
            ExecuteComboStarterAndRotateAsync,
            new SingleAttackIntent(() => ComboStarterDamage),
            new NextFaceComboAttackIntent(this));

        var router = new ConditionalBranchState(MoveRouterId);
        router.AddState(hiddenMove, () => !IsActiveFace);
        router.AddState(emergencyStunMove, () => Controller?.ConsumeEmergencyStunForMoveRoll(this) == true);
        router.AddState(turn1Move, () => (Controller?.StayTurnIndex ?? 0) == 0);
        router.AddState(climaxMove, () => Controller?.IsUpcomingFullCycleClimaxTurn == true);
        router.AddState(comboRotateMove, () => Controller?.ShouldUseComboRotateOnTurn2 == true);
        router.AddState(turn2StandardMove, () => true);

        MoveState[] allMoves =
        [
            hiddenMove,
            emergencyStunMove,
            turn1Move,
            turn2StandardMove,
            climaxMove,
            comboRotateMove
        ];
        foreach (MoveState move in allMoves)
        {
            move.FollowUpState = router;
        }

        return new MonsterMoveStateMachine([..allMoves, router], router);
    }

    protected async Task ExecuteStoneSingleAttackAsync(
        string animName,
        float hitDelaySeconds,
        int damage,
        bool strongShake = false)
    {
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        float mix = animName == "bastion_rebolt_attack" ? 0f : 0.08f;
        NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot(animName, IdleAnimationName, mix);
        SfxCmd.Play(StoneAttackSfx);
        await WaitForCombatBeatAsync(hitDelaySeconds);

        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        NGame.Instance?.ScreenShake(
            strongShake ? ShakeStrength.Strong : ShakeStrength.Medium,
            strongShake ? ShakeDuration.Normal : ShakeDuration.Short);

        await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithNoAttackerAnim()
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);
    }

    protected async Task ExecuteStoneMultiAttackAsync(
        string animName,
        float initialDelaySeconds,
        int damagePerHit,
        int hitCount)
    {
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        float mix = animName == "bastion_rebolt_attack" ? 0f : 0.08f;
        NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot(animName, IdleAnimationName, mix);
        SfxCmd.Play(StoneAttackSfx);
        await WaitForCombatBeatAsync(initialDelaySeconds);

        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
        await DamageCmd.Attack(damagePerHit)
            .WithHitCount(hitCount)
            .FromMonster(this)
            .WithNoAttackerAnim()
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);
    }

    private async Task ExecuteClimaxMoveAsync(IReadOnlyList<Creature> targets)
    {
        if (Controller != null)
        {
            await Controller.ExecuteFullCycleClimaxAsync(this, targets, ClimaxDamage);
        }
        else
        {
            await ExecuteStoneSingleAttackAsync($"climax_cycle_roar_{FaceSlotIndex}", 0.52f, ClimaxDamage, strongShake: true);
        }
    }

    private async Task ExecuteComboStarterAndRotateAsync(IReadOnlyList<Creature> targets)
    {
        await ExecuteStoneSingleAttackAsync(StarterAttackAnimName, 0.40f, ComboStarterDamage);
        if (Controller != null && !CombatManager.Instance.IsOverOrEnding)
        {
            await Controller.ExecuteComboRotationAndFollowUpAsync(this, targets);
        }
    }
}
