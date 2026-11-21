using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
///     死亡时给予所有玩家1点能量
/// </summary>
public sealed class ThingsOriginGainEnergyPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("Energy", 1m) };

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (IsMutable)
                DynamicVars["Energy"].BaseValue = Math.Max(0, Amount);
            return base.SmartDescriptionLocKey;
        }
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext, Creature creature, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (wasRemovalPrevented || creature != Owner || Amount <= 0)
            return;

        var combatState = Owner?.CombatState;
        if (combatState == null)
            return;

        Flash();
        foreach (var player in combatState.Players)
        {
            if (!player.Creature.IsDead)
                await PlayerCmd.GainEnergy(Amount, player);
        }
    }
}
