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
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 重砸巨腕 (Crushing Tentacle) — 渊墨巨蛸桥左可击杀主触腕。
/// 交替施放【崩桥重砸】与【盘踞护主】；在 Boss 蓄力喷墨回合自动将原状态机延后 1 回合、
/// 插入【潮壁蓄护】为本体提供临时格挡与 1 层原版残影（Blur），并于喷墨后次合与本体同步眩晕 1 回合。
/// 斩断时触发 20 反伤 + 2 易伤并立即洗净全牌库染黑卡牌。
/// </summary>
public sealed class ThingsAbyssalCrushingTentacle : ThingsMonsterModel, IAbyssalSyncedTentacle
{
    public const string HeavySlamId = "HEAVY_SLAM";
    public const string GuardCoilId = "GUARD_COIL";
    public const string ChargeBuffId = "CHARGE_BUFF";
    public const string PostSprayStunId = "POST_SPRAY_STUN";
    public const string MoveRouterId = "MOVE_ROUTER";

    private bool _floorBreachMode;
    private bool _playEmergeOnAdd;
    private bool _deathHandled;
    private int _normalCycleIndex;
    private AbyssalTentacleTurnOverride _pendingOverride;

    public bool IsFloorBreachMode => _floorBreachMode;

    public int HeavySlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 12, 10) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int GuardBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 12, 9) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int ChargeBossBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 18, 15) + (Sts2VersionSeam.IsDread() ? 3 : 0);

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 58, 52) + (Sts2VersionSeam.IsDread() ? 9 : 0);
    public override int MaxInitialHp => MinInitialHp;

    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_crushing_tentacle.tscn";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Slime;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/workbug_goop/workbug_goop_die";
    public override bool CanChangeScale => false;
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 1.15f;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<MinionPower>().ResolvedBigIconPath,
        ModelDb.Power<BlurPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsInkBlackoutPower>().ResolvedBigIconPath
    ]);

    internal void ConfigureSpawn(bool floorBreach, bool playEmergeOnAdd)
    {
        AssertMutable();
        _floorBreachMode = floorBreach;
        _playEmergeOnAdd = playEmergeOnAdd;
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _floorBreachMode = false;
        _playEmergeOnAdd = false;
        _deathHandled = false;
        _normalCycleIndex = 0;
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
            SetMoveImmediate(Move(desiredId), forceTransition: true);
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
            string normalId = _normalCycleIndex % 2 == 0 ? HeavySlamId : GuardCoilId;
            SetMoveImmediate(Move(normalId), forceTransition: true);
        }
    }

    public void SetStunnedVisual(bool stunned)
    {
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: true, stunned);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(
            isLeft: true,
            alive: true,
            floorBreach: _floorBreachMode,
            playEmerge: _playEmergeOnAdd);
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        if (Creature.GetPower<MinionPower>() == null)
        {
            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        }
        NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(
            isLeft: true,
            alive: true,
            floorBreach: _floorBreachMode,
            playEmerge: false);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var heavySlam = new MoveState(
            HeavySlamId,
            HeavySlamMove,
            new SingleAttackIntent(() => HeavySlamDamage));

        var guardCoil = new MoveState(
            GuardCoilId,
            GuardCoilMove,
            new DefendIntent());

        var chargeBuff = new MoveState(
            ChargeBuffId,
            ChargeBuffMove,
            new DefendIntent(),
            new BuffIntent());

        var postSprayStun = new MoveState(
            PostSprayStunId,
            PostSprayStunMove,
            new StunIntent());

        var router = new ConditionalBranchState(MoveRouterId);
        router.AddState(postSprayStun, () => _pendingOverride == AbyssalTentacleTurnOverride.PostSprayStun);
        router.AddState(chargeBuff, () => _pendingOverride == AbyssalTentacleTurnOverride.ChargeBuff);
        router.AddState(heavySlam, () => _normalCycleIndex % 2 == 0);
        router.AddState(guardCoil, () => true);

        heavySlam.FollowUpState = router;
        guardCoil.FollowUpState = router;
        chargeBuff.FollowUpState = router;
        postSprayStun.FollowUpState = router;

        return new MonsterMoveStateMachine([heavySlam, guardCoil, chargeBuff, postSprayStun, router], router);
    }

    private MoveState Move(string stateId) => (MoveState)MoveStateMachine!.States[stateId];

    private async Task HeavySlamMove(IReadOnlyList<Creature> targets)
    {
        _normalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleAction(isLeft: true, "heavy_slam", triggerBaseBurst: true);
        // Exact `attack_hit` Spine event beat (`t = 0.68s`) as the uncoiling barnacle hammer crashes across center bridge!
        await WaitForCombatBeatAsync(0.68f);

        await DamageCmd.Attack(HeavySlamDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_heavy_blunt")
            .Execute(null);

        await WaitForCombatBeatAsync(0.45f);
    }

    private async Task GuardCoilMove(IReadOnlyList<Creature> targets)
    {
        _normalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleAction(isLeft: true, "guard_coil", triggerBaseBurst: false);
        await WaitForCombatBeatAsync(0.52f);

        await CreatureCmd.GainBlock(Creature, GuardBlock, ValueProp.Move, null);
        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await CreatureCmd.GainBlock(boss.Creature, GuardBlock, ValueProp.Move, null);
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task ChargeBuffMove(IReadOnlyList<Creature> targets)
    {
        // Deliberately preserves `_normalCycleIndex` so the normal state machine shifts cleanly by 1 turn!
        _pendingOverride = AbyssalTentacleTurnOverride.None;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleChargeBuff(isLeft: true);
        await WaitForCombatBeatAsync(0.58f);

        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await CreatureCmd.GainBlock(boss.Creature, ChargeBossBlock, ValueProp.Move, null);
            await PowerCmd.Apply<BlurPower>(new ThrowingPlayerChoiceContext(), boss.Creature, 1m, Creature, null);
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task PostSprayStunMove(IReadOnlyList<Creature> targets)
    {
        _pendingOverride = AbyssalTentacleTurnOverride.None;
        ThingsAbyssalInkmaw.SpawnNativeStunnedVfx(Creature);
        await WaitForCombatBeatAsync(0.38f);
        SetStunnedVisual(false);
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
            NAbyssalInkmawBossBackground.Current?.PlayTentacleHurt(isLeft: true);
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
            await boss.HandleTentacleSeveredAsync(isLeft: true, Creature, choiceContext);
        }
        else
        {
            NAbyssalInkmawBossBackground.Current?.PlayTentacleRetreatDie(isLeft: true);
        }
    }

    private ThingsAbyssalInkmaw? FindBoss() =>
        CombatState?.Enemies.Select(e => e.Monster).OfType<ThingsAbyssalInkmaw>().FirstOrDefault();
}
