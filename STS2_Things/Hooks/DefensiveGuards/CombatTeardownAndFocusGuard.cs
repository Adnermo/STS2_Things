using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Config;

namespace STS2_Things.Hooks.DefensiveGuards;

/// <summary>
/// Defensive combat teardown &amp; controller focus guard:
/// 1. Prevents <c>NullReferenceException</c> / disposed-object crashes in <see cref="NControllerCardPlay.OnCancelPlayCard"/>
///    when combat tears down while a card targeting play is active.
/// 2. Keeps non-interactable creatures (<c>!IsInteractable</c>, such as submerged/protected boss parts) out of
///    controller focus order in <see cref="NCombatRoom.EnableControllerNavigation"/> and <c>NCombatRoom.UpdateCreatureNavigation</c>.
/// </summary>
internal static class CombatTeardownAndFocusGuard
{
    private static bool IsActive => ThingsModConfig.IsEnabled(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled);

    [HarmonyPatch(typeof(NControllerCardPlay), "OnCancelPlayCard")]
    private static class SafeCancelControllerCardPlayPatch
    {
        private static bool Prefix(NControllerCardPlay __instance)
        {
            if (!IsActive)
                return true;

            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
                return false;

            if (!GodotObject.IsInstanceValid(__instance) ||
                __instance.Holder == null ||
                !GodotObject.IsInstanceValid(__instance.Holder) ||
                !__instance.Holder.IsInsideTree())
            {
                room.EnableControllerNavigation();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.EnableControllerNavigation))]
    private static class NonInteractableCreatureFocusGuardPatch
    {
        private static void Postfix(NCombatRoom __instance)
        {
            if (!IsActive || !GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree())
                return;

            foreach (NCreature creatureNode in __instance.CreatureNodes)
            {
                if (GodotObject.IsInstanceValid(creatureNode) &&
                    !creatureNode.Entity.IsDead &&
                    !creatureNode.IsInteractable &&
                    creatureNode.Hitbox != null &&
                    GodotObject.IsInstanceValid(creatureNode.Hitbox))
                {
                    creatureNode.Hitbox.FocusMode = Control.FocusModeEnum.None;
                }
            }
        }
    }

    [HarmonyPatch(typeof(NCombatRoom), "UpdateCreatureNavigation")]
    private static class FirstInteractableTopNeighborGuardPatch
    {
        private static void Postfix(NCombatRoom __instance)
        {
            if (!IsActive || !GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree())
                return;

            Control? holderContainer = __instance.Ui?.Hand?.CardHolderContainer;
            if (holderContainer == null || !GodotObject.IsInstanceValid(holderContainer))
                return;

            NCreature? firstInteractable = __instance.CreatureNodes
                .Where(static n => GodotObject.IsInstanceValid(n) && n.IsInteractable && n.Hitbox != null && GodotObject.IsInstanceValid(n.Hitbox))
                .OrderBy(static n => n.GlobalPosition.X)
                .FirstOrDefault();

            holderContainer.FocusNeighborTop = firstInteractable?.Hitbox.GetPath() ?? new NodePath();
        }
    }
}
