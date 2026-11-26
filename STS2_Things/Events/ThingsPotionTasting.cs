using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>Personal random potion rewards, with a choice of payment for barter.</summary>
public sealed class ThingsPotionTasting : ThingsEventModel
{
    public const int SampleDamage = 7;
    public const int PairCost = 75;
    public const int BarterMaxHpLoss = 4;
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
        [new DamageVar("SampleDamage", SampleDamage, ValueProp.Unblockable | ValueProp.Unpowered),
         new GoldVar(PairCost),
         new MaxHpVar("BarterMaxHpLoss", BarterMaxHpLoss)];
    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(p =>
            !p.Creature.IsDead
            && (p.Creature.CurrentHp > SampleDamage
                || p.Gold >= PairCost
                || (p.Potions.Any() && p.Creature.MaxHp > BarterMaxHpLoss)));
    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SAMPLE", "BUY", "BARTER", "LEAVE");

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
        bool hasPotions = EventOwner.Potions.Any();
        bool hasBarterMaxHp = EventOwner.Creature.MaxHp > BarterMaxHpLoss;
        bool canBarter = canPair && hasPotions && hasBarterMaxHp;
        EventOption sampleOption = receivable > 0
            ? new EventOption(this, () => Award(1, false, null), InitialOptionKey("SAMPLE"))
                .ThatDoesDamage(SampleDamage)
            : new EventOption(this, null, InitialOptionKey("SAMPLE_LOCKED"));
        EventOption barterOption = new(
            this,
            canBarter ? () => ShowPayment(0) : null,
            InitialOptionKey(!canPair ? "CANNOT_RECEIVE" : !hasBarterMaxHp ? "BARTER_LOW_HP" : !hasPotions ? "BARTER_LOCKED" : "BARTER"));
        if (canBarter)
        {
            barterOption.ThatDecreasesMaxHp(BarterMaxHpLoss);
            barterOption.ThatWontSaveToChoiceHistory();
        }

        return
        [
            sampleOption,
            new EventOption(this, canBuy ? () => Award(2, true, null) : null,
                InitialOptionKey(!canPair ? "CANNOT_RECEIVE" : canBuy ? "BUY" : "BUY_LOCKED")),
            barterOption,
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private Task ShowPayment(int page)
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        var potions = EventOwner.Potions.ToArray();
        if (potions.Length == 0 || ReceivablePotions.Length < 2 || EventOwner.Creature.MaxHp <= BarterMaxHpLoss) return Back();
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
                .ThatDecreasesMaxHp(BarterMaxHpLoss)
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
        if (!paid && payment == null)
        {
            await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, SampleDamage);
            if (EventOwner.Creature.IsDead)
            {
                if (_permissionCaptured) PlayerCanModifyPotions = _previousPotionPermission;
                SetEventFinished(L10NLookup("THINGS_POTION_TASTING.pages.SAMPLE.loss"));
                return;
            }
        }
        if (payment != null)
        {
            await PotionCmd.Discard(payment);
            await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), BarterMaxHpLoss);
            if (EventOwner.Creature.IsDead)
            {
                if (_permissionCaptured) PlayerCanModifyPotions = _previousPotionPermission;
                SetEventFinished(L10NLookup("THINGS_POTION_TASTING.pages.BARTERED.loss"));
                return;
            }
        }
        if (paid)
        {
            await PlayerCmd.LoseGold(PairCost, EventOwner, GoldLossType.Spent);
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

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        SetEventFinished(L10NLookup("THINGS_POTION_TASTING.pages.LEAVE.description"));
        return Task.CompletedTask;
    }
}
