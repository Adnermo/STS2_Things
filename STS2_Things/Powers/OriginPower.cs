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
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Owner && result.UnblockedDamage > 0 && target.CurrentHp <= Amount)
        {
            Flash();
            var origin = (OriginFogmog)Owner.Monster;
            await CreatureCmd.Stun(Owner, origin.illusionMove.StateId);
            await PowerCmd.Remove(this);
        }
    }
}