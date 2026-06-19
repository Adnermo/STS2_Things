using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

public sealed class OriginPower : PowerModel
{
    private class Data
    {
        public decimal damageReceived;
    }

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool ShouldScaleInMultiplayer => true;

    // Amount = 半血阈值（由 PowerCmd.Apply 传入 baseHp/2，多人自动缩放）。
    // DisplayAmount = 阈值 - 已受伤害，显示"还剩多少血转阶段"。
    public override int DisplayAmount =>
        (int)Math.Max(0m, Amount - GetInternalData<Data>().damageReceived);

    protected override object InitInternalData() => new Data();

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || result.WasFullyBlocked)
            return;

        GetInternalData<Data>().damageReceived += (decimal)result.UnblockedDamage;
        InvokeDisplayAmountChanged();

        if (GetInternalData<Data>().damageReceived >= Amount)
        {
            Flash();
            if (Owner.Monster is not OriginFogmog origin || origin.illusionMove == null) return;
            origin.OnPhaseTransition();
            await CreatureCmd.Stun(Owner, origin.illusionMove.StateId);
            await PowerCmd.Remove(this);
        }
    }
}
