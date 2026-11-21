using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Enchantments;

namespace STS2_Things.Hooks;

/// <summary>
/// Harmony lifecycle hooks for <see cref="ThingsSplit"/> with fast O(1) short-circuiting
/// when a card does not carry <see cref="ThingsSplit"/>.
/// </summary>
internal static class ThingsSplitPatches
{
    [HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.BaseValue), MethodType.Setter)]
    private static class DynamicVarBaseValuePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            DynamicVar __instance,
            AbstractModel? ____owner,
            [HarmonyArgument(0)] decimal value)
        {
            if (____owner is CardModel { Enchantment: ThingsSplit split })
                split.BeforeDynamicBaseValueWrite(__instance, value);
        }
    }

    [HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.UpgradeValueBy))]
    private static class DynamicVarUpgradePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            DynamicVar __instance,
            AbstractModel? ____owner,
            [HarmonyArgument(0)] ref decimal addend,
            out ThingsSplit.DynamicUpgradeMutation? __state)
        {
            __state = ____owner is CardModel { Enchantment: ThingsSplit split }
                ? split.PrepareDynamicUpgrade(__instance, ref addend)
                : null;
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(
            ThingsSplit.DynamicUpgradeMutation? __state,
            Exception? __exception)
        {
            if (__state != null)
                ThingsSplit.CompleteDynamicUpgrade(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TheScythe), "CurrentDamage", MethodType.Setter)]
    private static class TheScytheCurrentDamagePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            TheScythe __instance,
            [HarmonyArgument(0)] int value,
            out ThingsSplit.FullValueSetterMutation? __state)
        {
            __state = __instance.Enchantment is ThingsSplit split
                ? split.PrepareFullValueSetter("Damage", value)
                : null;
        }

        [HarmonyPostfix]
        private static void Postfix(ThingsSplit.FullValueSetterMutation? __state)
        {
            if (__state != null)
                ThingsSplit.CompleteFullValueSetter(__state);
        }
    }

    [HarmonyPatch(typeof(GeneticAlgorithm), "CurrentBlock", MethodType.Setter)]
    private static class GeneticAlgorithmCurrentBlockPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            GeneticAlgorithm __instance,
            [HarmonyArgument(0)] int value,
            out ThingsSplit.FullValueSetterMutation? __state)
        {
            __state = __instance.Enchantment is ThingsSplit split
                ? split.PrepareFullValueSetter("Block", value)
                : null;
        }

        [HarmonyPostfix]
        private static void Postfix(ThingsSplit.FullValueSetterMutation? __state)
        {
            if (__state != null)
                ThingsSplit.CompleteFullValueSetter(__state);
        }
    }

    [HarmonyPatch(typeof(SovereignBlade), "CurrentDamage", MethodType.Setter)]
    private static class SovereignBladeCurrentDamagePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            SovereignBlade __instance,
            [HarmonyArgument(0)] ref decimal value)
        {
            if (__instance.Enchantment is ThingsSplit split)
                value = split.NormalizeStoredFullValue("Damage", value);
        }
    }

    [HarmonyPatch(typeof(SovereignBlade), "CurrentRepeats", MethodType.Setter)]
    private static class SovereignBladeCurrentRepeatsPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            SovereignBlade __instance,
            [HarmonyArgument(0)] ref decimal value)
        {
            if (__instance.Enchantment is ThingsSplit split)
            {
                string varName = __instance.DynamicVars.ContainsKey("HitCount") ? "HitCount" : "Repeat";
                value = split.NormalizeStoredFullValue(varName, value);
            }
        }
    }

    [HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.UpgradeBy))]
    private static class EnergyCostUpgradePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            CardEnergyCost __instance,
            CardModel ____card,
            [HarmonyArgument(0)] ref int addend,
            out ThingsSplit.EnergyUpgradeMutation? __state)
        {
            __state = ____card.Enchantment is ThingsSplit split
                ? split.PrepareEnergyUpgrade(__instance, ref addend)
                : null;
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(
            ThingsSplit.EnergyUpgradeMutation? __state,
            Exception? __exception)
        {
            if (__state != null)
                ThingsSplit.CompleteEnergyUpgrade(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.SetCustomBaseCost))]
    private static class EnergyCostCustomBasePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            CardEnergyCost __instance,
            CardModel ____card,
            [HarmonyArgument(0)] int newBaseCost)
        {
            if (____card.Enchantment is ThingsSplit split)
                split.BeforeCustomEnergyCostWrite(__instance, newBaseCost);
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.BaseStarCost), MethodType.Setter)]
    private static class BaseStarCostPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            CardModel __instance,
            [HarmonyArgument(0)] int value)
        {
            if (__instance.Enchantment is ThingsSplit split)
                split.BeforeBaseStarCostWrite(value);
        }
    }

    [HarmonyPatch(typeof(CardModel), "UpgradeStarCostBy")]
    private static class StarCostUpgradePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            CardModel __instance,
            [HarmonyArgument(0)] ref int addend,
            out ThingsSplit.StarUpgradeMutation? __state)
        {
            __state = __instance.Enchantment is ThingsSplit split
                ? split.PrepareStarUpgrade(ref addend)
                : null;
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(
            ThingsSplit.StarUpgradeMutation? __state,
            Exception? __exception)
        {
            if (__state != null)
                ThingsSplit.CompleteStarUpgrade(__state, __exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.ClearEnchantmentInternal))]
    private static class ClearEnchantmentPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CardModel __instance)
        {
            if (__instance.Enchantment is ThingsSplit split)
                split.RestoreUnsplitValues();
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.DowngradeInternal))]
    private static class CardDowngradePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CardModel __instance)
        {
            if (__instance.Enchantment is ThingsSplit split)
                split.BeginDowngrade();
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(CardModel __instance, Exception? __exception)
        {
            if (__instance.Enchantment is ThingsSplit split)
                split.CompleteDowngrade(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
    private static class CardDeserializationPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(CardModel __result)
        {
            if (__result.Enchantment is ThingsSplit split)
                split.ApplyPendingSerializedState();
        }
    }
}
