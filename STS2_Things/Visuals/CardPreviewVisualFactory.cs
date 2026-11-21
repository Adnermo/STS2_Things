using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace STS2_Things.Visuals;

/// <summary>
/// Creates combat-only card preview visuals without mutating global <see cref="TestMode.IsOn"/>.
/// Returns <c>null</c> cleanly in headless, non-interactive, or test mode.
/// </summary>
internal static class CardPreviewVisualFactory
{
    public static bool CanCreateCardPreviewVisuals =>
        NCombatRoom.Instance != null
        && GodotObject.IsInstanceValid(NCombatRoom.Instance)
        && !NonInteractiveMode.IsActive
        && !TestMode.IsOn
        && !string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase);

    public static NCard? TryCreate(CardModel card)
    {
        if (!CanCreateCardPreviewVisuals)
            return null;

        try
        {
            return NCard.Create(card);
        }
        catch (Exception ex)
        {
            Log.Debug($"[CardPreviewVisualFactory] NCard.Create failed for '{card.Id}': {ex.Message}");
            return null;
        }
    }

    public static NCard? TryAttachPreview(
        Node parent,
        CardModel card,
        PileType pileType = PileType.Deck,
        CardPreviewMode previewMode = CardPreviewMode.Normal)
    {
        if (!GodotObject.IsInstanceValid(parent) || parent.IsQueuedForDeletion())
            return null;

        NCard? nCard = TryCreate(card);
        if (nCard == null)
            return null;

        parent.AddChildSafely(nCard);
        try
        {
            if (nCard.Model != card)
                nCard.Model = card;
        }
        catch (Exception ex)
        {
            Log.Debug($"[CardPreviewVisualFactory] Assigning NCard.Model failed for '{card.Id}': {ex.Message}");
        }

        try
        {
            nCard.UpdateVisuals(pileType, previewMode);
        }
        catch (Exception ex)
        {
            Log.Debug($"[CardPreviewVisualFactory] NCard.UpdateVisuals failed for '{card.Id}': {ex.Message}");
        }

        nCard.MouseFilter = Control.MouseFilterEnum.Ignore;
        return nCard;
    }

    /// <summary>
    /// Safely detaches and returns a pooled <see cref="NCard"/> without calling synchronous
    /// <c>RemoveChild</c> while an ancestor is blocked inside <c>_ExitTree</c> propagation.
    /// </summary>
    public static void ReturnPreviewSafely(Node? node, bool parentExitingTree = false)
    {
        if (node == null || !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion())
            return;

        if (node is not NCard card)
        {
            node.QueueFreeSafely();
            return;
        }

        if (!parentExitingTree)
        {
            Node? parent = card.GetParent();
            if (parent != null && GodotObject.IsInstanceValid(parent) && NGame.IsMainThread() &&
                (parent.IsNodeReady() || !parent.IsInsideTree()))
            {
                parent.RemoveChild(card);
            }

            if (card.GetParent() == null)
            {
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(card) && !card.IsQueuedForDeletion() && card.GetParent() == null)
                        NodePool.Free(card);
                }).CallDeferred();
                return;
            }
        }

        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(card) || card.IsQueuedForDeletion())
                return;
            Node? currentParent = card.GetParent();
            if (currentParent != null && GodotObject.IsInstanceValid(currentParent))
                currentParent.RemoveChild(card);
            if (card.GetParent() == null)
                NodePool.Free(card);
        }).CallDeferred();
    }
}
