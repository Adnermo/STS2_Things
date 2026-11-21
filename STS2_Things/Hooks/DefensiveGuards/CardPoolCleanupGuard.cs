using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using STS2_Things.Config;
using STS2_Things.Platform;

namespace STS2_Things.Hooks.DefensiveGuards;

/// <summary>
/// Clears <c>_ancientPortrait</c> and <c>_enchantmentIcon</c> when an <see cref="NCard"/> returns to the node pool
/// so disposed external textures can never linger on pooled cards.
/// </summary>
internal static class CardPoolCleanupGuard
{
    [HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
    private static class ClearPooledCardExtraTexturesPatch
    {
        private static void Postfix(NCard __instance)
        {
            if (!ThingsModConfig.IsEnabled(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled))
                return;
            if (!GodotObject.IsInstanceValid(__instance))
                return;

            EngineAccess.ClearPooledCardExtraTextures(__instance);
        }
    }
}
