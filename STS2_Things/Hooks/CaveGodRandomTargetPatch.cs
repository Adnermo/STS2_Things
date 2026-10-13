using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using STS2_Things.Monsters;

namespace STS2_Things.Hooks;

/// <summary>Native random attacks only check IsAlive; the megalith's protected parts stay alive.</summary>
[HarmonyPatch(typeof(AttackCommand), "GetPossibleTargets")]
internal static class CaveGodRandomTargetPatch
{
    private static void Postfix(AttackCommand __instance, ref IReadOnlyList<Creature> __result)
    {
        if (!__instance.IsRandomlyTargeted || __instance.Attacker?.Side != CombatSide.Player
            || !__result.Any(IsProtectedPart)) return;

        // Re-evaluated by the native command on each hit, so an arm break exposes
        // the core immediately. Preserve the original order and RNG selection.
        // Explicit targets, AOE and all other enemies keep native behavior.
        __result = __result.Where(creature => !IsProtectedPart(creature)).ToArray();
    }

    private static bool IsProtectedPart(Creature creature) =>
        (creature.Monster is ThingsCaveGodBody or ThingsCaveGodHand) && !creature.IsHittable;
}
