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
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
/// 【断刃】(Broken Blade) Debuff：本回合无法打出攻击牌，回合结束自动移除。
/// </summary>
public sealed class ThingsCaveGodBrokenBladePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card.Owner?.Creature != Owner)
            return base.ShouldPlay(card, autoPlayType);
        return card.Type != CardType.Attack;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            Flash();
            await PowerCmd.Remove(this);
        }
    }
}

/// <summary>
/// 【碎盾】(Shattered Shield) Debuff：本回合无法打出技能牌，回合结束自动移除。
/// </summary>
public sealed class ThingsCaveGodShatteredShieldPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card.Owner?.Creature != Owner)
            return base.ShouldPlay(card, autoPlayType);
        return card.Type != CardType.Skill;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner))
        {
            Flash();
            await PowerCmd.Remove(this);
        }
    }
}
