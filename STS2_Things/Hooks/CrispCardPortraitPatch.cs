using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using STS2_Things.Platform;

namespace STS2_Things.Hooks;

/// <summary>
/// Upgrades card portraits from the 250x190 BPTC atlas sprite to the native 1000x760 BigPortrait
/// in event previews, upgrade/enchant/transform comparisons, and event deck selection grids.
/// Only replaces the texture when the node is still displaying <see cref="CardModel.Portrait"/>,
/// preserving compatibility with third-party card-art replacement mods.
/// </summary>
internal static class CrispCardPortraitPatch
{
    private const int MaxGridInitialUpgradeCount = 12;
    private const int GridUpgradeBatchPerFrame = 4;

    internal static bool TryUpgradeCardNode(NCard? cardNode)
    {
        if (cardNode == null || !GodotObject.IsInstanceValid(cardNode))
            return false;
        if (cardNode.Visibility != ModelVisibility.Visible)
            return false;

        CardModel? model = cardNode.Model;
        if (model == null || !model.HasPortrait)
            return false;

        Texture2D? current = Sts2VersionSeam.GetCurrentCardPortraitTexture(cardNode);
        if (current == null || !GodotObject.IsInstanceValid(current))
            return false;

        // Respect custom card-art replacement mods (e.g. CardArtEditor): only upgrade vanilla atlas portraits.
        Texture2D defaultPortrait = model.Portrait;
        if (!ReferenceEquals(current, defaultPortrait))
            return false;

        Texture2D? bigPortrait = Sts2VersionSeam.TryGetBigCardPortrait(model);
        if (bigPortrait == null || ReferenceEquals(bigPortrait, current) || !GodotObject.IsInstanceValid(bigPortrait))
            return false;

        Sts2VersionSeam.ApplyCardPortraitTexture(cardNode, bigPortrait);
        return true;
    }

    internal static void UpgradeCardsInSubtree(Node? root, ref int remaining)
    {
        if (root == null || !GodotObject.IsInstanceValid(root) || remaining <= 0)
            return;

        if (root is NCard card)
        {
            TryUpgradeCardNode(card);
            remaining--;
            return;
        }

        if (root is NCardHolder holder && holder.CardNode != null)
        {
            TryUpgradeCardNode(holder.CardNode);
            remaining--;
            return;
        }

        int childCount = root.GetChildCount();
        for (int i = 0; i < childCount && remaining > 0; i++)
            UpgradeCardsInSubtree(root.GetChild(i), ref remaining);
    }

    private static void CollectVisibleGridCards(
        Node? root,
        Rect2 viewportRect,
        bool hasViewportRect,
        List<NCard> results,
        int limit)
    {
        if (root == null || results.Count >= limit || !GodotObject.IsInstanceValid(root))
            return;

        if (root is NCardHolder holder)
        {
            if (holder.CardNode is { } card && GodotObject.IsInstanceValid(card) &&
                (!hasViewportRect || IsHolderPotentiallyVisible(holder, viewportRect)))
            {
                results.Add(card);
            }
            return;
        }

        if (root is NCard standaloneCard)
        {
            results.Add(standaloneCard);
            return;
        }

        int childCount = root.GetChildCount();
        for (int i = 0; i < childCount && results.Count < limit; i++)
            CollectVisibleGridCards(root.GetChild(i), viewportRect, hasViewportRect, results, limit);
    }

    private static bool IsHolderPotentiallyVisible(Control holder, Rect2 viewportRect)
    {
        Rect2 rect = holder.GetGlobalRect();
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
            return true;
        return viewportRect.Grow(240f).Intersects(rect);
    }

    private static int UpgradeCardBatch(List<NCard> cards, int startIndex, int maxUpgrades)
    {
        int upgraded = 0;
        int index = startIndex;
        while (index < cards.Count && upgraded < maxUpgrades)
        {
            NCard card = cards[index++];
            if (GodotObject.IsInstanceValid(card) && card.IsInsideTree() && TryUpgradeCardNode(card))
                upgraded++;
        }
        return index;
    }

    private static async Task UpgradeRemainingGridCardsAsync(
        NCardGridSelectionScreen screen,
        List<NCard> cards,
        int startIndex)
    {
        int index = startIndex;
        while (index < cards.Count)
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
                return;

            SceneTree? tree = screen.GetTree();
            if (tree == null)
                return;

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
                return;

            index = UpgradeCardBatch(cards, index, GridUpgradeBatchPerFrame);
        }
    }

    internal static bool IsInsideGridSelectionScreen(Node? node)
    {
        Node? cursor = node?.GetParent();
        for (int depth = 0; depth < 10 && cursor != null && GodotObject.IsInstanceValid(cursor); depth++)
        {
            if (cursor is NCardGridSelectionScreen)
                return true;
            cursor = cursor.GetParent();
        }
        return false;
    }

    [HarmonyPatch(typeof(NPreviewCardHolder), "Initialize")]
    private static class PreviewCardHolderPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NPreviewCardHolder __instance)
        {
            if (__instance == null || !GodotObject.IsInstanceValid(__instance))
                return;

            if (!TryUpgradeCardNode(__instance.CardNode))
            {
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(__instance))
                        TryUpgradeCardNode(__instance.CardNode);
                }).CallDeferred();
            }
        }
    }

    [HarmonyPatch(typeof(NUpgradePreview), "Reload")]
    private static class UpgradePreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NUpgradePreview __instance)
        {
            int budget = 16;
            UpgradeCardsInSubtree(__instance, ref budget);
        }
    }

    [HarmonyPatch(typeof(NEnchantPreview), nameof(NEnchantPreview.Init))]
    private static class EnchantPreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NEnchantPreview __instance)
        {
            int budget = 16;
            UpgradeCardsInSubtree(__instance, ref budget);
        }
    }

    [HarmonyPatch(typeof(NTransformPreview), nameof(NTransformPreview.Initialize))]
    private static class TransformPreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NTransformPreview __instance)
        {
            int budget = 32;
            UpgradeCardsInSubtree(__instance, ref budget);
        }
    }

    [HarmonyPatch(typeof(CardCmd), "PreviewInternal")]
    private static class EventCardPreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CardPreviewStyle style)
        {
            if (style != CardPreviewStyle.EventLayout)
                return;

            Node? container = NRun.Instance?.GlobalUi?.EventCardPreviewContainer;
            if (container == null || !GodotObject.IsInstanceValid(container))
                return;

            int budget = 24;
            UpgradeCardsInSubtree(container, ref budget);
        }
    }

    [HarmonyPatch(typeof(NCardGridSelectionScreen), "ConnectSignalsAndInitGrid")]
    private static class GridSelectionInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NCardGridSelectionScreen __instance)
        {
            if (__instance == null || !GodotObject.IsInstanceValid(__instance))
                return;

            Rect2 viewportRect = __instance.GetViewportRect();
            bool hasViewportRect = viewportRect.Size.X > 0f && viewportRect.Size.Y > 0f;
            var candidates = new List<NCard>(MaxGridInitialUpgradeCount);
            CollectVisibleGridCards(__instance, viewportRect, hasViewportRect, candidates, MaxGridInitialUpgradeCount);
            if (candidates.Count == 0)
                return;

            int nextIndex = UpgradeCardBatch(candidates, 0, GridUpgradeBatchPerFrame);
            if (nextIndex < candidates.Count)
            {
                _ = TaskHelper.RunSafely(UpgradeRemainingGridCardsAsync(__instance, candidates, nextIndex));
            }
        }
    }

    [HarmonyPatch(typeof(NGridCardHolder), "OnFocus")]
    private static class GridCardFocusPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NGridCardHolder __instance)
        {
            if (IsInsideGridSelectionScreen(__instance))
                TryUpgradeCardNode(__instance.CardNode);
        }
    }

    [HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder.SetIsPreviewingUpgrade))]
    private static class GridCardUpgradePreviewPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NGridCardHolder __instance)
        {
            if (IsInsideGridSelectionScreen(__instance))
                TryUpgradeCardNode(__instance.CardNode);
        }
    }

    [HarmonyPatch(typeof(NCardHolder), nameof(NCardHolder.ReassignToCard))]
    private static class GridCardReassignPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NCardHolder __instance)
        {
            if (__instance is NGridCardHolder && IsInsideGridSelectionScreen(__instance))
                TryUpgradeCardNode(__instance.CardNode);
        }
    }
}
