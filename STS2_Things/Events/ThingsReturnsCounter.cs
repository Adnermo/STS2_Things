using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsReturnsCounter : ThingsEventModel
{
    public const int ReceiptMaxHpLoss = 6;
    public const int ViewfinderDamage = 10;
    public const int RummageCost = 40;
    public const int RummageChoiceCount = 3;

    private static readonly (Func<EnchantmentModel> Model, decimal Amount)[] PrimaryEnchantmentPool =
    [
        (static () => ModelDb.Enchantment<Sharp>(), 2m),
        (static () => ModelDb.Enchantment<Nimble>(), 3m),
        (static () => ModelDb.Enchantment<Adroit>(), 3m),
        (static () => ModelDb.Enchantment<Steady>(), 1m),
        (static () => ModelDb.Enchantment<Swift>(), 2m),
        (static () => ModelDb.Enchantment<Vigorous>(), 5m),
    ];

    private static readonly (Func<EnchantmentModel> Model, decimal Amount)[] FallbackEnchantmentPool =
    [
        (static () => ModelDb.Enchantment<Glam>(), 1m),
        (static () => ModelDb.Enchantment<Momentum>(), 2m),
    ];

    private CardModel[]? _candidates;
    private bool _settling;

    private CardModel[] Candidates => _candidates ?? [];

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _candidates = null;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new MaxHpVar(ReceiptMaxHpLoss),
        new DamageVar(ViewfinderDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar(RummageCost),
        new CardsVar(RummageChoiceCount),
        new IntVar("ReceiptGold", ThingsReturnReceipt.RewardGold),
        new HealVar("ReceiptHeal", ThingsReturnReceipt.RewardHeal),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && ((player.GetRelic<ThingsReturnReceipt>() == null
                    && player.Creature.MaxHp > ReceiptMaxHpLoss)
                || (player.GetRelic<ThingsNarrowViewfinder>() == null
                    && player.Creature.CurrentHp > ViewfinderDamage)
                || player.Gold >= RummageCost));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "RECEIPT", "VIEWFINDER", "RUMMAGE", "LEAVE");

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (!isPreFinished && _candidates == null && EventOwner != null)
            _candidates = RollRummageCandidates();
        return Task.CompletedTask;
    }

    private CardModel[] RollRummageCandidates()
    {
        CardCreationOptions options = CardCreationOptions
            .ForNonCombatWithDefaultOdds([EventOwner.Character.CardPool])
            .WithFlags(CardCreationFlags.NoUpgradeRoll | CardCreationFlags.NoCardPoolModifications)
            .WithRngOverride(Rng);

        List<CardCreationResult> created = CardFactory.CreateForReward(EventOwner, RummageChoiceCount, options).ToList();
        List<CardModel> prepared = new(created.Count);
        foreach (CardCreationResult entry in created)
        {
            CardModel card = entry.Card;
            if (card.IsUpgradable)
                CardCmd.Upgrade(card);
            ApplyRandomValidEnchantment(card);
            prepared.Add(card);
        }
        return [.. prepared];
    }

    private void ApplyRandomValidEnchantment(CardModel card)
    {
        var eligible = PrimaryEnchantmentPool
            .Select(item => (Canonical: item.Model(), item.Amount))
            .Where(item => item.Canonical.CanEnchant(card))
            .ToList();
        if (eligible.Count == 0)
        {
            eligible = FallbackEnchantmentPool
                .Select(item => (Canonical: item.Model(), item.Amount))
                .Where(item => item.Canonical.CanEnchant(card))
                .ToList();
        }
        if (eligible.Count == 0)
            return;

        var chosen = eligible.StableShuffle(Rng)[0];
        CardCmd.Enchant(chosen.Canonical.ToMutable(), card, chosen.Amount);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool ownsReceipt = EventOwner.GetRelic<ThingsReturnReceipt>() != null;
        bool tooLowMaxHp = EventOwner.Creature.MaxHp <= ReceiptMaxHpLoss;
        EventOption receiptOption = (ownsReceipt, tooLowMaxHp) switch
        {
            (true, _) => new EventOption(this, null, InitialOptionKey("RECEIPT_OWNED"),
                HoverTipFactory.FromRelic<ThingsReturnReceipt>()),
            (false, true) => new EventOption(this, null, InitialOptionKey("RECEIPT_LOW_HP"),
                HoverTipFactory.FromRelic<ThingsReturnReceipt>()),
            _ => new EventOption(this, TakeReceipt, InitialOptionKey("RECEIPT"),
                HoverTipFactory.FromRelic<ThingsReturnReceipt>()).ThatDecreasesMaxHp(ReceiptMaxHpLoss),
        };

        bool ownsViewfinder = EventOwner.GetRelic<ThingsNarrowViewfinder>() != null;
        EventOption viewfinderOption = ownsViewfinder
            ? new EventOption(this, null, InitialOptionKey("VIEWFINDER_OWNED"),
                HoverTipFactory.FromRelic<ThingsNarrowViewfinder>())
            : new EventOption(this, TakeViewfinder, InitialOptionKey("VIEWFINDER"),
                HoverTipFactory.FromRelic<ThingsNarrowViewfinder>()).ThatDoesDamage(ViewfinderDamage);

        bool canRummage = EventOwner.Gold >= RummageCost && Candidates.Length > 0;
        EventOption rummageOption = canRummage
            ? new EventOption(this, Rummage, InitialOptionKey("RUMMAGE"), disableOnChosen: false)
            : new EventOption(this, null, InitialOptionKey("RUMMAGE_LOCKED"));

        EventOption leaveOption = new(this, Leave, InitialOptionKey("LEAVE"));

        return [receiptOption, viewfinderOption, rummageOption, leaveOption];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task TakeReceipt()
    {
        if (!BeginSettlement())
            return;
        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), ReceiptMaxHpLoss);
        if (!EventOwner.Creature.IsDead)
            await RelicCmd.Obtain(ModelDb.Relic<ThingsReturnReceipt>().ToMutable(), EventOwner);
        Finish(EventOwner.Creature.IsDead ? "RECEIPT.loss" : "RECEIPT.description");
    }

    private async Task TakeViewfinder()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            EventOwner.Creature,
            ViewfinderDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);
        if (!EventOwner.Creature.IsDead)
            await RelicCmd.Obtain(ModelDb.Relic<ThingsNarrowViewfinder>().ToMutable(), EventOwner);
        Finish(EventOwner.Creature.IsDead ? "VIEWFINDER.loss" : "VIEWFINDER.description");
    }

    private async Task Rummage()
    {
        if (!BeginSettlement())
            return;
        if (EventOwner.Gold < RummageCost || Candidates.Length == 0)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        CardModel? chosen = await CardSelectCmd.FromChooseACardScreen(
            new BlockingPlayerChoiceContext(),
            Candidates,
            EventOwner,
            canSkip: true);

        if (chosen == null || IsFinished || EventOwner.Gold < RummageCost)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await PlayerCmd.LoseGold(RummageCost, EventOwner, GoldLossType.Spent);
        CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(chosen, PileType.Deck));
        Finish("RUMMAGE.description");
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
            SetEventFinished(L10NLookup($"THINGS_RETURNS_COUNTER.pages.{pageSuffix}"));
    }
}
