using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Compatibility;
using STS2_Things.Monsters;
using STS2_Things.Powers;

namespace STS2_Things.Hooks;

/// <summary>
/// Captures the exact damage negated by upper/middle <see cref="ThingsHumanFaceColumn"/> layers
/// during live <see cref="CreatureCmd.Damage"/> executions of <see cref="Hook.ModifyDamage"/>
/// so <see cref="ThingsHumanFaceColumnPower"/> can redirect 100% of that prevented damage onto
/// the bottom-most living column layer without being poisoned by external preview/hint queries.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class HumanFaceColumnDamageRedirectPatch
{
    private static readonly AsyncLocal<int> ActiveDamageCommandDepth = new();
    private static readonly AsyncLocal<int> SuppressStagingDepth = new();

    internal static int EnterLiveDamageCommand()
    {
        int previous = ActiveDamageCommandDepth.Value;
        ActiveDamageCommandDepth.Value = previous + 1;
        return previous;
    }

    internal static void ExitLiveDamageCommand(int previousDepth)
    {
        ActiveDamageCommandDepth.Value = previousDepth;
    }

    internal static int EnterSuppressedPreview()
    {
        int previous = SuppressStagingDepth.Value;
        SuppressStagingDepth.Value = previous + 1;
        return previous;
    }

    internal static void ExitSuppressedPreview(int previousDepth)
    {
        SuppressStagingDepth.Value = previousDepth;
    }

    private static void Postfix(
        IRunState runState,
        ICombatState? combatState,
        Creature? target,
        Creature? dealer,
        decimal damage,
        ValueProp props,
        CardModel? cardSource,
        CardPlay? cardPlay,
        ModifyDamageHookType modifyDamageHookType,
        CardPreviewMode previewMode,
        decimal __result)
    {
        if (previewMode != CardPreviewMode.None ||
            ActiveDamageCommandDepth.Value <= 0 ||
            SuppressStagingDepth.Value > 0 ||
            target?.Monster is not ThingsHumanFaceColumn column ||
            column.Level < 1 ||
            ExternalCombatSimulationGuard.IsActive(target))
        {
            return;
        }

        ThingsHumanFaceColumnPower? power = target.GetPower<ThingsHumanFaceColumnPower>();
        if (power == null || power.IsBypassingReduction)
            return;

        if (!modifyDamageHookType.HasFlag(ModifyDamageHookType.Multiplicative))
        {
            power.StageModifyNegatedDamage(0m, props, reductionSkipped: true);
            return;
        }

        power.IsBypassingReduction = true;
        decimal unreduced;
        try
        {
            unreduced = Hook.ModifyDamage(
                runState,
                combatState,
                target,
                dealer,
                damage,
                props,
                cardSource,
                cardPlay,
                modifyDamageHookType,
                CardPreviewMode.None,
                out _);
        }
        finally
        {
            power.IsBypassingReduction = false;
        }

        decimal negated = Math.Max(0m, decimal.Truncate(unreduced) - decimal.Truncate(__result));
        power.StageModifyNegatedDamage(negated, props, reductionSkipped: false);
    }
}

/// <summary>
/// Scopes <see cref="HumanFaceColumnDamageRedirectPatch"/> staging strictly to active
/// <see cref="CreatureCmd.Damage"/> executions. Because <see cref="AsyncLocal{T}"/> flows into
/// the async state machine's captured <see cref="ExecutionContext"/> across <c>await</c>
/// boundaries while the caller's execution context is restored immediately in <c>Finalizer</c>,
/// multi-target AoE iterations keep staging active even after yielding awaits while outside UI
/// or preview queries always observe depth 0.
/// </summary>
[HarmonyPatch]
internal static class CreatureCmdDamageScopePatch
{
    private static MethodBase TargetMethod()
    {
        MethodInfo? v113Core = typeof(CreatureCmd)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != nameof(CreatureCmd.Damage))
                    return false;
                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 8 &&
                    parameters[0].ParameterType == typeof(PlayerChoiceContext) &&
                    parameters[1].ParameterType.IsGenericType &&
                    parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                    parameters[1].ParameterType.GetGenericArguments()[0].Name == "DamageTargetData";
            });
        if (v113Core != null)
            return v113Core;

        MethodInfo? v111Core = AccessTools.Method(
            typeof(CreatureCmd),
            nameof(CreatureCmd.Damage),
            [
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
                typeof(CardPlay),
            ]);
        return v111Core ?? throw new MissingMethodException(typeof(CreatureCmd).FullName, nameof(CreatureCmd.Damage));
    }

    [HarmonyPrefix]
    private static void Prefix(out int? __state)
    {
        __state = HumanFaceColumnDamageRedirectPatch.EnterLiveDamageCommand();
    }

    [HarmonyFinalizer]
    private static void Finalizer(int? __state)
    {
        if (__state.HasValue)
            HumanFaceColumnDamageRedirectPatch.ExitLiveDamageCommand(__state.Value);
    }
}

/// <summary>
/// Prevents <see cref="PoisonPower.CalculateTotalDamageNextTurn"/> (invoked with
/// <see cref="CardPreviewMode.None"/> during health bar refreshes, including synchronously
/// inside <c>Creature.LoseHpInternal</c>) from staging or clobbering column redirect state.
/// </summary>
[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.CalculateTotalDamageNextTurn))]
internal static class PoisonPowerPreviewScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(out int? __state)
    {
        __state = HumanFaceColumnDamageRedirectPatch.EnterSuppressedPreview();
    }

    [HarmonyFinalizer]
    private static void Finalizer(int? __state)
    {
        if (__state.HasValue)
            HumanFaceColumnDamageRedirectPatch.ExitSuppressedPreview(__state.Value);
    }
}

/// <summary>
/// Guards <see cref="Hook.AfterBlockBroken"/> against a null <see cref="ICombatState"/> when an earlier
/// target in an AoE <c>CreatureCmd.Damage</c> pass (such as a <see cref="ThingsHumanFaceColumn"/> damage
/// redirect) kills and removes a subsequent target before the outer loop reaches its <c>WasBlockBroken</c> check.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockBroken))]
internal static class AfterBlockBrokenNullCombatStateGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref ICombatState? combatState, ref Task __result)
    {
        if (combatState != null)
            return true;

        combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState != null)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
