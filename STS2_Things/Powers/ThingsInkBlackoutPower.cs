using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

/// <summary>
/// 墨浊覆牌 (Ink Blackout) — 渊墨巨蛸【高压渊墨扫射】施加给玩家的整回合全牌库染黑减益。
/// 玩家下一回合开始时，牌库内所有卡牌（抽牌堆、手牌、弃牌堆、消耗堆及洗牌重抽/衍生牌）均会被深渊墨水彻底染黑，
/// 统一替换为定制深渊墨蚀卡框、隐藏全部可识别信息与词条悬停提示，且费用在 0–3 间随机化；
/// 即使抽空牌堆从弃牌堆重新洗入抽牌堆也依然染黑。
/// 回合结束时自动消退；本回合斩断任意主触腕可立即冲净全部染黑卡牌并恢复原费用。
/// </summary>
public sealed class ThingsInkBlackoutPower : ThingsPowerModel
{
    public const int MaxTurns = 3;

    private bool _activeThisTurn;

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public bool IsActiveThisTurn => _activeThisTurn;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _activeThisTurn = false;
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        ClampTurns();
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (ReferenceEquals(power, this))
            ClampTurns();
        return Task.CompletedTask;
    }

    private void ClampTurns()
    {
        if (Amount > MaxTurns)
            SetAmount(MaxTurns);
    }

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Player || Amount <= 0 || Owner.Player == null || !participants.Contains(Owner))
            return Task.CompletedTask;

        AssertMutable();
        _activeThisTurn = true;
        Flash();

        AbyssalInkBlackout.InkEntirePlayerDeck(Owner.Player);
        NAbyssalInkSprayOverlay.SetPersistentInkBorder(true, washClean: false);
        return Task.CompletedTask;
    }

    public override Task AfterCardEnteredCombat(CardModel card)
    {
        if (!_activeThisTurn || Amount <= 0 || card.Owner?.Creature != Owner || AbyssalInkBlackout.IsWashedThisTurn(card))
            return Task.CompletedTask;

        AbyssalInkBlackout.InkCard(card, randomizeCost: true, rerollIfAlreadyInked: false);
        return Task.CompletedTask;
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (!_activeThisTurn || Amount <= 0 || card.Owner?.Creature != Owner || AbyssalInkBlackout.IsWashedThisTurn(card))
            return Task.CompletedTask;

        if (!AbyssalInkBlackout.IsInked(card))
        {
            AbyssalInkBlackout.InkCard(card, randomizeCost: true, rerollIfAlreadyInked: false);
        }
        else if (AbyssalInkBlackout.WasPlayedSinceLastRoll(card))
        {
            // Card was played, shuffled back from Discard into Draw, and redrawn during the same
            // blackened turn — keep it completely blackened and deterministically re-roll 0-3 cost!
            AbyssalInkBlackout.InkCard(card, randomizeCost: true, rerollIfAlreadyInked: true);
        }
        else
        {
            NInkBlackoutCardVeil.RefreshAllForModel(card);
        }

        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (_activeThisTurn && Amount > 0 && cardPlay.Card.Owner?.Creature == Owner)
        {
            AbyssalInkBlackout.MarkPlayedThisTurn(cardPlay.Card);
        }
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player || Owner.Player == null || !participants.Contains(Owner))
            return;

        if (!_activeThisTurn)
            return;

        AssertMutable();
        _activeThisTurn = false;
        AbyssalInkBlackout.ClearPlayerMarksAtTurnEnd(Owner.Player);
        NAbyssalInkSprayOverlay.SetPersistentInkBorder(false, washClean: false);
        await PowerCmd.Decrement(this);
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        if (_activeThisTurn)
        {
            AssertMutable();
            _activeThisTurn = false;
            if (oldOwner.Player is { } player)
            {
                AbyssalInkBlackout.WashPlayerCards(player, playWaveWash: true);
            }
            NAbyssalInkSprayOverlay.SetPersistentInkBorder(false, washClean: true);
        }
        return Task.CompletedTask;
    }
}
