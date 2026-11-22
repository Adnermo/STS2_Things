using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Platform;

namespace STS2_Things.Hooks;

/// <summary>
/// Extends the Depths campfire scene (<c>things_depths_rest_site.tscn</c>) cleanly for &gt;4-player
/// (5–32+) parties without altering 1–4 player layouts or non-Depths rest sites:
/// - Case A (external mods like MultiplayerLimitBreak / RemoveMultiplayerPlayerLimit that create
///   distinct containers for players 5..N): clones matching left/right stone stools and shadows
///   underneath every extra player in depth order before <c>RestSiteLighting</c>.
/// - Case B (external mods like Sts2Unlimited that stack players 5..N inside <c>Character_4</c>):
///   spreads stacked teammates onto alternating left/right campfire ring positions, flips facing
///   toward the hearth, and clones matching stone stools and shadows beneath them.
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
public static class DepthsRestSiteExtendedPartyPatch
{
    private const string MarkerNodeName = "ExtendedDepthsSeatsApplied";
    private const string BackSeatsContainerName = "ExtendedDepthsSeatsBack";
    private const string FrontSeatsContainerName = "ExtendedDepthsSeatsFront";

    private const float CampfireCenterX = 912f;
    private const float FrontBackSplitY = 612f;
    private const float VanillaCharacterScale = 0.5f;
    private const float StackOverlapRadiusSq = 64f;

    private static readonly Vector2 VanillaChar1Pos = new(625f, 642f);
    private static readonly Vector2 VanillaChar2Pos = new(1239f, 649f);
    private static readonly Vector2 VanillaChar3Pos = new(750f, 585f);
    private static readonly Vector2 VanillaChar4Pos = new(1092f, 583f);

    private static readonly Vector2 DefaultFrontLeftSeatDelta = new(-19f, 12f);
    private static readonly Vector2 DefaultFrontRightSeatDelta = new(-3f, -7f);
    private static readonly Vector2 DefaultBackLeftSeatDelta = new(-2f, -9f);
    private static readonly Vector2 DefaultBackRightSeatDelta = new(-8f, -2f);

    private readonly record struct SeatTemplate(
        Sprite2D Seat,
        Sprite2D? Shadow,
        Vector2 SeatDelta,
        Vector2 ShadowDelta);

    private readonly record struct PlannedSeat(
        SeatTemplate Template,
        Vector2 SeatPos,
        Vector2 ShadowPos,
        float ScaleMul,
        int PlayerIndex);

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(NRestSiteRoom __instance)
    {
        ApplyExtendedDepthsSeating(__instance);
    }

    public static void ApplyExtendedDepthsSeating(NRestSiteRoom? room)
    {
        if (room == null || !GodotObject.IsInstanceValid(room))
            return;

        if (!MultiplayerCompat.IsExtendedParty(room.Characters.Count))
            return;

        Control? bgContainer = room.GetNodeOrNull<Control>("BgContainer");
        Control? depthsBg = bgContainer?.GetNodeOrNull<Control>("DepthsRestSite");
        if (bgContainer == null || depthsBg == null || !GodotObject.IsInstanceValid(depthsBg))
            return;

        if (depthsBg.GetNodeOrNull(MarkerNodeName) != null)
            return;

        depthsBg.AddChild(new Node { Name = MarkerNodeName });

        SpreadStackedCharactersIfNeeded(bgContainer, depthsBg, room.Characters);
        CloneStoneStoolsForExtendedParty(bgContainer, depthsBg, room.Characters);
    }

    private static void SpreadStackedCharactersIfNeeded(
        Control bgContainer,
        Control depthsBg,
        List<NRestSiteCharacter> characters)
    {
        var stacked = new List<(NRestSiteCharacter Character, int PlayerIndex)>();
        var affectedParents = new HashSet<Node>();

        for (int i = MultiplayerCompat.VanillaMaxPlayers; i < characters.Count; i++)
        {
            NRestSiteCharacter ch = characters[i];
            if (ch == null || !GodotObject.IsInstanceValid(ch))
                continue;

            Node? parent = ch.GetParent();
            if (parent == null)
                continue;

            bool isStacked = false;
            for (int j = 0; j < i; j++)
            {
                NRestSiteCharacter earlier = characters[j];
                if (earlier == null || !GodotObject.IsInstanceValid(earlier))
                    continue;
                if (earlier.GetParent() == parent &&
                    ch.Position.DistanceSquaredTo(earlier.Position) < StackOverlapRadiusSq)
                {
                    isStacked = true;
                    break;
                }
            }

            if (isStacked)
            {
                stacked.Add((ch, i));
                affectedParents.Add(parent);
            }
        }

        if (stacked.Count == 0)
            return;

        for (int rank = 0; rank < stacked.Count; rank++)
        {
            (NRestSiteCharacter ch, _) = stacked[rank];
            (Vector2 targetDepthsPos, float seatScale, bool isRight) = ComputeExtendedFallbackSeat(rank);

            ch.Position = ConvertDepthsPosToParentLocal(bgContainer, depthsBg, ch.GetParent(), targetDepthsPos);

            float signX = ch.Scale.X < 0f ? -1f : 1f;
            float signY = ch.Scale.Y < 0f ? -1f : 1f;
            float baseAbsX = MathF.Abs(ch.Scale.X) > 0.001f ? MathF.Abs(ch.Scale.X) : 1f;
            float baseAbsY = MathF.Abs(ch.Scale.Y) > 0.001f ? MathF.Abs(ch.Scale.Y) : 1f;
            ch.Scale = new Vector2(signX * baseAbsX * seatScale, signY * baseAbsY * seatScale);

            if (ch.IsNodeReady() && ch.GetChildCount() > 0 && IsCharacterFlipped(ch) != isRight)
                ch.FlipX();
        }

        // Keep stacked characters drawn back-to-front by vertical position inside their shared parent.
        foreach (Node parent in affectedParents)
        {
            if (!GodotObject.IsInstanceValid(parent))
                continue;

            List<NRestSiteCharacter> siblings = parent.GetChildren()
                .OfType<NRestSiteCharacter>()
                .Where(GodotObject.IsInstanceValid)
                .OrderBy(c => c.Position.Y)
                .ThenBy(c => c.Position.X)
                .ToList();

            for (int idx = 0; idx < siblings.Count; idx++)
                parent.MoveChild(siblings[idx], idx);
        }
    }

    private static readonly (float OffsetX, float Y, float Scale)[] ExtendedSideSeats =
    [
        (444f, 668f, 0.95f), // 5–6p
        (340f, 578f, 0.93f), // 7–8p
        (554f, 616f, 0.90f), // 9–10p
        (616f, 694f, 0.88f), // 11–12p
        (468f, 550f, 0.86f), // 13–14p
        (680f, 636f, 0.84f), // 15–16p
        (574f, 542f, 0.83f), // 17–18p
        (732f, 716f, 0.82f), // 19–20p
        (678f, 568f, 0.80f), // 21–22p
        (508f, 732f, 0.82f), // 23–24p
        (772f, 656f, 0.78f), // 25–26p
        (768f, 582f, 0.78f), // 27–28p
        (390f, 736f, 0.84f), // 29–30p
        (266f, 532f, 0.80f), // 31–32p
    ];

    private static (Vector2 DepthsPos, float Scale, bool IsRight) ComputeExtendedFallbackSeat(int extraRank)
    {
        bool isRight = (extraRank % 2) == 1;
        int sideRank = extraRank / 2;

        float offsetX;
        float y;
        float scale;

        if (sideRank < ExtendedSideSeats.Length)
        {
            (offsetX, y, scale) = ExtendedSideSeats[sideRank];
        }
        else
        {
            int overflow = sideRank - ExtendedSideSeats.Length;
            int row = overflow % 3;
            int col = overflow / 3;
            offsetX = Math.Clamp(690f + col * 42f + (row % 2) * 26f, 650f, 790f);
            y = 540f + row * 82f;
            scale = 0.74f;
        }

        float x = isRight
            ? CampfireCenterX + offsetX
            : CampfireCenterX - offsetX;
        return (new Vector2(x, y), scale, isRight);
    }

    private static void CloneStoneStoolsForExtendedParty(
        Control bgContainer,
        Control depthsBg,
        List<NRestSiteCharacter> characters)
    {
        SeatTemplate? frontLeft = BuildTemplate(bgContainer, depthsBg, "seat_front_left", "seat_front_left_shadow", "Character_1", VanillaChar1Pos, DefaultFrontLeftSeatDelta);
        SeatTemplate? frontRight = BuildTemplate(bgContainer, depthsBg, "seat_front_right", "seat_front_right_shadow", "Character_2", VanillaChar2Pos, DefaultFrontRightSeatDelta);
        SeatTemplate? backLeft = BuildTemplate(bgContainer, depthsBg, "seat_back_left", "seat_back_left_shadow", "Character_3", VanillaChar3Pos, DefaultBackLeftSeatDelta);
        SeatTemplate? backRight = BuildTemplate(bgContainer, depthsBg, "seat_back_right", "seat_back_right_shadow", "Character_4", VanillaChar4Pos, DefaultBackRightSeatDelta);

        if (frontLeft == null && frontRight == null && backLeft == null && backRight == null)
            return;

        var backPlanned = new List<PlannedSeat>();
        var frontPlanned = new List<PlannedSeat>();

        for (int i = MultiplayerCompat.VanillaMaxPlayers; i < characters.Count; i++)
        {
            NRestSiteCharacter ch = characters[i];
            if (ch == null || !GodotObject.IsInstanceValid(ch))
                continue;

            (Vector2 charDepthsPos, float effectiveScaleY) = GetCharacterTransformInDepthsSpace(bgContainer, depthsBg, ch);

            bool isRight = charDepthsPos.X >= CampfireCenterX;
            bool isBack = charDepthsPos.Y < FrontBackSplitY;

            SeatTemplate? chosen = isBack
                ? (isRight ? backRight ?? frontRight : backLeft ?? frontLeft)
                : (isRight ? frontRight ?? backRight : frontLeft ?? backLeft);
            chosen ??= frontLeft ?? frontRight ?? backLeft ?? backRight;
            if (chosen is not { } tpl)
                continue;

            float charScaleRatio = Math.Clamp(effectiveScaleY / VanillaCharacterScale, 0.65f, 1.15f);
            float outerDist = MathF.Max(0f, MathF.Abs(charDepthsPos.X - CampfireCenterX) - 340f);
            float distanceFactor = Math.Clamp(1f - outerDist / 1800f, 0.84f, 1f);
            float seatScaleMul = charScaleRatio < 0.99f ? charScaleRatio : distanceFactor;

            Vector2 seatPos = charDepthsPos + tpl.SeatDelta * seatScaleMul;
            Vector2 shadowPos = seatPos + tpl.ShadowDelta * seatScaleMul;

            var planned = new PlannedSeat(tpl, seatPos, shadowPos, seatScaleMul, i);
            if (seatPos.Y < FrontBackSplitY)
                backPlanned.Add(planned);
            else
                frontPlanned.Add(planned);
        }

        if (backPlanned.Count > 0)
        {
            backPlanned.Sort((a, b) => a.SeatPos.Y.CompareTo(b.SeatPos.Y));
            Node2D backGroup = BuildSeatGroup(BackSeatsContainerName, backPlanned);
            InsertSeatGroupBefore(depthsBg, backGroup, "seat_front_left_shadow", "hearth_shadow", "RestSiteLighting");
        }

        if (frontPlanned.Count > 0)
        {
            frontPlanned.Sort((a, b) => a.SeatPos.Y.CompareTo(b.SeatPos.Y));
            Node2D frontGroup = BuildSeatGroup(FrontSeatsContainerName, frontPlanned);
            InsertSeatGroupBefore(depthsBg, frontGroup, "hearth_shadow", "RestSiteLighting", null);
        }
    }

    private static SeatTemplate? BuildTemplate(
        Control bgContainer,
        Control depthsBg,
        string seatNodeName,
        string shadowNodeName,
        string vanillaCharContainerName,
        Vector2 vanillaExpectedPos,
        Vector2 fallbackDelta)
    {
        Sprite2D? seat = depthsBg.GetNodeOrNull<Sprite2D>(seatNodeName);
        if (seat == null || !GodotObject.IsInstanceValid(seat))
            return null;

        Sprite2D? shadow = depthsBg.GetNodeOrNull<Sprite2D>(shadowNodeName);
        Vector2 seatDelta = fallbackDelta;

        Control? charContainer = bgContainer.GetNodeOrNull<Control>(vanillaCharContainerName);
        if (charContainer != null && GodotObject.IsInstanceValid(charContainer))
        {
            Vector2 liveCharPos = ConvertBgPosToDepthsSpace(depthsBg, charContainer.Position);
            if (liveCharPos.DistanceSquaredTo(vanillaExpectedPos) <= 144f)
                seatDelta = seat.Position - liveCharPos;
        }

        Vector2 shadowDelta = shadow != null && GodotObject.IsInstanceValid(shadow)
            ? shadow.Position - seat.Position
            : new Vector2(0f, 44f);

        return new SeatTemplate(seat, shadow, seatDelta, shadowDelta);
    }

    private static Node2D BuildSeatGroup(string groupName, List<PlannedSeat> seats)
    {
        var group = new Node2D { Name = groupName };
        foreach (PlannedSeat planned in seats)
        {
            if (planned.Template.Shadow != null && GodotObject.IsInstanceValid(planned.Template.Shadow))
            {
                Sprite2D shadowClone = CloneSprite(
                    planned.Template.Shadow,
                    $"ext_seat_{planned.PlayerIndex}_shadow",
                    planned.ShadowPos,
                    planned.ScaleMul);
                group.AddChild(shadowClone);
            }

            Sprite2D seatClone = CloneSprite(
                planned.Template.Seat,
                $"ext_seat_{planned.PlayerIndex}",
                planned.SeatPos,
                planned.ScaleMul);
            group.AddChild(seatClone);
        }

        return group;
    }

    private static Sprite2D CloneSprite(Sprite2D template, string name, Vector2 position, float scaleMul)
    {
        return new Sprite2D
        {
            Name = name,
            Texture = template.Texture,
            Centered = template.Centered,
            Offset = template.Offset,
            FlipH = template.FlipH,
            FlipV = template.FlipV,
            Modulate = template.Modulate,
            SelfModulate = template.SelfModulate,
            Material = template.Material,
            TextureFilter = template.TextureFilter,
            Position = position,
            Scale = template.Scale * scaleMul
        };
    }

    private static void InsertSeatGroupBefore(
        Control depthsBg,
        Node2D group,
        string primaryAnchor,
        string? secondaryAnchor,
        string? tertiaryAnchor)
    {
        depthsBg.AddChild(group);

        Node? anchor = depthsBg.GetNodeOrNull(primaryAnchor)
            ?? (secondaryAnchor != null ? depthsBg.GetNodeOrNull(secondaryAnchor) : null)
            ?? (tertiaryAnchor != null ? depthsBg.GetNodeOrNull(tertiaryAnchor) : null)
            ?? depthsBg.GetNodeOrNull("%RestSiteLighting");

        if (anchor != null && anchor.GetParent() == depthsBg)
            depthsBg.MoveChild(group, anchor.GetIndex());
    }

    private static (Vector2 DepthsPos, float EffectiveScaleY) GetCharacterTransformInDepthsSpace(
        Control bgContainer,
        Control depthsBg,
        NRestSiteCharacter ch)
    {
        Vector2 pos = ch.Position;
        float scaleY = MathF.Abs(ch.Scale.Y);
        if (scaleY < 0.001f)
            scaleY = 1f;

        Node? cursor = ch.GetParent();
        while (cursor != null && cursor != bgContainer)
        {
            if (cursor is Control ctrl)
            {
                pos = ctrl.Position + pos * ctrl.Scale;
                scaleY *= MathF.Abs(ctrl.Scale.Y) > 0.001f ? MathF.Abs(ctrl.Scale.Y) : 1f;
            }
            else if (cursor is Node2D n2d)
            {
                pos = n2d.Position + pos * n2d.Scale;
                scaleY *= MathF.Abs(n2d.Scale.Y) > 0.001f ? MathF.Abs(n2d.Scale.Y) : 1f;
            }

            cursor = cursor.GetParent();
        }

        Vector2 depthsPos = ConvertBgPosToDepthsSpace(depthsBg, pos);
        float bgScaleY = MathF.Abs(depthsBg.Scale.Y) > 0.001f ? MathF.Abs(depthsBg.Scale.Y) : 1f;
        return (depthsPos, scaleY / bgScaleY);
    }

    private static Vector2 ConvertBgPosToDepthsSpace(Control depthsBg, Vector2 bgPos)
    {
        float sx = MathF.Abs(depthsBg.Scale.X) > 0.0001f ? depthsBg.Scale.X : 1f;
        float sy = MathF.Abs(depthsBg.Scale.Y) > 0.0001f ? depthsBg.Scale.Y : 1f;
        return new Vector2((bgPos.X - depthsBg.Position.X) / sx, (bgPos.Y - depthsBg.Position.Y) / sy);
    }

    private static Vector2 ConvertDepthsPosToParentLocal(
        Control bgContainer,
        Control depthsBg,
        Node? parent,
        Vector2 depthsPos)
    {
        Vector2 bgPos = depthsBg.Position + depthsPos * depthsBg.Scale;
        if (parent == null || parent == bgContainer)
            return bgPos;

        var chain = new List<Node>();
        Node? cursor = parent;
        while (cursor != null && cursor != bgContainer)
        {
            chain.Add(cursor);
            cursor = cursor.GetParent();
        }

        Vector2 localPos = bgPos;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i] is Control ctrl)
            {
                float sx = MathF.Abs(ctrl.Scale.X) > 0.0001f ? ctrl.Scale.X : 1f;
                float sy = MathF.Abs(ctrl.Scale.Y) > 0.0001f ? ctrl.Scale.Y : 1f;
                localPos = new Vector2((localPos.X - ctrl.Position.X) / sx, (localPos.Y - ctrl.Position.Y) / sy);
            }
            else if (chain[i] is Node2D n2d)
            {
                float sx = MathF.Abs(n2d.Scale.X) > 0.0001f ? n2d.Scale.X : 1f;
                float sy = MathF.Abs(n2d.Scale.Y) > 0.0001f ? n2d.Scale.Y : 1f;
                localPos = new Vector2((localPos.X - n2d.Position.X) / sx, (localPos.Y - n2d.Position.Y) / sy);
            }
        }

        return localPos;
    }

    private static bool IsCharacterFlipped(NRestSiteCharacter ch)
    {
        if (ch.GetNodeOrNull<Control>("ControlRoot") is { } controlRoot)
            return controlRoot.Scale.X < 0f;

        foreach (Node child in ch.GetChildren())
        {
            if (child is Node2D n2d && string.Equals(n2d.GetClass(), "SpineSprite", StringComparison.Ordinal))
                return n2d.Scale.X < 0f;
        }

        return false;
    }
}
