using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Combat;
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
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

public enum AbyssalTentacleTurnOverride
{
    None = 0,
    ChargeBuff = 1,
    PostSprayStun = 2,
}

internal interface IAbyssalSyncedTentacle
{
    void ArmTurnOverride(AbyssalTentacleTurnOverride turnOverride);
    void SyncArmedOverrideImmediate();
    void CancelChargeBuffImmediate();
    void SetStunnedVisual(bool stunned);
}

/// <summary>
/// 渊墨巨蛸 (Abyssal Inkmaw) — Act 2「深处」巨物型水域古桥 Boss 本体。
/// 矗立于古桥后方宽阔地下湖中央；常规以【暗潮压境】与【深渊召腕】起势，随后进入可重复施放的三回合招牌大招阶段：
///   1. 【渊墨蓄力】(<c>INK_CHARGE</c>)：显示蓝箭头增益 + 黄色问号意图但实际空过蓄力，
///      同步令全体存活触手将原状态机无损延后 1 回合、插入各自独特的对本体 1 回合临时增益；
///   2. 【高压渊墨扫射】(<c>INK_SPRAY</c>)：携全队临时增益横扫全屏喷墨，
///      令玩家下一回合整个牌库（含洗牌重抽）全部染黑为统一深渊定制卡框并 0–3 随机化费用；
///   3. 【力竭眩晕】(<c>POST_SPRAY_STUN</c>)：喷墨后的下一回合，本体与全体存活触手同步眩晕 1 回合，
///      随后无损续接各自状态机并重启大循环。
/// </summary>
public sealed class ThingsAbyssalInkmaw : ThingsMonsterModel
{
    public const string AbyssalUndertowId = "ABYSSAL_UNDERTOW";
    public const string SummonTentacleId = "SUMMON_TENTACLE";
    public const string TidalCrashId = "TIDAL_CRASH";
    public const string AbyssalPressureId = "ABYSSAL_PRESSURE";
    public const string InkChargeId = "INK_CHARGE";
    public const string InkSprayId = "INK_SPRAY";
    public const string PostSprayStunId = "POST_SPRAY_STUN";
    public const string Phase2SurgeId = "PHASE2_SURGE";
    public const string MoveRouterId = "MOVE_ROUTER";

    private enum InkSequenceStage
    {
        None = 0,
        SprayNext = 1,
        StunNext = 2,
    }

    public int Phase { get; private set; } = 1;
    private bool _phase2Pending;
    private bool _deathHandled;
    private int _cycleStep;
    private InkSequenceStage _inkSequenceStage;

    public int Phase2HpThreshold => (Creature.MaxHp + 1) / 2;
    public bool IsChargingInk => NextMove?.Id == InkChargeId;
    public bool IsPostSprayStunned => NextMove?.Id == PostSprayStunId;

    public int UndertowDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public const int UndertowHits = 2;
    public int UndertowBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 10, 8) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    public int PressureBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 16, 12) + (Sts2VersionSeam.IsDread() ? 3 : 0);
    public int PressureStrength => 1 + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public const int PressureSlimedCount = 1;

    public int InkSprayDamage => Phase >= 2
        ? AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12) + (Sts2VersionSeam.IsDread() ? 2 : 0)
        : AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9) + (Sts2VersionSeam.IsDread() ? 1 : 0);

    public int InkBlackoutTurns => 1;

    public int TidalCrashDamage => Phase >= 2
        ? AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 22, 19) + (Sts2VersionSeam.IsDread() ? 2 : 0)
        : AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 18, 15) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    public int TidalDebuffStacks => Phase >= 2 ? 2 : 1;

    public int SummonBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 16, 12) + (Sts2VersionSeam.IsDread() ? 4 : 0);
    public int Phase2Strength => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 2, 1) + (Sts2VersionSeam.IsDread() ? 1 : 0);
    public int Phase2Block => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 24, 18) + (Sts2VersionSeam.IsDread() ? 6 : 0);

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 350, 320) + (Sts2VersionSeam.IsDread() ? 30 : 0);
    public override int MaxInitialHp => MinInitialHp;

    protected override string VisualsPath => "res://scenes/creature_visuals/things_abyssal_inkmaw.tscn";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Slime;
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_die";
    public override bool CanChangeScale => false;
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 2.25f;

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsAbyssalCoverPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsInkBlackoutPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalSurgePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempStrengthPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempThornsPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempWardPower>().ResolvedBigIconPath,
        ModelDb.Power<BlurPower>().ResolvedBigIconPath,
        ModelDb.Power<VigorPower>().ResolvedBigIconPath,
        ModelDb.Power<ThornsPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath,
        ModelDb.Power<WeakPower>().ResolvedBigIconPath,
        ModelDb.Power<FrailPower>().ResolvedBigIconPath,
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath,
        ..ModelDb.Card<Slimed>().AllPortraitPaths,
        ..ModelDb.Monster<ThingsAbyssalCrushingTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalInkTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalSpikeTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalShellTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalGlandTentacle>().AssetPaths
    ]);

    public ThingsAbyssalCrushingTentacle? LivingLeftTentacle =>
        CombatState?.Enemies.Where(c => c.IsAlive).Select(c => c.Monster).OfType<ThingsAbyssalCrushingTentacle>().FirstOrDefault();

    public ThingsAbyssalInkTentacle? LivingRightTentacle =>
        CombatState?.Enemies.Where(c => c.IsAlive).Select(c => c.Monster).OfType<ThingsAbyssalInkTentacle>().FirstOrDefault();

    public int LivingTentacleCount =>
        (LivingLeftTentacle != null ? 1 : 0) + (LivingRightTentacle != null ? 1 : 0);

    public IReadOnlyList<ThingsAbyssalSmallTentacleBase> LivingSmallTentacles =>
        CombatState?.Enemies.Where(c => c.IsAlive).Select(c => c.Monster).OfType<ThingsAbyssalSmallTentacleBase>().ToList()
        ?? [];

    public int LivingSmallTentacleCount => LivingSmallTentacles.Count;

    public ThingsAbyssalSmallTentacleBase? OuterLeftSmallTentacle =>
        LivingSmallTentacles.FirstOrDefault(t => t.IsOuterLeft);

    public ThingsAbyssalSmallTentacleBase? OuterRightSmallTentacle =>
        LivingSmallTentacles.FirstOrDefault(t => !t.IsOuterLeft);

    private IEnumerable<IAbyssalSyncedTentacle> EnumerateLivingSyncedTentacles() =>
        CombatState?.Enemies
            .Where(c => c.IsAlive && c != Creature)
            .Select(c => c.Monster)
            .OfType<IAbyssalSyncedTentacle>()
            .ToList()
        ?? [];

    protected override void AfterCloned()
    {
        base.AfterCloned();
        Phase = 1;
        _phase2Pending = false;
        _deathHandled = false;
        _cycleStep = 0;
        _inkSequenceStage = InkSequenceStage.None;
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        await SyncCoverPowerAsync(new ThrowingPlayerChoiceContext());
    }

    private bool ShouldTriggerPhase2Now() =>
        Phase == 1 && (_phase2Pending || Creature.CurrentHp <= Phase2HpThreshold);

    private bool WillRollInkChargeOnUpcomingTurn()
    {
        if (!Creature.IsAlive || _deathHandled)
            return false;
        if (ShouldTriggerPhase2Now())
            return false;
        if (_inkSequenceStage != InkSequenceStage.None)
            return false;
        return _cycleStep >= 4;
    }

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player || !Creature.IsAlive || _deathHandled)
            return Task.CompletedTask;

        // Coordinate ALL living tentacles BEFORE PrepareForNextTurn runs RollMove across slots!
        if (_inkSequenceStage == InkSequenceStage.StunNext)
        {
            foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
            {
                tentacle.ArmTurnOverride(AbyssalTentacleTurnOverride.PostSprayStun);
            }
        }
        else if (WillRollInkChargeOnUpcomingTurn())
        {
            foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
            {
                tentacle.ArmTurnOverride(AbyssalTentacleTurnOverride.ChargeBuff);
            }
        }

        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player || !Creature.IsAlive || _deathHandled)
            return Task.CompletedTask;

        string? nextId = NextMove?.Id;
        if (nextId == InkChargeId)
        {
            foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
            {
                tentacle.ArmTurnOverride(AbyssalTentacleTurnOverride.ChargeBuff);
                tentacle.SyncArmedOverrideImmediate();
                tentacle.SetStunnedVisual(false);
            }
            NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Charging);
        }
        else if (nextId == PostSprayStunId)
        {
            foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
            {
                tentacle.ArmTurnOverride(AbyssalTentacleTurnOverride.PostSprayStun);
                tentacle.SyncArmedOverrideImmediate();
                tentacle.SetStunnedVisual(true);
            }
            NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Stunned);
        }
        else
        {
            foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
            {
                tentacle.SetStunnedVisual(false);
            }
            NAbyssalInkmawBossBackground.Current?.SetBossStance(
                nextId == InkSprayId
                    ? NAbyssalInkmawBossBackground.BossStance.Charging
                    : NAbyssalInkmawBossBackground.BossStance.Normal);
        }

        return Task.CompletedTask;
    }

    public override decimal ModifyHpLostAfterOstyLate(
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Creature || Phase != 1 || _deathHandled || amount <= 0m)
            return amount;

        int threshold = Phase2HpThreshold;
        if (_phase2Pending)
            return 0m;

        if (target.CurrentHp - amount <= threshold)
            return Math.Max(0m, target.CurrentHp - threshold);

        return amount;
    }

    public override bool ShouldDie(Creature creature)
    {
        if (creature == Creature && Phase == 1 && !_deathHandled)
            return false;
        return base.ShouldDie(creature);
    }

    public override Task AfterPreventingDeath(Creature creature)
    {
        if (creature == Creature && Phase == 1 && !_deathHandled)
            TriggerPhase2Pending();
        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Creature || !Creature.IsAlive || _deathHandled)
            return Task.CompletedTask;

        if (result.TotalDamage > 0)
            NAbyssalInkmawBossBackground.Current?.PlayBossHurt();

        if (Phase == 1 && !_phase2Pending && Creature.CurrentHp <= Phase2HpThreshold)
            TriggerPhase2Pending();

        return Task.CompletedTask;
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature == Creature && Creature.IsAlive && Phase == 1 && !_phase2Pending && Creature.CurrentHp <= Phase2HpThreshold)
            TriggerPhase2Pending();
        return Task.CompletedTask;
    }

    private void TriggerPhase2Pending()
    {
        if (Phase != 1 || _phase2Pending || _deathHandled)
            return;

        _phase2Pending = true;

        // Preserve the player's Stun turn reward if Boss hits 50% HP while Stunned:
        // Phase 2 Surge fires on the immediately following turn instead of canceling Stun!
        if (NextMove?.Id == PostSprayStunId)
            return;

        if (!IsPerformingMove && MoveStateMachine != null)
        {
            bool wasChargingOrSpraying = NextMove?.Id == InkChargeId || NextMove?.Id == InkSprayId;
            _inkSequenceStage = InkSequenceStage.None;
            SetMoveImmediate(Move(Phase2SurgeId), forceTransition: true);
            NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Normal);

            if (wasChargingOrSpraying)
            {
                foreach (IAbyssalSyncedTentacle tentacle in EnumerateLivingSyncedTentacles())
                {
                    tentacle.CancelChargeBuffImmediate();
                }
            }
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var abyssalUndertow = new MoveState(
            AbyssalUndertowId,
            AbyssalUndertowMove,
            new MultiAttackIntent(UndertowDamage, UndertowHits),
            new DefendIntent());

        var summonTentacle = new MoveState(
            SummonTentacleId,
            SummonTentacleMove,
            new SummonIntent(),
            new DefendIntent());

        var tidalCrash = new MoveState(
            TidalCrashId,
            TidalCrashMove,
            new SingleAttackIntent(() => TidalCrashDamage),
            new DebuffIntent());

        var abyssalPressure = new MoveState(
            AbyssalPressureId,
            AbyssalPressureMove,
            new StatusIntent(PressureSlimedCount),
            new BuffIntent(),
            new DefendIntent());

        // Teal-blue upward Buff arrow + yellow "?" Unknown intent — do-nothing charge-up turn!
        var inkCharge = new MoveState(
            InkChargeId,
            InkChargeMove,
            new BuffIntent(),
            new UnknownIntent());

        var inkSpray = new MoveState(
            InkSprayId,
            InkSprayMove,
            new SingleAttackIntent(() => InkSprayDamage),
            new DebuffIntent());

        var postSprayStun = new MoveState(
            PostSprayStunId,
            PostSprayStunMove,
            new StunIntent());

        var phase2Surge = new MoveState(
            Phase2SurgeId,
            Phase2SurgeMove,
            new BuffIntent(),
            new SummonIntent(),
            new DefendIntent());

        // 7-turn repeatable Boss cadence:
        //   T1 ABYSSAL_UNDERTOW -> T2 SUMMON_TENTACLE (or ABYSSAL_PRESSURE if full board) ->
        //   T3 TIDAL_CRASH -> T4 ABYSSAL_PRESSURE ->
        //   [T5 INK_CHARGE -> T6 INK_SPRAY -> T7 POST_SPRAY_STUN] -> T8 repeats!
        var router = new ConditionalBranchState(MoveRouterId);
        router.AddState(postSprayStun, () => _inkSequenceStage == InkSequenceStage.StunNext);
        router.AddState(phase2Surge, () => Phase == 1 && (_phase2Pending || Creature.CurrentHp <= Phase2HpThreshold));
        router.AddState(inkSpray, () => _inkSequenceStage == InkSequenceStage.SprayNext);
        router.AddState(abyssalUndertow, () => _cycleStep == 0);
        router.AddState(summonTentacle, () => _cycleStep == 1 && (LivingTentacleCount < 2 || LivingSmallTentacleCount < 2));
        router.AddState(abyssalPressure, () => _cycleStep == 1);
        router.AddState(tidalCrash, () => _cycleStep == 2);
        router.AddState(abyssalPressure, () => _cycleStep == 3);
        router.AddState(inkCharge, () => true);

        abyssalUndertow.FollowUpState = router;
        summonTentacle.FollowUpState = router;
        tidalCrash.FollowUpState = router;
        abyssalPressure.FollowUpState = router;
        inkCharge.FollowUpState = router;
        inkSpray.FollowUpState = router;
        postSprayStun.FollowUpState = router;
        phase2Surge.FollowUpState = router;

        return new MonsterMoveStateMachine(
            [abyssalUndertow, summonTentacle, tidalCrash, abyssalPressure, inkCharge, inkSpray, postSprayStun, phase2Surge, router],
            abyssalUndertow);
    }

    private MoveState Move(string stateId) => (MoveState)MoveStateMachine!.States[stateId];

    private async Task AbyssalUndertowMove(IReadOnlyList<Creature> targets)
    {
        _cycleStep++;
        NAbyssalInkmawBossBackground.Current?.PlayTidalCrash();

        await WaitForCombatBeatAsync(0.58f);
        await DamageCmd.Attack(UndertowDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await WaitForCombatBeatAsync(0.28f);
        await DamageCmd.Attack(UndertowDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await CreatureCmd.GainBlock(Creature, UndertowBlock, ValueProp.Move, null);
        await WaitForCombatBeatAsync(0.35f);
    }

    private async Task TidalCrashMove(IReadOnlyList<Creature> targets)
    {
        _cycleStep++;
        NAbyssalInkmawBossBackground.Current?.PlayTidalCrash();

        // Exact `tidal_hit` Spine event timing (`t = 0.78s`) when twin flanking tsunami plumes erupt!
        await WaitForCombatBeatAsync(0.78f);

        await DamageCmd.Attack(TidalCrashDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_heavy_blunt")
            .Execute(null);

        foreach (Creature target in targets)
        {
            if (!target.IsAlive)
                continue;
            await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), target, TidalDebuffStacks, Creature, null);
            await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), target, TidalDebuffStacks, Creature, null);
        }

        await WaitForCombatBeatAsync(0.45f);
    }

    private async Task AbyssalPressureMove(IReadOnlyList<Creature> targets)
    {
        _cycleStep++;
        NAbyssalInkmawBossBackground.Current?.PlayBossPressurePulse();
        await WaitForCombatBeatAsync(0.62f);

        await CreatureCmd.GainBlock(Creature, PressureBlock, ValueProp.Move, null);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, PressureStrength, Creature, null);
        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, PressureSlimedCount, null);
        await WaitForCombatBeatAsync(0.40f);
    }

    private async Task SummonTentacleMove(IReadOnlyList<Creature> targets)
    {
        _cycleStep++;
        NAbyssalInkmawBossBackground.Current?.PlaySummonTentacle();
        await WaitForCombatBeatAsync(0.70f);

        await CreatureCmd.GainBlock(Creature, SummonBlock, ValueProp.Move, null);
        bool floorBreach = Phase >= 2;
        var choice = new ThrowingPlayerChoiceContext();
        await EnsureTentaclesSummonedAsync(floorBreach, choice);
        await SpawnSmallTentaclesAsync(Phase >= 2 ? 2 : 1, choice);
        await WaitForCombatBeatAsync(0.45f);
    }

    private async Task InkChargeMove(IReadOnlyList<Creature> targets)
    {
        // Deliberately performs NO attack or self-stat gain — all surviving tentacles supply
        // their unique temporary buffs on this turn while Boss gathers abyssal pressure.
        _inkSequenceStage = InkSequenceStage.SprayNext;
        NAbyssalInkmawBossBackground.Current?.PlayBossChargePulse();
        await WaitForCombatBeatAsync(0.85f);
    }

    private async Task InkSprayMove(IReadOnlyList<Creature> targets)
    {
        _inkSequenceStage = InkSequenceStage.StunNext;
        _cycleStep = 0;

        NAbyssalInkmawBossBackground.Current?.PlayInkSpray();
        NAbyssalInkSprayOverlay.PlaySweepDrench();

        // Wait precisely until the sweeping cylindrical hydro-ink beam reaches 0° dead-center over the player (`t = 1.06s`)!
        await WaitForCombatBeatAsync(1.06f);
        NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Normal);

        await DamageCmd.Attack(InkSprayDamage)
            .FromMonster(this)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null);

        foreach (Creature target in targets)
        {
            if (target.IsAlive)
            {
                await PowerCmd.Apply<ThingsInkBlackoutPower>(new ThrowingPlayerChoiceContext(), target, InkBlackoutTurns, Creature, null);
            }
        }

        await WaitForCombatBeatAsync(0.60f);
    }

    private async Task PostSprayStunMove(IReadOnlyList<Creature> targets)
    {
        _inkSequenceStage = InkSequenceStage.None;
        SpawnNativeStunnedVfx(Creature);
        await WaitForCombatBeatAsync(0.55f);
        NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Normal);
    }

    private async Task Phase2SurgeMove(IReadOnlyList<Creature> targets)
    {
        Phase = 2;
        _phase2Pending = false;
        _cycleStep = 0;
        _inkSequenceStage = InkSequenceStage.None;

        NAbyssalInkmawBossBackground.Current?.PlayPhase2Surge();
        await WaitForCombatBeatAsync(0.95f);

        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
        await ClearDebuffsSafelyAsync(Creature);

        // If Phase 2 interrupted an armed Ink Spray before it fired, strip unused charge-only Vigor.
        if (Creature.GetPower<VigorPower>() is { } staleVigor)
        {
            await PowerCmd.Remove(staleVigor);
        }

        var choice = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<ThingsAbyssalSurgePower>(choice, Creature, 1m, Creature, null);
        await PowerCmd.Apply<StrengthPower>(choice, Creature, Phase2Strength, Creature, null);
        await CreatureCmd.GainBlock(Creature, Phase2Block, ValueProp.Move, null);

        // In Phase 2 【暗潮狂涌】, any missing major tentacle smashes directly through the stone bridge slabs
        // and outer bridge breach craters erupt with up to 2 distinct small tendril variants!
        await EnsureTentaclesSummonedAsync(floorBreach: true, choice);
        await SpawnSmallTentaclesAsync(2, choice);
        await WaitForCombatBeatAsync(0.65f);
    }

    internal static void SpawnNativeStunnedVfx(Creature creature)
    {
        if (NStunnedVfx.Create(creature) is not { } vfx)
            return;

        Node? vfxContainer = creature.GetVfxContainer();
        if (vfxContainer == null)
            return;

        Callable.From(() => vfxContainer.AddChildSafely(vfx)).CallDeferred();
    }

    internal async Task EnsureTentaclesSummonedAsync(bool floorBreach, PlayerChoiceContext choiceContext)
    {
        if (CombatState == null || !Creature.IsAlive)
            return;

        if (LivingLeftTentacle == null)
        {
            var leftMonster = (ThingsAbyssalCrushingTentacle)ModelDb.Monster<ThingsAbyssalCrushingTentacle>().ToMutable();
            leftMonster.ConfigureSpawn(floorBreach, playEmergeOnAdd: true);
            string? slot = ResolveSlot(ThingsAbyssalInkmawBossEncounter.LeftTentacleSlot);
            Creature added = await CreatureCmd.Add(leftMonster, CombatState, Creature.Side, slot);
            await PowerCmd.Apply<MinionPower>(choiceContext, added, 1m, Creature, null);
        }

        if (LivingRightTentacle == null)
        {
            var rightMonster = (ThingsAbyssalInkTentacle)ModelDb.Monster<ThingsAbyssalInkTentacle>().ToMutable();
            rightMonster.ConfigureSpawn(floorBreach, playEmergeOnAdd: true);
            string? slot = ResolveSlot(ThingsAbyssalInkmawBossEncounter.RightTentacleSlot);
            Creature added = await CreatureCmd.Add(rightMonster, CombatState, Creature.Side, slot);
            await PowerCmd.Apply<MinionPower>(choiceContext, added, 1m, Creature, null);
        }

        CombatState.SortEnemiesBySlotName();
        await SyncCoverPowerAsync(choiceContext);
        NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
    }

    public async Task SpawnSmallTentaclesAsync(int maxToSpawn, PlayerChoiceContext choiceContext)
    {
        if (CombatState == null || !Creature.IsAlive || maxToSpawn <= 0)
            return;

        int spawned = 0;
        if (spawned < maxToSpawn && OuterLeftSmallTentacle == null)
        {
            ThingsAbyssalSmallTentacleBase leftSmall = CreateNextSmallTentacleVariant();
            leftSmall.ConfigureSpawn(isOuterLeft: true, playEmergeOnAdd: true);
            string? slot = ResolveSlot(ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot);
            Creature added = await CreatureCmd.Add(leftSmall, CombatState, Creature.Side, slot);
            await PowerCmd.Apply<MinionPower>(choiceContext, added, 1m, Creature, null);
            spawned++;
        }

        if (spawned < maxToSpawn && OuterRightSmallTentacle == null)
        {
            ThingsAbyssalSmallTentacleBase rightSmall = CreateNextSmallTentacleVariant();
            rightSmall.ConfigureSpawn(isOuterLeft: false, playEmergeOnAdd: true);
            string? slot = ResolveSlot(ThingsAbyssalInkmawBossEncounter.OuterRightTentacleSlot);
            Creature added = await CreatureCmd.Add(rightSmall, CombatState, Creature.Side, slot);
            await PowerCmd.Apply<MinionPower>(choiceContext, added, 1m, Creature, null);
            spawned++;
        }

        if (spawned > 0)
        {
            CombatState.SortEnemiesBySlotName();
            NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
        }
    }

    private ThingsAbyssalSmallTentacleBase CreateNextSmallTentacleVariant()
    {
        HashSet<Type> livingTypes = LivingSmallTentacles.Select(t => t.GetType()).ToHashSet();
        var candidates = new List<Func<ThingsAbyssalSmallTentacleBase>>(3);

        if (!livingTypes.Contains(typeof(ThingsAbyssalSpikeTentacle)))
            candidates.Add(() => (ThingsAbyssalSpikeTentacle)ModelDb.Monster<ThingsAbyssalSpikeTentacle>().ToMutable());
        if (!livingTypes.Contains(typeof(ThingsAbyssalShellTentacle)))
            candidates.Add(() => (ThingsAbyssalShellTentacle)ModelDb.Monster<ThingsAbyssalShellTentacle>().ToMutable());
        if (!livingTypes.Contains(typeof(ThingsAbyssalGlandTentacle)))
            candidates.Add(() => (ThingsAbyssalGlandTentacle)ModelDb.Monster<ThingsAbyssalGlandTentacle>().ToMutable());

        if (candidates.Count == 0)
        {
            candidates.Add(() => (ThingsAbyssalSpikeTentacle)ModelDb.Monster<ThingsAbyssalSpikeTentacle>().ToMutable());
            candidates.Add(() => (ThingsAbyssalShellTentacle)ModelDb.Monster<ThingsAbyssalShellTentacle>().ToMutable());
            candidates.Add(() => (ThingsAbyssalGlandTentacle)ModelDb.Monster<ThingsAbyssalGlandTentacle>().ToMutable());
        }

        int idx = RunRng?.MonsterAi != null ? RunRng.MonsterAi.NextInt(candidates.Count) : 0;
        return candidates[idx]();
    }

    internal async Task SyncCoverPowerAsync(PlayerChoiceContext choiceContext)
    {
        if (!Creature.IsAlive || CombatState == null)
            return;

        int desiredPercent = LivingTentacleCount * ThingsAbyssalCoverPower.ReductionPerTentaclePercent;
        ThingsAbyssalCoverPower? existing = Creature.GetPower<ThingsAbyssalCoverPower>();
        if (desiredPercent <= 0)
        {
            if (existing != null)
                await PowerCmd.Remove(existing);
            return;
        }

        if (existing == null)
        {
            await PowerCmd.Apply<ThingsAbyssalCoverPower>(choiceContext, Creature, desiredPercent, Creature, null);
        }
        else if (existing.Amount != desiredPercent)
        {
            await PowerCmd.ModifyAmount(choiceContext, existing, desiredPercent - existing.Amount, Creature, null);
        }
    }

    internal async Task HandleTentacleSeveredAsync(bool isLeft, Creature severedTentacle, PlayerChoiceContext choiceContext)
    {
        NAbyssalInkmawBossBackground.Current?.PlayTentacleRetreatDie(isLeft);

        // Severing ANY major bridge tentacle immediately washes ALL blackened cards across the entire
        // deck clean, surgically restores original energy costs, and removes ThingsInkBlackoutPower!
        if (CombatState != null)
        {
            AbyssalInkBlackout.WashAllCards(CombatState);
            foreach (Player player in CombatState.Players)
            {
                if (player.Creature.GetPower<ThingsInkBlackoutPower>() is { } inkPower)
                {
                    await PowerCmd.Remove(inkPower);
                }
            }
        }

        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        await SyncCoverPowerAsync(choiceContext);
        NAbyssalInkmawBossBackground.Current?.PlayBossHurt();
        NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);

        await CreatureCmd.Damage(
            choiceContext,
            Creature,
            ThingsAbyssalCoverPower.SeverRecoilDamage,
            ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
            null,
            null);

        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await PowerCmd.Apply<VulnerablePower>(
                choiceContext,
                Creature,
                ThingsAbyssalCoverPower.SeverVulnerableStacks,
                severedTentacle,
                null);
        }
    }

    internal Task HandleSmallTentacleSeveredAsync(bool isOuterLeft, Creature severedTentacle, PlayerChoiceContext choiceContext)
    {
        _ = severedTentacle;
        _ = choiceContext;
        NAbyssalInkmawBossBackground.Current?.PlaySmallTentacleRetreatDie(isOuterLeft);

        // Severing a small outer bridge tendril washes 1 blackened card clean in hand per player
        // and restores its original energy cost.
        if (CombatState != null)
        {
            AbyssalInkBlackout.WashOneRandomCardPerPlayer(CombatState, RunRng?.CombatCardSelection);
        }

        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            NAbyssalInkmawBossBackground.Current?.PlayBossHurt();
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
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
        NAbyssalInkmawBossBackground.Current?.PlayBossDie();

        if (CombatState != null)
        {
            AbyssalInkBlackout.WashAllCards(CombatState);
            List<Creature> survivingTentacles = CombatState.Enemies
                .Where(e => e.IsAlive && e != Creature &&
                            (e.Monster is ThingsAbyssalCrushingTentacle ||
                             e.Monster is ThingsAbyssalInkTentacle ||
                             e.Monster is ThingsAbyssalSmallTentacleBase))
                .ToList();
            foreach (Creature tentacle in survivingTentacles)
            {
                if (tentacle.IsAlive)
                    await CreatureCmd.Kill(tentacle, force: true);
            }
        }
    }

    private string? ResolveSlot(string preferredSlot)
    {
        if (CombatState?.Encounter?.Slots is { } slots && slots.Contains(preferredSlot))
        {
            bool occupied = CombatState.Enemies.Any(e => e.IsAlive && string.Equals(e.SlotName, preferredSlot, StringComparison.Ordinal));
            if (!occupied)
                return preferredSlot;
        }
        return null;
    }
}
