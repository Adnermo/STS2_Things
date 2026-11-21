using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Events.Minigames;

namespace STS2_Things.Events;

/// <summary>
/// Overgrowth (Act 1A) Briar Plinko minigame event ("古林落籽盘 / Overgrown Plinko").
/// Offers three HP-cost tiers for 1, 2, or 3 amber seed drops plus a clean zero-reward Leave option.
/// Drops bounce through thorny Overgrowth pegs and bonus glow-shroom bumpers into 7 root-cup bins.
/// </summary>
public sealed class ThingsForestPlinko : ThingsEventModel
{
    public const int Tier1HpCost = ForestPlinkoMinigame.Tier1HpCost;
    public const int Tier1Drops = ForestPlinkoMinigame.Tier1Drops;
    public const int Tier2HpCost = ForestPlinkoMinigame.Tier2HpCost;
    public const int Tier2Drops = ForestPlinkoMinigame.Tier2Drops;
    public const int Tier3HpCost = ForestPlinkoMinigame.Tier3HpCost;
    public const int Tier3Drops = ForestPlinkoMinigame.Tier3Drops;

    private bool _settling;
    private ThingsForestPlinkoOutcome? _bufferedRemoteOutcome;
    private TaskCompletionSource<ThingsForestPlinkoOutcome>? _remoteOutcomeWaiter;

    /// <summary>
    /// Optional driver used exclusively by headless probes/tests when NOverlayStack is absent.
    /// </summary>
    internal Func<ForestPlinkoMinigame, Task>? MinigameTestDriver { get; set; }

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
        new DamageVar("Tier1Hp", Tier1HpCost, ValueProp.Unblockable | ValueProp.Unpowered),
        new IntVar("Tier1Drops", Tier1Drops),
        new DamageVar("Tier2Hp", Tier2HpCost, ValueProp.Unblockable | ValueProp.Unpowered),
        new IntVar("Tier2Drops", Tier2Drops),
        new DamageVar("Tier3Hp", Tier3HpCost, ValueProp.Unblockable | ValueProp.Unpowered),
        new IntVar("Tier3Drops", Tier3Drops),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > Tier3HpCost);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "DROP_1", "DROP_2", "DROP_3", "LEAVE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState).Concat(
        [
            "res://images/events/minigames/things_forest_plinko_bg.png",
            "res://images/events/minigames/things_plinko_seed.png",
            "res://images/events/minigames/things_plinko_peg.png",
            "res://images/events/minigames/things_plinko_shroom.png",
            "res://images/events/minigames/things_plinko_bowl.png",
            "res://images/events/crystal_sphere/crystal_sphere_relic.png",
            "res://images/events/minigames/things_loot_relic_rare_box.png",
            "res://images/events/crystal_sphere/crystal_sphere_rare_potion.png",
            "res://images/events/crystal_sphere/crystal_sphere_big_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png",
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres",
            "res://images/powers/regen_power.png",
            "res://images/powers/hammer_time_power.png",
            "res://shaders/things_rune_sap_bed.gdshader",
            ThingsMinigameUiTheme.DivineButtonPath,
            ThingsMinigameUiTheme.DivineButtonOutlinePath,
            ThingsMinigameUiTheme.TinyNinePatchPath,
        ]).Distinct();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new EventOption(this, Drop1Async, InitialOptionKey("DROP_1")),
            new EventOption(this, Drop2Async, InitialOptionKey("DROP_2")),
            new EventOption(this, Drop3Async, InitialOptionKey("DROP_3")),
            new EventOption(this, LeaveAsync, InitialOptionKey("LEAVE")),
        ];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    internal void ReceiveRemoteOutcome(ThingsForestPlinkoOutcome outcome)
    {
        if (_remoteOutcomeWaiter != null)
        {
            _remoteOutcomeWaiter.TrySetResult(outcome);
            return;
        }
        _bufferedRemoteOutcome = outcome;
    }

    private Task Drop1Async() => RunPlinkoTierAsync(Tier1HpCost, Tier1Drops);
    private Task Drop2Async() => RunPlinkoTierAsync(Tier2HpCost, Tier2Drops);
    private Task Drop3Async() => RunPlinkoTierAsync(Tier3HpCost, Tier3Drops);

    private async Task RunPlinkoTierAsync(int hpCost, int drops)
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, hpCost);
        if (player.Creature.IsDead)
        {
            Finish("DROP.loss");
            return;
        }

        ThingsForestPlinkoOutcome outcome;
        if (LocalContext.IsMe(player))
        {
            var minigame = new ForestPlinkoMinigame(player, Rng, drops);
            NForestPlinkoScreen? screen = null;
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
                    screen = NForestPlinkoScreen.Create(minigame);
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

            ThingsEventMinigameSync.SendForestPlinkoResult(outcome);
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

    private Task<ThingsForestPlinkoOutcome> WaitForRemoteOutcomeAsync()
    {
        if (_bufferedRemoteOutcome != null)
        {
            ThingsForestPlinkoOutcome buffered = _bufferedRemoteOutcome;
            _bufferedRemoteOutcome = null;
            return Task.FromResult(buffered);
        }
        _remoteOutcomeWaiter = new TaskCompletionSource<ThingsForestPlinkoOutcome>();
        return _remoteOutcomeWaiter.Task;
    }

    internal async Task ApplyOutcomeAsync(Player player, ThingsForestPlinkoOutcome outcome)
    {
        int fallbackGold = 0;

        if (outcome.MaxHpGain > 0)
            await CreatureCmd.GainMaxHp(player.Creature, outcome.MaxHpGain);

        if (outcome.HealAmount > 0)
            await CreatureCmd.Heal(player.Creature, outcome.HealAmount);

        if (outcome.UpgradeCount > 0)
        {
            List<CardModel> upgradable = player.Deck.Cards.Where(c => c.IsUpgradable).ToList();
            for (int i = 0; i < outcome.UpgradeCount; i++)
            {
                if (upgradable.Count == 0)
                {
                    fallbackGold += ForestPlinkoMinigame.BinGoldBigAmount;
                    continue;
                }
                int pick = Rng.NextInt(upgradable.Count);
                CardModel chosen = upgradable[pick];
                upgradable.RemoveAt(pick);
                CardCmd.Upgrade(chosen, CardPreviewStyle.EventLayout);
            }
        }

        List<Reward> rewards = BuildRewards(player, outcome, fallbackGold);
        if (rewards.Count > 0)
            await RewardsCmd.OfferCustom(player, rewards);

        Finish(outcome.HasAnyLoot ? "PLINKO_HAUL.description" : "PLINKO_EMPTY.description");
    }

    private static List<Reward> BuildRewards(Player player, ThingsForestPlinkoOutcome outcome, int extraFallbackGold)
    {
        var rewards = new List<Reward>();
        int goldTotal = outcome.GoldAmount + extraFallbackGold;

        for (int i = 0; i < outcome.RareRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
                rewards.Add(new RelicReward(RelicRarity.Rare, player));
            else
                goldTotal += ForestPlinkoMinigame.BinGoldBigAmount * 2;
        }

        for (int i = 0; i < outcome.CommonRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
                rewards.Add(new RelicReward(RelicRarity.Common, player));
            else
                goldTotal += ForestPlinkoMinigame.BinGoldBigAmount;
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
