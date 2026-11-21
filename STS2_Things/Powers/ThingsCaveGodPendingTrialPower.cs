using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 内部隐藏调度状态：狂暴二阶段被巨爪抓走的玩家在下回合完成开局抽牌（含赌博筹码等抽牌遗物）后，
/// 再弹出「断刃 / 碎盾」二选一试炼弹窗。采用 Buff 类型以避免浪费玩家的人工制品。
/// </summary>
public sealed class ThingsCaveGodPendingTrialPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    public override bool ShouldPlayVfx => false;
    protected override bool IsVisibleInternal => false;

    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature != Owner)
            return;

        Creature owner = Owner;
        ThingsCaveGodHand? hand = Applier?.Monster as ThingsCaveGodHand;
        await PowerCmd.Remove(this);
        if (owner.IsAlive && hand != null)
        {
            await hand.ChooseTrialAfterDraw(choiceContext, owner);
        }
    }
}
