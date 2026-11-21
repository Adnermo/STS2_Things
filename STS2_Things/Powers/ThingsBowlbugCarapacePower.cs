using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 虫群护甲 — 盛碗虫族母在【重击】后获得的 1 回合临时防御 Buff。
/// 施加时立即按当前存活盛碗虫数量获得每只 Amount 点格挡（ValueProp.Move 支持多人缩放）；
/// 生效期间每当新的盛碗虫被召唤入场时，额外获得 Amount 点格挡；
/// 下一个敌方回合开始（BeforeSideTurnStart）时自动移除。
/// </summary>
public sealed class ThingsBowlbugCarapacePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block)
    ];

    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        await GrantInitialBlockAsync();
    }

    public async Task GrantInitialBlockAsync()
    {
        if (!Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        int initialBlock = ThingsBowlbugProgenitor.CountLiveBowlbugs(Owner.CombatState) * Amount;
        if (initialBlock > 0)
        {
            await CreatureCmd.GainBlock(Owner, initialBlock, ValueProp.Move, null, fast: true);
        }
    }

    public override async Task AfterCreatureAddedToCombat(Creature creature)
    {
        if (!Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;
        if (!ThingsBowlbugProgenitor.IsBowlbugMinion(creature))
            return;

        Flash();
        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null, fast: true);
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Enemy && Owner.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await PowerCmd.Remove(this);
        }
    }
}
