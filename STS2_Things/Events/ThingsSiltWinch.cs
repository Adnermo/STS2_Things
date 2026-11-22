using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths push-your-luck event featuring a hand-cranked silt winch over a flooded pit:
/// Haul 1 (-5 HP -> 3-choice Upgraded Common/Uncommon card reward),
/// Haul 2 (-10 HP -> random Potion or +25 Gold + 45 Gold),
/// Haul 3 (-10 Max HP + Decay -> Uncommon/Rare Relic),
/// or cut the loose pouch for a lossless 25 Gold.
/// </summary>
public sealed class ThingsSiltWinch : ThingsEventModel
{
    public const int Pull1Damage = 5;
    public const int Pull2Damage = 10;
    public const int Pull2Gold = 45;
    public const int Pull2PotionFallbackGold = 25;
    public const int Pull3MaxHpLoss = 10;
    public const int PouchGold = 25;

    private PotionModel? _mudPotion;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _mudPotion = null;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("Pull1Damage", Pull1Damage, ValueProp.Unblockable | ValueProp.Unpowered),
        new DamageVar("Pull2Damage", Pull2Damage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar("Pull2Gold", Pull2Gold),
        new GoldVar("Pull2TotalGold", Pull2Gold + Pull2PotionFallbackGold),
        new MaxHpVar("Pull3MaxHp", Pull3MaxHpLoss),
        new GoldVar("PouchGold", PouchGold),
        new LocalizedStringVar("Curse", static _ => new LocString("cards", "DECAY.title").GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > Pull1Damage);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "HAUL_1", "CUT_POUCH");

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        if (!isPreFinished && EventOwner != null)
            _mudPotion ??= PotionFactory.CreateRandomPotionOutOfCombat(EventOwner, Rng);
        return Task.CompletedTask;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, HaulDepth1, InitialOptionKey("HAUL_1"))
            .ThatDoesDamage(Pull1Damage),
        new EventOption(this, CutLoosePouch, InitialOptionKey("CUT_POUCH")),
    ];

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task HaulDepth1()
    {
        if (!BeginSettlement())
            return;

        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, Pull1Damage);
        if (EventOwner.Creature.IsDead)
        {
            Finish("HAUL_1.loss");
            return;
        }

        CardCreationOptions options = CardCreationOptions
            .ForNonCombatWithDefaultOdds(
                [EventOwner.Character.CardPool],
                static card => card.Rarity is CardRarity.Common or CardRarity.Uncommon)
            .WithFlags(CardCreationFlags.NoUpgradeRoll | CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications)
            .WithRngOverride(Rng);

        CardReward reward = new(options, 3, EventOwner);
        reward.AfterGenerated += () =>
        {
            foreach (CardModel card in reward.Cards)
            {
                if (card.IsUpgradable)
                    CardCmd.Upgrade(card);
            }
        };
        await RewardsCmd.OfferCustom(EventOwner, [reward]);

        _settling = false;
        bool canTakePotion = ThingsEventHelpers.CanReceivePotion(EventOwner, _mudPotion);
        string haul2Key = canTakePotion
            ? "THINGS_SILT_WINCH.pages.DEPTH_2.options.HAUL_2_POTION"
            : "THINGS_SILT_WINCH.pages.DEPTH_2.options.HAUL_2_GOLD";

        SetEventState(L10NLookup("THINGS_SILT_WINCH.pages.DEPTH_2.description"),
        [
            new EventOption(this, HaulDepth2, haul2Key).ThatDoesDamage(Pull2Damage),
            new EventOption(this, () => StopWinch("STOP_1.description"), "THINGS_SILT_WINCH.pages.DEPTH_2.options.STOP_1"),
        ]);
    }

    private async Task HaulDepth2()
    {
        if (!BeginSettlement())
            return;

        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, Pull2Damage);
        if (EventOwner.Creature.IsDead)
        {
            Finish("HAUL_2.loss");
            return;
        }

        await PlayerCmd.GainGold(Pull2Gold, EventOwner);
        if (ThingsEventHelpers.CanReceivePotion(EventOwner, _mudPotion))
            await PotionCmd.TryToProcure(_mudPotion!.ToMutable(), EventOwner);
        else
            await PlayerCmd.GainGold(Pull2PotionFallbackGold, EventOwner);

        _settling = false;
        ShowDepth3State();
    }

    private void ShowDepth3State()
    {
        bool lowMaxHp = EventOwner.Creature.MaxHp <= Pull3MaxHpLoss;
        bool hasUncommon = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Uncommon);
        bool hasRare = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Rare);

        EventOption haul3Option = (lowMaxHp, !hasUncommon && !hasRare) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                "THINGS_SILT_WINCH.pages.DEPTH_3.options.HAUL_3_LOW_HP",
                HoverTipFactory.FromCardWithCardHoverTips<Decay>()),
            (false, true) => new EventOption(
                this,
                null,
                "THINGS_SILT_WINCH.pages.DEPTH_3.options.HAUL_3_EMPTY",
                HoverTipFactory.FromCardWithCardHoverTips<Decay>()),
            _ => new EventOption(
                this,
                HaulDepth3,
                "THINGS_SILT_WINCH.pages.DEPTH_3.options.HAUL_3",
                HoverTipFactory.FromCardWithCardHoverTips<Decay>())
                .ThatDecreasesMaxHp(Pull3MaxHpLoss),
        };

        SetEventState(L10NLookup("THINGS_SILT_WINCH.pages.DEPTH_3.description"),
        [
            haul3Option,
            new EventOption(this, () => StopWinch("STOP_2.description"), "THINGS_SILT_WINCH.pages.DEPTH_3.options.STOP_2"),
        ]);
    }

    private async Task HaulDepth3()
    {
        if (!BeginSettlement())
            return;
        if (EventOwner.Creature.MaxHp <= Pull3MaxHpLoss)
        {
            _settling = false;
            ShowDepth3State();
            return;
        }

        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), Pull3MaxHpLoss);
        if (EventOwner.Creature.IsDead)
        {
            Finish("HAUL_3.loss");
            return;
        }

        bool hasUncommon = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Uncommon);
        bool hasRare = ThingsEventHelpers.HasGrabBagRelic(EventOwner, RelicRarity.Rare);
        RelicRarity? chosen = (hasRare, hasUncommon) switch
        {
            (true, true) => Rng.NextFloat() < 0.35f ? RelicRarity.Rare : RelicRarity.Uncommon,
            (true, false) => RelicRarity.Rare,
            (false, true) => RelicRarity.Uncommon,
            _ => null,
        };

        if (chosen is { } rarity)
            await ThingsEventHelpers.ObtainGrabBagRelicAsync(EventOwner, rarity);
        await CardPileCmd.AddCurseToDeck<Decay>(EventOwner);
        Finish("HAUL_3.description");
    }

    private Task StopWinch(string pageSuffix)
    {
        if (!IsFinished && !_settling)
        {
            _settling = true;
            Finish(pageSuffix);
        }
        return Task.CompletedTask;
    }

    private async Task CutLoosePouch()
    {
        if (!BeginSettlement())
            return;
        await PlayerCmd.GainGold(PouchGold, EventOwner);
        Finish("CUT_POUCH.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
