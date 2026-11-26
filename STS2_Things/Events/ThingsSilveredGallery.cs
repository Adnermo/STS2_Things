using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Glory (Act 3) event offering full-fidelity duplication of a Rare card (or any card fallback)
/// for 16 HP, trading 95 Gold + 1 Upgraded card for a Rare Relic, or leaving.
/// </summary>
public sealed class ThingsSilveredGallery : ThingsEventModel
{
    public const int DuplicateDamage = 16;
    public const int PawnGoldCost = 95;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("TwinDamage", DuplicateDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar(PawnGoldCost),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && ((player.Creature.CurrentHp > DuplicateDamage && player.Deck.Cards.Any(IsCloneable))
                || (player.Gold >= PawnGoldCost
                    && player.Deck.Cards.Any(IsPawnCandidate)
                    && (!player.RelicGrabBag.IsPopulated
                        || ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare)))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "DUPLICATE_RARE", "PAWN", "LEAVE");

    private static bool IsCloneable(CardModel card) => card.Type != CardType.Quest;

    private static List<CardModel> GetDuplicateCandidates(Player player, out bool rareOnly)
    {
        List<CardModel> rare = player.Deck.Cards
            .Where(card => IsCloneable(card) && card.Rarity == CardRarity.Rare)
            .ToList();
        if (rare.Count > 0)
        {
            rareOnly = true;
            return rare;
        }

        rareOnly = false;
        return player.Deck.Cards.Where(IsCloneable).ToList();
    }

    private static bool IsPawnCandidate(CardModel? card) =>
        card != null && card.IsUpgraded && card.IsRemovable;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        List<CardModel> dupCandidates = GetDuplicateCandidates(EventOwner, out bool rareOnly);
        EventOption dupOption = dupCandidates.Count == 0
            ? new EventOption(this, null, InitialOptionKey("DUPLICATE_LOCKED"))
            : new EventOption(
                this,
                DuplicateCard,
                InitialOptionKey(rareOnly ? "DUPLICATE_RARE" : "DUPLICATE_ANY"))
                .ThatDoesDamage(DuplicateDamage);

        bool hasRelic = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Rare);
        bool hasUpgraded = EventOwner.Deck.Cards.Any(IsPawnCandidate);
        bool hasGold = EventOwner.Gold >= PawnGoldCost;
        EventOption pawnOption = (!hasRelic, !hasUpgraded, !hasGold) switch
        {
            (true, _, _) => new EventOption(this, null, InitialOptionKey("PAWN_NO_RELIC")),
            (false, true, _) => new EventOption(this, null, InitialOptionKey("PAWN_NO_CARD")),
            (false, false, true) => new EventOption(this, null, InitialOptionKey("PAWN_NO_GOLD")),
            _ => new EventOption(this, PawnForRareRelic, InitialOptionKey("PAWN")),
        };

        return
        [
            dupOption,
            pawnOption,
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private void CancelToInitial()
    {
        _settling = false;
        if (EventOwner.Creature.IsDead)
            Finish("ABORTED.description");
        else
            SetEventState(InitialDescription, GenerateInitialOptions());
    }

    private async Task DuplicateCard()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        HashSet<CardModel> candidates = GetDuplicateCandidates(player, out bool rareOnly).ToHashSet();
        if (candidates.Count == 0)
        {
            CancelToInitial();
            return;
        }

        string promptKey = rareOnly ? "duplicateRarePrompt" : "duplicateAnyPrompt";
        CardModel? selected = (await CardSelectCmd.FromDeckGeneric(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.{promptKey}"), 1) { Cancelable = true, RequireManualConfirmation = true },
            candidates.Contains)).FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !player.Deck.Cards.Contains(selected) || !IsCloneable(selected))
        {
            CancelToInitial();
            return;
        }

        await ThingsEventHelpers.DealUnblockableDamageAsync(player, DuplicateDamage);
        if (player.Creature.IsDead)
        {
            Finish("DUPLICATE.loss");
            return;
        }

        CardModel copy = player.RunState.CloneCard(selected);
        var added = await CardPileCmd.Add(copy, PileType.Deck);
        CardCmd.PreviewCardPileAdd(added, 1.4f, CardPreviewStyle.EventLayout);
        Finish("DUPLICATE.description");
    }

    private async Task PawnForRareRelic()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (player.Gold < PawnGoldCost ||
            !ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare) ||
            !player.Deck.Cards.Any(IsPawnCandidate))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForRemoval(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.pawnPrompt"), 1) { Cancelable = true, RequireManualConfirmation = true },
            IsPawnCandidate)).FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !player.Deck.Cards.Contains(selected) || !IsPawnCandidate(selected))
        {
            CancelToInitial();
            return;
        }

        await PlayerCmd.LoseGold(PawnGoldCost, player, GoldLossType.Spent);
        await CardPileCmd.RemoveFromDeck([selected]);
        await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Rare);
        Finish("PAWN.description");
    }

    private Task Leave()
    {
        if (!BeginSettlement())
            return Task.CompletedTask;
        Finish("LEAVE.description");
        return Task.CompletedTask;
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
