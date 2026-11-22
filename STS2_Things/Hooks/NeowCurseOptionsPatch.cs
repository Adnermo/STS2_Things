using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using STS2_Things.Config;
using STS2_Things.Platform;
using STS2_Things.Relics;

namespace STS2_Things.Hooks;

/// <summary>
/// Appends enabled <c>STS2_Things</c> starter curse-tier relics to <see cref="Neow"/>'s
/// <c>CurseOptions</c> using the native <see cref="AncientEventModel"/> relic-option factory.
/// </summary>
[HarmonyPatch]
public static class NeowCurseOptionsPatch
{
    [HarmonyTargetMethod]
    public static MethodBase Target(HarmonyPatchType p, Harmony instance)
    {
        _ = p;
        _ = instance;
        return AccessTools.PropertyGetter(typeof(Neow), "CurseOptions")!;
    }

    private static void Postfix(Neow __instance, ref IEnumerable<EventOption> __result)
    {
        var list = __result.ToList();
        if (ThingsModConfig.IsEnabled(ThingsModConfig.NeowRelicCurseRemoverEnabled))
            AddRelicToList<ThingsCurseRemover>(__instance, list);
        if (ThingsModConfig.IsEnabled(ThingsModConfig.NeowRelicWhiteFlagEnabled))
            AddRelicToList<ThingsWhiteFlag>(__instance, list);
        if (ThingsModConfig.IsEnabled(ThingsModConfig.NeowRelicMagicGloveEnabled))
            AddRelicToList<ThingsMagicGlove>(__instance, list);

        // This conflict belongs to live Neow runs, not canonical AllPossibleOptions (Relic Collection / NeowsBones).
        if (__instance.IsMutable && __instance.Owner != null &&
            list.Any(option => option.Relic is ThingsMagicGlove))
        {
            list.RemoveAll(option => option.Relic?.Id.Entry == "PRECARIOUS_SHEARS");
        }

        __result = list;
    }

    private static void AddRelicToList<T>(Neow neow, List<EventOption> list) where T : RelicModel
    {
        var relic = ModelDb.Relic<T>().ToMutable();
        if (neow.Owner is { } owner)
        {
            if (!relic.IsAllowed(owner.RunState))
                return;
            relic.Owner = owner;
        }

        list.Add(EngineAccess.CreateAncientRelicOption(neow, relic));
    }
}
