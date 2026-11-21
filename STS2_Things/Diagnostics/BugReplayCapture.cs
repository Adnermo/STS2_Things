using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Map;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Captures universal pre-entry room checkpoints (Schema 2) across Combat, Boss, Event,
/// Ancient, Merchant, RestSite, and Treasure rooms, plus anonymized native CombatReplay blobs.
/// Heavy JSON/binary serialization, regex scrubbing, SHA-256, GZip, and Base64 encoding run on
/// background tasks while lightweight DTO snapshots are taken on the main thread.
/// </summary>
internal static class BugReplayCapture
{
    private static readonly FieldInfo? WriterReplayField = AccessTools.Field(typeof(CombatReplayWriter), "_replay");
    private static readonly object PendingGate = new();
    private static (RunState Run, int ActIndex, int ActFloor, SerializableRun Save)? _stagedMapPointSave;
    private static long _checkpointSeq;
    private static long _replaySeq;
    private static Task _pendingCheckpointTask = Task.CompletedTask;
    private static Task _pendingReplayTask = Task.CompletedTask;
    private static (WeakReference<RunState> RunRef, JsonObject Checkpoint)? _latestCheckpoint;
    private static (WeakReference<RunState> RunRef, JsonObject Checkpoint)? _completedCheckpoint;
    private static (WeakReference<RunState>? RunRef, JsonObject Replay)? _completedReplay;
    private static int _hasCompletedCapture;

    internal static JsonObject? Current { get; private set; }
    internal static JsonObject? CurrentCombatReplay { get; private set; }
    internal static bool HasPendingApply => Volatile.Read(ref _hasCompletedCapture) != 0;

    internal static void ClearRunState()
    {
        Interlocked.Increment(ref _checkpointSeq);
        Interlocked.Increment(ref _replaySeq);
        _stagedMapPointSave = null;
        lock (PendingGate)
        {
            Current = null;
            CurrentCombatReplay = null;
            _latestCheckpoint = null;
            _completedCheckpoint = null;
            _completedReplay = null;
            Volatile.Write(ref _hasCompletedCapture, 0);
        }
    }

    internal static void StagePreRoomSave()
    {
        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null) return;
            _stagedMapPointSave = (run, run.CurrentActIndex, run.ActFloor, RunManager.Instance.ToSave(null));
        }
        catch (Exception)
        {
            // Pre-room save staging is read-only and must never affect map point entry.
            _stagedMapPointSave = null;
        }
    }

    internal static void Capture(AbstractRoom? room)
    {
        if (room is null || room.IsPreFinished || room is MapRoom) return;

        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null) return;
            BugRunRecorder.EnsureRunStarted(run);

            SerializableRun save;
            var staged = _stagedMapPointSave;
            _stagedMapPointSave = null;

            if (staged is { } s
                && ReferenceEquals(s.Run, run)
                && s.ActIndex == run.CurrentActIndex
                && s.ActFloor == run.ActFloor)
            {
                // Exact pre-RollRoomTypeFor / pre-CreateRoom vanilla save: reproduces Unknown(?),
                // Merchant, RestSite, Treasure, Ancient, Event, and Combat rooms deterministically.
                save = s.Save;
            }
            else
            {
                save = RunManager.Instance.ToSave(null);
                // AbstractRoom.FromSerializable only supports CombatRoom (without ParentEventId) and EventRoom.
                if (room is CombatRoom { ParentEventId: null, IsPreFinished: false }
                    || room is EventRoom { IsPreFinished: false })
                {
                    save.PreFinishedRoom = room.ToSerializable();
                }
            }

            SerializableModifier? injectedLedger = null;
            if ((!Crossroads.TryGetSerializedLedger(save, out var ledgerMod) || ledgerMod is null)
                && Crossroads.Ledger(run) is { } liveLedger)
            {
                ledgerMod = liveLedger.ToSerializable();
            }
            if (ledgerMod is not null && !save.Modifiers.Any(m => Crossroads.IsLedgerId(m.Id)))
            {
                save.Modifiers.Add(ledgerMod);
                injectedLedger = ledgerMod;
            }

            var playerIds = new ulong[run.Players.Count];
            for (int i = 0; i < playerIds.Length; i++)
            {
                playerIds[i] = run.Players[i].NetId;
            }

            var metadata = BuildRoomMetadata(run, room);
            string? parentEventId = (room as CombatRoom)?.ParentEventId?.ToString();
            JsonTypeInfo<SerializableRun> runTypeInfo = JsonSerializationUtility.GetTypeInfo<SerializableRun>();
            BugRunRecorder.NoteCheckpointStarted(metadata);

            long seq = Interlocked.Increment(ref _checkpointSeq);
            var runRef = new WeakReference<RunState>(run);
            Task encodeTask = Task.Run(() =>
                EncodeCheckpointWorker(seq, runRef, save, injectedLedger, runTypeInfo, playerIds, metadata, parentEventId));
            lock (PendingGate)
            {
                _pendingCheckpointTask = encodeTask;
            }
        }
        catch (Exception ex)
        {
            var failed = new JsonObject
            {
                ["available"] = false,
                ["reason"] = "capture_failed",
                ["error_type"] = ex.GetType().FullName,
            };
            lock (PendingGate)
            {
                Current = failed;
            }
            BugRunRecorder.RecordCheckpoint(failed);
        }
    }

    private static void EncodeCheckpointWorker(
        long seq,
        WeakReference<RunState> runRef,
        SerializableRun save,
        SerializableModifier? injectedLedger,
        JsonTypeInfo<SerializableRun> runTypeInfo,
        ulong[] playerIds,
        CheckpointMetadata metadata,
        string? parentEventId)
    {
        JsonObject checkpoint;
        try
        {
            if (seq != Volatile.Read(ref _checkpointSeq)) return;

            JsonObject json;
            try
            {
                json = JsonSerializer.SerializeToNode(save, runTypeInfo)!.AsObject();
            }
            finally
            {
                if (injectedLedger is not null)
                {
                    save.Modifiers.Remove(injectedLedger);
                }
            }

            if (seq != Volatile.Read(ref _checkpointSeq)) return;

            checkpoint = BugReplayCodec.Encode(json, playerIds, metadata);
            if (parentEventId is not null)
            {
                checkpoint["event_combat"] = parentEventId;
            }
        }
        catch (Exception ex)
        {
            checkpoint = new JsonObject
            {
                ["available"] = false,
                ["reason"] = "capture_failed",
                ["error_type"] = ex.GetType().FullName,
            };
        }

        lock (PendingGate)
        {
            if (seq != Volatile.Read(ref _checkpointSeq)) return;
            Current = checkpoint;
            _latestCheckpoint = (runRef, checkpoint);
            _completedCheckpoint = (runRef, checkpoint);
            Volatile.Write(ref _hasCompletedCapture, 1);
        }
    }

    internal static void BeginCaptureCombatReplay(RunState? run)
    {
        try
        {
            var replayWriter = RunManager.Instance.CombatReplayWriter;
            if (replayWriter is null || WriterReplayField?.GetValue(replayWriter) is not CombatReplay replay || replay.serializableRun is null)
            {
                return;
            }

            // Snapshot list references on the main thread in microseconds; run anonymization,
            // PacketWriter serialization, SHA-256, GZip, and Base64 encoding on a worker thread.
            CombatReplay snapshot = SnapshotReplay(replay);
            long seq = Interlocked.Increment(ref _replaySeq);
            WeakReference<RunState>? runRef = run is null ? null : new WeakReference<RunState>(run);
            Task encodeTask = Task.Run(() => EncodeCombatReplayWorker(seq, runRef, snapshot));
            lock (PendingGate)
            {
                _pendingReplayTask = encodeTask;
            }
        }
        catch (Exception)
        {
            // Combat replay capture is optional and must never disrupt transitions or F2 submission.
        }
    }

    private static void EncodeCombatReplayWorker(long seq, WeakReference<RunState>? runRef, CombatReplay snapshot)
    {
        try
        {
            if (seq != Volatile.Read(ref _replaySeq)) return;
            var encoded = EncodeCombatReplayInstance(snapshot);
            if (encoded["available"]?.GetValue<bool>() != true) return;

            lock (PendingGate)
            {
                if (seq != Volatile.Read(ref _replaySeq)) return;
                CurrentCombatReplay = encoded;
                _completedReplay = (runRef, encoded);
                Volatile.Write(ref _hasCompletedCapture, 1);
            }
        }
        catch (Exception)
        {
            // Background combat replay encoding failure is non-fatal.
        }
    }

    private static CombatReplay SnapshotReplay(CombatReplay replay) => new()
    {
        version = replay.version,
        gitCommit = replay.gitCommit,
        modelIdHash = replay.modelIdHash,
        choiceIds = replay.choiceIds is null ? new List<uint>() : new List<uint>(replay.choiceIds),
        rewardIds = replay.rewardIds is null ? new List<int>() : new List<int>(replay.rewardIds),
        nextActionId = replay.nextActionId,
        nextChecksumId = replay.nextChecksumId,
        nextHookId = replay.nextHookId,
        serializableRun = replay.serializableRun,
        events = replay.events is null ? new List<CombatReplayEvent>() : new List<CombatReplayEvent>(replay.events),
        checksumData = replay.checksumData is null ? new List<ReplayChecksumData>() : new List<ReplayChecksumData>(replay.checksumData),
    };

    internal static bool TryTakeCompletedCheckpoint(RunState? activeRun, out JsonObject? checkpoint)
    {
        lock (PendingGate)
        {
            if (_completedCheckpoint is { } pending)
            {
                _completedCheckpoint = null;
                UpdateHasPendingFlagLocked();
                if (activeRun is not null && pending.RunRef.TryGetTarget(out var target) && ReferenceEquals(target, activeRun))
                {
                    checkpoint = pending.Checkpoint;
                    return true;
                }
            }
        }
        checkpoint = null;
        return false;
    }

    internal static bool TryTakeCompletedCombatReplay(RunState? activeRun, out JsonObject? combatReplay)
    {
        lock (PendingGate)
        {
            if (_completedReplay is { } pending)
            {
                _completedReplay = null;
                UpdateHasPendingFlagLocked();
                if (pending.RunRef is null
                    || (activeRun is not null && pending.RunRef.TryGetTarget(out var target) && ReferenceEquals(target, activeRun)))
                {
                    combatReplay = pending.Replay;
                    return true;
                }
            }
        }
        combatReplay = null;
        return false;
    }

    internal static JsonObject? GetLatestCheckpoint(RunState? activeRun)
    {
        if (activeRun is null) return null;
        lock (PendingGate)
        {
            if (_latestCheckpoint is { } latest
                && latest.RunRef.TryGetTarget(out var target)
                && ReferenceEquals(target, activeRun))
            {
                return (JsonObject)latest.Checkpoint.DeepClone();
            }
        }
        return null;
    }

    internal static JsonObject? GetLatestCombatReplay()
    {
        lock (PendingGate)
        {
            return (JsonObject?)CurrentCombatReplay?.DeepClone();
        }
    }

    internal static void WaitPendingCaptures(int timeoutMs = 5000)
    {
        Task cpTask;
        Task replayTask;
        lock (PendingGate)
        {
            cpTask = _pendingCheckpointTask;
            replayTask = _pendingReplayTask;
        }
        if (cpTask.IsCompleted && replayTask.IsCompleted) return;
        try
        {
            Task.WhenAll(cpTask, replayTask).Wait(Math.Clamp(timeoutMs, 50, 15_000));
        }
        catch (Exception)
        {
            // Explicit barrier waits best-effort without propagating background exceptions.
        }
    }

    internal static async Task FlushPendingAsync(int timeoutMs = 2500)
    {
        Task cpTask;
        Task replayTask;
        lock (PendingGate)
        {
            cpTask = _pendingCheckpointTask;
            replayTask = _pendingReplayTask;
        }
        if (cpTask.IsCompleted && replayTask.IsCompleted) return;
        try
        {
            await Task.WhenAll(cpTask, replayTask)
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 50, 15_000)))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Bounded barrier: F2 proceeds even if background encoding is unusually slow.
        }
        catch (Exception)
        {
            // Worker exceptions are handled inside each worker.
        }
    }

    private static void UpdateHasPendingFlagLocked()
    {
        if (_completedCheckpoint is null && _completedReplay is null)
        {
            Volatile.Write(ref _hasCompletedCapture, 0);
        }
    }

    internal static JsonObject CaptureCombatReplay()
    {
        try
        {
            var replayWriter = RunManager.Instance.CombatReplayWriter;
            if (replayWriter is null || WriterReplayField?.GetValue(replayWriter) is not CombatReplay replay || replay.serializableRun is null)
            {
                return GetLatestCombatReplay() ?? BugReplayCodec.UnavailableCombatReplay("no_active_combat_replay");
            }

            var encoded = EncodeCombatReplayInstance(replay);
            if (encoded["available"]?.GetValue<bool>() == true)
            {
                lock (PendingGate)
                {
                    CurrentCombatReplay = encoded;
                }
            }
            return encoded;
        }
        catch (Exception ex)
        {
            var failed = BugReplayCodec.UnavailableCombatReplay("replay_capture_failed");
            failed["error_type"] = LogSanitizer.Cap(ex.GetType().FullName ?? ex.GetType().Name, 200);
            return failed;
        }
    }

    internal static JsonObject EncodeCombatReplayInstance(CombatReplay replay)
    {
        var anonymized = replay.Anonymized();
        SerializableModifier? injectedReplayLedger = null;
        if (replay.serializableRun is not null && anonymized.serializableRun is not null)
        {
            if (!Crossroads.TryGetSerializedLedger(anonymized.serializableRun, out var anonLedger) || anonLedger is null)
            {
                if (Crossroads.TryGetSerializedLedger(replay.serializableRun, out var sourceLedger) && sourceLedger is not null)
                {
                    anonLedger = Crossroads.AnonymizeSerializedLedger(sourceLedger);
                    Crossroads.SetSerializedLedger(anonymized.serializableRun, anonLedger);
                }
            }
            if (anonLedger is not null && !anonymized.serializableRun.Modifiers.Any(m => Crossroads.IsLedgerId(m.Id)))
            {
                if (ReferenceEquals(anonymized.serializableRun.Modifiers, replay.serializableRun.Modifiers))
                {
                    anonymized.serializableRun.Modifiers = [.. replay.serializableRun.Modifiers];
                }
                anonymized.serializableRun.Modifiers.Add(anonLedger);
                injectedReplayLedger = anonLedger;
            }
        }

        try
        {
            var meta = new JsonObject
            {
                ["version"] = LogSanitizer.Cap(anonymized.version ?? "unknown", 80),
                ["model_id_hash"] = $"0x{anonymized.modelIdHash:x8}",
                ["event_count"] = anonymized.events?.Count ?? 0,
                ["choice_count"] = anonymized.choiceIds?.Count ?? 0,
                ["checksum_count"] = anonymized.checksumData?.Count ?? 0,
                ["checksums_stripped"] = false,
            };

            var writer = new PacketWriter { WarnOnGrow = false };
            writer.Reset();
            writer.Write(anonymized);
            writer.ZeroByteRemainder();

            var encoded = BugReplayCodec.EncodeCombatReplay(writer.Buffer.AsSpan(0, writer.BytePosition), meta);
            if (encoded["available"]?.GetValue<bool>() != true && anonymized.checksumData is { Count: > 0 })
            {
                // Long combats can accumulate large full-state checksum snapshots; strip them if needed
                // so the deterministic action/choice replay still fits within the wire limit.
                anonymized.checksumData = [];
                meta["checksums_stripped"] = true;
                writer.Reset();
                writer.Write(anonymized);
                writer.ZeroByteRemainder();
                encoded = BugReplayCodec.EncodeCombatReplay(writer.Buffer.AsSpan(0, writer.BytePosition), meta);
            }

            return encoded;
        }
        finally
        {
            if (injectedReplayLedger is not null && anonymized.serializableRun is not null)
            {
                anonymized.serializableRun.Modifiers.Remove(injectedReplayLedger);
            }
        }
    }

    private static CheckpointMetadata BuildRoomMetadata(RunState run, AbstractRoom room)
    {
        bool isAncient = room is EventRoom { CanonicalEvent: AncientEventModel };
        string entryKind = room switch
        {
            CombatRoom { RoomType: RoomType.Boss } => "boss_entry",
            CombatRoom { ParentEventId: not null } => "event_combat_entry",
            CombatRoom => "combat_entry",
            EventRoom when isAncient => "ancient_entry",
            EventRoom => "event_entry",
            MerchantRoom => "merchant_entry",
            RestSiteRoom => "rest_site_entry",
            TreasureRoom => "treasure_entry",
            _ => "room_entry",
        };

        string roomType = isAncient ? "Ancient" : room.RoomType.ToString();
        string? encounterId = (room as CombatRoom)?.Encounter?.Id.ToString();
        string? eventId = room is EventRoom eventRoom
            ? eventRoom.CanonicalEvent?.Id.ToString()
            : (room as CombatRoom)?.ParentEventId?.ToString();

        return new CheckpointMetadata(
            EntryKind: entryKind,
            RoomType: roomType,
            ActId: run.Act.Id.ToString(),
            ActIndex: run.CurrentActIndex,
            Floor: run.ActFloor,
            TotalFloor: run.TotalFloor,
            EncounterId: encounterId,
            EventId: eventId,
            Seed: run.Rng.StringSeed);
    }
}

[HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
internal static class ThingsReportPreRoomSavePatch
{
    private static void Prefix() => BugReplayCapture.StagePreRoomSave();
}

// Captures pre-entry state before AbstractRoom.EnterInternal mutates room/combat/event state.
// Does not replace, await, catch, or retry the original method.
[HarmonyPatch(typeof(AbstractRoom), nameof(AbstractRoom.Enter))]
internal static class ThingsReportCombatCheckpointPatch
{
    private static void Prefix(AbstractRoom __instance, bool isRestoringRoomStackBase)
    {
        if (!isRestoringRoomStackBase)
        {
            BugReplayCapture.Capture(__instance);
        }
    }
}
