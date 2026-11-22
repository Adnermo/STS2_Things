using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 嗔怒面 passive: 【怒目威压】.
/// Whenever an attack action from Wrath deals unblocked HP damage to at least one opponent,
/// ALL living visages of the tower gain +2 permanent Strength after the attack resolves.
/// </summary>
public sealed class ThingsWrathGlarePower : ThingsPowerModel
{
    public const int StrengthGainPerBloodiedAttack = 2;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("StrengthGain", StrengthGainPerBloodiedAttack)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<StrengthPower>()
    ];

    public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
    {
        if (command.Attacker != Owner || !Owner.IsAlive)
            return;

        bool drewBlood = command.Results
            .SelectMany(group => group)
            .Any(hit => hit.UnblockedDamage > 0 && hit.Receiver.Side != Owner.Side);
        if (!drewBlood)
            return;

        Flash();
        decimal gain = DynamicVars["StrengthGain"].BaseValue;
        if (Owner.Monster is ThingsHumanFaceTowerFace { Controller: { } ctrl })
        {
            await ctrl.ApplyStrengthToAllLivingFacesAsync(choiceContext, gain, Owner);
        }
        else
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, gain, Owner, null);
        }
    }
}
