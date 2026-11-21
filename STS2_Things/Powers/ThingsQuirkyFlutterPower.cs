using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// Native Flutter cannot be reused because its stun branch casts the owner to
/// ThievingHopper. This keeps the native behavior while targeting QuirkyHopper.
/// </summary>
public sealed class ThingsQuirkyFlutterPower : ThingsPowerModel
{
    private const string DamageDecreaseKey = "DamageDecrease";

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar(DamageDecreaseKey, 50m)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Stun)];

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return DynamicVars[DamageDecreaseKey].BaseValue / 100m;
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || result.UnblockedDamage == 0 || !props.IsPoweredAttack())
        {
            return;
        }

        await PowerCmd.Decrement(this);
        if (Amount > 0)
        {
            return;
        }

        await CreatureCmd.TriggerAnim(Owner, ThingsQuirkyHopper.StunTrigger, 0.6f);
        var hopper = Owner.Monster as ThingsQuirkyHopper
            ?? throw new InvalidOperationException("Quirky Flutter owner is not a Quirky Hopper.");
        var moveStateMachine = hopper.MoveStateMachine
            ?? throw new InvalidOperationException("Quirky Hopper move state machine is unavailable.");
        string nextState = moveStateMachine.StateLog.Last()
            .GetNextState(Owner, hopper.RunRng.MonsterAi);
        await CreatureCmd.Stun(Owner, StunnedMove, nextState);
        hopper.IsHovering = false;
        SfxCmd.StopLoop(Owner, ThingsQuirkyHopper.HoverLoop);
        Flash();
        await Cmd.Wait(0.25f);
    }

    private static Task StunnedMove(IReadOnlyList<Creature> targets)
    {
        return Task.CompletedTask;
    }
}
