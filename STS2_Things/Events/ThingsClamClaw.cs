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
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Events.Minigames;

namespace STS2_Things.Events;

/// <summary>
/// Underdocks (Act 1B) Sewer Clam Claw Machine event ("蚌口夹珍 / Clam Claw Crane").
/// Offers three Gold-priced tiers for 1, 2, or 3 grabs plus a clean zero-reward Leave option.
/// Uses an animated Underdocks Sewer Clam whose upper and lower shells clamp prizes inside its shell cavity.
/// </summary>
public sealed class ThingsClamClaw : ThingsEventModel
{
    public const int Tier1GoldCost = ClamClawMinigame.Tier1GoldCost;
    public const int Tier1Grabs = ClamClawMinigame.Tier1Grabs;
    public const int Tier2GoldCost = ClamClawMinigame.Tier2GoldCost;
    public const int Tier2Grabs = ClamClawMinigame.Tier2Grabs;
    public const int Tier3GoldCost = ClamClawMinigame.Tier3GoldCost;
    public const int Tier3Grabs = ClamClawMinigame.Tier3Grabs;

    private bool _settling;
    private ThingsClamClawOutcome? _bufferedRemoteOutcome;
    private TaskCompletionSource<ThingsClamClawOutcome>? _remoteOutcomeWaiter;

    /// <summary>
    /// Optional driver used exclusively by headless probes/tests when NOverlayStack is absent.
    /// </summary>
    internal Func<ClamClawMinigame, Task>? MinigameTestDriver { get; set; }

    public override bool IsDeterministic => false;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
        _bufferedRemoteOutcome = null;
        _remoteOutcomeWaiter = null;
        MinigameTestDriver = null;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar("Tier1Gold", Tier1GoldCost),
        new IntVar("Tier1Grabs", Tier1Grabs),
        new GoldVar("Tier2Gold", Tier2GoldCost),
        new IntVar("Tier2Grabs", Tier2Grabs),
        new GoldVar("Tier3Gold", Tier3GoldCost),
        new IntVar("Tier3Grabs", Tier3Grabs),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Gold >= Tier2GoldCost);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "GRAB_1", "GRAB_2", "GRAB_3", "LEAVE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState).Concat(
        [
            "res://images/events/minigames/things_clam_claw_bg.png",
            "res://images/events/minigames/things_clam_shell_upper.png",
            "res://images/events/minigames/things_clam_shell_lower.png",
            "res://images/events/minigames/things_clam_mouth_back.png",
            "res://images/events/minigames/things_clam_hinge.png",
            "res://images/events/crystal_sphere/crystal_sphere_relic.png",
            "res://images/events/minigames/things_loot_relic_rare_box.png",
            "res://images/events/crystal_sphere/crystal_sphere_big_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_gold.png",
            "res://images/relics/old_coin.png",
            "res://images/events/crystal_sphere/crystal_sphere_rare_potion.png",
            "res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png",
            "res://images/events/minigames/things_loot_scrap_bauble.png",
            "res://shaders/things_bilge_water.gdshader",
            ThingsMinigameUiTheme.DivineButtonPath,
            ThingsMinigameUiTheme.DivineButtonOutlinePath,
            ThingsMinigameUiTheme.TinyNinePatchPath,
        ]).Distinct();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        EventOption tier1 = EventOwner.Gold >= Tier1GoldCost
            ? new EventOption(this, Grab1Async, InitialOptionKey("GRAB_1"))
            : new EventOption(this, null, InitialOptionKey("GRAB_1_NO_GOLD"));
        EventOption tier2 = EventOwner.Gold >= Tier2GoldCost
            ? new EventOption(this, Grab2Async, InitialOptionKey("GRAB_2"))
            : new EventOption(this, null, InitialOptionKey("GRAB_2_NO_GOLD"));
        EventOption tier3 = EventOwner.Gold >= Tier3GoldCost
            ? new EventOption(this, Grab3Async, InitialOptionKey("GRAB_3"))
            : new EventOption(this, null, InitialOptionKey("GRAB_3_NO_GOLD"));
        EventOption leave = new(this, LeaveAsync, InitialOptionKey("LEAVE"));

        return [tier1, tier2, tier3, leave];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    internal void ReceiveRemoteOutcome(ThingsClamClawOutcome outcome)
    {
        if (_remoteOutcomeWaiter != null)
        {
            _remoteOutcomeWaiter.TrySetResult(outcome);
            return;
        }
        _bufferedRemoteOutcome = outcome;
    }

    private Task Grab1Async() => RunClawTierAsync(Tier1GoldCost, Tier1Grabs);
    private Task Grab2Async() => RunClawTierAsync(Tier2GoldCost, Tier2Grabs);
    private Task Grab3Async() => RunClawTierAsync(Tier3GoldCost, Tier3Grabs);

    private async Task RunClawTierAsync(int goldCost, int grabs)
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (player.Gold < goldCost)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await PlayerCmd.LoseGold(goldCost, player, GoldLossType.Spent);

        ThingsClamClawOutcome outcome;
        if (LocalContext.IsMe(player))
        {
            var minigame = new ClamClawMinigame(player, Rng, grabs);
            NClamClawScreen? screen = null;
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
                    screen = NClamClawScreen.Create(minigame);
                    NOverlayStack.Instance.Push(screen);
                    outcome = await minigame.CompletionTask;
                }
                else
                {
                    outcome = minigame.BuildOutcome();
                }
            }
            catch (TaskCanceledException)
            {
                return;
            }
            finally
            {
                if (screen != null && NOverlayStack.Instance != null)
                    NOverlayStack.Instance.Remove(screen);
            }

            ThingsEventMinigameSync.SendClamClawResult(outcome);
        }
        else
        {
            try
            {
                outcome = await WaitForRemoteOutcomeAsync();
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        await ApplyOutcomeAsync(player, outcome);
    }

    private Task<ThingsClamClawOutcome> WaitForRemoteOutcomeAsync()
    {
        if (_bufferedRemoteOutcome != null)
        {
            ThingsClamClawOutcome buffered = _bufferedRemoteOutcome;
            _bufferedRemoteOutcome = null;
            return Task.FromResult(buffered);
        }
        _remoteOutcomeWaiter = new TaskCompletionSource<ThingsClamClawOutcome>();
        return _remoteOutcomeWaiter.Task;
    }

    internal async Task ApplyOutcomeAsync(Player player, ThingsClamClawOutcome outcome)
    {
        List<Reward> rewards = BuildRewards(player, outcome);
        if (rewards.Count > 0)
            await RewardsCmd.OfferCustom(player, rewards);

        Finish(outcome.HasAnyLoot ? "CLAW_HAUL.description" : "CLAW_EMPTY.description");
    }

    private static List<Reward> BuildRewards(Player player, ThingsClamClawOutcome outcome)
    {
        var rewards = new List<Reward>();
        int goldTotal = outcome.GoldAmount;

        for (int i = 0; i < outcome.RareRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
                rewards.Add(new RelicReward(RelicRarity.Rare, player));
            else
                goldTotal += ClamClawMinigame.RareRelicFallbackGold;
        }

        for (int i = 0; i < outcome.CommonRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
                rewards.Add(new RelicReward(RelicRarity.Common, player));
            else
                goldTotal += ClamClawMinigame.RelicFallbackGold;
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

    private Task LeaveAsync()
    {
        if (!BeginSettlement())
            return Task.CompletedTask;
        Finish("LEAVE.description");
        return Task.CompletedTask;
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
