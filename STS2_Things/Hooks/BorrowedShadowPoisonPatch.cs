using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Hooks;

[HarmonyPatch]
internal static class BorrowedShadowPoisonPatch
{
    private static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(
        AccessTools.DeclaredMethod(typeof(PoisonPower), nameof(PoisonPower.Trigger)));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var nativeDamage = AccessTools.DeclaredMethod(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(ValueProp),
                typeof(CardModel), typeof(CardPlay)]);
        var calls = code.Where(instruction => instruction.Calls(nativeDamage)).ToArray();
        if (calls.Length != 1)
        {
            Log.Warn("STS2_Things: Poison damage call changed; Borrowed Shadow poison attribution is unavailable.");
            return code;
        }
        calls[0].operand = AccessTools.DeclaredMethod(typeof(BorrowedShadowPoisonPatch), nameof(Damage));
        return code;
    }

    // Poison intentionally has no dealer in the native damage pipeline. Keep that
    // intact (including damage modifiers and on-hit hooks), and convert only the
    // results of this exact Poison call rather than inferring from damage flags.
    private static async Task<IEnumerable<DamageResult>> Damage(
        PlayerChoiceContext choiceContext,
        Creature target,
        decimal amount,
        ValueProp props,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        bool hasSource = cardSource != null;
        var shadow = hasSource ? null : target.GetPower<ThingsBorrowedShadowPower>();
        var poison = shadow != null ? target.GetPower<PoisonPower>() : null;
        var combat = target.CombatState;
        var results = await CreatureCmd.Damage(choiceContext, target, amount, props, cardSource, cardPlay);
        if (shadow != null && poison != null && combat != null)
            await shadow.AfterPoisonDamage(poison, results.Where(result => result.Receiver == target).Sum(result => result.UnblockedDamage), combat);
        return results;
    }
}
