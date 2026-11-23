using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

public sealed class ThingsNarrowGate : ThingsEventModel
{
    public const int SqueezeDamage = 7;
    public const int RerollCost = 35;
    public const int CandidateCount = 3;
    private CardModel[]? _candidates;
    private HashSet<CardModel> _seenCandidates = [];
    private int _rerollCount;
    private bool _settling;
    private CardModel[] Candidates => _candidates ?? Array.Empty<CardModel>();

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _seenCandidates = [];
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _candidates = null;
        _seenCandidates.Clear();
        _rerollCount = 0;
        _settling = false;
    }
    private CardModel[] Removable => EventOwner.Deck.Cards.Count > 1
        ? EventOwner.Deck.Cards.Where(card => card.IsRemovable).ToArray() : Array.Empty<CardModel>();
    private bool CanRemove => Candidates.Any(card => Removable.Contains(card));
    private bool HasAlternatives => Removable.Any(card => !_seenCandidates.Contains(card));

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(RerollCost), new DamageVar("SqueezeDamage", SqueezeDamage, ValueProp.Unblockable | ValueProp.Unpowered), new DynamicVar("Candidates", CandidateCount)];
    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > SqueezeDamage
            && player.Deck.Cards.Count > 1
            && player.Deck.Cards.Any(card => card.IsRemovable));
    public override IEnumerable<LocString> GameInfoOptions
    {
        get
        {
            foreach (var text in DepthsEventDefaults.DescribeOptions(this, "REMOVE_CARD", "REROLL", "LEAVE"))
            {
                text.Add("Card", L10NLookup("THINGS_NARROW_GATE.card").GetFormattedText());
                yield return text;
            }
        }
    }

    public override void OnRoomEnter()
    {
        base.OnRoomEnter();
        if (Node?.GetNodeOrNull<Control>("%EventDescription") is { } description)
            description.CustomMinimumSize = new Vector2(description.CustomMinimumSize.X, 144);
    }

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (!isPreFinished && _candidates == null)
        {
            _candidates = Removable.ToList().StableShuffle(Rng).Take(CandidateCount).ToArray();
            foreach (CardModel card in _candidates) _seenCandidates.Add(card);
        }
        return Task.CompletedTask;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        string reroll = !HasAlternatives ? "NO_ALTERNATIVES" :
            EventOwner.Gold < RerollCost ? "NO_GOLD" : "REROLL";
        var options = new List<EventOption>();
        for (int i = 0; i < CandidateCount; i++)
        {
            var card = Candidates.ElementAtOrDefault(i);
            if (card != null && Removable.Contains(card))
            {
                string key = InitialOptionKey("REMOVE_CARD");
                var title = L10NLookup(key + ".title");
                var description = L10NLookup(key + ".description");
                title.Add("Card", card.Title);
                description.Add("Card", card.Title);
                options.Add(new EventOption(this, () => RemoveCard(card), title, description, key,
                    [HoverTipFactory.FromCard(card)]).ThatDoesDamage(SqueezeDamage).ThatHasDynamicTitle());
            }
            else if (i == 0 && !CanRemove)
            {
                // An empty/one-card deck must still have a way to finish.
                options.Add(new EventOption(this, Leave, InitialOptionKey("LEAVE")));
            }
            else options.Add(new EventOption(this, null, InitialOptionKey("REMOVE_LOCKED")));
        }
        // Retain this position even when fewer than three cards are eligible.
        options.Add(new EventOption(this, reroll == "REROLL" ? Reroll : null, InitialOptionKey(reroll)));
        return options;
    }

    private async Task RemoveCard(CardModel card)
    {
        if (IsFinished || _settling) return;
        if (!Removable.Contains(card) || !Candidates.Contains(card) || EventOwner.Creature.IsDead)
        {
            Refresh();
            return;
        }
        _settling = true;
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, SqueezeDamage);
        if (EventOwner.Creature.IsDead)
        {
            SetEventFinished(L10NLookup("THINGS_NARROW_GATE.pages.REMOVED.loss"));
            return;
        }
        await CardPileCmd.RemoveFromDeck(card);
        SetEventFinished(L10NLookup("THINGS_NARROW_GATE.pages.REMOVED.description"));
    }

    private async Task Reroll()
    {
        if (IsFinished || _settling || !HasAlternatives || EventOwner.Gold < RerollCost) return;
        _settling = true;
        _rerollCount++;
        var available = Removable;
        // Prefer cards the player has not seen yet; only backfill from previously
        // shown cards when fewer than CandidateCount unseen cards remain.
        var fresh = available.Where(card => !_seenCandidates.Contains(card)).ToList().StableShuffle(Rng);
        var remaining = available.Where(card => !fresh.Contains(card)).ToList().StableShuffle(Rng);
        _candidates = fresh.Concat(remaining).Take(CandidateCount).ToArray();
        foreach (CardModel card in _candidates) _seenCandidates.Add(card);
        await PlayerCmd.LoseGold(RerollCost, EventOwner, GoldLossType.Spent);
        _settling = false;
        Refresh();
    }

    private void Refresh()
    {
        if (IsFinished || _settling) return;
        LocString description = _rerollCount switch
        {
            0 => InitialDescription,
            1 => L10NLookup("THINGS_NARROW_GATE.pages.REROLLED.description"),
            _ => L10NLookup("THINGS_NARROW_GATE.pages.REROLLED_MORE.description"),
        };
        SetEventState(description, GenerateInitialOptions());
    }

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        SetEventFinished(L10NLookup("THINGS_NARROW_GATE.pages.LEAVE.description"));
        return Task.CompletedTask;
    }
}
