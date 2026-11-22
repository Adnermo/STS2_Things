using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Platform;

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
/// Not broadcast by the host (non-host peers ignore it; host broadcasts the authoritative ack).
/// </summary>
public struct ThingsCauldronGrabRequestMessage : INetMessage, IRunLocationTargetedMessage
{
    public RunLocation Location { get; set; }
    public float GrabX;
    public float GrabY;

    public bool ShouldBroadcast => false;
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
/// Host-authoritative reliable grab acknowledgement broadcasting which exact item indices (0..255)
/// a player claimed. Uses an explicit byte array loop (no 64-item bitmask ceiling and no 3-bit WriteList).
/// </summary>
public struct ThingsCauldronGrabAckMessage : INetMessage, IRunLocationTargetedMessage
{
    public const int MaxClaimedIndicesPerGrab = 64;

    public RunLocation Location { get; set; }
    public ulong GrabberNetId;
    public float GrabX;
    public float GrabY;
    public byte[]? ClaimedIndices;
    public int GrabsRemaining;

    /// <summary>
    /// Compatibility 64-bit bitmask projection over <see cref="ClaimedIndices"/> for indices 0..63.
    /// </summary>
    public ulong ClaimedBitmask
    {
        readonly get
        {
            ulong mask = 0UL;
            if (ClaimedIndices != null)
            {
                for (int i = 0; i < ClaimedIndices.Length; i++)
                {
                    byte idx = ClaimedIndices[i];
                    if (idx < 64)
                        mask |= 1UL << idx;
                }
            }
            return mask;
        }
        set
        {
            if (value == 0UL)
            {
                ClaimedIndices = [];
                return;
            }
            var list = new List<byte>(8);
            for (int bit = 0; bit < 64; bit++)
            {
                if ((value & (1UL << bit)) != 0UL)
                    list.Add((byte)bit);
            }
            ClaimedIndices = list.ToArray();
        }
    }

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
        int count = Math.Clamp(ClaimedIndices?.Length ?? 0, 0, MaxClaimedIndicesPerGrab);
        writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++)
            writer.WriteByte(ClaimedIndices![i]);
        writer.WriteInt(GrabsRemaining);
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        GrabberNetId = reader.ReadULong();
        GrabX = reader.ReadFloat();
        GrabY = reader.ReadFloat();
        int count = Math.Clamp((int)reader.ReadByte(), 0, MaxClaimedIndicesPerGrab);
        var indices = new byte[count];
        for (int i = 0; i < count; i++)
            indices[i] = reader.ReadByte();
        ClaimedIndices = indices;
        GrabsRemaining = reader.ReadInt();
    }
}

/// <summary>
/// Host-authoritative reliable final outcome table for every player in the Bronze Tripod Scramble
/// so all peers award identical rewards and pass room-exit checksums cleanly.
/// Bit-packed at 14 bytes/player (32 players ≈ 456 bytes, comfortably below PacketWriter's 1024-byte default).
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
        int count = Math.Clamp(PlayerCount, 0, MultiplayerCompat.MaxProtocolPlayers);
        if (NetIds == null || GoldAmounts == null || MaxHpGains == null || UpgradeCounts == null ||
            PotionCounts == null || CardCounts == null || CommonRelicCounts == null ||
            UncommonRelicCounts == null || RareRelicCounts == null || ItemsGrabbedCounts == null)
        {
            count = 0;
        }
        else
        {
            count = Math.Min(count, NetIds.Length);
        }

        writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++)
        {
            writer.WriteULong(NetIds![i]);
            writer.WriteInt(Math.Clamp(GoldAmounts![i], 0, 2000), 11);
            writer.WriteInt(Math.Clamp(MaxHpGains![i], 0, 31), 5);
            writer.WriteInt(Math.Clamp(UpgradeCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(PotionCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(CardCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(CommonRelicCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(UncommonRelicCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(RareRelicCounts![i], 0, 15), 4);
            writer.WriteInt(Math.Clamp(ItemsGrabbedCounts![i], 0, 255), 8);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        int count = Math.Clamp((int)reader.ReadByte(), 0, MultiplayerCompat.MaxProtocolPlayers);
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
            GoldAmounts[i] = reader.ReadInt(11);
            MaxHpGains[i] = reader.ReadInt(5);
            UpgradeCounts[i] = reader.ReadInt(4);
            PotionCounts[i] = reader.ReadInt(4);
            CardCounts[i] = reader.ReadInt(4);
            CommonRelicCounts[i] = reader.ReadInt(4);
            UncommonRelicCounts[i] = reader.ReadInt(4);
            RareRelicCounts[i] = reader.ReadInt(4);
            ItemsGrabbedCounts[i] = reader.ReadInt(8);
        }
    }

    public List<ThingsBronzeCauldronPlayerOutcome> ToSanitizedOutcomes()
    {
        int count = NetIds == null ? 0 : Math.Clamp(PlayerCount, 0, NetIds.Length);
        var list = new List<ThingsBronzeCauldronPlayerOutcome>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ThingsEventMinigameSync.SanitizeCauldronOutcome(new ThingsBronzeCauldronPlayerOutcome(
                NetIds![i],
                GoldAmounts[i],
                MaxHpGains[i],
                UpgradeCounts[i],
                PotionCounts[i],
                CardCounts[i],
                CommonRelicCounts[i],
                UncommonRelicCounts[i],
                RareRelicCounts[i],
                ItemsGrabbedCounts[i])));
        }
        return list;
    }

    public static ThingsCauldronFinalOutcomeMessage FromOutcomes(
        RunLocation location,
        IReadOnlyList<ThingsBronzeCauldronPlayerOutcome> outcomes)
    {
        int n = Math.Min(outcomes.Count, MultiplayerCompat.MaxProtocolPlayers);
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
            ThingsBronzeCauldronPlayerOutcome o = ThingsEventMinigameSync.SanitizeCauldronOutcome(outcomes[i]);
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

public static class ThingsEventMinigameSync
{
    /// <summary>
    /// Unified Bronze Tripod Scramble outcome sanitizer applied on BOTH host finalization
    /// and client packet decode so every peer awards bit-for-bit identical outcomes.
    /// </summary>
    public static ThingsBronzeCauldronPlayerOutcome SanitizeCauldronOutcome(ThingsBronzeCauldronPlayerOutcome outcome) => new(
        outcome.PlayerNetId,
        Math.Clamp(outcome.GoldAmount, 0, 1500),
        Math.Clamp(outcome.MaxHpGain, 0, 24),
        Math.Clamp(outcome.UpgradeCount, 0, 8),
        Math.Clamp(outcome.PotionCount, 0, 8),
        Math.Clamp(outcome.CardCount, 0, 8),
        Math.Clamp(outcome.CommonRelicCount, 0, 8),
        Math.Clamp(outcome.UncommonRelicCount, 0, 6),
        Math.Clamp(outcome.RareRelicCount, 0, 8),
        Math.Clamp(outcome.ItemsGrabbed, 0, 128));

    internal static bool IsRunActive =>
        RunManager.Instance is { IsInProgress: true, IsCleaningUp: false };

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
        public readonly List<(ulong OwnerNetId, Action Cancel)> RemoteOutcomeWaiters = [];
        public readonly HashSet<ulong> DisconnectedPeers = [];
        public RunLobby? SubscribedRunLobby;
        public bool LocalDisconnected;
        public BronzeCauldronMinigame? ActiveCauldron;
        public List<ThingsBronzeCauldronPlayerOutcome>? BufferedCauldronFinal;
        public RunLocation BufferedCauldronFinalLocation;

        public void EnsureRunLobbySubscribed()
        {
            RunLobby? lobby = RunManager.Instance?.RunLobby;
            if (lobby == null || ReferenceEquals(SubscribedRunLobby, lobby))
                return;
            if (SubscribedRunLobby != null)
                SubscribedRunLobby.RemotePlayerDisconnected -= HandlePeerDisconnected;
            SubscribedRunLobby = lobby;
            lobby.RemotePlayerDisconnected += HandlePeerDisconnected;
        }

        public void HandleLocalDisconnected(NetErrorInfo _)
        {
            LocalDisconnected = true;
            CancelAllWaiters();
        }

        public void HandleClientDisconnected(ulong peerId, NetErrorInfo _)
        {
            HandlePeerDisconnected(peerId);
        }

        public void HandlePeerDisconnected(ulong peerId)
        {
            DisconnectedPeers.Add(peerId);
            CancelWaitersForPeer(peerId);
        }

        public void CancelWaitersForPeer(ulong peerId)
        {
            List<Action>? toCancel = null;
            for (int i = RemoteOutcomeWaiters.Count - 1; i >= 0; i--)
            {
                if (RemoteOutcomeWaiters[i].OwnerNetId == peerId)
                {
                    toCancel ??= [];
                    toCancel.Add(RemoteOutcomeWaiters[i].Cancel);
                    RemoteOutcomeWaiters.RemoveAt(i);
                }
            }

            if (toCancel != null)
            {
                foreach (Action cancel in toCancel)
                    cancel();
            }
        }

        public void CancelAllWaiters()
        {
            if (RemoteOutcomeWaiters.Count > 0)
            {
                Action[] toCancel = new Action[RemoteOutcomeWaiters.Count];
                for (int i = 0; i < RemoteOutcomeWaiters.Count; i++)
                    toCancel[i] = RemoteOutcomeWaiters[i].Cancel;
                RemoteOutcomeWaiters.Clear();
                foreach (Action cancel in toCancel)
                    cancel();
            }

            BronzeCauldronMinigame? cauldron = ActiveCauldron;
            ActiveCauldron = null;
            BufferedCauldronFinal = null;
            cauldron?.CancelAllWaiters();
        }

        public void DetachAndCancelAll()
        {
            LocalDisconnected = true;
            GameService.Disconnected -= HandleLocalDisconnected;
            if (GameService is INetHostGameService hostService)
                hostService.ClientDisconnected -= HandleClientDisconnected;
            if (SubscribedRunLobby != null)
            {
                SubscribedRunLobby.RemotePlayerDisconnected -= HandlePeerDisconnected;
                SubscribedRunLobby = null;
            }
            CancelAllWaiters();
        }

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
            ActiveCauldron.ResolveAuthoritativeGrabIndices(senderId, message.GrabX, message.GrabY);
        }

        public void HandleCauldronGrabAck(ThingsCauldronGrabAckMessage message, ulong senderId)
        {
            if (ActiveCauldron == null || ActiveCauldron.IsHostAuthority)
                return;
            ActiveCauldron.ApplyResolvedGrab(
                message.GrabberNetId,
                message.GrabX,
                message.GrabY,
                message.ClaimedIndices ?? [],
                message.GrabsRemaining);
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
                BufferedCauldronFinalLocation = message.Location;
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
        gameService.Disconnected += state.HandleLocalDisconnected;
        if (gameService is INetHostGameService hostService)
            hostService.ClientDisconnected += state.HandleClientDisconnected;
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
        state.DetachAndCancelAll();
        States.Remove(synchronizer);
    }

    internal static void OnRunCleanUp(RunManager runManager)
    {
        OneOffSynchronizer? sync = runManager.OneOffSynchronizer;
        if (sync != null)
            Unbind(sync);
    }

    internal static void RegisterRemoteWaiter(ulong ownerNetId, Action cancel)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        state.EnsureRunLobbySubscribed();
        if (state.LocalDisconnected || state.DisconnectedPeers.Contains(ownerNetId))
        {
            cancel();
            return;
        }
        state.RemoteOutcomeWaiters.Add((ownerNetId, cancel));
    }

    internal static void UnregisterRemoteWaiter(Action cancel)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        for (int i = state.RemoteOutcomeWaiters.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(state.RemoteOutcomeWaiters[i].Cancel, cancel))
                state.RemoteOutcomeWaiters.RemoveAt(i);
        }
    }

    internal static void AttachCauldronSession(BronzeCauldronMinigame session)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;

        state.EnsureRunLobbySubscribed();
        if (state.LocalDisconnected)
        {
            session.CancelAllWaiters();
            return;
        }

        state.ActiveCauldron = session;
        if (session.IsHostAuthority)
        {
            session.HostGrabResolved += (grabberId, gx, gy, claimedIndices, left) =>
            {
                state.GameService.SendMessage(new ThingsCauldronGrabAckMessage
                {
                    Location = state.Buffer.CurrentLocation,
                    GrabberNetId = grabberId,
                    GrabX = gx,
                    GrabY = gy,
                    ClaimedIndices = claimedIndices,
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
            RunLocation bufferedLocation = state.BufferedCauldronFinalLocation;
            state.BufferedCauldronFinal = null;
            if (bufferedLocation == state.Buffer.CurrentLocation)
                session.ApplyFinalOutcomes(buffered);
        }
    }

    internal static void DetachCauldronSession(BronzeCauldronMinigame session)
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        if (ReferenceEquals(state.ActiveCauldron, session))
        {
            state.ActiveCauldron = null;
            state.BufferedCauldronFinal = null;
        }
    }

    internal static void ClearStaleCauldronBuffer()
    {
        OneOffSynchronizer? sync = RunManager.Instance?.OneOffSynchronizer;
        if (sync == null || !States.TryGetValue(sync, out State? state))
            return;
        if (state.ActiveCauldron == null)
            state.BufferedCauldronFinal = null;
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

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class ThingsEventMinigameSyncRunCleanUpPatch
{
    private static void Prefix(RunManager __instance)
        => ThingsEventMinigameSync.OnRunCleanUp(__instance);
}
