using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Events.Minigames;

namespace STS2_Things.Events;

/// <summary>
/// Depths multiplayer-exclusive shared event ("铜鼎争宝 / Bronze Tripod Scramble").
/// Only appears when Players.Count &gt; 1. Uses native shared-event voting:
/// Option 1 (Share): Every player peacefully receives 1 Common Relic, 45 Gold, and 1 Random Potion.
/// Option 2 (Scramble): All players enter a synchronized real-time cauldron grab-brawl with a
/// 3-2-1 countdown, reusing vanilla treasure-room Paper (open palm) and Rock (clenched fist) hands!
/// </summary>
public sealed class ThingsBronzeCauldron : ThingsEventModel
{
    public const int ShareGoldAmount = BronzeCauldronMinigame.ShareGoldAmount;

    private sealed class RoomSessionHolder
    {
        public int FloorStamp = -1;
        public BronzeCauldronMinigame? Session;
        public bool OverlayLaunched;
    }

    private static readonly ConditionalWeakTable<IRunState, RoomSessionHolder> RoomSessions = new();

    private bool _settling;
    private BronzeCauldronMinigame? _activeSession;

    /// <summary>
    /// Optional driver used exclusively by headless probes/tests when NOverlayStack is absent.
    /// </summary>
    internal Func<BronzeCauldronMinigame, Task>? MinigameTestDriver { get; set; }

    public override bool IsShared => true;
    public override bool IsDeterministic => false;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
        _activeSession = null;
        MinigameTestDriver = null;
    }

    protected override void OnEventFinished()
    {
        base.OnEventFinished();
        BronzeCauldronMinigame? session = _activeSession;
        _activeSession = null;
        if (session == null)
        {
            ThingsEventMinigameSync.ClearStaleCauldronBuffer();
            return;
        }

        if (!ThingsEventMinigameSync.IsRunActive)
        {
            session.CancelAllWaiters();
            ThingsEventMinigameSync.DetachCauldronSession(session);
            if (Owner?.RunState is { } runState &&
                RoomSessions.TryGetValue(runState, out RoomSessionHolder? holder) &&
                ReferenceEquals(holder.Session, session))
            {
                holder.Session = null;
                holder.OverlayLaunched = false;
            }
            return;
        }

        if (session.HasFinalizedOutcomes)
            ThingsEventMinigameSync.DetachCauldronSession(session);
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar("ShareGold", ShareGoldAmount),
        new IntVar("GrabCount", BronzeCauldronMinigame.GrabsPerPlayer),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 1 && runState.Players.Any(player => !player.Creature.IsDead);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SHARE", "SCRAMBLE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState)
    {
        var paths = new List<string>
        {
            "res://images/events/minigames/things_bronze_cauldron_bg.png",
            "res://images/events/crystal_sphere/crystal_sphere_relic.png",
            "res://images/events/minigames/things_loot_relic_rare_box.png",
            "res://images/events/crystal_sphere/crystal_sphere_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_big_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_rare_potion.png",
            "res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png",
            "res://images/powers/hammer_time_power.png",
            "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres",
            ThingsMinigameUiTheme.DivineButtonPath,
            ThingsMinigameUiTheme.DivineButtonOutlinePath,
            ThingsMinigameUiTheme.TinyNinePatchPath,
            "res://images/ui/hands/multiplayer_hand_ironclad_paper.png",
            "res://images/ui/hands/multiplayer_hand_ironclad_rock.png",
        };
        foreach (Player p in runState.Players)
        {
            string slug = p.Character.Id.Entry.ToLowerInvariant();
            paths.Add($"res://images/ui/hands/multiplayer_hand_{slug}_paper.png");
            paths.Add($"res://images/ui/hands/multiplayer_hand_{slug}_rock.png");
        }
        return base.GetAssetPaths(runState).Concat(paths).Distinct();
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new EventOption(this, ShareEquallyAsync, InitialOptionKey("SHARE")),
            new EventOption(this, ScrambleAsync, InitialOptionKey("SCRAMBLE")),
        ];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task ShareEquallyAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        var rewards = new List<Reward>();
        int goldTotal = ShareGoldAmount;
        if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
            rewards.Add(new RelicReward(RelicRarity.Common, player));
        else
            goldTotal += BronzeCauldronMinigame.ShareCommonRelicFallbackGold;

        rewards.Add(new GoldReward(goldTotal, goldTotal, player));

        await RewardsCmd.OfferCustom(player, rewards);
        Finish("SHARE.description");
    }

    private async Task ScrambleAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        IRunState runState = player.RunState;
        (BronzeCauldronMinigame session, bool launchUiHere) = AcquireSharedSession(runState);
        _activeSession = session;

        if (launchUiHere)
        {
            NBronzeCauldronScreen? screen = null;
            try
            {
                if (MinigameTestDriver != null)
                {
                    await MinigameTestDriver(session);
                    session.ConfirmUiExit();
                }
                else if (NOverlayStack.Instance != null)
                {
                    screen = NBronzeCauldronScreen.Create(session);
                    NOverlayStack.Instance.Push(screen);
                    await session.UiDismissedTask;
                }
                else
                {
                    session.ConfirmUiExit();
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
        }

        ThingsBronzeCauldronPlayerOutcome outcome;
        try
        {
            outcome = await session.WaitForPlayerOutcomeAsync(player.NetId);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ThingsEventMinigameSync.IsRunActive)
            return;

        await ApplyScrambleOutcomeAsync(player, outcome);
    }

    private (BronzeCauldronMinigame Session, bool LaunchUiHere) AcquireSharedSession(IRunState runState)
    {
        RoomSessionHolder holder = RoomSessions.GetOrCreateValue(runState);
        // CurrentActIndex occupies the hundreds digit (0..9) and Players.Count occupies 0..99,
        // so this floorStamp stays collision-free across all 1–64 protocol players without altering 1–4p behavior.
        int floorStamp = runState.TotalFloor * 1000 + runState.CurrentActIndex * 100 + runState.Players.Count;

        if (holder.Session == null || holder.FloorStamp != floorStamp)
        {
            bool isHost = RunManager.Instance?.NetService.Type != NetGameType.Client;
            List<Player> participants = runState.Players.Where(p => !p.Creature.IsDead).ToList();
            if (participants.Count == 0)
                participants = runState.Players.ToList();
            ulong localId = LocalContext.NetId ?? participants[0].NetId;
            uint sharedSeed = ComputeSharedCauldronSeed(runState);

            holder.FloorStamp = floorStamp;
            holder.Session = new BronzeCauldronMinigame(participants, localId, isHost, sharedSeed);
            holder.OverlayLaunched = false;
            ThingsEventMinigameSync.AttachCauldronSession(holder.Session);
        }

        bool shouldLaunch = false;
        bool localPlayerDead = runState.Players.Any(p => LocalContext.IsMe(p) && p.Creature.IsDead);
        if (!holder.OverlayLaunched && (LocalContext.IsMe(EventOwner) || (holder.Session.IsHostAuthority && localPlayerDead)))
        {
            holder.OverlayLaunched = true;
            shouldLaunch = true;
        }

        return (holder.Session, shouldLaunch);
    }

    internal static uint ComputeSharedCauldronSeed(IRunState runState)
    {
        unchecked
        {
            uint h = 2166136261u;
            string seedStr = runState.Rng.StringSeed ?? "things_cauldron";
            foreach (char ch in seedStr)
            {
                h ^= ch;
                h *= 16777619u;
            }
            h ^= (uint)(runState.TotalFloor * 7919 + runState.CurrentActIndex * 313 + runState.Players.Count * 17);
            return h == 0u ? 1337u : h;
        }
    }

    internal async Task ApplyScrambleOutcomeAsync(Player player, ThingsBronzeCauldronPlayerOutcome outcome)
    {
        int fallbackGold = 0;

        if (outcome.MaxHpGain > 0)
            await CreatureCmd.GainMaxHp(player.Creature, outcome.MaxHpGain);

        if (outcome.UpgradeCount > 0)
        {
            List<CardModel> upgradable = player.Deck.Cards.Where(c => c.IsUpgradable).ToList();
            for (int i = 0; i < outcome.UpgradeCount; i++)
            {
                if (upgradable.Count == 0)
                {
                    fallbackGold += BronzeCauldronMinigame.UpgradeFallbackGold;
                    continue;
                }
                int pick = Rng.NextInt(upgradable.Count);
                CardModel chosen = upgradable[pick];
                upgradable.RemoveAt(pick);
                CardCmd.Upgrade(chosen, CardPreviewStyle.EventLayout);
            }
        }

        var rewards = new List<Reward>();
        int goldTotal = outcome.GoldAmount + fallbackGold;

        for (int i = 0; i < outcome.RareRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
                rewards.Add(new RelicReward(RelicRarity.Rare, player));
            else
                goldTotal += BronzeCauldronMinigame.RelicFallbackGoldRare;
        }

        for (int i = 0; i < outcome.UncommonRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Uncommon))
                rewards.Add(new RelicReward(RelicRarity.Uncommon, player));
            else
                goldTotal += BronzeCauldronMinigame.RelicFallbackGoldUncommon;
        }

        for (int i = 0; i < outcome.CommonRelicCount; i++)
        {
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
                rewards.Add(new RelicReward(RelicRarity.Common, player));
            else
                goldTotal += BronzeCauldronMinigame.RelicFallbackGoldCommon;
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

        if (rewards.Count > 0)
            await RewardsCmd.OfferCustom(player, rewards);

        Finish(outcome.HasAnyLoot ? "SCRAMBLE_WIN.description" : "SCRAMBLE_EMPTY.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
