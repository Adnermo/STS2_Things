using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Encounters;
using STS2_Things.Platform;

namespace STS2_Things.Hooks;

/// <summary>
/// Replaces vanilla's asymmetric, two-row <c>FullyCenterPlayers</c> party grid for our two
/// monumental centered-stage Act 2 Depths bosses — <see cref="ThingsAbyssalInkmawBossEncounter"/>
/// (straight ancient stone bridge between left/right tentacles) and
/// <see cref="ThingsHumanFaceTowerBossEncounter"/> (wide circular concave stone chamber floor) —
/// and extends >4-player (5–32+) parties on those bosses plus
/// <see cref="ThingsCaveGodBossEncounter"/> into grounded, symmetrically centered staggered rows.
///
/// For 1–4 players:
/// - Inkmaw &amp; Tower arrange all 1–4 party members symmetrically in a single grounded front row
///   with full brightness, clean HP-bar clearance, and dedicated spacing for Necrobinder's Osty.
/// - Cave God is left 100% untouched (vanilla placement).
///
/// For 5–32+ players:
/// - Inkmaw, Tower, and Cave God arrange the party into 2–5 staggered grounded rows inside each
///   stage's safe floor bounds, keeping the local player front-and-center at full brightness with
///   subtle rear-row depth tinting and back-to-front child ordering.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets))]
public static class MonumentalBossPartyLayoutPatch
{
    public const float BaseFloorLocalY = 200f;

    private const float InkmawSpacing2P = 260f;
    private const float InkmawSpacing3P = 250f;
    private const float InkmawSpacing4P = 228f;
    private const float InkmawMaxSpanLocal = 700f;
    private const float InkmawMaxSpanWithOstyLocal = 740f;

    private const float TowerSpacing2P = 280f;
    private const float TowerSpacing3P = 265f;
    private const float TowerSpacing4P = 250f;
    private const float TowerMaxSpanLocal = 820f;
    private const float TowerMaxSpanWithOstyLocal = 860f;

    private const float InkmawExtendedMaxSpanLocal = 840f;
    private const float InkmawExtendedMaxSpanWithOstyLocal = 860f;
    private const float InkmawExtendedPreferredGap = 210f;
    private const float InkmawExtendedMaxRearDepth = 52f;

    private const float TowerExtendedMaxSpanLocal = 940f;
    private const float TowerExtendedMaxSpanWithOstyLocal = 960f;
    private const float TowerExtendedPreferredGap = 225f;
    private const float TowerExtendedMaxRearDepth = 68f;
    private const float TowerExtendedFrontArcMaxDrop = 8f;

    private const float CaveGodExtendedMaxSpanLocal = 860f;
    private const float CaveGodExtendedMaxSpanWithOstyLocal = 880f;
    private const float CaveGodExtendedPreferredGap = 215f;
    private const float CaveGodExtendedMaxRearDepth = 72f;

    private const float LocalNecrobinderSoloOffsetX = -75f;
    private const float LocalNecrobinderRightGapBonus = 115f;

    private enum BossStageKind
    {
        Inkmaw,
        Tower,
        CaveGod
    }

    private sealed class PartySlot
    {
        public required NCreature Player { get; init; }
        public required List<NCreature> Pets { get; init; }
        public bool IsLocal { get; init; }
        public bool IsLocalNecrobinder { get; init; }
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(List<NCreature> creatureNodes)
    {
        if (creatureNodes == null || creatureNodes.Count == 0)
            return;

        EncounterModel? encounter = creatureNodes.FirstOrDefault()?.Entity?.CombatState?.Encounter;
        bool isInkmaw = encounter is ThingsAbyssalInkmawBossEncounter;
        bool isTower = encounter is ThingsHumanFaceTowerBossEncounter;
        bool isCaveGod = encounter is ThingsCaveGodBossEncounter;
        if (!isInkmaw && !isTower && !isCaveGod)
            return;

        if (isCaveGod)
        {
            int playerCount = 0;
            foreach (NCreature node in creatureNodes)
            {
                if (node?.Entity is { IsPlayer: true })
                    playerCount++;
            }

            if (!MultiplayerCompat.IsExtendedParty(playerCount))
                return;

            ApplyCaveGodExtendedPartyFormation(creatureNodes);
            return;
        }

        ApplyMonumentalPartyFormation(creatureNodes, isTower);
    }

    public static void ApplyMonumentalPartyFormation(List<NCreature> creatureNodes, bool isTower)
    {
        if (!CollectPartySlots(creatureNodes, out PartySlot? localSlot, out List<PartySlot> rawPlayers))
            return;

        int totalCount = (localSlot != null ? 1 : 0) + rawPlayers.Count;
        if (MultiplayerCompat.IsExtendedParty(totalCount))
        {
            ApplyExtendedFormationSlots(localSlot, rawPlayers, isTower ? BossStageKind.Tower : BossStageKind.Inkmaw);
            return;
        }

        // 1–4 players: preserve the exact validated single-row grounded formation.
        // Order left-to-right so the local player stands prominently near center:
        //   1p: [Local]
        //   2p: [Local, Mate0]
        //   3p: [Mate0, Local, Mate1]
        //   4p: [Mate0, Local, Mate1, Mate2]
        List<PartySlot> ordered = BuildDisplayOrder(localSlot, rawPlayers);
        int count = ordered.Count;

        float[] slotXs = ComputeHorizontalSlots(ordered, isTower);
        float[] slotYs = ComputeVerticalSlots(count, isTower);

        for (int i = 0; i < count; i++)
        {
            PartySlot slot = ordered[i];
            NCreature player = slot.Player;

            player.Position = new Vector2(slotXs[i], slotYs[i]);
            if (player.Visuals != null)
                player.Visuals.Modulate = Colors.White;

            PositionSlotPets(slot, Colors.White, 0.67f);
        }

        SortFormationChildren(ordered);
    }

    public static void ApplyCaveGodExtendedPartyFormation(List<NCreature> creatureNodes)
    {
        if (!CollectPartySlots(creatureNodes, out PartySlot? localSlot, out List<PartySlot> rawPlayers))
            return;

        int totalCount = (localSlot != null ? 1 : 0) + rawPlayers.Count;
        if (!MultiplayerCompat.IsExtendedParty(totalCount))
            return;

        ApplyExtendedFormationSlots(localSlot, rawPlayers, BossStageKind.CaveGod);
    }

    private static bool CollectPartySlots(
        List<NCreature> creatureNodes,
        out PartySlot? localSlot,
        out List<PartySlot> rawPlayers)
    {
        rawPlayers = [];
        localSlot = null;

        foreach (NCreature node in creatureNodes)
        {
            if (node?.Entity == null || !node.Entity.IsPlayer)
                continue;

            bool isLocal = LocalContext.IsMe(node.Entity);
            var slot = new PartySlot
            {
                Player = node,
                Pets = [],
                IsLocal = isLocal,
                IsLocalNecrobinder = isLocal && node.Entity.Player?.Character is Necrobinder
            };

            if (isLocal && localSlot == null)
                localSlot = slot;
            else
                rawPlayers.Add(slot);
        }

        if (localSlot == null && rawPlayers.Count == 0)
            return false;

        // Attach pets to their owning player slot.
        List<PartySlot> allSlots = localSlot != null ? [localSlot, .. rawPlayers] : [.. rawPlayers];
        foreach (NCreature node in creatureNodes)
        {
            if (node?.Entity == null || node.Entity.IsPlayer)
                continue;

            PartySlot? ownerSlot = allSlots.FirstOrDefault(s => s.Player.Entity.Player == node.Entity.PetOwner);
            ownerSlot?.Pets.Add(node);
        }

        return true;
    }

    private static void ApplyExtendedFormationSlots(
        PartySlot? localSlot,
        List<PartySlot> teammates,
        BossStageKind stage)
    {
        int totalCount = (localSlot != null ? 1 : 0) + teammates.Count;
        int rowCount = ComputeExtendedRowCount(totalCount);

        int basePerRow = totalCount / rowCount;
        int extraFrontRows = totalCount % rowCount;
        int[] rowSizes = new int[rowCount];
        for (int r = 0; r < rowCount; r++)
            rowSizes[r] = basePerRow + (r < extraFrontRows ? 1 : 0);

        // Build row assignments with the local player front-and-center in row 0.
        var rows = new List<PartySlot>[rowCount];
        int mateCursor = 0;
        for (int r = 0; r < rowCount; r++)
        {
            int rowLen = rowSizes[r];
            var row = new List<PartySlot>(rowLen);
            int localCol = r == 0 && localSlot != null ? (rowLen - 1) / 2 : -1;
            for (int c = 0; c < rowLen; c++)
            {
                if (c == localCol && localSlot != null)
                    row.Add(localSlot);
                else if (mateCursor < teammates.Count)
                    row.Add(teammates[mateCursor++]);
            }
            rows[r] = row;
        }

        float maxSpan = stage switch
        {
            BossStageKind.Tower => TowerExtendedMaxSpanLocal,
            BossStageKind.CaveGod => CaveGodExtendedMaxSpanLocal,
            _ => InkmawExtendedMaxSpanLocal
        };
        float maxSpanWithOsty = stage switch
        {
            BossStageKind.Tower => TowerExtendedMaxSpanWithOstyLocal,
            BossStageKind.CaveGod => CaveGodExtendedMaxSpanWithOstyLocal,
            _ => InkmawExtendedMaxSpanWithOstyLocal
        };
        float preferredGap = stage switch
        {
            BossStageKind.Tower => TowerExtendedPreferredGap,
            BossStageKind.CaveGod => CaveGodExtendedPreferredGap,
            _ => InkmawExtendedPreferredGap
        };
        float maxRearDepth = stage switch
        {
            BossStageKind.Tower => TowerExtendedMaxRearDepth,
            BossStageKind.CaveGod => CaveGodExtendedMaxRearDepth,
            _ => InkmawExtendedMaxRearDepth
        };
        float frontArcMaxDrop = stage == BossStageKind.Tower ? TowerExtendedFrontArcMaxDrop : 0f;
        float petVisualScale = stage == BossStageKind.CaveGod ? 1.0f : 0.67f;

        var placed = new List<PartySlot>(totalCount);
        int prevRowLen = 0;

        for (int r = 0; r < rowCount; r++)
        {
            List<PartySlot> row = rows[r];
            int rowLen = row.Count;
            if (rowLen == 0)
                continue;

            float rowT = rowCount > 1 ? r / (float)(rowCount - 1) : 0f;
            float rowBaseY = BaseFloorLocalY - maxRearDepth * rowT;
            Color rowModulate = GetExtendedRowModulate(r);

            int necroGapIndex = -1;
            for (int c = 0; c < rowLen - 1; c++)
            {
                if (row[c].IsLocalNecrobinder)
                {
                    necroGapIndex = c;
                    break;
                }
            }

            float rowMaxSpan = necroGapIndex >= 0
                ? maxSpanWithOsty
                : maxSpan * (1f - 0.05f * rowT);

            float[] xs = ComputeExtendedRowHorizontalSlots(
                row,
                r,
                prevRowLen,
                preferredGap,
                rowMaxSpan,
                necroGapIndex);

            float rowArc = frontArcMaxDrop * (1f - 0.5f * rowT);
            float centerCol = (rowLen - 1) * 0.5f;

            for (int c = 0; c < rowLen; c++)
            {
                PartySlot slot = row[c];
                NCreature player = slot.Player;

                float y = rowBaseY;
                if (rowArc > 0.001f && centerCol > 0.001f)
                {
                    float normalized = MathF.Abs(c - centerCol) / centerCol;
                    y += rowArc * (normalized * normalized);
                }

                player.Position = new Vector2(xs[c], y);
                if (player.Visuals != null)
                    player.Visuals.Modulate = rowModulate;

                PositionSlotPets(slot, rowModulate, petVisualScale);
                placed.Add(slot);
            }

            prevRowLen = rowLen;
        }

        SortFormationChildren(placed);
    }

    private static int ComputeExtendedRowCount(int count) => count switch
    {
        <= 8 => 2,
        <= 15 => 3,
        <= 24 => 4,
        _ => 5
    };

    private static Color GetExtendedRowModulate(int rowIndex)
    {
        float shade = rowIndex switch
        {
            <= 0 => 1.00f,
            1 => 0.88f,
            2 => 0.78f,
            3 => 0.70f,
            _ => 0.64f
        };
        return new Color(shade, shade, shade, 1f);
    }

    private static float[] ComputeExtendedRowHorizontalSlots(
        List<PartySlot> row,
        int rowIndex,
        int prevRowLen,
        float preferredGap,
        float rowMaxSpan,
        int necroGapIndex)
    {
        int rowLen = row.Count;
        float[] xs = new float[rowLen];
        if (rowLen == 1)
        {
            xs[0] = row[0].IsLocalNecrobinder ? LocalNecrobinderSoloOffsetX : 0f;
            return xs;
        }

        // Odd rows that share parity with the row directly in front get a bounded half-column
        // stagger so rear teammates peek cleanly between foreground teammates while staying
        // strictly inside [-rowMaxSpan * 0.5f, +rowMaxSpan * 0.5f].
        bool needsStagger = rowIndex > 0 && (rowIndex % 2 == 1) && (rowLen % 2 == prevRowLen % 2);
        float prelimStep = MathF.Min(preferredGap, rowMaxSpan / (rowLen - 1));
        float staggerMag = needsStagger ? MathF.Min(prelimStep * 0.44f, 68f) : 0f;
        float staggerSign = ((rowIndex / 2) % 2 == 0) ? 1f : -1f;
        float staggerOffset = staggerMag * staggerSign;
        float spanCeiling = MathF.Max(120f, rowMaxSpan - 2f * staggerMag);

        float[] gaps = new float[rowLen - 1];
        float totalSpan = 0f;
        for (int i = 0; i < gaps.Length; i++)
        {
            gaps[i] = preferredGap + (i == necroGapIndex ? LocalNecrobinderRightGapBonus : 0f);
            totalSpan += gaps[i];
        }

        if (totalSpan > spanCeiling && totalSpan > 0.001f)
        {
            float shrink = spanCeiling / totalSpan;
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] *= shrink;
            totalSpan = spanCeiling;
        }

        float cursor = -totalSpan * 0.5f + staggerOffset;
        xs[0] = cursor;
        for (int i = 0; i < gaps.Length; i++)
        {
            cursor += gaps[i];
            xs[i + 1] = cursor;
        }

        return xs;
    }

    private static void SortFormationChildren(List<PartySlot> ordered)
    {
        if (ordered.Count == 0)
            return;

        // Sort children back-to-front by Y so front rows and outer-arc heroes render cleanly in
        // front of rear rows, and pets render right beside their owning hero.
        Node? parent = ordered[0].Player.GetParent();
        if (parent == null)
            return;

        int childCursor = 0;
        foreach (PartySlot slot in ordered.OrderBy(s => s.Player.Position.Y).ThenBy(s => s.Player.Position.X))
        {
            if (slot.Player.GetParent() == parent)
                parent.MoveChild(slot.Player, childCursor++);

            foreach (NCreature pet in slot.Pets)
            {
                if (pet.GetParent() == parent)
                    parent.MoveChild(pet, childCursor++);
                if (!slot.IsLocal && pet.Visuals?.Bounds != null)
                    pet.Visuals.Bounds.Visible = false;
            }
        }
    }

    private static List<PartySlot> BuildDisplayOrder(PartySlot? localSlot, List<PartySlot> teammates)
    {
        if (localSlot == null)
            return [.. teammates];

        int total = teammates.Count + 1;
        if (total == 1)
            return [localSlot];

        int localIndex = total switch
        {
            2 => 0,
            3 => 1,
            4 => 1,
            _ => (total - 1) / 2
        };

        var ordered = new List<PartySlot>(total);
        int mateCursor = 0;
        for (int i = 0; i < total; i++)
        {
            if (i == localIndex)
                ordered.Add(localSlot);
            else
                ordered.Add(teammates[mateCursor++]);
        }
        return ordered;
    }

    private static float[] ComputeHorizontalSlots(List<PartySlot> ordered, bool isTower)
    {
        int count = ordered.Count;
        float[] xs = new float[count];
        if (count == 1)
        {
            xs[0] = ordered[0].IsLocalNecrobinder ? LocalNecrobinderSoloOffsetX : 0f;
            return xs;
        }

        float baseGap = isTower
            ? count switch
            {
                2 => TowerSpacing2P,
                3 => TowerSpacing3P,
                4 => TowerSpacing4P,
                _ => TowerMaxSpanLocal / Math.Max(1, count - 1)
            }
            : count switch
            {
                2 => InkmawSpacing2P,
                3 => InkmawSpacing3P,
                4 => InkmawSpacing4P,
                _ => InkmawMaxSpanLocal / Math.Max(1, count - 1)
            };

        bool hasInnerLocalNecro = false;
        int necroGapIndex = -1;
        for (int i = 0; i < count - 1; i++)
        {
            if (ordered[i].IsLocalNecrobinder)
            {
                hasInnerLocalNecro = true;
                necroGapIndex = i;
                break;
            }
        }

        float maxSpan = isTower
            ? (hasInnerLocalNecro ? TowerMaxSpanWithOstyLocal : TowerMaxSpanLocal)
            : (hasInnerLocalNecro ? InkmawMaxSpanWithOstyLocal : InkmawMaxSpanLocal);

        float[] gaps = new float[count - 1];
        float totalSpan = 0f;
        for (int i = 0; i < gaps.Length; i++)
        {
            gaps[i] = baseGap + (i == necroGapIndex ? LocalNecrobinderRightGapBonus : 0f);
            totalSpan += gaps[i];
        }

        if (totalSpan > maxSpan && totalSpan > 0.001f)
        {
            float shrink = maxSpan / totalSpan;
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] *= shrink;
            totalSpan = maxSpan;
        }

        float cursor = -totalSpan * 0.5f;
        xs[0] = cursor;
        for (int i = 0; i < gaps.Length; i++)
        {
            cursor += gaps[i];
            xs[i + 1] = cursor;
        }

        return xs;
    }

    private static float[] ComputeVerticalSlots(int count, bool isTower)
    {
        float[] ys = new float[count];
        if (count <= 1 || !isTower)
        {
            // Straight ancient stone bridge on Abyssal Inkmaw keeps every teammate strictly flat
            // across the horizontal bridge slab with maximum clearance above the resting hand fan.
            for (int i = 0; i < count; i++)
                ys[i] = BaseFloorLocalY;
            return ys;
        }

        if (count == 2)
        {
            ys[0] = BaseFloorLocalY + 2f;
            ys[1] = BaseFloorLocalY + 2f;
            return ys;
        }

        // On Human-Faced Tower's circular concave chamber floor, outer teammates curve gently
        // forward (+Y, NEVER upward into the rear stairs/wall) to echo the circular stone inlay ring
        // while keeping every hero's Power row comfortably above the resting 5-card hand fan.
        float maxEdgeDrop = count == 3 ? 6f : 10f;

        float center = (count - 1) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            float normalized = MathF.Abs(i - center) / MathF.Max(1f, center);
            float curve = count == 4 && normalized < 0.5f
                ? 0f
                : normalized * normalized;
            ys[i] = BaseFloorLocalY + maxEdgeDrop * curve;
        }

        return ys;
    }

    private static void PositionSlotPets(PartySlot slot, Color modulate, float visualScaleFactor)
    {
        if (slot.Pets.Count == 0)
            return;

        NCreature player = slot.Player;
        List<NCreature> groundPets = [];

        foreach (NCreature pet in slot.Pets)
        {
            if (pet.Visuals != null)
                pet.Visuals.Modulate = modulate;

            if (slot.IsLocal && pet.Entity.Monster is Osty)
            {
                Vector2 ostyOffset = NCombatRoom.Instance != null
                    ? NCreature.GetOstyOffsetFromPlayer(pet.Entity)
                    : Vector2.Right * player.Hitbox.Size.X * 0.5f
                      + Osty.MinOffset.Lerp(Osty.MaxOffset, Mathf.Clamp((float)pet.Entity.MaxHp / 150f, 0f, 1f));
                pet.Position = player.Position + ostyOffset;
                continue;
            }

            if (slot.IsLocal && string.Equals(pet.Entity.Monster?.Id.Entry, "WUPPY", StringComparison.OrdinalIgnoreCase))
                continue;

            groundPets.Add(pet);
        }

        if (groundPets.Count == 0)
            return;

        float playerWidth = player.Visuals != null ? player.Visuals.Bounds.Size.X * visualScaleFactor : 110f;
        float petHalfScale = 0.5f * visualScaleFactor;
        float step = groundPets.Count > 1 ? playerWidth / (groundPets.Count - 1) : 0f;
        for (int i = 0; i < groundPets.Count; i++)
        {
            NCreature pet = groundPets[i];
            float petHalfW = pet.Visuals != null ? pet.Visuals.Bounds.Size.X * petHalfScale : 36f;
            pet.Position = new Vector2(
                player.Position.X + 20f - i * step - petHalfW,
                player.Position.Y + 10f);
        }
    }
}
