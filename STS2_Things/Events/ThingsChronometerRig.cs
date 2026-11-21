using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Glory (Act 3) event offering Imbued (Innate bottom-auto-play Skill) for Gold,
/// Perfect Fit (top of shuffled draw pile) for Max HP, or a free Potion (with Gold fallback).
/// </summary>
public sealed class ThingsChronometerRig : ThingsEventModel
{
    public const int GearGoldCost = 75;
    public const int PinMaxHpLoss = 8;
    public const int OilFallbackGold = 35;

    private PotionModel? _oilPotion;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _oilPotion = null;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(GearGoldCost),
        new MaxHpVar(PinMaxHpLoss),
        new GoldVar("FallbackGold", OilFallbackGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && ((player.Gold >= GearGoldCost && player.Deck.Cards.Any(CanEnchant<Imbued>))
                || (player.Creature.MaxHp > PinMaxHpLoss && player.Deck.Cards.Any(CanEnchant<PerfectFit>))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "GEAR", "PIN", "OIL_POTION");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<Imbued>().IconPath, ModelDb.Enchantment<PerfectFit>().IconPath])
            .Distinct();

    private static bool CanEnchant<TEnchantment>(CardModel? card) where TEnchantment : EnchantmentModel =>
        card != null && ModelDb.Enchantment<TEnchantment>().CanEnchant(card);

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (!isPreFinished && EventOwner != null)
            _oilPotion ??= PotionFactory.CreateRandomPotionOutOfCombat(EventOwner, Rng);
        return Task.CompletedTask;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool hasGearCard = EventOwner.Deck.Cards.Any(CanEnchant<Imbued>);
        bool hasGold = EventOwner.Gold >= GearGoldCost;
        EventOption gearOption = (!hasGearCard, !hasGold) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("GEAR_NO_CARD"),
                HoverTipFactory.FromEnchantment<Imbued>()),
            (false, true) => new EventOption(
                this,
                null,
                InitialOptionKey("GEAR_NO_GOLD"),
                HoverTipFactory.FromEnchantment<Imbued>()),
            _ => new EventOption(
                this,
                ApplyGear,
                InitialOptionKey("GEAR"),
                HoverTipFactory.FromEnchantment<Imbued>()),
        };

        bool lowMaxHp = EventOwner.Creature.MaxHp <= PinMaxHpLoss;
        bool hasPinCard = EventOwner.Deck.Cards.Any(CanEnchant<PerfectFit>);
        EventOption pinOption = (lowMaxHp, hasPinCard) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("PIN_LOW_HP"),
                HoverTipFactory.FromEnchantment<PerfectFit>()),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("PIN_NO_CARD"),
                HoverTipFactory.FromEnchantment<PerfectFit>()),
            _ => new EventOption(
                this,
                ApplyPin,
                InitialOptionKey("PIN"),
                HoverTipFactory.FromEnchantment<PerfectFit>())
                .ThatDecreasesMaxHp(PinMaxHpLoss),
        };

        bool canTakePotion = ThingsEventHelpers.CanReceivePotion(EventOwner, _oilPotion);
        EventOption oilOption = canTakePotion
            ? new EventOption(this, TakeOilPotion, InitialOptionKey("OIL_POTION"))
            : new EventOption(this, TakeOilFallbackGold, InitialOptionKey("OIL_GOLD"));

        return [gearOption, pinOption, oilOption];
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

        CardModel? selected = await ThingsEventHelpers.SelectCardForEnchantmentAsync<TEnchantment>(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.{promptKey}"), 1) { Cancelable = true, RequireManualConfirmation = true },
            CanEnchant<TEnchantment>);

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

    private Task ApplyGear() => ApplyEnchantmentOption<Imbued>(
        "gearPrompt",
        "GEAR",
        static player => player.Gold >= GearGoldCost,
        static player => PlayerCmd.LoseGold(GearGoldCost, player, GoldLossType.Spent));

    private Task ApplyPin() => ApplyEnchantmentOption<PerfectFit>(
        "pinPrompt",
        "PIN",
        static player => player.Creature.MaxHp > PinMaxHpLoss,
        player => LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), PinMaxHpLoss));

    private async Task TakeOilPotion()
    {
        if (!BeginSettlement())
            return;
        if (ThingsEventHelpers.CanReceivePotion(EventOwner, _oilPotion))
        {
            await PotionCmd.TryToProcure(_oilPotion!.ToMutable(), EventOwner);
            Finish("OIL_POTION.description");
            return;
        }

        await PlayerCmd.GainGold(OilFallbackGold, EventOwner);
        Finish("OIL_GOLD.description");
    }

    private async Task TakeOilFallbackGold()
    {
        if (!BeginSettlement())
            return;
        await PlayerCmd.GainGold(OilFallbackGold, EventOwner);
        Finish("OIL_GOLD.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
