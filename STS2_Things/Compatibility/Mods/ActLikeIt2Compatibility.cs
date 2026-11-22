using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Config;
using STS2_Things.Hooks;
using STS2_Things.Platform;

namespace STS2_Things.Compatibility.Mods;

/// <summary>
/// Optional interoperability with ActLikeIt2 (verified against v0.2.6 for STS2 v0.111.x and v0.113.x).
/// Registers <see cref="ThingsDepths"/> into ActLikeIt2's Act 2 slot, preserves explicit
/// <see cref="ThingsModConfig.FeatureGuaranteedDepthsEnabled"/> opt-in behavior, and restores
/// Blitz room generation when ActLikeIt2 swaps in <see cref="ThingsDepths"/>.
/// </summary>
internal static class ActLikeIt2Compatibility
{
    private const int DepthsActNumber = 2;
    private const int DepthsActIndex = 1;
    private const string ActLikeIt2HarmonyId = "ActLikeIt2";

    internal static void Install(Assembly assembly, Harmony harmony)
    {
        Type? registryType = assembly.GetType("ActLikeIt2.ActRegistry", false);
        Type? optionsType = assembly.GetType("ActLikeIt2.ActRegistrationOptions", false);
        if (registryType is null && assembly.GetType("ActLikeIt2.Loader.LoaderBootstrap", false) is not null)
        {
            // Variant loader stub; the real ActLikeIt2 variant assembly loads separately.
            return;
        }

        if (registryType is null || optionsType is null)
        {
            throw new NotSupportedException("ActLikeIt2 ActRegistry/ActRegistrationOptions types were not found.");
        }

        MethodInfo? registerGeneric = registryType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "Register" &&
                m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 &&
                m.ReturnType == typeof(void) &&
                m.GetParameters() is [{ ParameterType: var paramType }] &&
                paramType == optionsType);

        PropertyInfo? actNumberProperty = optionsType.GetProperty("ActNumber", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? optionDescriptionProperty = optionsType.GetProperty("OptionDescription", BindingFlags.Public | BindingFlags.Instance);

        if (registerGeneric is null ||
            actNumberProperty is null || actNumberProperty.PropertyType != typeof(int) || !actNumberProperty.CanWrite ||
            optionDescriptionProperty is null || optionDescriptionProperty.PropertyType != typeof(LocString) || !optionDescriptionProperty.CanWrite)
        {
            throw new NotSupportedException("ActLikeIt2 ActRegistry registration API differs from the verified contract.");
        }

        object options = Activator.CreateInstance(optionsType)
            ?? throw new NotSupportedException("Failed to construct ActLikeIt2.ActRegistrationOptions.");
        actNumberProperty.SetValue(options, DepthsActNumber);
        optionDescriptionProperty.SetValue(options, new LocString("acts", "THINGS_DEPTHS.fork_description"));

        registerGeneric.MakeGenericMethod(typeof(ThingsDepths)).Invoke(null, [options]);

        // If ModelDb.InitIds() has already completed (for example in late-load harnesses),
        // force ActRegistry to resolve pending type registrations immediately.
        if (ModelDb.GetByIdOrNull<ActModel>(ModelDb.GetId<ThingsDepths>()) is { Id.Entry.Length: > 0 })
        {
            MethodInfo? getForSlot = registryType.GetMethod(
                "GetRegistrationsForSlot",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(int)]);
            getForSlot?.Invoke(null, [DepthsActNumber]);
        }

        InstallGuaranteedDepthsInterop(assembly, harmony);
        InstallBlitzGenerationGuard(harmony);
    }

    private static void InstallGuaranteedDepthsInterop(Assembly assembly, Harmony harmony)
    {
        MethodInfo? getRandomList = AccessTools.Method(typeof(ActModel), nameof(ActModel.GetRandomList));
        MethodInfo? forkPrefix = assembly.GetType("ActLikeIt2.Patches.EnterActForkPatch", false)
            ?.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);

        ParameterInfo[]? parameters = forkPrefix?.GetParameters();
        bool validForkPrefix = forkPrefix?.ReturnType == typeof(bool) &&
            parameters is { Length: 3 } &&
            parameters[0].ParameterType == typeof(int) &&
            parameters[1].ParameterType == typeof(bool) &&
            parameters[2].ParameterType == typeof(Task).MakeByRefType();

        if (getRandomList is null || !validForkPrefix)
        {
            Log.Warn("STS2_Things: ActLikeIt2 EnterActForkPatch signature differs from verified contract; ThingsDepths is registered for Fork selection, but Guaranteed Depths fork-skip was skipped.");
            return;
        }

        var finalizer = new HarmonyMethod(typeof(ActLikeIt2Compatibility), nameof(AfterActRandomListFinalizer))
        {
            after = [ActLikeIt2HarmonyId],
            priority = Priority.Last,
        };
        harmony.Patch(getRandomList, finalizer: finalizer);
        harmony.Patch(
            forkPrefix!,
            prefix: new HarmonyMethod(typeof(ActLikeIt2Compatibility), nameof(BeforeEnterActForkPrefix)));
    }

    private static void InstallBlitzGenerationGuard(Harmony harmony)
    {
        // On STS2 v0.113.1+, ActLikeIt2 v0.2.6's RunStateActUtil.RegenerateActRooms passes
        // isBlitz: false when regenerating a swapped act. Preserve true Blitz generation for ThingsDepths.
        MethodInfo? generateRooms4Arg = AccessTools.Method(
            typeof(ActModel),
            nameof(ActModel.GenerateRooms),
            [typeof(Rng), typeof(UnlockState), typeof(bool), typeof(bool)]);
        if (generateRooms4Arg is null)
            return;

        harmony.Patch(
            generateRooms4Arg,
            prefix: new HarmonyMethod(typeof(ActLikeIt2Compatibility), nameof(PreserveBlitzDepthsGeneration)));
    }

    private static Exception? AfterActRandomListFinalizer(Exception? __exception, ref IEnumerable<ActModel> __result)
    {
        if (__exception is not null)
            return __exception;

        DepthsGuaranteedActPatch.ApplyGuaranteedDepths(ref __result);
        return null;
    }

    private static bool BeforeEnterActForkPrefix(int __0, ref bool __result)
    {
        if (__0 != DepthsActIndex || !ThingsModConfig.IsEnabled(ThingsModConfig.FeatureGuaranteedDepthsEnabled))
            return true;

        RunState? state = RunManager.Instance?.DebugOnlyGetState();
        if (state is null || state.Acts.Count <= DepthsActIndex || state.Acts[DepthsActIndex] is not ThingsDepths)
            return true;

        // Honor explicit "Guaranteed Depths" opt-in by bypassing only the Act 2 Fork screen
        // when this run already rolled ThingsDepths into Act 2 at run creation.
        __result = true;
        return false;
    }

    private static void PreserveBlitzDepthsGeneration(ActModel __instance, ref bool isBlitz)
    {
        if (!isBlitz &&
            __instance is ThingsDepths &&
            Sts2VersionSeam.IsBlitz(RunManager.Instance?.DebugOnlyGetState()))
        {
            isBlitz = true;
        }
    }
}
