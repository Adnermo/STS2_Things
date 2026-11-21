using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// </summary>
internal static class BugReplayCapture
{
    private static readonly FieldInfo? WriterReplayField = AccessTools.Field(typeof(CombatReplayWriter), "_replay");
    private static (RunState Run, int ActIndex, int ActFloor, SerializableRun Save)? _stagedMapPointSave;

    internal static JsonObject? Current { get; private set; }
    internal static JsonObject? CurrentCombatReplay { get; private set; }

    internal static void ClearRunState()
    {
        _stagedMapPointSave = null;
        Current = null;
        CurrentCombatReplay = null;
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

            JsonObject json;
            try
            {
                json = JsonSerializer.SerializeToNode(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!.AsObject();
            }
            finally
            {
                if (injectedLedger is not null)
                {
                    save.Modifiers.Remove(injectedLedger);
                }
            }
            var metadata = BuildRoomMetadata(run, room);
            var checkpoint = BugReplayCodec.Encode(json, run.Players.Select(p => p.NetId).ToArray(), metadata);
            if (room is CombatRoom { ParentEventId: { } parentEvent })
            {
                checkpoint["event_combat"] = parentEvent.ToString();
            }

            Current = checkpoint;
            BugRunRecorder.RecordCheckpoint(checkpoint);
        }
        catch (Exception ex)
        {
            Current = new JsonObject
            {
                ["available"] = false,
                ["reason"] = "capture_failed",
                ["error_type"] = ex.GetType().FullName,
            };
            BugRunRecorder.RecordCheckpoint(Current);
        }
    }

    internal static JsonObject CaptureCombatReplay()
    {
        try
        {
            var replayWriter = RunManager.Instance.CombatReplayWriter;
            if (replayWriter is null || WriterReplayField?.GetValue(replayWriter) is not CombatReplay replay || replay.serializableRun is null)
            {
                return CurrentCombatReplay ?? BugReplayCodec.UnavailableCombatReplay("no_active_combat_replay");
            }

            var encoded = EncodeCombatReplayInstance(replay);
            if (encoded["available"]?.GetValue<bool>() == true)
            {
                CurrentCombatReplay = encoded;
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
