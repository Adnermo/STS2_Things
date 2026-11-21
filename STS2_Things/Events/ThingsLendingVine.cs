using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Overgrowth (Act 1A) malicious escalating-torment event ("借牌老藤 / The Lending Vine").
/// Grabs 1 random removable card from the player's deck:
/// - Give it up: remove that card and immediately randomly upgrade 1 other card.
/// - Yank it back (repeatable): take escalating unblockable damage (5 -> 8 -> 11 -> ...)
///   to keep that card, while the vine mocks you and grabs a different removable card.
/// - Escape: discard 1 random Potion; if the player owns no Potions, gain Clumsy instead.
/// </summary>
public sealed class ThingsLendingVine : ThingsEventModel
{
    public const int InitialHpLoss = 5;
    public const int HpLossStep = 3;

    private int _pullCount;
    private CardModel? _grabbedCard;
    private HashSet<CardModel>? _skippedCards;
    private bool _settling;

    public int CurrentHpLoss => InitialHpLoss + _pullCount * HpLossStep;

    public CardModel? GrabbedCard => _grabbedCard;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _pullCount = 0;
        _grabbedCard = null;
        _skippedCards = null;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new StringVar("RandomCard"),
        new DynamicVar("HpLoss", InitialHpLoss),
        new CardsVar(1),
    ];

    public const int MinFloor = 5;

    public override bool IsAllowed(IRunState runState) =>
        runState.TotalFloor >= MinFloor
        && runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > InitialHpLoss
            && player.Deck.Cards.Any(card => card.IsRemovable));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "GIVE", "PULL", "ESCAPE_POTION");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        DynamicVars["HpLoss"].BaseValue = CurrentHpLoss;
        PickNextGrabbedCard();
        return BuildStageOptions();
    }

    private IReadOnlyList<EventOption> BuildStageOptions()
    {
        var options = new List<EventOption>(3);

        if (_grabbedCard != null)
        {
            options.Add(new EventOption(
                    this,
                    GiveCardAsync,
                    InitialOptionKey("GIVE"),
                    HoverTipFactory.FromCard(_grabbedCard))
                .ThatHasDynamicTitle());
        }
        else
        {
            options.Add(new EventOption(this, null, InitialOptionKey("GIVE_LOCKED")));
        }

        if (_grabbedCard != null)
        {
            options.Add(new EventOption(
                    this,
                    PullBackAsync,
                    InitialOptionKey("PULL"))
                .ThatDoesDamage(CurrentHpLoss));
        }

        bool hasPotion = EventOwner.Potions.Any();
        if (hasPotion)
        {
            options.Add(new EventOption(
                this,
                EscapeAsync,
                InitialOptionKey("ESCAPE_POTION")));
        }
        else
        {
            options.Add(new EventOption(
                this,
                EscapeAsync,
                InitialOptionKey("ESCAPE_CURSE"),
                HoverTipFactory.FromCard<Clumsy>()));
        }

        return options;
    }

    private void PickNextGrabbedCard()
    {
        List<CardModel> candidates;
        if (_grabbedCard == null)
        {
            candidates = EventOwner.Deck.Cards
                .Where(card => card.IsRemovable)
                .ToList();
        }
        else
        {
            _skippedCards ??= [];
            _skippedCards.Add(_grabbedCard);
            Type previousType = _grabbedCard.GetType();
            candidates = EventOwner.Deck.Cards
                .Where(card => card.IsRemovable
                    && card.GetType() != previousType
                    && !_skippedCards.Contains(card))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = EventOwner.Deck.Cards
                    .Where(card => card.IsRemovable && !_skippedCards.Contains(card))
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                candidates = EventOwner.Deck.Cards
                    .Where(card => card.IsRemovable)
                    .ToList();
            }
        }

        _grabbedCard = candidates.Count > 0 ? Rng.NextItem(candidates) : null;
        ((StringVar)DynamicVars["RandomCard"]).StringValue = _grabbedCard?.Title ?? string.Empty;
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task GiveCardAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        CardModel? grabbed = _grabbedCard;
        if (grabbed == null || !player.Deck.Cards.Contains(grabbed) || !grabbed.IsRemovable)
        {
            _settling = false;
            PickNextGrabbedCard();
            SetEventState(InitialDescription, BuildStageOptions());
            return;
        }

        await CardPileCmd.RemoveFromDeck(grabbed);

        List<CardModel> upgradable = player.Deck.Cards
            .Where(card => card.IsUpgradable)
            .ToList();

        if (upgradable.Count > 0 && Rng.NextItem(upgradable) is { } picked)
        {
            CardCmd.Upgrade(picked, CardPreviewStyle.EventLayout);
            Finish("GIVE.description");
            return;
        }

        Finish("GIVE_NO_UPGRADE.description");
    }

    private async Task PullBackAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        int damage = CurrentHpLoss;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, damage);
        if (player.Creature.IsDead)
        {
            Finish("PULL.loss");
            return;
        }

        int stageIndex = _pullCount;
        _pullCount++;
        DynamicVars["HpLoss"].BaseValue = CurrentHpLoss;
        PickNextGrabbedCard();

        string pageSuffix = stageIndex switch
        {
            0 => "PULL_0",
            1 => "PULL_1",
            2 => "PULL_2",
            _ => "PULL_LOOP",
        };

        _settling = false;
        SetEventState(L10NLookup($"{Id.Entry}.pages.{pageSuffix}.description"), BuildStageOptions());
    }

    private async Task EscapeAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        List<PotionModel> potions = player.Potions.ToList();
        if (potions.Count > 0 && Rng.NextItem(potions) is { } doomedPotion)
        {
            await PotionCmd.Discard(doomedPotion);
            Finish("ESCAPE_POTION.description");
            return;
        }

        await CardPileCmd.AddCurseToDeck<Clumsy>(player);
        Finish("ESCAPE_CURSE.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
