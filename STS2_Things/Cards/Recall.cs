using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

/// <summary>
/// 回溯 — Silent Uncommon Skill
/// 0费，获得"本回合从弃牌堆抽牌"的Buff
/// 强化：添加保留词条
/// </summary>
public sealed class Recall : CardModel
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => IsUpgraded
        ? new[] { CardKeyword.Retain }
        : System.Array.Empty<CardKeyword>();

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new IHoverTip[] { HoverTipFactory.FromPower<RecallPower>() };

    public Recall()
        : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<RecallPower>(choiceContext,
            Owner.Creature, 0m, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
