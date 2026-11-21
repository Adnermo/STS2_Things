using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
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

    private static readonly MethodInfo RollWithoutChangingMethod =
        typeof(CardRarityOdds).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == nameof(CardRarityOdds.RollWithoutChangingFutureOdds)
                && m.GetParameters() is var p
                && p.Length >= 2
                && p[0].ParameterType == typeof(CardRarityOddsType)
                && p[1].ParameterType == typeof(float))
        ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
            nameof(CardRarityOdds.RollWithoutChangingFutureOdds));

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
            object?[] invokeArgs = RollWithoutChangingMethod.GetParameters().Length switch
            {
                2 => [type, doubledOffset],
                3 => [type, doubledOffset, rngOverride],
                _ => throw new InvalidOperationException(
                    $"Unexpected parameter count for RollWithoutChangingFutureOdds: {RollWithoutChangingMethod.GetParameters().Length}")
            };
            return (CardRarity)(RollWithoutChangingMethod.Invoke(instance, invokeArgs)
                ?? throw new InvalidOperationException("RollWithoutChangingFutureOdds returned null."));
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
        if (!player.Relics.OfType<ThingsNarrowViewfinder>().Any())
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
    private static MethodBase TargetMethod()
        => typeof(CardRarityOdds).GetMethods(BindingFlags.Public | BindingFlags.Instance)
               .FirstOrDefault(m => m.Name == nameof(CardRarityOdds.RollWithoutChangingFutureOdds)
                   && m.GetParameters() is var p
                   && p.Length >= 2
                   && p[0].ParameterType == typeof(CardRarityOddsType)
                   && p[1].ParameterType == typeof(float))
           ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
               nameof(CardRarityOdds.RollWithoutChangingFutureOdds));

    private static void Prefix([HarmonyArgument(0)] CardRarityOddsType type, [HarmonyArgument(1)] ref float offset)
    {
        if (!NarrowViewfinderRareOddsContext.IsDoublingActive)
            return;
        offset = NarrowViewfinderRareOddsContext.ComputeDoubledOffset(type, offset);
    }
}

[HarmonyPatch]
internal static class NarrowViewfinderRollWithBaseOddsPatch
{
    private static MethodBase TargetMethod()
        => typeof(CardRarityOdds).GetMethods(BindingFlags.Public | BindingFlags.Instance)
               .FirstOrDefault(m => m.Name == nameof(CardRarityOdds.RollWithBaseOdds)
                   && m.GetParameters() is var p
                   && p.Length >= 1
                   && p[0].ParameterType == typeof(CardRarityOddsType))
           ?? throw new MissingMethodException(typeof(CardRarityOdds).FullName,
               nameof(CardRarityOdds.RollWithBaseOdds));

    private static bool Prefix(
        CardRarityOdds __instance,
        [HarmonyArgument(0)] CardRarityOddsType type,
        object[] __args,
        ref CardRarity __result)
    {
        if (!NarrowViewfinderRareOddsContext.IsDoublingActive)
            return true;

        Rng? rngOverride = __args.Length >= 2 ? __args[1] as Rng : null;
        __result = NarrowViewfinderRareOddsContext.RollDoubledWithBaseOdds(__instance, type, rngOverride);
        return false;
    }
}
