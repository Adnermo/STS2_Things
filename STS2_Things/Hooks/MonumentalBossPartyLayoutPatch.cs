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

namespace STS2_Things.Hooks;

/// <summary>
/// Replaces vanilla's asymmetric, two-row <c>FullyCenterPlayers</c> party grid for our two
/// monumental centered-stage Act 2 Depths bosses — <see cref="ThingsAbyssalInkmawBossEncounter"/>
/// (straight ancient stone bridge between left/right tentacles) and
/// <see cref="ThingsHumanFaceTowerBossEncounter"/> (wide circular concave stone chamber floor).
///
/// Vanilla <see cref="NCombatRoom.PositionPlayersAndPets"/> centers only Player 0, pushes Player 1
/// far left, and pushes Players 2–3 up by -120 local units (-86px) while dimming them to 50% grey,
/// which puts multiplayer teammates directly on top of the subterranean lake, Boss HP bar, or rear
/// stone tower wall. This patch arranges all 1–4+ party members symmetrically in a single grounded
/// front row (flat across the stone bridge, or gently curved forward along the circular tower floor
/// ring) with full brightness, clean HP-bar clearance, and dedicated spacing for Necrobinder's Osty.
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

    private const float LocalNecrobinderSoloOffsetX = -75f;
    private const float LocalNecrobinderRightGapBonus = 115f;

    private sealed class PartySlot
    {
        public required NCreature Player { get; init; }
        public required List<NCreature> Pets { get; init; }
        public bool IsLocal { get; init; }
        public bool IsLocalNecrobinder { get; init; }
    }

    [HarmonyPostfix]
    private static void Postfix(List<NCreature> creatureNodes)
    {
        if (creatureNodes == null || creatureNodes.Count == 0)
            return;

        EncounterModel? encounter = creatureNodes.FirstOrDefault()?.Entity?.CombatState?.Encounter;
        bool isInkmaw = encounter is ThingsAbyssalInkmawBossEncounter;
        bool isTower = encounter is ThingsHumanFaceTowerBossEncounter;
        if (!isInkmaw && !isTower)
            return;

        ApplyMonumentalPartyFormation(creatureNodes, isTower);
    }

    public static void ApplyMonumentalPartyFormation(List<NCreature> creatureNodes, bool isTower)
    {
        List<PartySlot> rawPlayers = [];
        PartySlot? localSlot = null;

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
            return;

        // Attach pets to their owning player slot.
        List<PartySlot> allSlots = localSlot != null ? [localSlot, .. rawPlayers] : [.. rawPlayers];
        foreach (NCreature node in creatureNodes)
        {
            if (node?.Entity == null || node.Entity.IsPlayer)
                continue;

            PartySlot? ownerSlot = allSlots.FirstOrDefault(s => s.Player.Entity.Player == node.Entity.PetOwner);
            ownerSlot?.Pets.Add(node);
        }

        // Order left-to-right so the local player stands prominently near center:
        //   1p: [Local]
        //   2p: [Local, Mate0]
        //   3p: [Mate0, Local, Mate1]
        //   4p: [Mate0, Local, Mate1, Mate2]
        //   Np: Local at index (N - 1) / 2, teammates filling remaining slots symmetrically.
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

            PositionSlotPets(slot);
        }

        // Sort children back-to-front by Y so slightly forward outer-arc heroes render cleanly in front
        // of inner heroes, and pets render right beside their owning hero.
        Node? parent = ordered[0].Player.GetParent();
        if (parent != null)
        {
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

    private static void PositionSlotPets(PartySlot slot)
    {
        if (slot.Pets.Count == 0)
            return;

        NCreature player = slot.Player;
        List<NCreature> groundPets = [];

        foreach (NCreature pet in slot.Pets)
        {
            if (pet.Visuals != null)
                pet.Visuals.Modulate = Colors.White;

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

        float playerWidth = player.Visuals != null ? player.Visuals.Bounds.Size.X * 0.67f : 110f;
        float step = groundPets.Count > 1 ? playerWidth / (groundPets.Count - 1) : 0f;
        for (int i = 0; i < groundPets.Count; i++)
        {
            NCreature pet = groundPets[i];
            float petHalfW = pet.Visuals != null ? pet.Visuals.Bounds.Size.X * 0.335f : 36f;
            pet.Position = new Vector2(
                player.Position.X + 20f - i * step - petHalfW,
                player.Position.Y + 10f);
        }
    }
}
