using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for all <c>STS2_Things</c> powers.
/// Unifies <c>AfterDeath</c>, <c>ModifyDamage*</c>, <c>AfterBlockBroken</c>, and <c>CardPlay</c> actor resolution.
/// </summary>
public abstract class ThingsPowerModel : PowerModel
{
#if STS2_V113_1
    public sealed override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
        => OnCreatureDeathAsync(choiceContext, creature, source, wasRemovalPrevented, deathAnimLength);
#else
    public sealed override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
        => OnCreatureDeathAsync(choiceContext, creature, null, wasRemovalPrevented, deathAnimLength);
#endif

    protected virtual Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
        => Task.CompletedTask;

    public sealed override decimal ModifyDamageAdditive(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageAdditive(target, amount, props, dealer, cardSource, cardPlay);

    public sealed override decimal ModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageMultiplicative(target, amount, props, dealer, cardSource, cardPlay);

    public sealed override decimal ModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageCap(target, props, dealer, cardSource, cardPlay);

    public sealed override Task AfterBlockBroken(PlayerChoiceContext choiceContext, Creature target, Creature? breaker)
        => OnBlockBrokenAsync(choiceContext, target, breaker);

    protected virtual decimal OnModifyDamageAdditive(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => 0m;

    protected virtual decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => 1m;

    protected virtual decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => decimal.MaxValue;

    protected virtual Task OnBlockBrokenAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        Creature? breaker)
        => Task.CompletedTask;

    protected static Player GetCardPlayActor(CardPlay play)
        => Sts2VersionSeam.GetCardPlayActor(play);
}
