using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsMedusa : ThingsEventModel
{
    public const int GoldCost = 90;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar(GoldCost) // 献上钱财所需金币
    ];

    private static bool CanSteady(CardModel? card) =>
        card != null && ModelDb.Enchantment<Steady>().CanEnchant(card);

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && (player.GetRelic<PetrifiedToad>() != null
                || (player.Gold >= GoldCost && player.Deck.Cards.Any(CanSteady))));

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var options = new List<EventOption>();

        // 选项1：物归原主 — 需要拥有石化蟾蜍
        var toad = EventOwner.GetRelic<PetrifiedToad>();
        if (toad != null)
        {
            var tips = HoverTipFactory.FromRelic<PetrifiedToad>()
                .Concat(HoverTipFactory.FromRelic<ThingsMedusaHair>());
            options.Add(new EventOption(this, ReturnToOwner,
                InitialOptionKey("RETURN_TO_OWNER"),
                tips));
        }
        else
        {
            var lockedTips = HoverTipFactory.FromRelic<PetrifiedToad>();
            options.Add(new EventOption(this, null,
                InitialOptionKey("RETURN_TO_OWNER_LOCKED"),
                lockedTips));
        }

        // 选项2：献上钱财 — 需要90金币且存在可附魔稳定的牌
        bool hasSteadyCard = EventOwner.Deck.Cards.Any(CanSteady);
        bool hasGold = EventOwner.Gold >= GoldCost;
        if (hasGold && hasSteadyCard)
        {
            options.Add(new EventOption(this, OfferGold,
                InitialOptionKey("OFFER_GOLD"),
                HoverTipFactory.FromEnchantment<Steady>()));
        }
        else if (!hasSteadyCard)
        {
            options.Add(new EventOption(this, null,
                InitialOptionKey("OFFER_GOLD_NO_CARD"),
                HoverTipFactory.FromEnchantment<Steady>()));
        }
        else
        {
            options.Add(new EventOption(this, null,
                InitialOptionKey("OFFER_GOLD_LOCKED"),
                Array.Empty<IHoverTip>()));
        }

        // 选项3：礼貌问候
        options.Add(new EventOption(this, Greet,
            InitialOptionKey("GREET"),
            Array.Empty<IHoverTip>()));

        return options;
    }

    /// <summary>
    /// 物归原主：失去石化蟾蜍，获得美杜莎之发
    /// </summary>
    private async Task ReturnToOwner()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return;
        _settling = true;
        var owner = EventOwner;
        var toad = owner.GetRelic<PetrifiedToad>();
        if (toad != null)
        {
            await RelicCmd.Remove(toad);
        }
        var hair = ModelDb.Relic<ThingsMedusaHair>().ToMutable();
        await RelicCmd.Obtain(hair, owner);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.RETURN_TO_OWNER.description"));
    }

    /// <summary>
    /// 献上钱财：选择一张卡附魔保留，确认后再扣除90金币
    /// </summary>
    private async Task OfferGold()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return;
        _settling = true;
        var owner = EventOwner;
        if (owner.Gold < GoldCost || !owner.Deck.Cards.Any(CanSteady))
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 0, 1)
        {
            Cancelable = true
        };
        var enchantment = ModelDb.Enchantment<Steady>();
        var card = (await CardSelectCmd.FromDeckForEnchantment(owner, enchantment, 1, CanSteady, prefs))
            .FirstOrDefault();

        if (card == null || owner.Creature.IsDead || owner.Gold < GoldCost ||
            !owner.Deck.Cards.Contains(card) || !CanSteady(card))
        {
            _settling = false;
            if (owner.Creature.IsDead)
                SetEventFinished(L10NLookup($"{Id.Entry}.pages.ABORTED.description"));
            else
                SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await PlayerCmd.LoseGold(GoldCost, owner, GoldLossType.Spent);
        CardCmd.Enchant<Steady>(card, 1m);
        var vfx = NCardEnchantVfx.Create(card);
        if (vfx != null)
        {
            NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(vfx);
        }
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.OFFER_GOLD.description"));
    }

    /// <summary>
    /// 礼貌问候：礼貌离开，无额外奖励
    /// </summary>
    private Task Greet()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return Task.CompletedTask;
        _settling = true;
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.GREET.description"));
        return Task.CompletedTask;
    }
}
