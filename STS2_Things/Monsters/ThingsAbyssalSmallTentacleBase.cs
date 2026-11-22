using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Combat;
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
using STS2_Things.Core.Models;
using STS2_Things.Encounters;
using STS2_Things.Platform;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// Shared base model for 渊墨巨蛸's three outer bridge-breaching Small Tentacle variants
/// (尖刺幼触 Attack, 甲壳幼触 Guard-Boss, 荧光幼触 Buff-Boss).
/// Implements lossless 1-turn state-machine postponement during Boss's 【渊墨蓄力】 turn
/// (inserting each variant's unique temporary Boss buff) and synchronized 1-turn Stun on the
/// post-spray turn.
/// </summary>
public abstract class ThingsAbyssalSmallTentacleBase : ThingsMonsterModel, IAbyssalSyncedTentacle
{
    public const string ChargeBuffId = "CHARGE_BUFF";
    public const string PostSprayStunId = "POST_SPRAY_STUN";
    public const string MoveRouterId = "MOVE_ROUTER";

    private bool _isOuterLeft;
    private bool _slotConfigured;
    private bool _playEmergeOnAdd;
    private bool _deathHandled;
    protected int NormalCycleIndex;
    private AbyssalTentacleTurnOverride _pendingOverride;

    public abstract string VariantSkinName { get; }
    protected abstract string FirstNormalMoveId { get; }
    protected abstract string SecondNormalMoveId { get; }

    public bool IsOuterLeft => _slotConfigured
        ? _isOuterLeft
        : string.Equals(Creature?.SlotName, ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot, StringComparison.Ordinal);

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 25, 22) + (Sts2VersionSeam.IsDread() ? 5 : 0);
    public override int MaxInitialHp => MinInitialHp;

    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Slime;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/workbug_goop/workbug_goop_die";
    public override bool CanChangeScale => false;
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 0.95f;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<MinionPower>().ResolvedBigIconPath
    ]);

    public void ConfigureSpawn(bool isOuterLeft, bool playEmergeOnAdd)
    {
        AssertMutable();
        _isOuterLeft = isOuterLeft;
        _slotConfigured = true;
        _playEmergeOnAdd = playEmergeOnAdd;
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _isOuterLeft = false;
        _slotConfigured = false;
        _playEmergeOnAdd = false;
        _deathHandled = false;
        NormalCycleIndex = 0;
        _pendingOverride = AbyssalTentacleTurnOverride.None;
    }

    public void ArmTurnOverride(AbyssalTentacleTurnOverride turnOverride)
    {
        if (!Creature.IsAlive || _deathHandled)
            return;
        _pendingOverride = turnOverride;
    }

    public void SyncArmedOverrideImmediate()
    {
        if (!Creature.IsAlive || _deathHandled || MoveStateMachine == null || IsPerformingMove)
            return;

        string? desiredId = _pendingOverride switch
        {
            AbyssalTentacleTurnOverride.PostSprayStun => PostSprayStunId,
            AbyssalTentacleTurnOverride.ChargeBuff => ChargeBuffId,
            _ => null,
        };

        if (desiredId != null && NextMove?.Id != desiredId)
        {
            SetMoveImmediate((MoveState)MoveStateMachine.States[desiredId], forceTransition: true);
        }
    }

    public void CancelChargeBuffImmediate()
    {
        if (_pendingOverride == AbyssalTentacleTurnOverride.ChargeBuff)
            _pendingOverride = AbyssalTentacleTurnOverride.None;

        if (!Creature.IsAlive || _deathHandled || MoveStateMachine == null || IsPerformingMove)
            return;

        if (NextMove?.Id == ChargeBuffId)
        {
            string normalId = NormalCycleIndex % 2 == 0 ? FirstNormalMoveId : SecondNormalMoveId;
            SetMoveImmediate((MoveState)MoveStateMachine.States[normalId], forceTransition: true);
        }
    }

    public void SetStunnedVisual(bool stunned)
    {
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(IsOuterLeft, stunned);
    }

    protected MonsterMoveStateMachine BuildSyncedStateMachine(
        MoveState firstNormalMove,
        MoveState secondNormalMove,
        params AbstractIntent[] chargeBuffIntents)
    {
        var chargeBuff = new MoveState(
            ChargeBuffId,
            ChargeBuffMoveWrapper,
            chargeBuffIntents.Length > 0 ? chargeBuffIntents : [new BuffIntent()]);

        var postSprayStun = new MoveState(
            PostSprayStunId,
            PostSprayStunMove,
            new StunIntent());

        var router = new ConditionalBranchState(MoveRouterId);
        router.AddState(postSprayStun, () => _pendingOverride == AbyssalTentacleTurnOverride.PostSprayStun);
        router.AddState(chargeBuff, () => _pendingOverride == AbyssalTentacleTurnOverride.ChargeBuff);
        router.AddState(firstNormalMove, () => NormalCycleIndex % 2 == 0);
        router.AddState(secondNormalMove, () => true);

        firstNormalMove.FollowUpState = router;
        secondNormalMove.FollowUpState = router;
        chargeBuff.FollowUpState = router;
        postSprayStun.FollowUpState = router;

        return new MonsterMoveStateMachine([firstNormalMove, secondNormalMove, chargeBuff, postSprayStun, router], router);
    }

    private async Task ChargeBuffMoveWrapper(IReadOnlyList<Creature> targets)
    {
        // Deliberately does NOT advance NormalCycleIndex so normal moves postpone cleanly by 1 turn!
        _pendingOverride = AbyssalTentacleTurnOverride.None;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleAction(IsOuterLeft, "buff_pulse");
        await WaitForCombatBeatAsync(0.58f);

        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await ApplyChargeBuffToBossAsync(boss);
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    protected abstract Task ApplyChargeBuffToBossAsync(ThingsAbyssalInkmaw boss);

    private async Task PostSprayStunMove(IReadOnlyList<Creature> targets)
    {
        _pendingOverride = AbyssalTentacleTurnOverride.None;
        ThingsAbyssalInkmaw.SpawnNativeStunnedVfx(Creature);
        await WaitForCombatBeatAsync(0.35f);
        SetStunnedVisual(false);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(
            IsOuterLeft,
            alive: true,
            variantSkin: VariantSkinName,
            playEmerge: _playEmergeOnAdd);
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        if (Creature.GetPower<MinionPower>() == null)
        {
            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        }
        NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(
            IsOuterLeft,
            alive: true,
            variantSkin: VariantSkinName,
            playEmerge: false);
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Creature && Creature.IsAlive && result.TotalDamage > 0)
            NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleHurt(IsOuterLeft);
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
        if (FindBoss() is { } boss)
        {
            await boss.HandleSmallTentacleSeveredAsync(IsOuterLeft, Creature, choiceContext);
        }
        else
        {
            NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleRetreatDie(IsOuterLeft);
        }
    }

    protected ThingsAbyssalInkmaw? FindBoss() =>
        CombatState?.Enemies.Select(e => e.Monster).OfType<ThingsAbyssalInkmaw>().FirstOrDefault();
}
