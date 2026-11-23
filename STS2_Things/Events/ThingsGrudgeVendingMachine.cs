using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths event featuring a spiteful vending cabinet: buy 1 of 3 shown potions,
/// escalate up to 3 kicks (Gold for 6 HP -> Common Relic for 12 HP -> Rare Relic + Shame for 18 HP),
/// or leave.
/// </summary>
public sealed class ThingsGrudgeVendingMachine : ThingsEventModel
{
    public const int CoinCost = 55;
    public const int Kick1Damage = 6;
    public const int Kick1Gold = 35;
    public const int Kick2Damage = 12;
    public const int Kick3Damage = 18;

    private PotionModel[]? _vendingPotions;
    private bool _previousPotionPermission;
    private bool _permissionCaptured;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _vendingPotions = null;
        _previousPotionPermission = false;
        _permissionCaptured = false;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(CoinCost),
        new DamageVar("Kick1Damage", Kick1Damage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar("Kick1Gold", Kick1Gold),
        new DamageVar("Kick2Damage", Kick2Damage, ValueProp.Unblockable | ValueProp.Unpowered),
        new DamageVar("Kick3Damage", Kick3Damage, ValueProp.Unblockable | ValueProp.Unpowered),
        new LocalizedStringVar("Curse", static _ => new LocString("cards", "SHAME.title").GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && (player.Gold >= CoinCost
                || player.Creature.CurrentHp > Kick1Damage));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "INSERT_COIN", "KICK_ONCE", "LEAVE");

    private bool CanReceive(PotionModel potion) =>
        EventOwner.HasOpenPotionSlots
        && Hook.ShouldProcurePotion(EventOwner.RunState, EventOwner.Creature.CombatState, potion.ToMutable(), EventOwner);

    private bool HasRelic(RelicRarity rarity) => ModelDb.AllRelics.Any(relic =>
        relic.Rarity == rarity
        && relic.IsAllowed(EventOwner.RunState)
        && EventOwner.RelicGrabBag.Contains(relic)
        && EventOwner.Relics.All(owned => owned.Id != relic.Id));

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (isPreFinished)
            return Task.CompletedTask;

        var modOnly = GetPlayerPotionOptions()
            .Where(potion => potion.GetType().Assembly != typeof(PotionModel).Assembly);
        _vendingPotions ??= PotionFactory.CreateRandomPotionsOutOfCombat(EventOwner, 3, Rng, modOnly)
            .Where(potion => potion != null)
            .ToArray();

        _previousPotionPermission = PlayerCanModifyPotions;
        _permissionCaptured = true;
        PlayerCanModifyPotions = false;
        return Task.CompletedTask;
    }

    protected override void OnEventFinished()
    {
        if (_permissionCaptured)
            PlayerCanModifyPotions = _previousPotionPermission;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        PotionModel[] receivable = (_vendingPotions ?? []).Where(CanReceive).ToArray();
        bool canOpenSlots = EventOwner.HasOpenPotionSlots && receivable.Length > 0;
        bool hasGold = EventOwner.Gold >= CoinCost;

        EventOption coinOption = (!canOpenSlots, !hasGold) switch
        {
            (true, _) => new EventOption(this, null, InitialOptionKey("COIN_FULL")),
            (false, true) => new EventOption(this, null, InitialOptionKey("COIN_NO_GOLD")),
            _ => new EventOption(this, ShowPotionTray, InitialOptionKey("INSERT_COIN"))
                .ThatWontSaveToChoiceHistory(),
        };

        return
        [
            coinOption,
            new EventOption(this, KickOnce, InitialOptionKey("KICK_ONCE"))
                .ThatDoesDamage(Kick1Damage),
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

    private Task ShowPotionTray()
    {
        if (IsFinished || _settling)
            return Task.CompletedTask;
        PotionModel[] optionsPotions = (_vendingPotions ?? []).Where(CanReceive).ToArray();
        if (optionsPotions.Length == 0 || EventOwner.Gold < CoinCost)
            return BackToInitial();

        List<EventOption> options = [];
        foreach (PotionModel potion in optionsPotions)
        {
            const string key = "THINGS_GRUDGE_VENDING_MACHINE.pages.TRAY.options.BUY_POTION";
            LocString title = L10NLookup(key + ".title");
            title.Add("Potion", potion.Title.GetFormattedText());
            LocString desc = L10NLookup(key + ".description");
            desc.Add("Potion", potion.Title.GetFormattedText());
            DynamicVars.AddTo(desc);
            options.Add(new EventOption(
                this,
                () => BuyChosenPotion(potion),
                title,
                desc,
                key,
                [HoverTipFactory.FromPotion(potion)])
                .ThatHasDynamicTitle());
        }
        options.Add(new EventOption(this, BackToInitial, "THINGS_GRUDGE_VENDING_MACHINE.pages.TRAY.options.BACK")
            .ThatWontSaveToChoiceHistory());
        SetEventState(L10NLookup("THINGS_GRUDGE_VENDING_MACHINE.pages.TRAY.description"), options);
        return Task.CompletedTask;
    }

    private Task BackToInitial()
    {
        if (!IsFinished && !_settling)
            SetEventState(InitialDescription, GenerateInitialOptions());
        return Task.CompletedTask;
    }

    private async Task BuyChosenPotion(PotionModel canonical)
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return;
        if (EventOwner.Gold < CoinCost || !CanReceive(canonical))
        {
            await BackToInitial();
            return;
        }

        _settling = true;
        await PlayerCmd.LoseGold(CoinCost, EventOwner, GoldLossType.Spent);
        if (_permissionCaptured)
            PlayerCanModifyPotions = _previousPotionPermission;
        await PotionCmd.TryToProcure(canonical.ToMutable(), EventOwner);
        Finish("BOUGHT.description");
    }

    private async Task KickOnce()
    {
        if (!BeginSettlement())
            return;
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, Kick1Damage);
        if (EventOwner.Creature.IsDead)
        {
            Finish("KICK_1.loss");
            return;
        }

        await PlayerCmd.GainGold(Kick1Gold, EventOwner);
        _settling = false;

        EventOption kickAgain = HasRelic(RelicRarity.Common)
            ? new EventOption(
                this,
                KickTwice,
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_1.options.KICK_TWICE")
                .ThatDoesDamage(Kick2Damage)
            : new EventOption(
                this,
                null,
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_1.options.EMPTY");

        SetEventState(L10NLookup("THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_1.description"),
        [
            kickAgain,
            new EventOption(
                this,
                () => LeaveFromKick("KICK_1_LEAVE.description"),
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_1.options.STOP"),
        ]);
    }

    private async Task KickTwice()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return;
        _settling = true;
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            EventOwner.Creature,
            Kick2Damage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (EventOwner.Creature.IsDead)
        {
            Finish("KICK_2.loss");
            return;
        }

        if (HasRelic(RelicRarity.Common))
            await ObtainRelic(RelicRarity.Common);
        _settling = false;

        EventOption kickFinal = HasRelic(RelicRarity.Rare)
            ? new EventOption(
                this,
                KickThrice,
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_2.options.KICK_THRICE",
                HoverTipFactory.FromCardWithCardHoverTips<Shame>())
                .ThatDoesDamage(Kick3Damage)
            : new EventOption(
                this,
                null,
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_2.options.EMPTY");

        SetEventState(L10NLookup("THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_2.description"),
        [
            kickFinal,
            new EventOption(
                this,
                () => LeaveFromKick("KICK_2_LEAVE.description"),
                "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_2.options.STOP"),
        ]);
    }

    private async Task KickThrice()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return;
        _settling = true;
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            EventOwner.Creature,
            Kick3Damage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (EventOwner.Creature.IsDead)
        {
            Finish("KICK_3.loss");
            return;
        }

        if (HasRelic(RelicRarity.Rare))
            await ObtainRelic(RelicRarity.Rare);
        await CardPileCmd.AddCurseToDeck<Shame>(EventOwner);
        Finish("KICK_3.description");
    }

    private Task LeaveFromKick(string pageSuffix)
    {
        if (!IsFinished && !_settling)
        {
            _settling = true;
            Finish(pageSuffix);
        }
        return Task.CompletedTask;
    }

    private Task Leave()
    {
        if (!BeginSettlement())
            return Task.CompletedTask;
        Finish("LEAVE.description");
        return Task.CompletedTask;
    }

    private async Task ObtainRelic(RelicRarity rarity)
    {
        RelicModel relic = RelicFactory.PullNextRelicFromFront(
            EventOwner,
            rarity,
            candidate => EventOwner.Relics.All(owned => owned.Id != candidate.Id));
        await RelicCmd.Obtain(relic.ToMutable(), EventOwner);
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
