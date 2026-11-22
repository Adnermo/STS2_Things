using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Cards;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

public sealed class ThingsReusePower : ThingsPowerModel
{
    private sealed class Data
    {
        public int TriggersThisTurn;
    }

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override object InitInternalData() => new Data();

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<Fuel>()
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<Soot>());

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Player && participants.Contains(Owner))
        {
            GetInternalData<Data>().TriggersThisTurn = 0;
        }

        return Task.CompletedTask;
    }

    public override async Task AfterCardDrawn(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool fromHandDraw)
    {
        // 触发条件：
        // 1. 拥有者存活且战斗未结束
        // 2. 抽到的牌属于这个能力的拥有者（不是队友的牌）
        // 3. 抽到的是状态牌（Status），且目前仍在手牌中（未被其他抽牌钩子先消耗/移走）
        if (!Owner.IsAlive
            || CombatManager.Instance.IsOverOrEnding
            || card.Owner.Creature != Owner
            || card.Type != CardType.Status
            || card.Pile?.Type != PileType.Hand)
        {
            return;
        }

        if (Owner.Player is not { } player)
            return;

        Data data = GetInternalData<Data>();
        if (data.TriggersThisTurn >= Amount)
            return;

        data.TriggersThisTurn++;
        Flash();
        await CardPileCmd.RemoveFromCombat(card);
        if (!Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        var fuelCard = CombatState.CreateCard<Fuel>(player);
        var sootCard = CombatState.CreateCard<Soot>(player);
        await CardPileCmd.AddGeneratedCardToCombat(fuelCard, PileType.Hand, player);
        await CardPileCmd.AddGeneratedCardToCombat(sootCard, PileType.Discard, player);
    }
}
