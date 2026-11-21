using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>Personal random potion rewards, with a choice of payment for barter.</summary>
public sealed class ThingsPotionTasting : ThingsEventModel
{
    private const int PairCost = 40;
    private const int WarmWaterHeal = 10;
    private const int BuyHeal = 8;
    private const int BarterMaxHp = 3;
    private const int PageSize = 3;
    private PotionModel[]? _rolledPotions;
    private bool _previousPotionPermission;
    private bool _permissionCaptured;
    private bool _settling;
    private PotionModel[] ReceivablePotions => (_rolledPotions ?? Array.Empty<PotionModel>()).Where(CanReceive).ToArray();

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _rolledPotions = null;
        _previousPotionPermission = false;
        _permissionCaptured = false;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(PairCost), new HealVar(WarmWaterHeal), new IntVar("BuyHeal", BuyHeal), new IntVar("BarterMaxHp", BarterMaxHp)];
    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && (player.Gold >= PairCost || player.Potions.Any()));
    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SAMPLE", "BUY", "BARTER", "WATER");

    private bool CanReceive(PotionModel potion) => Hook.ShouldProcurePotion(EventOwner.RunState,
        EventOwner.Creature.CombatState, potion.ToMutable(), EventOwner);

    public override void OnRoomEnter()
    {
        base.OnRoomEnter();
        if (Node?.GetNodeOrNull<Control>("%EventDescription") is { } description)
            description.CustomMinimumSize = new Vector2(description.CustomMinimumSize.X, 144);
    }

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (isPreFinished) return Task.CompletedTask;
        // The native factory respects character pools, unlocks and rarity odds.
        var options = GetPlayerPotionOptions();
        var nonVanilla = options.Where(potion => potion.GetType().Assembly != typeof(PotionModel).Assembly);
        // Roll once and keep results hidden until the native reward screen.
        // Returning from the payment menu cannot reroll the reward.
        _rolledPotions ??= PotionFactory.CreateRandomPotionsOutOfCombat(EventOwner, 2, Rng, nonVanilla).ToArray();
        _previousPotionPermission = PlayerCanModifyPotions;
        _permissionCaptured = true;
        PlayerCanModifyPotions = false;
        return Task.CompletedTask;
    }

    protected override void OnEventFinished()
    {
        if (_permissionCaptured) PlayerCanModifyPotions = _previousPotionPermission;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        int receivable = ReceivablePotions.Length;
        bool canPair = receivable >= 2;
        bool canBuy = canPair && EventOwner.Gold >= PairCost;
        bool canBarter = canPair && EventOwner.Potions.Any();
        EventOption barterOption = new(
            this,
            canBarter ? () => ShowPayment(0) : null,
            InitialOptionKey(!canPair ? "CANNOT_RECEIVE" : canBarter ? "BARTER" : "BARTER_LOCKED"));
        if (canBarter)
            barterOption.ThatWontSaveToChoiceHistory();

        return
        [
            receivable > 0
                ? new EventOption(this, () => Award(1, false, null), InitialOptionKey("SAMPLE"))
                : new EventOption(this, DrinkWater, InitialOptionKey("WATER")),
            new EventOption(this, canBuy ? () => Award(2, true, null) : null,
                InitialOptionKey(!canPair ? "CANNOT_RECEIVE" : canBuy ? "BUY" : "BUY_LOCKED")),
            barterOption,
        ];
    }

    private Task ShowPayment(int page)
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        var potions = EventOwner.Potions.ToArray();
        if (potions.Length == 0 || ReceivablePotions.Length < 2) return Back();
        int pages = (potions.Length + PageSize - 1) / PageSize;
        page %= pages;
        var options = new List<EventOption>();
        foreach (var potion in potions.Skip(page * PageSize).Take(PageSize))
        {
            const string key = "THINGS_POTION_TASTING.pages.PAYMENT.options.GIVE";
            var title = L10NLookup(key + ".title");
            title.Add("Potion", potion.Title.GetFormattedText());
            var description = L10NLookup(key + ".description");
            description.Add("Potion", potion.Title.GetFormattedText());
            options.Add(new EventOption(this, () => Award(2, false, potion), title, description, key,
                new[] { HoverTipFactory.FromPotion(potion) })
                .ThatHasDynamicTitle());
        }
        if (pages > 1)
        {
            int nextPage = (page + 1) % pages;
            const string key = "THINGS_POTION_TASTING.pages.PAYMENT.options.MORE";
            var description = L10NLookup(key + ".description");
            description.Add("Page", page + 1);
            description.Add("Pages", pages);
            options.Add(new EventOption(this, () => ShowPayment(nextPage),
                L10NLookup(key + ".title"), description, key, Array.Empty<IHoverTip>())
                .ThatWontSaveToChoiceHistory());
        }
        options.Add(new EventOption(this, Back, "THINGS_POTION_TASTING.pages.PAYMENT.options.BACK")
            .ThatWontSaveToChoiceHistory());
        SetEventState(L10NLookup("THINGS_POTION_TASTING.pages.PAYMENT.description"), options);
        return Task.CompletedTask;
    }

    private async Task Award(int count, bool paid, PotionModel? payment)
    {
        if (IsFinished || _settling) return;
        var potions = ReceivablePotions.Take(count).ToArray();
        if (EventOwner.Creature.IsDead || (paid && EventOwner.Gold < PairCost) ||
            potions.Length != count || (payment != null && !EventOwner.Potions.Contains(payment)))
            { await Back(); return; }
        _settling = true;
        if (payment != null)
        {
            await PotionCmd.Discard(payment);
            await CreatureCmd.GainMaxHp(EventOwner.Creature, BarterMaxHp);
        }
        if (paid)
        {
            await PlayerCmd.LoseGold(PairCost, EventOwner, GoldLossType.Spent);
            await CreatureCmd.Heal(EventOwner.Creature, BuyHeal);
        }
        // Restore native potion controls before opening rewards, so full slots
        // can be freed normally. Never try to Obtain into an already full belt.
        if (_permissionCaptured) PlayerCanModifyPotions = _previousPotionPermission;
        await RewardsCmd.OfferCustom(EventOwner,
            potions.Select(potion => (Reward)new PotionReward(potion.ToMutable(), EventOwner)).ToList());
        SetEventFinished(L10NLookup($"THINGS_POTION_TASTING.pages.{(payment != null ? "BARTERED" : paid ? "BOUGHT" : "SAMPLED")}.description"));
    }

    private Task Back()
    {
        if (!IsFinished && !_settling) SetEventState(InitialDescription, GenerateInitialOptions());
        return Task.CompletedTask;
    }

    private async Task DrinkWater()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        await CreatureCmd.Heal(EventOwner.Creature, WarmWaterHeal);
        SetEventFinished(L10NLookup("THINGS_POTION_TASTING.pages.WATER.description"));
    }
}
