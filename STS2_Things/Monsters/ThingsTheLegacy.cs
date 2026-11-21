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
///     腐化之遗 — Underdocks Boss
///     开局: LegacyBeatOfDeathPower (玩家出牌扣2血) + HardenedShellPower (HP/3 层)
///     意图循环:
///     衰竭 → 回音 → 血弹 → 强化 → 循环
///     强化阶段:
///     第1次: 1层人工制品
///     第2次: 1层死亡律动 + 硬化外壳改成HP/4
///     第3次: 眩晕戳刺(ThingsDazedPower)
///     第4次: 2点力量 + 1层死亡律动
///     之后循环: n+1层人工制品 + 外壳HP/(n+1) + 2点力量 + 1层死亡律动
/// </summary>
public sealed class ThingsTheLegacy : ThingsSpineMonster
{
    // The encounter lives in Underdocks, so it uses an act1_b track that is
    // already loaded by the native Act music lifecycle.
    private const string _trackName = "waterfall_giant_progress";
    private const int BloodDamage = 3;

    private const int BloodCount = 4;

    // 复用Vantom（暗黑Boss）的原版音效
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_inky_lance";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_buff";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/vantom/vantom_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
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
        ModelDb.Power<ThingsLegacyBeatOfDeathPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsDazedPower>().ResolvedBigIconPath,
        ModelDb.Power<HardenedShellPower>().ResolvedBigIconPath,
        ModelDb.Power<ArtifactPower>().ResolvedBigIconPath,
        ModelDb.Power<StrengthPower>().ResolvedBigIconPath
    ]).Distinct();

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 298, 285) + (Sts2VersionSeam.IsDread() ? 40 : 0);

    public override int MaxInitialHp => MinInitialHp;

    private int EchoDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 13, 12);

    private decimal OpeningBeatOfDeathAmount => 2m + (Sts2VersionSeam.IsDread() ? 1m : 0m);

    private decimal StrengthenStrengthAmount => 2m + (Sts2VersionSeam.IsDread() ? 1m : 0m);

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
        // 死亡律动: 玩家每出一张牌扣 2 血（恐惧模式下为 3 血）
        await PowerCmd.Apply<ThingsLegacyBeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, OpeningBeatOfDeathAmount, Creature, null);
        // 硬化外壳: 层数 = 最大 HP / 3
        // 注意：HardenedShellPower 的 ShouldScaleInMultiplayer=true，PowerCmd.Apply 会自动缩放。
        // 必须用缩放前的原始 HP（MonsterMaxHpBeforeModification）计算，否则会双重缩放导致层数超过 MaxHp。
        var baseHp = Creature.MonsterMaxHpBeforeModification ?? Creature.MaxHp;
        await PowerCmd.Apply<HardenedShellPower>(new ThrowingPlayerChoiceContext(), Creature,
            baseHp / 3m, Creature, null);
    }

    // ========== 状态机 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        List<MonsterState> list = new();

        // 衰竭: 给玩家 Burn + Dazed + Slimed 各一张
        var exhaustMove = new MoveState("EXHAUST_MOVE", ExhaustMove,
            new StatusIntent(6));

        // 回音: 12/14 伤害
        var echoMove = new MoveState("ECHO_MOVE", EchoMove,
            new SingleAttackIntent(EchoDamage));

        // 血弹: 3×4
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

        await CardPileCmd.AddToCombatAndPreview<Burn>(targets, PileType.Discard, 2, null);
        await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, 2, null);
        await CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, 2, null);
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

        switch (strengthenStage)
        {
            case 1:
                // 第1次强化：1层人工制品
                await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
                break;
            case 2:
                // 第2次强化：1层死亡律动 + 硬化外壳改成HP/4
                await PowerCmd.Apply<ThingsLegacyBeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
                await SetShellDivisor(4);
                break;
            case 3:
                // 第3次强化：眩晕戳刺(ThingsDazedPower)
                await PowerCmd.Apply<ThingsDazedPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
                break;
            case 4:
                // 第4次强化：2点力量 + 1层死亡律动
                await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, StrengthenStrengthAmount, Creature, null);
                await PowerCmd.Apply<ThingsLegacyBeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
                break;
            default:
                // 之后循环：第1次循环=2层人工制品+HP/5, 第2次=3层+HP/6, ...
                var n = strengthenStage - 4;
                // 先施死亡律动再施人工制品，避免人工制品抵消死亡律动
                await PowerCmd.Apply<ThingsLegacyBeatOfDeathPower>(new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
                await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), Creature, n + 1m, Creature, null);
                await SetShellDivisor(n + 4);
                await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, StrengthenStrengthAmount, Creature, null);
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
