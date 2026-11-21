using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Platform;

namespace STS2_Things.Powers;

/// <summary>
/// 悲叹 (Lamentation) player debuff inflicted by holding <see cref="Cards.ThingsLamentTear"/> at Turn End.
/// Inspired by vanilla <c>TaintedPower</c>, each stack adds +1 damage per hit whenever its owner takes
/// powered attack damage from <see cref="ThingsHumanFaceTowerLament"/>, persisting until Lament is defeated.
/// </summary>
public sealed class ThingsLamentSorrowPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override decimal OnModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredAttack())
            return 0m;
        if (dealer?.Monster is not ThingsHumanFaceTowerLament || !dealer.IsAlive)
            return 0m;

        return Amount;
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        RefreshLamentIntents();
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power == this)
            RefreshLamentIntents();
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        RefreshLamentIntents(oldOwner.CombatState);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || creature.IsAlive || creature.Monster is not ThingsHumanFaceTowerLament)
            return;

        if (Owner?.CombatState is not { } combatState || !Owner.IsAlive)
            return;

        if (!combatState.Enemies.Any(e => e != creature && e.IsAlive && e.Monster is ThingsHumanFaceTowerLament) &&
            Owner.GetPower<ThingsLamentSorrowPower>() == this)
        {
            await PowerCmd.Remove(this);
        }
    }

    private void RefreshLamentIntents(ICombatState? stateOverride = null)
    {
        ICombatState? combatState = stateOverride ?? Owner?.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding)
            return;

        foreach (Creature enemy in combatState.Enemies)
        {
            if (enemy.IsAlive && enemy.Monster is ThingsHumanFaceTowerLament)
                Sts2VersionSeam.RefreshMonsterIntentsUi(enemy);
        }
    }
}
