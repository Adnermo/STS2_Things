using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;
using STS2_Things.Enchantments;

namespace STS2_Things.Events;

public sealed class ThingsCuttingItClose : ThingsEventModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new LocalizedStringVar("Enchantment", static _ => ModelDb.Enchantment<ThingsSplit>().Title.GetFormattedText())
    ];

    public override bool IsAllowed(IRunState runState)
    {
        var split = ModelDb.Enchantment<ThingsSplit>();
        return runState.Players.All(player => player.Deck.Cards.Any(card => IsSplitCandidate(split, card)));
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var split = ModelDb.Enchantment<ThingsSplit>();
        bool canSplit = EventOwner.Deck.Cards.Any(card => IsSplitCandidate(split, card));
        bool canThrow = EventOwner.Deck.Cards.Any(card => card.IsRemovable);
        var options = new List<EventOption>
        {
            new EventOption(
                this,
                canSplit ? Improvise : null,
                InitialOptionKey(canSplit ? "IMPROVISE" : "IMPROVISE_LOCKED"),
                HoverTipFactory.FromEnchantment<ThingsSplit>()),
            new EventOption(
                this,
                canThrow ? Throw : null,
                InitialOptionKey(canThrow ? "THROW" : "THROW_LOCKED"))
        };
        if (!canSplit && !canThrow)
        {
            options.Add(new EventOption(this, Leave, "THINGS_POLITE_MAW.pages.INITIAL.options.LEAVE"));
        }
        return options;
    }

    private Task Leave()
    {
        FinishWithoutCard();
        return Task.CompletedTask;
    }

    private async Task Improvise()
    {
        var split = ModelDb.Enchantment<ThingsSplit>();
        var selected = (await CardSelectCmd.FromDeckForEnchantment(
            EventOwner,
            split,
            1,
            card => card is not null && IsSplitCandidate(split, card),
            new CardSelectorPrefs(
                L10NLookup("THINGS_CUTTING_IT_CLOSE.pages.IMPROVISE.selectionScreenPrompt"),
                1))).FirstOrDefault();

        if (selected is null)
        {
            FinishWithoutCard();
            return;
        }

        CardModel[] copies =
        [
            EventOwner.RunState.CloneCard(selected),
            EventOwner.RunState.CloneCard(selected)
        ];

        foreach (var copy in copies)
        {
            CardCmd.Enchant<ThingsSplit>(copy, 1m);
        }

        await CardPileCmd.RemoveFromDeck(selected);
        var addResults = await CardPileCmd.Add(copies, PileType.Deck);
        CardCmd.PreviewCardPileAdd(addResults, 1.6f, CardPreviewStyle.EventLayout);
        SetEventFinished(L10NLookup("THINGS_CUTTING_IT_CLOSE.pages.IMPROVISE.description"));
    }

    private async Task Throw()
    {
        var selected = (await CardSelectCmd.FromDeckForRemoval(
            EventOwner,
            new CardSelectorPrefs(
                L10NLookup("THINGS_CUTTING_IT_CLOSE.pages.THROW.selectionScreenPrompt"),
                1))).FirstOrDefault();

        if (selected is null)
        {
            FinishWithoutCard();
            return;
        }

        await CardPileCmd.RemoveFromDeck(selected);
        SetEventFinished(L10NLookup("THINGS_CUTTING_IT_CLOSE.pages.THROW.description"));
    }

    private static bool IsSplitCandidate(ThingsSplit split, CardModel card)
    {
        return card.Type == CardType.Attack &&
               card.IsRemovable &&
               split.CanEnchant(card);
    }

    private void FinishWithoutCard()
    {
        // Selection is normally mandatory. Still close the event cleanly if a
        // replay, disconnect, death, or future cancelable selector returns no
        // card; EventOption has already recorded the click at this point.
        SetEventFinished(L10NLookup("THINGS_CUTTING_IT_CLOSE.pages.ABORTED.description"));
    }
}
