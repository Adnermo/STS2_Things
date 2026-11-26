using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsEchoingWell : ThingsEventModel
{
    public const int SpiralCost = 60;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new IntVar("Combats", 3), new GoldVar("SpiralGold", SpiralCost)];

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "RECORD", "SPIRAL", "LEAVE");

    // Native free-cost modifiers do not turn either kind of X cost into zero.
    private static bool CanRecord(CardModel card) => card.IsRemovable &&
        (card.Type is CardType.Attack or CardType.Skill) &&
        !card.EnergyCost.CostsX && !card.HasStarCostX && card.EnergyCost.Canonical >= 0;

    private static bool CanUse(Player player) => !player.Creature.IsDead &&
        player.Deck.Cards.Count > 1 && player.Deck.Cards.Any(CanRecord) &&
        player.GetRelic<ThingsBottledEcho>() == null;

    private static bool CanSpiral(CardModel? card) =>
        card != null && ModelDb.Enchantment<Spiral>().CanEnchant(card);

    private static bool HasSpiralTarget(Player player) =>
        !player.Creature.IsDead && player.Deck.Cards.Any(CanSpiral);

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && (CanUse(player) || (player.Gold >= SpiralCost && HasSpiralTarget(player))));

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        string spiralKey = !HasSpiralTarget(EventOwner) ? "SPIRAL_NO_CARD" :
            EventOwner.Gold < SpiralCost ? "SPIRAL_NO_GOLD" : "SPIRAL";
        return
        [
            new EventOption(this, CanUse(EventOwner) ? Record : null,
                InitialOptionKey(CanUse(EventOwner) ? "RECORD" : "LOCKED"),
                ModelDb.Relic<ThingsBottledEcho>().HoverTips),
            new EventOption(this, spiralKey == "SPIRAL" ? CarveSpiral : null,
                InitialOptionKey(spiralKey),
                HoverTipFactory.FromEnchantment<Spiral>()),
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private async Task Record()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        var card = (await CardSelectCmd.FromDeckForRemoval(player,
            new CardSelectorPrefs(L10NLookup("THINGS_ECHOING_WELL.selectionPrompt"), 1)
                { Cancelable = true, RequireManualConfirmation = true }, CanRecord)).FirstOrDefault();
        if (card == null || !CanUse(player) || !player.Deck.Cards.Contains(card) || !CanRecord(card))
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }
        var echo = (ThingsBottledEcho)ModelDb.Relic<ThingsBottledEcho>().ToMutable();
        echo.Record(card);
        await RelicCmd.Obtain(echo, player);
        await CardPileCmd.RemoveFromDeck(card);
        Finish("RECORDED");
    }

    private async Task CarveSpiral()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        CardModel? card = (await CardSelectCmd.FromDeckForEnchantment(player,
            ModelDb.Enchantment<Spiral>(), 1, CanSpiral,
            new CardSelectorPrefs(L10NLookup("THINGS_ECHOING_WELL.spiralPrompt"), 0, 1)
                { Cancelable = true })).FirstOrDefault();
        if (card == null || player.Creature.IsDead || !player.Deck.Cards.Contains(card) ||
            !CanSpiral(card) || player.Gold < SpiralCost)
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }
        await PlayerCmd.LoseGold(SpiralCost, player, GoldLossType.Spent);
        CardCmd.Enchant<Spiral>(card, 1m);
        NCardEnchantVfx? vfx = NCardEnchantVfx.Create(card);
        if (vfx != null) NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(vfx);
        Finish("SPIRAL");
    }

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        Finish("LEAVE");
        return Task.CompletedTask;
    }

    private void Finish(string page)
    {
        if (!IsFinished) SetEventFinished(L10NLookup($"THINGS_ECHOING_WELL.pages.{page}.description"));
    }
}
