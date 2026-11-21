using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Enchantments;

/// <summary>
///     消散 — 给被附魔的卡牌添加虚无词条
///     回合结束时若在手牌中则自动消耗
/// </summary>
public sealed class ThingsDisperse : EnchantmentModel
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.FromKeyword(CardKeyword.Ethereal) };

    protected override void OnEnchant()
    {
        Card.AddKeyword(CardKeyword.Ethereal);
    }

    // 允许附魔诅咒/状态牌，但拒绝任务牌、已有附魔牌与已具备虚无词条的牌
    public override bool CanEnchant(CardModel card)
    {
        return card != null
            && card.Type != CardType.Quest
            && card.Enchantment == null
            && !card.Keywords.Contains(CardKeyword.Ethereal);
    }
}