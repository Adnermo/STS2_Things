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
using STS2_Things.Core.Models;
using STS2_Things.Enchantments;

namespace STS2_Things.Events;

/// <summary>
/// Depths event balancing deck weight vs speed: apply Weighted (+1 cost, 2x damage/block),
/// Slumbering Essence (cost -1 per turn retained in hand), or upgrade a basic/common card.
/// </summary>
public sealed class ThingsCrookedCounterweight : ThingsEventModel
{
    public const int WeightDamage = 8;
    public const int WatchMaxHpLoss = 5;
    public const int ShimFallbackHeal = 8;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("WeightDamage", WeightDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new MaxHpVar(WatchMaxHpLoss),
        new HealVar("FallbackHeal", ShimFallbackHeal),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && ((player.Creature.CurrentHp > WeightDamage && player.Deck.Cards.Any(CanEnchant<ThingsWeighted>))
                || (player.Creature.MaxHp > WatchMaxHpLoss && player.Deck.Cards.Any(CanEnchant<SlumberingEssence>))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "WEIGHT", "WATCH", "SHIM_UPGRADE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<ThingsWeighted>().IconPath, ModelDb.Enchantment<SlumberingEssence>().IconPath])
            .Distinct();

    private static bool CanEnchant<TEnchantment>(CardModel? card) where TEnchantment : EnchantmentModel =>
        card != null && ModelDb.Enchantment<TEnchantment>().CanEnchant(card);

    private static List<CardModel> GetShimCandidates(Player player) =>
        player.Deck.Cards
            .Where(card => card.IsUpgradable && card.Rarity is CardRarity.Basic or CardRarity.Common)
            .ToList();

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool canWeight = EventOwner.Deck.Cards.Any(CanEnchant<ThingsWeighted>);
        EventOption weightOption = canWeight
            ? new EventOption(
                this,
                ApplyWeight,
                InitialOptionKey("WEIGHT"),
                HoverTipFactory.FromEnchantment<ThingsWeighted>())
                .ThatDoesDamage(WeightDamage)
            : new EventOption(
                this,
                null,
                InitialOptionKey("WEIGHT_NO_CARD"),
                HoverTipFactory.FromEnchantment<ThingsWeighted>());

        bool lowMaxHp = EventOwner.Creature.MaxHp <= WatchMaxHpLoss;
        bool canWatch = EventOwner.Deck.Cards.Any(CanEnchant<SlumberingEssence>);
        EventOption watchOption = (lowMaxHp, canWatch) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("WATCH_LOW_HP"),
                HoverTipFactory.FromEnchantment<SlumberingEssence>()),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("WATCH_NO_CARD"),
                HoverTipFactory.FromEnchantment<SlumberingEssence>()),
            _ => new EventOption(
                this,
                ApplyWatch,
                InitialOptionKey("WATCH"),
                HoverTipFactory.FromEnchantment<SlumberingEssence>())
                .ThatDecreasesMaxHp(WatchMaxHpLoss),
        };

        bool hasShimCard = GetShimCandidates(EventOwner).Count > 0;
        EventOption shimOption = hasShimCard
            ? new EventOption(this, ShimUpgrade, InitialOptionKey("SHIM_UPGRADE"))
            : new EventOption(this, ShimHeal, InitialOptionKey("SHIM_HEAL"));

        return [weightOption, watchOption, shimOption];
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

    private async Task ApplyEnchantmentOption<TEnchantment>(
        string promptKey,
        string pagePrefix,
        Func<Player, bool> precheck,
        Func<Player, Task> payCost) where TEnchantment : EnchantmentModel
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (!precheck(player) || !player.Deck.Cards.Any(CanEnchant<TEnchantment>))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForEnchantment(
            player,
            ModelDb.Enchantment<TEnchantment>(),
            1,
            CanEnchant<TEnchantment>,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.{promptKey}"), 0, 1) { Cancelable = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !precheck(player) ||
            !player.Deck.Cards.Contains(selected) || !CanEnchant<TEnchantment>(selected))
        {
            CancelToInitial();
            return;
        }

        await payCost(player);
        if (player.Creature.IsDead)
        {
            Finish($"{pagePrefix}.loss");
            return;
        }

        CardCmd.Enchant<TEnchantment>(selected, 1m);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish($"{pagePrefix}.description");
    }

    private Task ApplyWeight() => ApplyEnchantmentOption<ThingsWeighted>(
        "weightPrompt",
        "WEIGHT",
        static _ => true,
        static player => CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            player.Creature,
            WeightDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null));

    private Task ApplyWatch() => ApplyEnchantmentOption<SlumberingEssence>(
        "watchPrompt",
        "WATCH",
        static player => player.Creature.MaxHp > WatchMaxHpLoss,
        player => LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), WatchMaxHpLoss));

    private async Task ShimUpgrade()
    {
        if (!BeginSettlement())
            return;
        List<CardModel> candidates = GetShimCandidates(EventOwner);
        if (candidates.Count == 0 || Rng.NextItem(candidates) is not { } picked)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        CardCmd.Upgrade(picked, CardPreviewStyle.EventLayout);
        Finish("SHIM_UPGRADE.description");
        await Task.CompletedTask;
    }

    private async Task ShimHeal()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Heal(EventOwner.Creature, ShimFallbackHeal);
        Finish("SHIM_HEAL.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
