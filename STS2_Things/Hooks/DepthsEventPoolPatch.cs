using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Acts;

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
    private static void Postfix(ActModel __instance) => DepthsEventPool.RemoveNativeEvents(__instance);
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
internal static class DepthsSavedEventsPatch
{
    private static void Prefix(ActModel __instance) => DepthsEventPool.RemoveNativeEvents(__instance);
}

internal static class DepthsEventPool
{
    private static bool IsBaseGameEvent(EventModel model) =>
        model.GetType().Assembly == typeof(ActModel).Assembly;

    internal static void RemoveNativeEvents(ActModel act)
    {
        if (act is not ThingsDepths depths) return;
        List<EventModel> events = depths.Rooms.events;
        if (events.Count == 0)
        {
            events.AddRange(depths.AllEvents.Concat(ModelDb.AllSharedEvents).Distinct());
        }
        if (events.Any(e => !IsBaseGameEvent(e)))
        {
            events.RemoveAll(IsBaseGameEvent);
        }
    }
}
