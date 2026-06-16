using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Powers;

/// <summary>
///     死亡律动 — Boss 身上的 Debuff，玩家每出一张牌 Boss 受到伤害
///     完全模仿 StranglePower 的实现
/// </summary>
public sealed class 
    BeatOfDeathPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override object InitInternalData()
    {
        return new Data();
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        GetInternalData<Data>().amountsForPlayedCards.Add(cardPlay.Card, Amount);
        return Task.CompletedTask;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (GetInternalData<Data>().amountsForPlayedCards.Remove(cardPlay.Card, out var value))
        {
            Flash();
            // 伤害目标: base.Owner = Boss 自己
            await CreatureCmd.Damage(context, Owner, value,
                ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        }
    }

    private class Data
    {
        public readonly Dictionary<CardModel, decimal> amountsForPlayedCards = new();
    }
}