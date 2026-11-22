using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Acts;
using STS2_Things.Config;

namespace STS2_Things.Hooks;

// Models are discovered automatically, but the native list of selectable acts is explicit.
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Acts), MethodType.Getter)]
internal static class DepthsActCatalogPatch
{
    private static IEnumerable<ActModel>? _cachedSourceRef;
    private static int _cachedSourceCount = -1;
    private static ActModel[]? _cachedResult;

    /// <summary>
    /// Clears <c>ModelDb._actsByIndex</c> if another mod accessed it before our Harmony patch was
    /// installed, which would otherwise permanently cache Act 2 without <see cref="ThingsDepths"/>.
    /// </summary>
    internal static void InvalidateActsByIndexCache()
    {
        try
        {
            FieldInfo? field = AccessTools.Field(typeof(ModelDb), "_actsByIndex");
            if (field?.GetValue(null) is not IEnumerable<IEnumerable<ActModel>> cached)
                return;

            bool hasDepths = cached.Any(group => group.Any(act => act is ThingsDepths));
            if (!hasDepths)
            {
                field.SetValue(null, null);
                Log.Info("STS2_Things: invalidated stale ModelDb._actsByIndex cache so ThingsDepths is discoverable.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"STS2_Things: failed to inspect/invalidate ModelDb._actsByIndex cache: {ex.Message}");
        }
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<ActModel> __result)
    {
        IEnumerable<ActModel> source = __result;
        int count = source switch
        {
            ICollection<ActModel> col => col.Count,
            IReadOnlyCollection<ActModel> roCol => roCol.Count,
            _ => -1,
        };
        if (_cachedResult != null &&
            ReferenceEquals(_cachedSourceRef, source) &&
            _cachedSourceCount == count)
        {
            __result = _cachedResult;
            return;
        }

        ActModel[] result = source.Append(ModelDb.Act<ThingsDepths>()).Distinct()
            .OrderBy(act => act.Index).ThenBy(act => act.IsDefault ? 0 : 1).ToArray();
        _cachedSourceRef = source;
        _cachedSourceCount = count;
        _cachedResult = result;
        __result = result;
    }
}

// Postfix runs after vanilla ActModel.GetRandomList has already consumed rng identically,
// preserving downstream RNG determinism (e.g. RandomCharacter / RotatingCharacter in StartRunLobby).
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GetRandomList))]
internal static class DepthsGuaranteedActPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<ActModel> __result)
    {
        if (!ThingsModConfig.IsEnabled(ThingsModConfig.FeatureGuaranteedDepthsEnabled))
            return;

        if (__result is null)
            return;

        List<ActModel> list = __result.ToList();
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

        __result = list;
    }
}

// Vanilla derives its achievement enum from the act ID. A mod act has no matching
// platform achievement; do not map it onto Hive's completion or parse a nonexistent enum.
[HarmonyPatch(typeof(AchievementsHelper), nameof(AchievementsHelper.CheckForDefeatedAllEnemiesAchievement))]
internal static class DepthsVanillaAchievementPatch
{
    private static bool Prefix(ActModel act) => act is not ThingsDepths;
}
