using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using STS2_Things.Monsters;

namespace STS2_Things.Hooks;

/// <summary>
/// Repairs three native NHoverTipSet layout edge cases for custom STS2_Things monsters
/// (such as ThingsCaveGodLeftHand under 0.75x camera scaling with multiple CardHoverTips):
/// 1. NCreature.Hitbox.Scale is local (1,1), ignoring camera/container scaling;
/// 2. Left-edge overflow in CorrectHorizontalOverflow can overlap cards onto text tips or snap Y to 0;
/// 3. Bone-tracked Cave God hands move continuously and need SetFollowOwner() plus third-party fault isolation.
/// </summary>
[HarmonyPatch(typeof(NCreature), nameof(NCreature.ShowHoverTips))]
internal static class MonsterHoverTipLayoutPatch
{
    private const float ViewportMargin = 12f;
    private const float ContainerGap = 10f;

    private static readonly FieldInfo? ActiveTipsField =
        AccessTools.Field(typeof(NHoverTipSet), "_activeHoverTips");
    private static readonly FieldInfo? TextContainerField =
        AccessTools.Field(typeof(NHoverTipSet), "_textHoverTipContainer");
    private static readonly FieldInfo? CardContainerField =
        AccessTools.Field(typeof(NHoverTipSet), "_cardHoverTipContainer");

    private static Exception? Finalizer(NCreature __instance, Exception? __exception)
    {
        var monster = __instance.Entity?.Monster;
        if (monster == null || monster.GetType().Assembly != typeof(STS2_ThingsInit).Assembly)
            return __exception;

        if (ActiveTipsField?.GetValue(null) is not Dictionary<Control, NHoverTipSet> activeTips ||
            !activeTips.TryGetValue(__instance.Hitbox, out NHoverTipSet? tipSet) ||
            tipSet == null || !GodotObject.IsInstanceValid(tipSet))
        {
            return __exception;
        }

        if (monster is ThingsCaveGodHand hand)
            hand.Body?.Background?.UpdateHandAnchors([__instance]);

        var textContainer = TextContainerField?.GetValue(tipSet) as Control;
        var cardContainer = CardContainerField?.GetValue(tipSet) as NHoverTipCardContainer;
        if (textContainer == null || cardContainer == null)
            return __exception;

        if (__exception != null)
        {
            // A third-party NCard.UpdateVisuals patch threw during NHoverTipSet.Init before SetAlignment ran,
            // which otherwise strands the half-built tip set at (0, 0) with card & text stacked together.
            foreach (Control child in cardContainer.GetChildren().OfType<Control>().ToList())
            {
                NCard? cardNode = child.GetNodeOrNull<NCard>("%Card");
                if (cardNode?.Model == null)
                    child.QueueFree();
            }
            Log.Warn($"STS2_Things: isolated third-party hover-tip preview fault on {monster.Id}: {__exception.Message}");
            __exception = null;
        }

        Rect2 hitboxRect = __instance.Hitbox.GetGlobalRect();
        Vector2 viewport = tipSet.GetViewportRect().Size;
        if (viewport.X <= 0f || viewport.Y <= 0f)
            viewport = new Vector2(1920f, 1080f);

        bool isLeftHalf = hitboxRect.GetCenter().X < viewport.X * 0.5f;
        if (cardContainer.GetChildCount() > 0 && cardContainer.Size == Vector2.Zero)
        {
            cardContainer.LayoutResizeAndReposition(
                isLeftHalf ? hitboxRect.Position : new Vector2(hitboxRect.End.X, hitboxRect.Position.Y),
                isLeftHalf ? HoverTipAlignment.Left : HoverTipAlignment.Right);
        }

        Vector2 textSize = textContainer.Size;
        Vector2 cardSize = MeasureCardContainer(cardContainer);
        bool hasCards = cardSize.X > 0.5f && cardSize.Y > 0.5f;

        bool outOfBounds = textContainer.GlobalPosition.X < ViewportMargin ||
                           textContainer.GlobalPosition.X + textSize.X > viewport.X - ViewportMargin ||
                           textContainer.GlobalPosition.Y < ViewportMargin ||
                           textContainer.GlobalPosition.Y + textSize.Y > viewport.Y - ViewportMargin ||
                           (hasCards && (cardContainer.GlobalPosition.X < ViewportMargin ||
                                         cardContainer.GlobalPosition.X + cardSize.X > viewport.X - ViewportMargin ||
                                         cardContainer.GlobalPosition.Y < ViewportMargin ||
                                         cardContainer.GlobalPosition.Y + cardSize.Y > viewport.Y - ViewportMargin));
        bool overlaps = hasCards &&
                        new Rect2(textContainer.GlobalPosition, textSize).Intersects(
                            new Rect2(cardContainer.GlobalPosition, cardSize));

        if (isLeftHalf || outOfBounds || overlaps)
        {
            LayoutNonOverlapping(hitboxRect, viewport, isLeftHalf, textContainer, textSize, cardContainer, cardSize, hasCards);
        }

        return __exception;
    }

    private static Vector2 MeasureCardContainer(NHoverTipCardContainer cardContainer)
    {
        if (cardContainer.GetChildCount() == 0)
            return Vector2.Zero;
        Vector2 size = cardContainer.Size;
        foreach (Control tip in cardContainer.GetChildren().OfType<Control>())
        {
            if (tip.IsQueuedForDeletion()) continue;
            Vector2 childSize = tip.Size;
            if (childSize == Vector2.Zero)
                childSize = tip.GetCombinedMinimumSize();
            if (childSize == Vector2.Zero)
                childSize = new Vector2(260f, 360f);
            size = new Vector2(
                Math.Max(size.X, tip.Position.X + childSize.X),
                Math.Max(size.Y, tip.Position.Y + childSize.Y));
        }
        return size;
    }

    private static void LayoutNonOverlapping(
        Rect2 hitboxRect,
        Vector2 viewport,
        bool isLeftHalf,
        Control textContainer,
        Vector2 textSize,
        NHoverTipCardContainer cardContainer,
        Vector2 cardSize,
        bool hasCards)
    {
        float textY = Math.Clamp(hitboxRect.Position.Y, ViewportMargin, Math.Max(ViewportMargin, viewport.Y - ViewportMargin - textSize.Y));
        float cardY = Math.Clamp(hitboxRect.Position.Y, ViewportMargin, Math.Max(ViewportMargin, viewport.Y - ViewportMargin - cardSize.Y));

        if (isLeftHalf)
        {
            float textX = Math.Max(ViewportMargin, hitboxRect.End.X + ContainerGap);
            if (!hasCards)
            {
                textX = Math.Clamp(textX, ViewportMargin, Math.Max(ViewportMargin, viewport.X - ViewportMargin - textSize.X));
                textContainer.GlobalPosition = new Vector2(textX, textY);
                return;
            }

            float leftCardX = hitboxRect.Position.X - ContainerGap - cardSize.X;
            if (leftCardX >= ViewportMargin && textX + textSize.X <= viewport.X - ViewportMargin)
            {
                textContainer.GlobalPosition = new Vector2(textX, textY);
                cardContainer.GlobalPosition = new Vector2(leftCardX, cardY);
                return;
            }

            float cardX = textX + textSize.X + ContainerGap;
            if (cardX + cardSize.X > viewport.X - ViewportMargin)
            {
                float overflow = (cardX + cardSize.X) - (viewport.X - ViewportMargin);
                textX = Math.Max(ViewportMargin, textX - overflow);
                cardX = Math.Min(viewport.X - ViewportMargin - cardSize.X, textX + textSize.X + ContainerGap);
            }
            textContainer.GlobalPosition = new Vector2(textX, textY);
            cardContainer.GlobalPosition = new Vector2(cardX, cardY);
        }
        else
        {
            float textX = hitboxRect.Position.X - ContainerGap - textSize.X;
            if (!hasCards)
            {
                textX = Math.Clamp(textX, ViewportMargin, Math.Max(ViewportMargin, viewport.X - ViewportMargin - textSize.X));
                textContainer.GlobalPosition = new Vector2(textX, textY);
                return;
            }

            float rightCardX = hitboxRect.End.X + ContainerGap;
            if (textX >= ViewportMargin && rightCardX + cardSize.X <= viewport.X - ViewportMargin)
            {
                textContainer.GlobalPosition = new Vector2(textX, textY);
                cardContainer.GlobalPosition = new Vector2(rightCardX, cardY);
                return;
            }

            float cardX = textX - ContainerGap - cardSize.X;
            if (cardX < ViewportMargin)
            {
                cardX = ViewportMargin;
                textX = Math.Min(viewport.X - ViewportMargin - textSize.X, cardX + cardSize.X + ContainerGap);
            }
            cardContainer.GlobalPosition = new Vector2(cardX, cardY);
            textContainer.GlobalPosition = new Vector2(textX, textY);
        }
    }
}
