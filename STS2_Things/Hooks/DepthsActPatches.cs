using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Acts;
using STS2_Things.Config;

namespace STS2_Things.Hooks;

// Models are discovered automatically, but the native list of selectable acts is explicit.
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Acts), MethodType.Getter)]
internal static class DepthsActCatalogPatch
{
    private static readonly Assembly BaseGameAssembly = typeof(ActModel).Assembly;
    private static ActModel[]? _cachedSourceSnapshot;
    private static ActModel[]? _cachedResult;

    /// <summary>
    /// Clears <c>ModelDb._actsByIndex</c> if another mod accessed it before our Harmony patch was
    /// installed, which would otherwise permanently cache Act 2 without <see cref="ThingsDepths"/>
    /// or in non-deterministic multi-mod order.
    /// </summary>
    internal static void InvalidateActsByIndexCache()
    {
        try
        {
            FieldInfo? field = AccessTools.Field(typeof(ModelDb), "_actsByIndex");
            if (field?.GetValue(null) is not IEnumerable<IEnumerable<ActModel>> cached)
                return;

            ActModel[] cachedFlattened = cached.SelectMany(group => group).ToArray();
            ActModel[] expected = ModelDb.Acts.Where(act => act.Index >= 0).ToArray();
            bool matchesExpected = cachedFlattened.Length == expected.Length &&
                cachedFlattened.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second));
            if (!matchesExpected)
            {
                field.SetValue(null, null);
                Log.Info("STS2_Things: invalidated stale ModelDb._actsByIndex cache so Act 2 ordering and ThingsDepths are deterministic.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"STS2_Things: failed to inspect/invalidate ModelDb._actsByIndex cache: {ex.Message}");
        }
    }

    internal static ActModel[] BuildDeterministicCatalog(IEnumerable<ActModel> source)
    {
        return source
            .Append(ModelDb.Act<ThingsDepths>())
            .Distinct()
            .OrderBy(act => act.Index)
            .ThenBy(act => act.IsDefault ? 0 : 1)
            .ThenBy(act => act.GetType().Assembly == BaseGameAssembly ? 0 : 1)
            .ThenBy(act => act.Id?.Entry ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(act => act.Id?.Category ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(act => act.GetType().FullName ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<ActModel> __result)
    {
        IEnumerable<ActModel> source = __result;
        if (source is IReadOnlyList<ActModel> list)
        {
            ActModel[]? cachedSnapshot = _cachedSourceSnapshot;
            ActModel[]? cachedResult = _cachedResult;
            if (cachedSnapshot is not null &&
                cachedResult is not null &&
                MatchesSnapshot(list, cachedSnapshot))
            {
                __result = cachedResult;
                return;
            }

            ActModel[] snapshot = new ActModel[list.Count];
            for (int i = 0; i < list.Count; i++)
                snapshot[i] = list[i];

            ActModel[] result = BuildDeterministicCatalog(snapshot);
            _cachedSourceSnapshot = snapshot;
            _cachedResult = result;
            __result = result;
            return;
        }

        __result = BuildDeterministicCatalog(source);
    }

    private static bool MatchesSnapshot(IReadOnlyList<ActModel> list, ActModel[] snapshot)
    {
        if (list.Count != snapshot.Length)
            return false;

        for (int i = 0; i < snapshot.Length; i++)
        {
            if (!ReferenceEquals(list[i], snapshot[i]))
                return false;
        }

        return true;
    }
}

// Postfix runs after vanilla ActModel.GetRandomList has already consumed rng identically,
// preserving downstream RNG determinism (e.g. RandomCharacter / RotatingCharacter in StartRunLobby).
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GetRandomList))]
internal static class DepthsGuaranteedActPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(bool isMultiplayer, ref IEnumerable<ActModel> __result)
    {
        if (ThingsModConfig.IsEnabled(ThingsModConfig.FeatureGuaranteedDepthsEnabled) ||
            DepthsActPityTracker.ShouldForceDepthsInActRoll(isMultiplayer))
        {
            ForceDepthsInActList(ref __result);
        }
    }

    internal static void ApplyExplicitGuaranteedDepths(ref IEnumerable<ActModel> acts)
    {
        if (!ThingsModConfig.IsEnabled(ThingsModConfig.FeatureGuaranteedDepthsEnabled))
            return;

        ForceDepthsInActList(ref acts);
    }

    internal static void ApplyGuaranteedDepths(ref IEnumerable<ActModel> acts)
    {
        ApplyExplicitGuaranteedDepths(ref acts);
    }

    private static void ForceDepthsInActList(ref IEnumerable<ActModel> acts)
    {
        if (acts is null)
            return;

        List<ActModel> list = acts.ToList();
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();

        int targetIndex = list.FindIndex(act => act.Index == 1);
        if (targetIndex >= 0)
        {
            list[targetIndex] = depths;
        }
        else if (list.Count > 1)
        {
            list[1] = depths;
        }

        acts = list;
    }
}

// Captures the lobby GameMode around StartRunLobby.BeginRunLocally so Daily runs stay
// exempt from anti-streak pity while preserving vanilla RNG consumption.
[HarmonyPatch(typeof(StartRunLobby), "BeginRunLocally")]
internal static class DepthsRunStartContextPatch
{
    private static void Prefix(StartRunLobby __instance, out DepthsActPityTracker.RunStartScope __state)
    {
        __state = DepthsActPityTracker.PushRunStartGameMode(__instance.GameMode);
    }

    private static void Finalizer(DepthsActPityTracker.RunStartScope __state)
    {
        DepthsActPityTracker.PopRunStartGameMode(__state);
    }
}

// Commits the anti-streak pity counter only when a run actually enters Act 2, after any
// ActLikeIt2 Fork swap has finalized State.Acts[1] and never on save/reload into Act 2.
[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetActInternal))]
internal static class DepthsActEntryPatch
{
    private static void Prefix(RunManager __instance, int actIndex)
    {
        DepthsActPityTracker.RecordAct2Entry(__instance, actIndex);
    }
}

// Vanilla derives its achievement enum from the act ID. A mod act has no matching
// platform achievement; do not map it onto Hive's completion or parse a nonexistent enum.
[HarmonyPatch(typeof(AchievementsHelper), nameof(AchievementsHelper.CheckForDefeatedAllEnemiesAchievement))]
internal static class DepthsVanillaAchievementPatch
{
    private static bool Prefix(ActModel act) => act is not ThingsDepths;
}
