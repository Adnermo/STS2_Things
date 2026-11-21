using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

public sealed class ThingsAbsorbentSpongePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("DamagePerWater", 4)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<ThingsSpongeReservoirPower>()];

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        RefreshThreshold();
        return Task.CompletedTask;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || !Owner.IsAlive || result.UnblockedDamage <= 0 || !props.IsPoweredAttack()) return;
        if (Owner.Monster is not ThingsWaterSponge sponge ||
            Owner.GetPower<ThingsSpongeDamageProgressPower>() is not { } progress) return;
        RefreshThreshold();
        if (sponge.Water >= ThingsWaterSponge.WaterCapacity)
        {
            progress.Reset();
            return;
        }
        int damage = progress.Damage + result.UnblockedDamage;
        int stacks = Math.Min(ThingsWaterSponge.WaterCapacity - sponge.Water, damage / sponge.DamagePerWater);
        // Overflow at full capacity is discarded, so a large hit cannot bank
        // another spray. Otherwise partial damage carries across hits and turns.
        progress.SetDamage(sponge.Water + stacks >= ThingsWaterSponge.WaterCapacity
            ? 0 : damage % sponge.DamagePerWater);
        if (stacks == 0) return;
        Flash();
        await sponge.AbsorbWater(choiceContext, stacks);
    }

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        RefreshThreshold();
        // A forced stun keeps its turn. Once it has been performed, a full
        // reservoir must still select the visible spray for the following turn.
        if (side == CombatSide.Enemy && Owner.Monster is ThingsWaterSponge sponge) sponge.PrepareSpray();
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        oldOwner.GetPower<ThingsSpongeDamageProgressPower>()?.Reset();
        return Task.CompletedTask;
    }

    private void RefreshThreshold()
    {
        if (Owner.Monster is not ThingsWaterSponge sponge) return;
        DynamicVars["DamagePerWater"].BaseValue = sponge.DamagePerWater;
        InvokeDisplayAmountChanged();
    }
}
