using HarmonyLib;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Modifiers;
using STS2_Things.Platform;

namespace STS2_Things.Map;

[HarmonyPatch(typeof(RunState), "CreateShared")]
internal static class CrossroadRunPatch
{
    private static void Prefix(ref IReadOnlyList<ModifierModel> modifiers, out ThingsCrossroads? __state)
    {
        __state = modifiers.OfType<ThingsCrossroads>().FirstOrDefault();
        if (modifiers.Any(m => m is ThingsCrossroads))
            modifiers = modifiers.Where(m => m is not ThingsCrossroads).ToArray();
    }

    private static void Postfix(RunState __result, ThingsCrossroads? __state)
    {
        if (Sts2VersionSeam.IsBlitz(__result))
            return;

        Crossroads.Attach(__result, __state ?? (ThingsCrossroads)ModelDb.Modifier<ThingsCrossroads>().ToMutable());
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterMapGenerated))]
internal static class CrossroadMapGeneratedHookPatch
{
    private static void Postfix(IRunState runState, ActMap map, int actIndex, ref Task __result)
    {
        if (Sts2VersionSeam.IsBlitz(runState))
            return;

        if (runState is RunState run)
            __result = EnsurePlanAfterHooksAsync(__result, run, map, actIndex);
    }

    private static async Task EnsurePlanAfterHooksAsync(Task pending, RunState run, ActMap map, int actIndex)
    {
        await pending;
        Crossroads.Ledger(run)?.EnsurePlan(run, map, actIndex);
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class CrossroadFromSerializablePatch
{
    private static void Prefix(SerializableRun save) =>
        Crossroads.ExtractSerializedLedger(save);

    private static void Postfix(SerializableRun save, RunState __result)
    {
        if (Sts2VersionSeam.IsBlitz(__result))
            return;

        if (Crossroads.TryGetSerializedLedger(save, out var savedMod) && savedMod != null)
        {
            var restored = (ThingsCrossroads)ModifierModel.FromSerializable(savedMod);
            Crossroads.Attach(__result, restored);
        }

        CrossroadSaveLoadPatches.CaptureCanonicalizedLedger(__result);
    }
}

[HarmonyPatch]
internal static class CrossroadSaveLoadPatches
{
    [ThreadStatic]
    private static int _canonicalizingDepth;

    [ThreadStatic]
    private static SerializableModifier? _canonicalizedLedger;

    private static bool IsBlitzSave(SerializableRun? save) =>
#if STS2_V113_1
        save is { GameMode: GameMode.Blitz };
#else
        false;
#endif

    internal static void CaptureCanonicalizedLedger(RunState run)
    {
        if (Sts2VersionSeam.IsBlitz(run))
            return;

        if (_canonicalizingDepth > 0 && Crossroads.Ledger(run) is { } ledger)
            _canonicalizedLedger = ledger.ToSerializable();
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.LoadRunSave))]
    [HarmonyPostfix]
    private static void AfterLoadRunSave(ReadSaveResult<SerializableRun>? __result)
    {
        if (__result is { Success: true, SaveData: { } save })
            Crossroads.ExtractSerializedLedger(save);
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.LoadMultiplayerRunSave))]
    [HarmonyPostfix]
    private static void AfterLoadMultiplayerRunSave(ReadSaveResult<SerializableRun>? __result)
    {
        if (__result is { Success: true, SaveData: { } save })
            Crossroads.ExtractSerializedLedger(save);
    }

    [HarmonyPatch(typeof(SerializableRun), nameof(SerializableRun.Deserialize))]
    [HarmonyPostfix]
    private static void AfterDeserializePacket(SerializableRun __instance) =>
        Crossroads.ExtractSerializedLedger(__instance);

    [HarmonyPatch(typeof(SerializableRun), nameof(SerializableRun.Anonymized))]
    [HarmonyPostfix]
    private static void AfterAnonymized(SerializableRun __instance, SerializableRun __result)
    {
        if (ReferenceEquals(__result.Modifiers, __instance.Modifiers))
            __result.Modifiers = [.. __instance.Modifiers];
        if (IsBlitzSave(__result))
            return;
        if (Crossroads.TryGetSerializedLedger(__instance, out var mod) && mod != null)
            Crossroads.SetSerializedLedger(__result, Crossroads.AnonymizeSerializedLedger(mod));
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ToSave), [typeof(AbstractRoom)])]
    [HarmonyPostfix]
    private static void AfterToSave(RunManager __instance, SerializableRun __result)
    {
        if (IsBlitzSave(__result))
            return;
        if (__instance.DebugOnlyGetState() is { } state &&
            !Sts2VersionSeam.IsBlitz(state) &&
            Crossroads.Ledger(state) is { } ledger)
            Crossroads.SetSerializedLedger(__result, ledger.ToSerializable());
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CanonicalizeSave))]
    [HarmonyPrefix]
    private static void BeforeCanonicalizeSave(SerializableRun save)
    {
        Crossroads.ExtractSerializedLedger(save);
        _canonicalizingDepth++;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CanonicalizeSave))]
    [HarmonyPostfix]
    private static void AfterCanonicalizeSave(SerializableRun __result)
    {
        if (IsBlitzSave(__result))
            return;
        if (_canonicalizedLedger != null)
            Crossroads.SetSerializedLedger(__result, _canonicalizedLedger);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CanonicalizeSave))]
    [HarmonyFinalizer]
    private static void FinalizeCanonicalizeSave()
    {
        _canonicalizingDepth--;
        if (_canonicalizingDepth <= 0)
        {
            _canonicalizingDepth = 0;
            _canonicalizedLedger = null;
        }
    }

    [HarmonyPatch(typeof(SerializableRun), nameof(SerializableRun.Serialize), [typeof(PacketWriter)])]
    [HarmonyPrefix]
    private static void BeforeSerializePacket(SerializableRun __instance, out SerializableModifier? __state)
    {
        __state = null;
        if (IsBlitzSave(__instance))
        {
            __instance.Modifiers.RemoveAll(m => Crossroads.IsLedgerId(m.Id));
            return;
        }
        if (!Crossroads.TryGetSerializedLedger(__instance, out var mod) &&
            _canonicalizingDepth > 0 && _canonicalizedLedger != null)
        {
            mod = _canonicalizedLedger;
            Crossroads.SetSerializedLedger(__instance, mod);
        }

        if (mod != null && !__instance.Modifiers.Any(m => Crossroads.IsLedgerId(m.Id)))
        {
            __instance.Modifiers.Add(mod);
            __state = mod;
        }
    }

    [HarmonyPatch(typeof(SerializableRun), nameof(SerializableRun.Serialize), [typeof(PacketWriter)])]
    [HarmonyFinalizer]
    private static void FinalizeSerializePacket(SerializableRun __instance, SerializableModifier? __state)
    {
        if (__state != null)
            __instance.Modifiers.Remove(__state);
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), [typeof(SerializableRun), typeof(bool)])]
    [HarmonyPrefix]
    private static void BeforeSaveRun(SerializableRun save, out SerializableModifier? __state)
    {
        __state = null;
        if (IsBlitzSave(save))
        {
            save.Modifiers.RemoveAll(m => Crossroads.IsLedgerId(m.Id));
            return;
        }
        if (Crossroads.TryGetSerializedLedger(save, out var mod) &&
            mod != null &&
            !save.Modifiers.Any(m => Crossroads.IsLedgerId(m.Id)))
        {
            save.Modifiers.Add(mod);
            __state = mod;
        }
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), [typeof(SerializableRun), typeof(bool)])]
    [HarmonyPostfix]
    private static void AfterSaveRun(SerializableRun save, SerializableModifier? __state, ref Task __result)
    {
        if (__state != null)
            __result = RemoveInjectedLedgerAfterSaveAsync(__result, save, __state);
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), [typeof(SerializableRun), typeof(bool)])]
    [HarmonyFinalizer]
    private static void FinalizeSaveRun(Exception? __exception, SerializableRun save, SerializableModifier? __state)
    {
        if (__exception != null && __state != null)
            save.Modifiers.Remove(__state);
    }

    private static async Task RemoveInjectedLedgerAfterSaveAsync(Task pending, SerializableRun save, SerializableModifier injected)
    {
        try
        {
            await pending;
        }
        finally
        {
            save.Modifiers.Remove(injected);
        }
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.AddVisitedMapCoord))]
internal static class CrossroadHistoryRecordPatch
{
    private static void Postfix(RunState __instance, bool __result)
    {
        if (__result && !Sts2VersionSeam.IsBlitz(__instance))
            Crossroads.Ledger(__instance)?.RecordHistory(__instance);
    }
}

// Native histories normally use row as their index. A side room adds another
// history entry on the same row, so use the saved visit order for these runs.
[HarmonyPatch(typeof(RunState), nameof(RunState.GetHistoryEntryFor))]
internal static class CrossroadHistoryLookupPatch
{
    private static bool Prefix(RunState __instance, MapLocation location, ref MapPointHistoryEntry? __result)
    {
        if (Sts2VersionSeam.IsBlitz(__instance)) return true;
        int? index = Crossroads.Ledger(__instance)?.HistoryIndex(location);
        if (!index.HasValue || location.actIndex < 0 || location.actIndex >= __instance.MapPointHistory.Count) return true;
        var history = __instance.MapPointHistory[location.actIndex];
        __result = index.Value < history.Count ? history[index.Value] : null;
        return false;
    }
}

[HarmonyPatch(typeof(MapTravel), nameof(MapTravel.GetTravelablePointsFrom))]
internal static class CrossroadFreeTravelPatch
{
    private static void Postfix(IRunState runState, MapPoint currentPoint, ref IEnumerable<MapPoint> __result)
    {
        if (runState is not RunState run || Sts2VersionSeam.IsBlitz(run)) return;
        // Free-travel relics replace Children with the next row. Retain a paid
        // horizontal option, while never making a visited room reachable again.
        var plan = Crossroads.Ledger(runState)?.EnsurePlan(run, runState.Map, runState.CurrentActIndex);
        var destinations = plan?.Roads.Where(r => r.IsOpen && r.From == currentPoint.coord && !run.VisitedMapCoords.Contains(r.To))
            .Select(r => runState.Map.GetPoint(r.To)).OfType<MapPoint>() ?? [];
        __result = __result.Concat(destinations).Distinct();
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class CrossroadMapDisplayPatch
{
    internal static readonly ConditionalWeakTable<NMapScreen, RunState> Owners = new();
    private static void Postfix(NMapScreen __instance, ActMap map)
    {
        if (Owners.TryGetValue(__instance, out var run)) NCrossroadLayer.Attach(__instance, run, map);
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Initialize))]
internal static class CrossroadMapOwnerPatch
{
    private static void Postfix(NMapScreen __instance, IRunState __0)
    {
        if (__0 is not RunState run) return;
        CrossroadMapDisplayPatch.Owners.Remove(__instance);
        CrossroadMapDisplayPatch.Owners.Add(__instance, run);
    }
}
