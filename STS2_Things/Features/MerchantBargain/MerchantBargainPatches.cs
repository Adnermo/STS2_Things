using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2_Things.Features.MerchantBargain;

[HarmonyPatch]
internal static class MerchantEntryOnTryPurchaseWrapperPatch
{
#if STS2_V113
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(MegaCrit.Sts2.Core.Multiplayer.Game.MerchantSynchronizer),
        nameof(MegaCrit.Sts2.Core.Multiplayer.Game.MerchantSynchronizer.PurchaseLocalEntry),
        [typeof(MerchantEntry)])!;

    private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.MerchantSynchronizer __instance,
        MerchantEntry entry, ref Task<bool> __result)
    {
        if (!MerchantBargainManager.TryIntercept(entry, __instance.GetInventoryFor(entry.Player),
                ignoreCost: false, out Task<bool> bargainResult)) return true;
        __result = bargainResult;
        return false;
    }
#else
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper),
        [typeof(MerchantInventory), typeof(bool)])!;

    private static bool Prefix(
        MerchantEntry __instance,
        MerchantInventory? inventory,
        bool ignoreCost,
        ref Task<bool> __result)
    {
        if (!MerchantBargainManager.TryIntercept(
                __instance,
                inventory,
                ignoreCost,
                out Task<bool> bargainResult))
        {
            return true;
        }

        __result = bargainResult;
        return false;
    }
#endif
}

[HarmonyPatch]
internal static class MerchantEntryCostPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(MerchantEntry), nameof(MerchantEntry.Cost))
            ?? throw new MissingMethodException(typeof(MerchantEntry).FullName, nameof(MerchantEntry.Cost));
    }

    private static bool Prefix(MerchantEntry __instance, ref int __result)
    {
        if (!MerchantBargainManager.TryGetPriceOverride(__instance, out int price))
        {
            return true;
        }

        __result = price;
        return false;
    }
}

#if STS2_V113
[HarmonyPatch(typeof(NStandardMerchantRoom), nameof(NStandardMerchantRoom.AssetPaths), MethodType.Getter)]
#else
[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom.AssetPaths), MethodType.Getter)]
#endif
internal static class MerchantBargainAssetPreloadPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<string> __result)
    {
        __result = __result
            .Concat(MerchantBargainAssets.PreloadPaths)
            .Distinct(StringComparer.Ordinal);
    }
}
