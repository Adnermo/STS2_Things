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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using STS2_Things.Audio;
using STS2_Things.Platform;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
///     腐化之遗 — Underdocks Boss（一层腐化之心遗蜕）
///     开局: LegacyBeatOfDeathPower (固定 2 层，玩家出牌 Boss 扣 2 血，不再叠加) + HardenedShellPower (HP/5 层)
///     意图循环:
///     衰竭(仅开局: 1层易伤 + 1/2层脆弱 + 灼伤1/晕眩1/黏液1) → 回音 → 血弹 → 强化 → 循环
///     强化阶段(均不再追加自伤律动，每轮稳定加力量):
///     第1次: 1层人工制品 + 1(恐惧2)力量
///     第2次: 硬化外壳收紧至 HP/6 + 1(恐惧2)力量
///     第3次: 晕眩打击(ThingsDazedPower) + 1(恐惧2)力量
///     第4次: 2层人工制品 + 硬化外壳收紧至 HP/7 + 1(恐惧2)力量
///     之后循环: (n+2)层人工制品 + 外壳收紧至 HP/(n+7) + 1(恐惧2)力量
/// </summary>
public sealed class ThingsTheLegacy : ThingsSpineMonster
{
    // The encounter lives in Underdocks, so it uses an act1_b track that is
    // already loaded by the native Act music lifecycle.
    private const string _trackName = "waterfall_giant_progress";
    private const int BloodDamage = 2;
    private const int MinShellCap = 20;

    // 复用Vantom（暗黑Boss）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_inky_lance";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_buff";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override float HpBarSizeReduction => 140f;
    public override Vector2 ExtraDeathVfxPadding => new(1.4f, 1.7f);

    private static readonly string[] HeartbeatFiles =
    [
        "res://sfx/the_legacy/HeartBeat_1.ogg",
        "res://sfx/the_legacy/HeartBeat_2.ogg"
    ];

    private Godot.Timer? _heartbeatTimer;
    private int _heartbeatIndex;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _heartbeatTimer = null;
        _heartbeatIndex = 0;
    }

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat(
    [
        ..HeartbeatFiles,
        ModelDb.Power<ThingsLegacyBeatOfDeathPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsDazedPower>().ResolvedBigIconPath,
        ModelDb.Power<HardenedShellPower>().ResolvedBigIconPath,
        ModelDb.Power<ArtifactPower>().ResolvedBigIconPath,
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath,
        ModelDb.Power<FrailPower>().ResolvedBigIconPath
    ]).Distinct();

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 260, 240) + (Sts2VersionSeam.IsDread() ? 25 : 0);

    public override int MaxInitialHp => MinInitialHp;

    private int EchoDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 15, 13) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    private int BloodCount => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 5, 4);

    private int ExhaustVulnerableTurns => 1;

    private int ExhaustFrailTurns => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 2, 1);

    private decimal OpeningBeatOfDeathAmount => 2m;

    private decimal StrengthenStrengthAmount => 1m + (Sts2VersionSeam.IsDread() ? 1m : 0m);

    // ========== 入场 ==========
    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        // 初始化专属音乐参数（CustomBgm = act1_b_boss_waterfall_giant）
        STS2_Things.Audio.ModMusicPolicy.UpdateParameter(_trackName, 1f);
        // 心跳循环：挂在 Pausable 节点计时器上，游戏暂停或退场时自动停跳
        StartHeartbeat();
        // 隐藏 Power 保存下一次强化阶段，使进度进入多人 checksum/full-state。
        await PowerCmd.Apply<ThingsLegacyProgressPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
        // 死亡律动: 保留反向特色并固定为 2 层（强化与恐惧模式不再增加自伤）
        await PowerCmd.Apply<ThingsLegacyBeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, OpeningBeatOfDeathAmount, Creature, null);
        // 硬化外壳: 开局即收紧至最大 HP / 5（48/52，恐惧 53/57）
        // 注意：HardenedShellPower 的 ShouldScaleInMultiplayer=true，PowerCmd.Apply 会自动缩放。
        // 必须用缩放前的原始 HP（MonsterMaxHpBeforeModification）计算，否则会双重缩放导致层数超过 MaxHp。
        var baseHp = Creature.MonsterMaxHpBeforeModification ?? Creature.MaxHp;
        await PowerCmd.Apply<HardenedShellPower>(new ThrowingPlayerChoiceContext(), Creature,
            baseHp / 5m, Creature, null);
    }

    // ========== 状态机 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        List<MonsterState> list = new();

        // 衰竭: 1 层易伤 + 1(2) 层脆弱 + Burn 1 / Dazed 1 / Slimed 1（共 3 张）
        var exhaustMove = new MoveState("EXHAUST_MOVE", ExhaustMove,
            new DebuffIntent(),
            new StatusIntent(3));

        // 回音: 13/15 (+2 恐惧) 单段重击
        var echoMove = new MoveState("ECHO_MOVE", EchoMove,
            new SingleAttackIntent(EchoDamage));

        // 血弹: 2×4 / 2×5 高频连击
        var bloodMove = new MoveState("BLOOD_MOVE", BloodMove,
            new MultiAttackIntent(BloodDamage, BloodCount));

        // 强化: 根据次数递增
        var strengthenMove = new MoveState("STRENGTHEN_MOVE", StrengthenMove,
            new BuffIntent());

        // 固定循环: 衰竭(仅开局) → 回音 → 血弹 → 强化 → 回音 → 血弹 → 强化 → ...
        exhaustMove.FollowUpState = echoMove;
        echoMove.FollowUpState = bloodMove;
        bloodMove.FollowUpState = strengthenMove;
        strengthenMove.FollowUpState = echoMove;

        list.Add(exhaustMove);
        list.Add(echoMove);
        list.Add(bloodMove);
        list.Add(strengthenMove);

        return new MonsterMoveStateMachine(list, exhaustMove);
    }

    // ========== 招式 ==========

    private async Task ExhaustMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        var ctx = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<VulnerablePower>(ctx, targets, ExhaustVulnerableTurns, Creature, null);
        await PowerCmd.Apply<FrailPower>(ctx, targets, ExhaustFrailTurns, Creature, null);
        await CardPileCmd.AddToCombatAndPreview<Burn>(targets, PileType.Discard, 1, null);
        await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, 1, null);
        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 1, null);
    }

    private async Task EchoMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(EchoDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task BloodMove(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(BloodDamage)
            .FromMonster(this)
            .WithHitCount(BloodCount)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
    }

    private async Task StrengthenMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);

        var progress = Creature.GetPower<ThingsLegacyProgressPower>()
            ?? throw new InvalidOperationException("Legacy progress power is missing.");
        int strengthenStage = progress.NextStage;
        var ctx = new ThrowingPlayerChoiceContext();

        switch (strengthenStage)
        {
            case 1:
                // 第1次强化：1层人工制品 + 力量
                await PowerCmd.Apply<ArtifactPower>(ctx, Creature, 1m, Creature, null);
                await PowerCmd.Apply<StrengthPower>(ctx, Creature, StrengthenStrengthAmount, Creature, null);
                break;
            case 2:
                // 第2次强化：硬化外壳收紧至 HP/6 + 力量
                await SetShellDivisor(6);
                await PowerCmd.Apply<StrengthPower>(ctx, Creature, StrengthenStrengthAmount, Creature, null);
                break;
            case 3:
                // 第3次强化：晕眩打击 + 力量
                await PowerCmd.Apply<ThingsDazedPower>(ctx, Creature, 1m, Creature, null);
                await PowerCmd.Apply<StrengthPower>(ctx, Creature, StrengthenStrengthAmount, Creature, null);
                break;
            case 4:
                // 第4次强化：2层人工制品 + 硬化外壳收紧至 HP/7 + 力量
                await PowerCmd.Apply<ArtifactPower>(ctx, Creature, 2m, Creature, null);
                await SetShellDivisor(7);
                await PowerCmd.Apply<StrengthPower>(ctx, Creature, StrengthenStrengthAmount, Creature, null);
                break;
            default:
                // 之后循环：持续加人工制品 + 收紧外壳 + 力量
                var n = strengthenStage - 4;
                await PowerCmd.Apply<ArtifactPower>(ctx, Creature, n + 2m, Creature, null);
                await SetShellDivisor(n + 7);
                await PowerCmd.Apply<StrengthPower>(ctx, Creature, StrengthenStrengthAmount, Creature, null);
                break;
        }

        progress.Advance();
    }

    private async Task SetShellDivisor(int divisor)
    {
        var shell = Creature.Powers.OfType<HardenedShellPower>().FirstOrDefault();
        if (shell != null)
        {
            // PowerCmd.ModifyAmount truncates decimal offsets. Compute the desired
            // shell as an integer first so every divisor reaches the exact floor.
            var target = Creature.MaxHp / divisor;
            if (target < MinShellCap)
                target = MinShellCap;
            await PowerCmd.ModifyAmount(new ThrowingPlayerChoiceContext(), shell,
                target - shell.Amount, Creature, null);
        }
    }

    // ========== 死亡升调 ==========
    protected override Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature == Creature)
        {
            StopHeartbeat();
            // 死亡升调（vantom_progress=5 触发FMOD升调自动化）
            STS2_Things.Audio.ModMusicPolicy.UpdateParameter(_trackName, 5f);
        }
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        StopHeartbeat();
        return base.AfterCombatEnd(room);
    }

    public override void BeforeRemovedFromRoom()
    {
        base.BeforeRemovedFromRoom();
        StopHeartbeat();
    }

    // ========== 心跳循环（半血后加速，Pausable Godot.Timer） ==========

    private void StartHeartbeat()
    {
        StopHeartbeat();
        if (TestMode.IsOn
            || string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase)
            || !Creature.IsAlive
            || CombatManager.Instance.IsOverOrEnding)
            return;

        Node? host = Sts2VersionSeam.GetCreatureNode(Creature) ?? (Node?)NCombatRoom.Instance;
        if (host == null || !GodotObject.IsInstanceValid(host) || !host.IsInsideTree())
            return;

        _heartbeatIndex = 0;
        if (host.GetTree() is { Paused: false })
        {
            PlayHeartbeatBeat();
        }

        double interval = GetHeartbeatIntervalSeconds();
        var timer = new Godot.Timer
        {
            Name = "ThingsLegacyHeartbeatTimer",
            ProcessMode = Node.ProcessModeEnum.Pausable,
            OneShot = false,
            Autostart = false,
            WaitTime = interval,
        };
        timer.Timeout += OnHeartbeatTick;
        timer.TreeExiting += OnHeartbeatTimerExiting;
        host.AddChildSafely(timer);
        timer.Start(interval);
        _heartbeatTimer = timer;
    }

    private void StopHeartbeat()
    {
        Godot.Timer? timer = _heartbeatTimer;
        _heartbeatTimer = null;
        if (timer == null || !GodotObject.IsInstanceValid(timer))
            return;

        timer.Timeout -= OnHeartbeatTick;
        timer.TreeExiting -= OnHeartbeatTimerExiting;
        timer.Stop();
        timer.QueueFreeSafely();
    }

    private void OnHeartbeatTimerExiting()
    {
        if (_heartbeatTimer != null)
        {
            _heartbeatTimer.Timeout -= OnHeartbeatTick;
            _heartbeatTimer.TreeExiting -= OnHeartbeatTimerExiting;
            _heartbeatTimer = null;
        }
    }

    private void OnHeartbeatTick()
    {
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
        {
            StopHeartbeat();
            return;
        }

        PlayHeartbeatBeat();

        if (_heartbeatTimer != null && GodotObject.IsInstanceValid(_heartbeatTimer))
        {
            double nextInterval = GetHeartbeatIntervalSeconds();
            if (Math.Abs(_heartbeatTimer.WaitTime - nextInterval) > 0.01)
            {
                _heartbeatTimer.WaitTime = nextInterval;
                _heartbeatTimer.Start(nextInterval);
            }
        }
    }

    private void PlayHeartbeatBeat()
    {
        try
        {
            NativeSfxPlayer.Play(HeartbeatFiles[_heartbeatIndex], volumeDb: -10f);
            _heartbeatIndex = (_heartbeatIndex + 1) % HeartbeatFiles.Length;
        }
        catch (Exception ex)
        {
            Log.Error($"[ThingsTheLegacy] Heartbeat playback error: {ex}");
        }
    }

    private double GetHeartbeatIntervalSeconds() =>
        Creature.CurrentHp <= Creature.MaxHp / 2 ? 0.75 : 1.5;
}
