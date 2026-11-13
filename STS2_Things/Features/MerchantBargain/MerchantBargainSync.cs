#if STS2_V113
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using System.Runtime.CompilerServices;

namespace STS2_Things.Features.MerchantBargain;

/// <summary>
/// Announces a negotiated price before the native, reliable purchase message.
/// The native location buffer preserves ordering when another peer is still loading.
/// Inventory mutation, gold spending, restocking and purchase hooks stay native.
/// </summary>
public struct MerchantBargainPriceMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public uint EntryId;
    public int Price;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteUInt(EntryId, 8);
        writer.WriteInt(Price);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        EntryId = reader.ReadUInt(8);
        Price = reader.ReadInt();
    }
}

internal static class MerchantBargainSync
{
    private sealed class State(MerchantSynchronizer synchronizer, INetGameService service,
        IRunState runState, RunLocationTargetedMessageBuffer buffer)
    {
        public readonly INetGameService Service = service;
        public readonly RunLocationTargetedMessageBuffer Buffer = buffer;

        public void Receive(MerchantBargainPriceMessage message, ulong senderId)
        {
            var player = runState.Players.FirstOrDefault(p => p.NetId == senderId);
            if (player is null || runState.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.MerchantRoom)
                return;
            var entry = synchronizer.GetInventoryFor(player).AllEntries
                .FirstOrDefault(e => e.Id == message.EntryId);
            if (entry is null || !entry.IsStocked || message.Price < 0 ||
                message.Price > entry.Cost || message.Price > player.Gold)
                return;
            MerchantBargainManager.SetPriceOverride(entry, message.Price);
        }
    }

    private static readonly ConditionalWeakTable<MerchantSynchronizer, State> States = new();

    internal static void Bind(MerchantSynchronizer synchronizer, INetGameService service,
        IRunState runState, RunLocationTargetedMessageBuffer buffer)
    {
        var state = new State(synchronizer, service, runState, buffer);
        States.Add(synchronizer, state);
        buffer.RegisterMessageHandler<MerchantBargainPriceMessage>(state.Receive);
    }

    internal static void Unbind(MerchantSynchronizer synchronizer)
    {
        if (!States.TryGetValue(synchronizer, out var state)) return;
        state.Buffer.UnregisterMessageHandler<MerchantBargainPriceMessage>(state.Receive);
        States.Remove(synchronizer);
    }

    internal static void AnnouncePrice(MerchantSynchronizer synchronizer, MerchantEntry entry, int price)
    {
        if (!entry.Id.HasValue || !States.TryGetValue(synchronizer, out var state))
            throw new InvalidOperationException("Native merchant synchronization is not initialized.");
        state.Service.SendMessage(new MerchantBargainPriceMessage
        {
            Location = state.Buffer.CurrentLocation,
            EntryId = entry.Id.Value,
            Price = price
        });
    }
}

[HarmonyPatch(typeof(MerchantSynchronizer), MethodType.Constructor,
    [typeof(INetGameService), typeof(IRunState), typeof(RunLocationTargetedMessageBuffer)])]
internal static class MerchantBargainSyncBindPatch
{
    private static void Postfix(MerchantSynchronizer __instance, INetGameService netService,
        IRunState runState, RunLocationTargetedMessageBuffer locationBuffer)
        => MerchantBargainSync.Bind(__instance, netService, runState, locationBuffer);
}

[HarmonyPatch(typeof(MerchantSynchronizer), nameof(MerchantSynchronizer.Dispose))]
internal static class MerchantBargainSyncUnbindPatch
{
    private static void Prefix(MerchantSynchronizer __instance) => MerchantBargainSync.Unbind(__instance);
}

[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseUnsynchronized))]
internal static class MerchantBargainNativePurchaseCleanupPatch
{
    private static void Postfix(MerchantEntry __instance, ref Task<bool> __result)
        => __result = Complete(__instance, __result);

    private static async Task<bool> Complete(MerchantEntry entry, Task<bool> purchase)
    {
        try { return await purchase; }
        finally { MerchantBargainManager.ClearPriceOverride(entry); }
    }
}
#endif
