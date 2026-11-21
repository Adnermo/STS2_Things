using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using STS2_Things.Core.Models;
using STS2_Things.Relics;
using STS2_Things.RestSite;

namespace STS2_Things.Modifiers;

// Run-state subscribers are enumerated after native relics, so Shovel/Mend and
// normal relic modifiers finish before this spent-camp rule is applied.
public sealed class ThingsSpentEmberRestPolicy : ThingsModifierModel
{
    public override LocString Title => new("relics", "THINGS_BORROWED_EMBER.title");

    public override LocString Description => new("relics", "THINGS_BORROWED_EMBER.debtTerms");

    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options)
    {
        if (player.GetRelic<ThingsBorrowedEmber>() is not { IsSpentCampHere: true }) return false;
        options.Clear();
        options.Add(new SpentEmberRestSiteOption(player));
        return true;
    }
}
