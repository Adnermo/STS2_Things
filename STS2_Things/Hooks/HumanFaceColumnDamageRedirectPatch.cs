using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;
using STS2_Things.Powers;

namespace STS2_Things.Hooks;

/// <summary>
/// Captures the exact damage negated by upper/middle <see cref="ThingsHumanFaceColumn"/> layers
/// during <see cref="Hook.ModifyDamage"/> (non-preview executions only) so <see cref="ThingsHumanFaceColumnPower"/>
/// can redirect 100% of that prevented damage onto the bottom-most living column layer.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class HumanFaceColumnDamageRedirectPatch
{
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
        if (previewMode != CardPreviewMode.None || target?.Monster is not ThingsHumanFaceColumn column || column.Level < 1)
            return;

        ThingsHumanFaceColumnPower? power = target.GetPower<ThingsHumanFaceColumnPower>();
        if (power == null || power.IsBypassingReduction)
            return;

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
        power.StageModifyNegatedDamage(negated, props);
    }
}
