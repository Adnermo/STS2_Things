using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Events;

namespace STS2_Things.Hooks;

/// <summary>
/// Persists non-canonical <see cref="CardKeyword.Eternal"/> keywords added to cards
/// (such as <see cref="ThingsCrackedBrazier"/>'s Eternal Burn) across JSON save/load
/// and multiplayer <see cref="SerializableCard"/> packet serialization.
/// Vanilla STS2 intentionally does not serialize <see cref="CardModel.Keywords"/>.
/// </summary>
[HarmonyPatch(typeof(CardModel))]
internal static class EternalCardKeywordPersistencePatch
{
    [HarmonyPatch(nameof(CardModel.ToSerializable))]
    [HarmonyPostfix]
    private static void ToSerializablePostfix(CardModel __instance, SerializableCard __result)
    {
        if (__instance == null || __result == null)
            return;

        // Only persist Eternal when it was added locally on a card that doesn't already
        // get Eternal from CanonicalKeywords or Tezcatara's Ember (which re-applies Eternal on load).
        if (!__instance.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Eternal)
            || __instance.CanonicalKeywords.Contains(CardKeyword.Eternal)
            || __instance.Enchantment is TezcatarasEmber)
            return;

        __result.Props ??= new SavedProperties();
        __result.Props.bools ??= [];
        if (!__result.Props.bools.Any(entry => entry.name == ThingsCrackedBrazier.EternalKeywordSavedPropertyName))
        {
            __result.Props.bools.Add(
                new SavedProperties.SavedProperty<bool>(ThingsCrackedBrazier.EternalKeywordSavedPropertyName, true));
        }
    }

    [HarmonyPatch(nameof(CardModel.FromSerializable), typeof(SerializableCard))]
    [HarmonyPostfix]
    private static void FromSerializablePostfix(SerializableCard save, CardModel __result)
    {
        if (__result == null || save?.Props?.bools == null)
            return;

        foreach (SavedProperties.SavedProperty<bool> entry in save.Props.bools)
        {
            if (entry.name == ThingsCrackedBrazier.EternalKeywordSavedPropertyName && entry.value)
            {
                if (!__result.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Eternal))
                    __result.AddKeyword(CardKeyword.Eternal);
                break;
            }
        }
    }
}
