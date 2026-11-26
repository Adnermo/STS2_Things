using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;
using STS2_Things.Events.Minigames;

namespace STS2_Things.Events;

/// <summary>
/// Overgrowth (Act 1A) interactive rune-path weaving minigame event ("Overgrown Rune Loom / 古藤刻痕盘").
/// Select 1 Deck card, then trace a 1..4 tile orthogonal path on a 4x4 mossy rune slab to combine
/// 100% vanilla save-safe Upgrade / Enchantment / Forest Boon effects against interior thorn/sap costs;
/// or gather warm forest sap for a lossless Act 1 heal.
/// </summary>
public sealed class ThingsMossRuneLoom : ThingsEventModel
{
    public const int MaxSteps = RuneLoomMinigame.MaxPathLength;
    public const int GatherSapHeal = RuneLoomMinigame.FallbackGatherSapHeal;

    private bool _settling;
    private ThingsRuneLoomOutcome? _bufferedRemoteOutcome;
    private TaskCompletionSource<ThingsRuneLoomOutcome>? _remoteOutcomeWaiter;
    private Action? _remoteWaiterCancelRegistration;

    /// <summary>
    /// Optional driver used exclusively by headless probes/tests when NOverlayStack is absent.
    /// </summary>
    internal Func<RuneLoomMinigame, Task>? MinigameTestDriver { get; set; }

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
        new IntVar("MaxSteps", MaxSteps),
        new HealVar("SapHeal", GatherSapHeal),
        new LocalizedStringVar("Curse", static _ => new LocString("cards", "CLUMSY.title").GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && player.Deck.Cards.Any(RuneLoomMinigame.IsEligibleCard));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "WEAVE", "GATHER_SAP");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState).Concat(
        [
            "res://images/events/minigames/things_rune_loom_bg.png",
            "res://images/events/minigames/things_rune_tile_stone.png",
            "res://shaders/things_rune_sap_bed.gdshader",
            ThingsMinigameUiTheme.DivineButtonPath,
            ThingsMinigameUiTheme.DivineButtonOutlinePath,
            ThingsMinigameUiTheme.TinyNinePatchPath,
            "res://images/powers/hammer_time_power.png",
            "res://images/powers/regen_power.png",
            "res://images/ui/emote/heart.png",
            "res://images/powers/thorns_power.png",
            "res://images/powers/poison_power.png",
            "res://images/events/crystal_sphere/crystal_sphere_gold.png",
            "res://images/events/crystal_sphere/crystal_sphere_curse.png",
            ModelDb.Enchantment<Sharp>().IconPath,
            ModelDb.Enchantment<Vigorous>().IconPath,
            ModelDb.Enchantment<Nimble>().IconPath,
            ModelDb.Enchantment<Adroit>().IconPath,
            ModelDb.Enchantment<Steady>().IconPath,
            ModelDb.Enchantment<Swift>().IconPath,
            ModelDb.Enchantment<Clone>().IconPath,
            ModelDb.Enchantment<Sown>().IconPath,
        ]).Distinct();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool hasEligibleCard = EventOwner.Deck.Cards.Any(RuneLoomMinigame.IsEligibleCard);
        EventOption weaveOption = hasEligibleCard
            ? new EventOption(
                this,
                WeaveCardAsync,
                InitialOptionKey("WEAVE"),
                HoverTipFactory.FromCardWithCardHoverTips<Clumsy>())
            : new EventOption(this, null, InitialOptionKey("WEAVE_NO_CARD"));

        EventOption gatherSapOption = new(this, GatherSapAsync, InitialOptionKey("GATHER_SAP"));
        return [weaveOption, gatherSapOption];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private void CancelToInitial()
    {
        _settling = false;
        CancelPendingRemoteWaiter();
        if (EventOwner.Creature.IsDead)
            Finish("ABORTED.description");
        else
            SetEventState(InitialDescription, GenerateInitialOptions());
    }

    internal void ReceiveRemoteOutcome(ThingsRuneLoomOutcome outcome)
    {
        TaskCompletionSource<ThingsRuneLoomOutcome>? waiter = _remoteOutcomeWaiter;
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

        TaskCompletionSource<ThingsRuneLoomOutcome>? waiter = _remoteOutcomeWaiter;
        _remoteOutcomeWaiter = null;
        waiter?.TrySetCanceled();
    }

    private async Task WeaveCardAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (!player.Deck.Cards.Any(RuneLoomMinigame.IsEligibleCard))
        {
            CancelToInitial();
            return;
        }

        CardSelectorPrefs prefs = new(L10NLookup($"{Id.Entry}.selectPrompt"), 0, 1)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        CardModel? selected = (await CardSelectCmd.FromDeckGeneric(
            player,
            prefs,
            RuneLoomMinigame.IsEligibleCard)).FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !player.Deck.Cards.Contains(selected))
        {
            CancelToInitial();
            return;
        }

        int deckIndex = player.Deck.Cards.ToList().IndexOf(selected);
        ThingsRuneLoomOutcome outcome;

        if (LocalContext.IsMe(player))
        {
            // Use an isolated derived RNG for local-only loom generation so EventModel.Rng
            // is never mutated out of sync with remote peers.
            var minigame = new RuneLoomMinigame(player, selected, deckIndex, CreateLocalMinigameRng(player, deckIndex));
            NRuneLoomScreen? screen = null;
            try
            {
                if (MinigameTestDriver != null)
                {
                    await MinigameTestDriver(minigame);
                    if (!minigame.CompletionTask.IsCompleted)
                        minigame.Cancel();
                    outcome = await minigame.CompletionTask;
                }
                else if (NOverlayStack.Instance != null)
                {
                    screen = NRuneLoomScreen.Create(minigame);
                    NOverlayStack.Instance.Push(screen);
                    outcome = await minigame.CompletionTask;
                }
                else
                {
                    outcome = minigame.BuildOutcome(cancelled: true);
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

            ThingsEventMinigameSync.SendRuneLoomResult(outcome);
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

        if (outcome.Cancelled)
        {
            CancelToInitial();
            return;
        }

        await ApplyOutcomeAsync(player, selected, outcome);
    }

    private async Task<ThingsRuneLoomOutcome> WaitForRemoteOutcomeAsync()
    {
        if (_bufferedRemoteOutcome != null)
        {
            ThingsRuneLoomOutcome buffered = _bufferedRemoteOutcome;
            _bufferedRemoteOutcome = null;
            return buffered;
        }

        var waiter = new TaskCompletionSource<ThingsRuneLoomOutcome>();
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

    internal async Task ApplyOutcomeAsync(Player player, CardModel? selected, ThingsRuneLoomOutcome outcome)
    {
        if (selected == null && outcome.DeckCardIndex >= 0 && outcome.DeckCardIndex < player.Deck.Cards.Count)
            selected = player.Deck.Cards[outcome.DeckCardIndex];

        // 1. Pay path costs first so death or resource drain resolves cleanly.
        if (outcome.GoldLoss > 0)
        {
            int actualGoldLoss = Math.Min(outcome.GoldLoss, player.Gold);
            if (actualGoldLoss > 0)
                await PlayerCmd.LoseGold(actualGoldLoss, player, GoldLossType.Spent);
        }

        if (outcome.MaxHpLoss > 0)
        {
            int maxAllowedLoss = Math.Max(0, player.Creature.MaxHp - 1);
            int actualMaxHpLoss = Math.Min(outcome.MaxHpLoss, maxAllowedLoss);
            if (actualMaxHpLoss > 0)
                await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), actualMaxHpLoss);
        }

        if (outcome.DamageAmount > 0)
        {
            await ThingsEventHelpers.DealUnblockableDamageAsync(player, outcome.DamageAmount);
            if (player.Creature.IsDead)
            {
                Finish("WEAVE.loss");
                return;
            }
        }

        for (int i = 0; i < outcome.CurseCount; i++)
            await CardPileCmd.AddCurseToDeck<Clumsy>(player);

        // 2. Apply save-safe card upgrade & vanilla enchantment.
        bool modifiedCard = false;
        if (selected != null && player.Deck.Cards.Contains(selected))
        {
            if (outcome.UpgradeCard && selected.IsUpgradable)
            {
                CardCmd.Upgrade(selected, CardPreviewStyle.EventLayout);
                modifiedCard = true;
            }

            if (outcome.EnchantmentKind != RuneLoomEnchantmentKind.None && outcome.EnchantmentAmount > 0)
            {
                if (TryApplyEnchantment(selected, outcome.EnchantmentKind, outcome.EnchantmentAmount))
                {
                    CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
                    modifiedCard = true;
                }
            }
        }

        // 3. Apply Overgrowth forest boons.
        if (outcome.MaxHpGain > 0)
            await CreatureCmd.GainMaxHp(player.Creature, outcome.MaxHpGain);
        if (outcome.HealAmount > 0)
            await CreatureCmd.Heal(player.Creature, outcome.HealAmount);
        if (outcome.GoldGain > 0)
            await PlayerCmd.GainGold(outcome.GoldGain, player);

        if (outcome.HasAnyCost)
            Finish("WEAVE_BLOOD.description");
        else if (modifiedCard || outcome.HasAnyGain)
            Finish("WEAVE_CLEAN.description");
        else
            Finish("WEAVE_MOSS.description");
    }

    internal static bool TryApplyEnchantment(CardModel card, RuneLoomEnchantmentKind kind, int amount)
    {
        EnchantmentModel? canonical = RuneLoomMinigame.GetCanonicalEnchantment(kind);
        if (canonical == null || !canonical.CanEnchant(card))
            return false;

        switch (kind)
        {
            case RuneLoomEnchantmentKind.Sharp:
                CardCmd.Enchant<Sharp>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Vigorous:
                CardCmd.Enchant<Vigorous>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Nimble:
                CardCmd.Enchant<Nimble>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Adroit:
                CardCmd.Enchant<Adroit>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Steady:
                CardCmd.Enchant<Steady>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Swift:
                CardCmd.Enchant<Swift>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Clone:
                CardCmd.Enchant<Clone>(card, amount);
                return true;
            case RuneLoomEnchantmentKind.Sown:
                CardCmd.Enchant<Sown>(card, amount);
                return true;
            default:
                return false;
        }
    }

    private async Task GatherSapAsync()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Heal(EventOwner.Creature, GatherSapHeal);
        Finish("GATHER_SAP.description");
    }

    private static Rng CreateLocalMinigameRng(Player player, int deckIndex)
    {
        IRunState runState = player.RunState;
        unchecked
        {
            ulong h = 14695981039346656037UL;
            string seedStr = runState.Rng.StringSeed ?? "things_moss_rune_loom";
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
            h ^= (ulong)(uint)deckIndex * 131UL;
            return new Rng(h == 0UL ? 1337UL : h, "things_moss_rune_loom");
        }
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
