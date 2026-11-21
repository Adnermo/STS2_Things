using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Reliable multiplayer message synchronizing the local player's Underdocks Bilge Grate
/// minesweeper outcome to remote peers so damage, rewards, and event completion stay in lockstep.
/// Compatible with both v111.1 and v113.1 via native OneOffSynchronizer.
/// </summary>
public struct ThingsBilgeSweepResultMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public bool HitMine;
    public int GoldAmount;
    public int PotionCount;
    public int CardCount;
    public int CommonRelicCount;
    public int ProbesUsed;
    public bool CashedOut;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteBool(HitMine);
        writer.WriteInt(GoldAmount);
        writer.WriteInt(PotionCount);
        writer.WriteInt(CardCount);
        writer.WriteInt(CommonRelicCount);
        writer.WriteInt(ProbesUsed);
        writer.WriteBool(CashedOut);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        HitMine = reader.ReadBool();
        GoldAmount = reader.ReadInt();
        PotionCount = reader.ReadInt();
        CardCount = reader.ReadInt();
        CommonRelicCount = reader.ReadInt();
        ProbesUsed = reader.ReadInt();
        CashedOut = reader.ReadBool();
    }

    public ThingsBilgeSweepOutcome ToSanitizedOutcome() => new(
        HitMine,
        Math.Clamp(GoldAmount, 0, 200),
        Math.Clamp(PotionCount, 0, 2),
        Math.Clamp(CardCount, 0, 2),
        Math.Clamp(CommonRelicCount, 0, 1),
        Math.Clamp(ProbesUsed, 0, 12),
        CashedOut);
}

/// <summary>
/// Reliable multiplayer message synchronizing the local player's Overgrowth Rune Loom
/// traced path outcome (including clean cancellation back to initial options) to remote peers.
/// </summary>
public struct ThingsRuneLoomResultMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public bool Cancelled;
    public int DeckCardIndex;
    public bool UpgradeCard;
    public byte EnchantmentKind;
    public int EnchantmentAmount;
    public int HealAmount;
    public int GoldGain;
    public int MaxHpGain;
    public int DamageAmount;
    public int GoldLoss;
    public int MaxHpLoss;
    public int CurseCount;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteBool(Cancelled);
        writer.WriteInt(DeckCardIndex);
        writer.WriteBool(UpgradeCard);
        writer.WriteByte(EnchantmentKind);
        writer.WriteInt(EnchantmentAmount);
        writer.WriteInt(HealAmount);
        writer.WriteInt(GoldGain);
        writer.WriteInt(MaxHpGain);
        writer.WriteInt(DamageAmount);
        writer.WriteInt(GoldLoss);
        writer.WriteInt(MaxHpLoss);
        writer.WriteInt(CurseCount);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        Cancelled = reader.ReadBool();
        DeckCardIndex = reader.ReadInt();
        UpgradeCard = reader.ReadBool();
        EnchantmentKind = reader.ReadByte();
        EnchantmentAmount = reader.ReadInt();
        HealAmount = reader.ReadInt();
        GoldGain = reader.ReadInt();
        MaxHpGain = reader.ReadInt();
        DamageAmount = reader.ReadInt();
        GoldLoss = reader.ReadInt();
        MaxHpLoss = reader.ReadInt();
        CurseCount = reader.ReadInt();
    }

    public ThingsRuneLoomOutcome ToSanitizedOutcome()
    {
        RuneLoomEnchantmentKind kind = Enum.IsDefined(typeof(RuneLoomEnchantmentKind), EnchantmentKind)
            ? (RuneLoomEnchantmentKind)EnchantmentKind
            : RuneLoomEnchantmentKind.None;

        return new ThingsRuneLoomOutcome(
            Cancelled,
            DeckCardIndex,
            UpgradeCard,
            kind,
            Math.Clamp(EnchantmentAmount, 0, 10),
            Math.Clamp(HealAmount, 0, 40),
            Math.Clamp(GoldGain, 0, 120),
            Math.Clamp(MaxHpGain, 0, 12),
            Math.Clamp(DamageAmount, 0, 40),
            Math.Clamp(GoldLoss, 0, 120),
            Math.Clamp(MaxHpLoss, 0, 16),
            Math.Clamp(CurseCount, 0, 2));
    }
}

/// <summary>
/// Reliable multiplayer message synchronizing the local player's Underdocks Sewer Clam Claw Machine
/// outcome to remote peers so loot rewards and event completion stay in lockstep.
/// </summary>
public struct ThingsClamClawResultMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public int GoldAmount;
    public int PotionCount;
    public int CardCount;
    public int CommonRelicCount;
    public int RareRelicCount;
    public int GrabsUsed;
    public int SlippedCount;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteInt(GoldAmount);
        writer.WriteInt(PotionCount);
        writer.WriteInt(CardCount);
        writer.WriteInt(CommonRelicCount);
        writer.WriteInt(RareRelicCount);
        writer.WriteInt(GrabsUsed);
        writer.WriteInt(SlippedCount);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        GoldAmount = reader.ReadInt();
        PotionCount = reader.ReadInt();
        CardCount = reader.ReadInt();
        CommonRelicCount = reader.ReadInt();
        RareRelicCount = reader.ReadInt();
        GrabsUsed = reader.ReadInt();
        SlippedCount = reader.ReadInt();
    }

    public ThingsClamClawOutcome ToSanitizedOutcome() => new(
        Math.Clamp(GoldAmount, 0, 200),
        Math.Clamp(PotionCount, 0, 3),
        Math.Clamp(CardCount, 0, 3),
        Math.Clamp(CommonRelicCount, 0, 1),
        Math.Clamp(RareRelicCount, 0, 1),
        Math.Clamp(GrabsUsed, 0, 3),
        Math.Clamp(SlippedCount, 0, 3));
}

/// <summary>
/// Reliable multiplayer message synchronizing the local player's Overgrowth Briar Plinko
/// outcome to remote peers so heals, upgrades, max HP, rewards, and completion stay in lockstep.
/// </summary>
public struct ThingsForestPlinkoResultMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public int GoldAmount;
    public int HealAmount;
    public int MaxHpGain;
    public int PotionCount;
    public int CardCount;
    public int CommonRelicCount;
    public int RareRelicCount;
    public int UpgradeCount;
    public int DropsUsed;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteInt(GoldAmount);
        writer.WriteInt(HealAmount);
        writer.WriteInt(MaxHpGain);
        writer.WriteInt(PotionCount);
        writer.WriteInt(CardCount);
        writer.WriteInt(CommonRelicCount);
        writer.WriteInt(RareRelicCount);
        writer.WriteInt(UpgradeCount);
        writer.WriteInt(DropsUsed);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        GoldAmount = reader.ReadInt();
        HealAmount = reader.ReadInt();
        MaxHpGain = reader.ReadInt();
        PotionCount = reader.ReadInt();
        CardCount = reader.ReadInt();
        CommonRelicCount = reader.ReadInt();
        RareRelicCount = reader.ReadInt();
        UpgradeCount = reader.ReadInt();
        DropsUsed = reader.ReadInt();
    }

    public ThingsForestPlinkoOutcome ToSanitizedOutcome() => new(
        Math.Clamp(GoldAmount, 0, 200),
        Math.Clamp(HealAmount, 0, 20),
        Math.Clamp(MaxHpGain, 0, 12),
        Math.Clamp(PotionCount, 0, 3),
        Math.Clamp(CardCount, 0, 3),
        Math.Clamp(CommonRelicCount, 0, 1),
        Math.Clamp(RareRelicCount, 0, 1),
        Math.Clamp(UpgradeCount, 0, 1),
        Math.Clamp(DropsUsed, 0, 3));
}

/// <summary>
/// Unreliable 20Hz cursor/hand sync during Depths Bronze Tripod Scramble so teammates see each
/// other's open hands hovering over the cauldron in real time.
/// </summary>
public struct ThingsCauldronHandMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public float HandX;
    public float HandY;
    public bool IsClenched;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Unreliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteFloat(HandX);
        writer.WriteFloat(HandY);
        writer.WriteBool(IsClenched);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        HandX = reader.ReadFloat();
        HandY = reader.ReadFloat();
        IsClenched = reader.ReadBool();
    }
}

/// <summary>
/// Client-to-Host reliable grab request during Depths Bronze Tripod Scramble.
/// </summary>
public struct ThingsCauldronGrabRequestMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public float GrabX;
    public float GrabY;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteFloat(GrabX);
        writer.WriteFloat(GrabY);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        GrabX = reader.ReadFloat();
        GrabY = reader.ReadFloat();
    }
}

/// <summary>
/// Host-authoritative reliable grab acknowledgement broadcasting which exact items a player claimed.
/// </summary>
public struct ThingsCauldronGrabAckMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public ulong GrabberNetId;
    public float GrabX;
    public float GrabY;
    public ulong ClaimedBitmask;
    public int GrabsRemaining;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteULong(GrabberNetId);
        writer.WriteFloat(GrabX);
        writer.WriteFloat(GrabY);
        writer.WriteULong(ClaimedBitmask);
        writer.WriteInt(GrabsRemaining);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        GrabberNetId = reader.ReadULong();
        GrabX = reader.ReadFloat();
        GrabY = reader.ReadFloat();
        ClaimedBitmask = reader.ReadULong();
        GrabsRemaining = reader.ReadInt();
    }
}

/// <summary>
/// Host-authoritative reliable final outcome table for every player in the Bronze Tripod Scramble
/// so all peers award identical rewards and pass room-exit checksums cleanly.
/// </summary>
public struct ThingsCauldronFinalOutcomeMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public int PlayerCount;
    public ulong[] NetIds;
    public int[] GoldAmounts;
    public int[] MaxHpGains;
    public int[] UpgradeCounts;
    public int[] PotionCounts;
    public int[] CardCounts;
    public int[] CommonRelicCounts;
    public int[] UncommonRelicCounts;
    public int[] RareRelicCounts;
    public int[] ItemsGrabbedCounts;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        int count = Math.Clamp(PlayerCount, 0, 8);
        writer.WriteInt(count);
        for (int i = 0; i < count; i++)
        {
            writer.WriteULong(NetIds[i]);
            writer.WriteInt(GoldAmounts[i]);
            writer.WriteInt(MaxHpGains[i]);
            writer.WriteInt(UpgradeCounts[i]);
            writer.WriteInt(PotionCounts[i]);
            writer.WriteInt(CardCounts[i]);
            writer.WriteInt(CommonRelicCounts[i]);
            writer.WriteInt(UncommonRelicCounts[i]);
            writer.WriteInt(RareRelicCounts[i]);
            writer.WriteInt(ItemsGrabbedCounts[i]);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        int count = Math.Clamp(reader.ReadInt(), 0, 8);
        PlayerCount = count;
        NetIds = new ulong[count];
        GoldAmounts = new int[count];
        MaxHpGains = new int[count];
        UpgradeCounts = new int[count];
        PotionCounts = new int[count];
        CardCounts = new int[count];
        CommonRelicCounts = new int[count];
        UncommonRelicCounts = new int[count];
        RareRelicCounts = new int[count];
        ItemsGrabbedCounts = new int[count];
        for (int i = 0; i < count; i++)
        {
            NetIds[i] = reader.ReadULong();
            GoldAmounts[i] = reader.ReadInt();
            MaxHpGains[i] = reader.ReadInt();
            UpgradeCounts[i] = reader.ReadInt();
            PotionCounts[i] = reader.ReadInt();
            CardCounts[i] = reader.ReadInt();
            CommonRelicCounts[i] = reader.ReadInt();
            UncommonRelicCounts[i] = reader.ReadInt();
            RareRelicCounts[i] = reader.ReadInt();
            ItemsGrabbedCounts[i] = reader.ReadInt();
        }
    }

    public List<ThingsBronzeCauldronPlayerOutcome> ToSanitizedOutcomes()
    {
        var list = new List<ThingsBronzeCauldronPlayerOutcome>(PlayerCount);
        for (int i = 0; i < PlayerCount; i++)
        {
            list.Add(new ThingsBronzeCauldronPlayerOutcome(
                NetIds[i],
                Math.Clamp(GoldAmounts[i], 0, 600),
                Math.Clamp(MaxHpGains[i], 0, 12),
                Math.Clamp(UpgradeCounts[i], 0, 3),
                Math.Clamp(PotionCounts[i], 0, 4),
                Math.Clamp(CardCounts[i], 0, 4),
                Math.Clamp(CommonRelicCounts[i], 0, 4),
                Math.Clamp(UncommonRelicCounts[i], 0, 2),
                Math.Clamp(RareRelicCounts[i], 0, 4),
                Math.Clamp(ItemsGrabbedCounts[i], 0, 64)));
        }
        return list;
    }

    public static ThingsCauldronFinalOutcomeMessage FromOutcomes(
        RunLocation location,
        IReadOnlyList<ThingsBronzeCauldronPlayerOutcome> outcomes)
    {
        int n = Math.Min(outcomes.Count, 8);
        var msg = new ThingsCauldronFinalOutcomeMessage
        {
            Location = location,
            PlayerCount = n,
            NetIds = new ulong[n],
            GoldAmounts = new int[n],
            MaxHpGains = new int[n],
            UpgradeCounts = new int[n],
            PotionCounts = new int[n],
            CardCounts = new int[n],
            CommonRelicCounts = new int[n],
            UncommonRelicCounts = new int[n],
            RareRelicCounts = new int[n],
            ItemsGrabbedCounts = new int[n],
        };
        for (int i = 0; i < n; i++)
        {
            ThingsBronzeCauldronPlayerOutcome o = outcomes[i];
            msg.NetIds[i] = o.PlayerNetId;
            msg.GoldAmounts[i] = o.GoldAmount;
            msg.MaxHpGains[i] = o.MaxHpGain;
            msg.UpgradeCounts[i] = o.UpgradeCount;
            msg.PotionCounts[i] = o.PotionCount;
            msg.CardCounts[i] = o.CardCount;
            msg.CommonRelicCounts[i] = o.CommonRelicCount;
            msg.UncommonRelicCounts[i] = o.UncommonRelicCount;
            msg.RareRelicCounts[i] = o.RareRelicCount;
            msg.ItemsGrabbedCounts[i] = o.ItemsGrabbed;
        }
        return msg;
    }
}

internal static class ThingsEventMinigameSync
{
    private sealed class State(
        RunLocationTargetedMessageBuffer buffer,
        INetGameService gameService,
        IPlayerCollection playerCollection,
        ulong localPlayerId)
    {
        public readonly RunLocationTargetedMessageBuffer Buffer = buffer;
        public readonly INetGameService GameService = gameService;
        public readonly IPlayerCollection PlayerCollection = playerCollection;
        public readonly ulong LocalPlayerId = localPlayerId;
        public BronzeCauldronMinigame? ActiveCauldron;
        public List<ThingsBronzeCauldronPlayerOutcome>? BufferedCauldronFinal;

        public void HandleBilgeSweep(ThingsBilgeSweepResultMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId)
                return;
            Player? player = PlayerCollection.GetPlayer(senderId);
            if (player == null)
                return;
            EventModel? ev = RunManager.Instance?.EventSynchronizer.GetEventForPlayer(player);
            if (ev is ThingsBilgeMines bilgeMines)
                bilgeMines.ReceiveRemoteOutcome(message.ToSanitizedOutcome());
        }

        public void HandleRuneLoom(ThingsRuneLoomResultMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId)
                return;
            Player? player = PlayerCollection.GetPlayer(senderId);
            if (player == null)
                return;
            EventModel? ev = RunManager.Instance?.EventSynchronizer.GetEventForPlayer(player);
            if (ev is ThingsMossRuneLoom runeLoom)
                runeLoom.ReceiveRemoteOutcome(message.ToSanitizedOutcome());
        }

        public void HandleClamClaw(ThingsClamClawResultMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId)
                return;
            Player? player = PlayerCollection.GetPlayer(senderId);
            if (player == null)
                return;
            EventModel? ev = RunManager.Instance?.EventSynchronizer.GetEventForPlayer(player);
            if (ev is ThingsClamClaw clamClaw)
                clamClaw.ReceiveRemoteOutcome(message.ToSanitizedOutcome());
        }

        public void HandleForestPlinko(ThingsForestPlinkoResultMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId)
                return;
            Player? player = PlayerCollection.GetPlayer(senderId);
            if (player == null)
                return;
            EventModel? ev = RunManager.Instance?.EventSynchronizer.GetEventForPlayer(player);
            if (ev is ThingsForestPlinko forestPlinko)
                forestPlinko.ReceiveRemoteOutcome(message.ToSanitizedOutcome());
        }

        public void HandleCauldronHand(ThingsCauldronHandMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId)
                return;
            ActiveCauldron?.ApplyRemoteHandPosition(senderId, message.HandX, message.HandY, message.IsClenched);
        }

        public void HandleCauldronGrabRequest(ThingsCauldronGrabRequestMessage message, ulong senderId)
        {
            if (senderId == LocalPlayerId || ActiveCauldron == null || !ActiveCauldron.IsHostAuthority)
                return;
            ActiveCauldron.ResolveAuthoritativeGrab(senderId, message.GrabX, message.GrabY);
        }

        public void HandleCauldronGrabAck(ThingsCauldronGrabAckMessage message, ulong senderId)
        {
            if (ActiveCauldron == null || ActiveCauldron.IsHostAuthority)
                return;
            ActiveCauldron.ApplyResolvedGrab(message.GrabberNetId, message.GrabX, message.GrabY, message.ClaimedBitmask, message.GrabsRemaining);
        }

        public void HandleCauldronFinal(ThingsCauldronFinalOutcomeMessage message, ulong senderId)
        {
            List<ThingsBronzeCauldronPlayerOutcome> outcomes = message.ToSanitizedOutcomes();
            if (ActiveCauldron != null)
            {
                if (!ActiveCauldron.IsHostAuthority)
                    ActiveCauldron.ApplyFinalOutcomes(outcomes);
            }
            else
            {
                BufferedCauldronFinal = outcomes;
            }
        }
    }

    private static readonly ConditionalWeakTable<OneOffSynchronizer, State> States = new();

    internal static void Bind(
        OneOffSynchronizer synchronizer,
        RunLocationTargetedMessageBuffer buffer,
        INetGameService gameService,
        IPlayerCollection playerCollection,
        ulong localPlayerId)
    {
        var state = new State(buffer, gameService, playerCollection, localPlayerId);
        States.Add(synchronizer, state);
        buffer.RegisterMessageHandler<ThingsBilgeSweepResultMessage>(state.HandleBilgeSweep);
        buffer.RegisterMessageHandler<ThingsRuneLoomResultMessage>(state.HandleRuneLoom);
        buffer.RegisterMessageHandler<ThingsClamClawResultMessage>(state.HandleClamClaw);
        buffer.RegisterMessageHandler<ThingsForestPlinkoResultMessage>(state.HandleForestPlinko);
        buffer.RegisterMessageHandler<ThingsCauldronHandMessage>(state.HandleCauldronHand);
        buffer.RegisterMessageHandler<ThingsCauldronGrabRequestMessage>(state.HandleCauldronGrabRequest);
        buffer.RegisterMessageHandler<ThingsCauldronGrabAckMessage>(state.HandleCauldronGrabAck);
        buffer.RegisterMessageHandler<ThingsCauldronFinalOutcomeMessage>(state.HandleCauldronFinal);
    }

    internal static void Unbind(OneOffSynchronizer synchronizer)
    {
        if (!States.TryGetValue(synchronizer, out State? state))
            return;
        state.Buffer.UnregisterMessageHandler<ThingsBilgeSweepResultMessage>(state.HandleBilgeSweep);
        state.Buffer.UnregisterMessageHandler<ThingsRuneLoomResultMessage>(state.HandleRuneLoom);
        state.Buffer.UnregisterMessageHandler<ThingsClamClawResultMessage>(state.HandleClamClaw);
        state.Buffer.UnregisterMessageHandler<ThingsForestPlinkoResultMessage>(state.HandleForestPlinko);
        state.Buffer.UnregisterMessageHandler<ThingsCauldronHandMessage>(state.HandleCauldronHand);
        state.Buffer.UnregisterMessageHandler<ThingsCauldronGrabRequestMessage>(state.HandleCauldronGrabRequest);
        state.Buffer.UnregisterMessageHandler<ThingsCauldronGrabAckMessage>(state.HandleCauldronGrabAck);
        state.Buffer.UnregisterMessageHandler<ThingsCauldronFinalOutcomeMessage>(state.HandleCauldronFinal);
        state.ActiveCauldron = null;
        state.BufferedCauldronFinal = null;
        States.Remove(synchronizer);
    }

    internal static void AttachCauldronSession(BronzeCauldronMinigame session)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;

        state.ActiveCauldron = session;
        if (session.IsHostAuthority)
        {
            session.HostGrabResolved += (grabberId, gx, gy, mask, left) =>
            {
                state.GameService.SendMessage(new ThingsCauldronGrabAckMessage
                {
                    Location = state.Buffer.CurrentLocation,
                    GrabberNetId = grabberId,
                    GrabX = gx,
                    GrabY = gy,
                    ClaimedBitmask = mask,
                    GrabsRemaining = left,
                });
            };
            session.HostFinalizedOutcomes += outcomes =>
            {
                state.GameService.SendMessage(
                    ThingsCauldronFinalOutcomeMessage.FromOutcomes(state.Buffer.CurrentLocation, outcomes));
            };
        }
        else if (state.BufferedCauldronFinal != null)
        {
            List<ThingsBronzeCauldronPlayerOutcome> buffered = state.BufferedCauldronFinal;
            state.BufferedCauldronFinal = null;
            session.ApplyFinalOutcomes(buffered);
        }
    }

    internal static void DetachCauldronSession(BronzeCauldronMinigame session)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        if (ReferenceEquals(state.ActiveCauldron, session))
            state.ActiveCauldron = null;
    }

    internal static void SendCauldronHandUpdate(float x, float y, bool isClenched)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsCauldronHandMessage
        {
            Location = state.Buffer.CurrentLocation,
            HandX = x,
            HandY = y,
            IsClenched = isClenched,
        });
    }

    internal static void SendCauldronGrabRequest(float x, float y)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsCauldronGrabRequestMessage
        {
            Location = state.Buffer.CurrentLocation,
            GrabX = x,
            GrabY = y,
        });
    }

    internal static void SendBilgeSweepResult(ThingsBilgeSweepOutcome outcome)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsBilgeSweepResultMessage
        {
            Location = state.Buffer.CurrentLocation,
            HitMine = outcome.HitMine,
            GoldAmount = outcome.GoldAmount,
            PotionCount = outcome.PotionCount,
            CardCount = outcome.CardCount,
            CommonRelicCount = outcome.CommonRelicCount,
            ProbesUsed = outcome.ProbesUsed,
            CashedOut = outcome.CashedOut,
        });
    }

    internal static void SendRuneLoomResult(ThingsRuneLoomOutcome outcome)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsRuneLoomResultMessage
        {
            Location = state.Buffer.CurrentLocation,
            Cancelled = outcome.Cancelled,
            DeckCardIndex = outcome.DeckCardIndex,
            UpgradeCard = outcome.UpgradeCard,
            EnchantmentKind = (byte)outcome.EnchantmentKind,
            EnchantmentAmount = outcome.EnchantmentAmount,
            HealAmount = outcome.HealAmount,
            GoldGain = outcome.GoldGain,
            MaxHpGain = outcome.MaxHpGain,
            DamageAmount = outcome.DamageAmount,
            GoldLoss = outcome.GoldLoss,
            MaxHpLoss = outcome.MaxHpLoss,
            CurseCount = outcome.CurseCount,
        });
    }

    internal static void SendClamClawResult(ThingsClamClawOutcome outcome)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsClamClawResultMessage
        {
            Location = state.Buffer.CurrentLocation,
            GoldAmount = outcome.GoldAmount,
            PotionCount = outcome.PotionCount,
            CardCount = outcome.CardCount,
            CommonRelicCount = outcome.CommonRelicCount,
            RareRelicCount = outcome.RareRelicCount,
            GrabsUsed = outcome.GrabsUsed,
            SlippedCount = outcome.SlippedCount,
        });
    }

    internal static void SendForestPlinkoResult(ThingsForestPlinkoOutcome outcome)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.GameService.SendMessage(new ThingsForestPlinkoResultMessage
        {
            Location = state.Buffer.CurrentLocation,
            GoldAmount = outcome.GoldAmount,
            HealAmount = outcome.HealAmount,
            MaxHpGain = outcome.MaxHpGain,
            PotionCount = outcome.PotionCount,
            CardCount = outcome.CardCount,
            CommonRelicCount = outcome.CommonRelicCount,
            RareRelicCount = outcome.RareRelicCount,
            UpgradeCount = outcome.UpgradeCount,
            DropsUsed = outcome.DropsUsed,
        });
    }
}

[HarmonyPatch(typeof(OneOffSynchronizer), MethodType.Constructor,
    [typeof(RunLocationTargetedMessageBuffer), typeof(INetGameService), typeof(IPlayerCollection), typeof(ulong)])]
internal static class ThingsEventMinigameSyncBindPatch
{
    private static void Postfix(
        OneOffSynchronizer __instance,
        RunLocationTargetedMessageBuffer messageBuffer,
        INetGameService gameService,
        IPlayerCollection playerCollection,
        ulong localPlayerId)
        => ThingsEventMinigameSync.Bind(__instance, messageBuffer, gameService, playerCollection, localPlayerId);
}

[HarmonyPatch(typeof(OneOffSynchronizer), nameof(OneOffSynchronizer.Dispose))]
internal static class ThingsEventMinigameSyncUnbindPatch
{
    private static void Prefix(OneOffSynchronizer __instance)
        => ThingsEventMinigameSync.Unbind(__instance);
}
