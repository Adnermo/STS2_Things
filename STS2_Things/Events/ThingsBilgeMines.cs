using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Events.Minigames;

namespace STS2_Things.Events;

/// <summary>
/// Underdocks (Act 1B) interactive minesweeper minigame event ("Bilge Grate / 暗渠货栅").
/// Offers a free 5-probe sweep, a 25-Gold 7-probe sweep, or a lossless 20-Gold edge skim.
/// Uses an IOverlayScreen 6x6 board with glint-deduction minesweeper mechanics and lockstep multiplayer sync.
/// </summary>
public sealed class ThingsBilgeMines : ThingsEventModel
{
    public const int FreeProbes = BilgeSweepMinigame.FreeProbes;
    public const int PaidProbes = BilgeSweepMinigame.PaidProbes;
    public const int PaidProbeGoldCost = BilgeSweepMinigame.PaidProbeGoldCost;
    public const int MineBurstDamage = BilgeSweepMinigame.MineBurstDamage;
    public const int EdgeSkimGold = BilgeSweepMinigame.EdgeSkimGold;

    private bool _settling;
    private ThingsBilgeSweepOutcome? _bufferedRemoteOutcome;
    private TaskCompletionSource<ThingsBilgeSweepOutcome>? _remoteOutcomeWaiter;
    private Action? _remoteWaiterCancelRegistration;

    /// <summary>
    /// Optional driver used exclusively by headless probes/tests when NOverlayStack is absent.
    /// </summary>
    internal Func<BilgeSweepMinigame, Task>? MinigameTestDriver { get; set; }

    public override bool IsDeterministic => false;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
        _bufferedRemoteOutcome = null;
        _remoteOutcomeWaiter = null;
        _remoteWaiterCancelRegistration = null;
        MinigameTestDriver = null;
    }

    protected override void OnEventFinished()
    {
        base.OnEventFinished();
        CancelPendingRemoteWaiter();
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("FreeProbes", FreeProbes),
        new IntVar("PaidProbes", PaidProbes),
        new GoldVar("PaidGold", PaidProbeGoldCost),
        new DamageVar("MineDamage", MineBurstDamage, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered),
        new GoldVar("EdgeGold", EdgeSkimGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player => !player.Creature.IsDead);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SWEEP", "SWEEP_PAID", "SKIM_EDGE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState).Concat(
        [
            "res://images/events/minigames/things_bilge_sweep_bg.png",
            "res://images/events/minigames/things_bilge_tile_cover.png",
            "res://images/events/minigames/things_bilge_tile_glint.png",
            "res://images/events/minigames/things_bilge_flag_icon.png",
            "res://images/events/minigames/things_bilge_probe_icon.png",
            "res://shaders/things_bilge_water.gdshader",
            ThingsMinigameUiTheme.DivineButtonPath,
            ThingsMinigameUiTheme.DivineButtonOutlinePath,
            ThingsMinigameUiTheme.TinyNinePatchPath,
            "res://images/events/crystal_sphere/crystal_sphere_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_common_potion.png",
            "res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png",
            "res://images/events/crystal_sphere/crystal_sphere_relic.png",
            "res://images/powers/poison_power.png",
        ]).Distinct();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        EventOption freeSweep = new(this, SweepFreeAsync, InitialOptionKey("SWEEP"));
        EventOption paidSweep = EventOwner.Gold >= PaidProbeGoldCost
            ? new EventOption(this, SweepPaidAsync, InitialOptionKey("SWEEP_PAID"))
            : new EventOption(this, null, InitialOptionKey("SWEEP_PAID_NO_GOLD"));
        EventOption skimEdge = new(this, SkimEdgeAsync, InitialOptionKey("SKIM_EDGE"));

        return [freeSweep, paidSweep, skimEdge];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    internal void ReceiveRemoteOutcome(ThingsBilgeSweepOutcome outcome)
    {
        TaskCompletionSource<ThingsBilgeSweepOutcome>? waiter = _remoteOutcomeWaiter;
        _remoteOutcomeWaiter = null;
        if (waiter != null && waiter.TrySetResult(outcome))
            return;
        _bufferedRemoteOutcome = outcome;
    }

    private void CancelPendingRemoteWaiter()
    {
        Action? cancel = _remoteWaiterCancelRegistration;
        _remoteWaiterCancelRegistration = null;
        if (cancel != null)
            ThingsEventMinigameSync.UnregisterRemoteWaiter(cancel);

        TaskCompletionSource<ThingsBilgeSweepOutcome>? waiter = _remoteOutcomeWaiter;
        _remoteOutcomeWaiter = null;
        waiter?.TrySetCanceled();
    }

    private Task SweepFreeAsync() => RunSweepOptionAsync(FreeProbes, paid: false);

    private Task SweepPaidAsync() => RunSweepOptionAsync(PaidProbes, paid: true);

    private async Task RunSweepOptionAsync(int probes, bool paid)
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (paid)
        {
            if (player.Gold < PaidProbeGoldCost)
            {
                _settling = false;
                SetEventState(InitialDescription, GenerateInitialOptions());
                return;
            }
            await PlayerCmd.LoseGold(PaidProbeGoldCost, player, GoldLossType.Spent);
        }

        ThingsBilgeSweepOutcome outcome;
        if (LocalContext.IsMe(player))
        {
            // Use an isolated derived RNG for local-only board generation so EventModel.Rng
            // is never mutated out of sync with remote peers.
            var minigame = new BilgeSweepMinigame(player, CreateLocalMinigameRng(player, probes), probes);
            NBilgeSweepScreen? screen = null;
            try
            {
                if (MinigameTestDriver != null)
                {
                    await MinigameTestDriver(minigame);
                    minigame.ConfirmExit();
                    outcome = await minigame.CompletionTask;
                }
                else if (NOverlayStack.Instance != null)
                {
                    screen = NBilgeSweepScreen.Create(minigame);
                    NOverlayStack.Instance.Push(screen);
                    outcome = await minigame.CompletionTask;
                }
                else
                {
                    outcome = minigame.BuildOutcome();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (screen != null && NOverlayStack.Instance != null)
                    NOverlayStack.Instance.Remove(screen);
            }

            if (!ThingsEventMinigameSync.IsRunActive)
                return;

            ThingsEventMinigameSync.SendBilgeSweepResult(outcome);
        }
        else
        {
            try
            {
                outcome = await WaitForRemoteOutcomeAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!ThingsEventMinigameSync.IsRunActive)
                return;
        }

        await ApplyOutcomeAsync(player, outcome);
    }

    private async Task<ThingsBilgeSweepOutcome> WaitForRemoteOutcomeAsync()
    {
        if (_bufferedRemoteOutcome != null)
        {
            ThingsBilgeSweepOutcome buffered = _bufferedRemoteOutcome;
            _bufferedRemoteOutcome = null;
            return buffered;
        }

        var waiter = new TaskCompletionSource<ThingsBilgeSweepOutcome>();
        _remoteOutcomeWaiter = waiter;
        Action cancel = () =>
        {
            if (ReferenceEquals(_remoteOutcomeWaiter, waiter))
                _remoteOutcomeWaiter = null;
            waiter.TrySetCanceled();
        };
        _remoteWaiterCancelRegistration = cancel;
        ThingsEventMinigameSync.RegisterRemoteWaiter(EventOwner.NetId, cancel);
        try
        {
            return await waiter.Task;
        }
        finally
        {
            if (ReferenceEquals(_remoteOutcomeWaiter, waiter))
                _remoteOutcomeWaiter = null;
            if (ReferenceEquals(_remoteWaiterCancelRegistration, cancel))
            {
                _remoteWaiterCancelRegistration = null;
                ThingsEventMinigameSync.UnregisterRemoteWaiter(cancel);
            }
        }
    }

    internal async Task ApplyOutcomeAsync(Player player, ThingsBilgeSweepOutcome outcome)
    {
        if (outcome.HitMine)
        {
            await ThingsEventHelpers.DealUnblockableDamageAsync(player, MineBurstDamage);
            if (player.Creature.IsDead)
            {
                Finish("SWEEP_BURST.loss");
                return;
            }
        }

        List<Reward> rewards = BuildRewards(player, outcome);
        if (rewards.Count > 0)
            await RewardsCmd.OfferCustom(player, rewards);

        if (outcome.HitMine)
            Finish(outcome.HasAnyLoot ? "SWEEP_BURST_LOOT.description" : "SWEEP_BURST_EMPTY.description");
        else if (outcome.HasAnyLoot)
            Finish("SWEEP_CLEAN.description");
        else
            Finish("SWEEP_EMPTY.description");
    }

    private List<Reward> BuildRewards(Player player, ThingsBilgeSweepOutcome outcome)
    {
        var rewards = new List<Reward>();
        int goldTotal = outcome.GoldAmount;

        for (int i = 0; i < outcome.CommonRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
                rewards.Add(new RelicReward(RelicRarity.Common, player));
            else
                goldTotal += BilgeSweepMinigame.RelicFallbackGold;
        }

        if (goldTotal > 0)
            rewards.Add(new GoldReward(goldTotal, goldTotal, player));

        for (int i = 0; i < outcome.PotionCount; i++)
            rewards.Add(new PotionReward(player));

        for (int i = 0; i < outcome.CardCount; i++)
        {
            CardCreationOptions options = CardCreationOptions.ForNonCombatWithDefaultOdds([player.Character.CardPool]);
            rewards.Add(new CardReward(options, 3, player));
        }

        return rewards;
    }

    private async Task SkimEdgeAsync()
    {
        if (!BeginSettlement())
            return;
        await PlayerCmd.GainGold(EdgeSkimGold, EventOwner);
        Finish("SKIM_EDGE.description");
    }

    private static Rng CreateLocalMinigameRng(Player player, int probes)
    {
        IRunState runState = player.RunState;
        unchecked
        {
            ulong h = 14695981039346656037UL;
            string seedStr = runState.Rng.StringSeed ?? "things_bilge_mines";
            foreach (char ch in seedStr)
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            h ^= (ulong)(uint)runState.TotalFloor * 7919UL;
            h *= 1099511628211UL;
            h ^= (ulong)(uint)runState.CurrentActIndex * 313UL;
            h *= 1099511628211UL;
            h ^= player.NetId;
            h *= 1099511628211UL;
            h ^= (ulong)(uint)probes * 131UL;
            return new Rng(h == 0UL ? 1337UL : h, "things_bilge_mines");
        }
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
