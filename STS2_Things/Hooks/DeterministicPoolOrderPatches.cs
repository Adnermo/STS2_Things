using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace STS2_Things.Hooks;

/// <summary>
///     ModHelper 按 Mod 初始化顺序追加池内容，而联机只校验 Mod 集合、
///     不要求两端加载顺序相同。保留原版前缀顺序，仅用官方 ContentSorter 对原版池的
///     Mod 后缀排序，避免随机奖励把同一 RNG 索引映射到不同模型。
/// </summary>
internal static class DeterministicContentOrder
{
    private static class PoolOrderCache<TModel>
        where TModel : AbstractModel
    {
        internal sealed class CachedOrder
        {
            public required IEnumerable<TModel> SourceRef { get; init; }
            public required int SourceCount { get; init; }
            public required IEnumerable<TModel> Sorted { get; init; }
        }

        internal static readonly ConditionalWeakTable<AbstractModel, CachedOrder> Table = new();
    }

    private static int GetFastCount<TModel>(IEnumerable<TModel> source) => source switch
    {
        ICollection<TModel> col => col.Count,
        IReadOnlyCollection<TModel> roCol => roCol.Count,
        _ => -1,
    };

    public static IEnumerable<TModel> SortBaseThenMods<TModel>(IEnumerable<TModel> source)
        where TModel : AbstractModel
    {
        var baseGame = AssemblyInfo.BaseGame;
        if (baseGame == null || AssemblyInfo.ModMap == null)
            return source;

        IReadOnlyList<TModel> models;
        int moddedCount = 0;

        if (source is IReadOnlyList<TModel> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].GetType().Assembly != baseGame)
                    moddedCount++;
            }

            if (moddedCount <= 1)
                return source;

            models = list;
        }
        else
        {
            var collected = new List<TModel>();
            foreach (TModel model in source)
            {
                collected.Add(model);
                if (model.GetType().Assembly != baseGame)
                    moddedCount++;
            }

            if (moddedCount <= 1)
                return collected.Count == 0 ? Array.Empty<TModel>() : collected.ToArray();

            models = collected;
        }

        int totalCount = models.Count;
        var instancesByType = new Dictionary<Type, Queue<TModel>>(moddedCount);
        var moddedTypes = new Type[moddedCount];
        int moddedIndex = 0;
        for (int i = 0; i < totalCount; i++)
        {
            TModel model = models[i];
            Type modelType = model.GetType();
            if (modelType.Assembly == baseGame)
                continue;

            moddedTypes[moddedIndex++] = modelType;
            if (!instancesByType.TryGetValue(modelType, out Queue<TModel>? queue))
            {
                queue = new Queue<TModel>(1);
                instancesByType[modelType] = queue;
            }
            queue.Enqueue(model);
        }

        var sortedTypes = ContentSorter<ModelId>.Sort(moddedTypes, ModelDb.GetId);

        var output = new TModel[totalCount];
        int writeIndex = 0;
        for (int i = 0; i < totalCount; i++)
        {
            TModel model = models[i];
            if (model.GetType().Assembly == baseGame)
                output[writeIndex++] = model;
        }

        foreach (var item in sortedTypes)
            output[writeIndex++] = instancesByType[item.type].Dequeue();

        return output;
    }

    public static IEnumerable<TModel> SortNativePool<TModel>(
        AbstractModel pool,
        IEnumerable<TModel> source)
        where TModel : AbstractModel
    {
        if (AssemblyInfo.BaseGame == null || AssemblyInfo.ModMap == null ||
            pool.GetType().Assembly != AssemblyInfo.BaseGame)
        {
            return source;
        }

        int sourceCount = GetFastCount(source);
        ConditionalWeakTable<AbstractModel, PoolOrderCache<TModel>.CachedOrder> table = PoolOrderCache<TModel>.Table;
        if (table.TryGetValue(pool, out PoolOrderCache<TModel>.CachedOrder? cached) &&
            ReferenceEquals(cached.SourceRef, source) &&
            cached.SourceCount == sourceCount)
        {
            return cached.Sorted;
        }

        IEnumerable<TModel> sorted = SortBaseThenMods(source);
        table.AddOrUpdate(pool, new PoolOrderCache<TModel>.CachedOrder
        {
            SourceRef = source,
            SourceCount = sourceCount,
            Sorted = sorted,
        });
        return sorted;
    }
}

[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.AllCards), MethodType.Getter)]
internal static class DeterministicCardPoolOrderPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        __result = DeterministicContentOrder.SortNativePool(__instance, __result);
    }
}

[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.AllRelics), MethodType.Getter)]
internal static class DeterministicRelicPoolOrderPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(RelicPoolModel __instance, ref IEnumerable<RelicModel> __result)
    {
        __result = DeterministicContentOrder.SortNativePool(__instance, __result);
    }
}

[HarmonyPatch(typeof(PotionPoolModel), nameof(PotionPoolModel.AllPotions), MethodType.Getter)]
internal static class DeterministicPotionPoolOrderPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(PotionPoolModel __instance, ref IEnumerable<PotionModel> __result)
    {
        __result = DeterministicContentOrder.SortNativePool(__instance, __result);
    }
}
