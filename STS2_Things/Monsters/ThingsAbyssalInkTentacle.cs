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
/// 墨腺触腕 (Ink-Gland Tentacle) — 渊墨巨蛸桥右可击杀主触腕。
/// 交替施放【墨囊增压】与【墨鞭连抽】（2 段鞭击 + 1 层虚弱）；在 Boss 蓄力喷墨回合将原状态机延后 1 回合、
/// 插入【墨涌蓄力】为本体施加 1 回合临时力量（原版 TemporaryStrength 语义，喷墨结束后自动回退），
/// 并于喷墨后次合与本体同步眩晕 1 回合；斩断时触发 20 反伤 + 2 易伤并立即洗净全牌库染黑卡牌。
/// </summary>
public sealed class ThingsAbyssalInkTentacle : ThingsMonsterModel, IAbyssalSyncedTentacle
{
    public const string WhipLashId = "WHIP_LASH";
    public const string InkBolsterId = "INK_BOLSTER";
    public const string ChargeBuffId = "CHARGE_BUFF";
    public const string PostSprayStunId = "POST_SPRAY_STUN";
    public const string MoveRouterId = "MOVE_ROUTER";

    private bool _floorBreachMode;
    private bool _playEmergeOnAdd;
    private bool _deathHandled;
    private int _normalCycleIndex;
    private AbyssalTentacleTurnOverride _pendingOverride;

    public bool IsFloorBreachMode => _floorBreachMode;

    public int WhipLashDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public const int WhipLashHits = 2;
    public int BolsterStrength => 1 + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int BolsterBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 10, 7) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int ChargeBossTempStrength => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3) + (Sts2VersionSeam.IsDread() ? 1 : 0);

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 54, 48) + (Sts2VersionSeam.IsDread() ? 10 : 0);
    public override int MaxInitialHp => MinInitialHp;

    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_ink_tentacle.tscn";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Slime;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/workbug_goop/workbug_goop_die";
    public override bool CanChangeScale => false;
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 1.15f;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<MinionPower>().ResolvedBigIconPath,
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath,
        ModelDb.Power<WeakPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsInkBlackoutPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempStrengthPower>().ResolvedBigIconPath
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
            string normalId = _normalCycleIndex % 2 == 0 ? InkBolsterId : WhipLashId;
            SetMoveImmediate(Move(normalId), forceTransition: true);
        }
    }

    public void SetStunnedVisual(bool stunned)
    {
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: false, stunned);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(
            isLeft: false,
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
            isLeft: false,
            alive: true,
            floorBreach: _floorBreachMode,
            playEmerge: false);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var inkBolster = new MoveState(
            InkBolsterId,
            InkBolsterMove,
            new BuffIntent(),
            new DefendIntent());

        var whipLash = new MoveState(
            WhipLashId,
            WhipLashMove,
            new MultiAttackIntent(WhipLashDamage, WhipLashHits),
            new DebuffIntent());

        var chargeBuff = new MoveState(
            ChargeBuffId,
            ChargeBuffMove,
            new BuffIntent());

        var postSprayStun = new MoveState(
            PostSprayStunId,
            PostSprayStunMove,
            new StunIntent());

        var router = new ConditionalBranchState(MoveRouterId);
        router.AddState(postSprayStun, () => _pendingOverride == AbyssalTentacleTurnOverride.PostSprayStun);
        router.AddState(chargeBuff, () => _pendingOverride == AbyssalTentacleTurnOverride.ChargeBuff);
        router.AddState(inkBolster, () => _normalCycleIndex % 2 == 0);
        router.AddState(whipLash, () => true);

        inkBolster.FollowUpState = router;
        whipLash.FollowUpState = router;
        chargeBuff.FollowUpState = router;
        postSprayStun.FollowUpState = router;

        return new MonsterMoveStateMachine([inkBolster, whipLash, chargeBuff, postSprayStun, router], router);
    }

    private MoveState Move(string stateId) => (MoveState)MoveStateMachine!.States[stateId];

    private async Task WhipLashMove(IReadOnlyList<Creature> targets)
    {
        _normalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleAction(isLeft: false, "whip_lash", triggerBaseBurst: true);

        // Distinct 2-hit over-bridge whip-slam cadence matching Spine `hit_1` (`0.46s`) and `hit_2` (`0.92s`, +0.46s delta)!
        await WaitForCombatBeatAsync(0.46f);
        await DamageCmd.Attack(WhipLashDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await WaitForCombatBeatAsync(0.46f);
        await DamageCmd.Attack(WhipLashDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        foreach (Creature target in targets)
        {
            if (target.IsAlive)
            {
                await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), target, 1m, Creature, null);
            }
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task InkBolsterMove(IReadOnlyList<Creature> targets)
    {
        _normalCycleIndex++;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleAction(isLeft: false, "ink_bolster", triggerBaseBurst: false);
        await WaitForCombatBeatAsync(0.62f);

        await CreatureCmd.GainBlock(Creature, BolsterBlock, ValueProp.Move, null);
        if (CombatState != null)
        {
            foreach (Creature enemy in CombatState.Enemies.Where(e => e.IsAlive).ToList())
            {
                await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), enemy, BolsterStrength, Creature, null);
            }
        }

        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task ChargeBuffMove(IReadOnlyList<Creature> targets)
    {
        _pendingOverride = AbyssalTentacleTurnOverride.None;
        NAbyssalInkmawBossBackground.Current?.PlayTentacleChargeBuff(isLeft: false);
        await WaitForCombatBeatAsync(0.58f);

        if (FindBoss() is { } boss && boss.Creature.IsAlive)
        {
            await PowerCmd.Apply<ThingsAbyssalTempStrengthPower>(
                new ThrowingPlayerChoiceContext(),
                boss.Creature,
                ChargeBossTempStrength,
                Creature,
                null);
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
            NAbyssalInkmawBossBackground.Current?.PlayTentacleHurt(isLeft: false);
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
            await boss.HandleTentacleSeveredAsync(isLeft: false, Creature, choiceContext);
        }
        else
        {
            NAbyssalInkmawBossBackground.Current?.PlayTentacleRetreatDie(isLeft: false);
        }
    }

    private ThingsAbyssalInkmaw? FindBoss() =>
        CombatState?.Enemies.Select(e => e.Monster).OfType<ThingsAbyssalInkmaw>().FirstOrDefault();
}
