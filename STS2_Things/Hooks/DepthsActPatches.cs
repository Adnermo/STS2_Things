using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Acts;

namespace STS2_Things.Hooks;

// Models are discovered automatically, but the native list of selectable acts is explicit.
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Acts), MethodType.Getter)]
internal static class DepthsActCatalogPatch
{
    private static IEnumerable<ActModel>? _cachedSourceRef;
    private static int _cachedSourceCount = -1;
    private static ActModel[]? _cachedResult;

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

// Vanilla derives its achievement enum from the act ID. A mod act has no matching
// platform achievement; do not map it onto Hive's completion or parse a nonexistent enum.
[HarmonyPatch(typeof(AchievementsHelper), nameof(AchievementsHelper.CheckForDefeatedAllEnemiesAchievement))]
internal static class DepthsVanillaAchievementPatch
{
    private static bool Prefix(ActModel act) => act is not ThingsDepths;
}
