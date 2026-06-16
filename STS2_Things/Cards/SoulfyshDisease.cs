using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Enchantments;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

public sealed class SoulfyshDisease : CardModel
{
    private bool _wasPlayedWithSly;

    public override IEnumerable<CardKeyword> CanonicalKeywords => new[]
    {
        CardKeyword.Exhaust,
        CardKeyword.Ethereal,
        CardKeyword.Sly
    };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new IHoverTip[] { HoverTipFactory.FromPower<SoulfyshDiseasePower>() }
            .Concat(IsUpgraded
                ? HoverTipFactory.FromEnchantment<Disperse>()
                : Enumerable.Empty<IHoverTip>())
            .Concat(new[] { HoverTipFactory.FromCard<Beckon>() });

    public SoulfyshDisease()
        : base(4, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        if (cardPlay.Resources.EnergySpent == 0 && cardPlay.Resources.EnergyValue > 0)
        {
            _wasPlayedWithSly = true;
            var beckon = CombatState.CreateCard<Beckon>(Owner);

            if (IsUpgraded)
                CardCmd.Enchant<Disperse>(beckon, 1);

            await CardPileCmd.Add(beckon, PileType.Discard);

            CardModel copy = CreateClone();
            CardCmd.PreviewCardPileAdd(
                await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, base.Owner),
                2.2f);
        }

        await PowerCmd.Apply<SoulfyshDiseasePower>(choiceContext,
            Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        base.EnergyCost.UpgradeBy(-1);
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
