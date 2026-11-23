using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Hooks;

namespace STS2_Things.Config;

/// <summary>Session overrides never replace the local preferences written to disk.</summary>
public static class MultiplayerConfig
{
    private static INetGameService? _service;
    private static ThingsConfigSnapshotMessage? _snapshot;
    private static ThingsDepthsPityMessage? _depthsPity;
    private static GameMode _boundGameMode = GameMode.None;
    private static bool _initialized;
    private static readonly Dictionary<string, int> Indices = ThingsModConfig.Entries
        .Select((entry, index) => (entry.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index);
    private static readonly bool[] IsBoolEntry = ThingsModConfig.Entries
        .Select(entry => entry.Default is bool).ToArray();

    public static bool IsActive => _service is not null;
    public static bool IsHost => _service?.Type == NetGameType.Host;
    public static bool IsLocked => _snapshot?.Locked == true;
    public static bool HasSnapshot => _snapshot.HasValue;
    public static bool HasDepthsPity => _depthsPity.HasValue;
    public static bool SessionDepthsPityActive => _depthsPity?.ForceDepths == true;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ThingsModConfig.LocalChanged += OnLocalChanged;
    }

    public static bool CanEdit(string key) => !IsActive ||
        IsHost && (!IsLocked || key == ThingsModConfig.FeatureCustomBgmEnabled);

    public static bool TryGetBoolOverride(string key, out bool value)
    {
        if (_snapshot is { } snapshot && Indices.TryGetValue(key, out int index) && IsBoolEntry[index])
        {
            value = snapshot.Values[index] == 1;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryGetIntOverride(string key, out int value)
    {
        if (_snapshot is { } snapshot && Indices.TryGetValue(key, out int index) && !IsBoolEntry[index])
        {
            value = snapshot.Values[index];
            return true;
        }

        value = 0;
        return false;
    }

    public static object? GetOverride(string key)
    {
        if (_snapshot is not { } snapshot || !Indices.TryGetValue(key, out int index)) return null;
        return IsBoolEntry[index] ? (object)(snapshot.Values[index] == 1) : snapshot.Values[index];
    }

    public static void Bind(INetGameService service, bool lockForLoadedRun = false)
    {
        BindCore(service, lockForLoadedRun, GameMode.None);
    }

    internal static void BindStartLobby(INetGameService service, GameMode gameMode)
    {
        BindCore(service, lockForLoadedRun: false, gameMode);
    }

    private static void BindCore(INetGameService service, bool lockForLoadedRun, GameMode gameMode)
    {
        Initialize();
        if (service.Type is not (NetGameType.Host or NetGameType.Client))
        {
            Clear();
            return;
        }
        if (ReferenceEquals(_service, service))
        {
            if (gameMode != GameMode.None)
                _boundGameMode = gameMode;
            if (lockForLoadedRun && IsHost && !IsLocked)
                PrepareRunForGameMode(service, _boundGameMode, disableDepthsPity: true);
            return;
        }
        Clear();
        _service = service;
        _boundGameMode = gameMode;
        service.RegisterMessageHandler<ThingsConfigSnapshotMessage>(Receive);
        service.RegisterMessageHandler<ThingsDepthsPityMessage>(ReceiveDepthsPity);
        service.Disconnected += OnDisconnected;
        if (service is INetHostGameService host)
        {
            host.ClientConnected += SendToJoiningPlayer;
            CaptureHost(lockForLoadedRun, gameMode, disableDepthsPity: lockForLoadedRun);
            SendToConnectedPeers(host);
        }
        ThingsModConfig.NotifyEffectiveChanged();
    }

    public static void SyncPreConnectedClients(INetGameService service)
    {
        Bind(service);
        if (service is INetHostGameService host)
            SendToConnectedPeers(host);
    }

    private static int SendToConnectedPeers(INetHostGameService host)
    {
        HashSet<ulong> sent = [];
        foreach (ulong peer in host.NetHost?.ConnectedPeerIds ?? [])
            if (sent.Add(peer)) SendToJoiningPlayer(peer);
#if STS2_V113_1
        foreach (var peer in host.ConnectedPeers)
            if (sent.Add(peer.peerId)) SendToJoiningPlayer(peer.peerId);
#else
        if (host is MegaCrit.Sts2.Core.Multiplayer.NetHostGameService netHostService)
        {
            foreach (var peer in netHostService.ConnectedPeers)
                if (sent.Add(peer.peerId)) SendToJoiningPlayer(peer.peerId);
        }
#endif
        return sent.Count;
    }

    public static void Clear()
    {
        if (_service is not { } service) return;
        _service = null;
        _snapshot = null;
        _depthsPity = null;
        _boundGameMode = GameMode.None;
        service.UnregisterMessageHandler<ThingsConfigSnapshotMessage>(Receive);
        service.UnregisterMessageHandler<ThingsDepthsPityMessage>(ReceiveDepthsPity);
        service.Disconnected -= OnDisconnected;
        if (service is INetHostGameService host) host.ClientConnected -= SendToJoiningPlayer;
        ThingsModConfig.NotifyEffectiveChanged();
    }

    public static bool PrepareRun(INetGameService service) =>
        PrepareRunForGameMode(service, GameMode.None, disableDepthsPity: false);

    internal static bool PrepareRunForGameMode(
        INetGameService service,
        GameMode gameMode,
        bool disableDepthsPity = false)
    {
        BindCore(service, lockForLoadedRun: false, gameMode);
        if (!IsActive) return true;
        if (IsHost)
        {
            if (!IsLocked)
            {
                CaptureHost(locked: true, gameMode, disableDepthsPity);
                Broadcast(includeDepthsPity: true);
                ThingsModConfig.NotifyEffectiveChanged();
            }
            return true;
        }
        if (IsLocked) return true;
        RejectIncompatibleSession("No final host configuration was received before run start.");
        return false;
    }

    public static void Detach(INetGameService? service)
    {
        if (ReferenceEquals(service, _service)) Clear();
    }

    private static void CaptureHost(
        bool locked,
        GameMode gameMode = GameMode.None,
        bool disableDepthsPity = false)
    {
        bool wasLocked = _snapshot is { Locked: true };
        int[] values = _snapshot is { Locked: true } old ? (int[])old.Values.Clone() :
            ThingsModConfig.Entries.Select(entry => Convert.ToInt32(ThingsModConfig.GetLocalValue(entry.Key))).ToArray();
        values[Indices[ThingsModConfig.FeatureCustomBgmEnabled]] =
            Convert.ToInt32(ThingsModConfig.GetLocalValue(ThingsModConfig.FeatureCustomBgmEnabled));
        uint revision = (_snapshot?.Revision ?? 0) + 1;
        if (!wasLocked)
        {
            GameMode effectiveMode = gameMode != GameMode.None ? gameMode : _boundGameMode;
            _depthsPity = new ThingsDepthsPityMessage
            {
                Revision = revision,
                ForceDepths = !disableDepthsPity && DepthsActPityTracker.ShouldHostActivatePity(effectiveMode),
            };
        }
        _snapshot = new ThingsConfigSnapshotMessage
        {
            Version = ThingsConfigSnapshotMessage.Protocol,
            SchemaHash = ThingsConfigSnapshotMessage.Schema,
            Revision = revision,
            Locked = locked,
            Values = values,
        };
    }

    private static void OnLocalChanged()
    {
        if (!IsHost) return;
        bool wasLocked = IsLocked;
        CaptureHost(wasLocked, _boundGameMode);
        Broadcast(includeDepthsPity: !wasLocked);
    }

    private static void Broadcast(bool includeDepthsPity)
    {
        if (_service is not { IsConnected: true } service || _snapshot is not { } snapshot)
            return;
        if (service is INetHostGameService host)
        {
            if (SendToConnectedPeers(host) == 0 && host.NetHost is null)
            {
                if (includeDepthsPity && _depthsPity is { } pityMessage)
                    service.SendMessage(pityMessage);
                service.SendMessage(snapshot);
            }
            return;
        }
        if (includeDepthsPity && _depthsPity is { } pity)
            service.SendMessage(pity);
        service.SendMessage(snapshot);
    }

    private static void SendToJoiningPlayer(ulong playerId)
    {
        if (_service is not { IsConnected: true } service)
            return;
        if (_depthsPity is { } pityMessage)
            service.SendMessage(pityMessage, playerId);
        if (_snapshot is { } snapshot)
            service.SendMessage(snapshot, playerId);
    }

    private static void ReceiveDepthsPity(ThingsDepthsPityMessage message, ulong senderId)
    {
        if (_service is not INetClientGameService client || client.NetClient?.HostNetId != senderId)
            return;
        if (message.Revision == 0)
            return;
        if (_depthsPity is { } previous && message.Revision <= previous.Revision)
            return;
        if (_snapshot is { Locked: true } && message.ForceDepths != (_depthsPity?.ForceDepths ?? false))
        {
            RejectIncompatibleSession("Host attempted to change locked Depths pity state.");
            return;
        }
        _depthsPity = message;
        Log.Info($"STS2_Things: host Depths pity state applied (revision {message.Revision}, forceDepths={message.ForceDepths}).");
    }

    private static void Receive(ThingsConfigSnapshotMessage message, ulong senderId)
    {
        if (_service is not INetClientGameService client || client.NetClient?.HostNetId != senderId)
            return;
        if (!message.IsValid())
        {
            RejectIncompatibleSession("Host configuration protocol or values are incompatible.");
            return;
        }
        if (_snapshot is { } previous)
        {
            if (message.Revision <= previous.Revision) return;
            if (previous.Locked && (!message.Locked || message.Values.Where((value, index) =>
                    index != Indices[ThingsModConfig.FeatureCustomBgmEnabled] && value != previous.Values[index]).Any()))
            {
                RejectIncompatibleSession("Host attempted to change locked gameplay configuration.");
                return;
            }
        }
        _snapshot = message with { Values = (int[])message.Values.Clone() };
        ThingsModConfig.NotifyEffectiveChanged();
        Log.Info($"STS2_Things: host configuration applied (revision {message.Revision}, locked={message.Locked}).");
    }

    private static void RejectIncompatibleSession(string reason)
    {
        Log.Warn("STS2_Things: " + reason + " All players must use the same mod build.");
        _service?.Disconnect(NetError.ModMismatch);
        Clear();
    }

    private static void OnDisconnected(NetErrorInfo _) => Clear();
}
