using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Acts;

namespace STS2_Things.Hooks;

// GenerateRooms appends native shared events independently of AllEvents.
// Also clean a persisted pool immediately before drawing, so existing runs
// receive the same policy without changing completed rooms or other acts.
#if STS2_V113
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
    internal static void RemoveNativeEvents(ActModel act)
    {
        if (act is not Depths) return;
        foreach (EventModel model in ModelDb.AllEvents.Where(e => e.GetType().Assembly == typeof(ActModel).Assembly))
            act.RemoveEventFromSet(model);
    }
}
