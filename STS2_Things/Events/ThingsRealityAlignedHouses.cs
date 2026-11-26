using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>Per-player choices about repetition, returning home, and an endless road.</summary>
public sealed class ThingsRealityAlignedHouses : ThingsEventModel
{
    public const int MergeDamage = 10;
    public const int AddressMaxHpLoss = 7;
    public const int InitialRoadHpLoss = 6;
    public const int InitialRoadGold = 50;
    private const int MaximumLaps = 3;
    private int _completedLaps;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _completedLaps = 0;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("MergeDamage", MergeDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new IntVar("AddressMaxHpLoss", AddressMaxHpLoss),
        new HpLossVar("RoadHpLoss", InitialRoadHpLoss),
        new GoldVar(InitialRoadGold),
        new IntVar("MaximumLaps", MaximumLaps),
        new IntVar("NextLap", 1),
        new LocalizedStringVar("Enchantment", static _ => ModelDb.Enchantment<PerfectFit>().Title.GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(p =>
            !p.Creature.IsDead
            && (p.Creature.CurrentHp > InitialRoadHpLoss
                || p.Creature.MaxHp > AddressMaxHpLoss));

    // Keep 1.19.0 localization keys available to old run histories, but publish
    // only the new options in the native game-information catalogue.
    public override IEnumerable<LocString> GameInfoOptions
    {
        get
        {
            foreach (string key in new[] { "MERGE", "MERGE_LOCKED", "ADDRESS", "ADDRESS_LOCKED", "ROAD", "BLUE" })
                foreach (string part in new[] { "title", "description" })
                {
                    var text = L10NLookup($"{InitialOptionKey(key)}.{part}");
                    DynamicVars.AddTo(text);
                    yield return text;
                }
        }
    }

    private static bool IsMergeCard(CardModel card) => card.IsRemovable && card.IsUpgradable &&
        !card.IsUpgraded && card.Enchantment == null &&
        card.Type is CardType.Attack or CardType.Skill or CardType.Power;

    private List<CardModel> MergeCandidates()
    {
        var cards = EventOwner.Deck.Cards.Where(IsMergeCard).ToList();
        var paired = cards.GroupBy(card => card.Id).Where(group => group.Count() >= 2)
            .Select(group => group.Key).ToHashSet();
        return cards.Where(card => paired.Contains(card.Id)).ToList();
    }

    private static bool IsAddressCard(CardModel card) =>
        card.IsUpgradable && ModelDb.Enchantment<PerfectFit>().CanEnchant(card);

    private IEnumerable<IHoverTip> RoadTips()
    {
        var description = L10NLookup("THINGS_REALITY_ALIGNED_HOUSES.roadHint.description");
        DynamicVars.AddTo(description);
        yield return new HoverTip(L10NLookup("THINGS_REALITY_ALIGNED_HOUSES.roadHint.title"), description);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool merge = MergeCandidates().Count > 0;
        bool address = EventOwner.Creature.MaxHp > AddressMaxHpLoss && EventOwner.Deck.Cards.Any(IsAddressCard);
        EventOption mergeOption = new(this, merge ? MergeHouses : null,
            InitialOptionKey(merge ? "MERGE" : "MERGE_LOCKED"));
        if (merge) mergeOption.ThatDoesDamage(MergeDamage);
        return
        [
            mergeOption,
            address
                ? new EventOption(this, LeaveAnAddress, InitialOptionKey("ADDRESS"),
                    HoverTipFactory.FromEnchantment<PerfectFit>())
                    .ThatDecreasesMaxHp(DynamicVars["AddressMaxHpLoss"].BaseValue)
                : new EventOption(this, null, InitialOptionKey("ADDRESS_LOCKED")),
            new EventOption(this, WalkAnotherLap, InitialOptionKey("ROAD"), RoadTips())
                .ThatDoesDamage(DynamicVars["RoadHpLoss"].BaseValue),
            new EventOption(this, FollowBlueHouse, InitialOptionKey("BLUE")),
        ];
    }

    private async Task MergeHouses()
    {
        var candidates = MergeCandidates().ToHashSet();
        var keeper = (await CardSelectCmd.FromDeckGeneric(EventOwner,
            new CardSelectorPrefs(L10NLookup("THINGS_REALITY_ALIGNED_HOUSES.pages.MERGE.keepPrompt"), 1),
            candidates.Contains)).FirstOrDefault();
        if (keeper == null || EventOwner.Creature.IsDead) { FinishWithoutSelection(); return; }

        // Two native selections let the player choose exactly which copy stays.
        // Never silently consume an upgraded, enchanted, or non-removable card.
        var other = (await CardSelectCmd.FromDeckForRemoval(EventOwner,
            new CardSelectorPrefs(L10NLookup("THINGS_REALITY_ALIGNED_HOUSES.pages.MERGE.foldPrompt"), 1),
            card => card != keeper && card.Id == keeper.Id && IsMergeCard(card))).FirstOrDefault();
        if (other == null || EventOwner.Creature.IsDead ||
            !EventOwner.Deck.Cards.Contains(keeper) || !IsMergeCard(keeper))
        { FinishWithoutSelection(); return; }

        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, MergeDamage);
        if (EventOwner.Creature.IsDead) { Finish("DEATH"); return; }

        await CardPileCmd.RemoveFromDeck(other);
        if (EventOwner.Creature.IsDead) { Finish("DEATH"); return; }
        CardCmd.Upgrade(keeper, CardPreviewStyle.EventLayout);
        Finish("MERGE");
    }

    private async Task LeaveAnAddress()
    {
        var selected = (await CardSelectCmd.FromDeckForEnchantment(EventOwner,
            ModelDb.Enchantment<PerfectFit>(), 1, card => card != null && card.IsUpgradable,
            new CardSelectorPrefs(L10NLookup("THINGS_REALITY_ALIGNED_HOUSES.pages.ADDRESS.selectionPrompt"), 1)))
            .FirstOrDefault();
        if (selected == null || EventOwner.Creature.IsDead) { FinishWithoutSelection(); return; }

        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(),
            DynamicVars["AddressMaxHpLoss"].BaseValue);
        if (EventOwner.Creature.IsDead) { Finish("DEATH"); return; }
        CardCmd.Enchant<PerfectFit>(selected, 1m);
        // One native upgrade preview shows the finished card with its address.
        CardCmd.Upgrade(selected, CardPreviewStyle.EventLayout);
        Finish("ADDRESS");
    }

    private async Task WalkAnotherLap()
    {
        AssertMutable();
        if (IsFinished || _completedLaps >= MaximumLaps) return;
        decimal damage = DynamicVars["RoadHpLoss"].BaseValue;
        decimal gold = DynamicVars.Gold.BaseValue;
        await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), EventOwner.Creature,
            damage, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        if (EventOwner.Creature.IsDead) { Finish("DEATH"); return; }
        await PlayerCmd.GainGold(gold, EventOwner);
        if (EventOwner.Creature.IsDead) { Finish("DEATH"); return; }
        _completedLaps++;
        if (_completedLaps == MaximumLaps) { Finish("ROAD_END"); return; }

        DynamicVars["RoadHpLoss"].BaseValue = damage * 2;
        DynamicVars.Gold.BaseValue = gold * 2;
        DynamicVars["NextLap"].BaseValue = _completedLaps + 1;
        SetEventState(L10NLookup($"THINGS_REALITY_ALIGNED_HOUSES.pages.LAP_{_completedLaps}.description"),
        [
            new EventOption(this, WalkAnotherLap, "THINGS_REALITY_ALIGNED_HOUSES.pages.ROAD.options.CONTINUE", RoadTips())
                .ThatDoesDamage(DynamicVars["RoadHpLoss"].BaseValue).ThatHasDynamicTitle(),
            new EventOption(this, LeaveRoad, "THINGS_REALITY_ALIGNED_HOUSES.pages.ROAD.options.STOP"),
        ]);
    }

    private Task LeaveRoad() { Finish("ROAD_STOP"); return Task.CompletedTask; }
    private Task FollowBlueHouse()
    {
        if (IsFinished) return Task.CompletedTask;
        Finish("BLUE");
        return Task.CompletedTask;
    }
    private void FinishWithoutSelection() => Finish(EventOwner.Creature.IsDead ? "DEATH" : "ABORTED");
    private void Finish(string page)
    {
        if (!IsFinished) SetEventFinished(L10NLookup($"THINGS_REALITY_ALIGNED_HOUSES.pages.{page}.description"));
    }
}
