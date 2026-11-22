using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsMedusaHair : ThingsRelicModel
{
    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            foreach (var tip in HoverTipFactory.FromCardWithCardHoverTips<StoneArmor>(upgrade: true))
                yield return tip;
            foreach (var tip in HoverTipFactory.FromCardWithCardHoverTips<GiantRock>(upgrade: true))
                yield return tip;
            yield return HoverTipFactory.FromKeyword(CardKeyword.Exhaust);
        }
    }

    /// <summary>
    /// 战斗开始时（抽牌前），将一张0费强化后的岩石铠甲和一张带消耗的0费巨石+加入手牌。
    /// </summary>
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext,
        ICombatState combatState)
    {
        if (player == Owner && player.PlayerCombatState?.TurnNumber == 1)
        {
            Flash();

            // 0费强化后的岩石铠甲
            var stoneArmor = combatState.CreateCard<StoneArmor>(Owner);
            stoneArmor.EnergyCost.SetThisCombat(0);
            stoneArmor.UpgradeInternal();
            stoneArmor.FinalizeUpgradeInternal();
            await CardPileCmd.AddGeneratedCardToCombat(stoneArmor, PileType.Hand, Owner);

            // 0费带消耗的巨石+
            var giantRock = combatState.CreateCard<GiantRock>(Owner);
            giantRock.EnergyCost.SetThisCombat(0);
            giantRock.AddKeyword(CardKeyword.Exhaust);
            giantRock.UpgradeInternal();
            giantRock.FinalizeUpgradeInternal();
            await CardPileCmd.AddGeneratedCardToCombat(giantRock, PileType.Hand, Owner);
        }
    }
}
