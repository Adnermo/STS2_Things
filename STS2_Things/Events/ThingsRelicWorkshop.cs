using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>A personal, previewed Uncommon exchange, sale, or card reforge using native rules.</summary>
public sealed class ThingsRelicWorkshop : ThingsEventModel
{
    public const int TradeCost = 65;
    public const int SaleGold = 150;
    public const int ReforgeDamage = 8;
    private const int PageSize = 3;
    private RelicModel? _offer;
    private bool _settling;
    private RelicModel[] TradableRelics => EventOwner.Relics.Where(relic => relic.IsTradable).ToArray();
    private bool CanTrade => _offer != null && TradableRelics.Length > 0 && EventOwner.Gold >= TradeCost;
    private static bool CanTransform(CardModel card) => card.Type != CardType.Quest && card.IsTransformable;
    private bool HasTransformableCard => EventOwner.Deck.Cards.Any(CanTransform);

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _offer = null;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(TradeCost), new IntVar("SaleGold", SaleGold),
        new DamageVar("ReforgeDamage", ReforgeDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new LocalizedStringVar("Offer", static owner =>
            owner is ThingsRelicWorkshop { _offer: { } r }
                ? r.Title.GetFormattedText()
                : new LocString("events", "THINGS_RELIC_WORKSHOP.uncommonRelic").GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && player.Relics.Any(relic => relic.IsTradable));
    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "TRADE", "SELL", "REFORGE");

    public override void OnRoomEnter()
    {
        base.OnRoomEnter();
        // At most three relics plus More and Back, using native event buttons.
        if (Node?.GetNodeOrNull<Control>("%EventDescription") is { } description)
            description.CustomMinimumSize = new Vector2(description.CustomMinimumSize.X, 144);
    }

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (!isPreFinished && _offer == null && TradableRelics.Length > 0)
        {
            var relic = RelicFactory.PullNextRelicFromFront(EventOwner, RelicRarity.Uncommon,
                candidate => EventOwner.Relics.All(owned => owned.Id != candidate.Id));
            // An exhausted grab bag returns Circlet. Do not advertise it as Uncommon.
            if (relic.Rarity == RelicRarity.Uncommon && EventOwner.Relics.All(owned => owned.Id != relic.Id))
            {
                _offer = relic.ToMutable();
                _offer.Owner = EventOwner;
                ((StringVar)DynamicVars["Offer"]).StringValue = _offer.Title.GetFormattedText();
            }
        }
        return Task.CompletedTask;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        string trade = TradableRelics.Length == 0 ? "NO_RELIC" : _offer == null ? "NO_OFFER" :
            EventOwner.Gold < TradeCost ? "NO_GOLD" : "TRADE";
        EventOption tradeOption = new(
            this,
            trade == "TRADE" ? () => ShowRelics(false, 0) : null,
            InitialOptionKey(trade),
            _offer?.HoverTips ?? Array.Empty<IHoverTip>());
        if (trade == "TRADE")
            tradeOption.ThatWontSaveToChoiceHistory();

        bool canSell = TradableRelics.Length > 0;
        EventOption sellOption = new(
            this,
            canSell ? () => ShowRelics(true, 0) : null,
            InitialOptionKey(canSell ? "SELL" : "SELL_LOCKED"));
        if (canSell)
            sellOption.ThatWontSaveToChoiceHistory();

        var options = new List<EventOption>
        {
            tradeOption,
            sellOption,
            HasTransformableCard
                ? new EventOption(this, ReforgeCard, InitialOptionKey("REFORGE"),
                    HoverTipFactory.Static(StaticHoverTip.Transform))
                    .ThatDoesDamage(ReforgeDamage)
                : new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        };
        if (!canSell && HasTransformableCard && EventOwner.Creature.CurrentHp <= ReforgeDamage)
        {
            options.Add(new EventOption(this, Leave, InitialOptionKey("LEAVE")));
        }
        return options;
    }

    private Task ShowRelics(bool selling, int page)
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        var relics = TradableRelics;
        if (relics.Length == 0 || (!selling && !CanTrade)) return Back();
        int pages = (relics.Length + PageSize - 1) / PageSize;
        page %= pages;
        var options = new List<EventOption>();
        string key = $"THINGS_RELIC_WORKSHOP.pages.SELECT.options.{(selling ? "SELL" : "TRADE")}";
        foreach (var relic in relics.Skip(page * PageSize).Take(PageSize))
        {
            var title = L10NLookup(key + ".title");
            title.Add("Relic", relic.Title.GetFormattedText());
            var description = L10NLookup(key + ".description");
            description.Add("Relic", relic.Title.GetFormattedText());
            var tips = selling ? relic.HoverTips : relic.HoverTips.Concat(_offer!.HoverTips);
            options.Add(new EventOption(this, () => Settle(relic, selling), title, description, key, tips)
                .ThatHasDynamicTitle());
        }
        if (pages > 1)
        {
            int nextPage = (page + 1) % pages;
            const string moreKey = "THINGS_RELIC_WORKSHOP.pages.SELECT.options.MORE";
            var description = L10NLookup(moreKey + ".description");
            description.Add("Page", page + 1);
            description.Add("Pages", pages);
            options.Add(new EventOption(this, () => ShowRelics(selling, nextPage),
                L10NLookup(moreKey + ".title"), description, moreKey, Array.Empty<IHoverTip>())
                .ThatWontSaveToChoiceHistory());
        }
        options.Add(new EventOption(this, Back, "THINGS_RELIC_WORKSHOP.pages.SELECT.options.BACK")
            .ThatWontSaveToChoiceHistory());
        SetEventState(L10NLookup($"THINGS_RELIC_WORKSHOP.pages.SELECT_{(selling ? "SELL" : "TRADE")}.description"), options);
        return Task.CompletedTask;
    }

    private async Task Settle(RelicModel relic, bool selling)
    {
        if (IsFinished || _settling) return;
        if (EventOwner.Creature.IsDead || !EventOwner.Relics.Contains(relic) || !relic.IsTradable ||
            (!selling && !CanTrade)) { await Back(); return; }
        _settling = true;
        await RelicCmd.Remove(relic);
        if (selling) await PlayerCmd.GainGold(SaleGold, EventOwner);
        else
        {
            await PlayerCmd.LoseGold(TradeCost, EventOwner, GoldLossType.Spent);
            await RelicCmd.Obtain(_offer!, EventOwner);
        }
        SetEventFinished(L10NLookup($"THINGS_RELIC_WORKSHOP.pages.{(selling ? "SOLD" : "TRADED")}.description"));
    }

    private Task Back()
    {
        if (!IsFinished && !_settling) SetEventState(InitialDescription, GenerateInitialOptions());
        return Task.CompletedTask;
    }

    private async Task ReforgeCard()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        CardModel? selected = (await CardSelectCmd.FromDeckForTransformation(EventOwner,
            new CardSelectorPrefs(L10NLookup("THINGS_RELIC_WORKSHOP.reforgePrompt"), 1)
                { Cancelable = true, RequireManualConfirmation = true }))
            .FirstOrDefault();
        if (selected == null || EventOwner.Creature.IsDead ||
            !EventOwner.Deck.Cards.Contains(selected) || !CanTransform(selected))
        {
            _settling = false;
            await Back();
            return;
        }
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, ReforgeDamage);
        if (EventOwner.Creature.IsDead)
        {
            SetEventFinished(L10NLookup("THINGS_RELIC_WORKSHOP.pages.REFORGE.loss"));
            return;
        }
        await CardCmd.TransformToRandom(selected, Rng, CardPreviewStyle.EventLayout);
        SetEventFinished(L10NLookup("THINGS_RELIC_WORKSHOP.pages.REFORGE.description"));
    }

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        SetEventFinished(L10NLookup("THINGS_RELIC_WORKSHOP.pages.LEAVE.description"));
        return Task.CompletedTask;
    }
}
