using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using STS2_Things.Config;
using STS2_Things.Platform;

namespace STS2_Things.Hooks.DefensiveGuards;

/// <summary>
/// Defensive UI guard for <see cref="NHoverTipSet"/>:
/// 1. Cleans up half-registered hover-tip nodes if <c>Init()</c> throws before <c>ConnectTreeExiting(owner)</c>.
/// 2. Clamps negative top/left coordinates after <c>CorrectVerticalOverflow</c> / <c>CorrectHorizontalOverflow</c>
///    so tall or multi-tip stacks never clip above or to the left of the viewport.
/// </summary>
internal static class HoverTipViewportGuard
{
    private static bool IsActive => ThingsModConfig.IsEnabled(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled);

    [HarmonyPatch]
    private static class CreateHoverTipFinalizerPatch
    {
        private static MethodBase TargetMethod()
        {
#if STS2_V111_1
            return AccessTools.Method(typeof(NHoverTipSet), nameof(NHoverTipSet.CreateAndShow),
                [typeof(Control), typeof(IEnumerable<IHoverTip>), typeof(HoverTipAlignment)]);
#else
            return AccessTools.Method(typeof(NHoverTipSet), "CreateAndRegister",
                [typeof(Control), typeof(IEnumerable<IHoverTip>)]);
#endif
        }

        private static Exception? Finalizer(Exception? __exception, Control owner)
        {
            if (__exception != null && IsActive)
            {
                try
                {
                    NHoverTipSet.Remove(owner);
                }
                catch (Exception cleanupEx)
                {
                    Log.Debug($"[STS2_Things] HoverTip cleanup after init fault skipped: {cleanupEx.Message}");
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NHoverTipSet), "CorrectVerticalOverflow")]
    private static class ClampVerticalOverflowPatch
    {
        private static void Postfix(NHoverTipSet __instance)
        {
            if (!IsActive || !GodotObject.IsInstanceValid(__instance))
                return;

            var (textContainer, cardContainer) = EngineAccess.GetHoverTipContainers(__instance);
            if (textContainer != null && GodotObject.IsInstanceValid(textContainer) && textContainer.GlobalPosition.Y < 0f)
                textContainer.GlobalPosition = new Vector2(textContainer.GlobalPosition.X, 0f);
            if (cardContainer != null && GodotObject.IsInstanceValid(cardContainer) && cardContainer.GlobalPosition.Y < 0f)
                cardContainer.GlobalPosition = new Vector2(cardContainer.GlobalPosition.X, 0f);
        }
    }

    [HarmonyPatch(typeof(NHoverTipSet), "CorrectHorizontalOverflow")]
    private static class ClampHorizontalOverflowPatch
    {
        private static void Postfix(NHoverTipSet __instance)
        {
            if (!IsActive || !GodotObject.IsInstanceValid(__instance))
                return;

            var (textContainer, cardContainer) = EngineAccess.GetHoverTipContainers(__instance);
            if (textContainer != null && GodotObject.IsInstanceValid(textContainer) && textContainer.GlobalPosition.X < 0f)
                textContainer.GlobalPosition = new Vector2(0f, textContainer.GlobalPosition.Y);
            if (cardContainer != null && GodotObject.IsInstanceValid(cardContainer) && cardContainer.GlobalPosition.X < 0f)
                cardContainer.GlobalPosition = new Vector2(0f, cardContainer.GlobalPosition.Y);
        }
    }
}
