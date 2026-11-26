using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Act 1 (Overgrowth / Underdocks) event: apply vanilla Inky enchantment to an enemy-targeting Attack or Skill
/// for 10 HP damage, transform 1 non-Curse card into a random Colorless card for 7 HP damage, or leave.
/// </summary>
public sealed class ThingsBlindCrabInkwell : ThingsEventModel
{
    public const int PinchDamage = 10;
    public const int RewriteDamage = 7;
    public const int InkyWeakAmount = 1;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("PinchDamage", PinchDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new DamageVar("RewriteDamage", RewriteDamage, ValueProp.Unblockable | ValueProp.Unpowered),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && ((player.Creature.CurrentHp > PinchDamage && player.Deck.Cards.Any(IsInkyCandidate))
                || (player.Creature.CurrentHp > RewriteDamage && player.Deck.Cards.Any(card => CanRewrite(player, card)))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SOAK", "REWRITE", "LEAVE");

    // Inky reads cardPlay.Target unless TargetType == AllEnemies, so exclude RandomEnemy/Self/Ally cards.
    private static bool IsInkyCandidate(CardModel? card) =>
        card != null
        && card.Type is CardType.Attack or CardType.Skill
        && card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies
        && ModelDb.Enchantment<Inky>().CanEnchant(card);

    private static List<CardModel> GetColorlessTransformPool(Player player, CardModel original) =>
        ModelDb.CardPool<ColorlessCardPool>()
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(candidate => candidate.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare
                                && candidate.Id != original.Id)
            .ToList();

    private static bool CanRewrite(Player player, CardModel card) =>
        card.IsTransformable
        && card.Type != CardType.Curse
        && GetColorlessTransformPool(player, card).Count > 0;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool canSoak = EventOwner.Deck.Cards.Any(IsInkyCandidate);
        EventOption soakOption = canSoak
            ? new EventOption(
                this,
                SoakCard,
                InitialOptionKey("SOAK"),
                HoverTipFactory.FromEnchantment<Inky>(InkyWeakAmount))
                .ThatDoesDamage(PinchDamage)
            : new EventOption(
                this,
                null,
                InitialOptionKey("SOAK_NO_CARD"),
                HoverTipFactory.FromEnchantment<Inky>(InkyWeakAmount));

        bool canRewrite = EventOwner.Deck.Cards.Any(card => CanRewrite(EventOwner, card));
        EventOption rewriteOption = canRewrite
            ? new EventOption(this, RewriteCard, InitialOptionKey("REWRITE"))
                .ThatDoesDamage(RewriteDamage)
            : new EventOption(this, null, InitialOptionKey("REWRITE_NO_CARD"));

        return
        [
            soakOption,
            rewriteOption,
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

    private async Task SoakCard()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (!player.Deck.Cards.Any(IsInkyCandidate))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForEnchantment(
            player,
            ModelDb.Enchantment<Inky>(),
            1,
            IsInkyCandidate,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.soakPrompt"), 0, 1) { Cancelable = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead ||
            !player.Deck.Cards.Contains(selected) || !IsInkyCandidate(selected))
        {
            CancelToInitial();
            return;
        }

        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            player.Creature,
            PinchDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (player.Creature.IsDead)
        {
            Finish("SOAK.loss");
            return;
        }

        CardCmd.Enchant<Inky>(selected, InkyWeakAmount);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish("SOAK.description");
    }

    private async Task RewriteCard()
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (!player.Deck.Cards.Any(card => CanRewrite(player, card)))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckGeneric(
            player,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.rewritePrompt"), 1)
            {
                Cancelable = true,
                RequireManualConfirmation = true,
            },
            card => CanRewrite(player, card)))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead ||
            !player.Deck.Cards.Contains(selected) || !CanRewrite(player, selected))
        {
            CancelToInitial();
            return;
        }

        await ThingsEventHelpers.DealUnblockableDamageAsync(player, RewriteDamage);
        if (player.Creature.IsDead)
        {
            Finish("REWRITE.loss");
            return;
        }

        CardModel replacement = CardFactory.CreateRandomCardForTransform(
            selected,
            GetColorlessTransformPool(player, selected),
            isInCombat: false,
            Rng);
        await CardCmd.Transform(selected, replacement, CardPreviewStyle.EventLayout);
        Finish("REWRITE.description");
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
