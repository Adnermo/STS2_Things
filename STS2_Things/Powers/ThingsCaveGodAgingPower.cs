using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 老化（Aging）：每次击倒其中一只手臂，向所有存活玩家手牌加入一张 0 费岩石铠甲。
/// </summary>
public sealed class ThingsCaveGodAgingPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        ModelDb.Card<StoneArmor>().HoverTips;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() =>
        Owner?.Monster is not ThingsCaveGodHand { Body.IsDefeated: false };

    public override bool ShouldOwnerDeathTriggerFatal() =>
        Owner?.Monster is not ThingsCaveGodHand { Body.IsDefeated: false };

    public async Task OnArmDowned()
    {
        if (Owner?.Monster is not ThingsCaveGodHand { Body: { IsDefeated: false } })
            return;
        Flash();
        await GiveStoneArmor();
    }

    internal async Task GiveStoneArmor()
    {
        if (Owner?.CombatState is not { } combatState)
            return;

        foreach (Player player in combatState.Players)
        {
            if (player.Creature.IsDead)
                continue;

            CardModel stoneArmor = combatState.CreateCard<StoneArmor>(player);
            stoneArmor.SetToFreeThisCombat();
            await CardPileCmd.AddGeneratedCardToCombat(stoneArmor, PileType.Hand, player);
        }
    }
}
