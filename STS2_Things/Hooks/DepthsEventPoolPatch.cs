using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Acts;
using STS2_Things.Events;

namespace STS2_Things.Hooks;

// GenerateRooms appends native shared events independently of AllEvents.
// Also clean a persisted pool immediately before drawing, so existing runs
// receive the same policy without changing completed rooms or other acts.
#if STS2_V113_1
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms),
    [typeof(MegaCrit.Sts2.Core.Random.Rng), typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), typeof(bool), typeof(bool)])]
#else
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
#endif
internal static class DepthsGeneratedEventsPatch
{
    private static void Postfix(ActModel __instance, bool isMultiplayer) =>
        DepthsEventPool.RemoveNativeEvents(__instance, runState: null, isMultiplayer: isMultiplayer);
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
internal static class DepthsSavedEventsPatch
{
    private static void Prefix(ActModel __instance, RunState runState) =>
        DepthsEventPool.RemoveNativeEvents(__instance, runState: runState, isMultiplayer: runState.Players.Count > 1);
}

internal static class DepthsEventPool
{
    private static bool IsBaseGameEvent(EventModel model) =>
        model.GetType().Assembly == typeof(ActModel).Assembly;

    private static bool IsMultiplayerOnlyEvent(EventModel model) =>
        model is ThingsBronzeCauldron or ThingsSpikedDoor;

    internal static void RemoveNativeEvents(ActModel act, RunState? runState = null, bool? isMultiplayer = null)
    {
        if (act is not ThingsDepths depths) return;
        List<EventModel> events = depths.Rooms.events;
        if (events.Count == 0)
        {
            events.AddRange(depths.AllEvents.Concat(ModelDb.AllSharedEvents).Distinct());
        }

        bool hasUsableModEvent = runState != null
            ? events.Any(e => !IsBaseGameEvent(e) && e.IsAllowed(runState))
            : events.Any(e => !IsBaseGameEvent(e) && (isMultiplayer != false || !IsMultiplayerOnlyEvent(e)));

        if (hasUsableModEvent)
        {
            events.RemoveAll(IsBaseGameEvent);
            return;
        }

        if (runState != null && !events.Any(e => e.IsAllowed(runState)))
        {
            foreach (EventModel fallback in ModelDb.AllSharedEvents
                         .Concat(ModelDb.Act<Hive>().AllEvents.Where(IsBaseGameEvent))
                         .Distinct())
            {
                if (!events.Contains(fallback) && fallback.IsAllowed(runState))
                {
                    events.Add(fallback);
                }
            }
        }
    }
}
