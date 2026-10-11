using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_Things.Diagnostics;

internal static class BugReplayCapture
{
    internal static JsonObject? Current { get; private set; }
    internal static void Capture(CombatRoom room)
    {
        try
        {
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run is null || room.IsPreFinished) return;
            // EnterInternal has not reset/shuffled player piles, generated monsters
            // or invoked BeforeCombatStart/BeforeHandDraw yet. Use the native codec.
            var save = RunManager.Instance.ToSave(null);
            if (room.ParentEventId is null) save.PreFinishedRoom = room.ToSerializable();
            var json = JsonSerializer.SerializeToNode(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>())!.AsObject();
            var checkpoint = BugReplayCodec.Encode(json, run.Players.Select(p => p.NetId).ToArray());
            checkpoint["kind"] = "combat_entry";
            checkpoint["encounter"] = room.Encounter.Id.ToString();
            checkpoint["act_index"] = run.CurrentActIndex;
            checkpoint["floor"] = run.ActFloor;
            checkpoint["total_floor"] = run.TotalFloor;
            checkpoint["seed"] = run.Rng.StringSeed;
            checkpoint["event_combat"] = room.ParentEventId?.ToString();
            Current = checkpoint;
            BugRunRecorder.RecordCheckpoint(checkpoint);
        }
        catch (Exception ex)
        {
            Current = new JsonObject { ["available"] = false, ["reason"] = "capture_failed",
                ["error_type"] = ex.GetType().FullName };
            BugRunRecorder.RecordCheckpoint(Current);
        }
    }
}

// An event after room.Enter is too late for failures in startup tasks. This
// narrow observer does not replace, await, catch or retry the original method.
[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.EnterInternal))]
internal static class ThingsReportCombatCheckpointPatch
{
    private static void Prefix(CombatRoom __instance) => BugReplayCapture.Capture(__instance);
}
