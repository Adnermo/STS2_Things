using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Relics;

namespace STS2_Things.Hooks;

internal static class NarrowViewfinderRareOddsContext
{
    private static readonly AsyncLocal<int> ActiveDepth = new();
    private static readonly AsyncLocal<int> SuppressDoubleDepth = new();

    private static readonly Func<CardRarityOddsType, CardRarity, float> BaseOddsAccessor =
        (Func<CardRarityOddsType, CardRarity, float>)Delegate.CreateDelegate(
            typeof(Func<CardRarityOddsType, CardRarity, float>),
            AccessTools.DeclaredMethod(typeof(CardRarityOdds), "GetBaseOdds")
                ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName, "GetBaseOdds"));

#if STS2_V113_1
    private static readonly Func<CardRarityOdds, CardRarityOddsType, float, Rng?, CardRarity> RollWithoutChangingDelegate =
        (Func<CardRarityOdds, CardRarityOddsType, float, Rng?, CardRarity>)Delegate.CreateDelegate(
            typeof(Func<CardRarityOdds, CardRarityOddsType, float, Rng?, CardRarity>),
            AccessTools.Method(
                typeof(CardRarityOdds),
                nameof(CardRarityOdds.RollWithoutChangingFutureOdds),
                [typeof(CardRarityOddsType), typeof(float), typeof(Rng)])
            ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
                nameof(CardRarityOdds.RollWithoutChangingFutureOdds)));
#else
    private static readonly Func<CardRarityOdds, CardRarityOddsType, float, CardRarity> RollWithoutChangingDelegate =
        (Func<CardRarityOdds, CardRarityOddsType, float, CardRarity>)Delegate.CreateDelegate(
            typeof(Func<CardRarityOdds, CardRarityOddsType, float, CardRarity>),
            AccessTools.Method(
                typeof(CardRarityOdds),
                nameof(CardRarityOdds.RollWithoutChangingFutureOdds),
                [typeof(CardRarityOddsType), typeof(float)])
            ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
                nameof(CardRarityOdds.RollWithoutChangingFutureOdds)));
#endif

    public static bool IsDoublingActive => ActiveDepth.Value > 0 && SuppressDoubleDepth.Value == 0;

    public static void PushScope() => ActiveDepth.Value++;

    public static void PopScope() => ActiveDepth.Value = Math.Max(0, ActiveDepth.Value - 1);

    public static float ComputeDoubledOffset(CardRarityOddsType type, float originalOffset)
    {
        float baseRare = BaseOddsAccessor(type, CardRarity.Rare);
        float effectiveRare = Math.Max(0f, baseRare + originalOffset);
        float doubledRare = Math.Clamp(effectiveRare * 2f, 0f, 1f);
        return doubledRare - baseRare;
    }

    public static CardRarity RollDoubledWithBaseOdds(CardRarityOdds instance, CardRarityOddsType type, Rng? rngOverride)
    {
        float doubledOffset = ComputeDoubledOffset(type, 0f);
        SuppressDoubleDepth.Value++;
        try
        {
#if STS2_V113_1
            return RollWithoutChangingDelegate(instance, type, doubledOffset, rngOverride);
#else
            _ = rngOverride;
            return RollWithoutChangingDelegate(instance, type, doubledOffset);
#endif
        }
        finally
        {
            SuppressDoubleDepth.Value = Math.Max(0, SuppressDoubleDepth.Value - 1);
        }
    }
}

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    typeof(Player), typeof(int), typeof(CardCreationOptions))]
internal static class NarrowViewfinderCreateForRewardScopePatch
{
    private static void Prefix(Player? player, CardCreationOptions? options, out bool __state)
    {
        __state = false;
        if (player == null || options == null)
            return;
        if (!options.Flags.HasFlag(CardCreationFlags.IsCardReward)
            || options.Flags.HasFlag(CardCreationFlags.NoRarityModification)
            || options.RarityOdds is CardRarityOddsType.Uniform or CardRarityOddsType.BossEncounter)
        {
            return;
        }
        bool hasViewfinder = false;
        IReadOnlyList<RelicModel> relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i] is ThingsNarrowViewfinder)
            {
                hasViewfinder = true;
                break;
            }
        }
        if (!hasViewfinder)
            return;

        NarrowViewfinderRareOddsContext.PushScope();
        __state = true;
    }

    private static void Finalizer(bool __state)
    {
        if (__state)
            NarrowViewfinderRareOddsContext.PopScope();
    }
}

[HarmonyPatch]
internal static class NarrowViewfinderRollWithoutChangingPatch
{
    private static MethodBase TargetMethod() =>
#if STS2_V113_1
        AccessTools.Method(
            typeof(CardRarityOdds),
            nameof(CardRarityOdds.RollWithoutChangingFutureOdds),
            [typeof(CardRarityOddsType), typeof(float), typeof(Rng)])
#else
        AccessTools.Method(
            typeof(CardRarityOdds),
            nameof(CardRarityOdds.RollWithoutChangingFutureOdds),
            [typeof(CardRarityOddsType), typeof(float)])
#endif
        ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
            nameof(CardRarityOdds.RollWithoutChangingFutureOdds));

    private static void Prefix(CardRarityOddsType type, ref float offset)
    {
        if (!NarrowViewfinderRareOddsContext.IsDoublingActive)
            return;
        offset = NarrowViewfinderRareOddsContext.ComputeDoubledOffset(type, offset);
    }
}

[HarmonyPatch]
internal static class NarrowViewfinderRollWithBaseOddsPatch
{
    private static MethodBase TargetMethod() =>
#if STS2_V113_1
        AccessTools.Method(
            typeof(CardRarityOdds),
            nameof(CardRarityOdds.RollWithBaseOdds),
            [typeof(CardRarityOddsType), typeof(Rng)])
#else
        AccessTools.Method(
            typeof(CardRarityOdds),
            nameof(CardRarityOdds.RollWithBaseOdds),
            [typeof(CardRarityOddsType)])
#endif
        ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
            nameof(CardRarityOdds.RollWithBaseOdds));

#if STS2_V113_1
    private static bool Prefix(
        CardRarityOdds __instance,
        CardRarityOddsType type,
        Rng? rngOverride,
        ref CardRarity __result)
    {
        if (!NarrowViewfinderRareOddsContext.IsDoublingActive)
            return true;

        __result = NarrowViewfinderRareOddsContext.RollDoubledWithBaseOdds(__instance, type, rngOverride);
        return false;
    }
#else
    private static bool Prefix(
        CardRarityOdds __instance,
        CardRarityOddsType type,
        ref CardRarity __result)
    {
        if (!NarrowViewfinderRareOddsContext.IsDoublingActive)
            return true;

        __result = NarrowViewfinderRareOddsContext.RollDoubledWithBaseOdds(__instance, type, null);
        return false;
    }
#endif
}
