using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Config;
using STS2_Things.Platform;

namespace STS2_Things.Features.MerchantBargain;

internal static class MerchantBargainManager
{
    private sealed class InventoryState
    {
        public MerchantBargainSession<MerchantEntry> Session { get; } = new();
    }

    private sealed class PriceOverride
    {
        public required int Price { get; init; }
    }

    private static readonly ConditionalWeakTable<MerchantInventory, InventoryState> InventoryStates = new();
    private static readonly ConditionalWeakTable<MerchantEntry, PriceOverride> PriceOverrides = new();

    internal static bool TryIntercept(
        MerchantEntry entry,
        MerchantInventory? inventory,
        bool ignoreCost,
        out Task<bool> result)
    {
        result = Task.FromResult(false);
        if (!ThingsModConfig.IsEnabled(ThingsModConfig.FeatureMerchantBargainEnabled) ||
            ignoreCost || inventory is null)
        {
            return false;
        }

        if (InventoryStates.TryGetValue(inventory, out InventoryState? activeState) &&
            activeState.Session.FightInProgress &&
            !TryGetPriceOverride(entry, out _))
        {
            return true;
        }

        if (entry is MerchantCardRemovalEntry ||
#if STS2_V113_1
            entry is BlitzMerchantEntry ||
#endif
            TryGetPriceOverride(entry, out _))
        {
            return false;
        }

        Player player = inventory.Player;
        if (player is null || Sts2VersionSeam.IsBlitz(player.RunState))
        {
            return false;
        }
        NMerchantInventory? merchantUi = NMerchantRoom.Instance?.Inventory;
        if (player.RunState.CurrentRoom is not MerchantRoom ||
            merchantUi is null || !merchantUi.IsOpen || merchantUi.Inventory != inventory)
        {
            return false;
        }

        InventoryState state = InventoryStates.GetValue(inventory, static _ => new InventoryState());
        int price = entry.Cost;
        MerchantBargainAttemptDisposition disposition = state.Session.RegisterAttempt(
            entry,
            entry.IsStocked,
            MerchantBargainRules.IsEligibleShortfall(price, player.Gold));
        switch (disposition)
        {
            case MerchantBargainAttemptDisposition.PassThrough:
                return false;
            case MerchantBargainAttemptDisposition.Suppress:
                return true;
            case MerchantBargainAttemptDisposition.PurchaseAtNegotiatedPrice:
                int negotiatedPrice = state.Session.WinningPrice
                    ?? throw new InvalidOperationException(
                        "A merchant bargain winning entry has no negotiated price.");
                result = PurchaseAtNegotiatedPriceAsync(
                    entry,
                    inventory,
                    state,
                    negotiatedPrice);
                return true;
            case MerchantBargainAttemptDisposition.StartFight:
                result = ResolveFightAndPurchaseAsync(entry, inventory, merchantUi, state);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
        }
    }

    internal static bool TryGetPriceOverride(MerchantEntry entry, out int price)
    {
        if (ThingsModConfig.IsEnabled(ThingsModConfig.FeatureMerchantBargainEnabled) &&
            PriceOverrides.TryGetValue(entry, out PriceOverride? priceOverride))
        {
            price = priceOverride.Price;
            return true;
        }

        price = 0;
        return false;
    }

    private static bool IsMerchantContextAlive(NMerchantInventory merchantUi, Player player) =>
        GodotObject.IsInstanceValid(merchantUi) &&
        merchantUi.IsInsideTree() &&
        RunManager.Instance is { IsInProgress: true, IsCleaningUp: false } &&
        player.RunState.CurrentRoom is MerchantRoom;

    private static async Task<bool> ResolveFightAndPurchaseAsync(
        MerchantEntry entry,
        MerchantInventory inventory,
        NMerchantInventory merchantUi,
        InventoryState state)
    {
        NMerchantBargainRps? overlay = null;
        merchantUi.BlockInput();
        try
        {
            Player player = inventory.Player;
            if (!IsMerchantContextAlive(merchantUi, player) || merchantUi.GetTree() is not { } tree)
            {
                state.Session.ResetFightAfterError(entry);
                return false;
            }

            merchantUi.MerchantHand.StopPointing(0f);
            await tree
                .CreateTimer(0.7)
                .AwaitSignal(SceneTreeTimer.SignalName.Timeout, merchantUi);

            if (!IsMerchantContextAlive(merchantUi, player))
            {
                state.Session.ResetFightAfterError(entry);
                return false;
            }

            overlay = NMerchantBargainRps.Create(player, merchantUi.GetViewportRect().Size);
            merchantUi.AddChild(overlay);
            if (!await overlay.PlayEntrance() || !IsMerchantContextAlive(merchantUi, player))
            {
                state.Session.ResetFightAfterError(entry);
                return false;
            }

            Rng rng = CreateFightRng(player);
            MerchantBargainRoundResult roundResult;
            do
            {
                RelicPickingFightMove playerMove = (RelicPickingFightMove)rng.NextInt(3);
                RelicPickingFightMove merchantMove = (RelicPickingFightMove)rng.NextInt(3);
                if (!await overlay.PlayRound(playerMove, merchantMove) || !IsMerchantContextAlive(merchantUi, player))
                {
                    state.Session.ResetFightAfterError(entry);
                    return false;
                }
                roundResult = MerchantBargainRules.EvaluateRound(playerMove, merchantMove);
                if (roundResult == MerchantBargainRoundResult.Tie)
                {
                    if (!await overlay.PlayTie() || !IsMerchantContextAlive(merchantUi, player))
                    {
                        state.Session.ResetFightAfterError(entry);
                        return false;
                    }
                }
            }
            while (roundResult == MerchantBargainRoundResult.Tie);

            bool playerWon = roundResult == MerchantBargainRoundResult.PlayerWon;
            if (!await overlay.PlayOutcome(playerWon) || !IsMerchantContextAlive(merchantUi, player))
            {
                state.Session.ResetFightAfterError(entry);
                return false;
            }
            if (!await overlay.PlayExit() || !IsMerchantContextAlive(merchantUi, player))
            {
                state.Session.ResetFightAfterError(entry);
                return false;
            }
            if (!playerWon)
            {
                state.Session.MarkFightLost();
                entry.InvokePurchaseFailed(PurchaseStatus.FailureGold);
                return false;
            }

            int negotiatedPrice = player.Gold;
            state.Session.MarkFightWon(entry, negotiatedPrice);
            return await PurchaseAtNegotiatedPriceAsync(
                entry,
                inventory,
                state,
                negotiatedPrice);
        }
        catch (Exception exception)
        {
            state.Session.ResetFightAfterError(entry);
            Log.Error($"[STS2_Things] Merchant bargain fight failed: {exception}");
            if (IsMerchantContextAlive(merchantUi, inventory.Player))
            {
                entry.InvokePurchaseFailed(PurchaseStatus.FailureGold);
            }
            return false;
        }
        finally
        {
            state.Session.EndFight();
            if (overlay != null && GodotObject.IsInstanceValid(overlay))
            {
                overlay.QueueFree();
            }
            if (GodotObject.IsInstanceValid(merchantUi) && merchantUi.IsInsideTree())
            {
                merchantUi.UnblockInput();
            }
        }
    }

    private static async Task<bool> PurchaseAtNegotiatedPriceAsync(
        MerchantEntry entry,
        MerchantInventory inventory,
        InventoryState state,
        int negotiatedPrice)
    {
        SetPriceOverride(entry, negotiatedPrice);
        bool success = false;
        try
        {
#if STS2_V113_1
            var synchronizer = RunManager.Instance.MerchantSynchronizer;
            bool announced = false;
            try
            {
                MerchantBargainSync.AnnouncePrice(synchronizer, entry, negotiatedPrice);
                announced = true;
                success = await synchronizer.PurchaseLocalEntry(entry);
                if (!success)
                {
                    MerchantBargainSync.ClearAnnouncedPrice(synchronizer, entry);
                    announced = false;
                }
            }
            catch (Exception)
            {
                if (announced)
                {
                    MerchantBargainSync.ClearAnnouncedPrice(synchronizer, entry);
                }
                throw;
            }
#else
            success = await entry.OnTryPurchaseWrapper(inventory);
#endif
            if (success)
            {
                state.Session.MarkPurchaseSucceeded();
            }

            return success;
        }
        finally
        {
            PriceOverrides.Remove(entry);
            entry.OnMerchantInventoryUpdated();
        }
    }

    internal static void ClearPriceOverride(MerchantEntry entry) => PriceOverrides.Remove(entry);

    internal static void SetPriceOverride(MerchantEntry entry, int price)
    {
        PriceOverrides.Remove(entry);
        PriceOverrides.Add(entry, new PriceOverride { Price = price });
    }

    private static Rng CreateFightRng(Player player)
    {
        ulong floor = (ulong)Math.Max(0, player.RunState.TotalFloor);
        ulong playerSlot = (ulong)Math.Max(0, player.RunState.GetPlayerSlotIndex(player));
        ulong seed = unchecked(
            player.PlayerRng.Seed ^
            ((floor + 1UL) * 0x9E3779B97F4A7C15UL) ^
            ((playerSlot + 1UL) * 0xBF58476D1CE4E5B9UL));
        return new Rng(seed, "sts2_things_merchant_bargain");
    }
}
