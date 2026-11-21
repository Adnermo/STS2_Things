using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Core.Models;

namespace STS2_Things.Cards;

/// <summary>
/// 整理 — Silent Uncommon Skill
/// 1费，抽1张牌，选择牌库中1张牌丢弃（可触发奇巧），消耗
/// 强化：去掉消耗
/// </summary>
public sealed class ThingsPackUp : ThingsCardModel
{
    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Exhaust];

    protected override List<DynamicVar> CanonicalVars =>
    [
        new CardsVar(1)
    ];

    public ThingsPackUp()
        : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 抽1张牌
        await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);

        // 从抽牌堆选择牌丢弃
        var drawPile = PileType.Draw.GetPile(Owner);
        if (drawPile.Cards.Count > 0)
        {
            foreach (CardModel selected in await CardSelectCmd.FromCombatPile(
                prefs: new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, DynamicVars.Cards.IntValue),
                context: choiceContext,
                pile: drawPile,
                player: Owner))
            {
                await CardCmd.Discard(choiceContext, selected);
            }
        }
    }

    protected override void OnUpgrade()
    {
        RemoveKeyword(CardKeyword.Exhaust);
    }
}
