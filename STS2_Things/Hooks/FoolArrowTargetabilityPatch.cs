#if STS2_V113_1
using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Random;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Hooks;

/// <summary>
/// Fool arrow combat integration for <c>STS2_Things</c>:
/// 1. Returns stranded arrows cleanly when a target transitions into a non-interactable / unhittable
///    state during the shot itself (such as breaking <c>ThingsCaveGodHand</c> or rotating a tower face).
/// 2. Anchors <see cref="NImpaledArrowVfx"/> to animated Spine bones on custom <c>STS2_Things</c>
///    monsters and <see cref="NCaveGodBossBackground"/> when native <c>ARROW_HIT_*</c> slots are absent.
/// </summary>
[HarmonyPatch]
internal static class FoolArrowTargetabilityPatch
{
    private static readonly FieldInfo? VfxCreatureField =
        AccessTools.Field(typeof(NImpaledArrowVfx), "_creature");

    private static readonly float[] ArrowRotationOffsets = [0f, -20f, 20f];

    private static readonly Vector2[] ArrowPositionOffsets =
    [
        new(0f, 0f),
        new(15f, 5f),
        new(-15f, 5f),
    ];

    private static readonly string[] LeftArmBoneCandidates = ["arm1_3", "fist1"];
    private static readonly string[] RightArmBoneCandidates = ["arm2_3", "fist2"];
    private static readonly string[] CaveGodBodyBoneCandidates = ["head", "body"];

    private static readonly string[] CustomMonsterBoneCandidates =
    [
        "chest",
        "torso",
        "body",
        "spine",
        "body_main",
        "pelvis",
        "center",
        "head",
        "root",
    ];

    [HarmonyPatch(typeof(ImpaledPower), nameof(ImpaledPower.RemoveIfNoArrows))]
    [HarmonyPrefix]
    private static bool SkipRemoveIfUnattached(ImpaledPower __instance, ref Task __result)
    {
        if (!__instance.IsMutable || __instance.Owner == null)
        {
            __result = Task.CompletedTask;
            return false;
        }
        return true;
    }

    [HarmonyPatch(
        typeof(ArrowCmd),
        "ImpaleTarget",
        new[] { typeof(Creature), typeof(ArrowSlot), typeof(AttackCommand), typeof(PlayerChoiceContext) })]
    [HarmonyPostfix]
    private static void AfterImpaleWithAttackCommand(
        Creature target,
        ArrowSlot arrowSlot,
        AttackCommand attackCommand,
        PlayerChoiceContext context,
        ref Task __result)
    {
        __result = DislodgeIfStrandedOrUnhittableAsync(
            __result,
            target,
            arrowSlot,
            context,
            attackCommand.CardPlay?.Card);
    }

    [HarmonyPatch(
        typeof(ArrowCmd),
        "ImpaleTarget",
        new[] { typeof(Creature), typeof(ArrowSlot), typeof(Func<Task>), typeof(PlayerChoiceContext), typeof(CardModel) })]
    [HarmonyPostfix]
    private static void AfterImpaleWithAction(
        Creature target,
        ArrowSlot arrowSlot,
        PlayerChoiceContext context,
        CardModel? card,
        ref Task __result)
    {
        __result = DislodgeIfStrandedOrUnhittableAsync(
            __result,
            target,
            arrowSlot,
            context,
            card);
    }

    [HarmonyPatch(typeof(NImpaledArrowVfx), nameof(NImpaledArrowVfx._Ready))]
    [HarmonyPostfix]
    private static void AfterImpaledArrowReady(NImpaledArrowVfx __instance)
    {
        if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree())
            return;

        if (__instance.Get(NImpaledArrowVfx.PropertyName._frontSlotNode).AsGodotObject() != null)
            return;

        if (VfxCreatureField?.GetValue(__instance) is not Creature creature)
            return;

        NCombatRoom? room = NCombatRoom.Instance;
        NCreature? nCreature = room?.GetCreatureNode(creature);
        if (nCreature == null || !GodotObject.IsInstanceValid(nCreature) ||
            nCreature.Entity?.Monster is not ThingsMonsterModel monster)
        {
            return;
        }

        Node2D? backArrowNode = __instance.Get(NImpaledArrowVfx.PropertyName._backArrowNode).As<Node2D>();
        Node2D? frontArrowVisual = __instance.Get(NImpaledArrowVfx.PropertyName._frontArrowVisual).As<Node2D>();
        Node2D? backArrowVisual = __instance.Get(NImpaledArrowVfx.PropertyName._backArrowVisual).As<Node2D>();
        if (backArrowNode == null || frontArrowVisual == null || backArrowVisual == null)
            return;

        Vector2 initialGlobalPos = __instance.GlobalPosition;
        if (!TryResolveArrowBoneTarget(
                room,
                nCreature,
                monster,
                out Node2D? spineNode,
                out MegaSprite? megaSprite,
                out string? boneName,
                out float visualScale) ||
            spineNode == null ||
            megaSprite == null ||
            boneName == null)
        {
            return;
        }

        NThingsArrowBoneAnchor? frontAnchor = NThingsArrowBoneAnchor.Create(
            spineNode,
            megaSprite,
            boneName,
            initialGlobalPos,
            showBehindParent: false);
        if (frontAnchor == null)
            return;

        NThingsArrowBoneAnchor? backAnchor = NThingsArrowBoneAnchor.Create(
            spineNode,
            megaSprite,
            boneName,
            initialGlobalPos,
            showBehindParent: true);
        if (backAnchor == null)
        {
            frontAnchor.QueueFreeSafely();
            return;
        }

        __instance.Position = Vector2.Zero;
        __instance.RotationDegrees = 0f;
        backArrowNode.Reparent(__instance, keepGlobalTransform: false);
        backArrowNode.Position = Vector2.Zero;
        backArrowNode.RotationDegrees = 0f;
        backArrowNode.Scale = Vector2.One;

        frontArrowVisual.Reparent(frontAnchor, keepGlobalTransform: false);
        backArrowVisual.Reparent(backAnchor, keepGlobalTransform: false);
        frontArrowVisual.Scale *= visualScale;
        backArrowVisual.Scale *= visualScale;

        ImpaledPower? power = creature.GetPower<ImpaledPower>();
        if (power != null)
        {
            int index = power.Arrows.Count % ArrowRotationOffsets.Length;
            float rotationOffset = ArrowRotationOffsets[index];
            Vector2 positionOffset = ArrowPositionOffsets[index];
            frontArrowVisual.RotationDegrees += rotationOffset;
            backArrowVisual.RotationDegrees += rotationOffset;
            frontArrowVisual.Position += positionOffset;
            backArrowVisual.Position += positionOffset;
        }

        __instance.Set(NImpaledArrowVfx.PropertyName._frontSlotNode, frontAnchor);
        __instance.Set(NImpaledArrowVfx.PropertyName._backSlotNode, backAnchor);
    }

    private static bool TryResolveArrowBoneTarget(
        NCombatRoom? room,
        NCreature nCreature,
        ThingsMonsterModel monster,
        out Node2D? spineNode,
        out MegaSprite? megaSprite,
        out string? boneName,
        out float visualScale)
    {
        spineNode = null;
        megaSprite = null;
        boneName = null;
        visualScale = 1f;

        if (monster is ThingsCaveGodLeftHand or ThingsCaveGodRightHand or ThingsCaveGodCaptiveClaw or ThingsCaveGodBody)
        {
            NCaveGodBossBackground? bg = FindCaveGodBackground(room);
            if (bg == null || !GodotObject.IsInstanceValid(bg) || !bg.IsInsideTree())
                return false;

            ReadOnlySpan<string> candidates;
            if (monster is ThingsCaveGodBody)
            {
                spineNode = bg.BodySpineNode;
                megaSprite = bg.BodySpineController;
                candidates = CaveGodBodyBoneCandidates;
                visualScale = 1.5f;
            }
            else
            {
                bool isLeft = monster is ThingsCaveGodLeftHand or ThingsCaveGodCaptiveClaw { IsLeftSide: true };
                spineNode = bg.ArmsSpineNode;
                megaSprite = bg.ArmsSpineController;
                candidates = isLeft ? LeftArmBoneCandidates : RightArmBoneCandidates;
                visualScale = 1.35f;
            }

            if (spineNode == null || !GodotObject.IsInstanceValid(spineNode) || !spineNode.IsInsideTree())
                return false;

            if (megaSprite == null || !GodotObject.IsInstanceValid(megaSprite.BoundObject))
            {
                megaSprite = new MegaSprite(spineNode);
            }

            boneName = TryResolveBoneName(megaSprite, candidates);
            if (boneName == null)
            {
                megaSprite = null;
                return false;
            }

            return true;
        }

        if (nCreature.Visuals is { HasSpineAnimation: true, Body: { } body, SpineBody: { } spineBody } &&
            !nCreature.HasSpineArrowSlots &&
            GodotObject.IsInstanceValid(body) &&
            body.IsInsideTree() &&
            GodotObject.IsInstanceValid(spineBody.BoundObject))
        {
            boneName = TryResolveBoneName(spineBody, CustomMonsterBoneCandidates);
            if (boneName == null)
                return false;

            spineNode = body;
            megaSprite = spineBody;
            visualScale = 1f / MathF.Max(0.25f, MathF.Abs(body.Scale.X)) * Rng.Chaotic.NextFloat(0.9f, 1.1f);
            return true;
        }

        return false;
    }

    private static string? TryResolveBoneName(MegaSprite megaSprite, ReadOnlySpan<string> candidates)
    {
        if (!GodotObject.IsInstanceValid(megaSprite.BoundObject))
            return null;

        MegaSkeleton? skeleton = megaSprite.GetSkeleton();
        if (skeleton == null)
            return null;

        foreach (string candidate in candidates)
        {
            MegaBone? bone = skeleton.FindBone(candidate);
            if (bone != null && megaSprite.GetGlobalBoneTransform(candidate).HasValue)
                return candidate;
        }

        return null;
    }

    private static NCaveGodBossBackground? FindCaveGodBackground(NCombatRoom? room)
    {
        if (room == null || !GodotObject.IsInstanceValid(room))
            return null;

        if (room.Background != null && GodotObject.IsInstanceValid(room.Background))
        {
            if (room.Background.GetNodeOrNull<NCaveGodBossBackground>("%CaveGod") is { } unique)
                return unique;
            if (FindDescendant<NCaveGodBossBackground>(room.Background) is { } inBg)
                return inBg;
        }

        return FindDescendant<NCaveGodBossBackground>(room);
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child))
                continue;
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }

        return null;
    }

    private static async Task DislodgeIfStrandedOrUnhittableAsync(
        Task innerTask,
        Creature target,
        ArrowSlot arrowSlot,
        PlayerChoiceContext context,
        CardModel? card)
    {
        await innerTask;
        if (arrowSlot.Status != ArrowStatus.Away)
            return;

        bool impaledOnTarget = ArrowCmd.CreatureIsImpaledByPlayersArrow(target, arrowSlot.owner);
        if (impaledOnTarget && target.IsHittable)
            return;

        ICombatState? combatState = arrowSlot.owner.Creature.CombatState ?? target.CombatState;
        if (combatState == null || CombatManager.Instance.IsEnding)
            return;

        if (impaledOnTarget)
        {
            while (ArrowCmd.CreatureIsImpaledByPlayersArrow(target, arrowSlot.owner))
            {
                await ArrowCmd.DislodgeArrow(target, arrowSlot.owner, context, card);
            }
            return;
        }

        if (arrowSlot.DestroyAfterImpaled)
        {
            arrowSlot.Status = ArrowStatus.Destroyed;
            return;
        }

        var transientImpaled = (ImpaledPower)ModelDb.Power<ImpaledPower>().ToMutable();
        transientImpaled.Applier = arrowSlot.owner.Creature;
        transientImpaled.AddArrow(arrowSlot);
        await ArrowCmd.DislodgeArrow(combatState, transientImpaled, context, card);
    }
}
#endif
