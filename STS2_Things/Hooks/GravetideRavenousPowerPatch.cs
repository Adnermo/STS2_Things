using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Monsters;
using STS2_Things.Platform;

namespace STS2_Things.Hooks;

/// <summary>
/// Lets native <see cref="RavenousPower"/> drive the shared Corpse Slug devour state on
/// <see cref="ThingsGravetideCorpseSlug"/> without casting <c>Owner.Monster</c> to <c>CorpseSlug</c>.
/// </summary>
[HarmonyPatch(typeof(RavenousPower), nameof(RavenousPower.AfterDeath))]
internal static class GravetideRavenousPowerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        RavenousPower __instance,
        PlayerChoiceContext choiceContext,
        Creature target,
        bool wasRemovalPrevented,
        ref Task __result)
    {
        if (__instance.Owner.Monster is not ThingsGravetideCorpseSlug)
            return true;

        __result = HandleAfterDeathAsync(__instance, choiceContext, target, wasRemovalPrevented);
        return false;
    }

    private static async Task HandleAfterDeathAsync(
        RavenousPower power,
        PlayerChoiceContext choiceContext,
        Creature target,
        bool wasRemovalPrevented)
    {
        Creature owner = power.Owner;
        if (wasRemovalPrevented ||
            target == owner ||
            target.Side != owner.Side ||
            owner.IsDead ||
            owner.Monster is not GravetideSlugBase slug)
        {
            return;
        }

        EngineAccess.FlashPower(power);
        SfxCmd.Play(GravetideSlugBase.DevourSfx);
        await CreatureCmd.TriggerAnim(owner, GravetideSlugBase.DevourStartTrigger, 0.5f);
        slug.IsDevouring = true;
        await CreatureCmd.Stun(owner, _ => EndRavenousAsync(owner, slug));
        await PowerCmd.Apply<StrengthPower>(choiceContext, owner, power.Amount, owner, null);
    }

    private static async Task EndRavenousAsync(Creature owner, GravetideSlugBase slug)
    {
        if (owner.IsDead)
            return;

        SfxCmd.Play(GravetideSlugBase.DevourEndSfx);
        await CreatureCmd.TriggerAnim(owner, GravetideSlugBase.DevourEndTrigger, 0.5f);
        slug.IsDevouring = false;
    }
}
