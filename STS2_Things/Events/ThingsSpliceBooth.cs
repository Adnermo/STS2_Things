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
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Enchantments;

namespace STS2_Things.Events;

/// <summary>
/// Depths event offering an Overclocked (-1 cost, 3 HP loss on play) enchantment,
/// same-class same-rarity upgraded pair re-forging, or random card downgrade for gold.
/// </summary>
public sealed class ThingsSpliceBooth : ThingsEventModel
{
    public const int OverclockMaxHpLoss = 8;
    public const int RewireDamage = 8;
    public const int RewireCardCount = 2;
    public const int DowngradeGold = 85;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new MaxHpVar(OverclockMaxHpLoss),
        new DamageVar("RewireDamage", RewireDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new CardsVar("RewireCards", RewireCardCount),
        new GoldVar("SalvageGold", DowngradeGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && ((player.Creature.MaxHp > OverclockMaxHpLoss && player.Deck.Cards.Any(CanOverclock))
                || (player.Creature.CurrentHp > RewireDamage
                    && player.Deck.Cards.Count(card => CanRewire(player, card)) >= RewireCardCount)));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "OVERCLOCK", "REWIRE", "SALVAGE", "LEAVE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<ThingsOverclocked>().IconPath])
            .Distinct();

    private static bool CanOverclock(CardModel? card) =>
        card != null && ModelDb.Enchantment<ThingsOverclocked>().CanEnchant(card);

    private static List<CardModel> GetSameClassSameRarityPool(Player player, CardModel original) =>
        player.Character.CardPool
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(candidate => candidate.Rarity == original.Rarity
                                && candidate.Id != original.Id
                                && candidate.IsRemovable)
            .ToList();

    private static bool CanRewire(Player player, CardModel card) =>
        card.IsTransformable
        && card.Type != CardType.Curse
        && GetSameClassSameRarityPool(player, card).Count > 0;

    private static List<CardModel> GetUpgradedCards(Player player) =>
        player.Deck.Cards.Where(card => card.IsUpgraded).ToList();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool lowMaxHp = EventOwner.Creature.MaxHp <= OverclockMaxHpLoss;
        bool hasOverclockCard = EventOwner.Deck.Cards.Any(CanOverclock);
        EventOption overclockOption = (lowMaxHp, hasOverclockCard) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("OVERCLOCK_LOW_HP"),
                HoverTipFactory.FromEnchantment<ThingsOverclocked>()),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("OVERCLOCK_NO_CARD"),
                HoverTipFactory.FromEnchantment<ThingsOverclocked>()),
            _ => new EventOption(
                this,
                ApplyOverclock,
                InitialOptionKey("OVERCLOCK"),
                HoverTipFactory.FromEnchantment<ThingsOverclocked>())
                .ThatDecreasesMaxHp(OverclockMaxHpLoss),
        };

        bool canRewire = EventOwner.Deck.Cards.Count(card => CanRewire(EventOwner, card)) >= RewireCardCount;
        EventOption rewireOption = canRewire
            ? new EventOption(this, RewirePair, InitialOptionKey("REWIRE")).ThatDoesDamage(RewireDamage)
            : new EventOption(this, null, InitialOptionKey("REWIRE_NO_CARDS"));

        bool hasUpgraded = GetUpgradedCards(EventOwner).Count > 0;
        EventOption thirdOption = hasUpgraded
            ? new EventOption(this, SalvageCopper, InitialOptionKey("SALVAGE"))
            : new EventOption(this, Leave, InitialOptionKey("LEAVE"));

        return [overclockOption, rewireOption, thirdOption];
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

    private async Task ApplyOverclock()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (player.Creature.MaxHp <= OverclockMaxHpLoss || !player.Deck.Cards.Any(CanOverclock))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForEnchantment(
            player,
            ModelDb.Enchantment<ThingsOverclocked>(),
            1,
            CanOverclock,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.overclockPrompt"), 0, 1) { Cancelable = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead || player.Creature.MaxHp <= OverclockMaxHpLoss ||
            !player.Deck.Cards.Contains(selected) || !CanOverclock(selected))
        {
            CancelToInitial();
            return;
        }

        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), OverclockMaxHpLoss);
        if (player.Creature.IsDead)
        {
            Finish("OVERCLOCK.loss");
            return;
        }

        CardCmd.Enchant<ThingsOverclocked>(selected, 1m);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish("OVERCLOCK.description");
    }

    private async Task RewirePair()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (player.Deck.Cards.Count(card => CanRewire(player, card)) < RewireCardCount)
        {
            CancelToInitial();
            return;
        }

        List<CardModel> selected = (await CardSelectCmd.FromDeckGeneric(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.rewirePrompt"), RewireCardCount)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            },
            card => CanRewire(player, card))).ToList();

        if (selected.Count != RewireCardCount || player.Creature.IsDead ||
            selected.Any(card => !player.Deck.Cards.Contains(card) || !CanRewire(player, card)))
        {
            CancelToInitial();
            return;
        }

        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            player.Creature,
            RewireDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (player.Creature.IsDead)
        {
            Finish("REWIRE.loss");
            return;
        }

        List<CardTransformation> transformations = new(selected.Count);
        foreach (CardModel original in selected)
        {
            List<CardModel> pool = GetSameClassSameRarityPool(player, original);
            if (Rng.NextItem(pool) is not { } canonical)
                continue;
            CardModel replacement = (original.CardScope ?? player.RunState).CreateCard(canonical, player);
            if (replacement.IsUpgradable)
                CardCmd.Upgrade(replacement);
            transformations.Add(new CardTransformation(original, replacement));
        }

        await CardCmd.Transform(transformations, null, CardPreviewStyle.EventLayout);
        Finish("REWIRE.description");
    }

    private async Task SalvageCopper()
    {
        if (!BeginSettlement())
            return;
        List<CardModel> upgraded = GetUpgradedCards(EventOwner);
        if (upgraded.Count == 0 || Rng.NextItem(upgraded) is not { } target)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        CardCmd.Downgrade(target);
        CardCmd.Preview(target, 1.2f, CardPreviewStyle.EventLayout);
        await PlayerCmd.GainGold(DowngradeGold, EventOwner);
        Finish("SALVAGE.description");
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
