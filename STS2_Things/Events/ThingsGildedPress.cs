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
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths event offering vanilla late-game card enchantments (Royally Approved or Clone)
/// in exchange for Max HP or Gold, or leaving.
/// </summary>
public sealed class ThingsGildedPress : ThingsEventModel
{
    public const int SealMaxHpLoss = 8;
    public const int MoldGoldCost = 75;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new MaxHpVar(SealMaxHpLoss),
        new GoldVar(MoldGoldCost),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && ((player.Creature.MaxHp > SealMaxHpLoss
                    && player.Deck.Cards.Any(CanApplyEnchantment<RoyallyApproved>))
                || (player.Gold >= MoldGoldCost
                    && player.Deck.Cards.Any(CanApplyEnchantment<Clone>))));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SEAL", "WAX_MOLD", "LEAVE");

    public override IEnumerable<string> GetAssetPaths(IRunState runState) =>
        base.GetAssetPaths(runState)
            .Concat([ModelDb.Enchantment<RoyallyApproved>().IconPath, ModelDb.Enchantment<Clone>().IconPath])
            .Distinct();

    private static bool CanApplyEnchantment<TEnchantment>(CardModel? card) where TEnchantment : EnchantmentModel =>
        card != null && ModelDb.Enchantment<TEnchantment>().CanEnchant(card);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool lowMaxHp = EventOwner.Creature.MaxHp <= SealMaxHpLoss;
        bool hasSealCard = EventOwner.Deck.Cards.Any(CanApplyEnchantment<RoyallyApproved>);
        EventOption sealOption = (lowMaxHp, hasSealCard) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("SEAL_LOW_HP"),
                HoverTipFactory.FromEnchantment<RoyallyApproved>()),
            (false, false) => new EventOption(
                this,
                null,
                InitialOptionKey("SEAL_NO_CARD"),
                HoverTipFactory.FromEnchantment<RoyallyApproved>()),
            _ => new EventOption(
                this,
                ApplySeal,
                InitialOptionKey("SEAL"),
                HoverTipFactory.FromEnchantment<RoyallyApproved>())
                .ThatDecreasesMaxHp(SealMaxHpLoss),
        };

        bool hasCloneCard = EventOwner.Deck.Cards.Any(CanApplyEnchantment<Clone>);
        bool hasGold = EventOwner.Gold >= MoldGoldCost;
        EventOption moldOption = (!hasCloneCard, !hasGold) switch
        {
            (true, _) => new EventOption(
                this,
                null,
                InitialOptionKey("WAX_MOLD_NO_CARD"),
                HoverTipFactory.FromEnchantment<Clone>()),
            (false, true) => new EventOption(
                this,
                null,
                InitialOptionKey("WAX_MOLD_NO_GOLD"),
                HoverTipFactory.FromEnchantment<Clone>()),
            _ => new EventOption(
                this,
                ApplyWaxMold,
                InitialOptionKey("WAX_MOLD"),
                HoverTipFactory.FromEnchantment<Clone>()),
        };

        return
        [
            sealOption,
            moldOption,
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

    private async Task ApplyEnchantmentOption<TEnchantment>(
        string promptKey,
        string pagePrefix,
        Func<Player, bool> precheck,
        Func<Player, Task> payCost,
        bool canKill) where TEnchantment : EnchantmentModel
    {
        if (!BeginSettlement())
            return;
        Player player = EventOwner;
        if (!precheck(player) || !player.Deck.Cards.Any(CanApplyEnchantment<TEnchantment>))
        {
            CancelToInitial();
            return;
        }

        CardModel? selected = (await CardSelectCmd.FromDeckForEnchantment(
            player,
            ModelDb.Enchantment<TEnchantment>(),
            1,
            CanApplyEnchantment<TEnchantment>,
            new CardSelectorPrefs(L10NLookup($"{Id.Entry}.{promptKey}"), 0, 1) { Cancelable = true }))
            .FirstOrDefault();

        if (selected == null || player.Creature.IsDead || !precheck(player) ||
            !player.Deck.Cards.Contains(selected) || !CanApplyEnchantment<TEnchantment>(selected))
        {
            CancelToInitial();
            return;
        }

        await payCost(player);
        if (canKill && player.Creature.IsDead)
        {
            Finish($"{pagePrefix}.loss");
            return;
        }

        CardCmd.Enchant<TEnchantment>(selected, 1m);
        CardCmd.Preview(selected, 1.4f, CardPreviewStyle.EventLayout);
        Finish($"{pagePrefix}.description");
    }

    private Task ApplySeal() => ApplyEnchantmentOption<RoyallyApproved>(
        "sealPrompt",
        "SEAL",
        static player => player.Creature.MaxHp > SealMaxHpLoss,
        player => LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), SealMaxHpLoss),
        canKill: true);

    private Task ApplyWaxMold() => ApplyEnchantmentOption<Clone>(
        "moldPrompt",
        "WAX_MOLD",
        static player => player.Gold >= MoldGoldCost,
        static player => PlayerCmd.LoseGold(MoldGoldCost, player, GoldLossType.Spent),
        canKill: false);

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
