using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
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

namespace STS2_Things.Events;

/// <summary>
/// Hive event offering batch duplicate removal (two matching cards for 18 HP damage),
/// Goopy enchantment on a Defend card for Gold, or leaving.
/// </summary>
public sealed class ThingsAntSortingTable : ThingsEventModel
{
    public const int CullDamage = 18;
    public const int GlueGoldCost = 60;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("CullDamage", CullDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar(GlueGoldCost),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && ((player.Creature.CurrentHp > CullDamage && DuplicateRemovableCandidates(player).Count >= 2)
                || (player.Gold >= GlueGoldCost && player.Deck.Cards.Any(CanApplyGoopy))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "CULL", "GLUE", "LEAVE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<Goopy>().IconPath])
            .Distinct();

    private static List<CardModel> DuplicateRemovableCandidates(Player player)
    {
        List<CardModel> removable = player.Deck.Cards.Where(card => card.IsRemovable).ToList();
        HashSet<ModelId> duplicateIds = removable
            .GroupBy(card => card.Id)
            .Where(group => group.Count() >= 2)
            .Select(group => group.Key)
            .ToHashSet();
        return removable.Where(card => duplicateIds.Contains(card.Id)).ToList();
    }

    private static bool CanApplyGoopy(CardModel? card) =>
        card != null && ModelDb.Enchantment<Goopy>().CanEnchant(card);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool canCull = DuplicateRemovableCandidates(EventOwner).Count >= 2;
        EventOption cullOption = canCull
            ? new EventOption(this, CullDuplicates, InitialOptionKey("CULL"))
                .ThatDoesDamage(CullDamage)
            : new EventOption(this, null, InitialOptionKey("CULL_NO_PAIRS"));

        bool hasGoopyCard = EventOwner.Deck.Cards.Any(CanApplyGoopy);
        bool hasGold = EventOwner.Gold >= GlueGoldCost;
        EventOption glueOption = (!hasGoopyCard, !hasGold) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("GLUE_NO_CARD"),
                HoverTipFactory.FromEnchantment<Goopy>()),
            (false, true) => new EventOption(
                this,
                null,
                InitialOptionKey("GLUE_NO_GOLD"),
                HoverTipFactory.FromEnchantment<Goopy>()),
            _ => new EventOption(
                this,
                ApplyChitinGlue,
                InitialOptionKey("GLUE"),
                HoverTipFactory.FromEnchantment<Goopy>()),
        };

        return
        [
            cullOption,
            glueOption,
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

    private void CancelToInitial()
    {
        _settling = false;
        if (EventOwner.Creature.IsDead)
            Finish("ABORTED.description");
        else
            SetEventState(InitialDescription, GenerateInitialOptions());
    }

    private async Task CullDuplicates()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        HashSet<CardModel> candidates = DuplicateRemovableCandidates(player).ToHashSet();
        if (candidates.Count < 2)
        {
            CancelToInitial();
            return;
        }

        CardModel? first = (await CardSelectCmd.FromDeckForRemoval(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.cullFirstPrompt"), 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            },
            candidates.Contains))
            .FirstOrDefault();

        if (first == null || player.Creature.IsDead || !player.Deck.Cards.Contains(first) || !first.IsRemovable)
        {
            CancelToInitial();
            return;
        }

        CardModel? second = (await CardSelectCmd.FromDeckForRemoval(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.cullSecondPrompt"), 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            },
            card => card != first && card.Id == first.Id && card.IsRemovable))
            .FirstOrDefault();

        if (second == null || player.Creature.IsDead ||
            !player.Deck.Cards.Contains(first) || !player.Deck.Cards.Contains(second))
        {
            CancelToInitial();
            return;
        }

        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            player.Creature,
            CullDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (player.Creature.IsDead)
        {
            Finish("CULL.loss");
            return;
        }

        await CardPileCmd.RemoveFromDeck([first, second]);
        Finish("CULL.description");
    }

    private async Task ApplyChitinGlue()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (player.Gold < GlueGoldCost || !player.Deck.Cards.Any(CanApplyGoopy))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForEnchantment(
            player,
            ModelDb.Enchantment<Goopy>(),
            1,
            CanApplyGoopy,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.gluePrompt"), 0, 1) { Cancelable = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead || player.Gold < GlueGoldCost ||
            !player.Deck.Cards.Contains(selected) || !CanApplyGoopy(selected))
        {
            CancelToInitial();
            return;
        }

        await PlayerCmd.LoseGold(GlueGoldCost, player, GoldLossType.Spent);
        // Goopy starts at Amount = 1 (adding Amount - 1 = 0 initially) and increments +1 each play.
        CardCmd.Enchant<Goopy>(selected, 1m);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish("GLUE.description");
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
