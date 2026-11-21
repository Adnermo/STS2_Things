using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Hive (Act 2) high-risk greed event ("过饱育婴室 / Overstuffed Brood").
/// - Reach for the Core: gain 1 random Rare Relic + 2 Decay curses.
/// - Incubate a Card: choose 1 deck card to add 2 full-fidelity clones, lose 10 Max HP.
/// - Slash &amp; Bolt: flee immediately, but bursting acid deals 9 unblockable damage.
/// </summary>
public sealed class ThingsOverstuffedBrood : ThingsEventModel
{
    public const int CurseCount = 2;
    public const int CloneCopies = 2;
    public const int CloneMaxHpLoss = 10;
    public const int FleeDamage = 9;
    public const int RareRelicFallbackGold = 150;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new MaxHpVar(CloneMaxHpLoss),
        new DamageVar("FleeDamage", FleeDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new CardsVar("CloneCopies", CloneCopies),
        new CardsVar("CurseCount", CurseCount),
        new GoldVar("FallbackGold", RareRelicFallbackGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > FleeDamage
            && player.Creature.MaxHp > CloneMaxHpLoss);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "REACH_CORE", "CLONE_SAC", "SLASH_FLEE");

    private static bool IsCloneable(CardModel card) => card.Type != CardType.Quest;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool hasRareRelic = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Rare);
        EventOption reachOption = new EventOption(
            this,
            ReachCoreAsync,
            InitialOptionKey(hasRareRelic ? "REACH_CORE" : "REACH_CORE_GOLD"),
            HoverTipFactory.FromCard<Decay>());

        bool canClone = EventOwner.Creature.MaxHp > CloneMaxHpLoss
            && EventOwner.Deck.Cards.Any(IsCloneable);
        EventOption cloneOption = canClone
            ? new EventOption(this, CloneCardAsync, InitialOptionKey("CLONE_SAC"))
            : new EventOption(this, null, InitialOptionKey("CLONE_LOCKED"));

        EventOption fleeOption = new EventOption(
                this,
                SlashFleeAsync,
                InitialOptionKey("SLASH_FLEE"))
            .ThatDoesDamage(FleeDamage);

        return [reachOption, cloneOption, fleeOption];
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

    private async Task ReachCoreAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
            await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Rare);
        else
            await PlayerCmd.GainGold(RareRelicFallbackGold, player);

        await CardPileCmd.AddCursesToDeck(
            [ModelDb.Card<Decay>(), ModelDb.Card<Decay>()],
            player);

        Finish("REACH_CORE.description");
    }

    private async Task CloneCardAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        if (player.Creature.MaxHp <= CloneMaxHpLoss || !player.Deck.Cards.Any(IsCloneable))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckGeneric(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.clonePrompt"), 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            },
            IsCloneable)).FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !player.Deck.Cards.Contains(selected) || !IsCloneable(selected))
        {
            CancelToInitial();
            return;
        }

        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), CloneMaxHpLoss);
        if (player.Creature.IsDead)
        {
            Finish("CLONE.loss");
            return;
        }

        var addedResults = new List<CardPileAddResult>(CloneCopies);
        for (int i = 0; i < CloneCopies; i++)
        {
            CardModel copy = player.RunState.CloneCard(selected);
            addedResults.Add(await CardPileCmd.Add(copy, PileType.Deck));
        }

        CardCmd.PreviewCardPileAdd(addedResults, 1.4f, CardPreviewStyle.EventLayout);
        Finish("CLONE.description");
    }

    private async Task SlashFleeAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, FleeDamage);
        if (player.Creature.IsDead)
        {
            Finish("FLEE.loss");
            return;
        }

        Finish("FLEE.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
