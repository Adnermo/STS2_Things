using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Cards;
using STS2_Things.Config;

namespace STS2_Things.Hooks;

/// <summary>
/// 模组自制职业牌的可配置门控策略。
///
/// 设计约束：
///   - 保持 <see cref="CardPoolModel.AllCards"/> 与 <see cref="ModelDb.AllCards"/> 完整，
///     避免影响已有存档加载、<c>ModelIdSerializationCache</c>、确定性排序与图鉴条目；
///   - 在运行期卡池入口 <see cref="CardPoolModel.GetUnlockedCards"/> 过滤已禁用的 5 张自制职业牌，
///     同步覆盖战斗奖励、普通/闪电商店、变化、药水、随机生成牌/能力/遗物与事件抽池；
///   - 在卡牌图鉴 <see cref="NCardLibraryGrid.RefreshVisibility"/> 期间放行，使禁用卡牌在图鉴中仍保持已解锁可见；
///   - 不干预事件专属牌（<see cref="ThingsSurrender"/>）及怪物/Boss 衍生状态牌（TokenCardPool）。
/// </summary>
public static class CustomCardPoolPolicy
{
    [ThreadStatic]
    private static int _unfilteredCatalogDepth;

    private static readonly IReadOnlyDictionary<Type, string> ConfigurableCardKeys =
        new Dictionary<Type, string>
        {
            [typeof(ThingsCollision)] = ThingsModConfig.CardCollisionEnabled,
            [typeof(ThingsReuse)] = ThingsModConfig.CardReuseEnabled,
            [typeof(ThingsSoulfyshDisease)] = ThingsModConfig.CardSoulfyshDiseaseEnabled,
            [typeof(ThingsRecall)] = ThingsModConfig.CardRecallEnabled,
            [typeof(ThingsPackUp)] = ThingsModConfig.CardPackUpEnabled,
        };

    internal static bool IsUnfilteredCatalogScope => _unfilteredCatalogDepth > 0;

    internal static void EnterUnfilteredCatalogScope() => _unfilteredCatalogDepth++;

    internal static void ExitUnfilteredCatalogScope()
    {
        _unfilteredCatalogDepth--;
        if (_unfilteredCatalogDepth < 0)
            _unfilteredCatalogDepth = 0;
    }

    public static bool AreAllConfigurableCardsEnabled() => ThingsModConfig.AllCustomCardsEnabled;

    public static bool IsCardAllowed(CardModel? card)
    {
        if (card is null)
            return true;
        return !ConfigurableCardKeys.TryGetValue(card.GetType(), out string? configKey) ||
               ThingsModConfig.IsCustomCardEnabled(configKey);
    }

    public static IEnumerable<CardModel> FilterUnlockedCards(IEnumerable<CardModel> source)
    {
        if (source is null || IsUnfilteredCatalogScope || ThingsModConfig.AllCustomCardsEnabled)
            return source!;
        return source.Where(IsCardAllowed).ToList();
    }
}

[HarmonyPatch(
    typeof(CardPoolModel),
    nameof(CardPoolModel.GetUnlockedCards),
    typeof(UnlockState),
    typeof(CardMultiplayerConstraint))]
internal static class CardPoolUnlockedCardsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not (IroncladCardPool or DefectCardPool or SilentCardPool))
            return;
        if (ThingsModConfig.AllCustomCardsEnabled || CustomCardPoolPolicy.IsUnfilteredCatalogScope)
            return;
        __result = CustomCardPoolPolicy.FilterUnlockedCards(__result);
    }
}

[HarmonyPatch]
internal static class UnfilteredCardLibraryScopePatches
{
    [HarmonyPatch(typeof(NCardLibraryGrid), nameof(NCardLibraryGrid.RefreshVisibility))]
    [HarmonyPrefix]
    private static void BeforeRefreshVisibility() => CustomCardPoolPolicy.EnterUnfilteredCatalogScope();

    [HarmonyPatch(typeof(NCardLibraryGrid), nameof(NCardLibraryGrid.RefreshVisibility))]
    [HarmonyFinalizer]
    private static void AfterRefreshVisibility() => CustomCardPoolPolicy.ExitUnfilteredCatalogScope();
}
