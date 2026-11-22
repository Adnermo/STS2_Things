using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Enchantments;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

public sealed class ThingsSoulfyshDisease : ThingsCardModel
{
    private bool _slyDiscardPending;
    private bool _wasPlayedWithSly;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _slyDiscardPending = false;
        _wasPlayedWithSly = false;
    }

    public override Task BeforeCardAutoPlayed(CardModel card, Creature? target, AutoPlayType type)
    {
        if (card == this && type == AutoPlayType.SlyDiscard)
            _slyDiscardPending = true;
        return Task.CompletedTask;
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[]
    {
        CardKeyword.Exhaust,
        CardKeyword.Ethereal,
        CardKeyword.Sly
    };

    protected override List<DynamicVar> CanonicalVars =>
    [
        new IntVar("BeckonCount", 2)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromPowerWithPowerHoverTips<ThingsSoulfyshDiseasePower>()
            .Concat(HoverTipFactory.FromEnchantment<ThingsDisperse>())
            .Append(HoverTipFactory.FromCard<Beckon>());

    public ThingsSoulfyshDisease()
        : base(3, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.IsFirstInSeries)
            _wasPlayedWithSly = false;

        bool wasPlayedWithSly = _slyDiscardPending && cardPlay.IsAutoPlay;
        _slyDiscardPending = false;

        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        if (wasPlayedWithSly)
        {
            var combatState = CombatState
                ?? throw new InvalidOperationException("Soulfysh Disease was played outside combat.");
            // 奇巧时：仅当本牌本次打出将消耗（例如未被灵魂之力移除消耗）时才复制本牌到弃牌堆并由消耗钩子移除本体；
            // 若消耗已被移除或重定向，本体会自然进入弃牌堆，无需再克隆以免指数复制。
            if (cardPlay.ResultPile == PileType.Exhaust)
            {
                _wasPlayedWithSly = true;
                CardModel copy;
                if (Pile == null || Pile.Type.IsCombatPile())
                {
                    copy = CreateClone();
                    copy.FinalizeUpgradeInternal();
                }
                else
                {
                    copy = combatState.CreateCard<ThingsSoulfyshDisease>(Owner);
                    for (int i = 0; i < CurrentUpgradeLevel; i++)
                    {
                        copy.UpgradeInternal();
                        copy.FinalizeUpgradeInternal();
                    }
                    if (Enchantment != null)
                    {
                        var clonedEnchantment = (EnchantmentModel)Enchantment.ClonePreservingMutability();
                        CardCmd.Enchant(clonedEnchantment, copy, clonedEnchantment.Amount);
                    }
                }
                await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, Owner);
            }

            int beckonCount = DynamicVars["BeckonCount"].IntValue;
            for (int i = 0; i < beckonCount; i++)
            {
                var beckon = combatState.CreateCard<Beckon>(Owner);
                CardCmd.Enchant<ThingsDisperse>(beckon, 1);
                await CardPileCmd.AddGeneratedCardToCombat(beckon, PileType.Discard, Owner);
            }
        }

        await PowerCmd.Apply<ThingsSoulfyshDiseasePower>(choiceContext,
            Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        base.EnergyCost.UpgradeBy(-1);
        DynamicVars["BeckonCount"].UpgradeValueBy(-1);
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this && Pile?.Type is not (PileType.Play or PileType.Exhaust))
        {
            _slyDiscardPending = false;
            _wasPlayedWithSly = false;
        }
        return Task.CompletedTask;
    }

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        await base.AfterCardExhausted(choiceContext, card, causedByEthereal);
        if (card == this && _wasPlayedWithSly)
        {
            _wasPlayedWithSly = false;
            await CardPileCmd.RemoveFromCombat(this);
        }
    }
}
