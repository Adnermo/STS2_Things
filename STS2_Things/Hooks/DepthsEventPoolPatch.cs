using HarmonyLib;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Events;

namespace STS2_Things.Hooks;

// GenerateRooms appends native shared events independently of AllEvents.
// Also clean a persisted pool immediately before drawing, so existing runs
// receive the same policy without changing completed rooms or other acts.
#if STS2_V113_1
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms),
    [typeof(Rng), typeof(UnlockState), typeof(bool), typeof(bool)])]
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
    private const string FallbackRngStream = "things_depths_event_fallback";

    private static bool IsBaseGameEvent(EventModel model) =>
        model.GetType().Assembly == typeof(ActModel).Assembly;

    private static bool IsMultiplayerOnlyEvent(EventModel model) =>
        model is ThingsBronzeCauldron or ThingsSpikedDoor;

    private static bool IsUsableEvent(EventModel model, RunState runState) =>
        model.IsAllowed(runState) && !runState.VisitedEventIds.Contains(model.Id);

    private static bool IsUsableModEvent(EventModel model, RunState runState) =>
        !IsBaseGameEvent(model) && IsUsableEvent(model, runState);

    private static int NormalizeIndex(int index, int count)
    {
        if (count <= 0) return 0;
        int mod = index % count;
        return mod < 0 ? mod + count : mod;
    }

    private static void RemoveLockedEpochEvents(List<EventModel> pool, UnlockState? unlockState)
    {
        if (unlockState == null) return;
        if (!unlockState.IsEpochRevealed<Event1Epoch>())
            pool.RemoveAll(e => Event1Epoch.Events.Any(ev => ev.Id == e.Id));
        if (!unlockState.IsEpochRevealed<Event2Epoch>())
            pool.RemoveAll(e => Event2Epoch.Events.Any(ev => ev.Id == e.Id));
        if (!unlockState.IsEpochRevealed<Event3Epoch>())
            pool.RemoveAll(e => Event3Epoch.Events.Any(ev => ev.Id == e.Id));
    }

    private static List<EventModel> BuildShuffledFallbackEvents(RunState runState)
    {
        List<EventModel> fallbackPool = ModelDb.AllSharedEvents
            .Concat(ModelDb.Act<Hive>().AllEvents.Where(IsBaseGameEvent))
            .Distinct()
            .OrderBy(e => e.Id.Entry, StringComparer.Ordinal)
            .ToList();
        RemoveLockedEpochEvents(fallbackPool, runState.UnlockState);
        // Derive a dedicated stream from runState.Rng.Seed so fallback order is
        // uniformly randomized per run, identical across multiplayer peers, and
        // consumes zero draws from any vanilla RNG stream.
        Rng fallbackRng = new(runState.Rng.Seed, FallbackRngStream);
        fallbackPool.UnstableShuffle(fallbackRng);
        return fallbackPool;
    }

    private static void StripBaseGameEventsPreservingCursor(RoomSet rooms)
    {
        List<EventModel> events = rooms.events;
        if (events.Count == 0 || !events.Any(IsBaseGameEvent))
            return;

        int currentIndex = NormalizeIndex(rooms.eventsVisited, events.Count);
        int keptBeforeCursor = 0;
        for (int i = 0; i < currentIndex; i++)
        {
            if (!IsBaseGameEvent(events[i]))
                keptBeforeCursor++;
        }

        events.RemoveAll(IsBaseGameEvent);
        rooms.eventsVisited = events.Count > 0 ? keptBeforeCursor % events.Count : 0;
    }

    internal static void RemoveNativeEvents(ActModel act, RunState? runState = null, bool? isMultiplayer = null)
    {
        if (act is not ThingsDepths depths) return;
        RoomSet rooms = depths.Rooms;
        List<EventModel> events = rooms.events;

        if (events.Count == 0)
        {
            List<EventModel> seedPool = depths.AllEvents
                .Concat(ModelDb.AllSharedEvents)
                .Distinct()
                .ToList();
            if (runState != null)
            {
                seedPool.Sort((a, b) => string.CompareOrdinal(a.Id.Entry, b.Id.Entry));
                RemoveLockedEpochEvents(seedPool, runState.UnlockState);
                seedPool.UnstableShuffle(new Rng(runState.Rng.Seed, FallbackRngStream));
            }
            events.AddRange(seedPool);
            rooms.eventsVisited = 0;
        }

        if (runState == null)
        {
            bool hasModeModEvent = events.Any(e =>
                !IsBaseGameEvent(e) && (isMultiplayer != false || !IsMultiplayerOnlyEvent(e)));
            if (hasModeModEvent)
            {
                StripBaseGameEventsPreservingCursor(rooms);
            }
            return;
        }

        if (events.Any(e => IsUsableModEvent(e, runState)))
        {
            StripBaseGameEventsPreservingCursor(rooms);
            return;
        }

        if (events.Any(e => IsUsableEvent(e, runState)))
        {
            return;
        }

        // Normalize cursor before extending the pool so appending fallback events
        // never shifts eventsVisited % events.Count into a skewed offset.
        rooms.eventsVisited = NormalizeIndex(rooms.eventsVisited, events.Count);
        HashSet<ModelId> existingIds = events.Select(e => e.Id).ToHashSet();
        foreach (EventModel fallback in BuildShuffledFallbackEvents(runState))
        {
            if (existingIds.Add(fallback.Id))
            {
                events.Add(fallback);
            }
        }
    }
}
