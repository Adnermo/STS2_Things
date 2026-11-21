using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_Things.Hooks;

/// <summary>
/// Vanilla STS2 only injects <see cref="CloneRestSiteOption"/> via <c>PaelsGrowth.TryModifyRestSiteOptions</c>;
/// the <see cref="Clone"/> enchantment itself does not register a campfire button. When a player obtains a
/// <see cref="Clone"/>-enchanted card without Pael's Growth (e.g. from Gilded Press or Overgrown Rune Loom),
/// append a single <see cref="CloneRestSiteOption"/> deterministically so the enchantment works as described.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyRestSiteOptions))]
internal static class CloneEnchantmentRestSitePatch
{
    private static void Postfix(IRunState runState, Player player, ICollection<RestSiteOption> options)
    {
        if (player?.Deck?.Cards == null || options == null)
            return;

        if (!player.Deck.Cards.Any(card => card.Enchantment is Clone))
            return;

        if (options.OfType<CloneRestSiteOption>().Any())
            return;

        options.Add(new CloneRestSiteOption(player));
    }
}
