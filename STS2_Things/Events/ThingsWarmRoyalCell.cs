using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

/// <summary>
/// Hive event offering royal jelly sustain, basic Strike/Defend re-forging into Common cards,
/// or the Amber Comb Core relic paired with Sticky Beeswax status clutter.
/// </summary>
public sealed class ThingsWarmRoyalCell : ThingsEventModel
{
    public const int JellyHeal = 22;
    public const int JellyMaxHp = 6;
    public const int JellyBeeswaxCount = 1;
    public const int ReforgeDamage = 6;
    public const int ReforgeCount = 2;
    public const int CoreBeeswaxCount = 2;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new HealVar("JellyHeal", JellyHeal),
        new MaxHpVar(JellyMaxHp),
        new IntVar("JellyBeeswax", JellyBeeswaxCount),
        new DamageVar("ReforgeDamage", ReforgeDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new CardsVar("ReforgeCards", ReforgeCount),
        new IntVar("CoreBeeswax", CoreBeeswaxCount),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && player.GetRelic<ThingsAmberCombCore>() == null);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "EAT_JELLY", "REFORGE", "TAKE_CORE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([
                ModelDb.Card<ThingsStickyBeeswax>().PortraitPath,
                ModelDb.Relic<ThingsAmberCombCore>().PackedIconPath,
            ])
            .Distinct();

    public override void CalculateVars()
    {
        if (Owner == null)
            return;
        int eligibleCount = GetBasicStrikeOrDefendCards(Owner).Count;
        if (eligibleCount > 0)
            DynamicVars["ReforgeCards"].BaseValue = Math.Min(ReforgeCount, eligibleCount);
    }

    private static List<CardModel> GetBasicStrikeOrDefendCards(Player player) =>
        player.Deck.Cards
            .Where(card => card.IsTransformable
                           && card.Rarity == CardRarity.Basic
                           && (card.Tags.Contains(CardTag.Strike) || card.Tags.Contains(CardTag.Defend)))
            .ToList();

    private static List<CardModel> GetCommonPoolCandidates(Player player, ModelId excludedId) =>
        player.Character.CardPool
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(candidate => candidate.Rarity == CardRarity.Common
                                && candidate.IsRemovable
                                && candidate.Id != excludedId)
            .ToList();

    private static bool CanReforge(Player player)
    {
        List<CardModel> basics = GetBasicStrikeOrDefendCards(player);
        return basics.Count > 0 && GetCommonPoolCandidates(player, basics[0].Id).Count > 0;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool canReforge = CanReforge(EventOwner);
        bool ownsCore = EventOwner.GetRelic<ThingsAmberCombCore>() != null;

        EventOption jellyOption = new(
            this,
            EatJelly,
            InitialOptionKey("EAT_JELLY"),
            HoverTipFactory.FromCardWithCardHoverTips<ThingsStickyBeeswax>());

        EventOption reforgeOption = canReforge
            ? new EventOption(this, ReforgeBasics, InitialOptionKey("REFORGE")).ThatDoesDamage(ReforgeDamage)
            : new EventOption(this, null, InitialOptionKey("REFORGE_NO_CARD"));

        EventOption coreOption = ownsCore
            ? new EventOption(
                this,
                null,
                InitialOptionKey("CORE_OWNED"),
                ModelDb.Relic<ThingsAmberCombCore>().HoverTips)
            : new EventOption(
                this,
                TakeCore,
                InitialOptionKey("TAKE_CORE"),
                ModelDb.Relic<ThingsAmberCombCore>().HoverTips
                    .Concat(HoverTipFactory.FromCardWithCardHoverTips<ThingsStickyBeeswax>()));

        return [jellyOption, reforgeOption, coreOption];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task AddBeeswaxCardsAsync(int count)
    {
        List<CardModel> cards = new(count);
        for (int i = 0; i < count; i++)
            cards.Add(EventOwner.RunState.CreateCard<ThingsStickyBeeswax>(EventOwner));
        IReadOnlyList<CardPileAddResult> results = await CardPileCmd.Add(cards, PileType.Deck);
        CardCmd.PreviewCardPileAdd(results, 1.4f, CardPreviewStyle.EventLayout);
    }

    private async Task EatJelly()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Heal(EventOwner.Creature, JellyHeal);
        await CreatureCmd.GainMaxHp(EventOwner.Creature, JellyMaxHp);
        await AddBeeswaxCardsAsync(JellyBeeswaxCount);
        Finish("EAT_JELLY.description");
    }

    private async Task ReforgeBasics()
    {
        if (!BeginSettlement())
            return;
        List<CardModel> basics = GetBasicStrikeOrDefendCards(EventOwner);
        if (basics.Count == 0)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            EventOwner.Creature,
            ReforgeDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (EventOwner.Creature.IsDead)
        {
            Finish("REFORGE.loss");
            return;
        }

        List<CardModel> picked = basics.StableShuffle(Rng).Take(Math.Min(ReforgeCount, basics.Count)).ToList();
        List<CardTransformation> transformations = new(picked.Count);
        foreach (CardModel original in picked)
        {
            List<CardModel> pool = GetCommonPoolCandidates(EventOwner, original.Id);
            if (pool.Count == 0 || Rng.NextItem(pool) is not { } canonical)
                continue;
            CardModel replacement = (original.CardScope ?? EventOwner.RunState).CreateCard(canonical, EventOwner);
            transformations.Add(new CardTransformation(original, replacement));
        }

        if (transformations.Count > 0)
            await CardCmd.Transform(transformations, null, CardPreviewStyle.EventLayout);

        Finish("REFORGE.description");
    }

    private async Task TakeCore()
    {
        if (!BeginSettlement())
            return;
        if (EventOwner.GetRelic<ThingsAmberCombCore>() != null)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await RelicCmd.Obtain(ModelDb.Relic<ThingsAmberCombCore>().ToMutable(), EventOwner);
        await AddBeeswaxCardsAsync(CoreBeeswaxCount);
        Finish("TAKE_CORE.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
