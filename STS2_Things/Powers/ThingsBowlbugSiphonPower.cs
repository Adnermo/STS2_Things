using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
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
using STS2_Things.Platform;

namespace STS2_Things.Powers;

/// <summary>
/// 汲取 — 盛碗虫族母在【激励】回合施加的蓄势 Buff。
/// 在玩家回合期间按当前存活盛碗虫数量（每只 +Amount）实时加成并刷新下回合【重击】意图预览；
/// 在玩家回合结束时移除自身，并转化为等量的 <see cref="ThingsBowlbugVigorPower"/>。
/// </summary>
public sealed class ThingsBowlbugSiphonPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("ProjectedVigor", 0)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<ThingsBowlbugVigorPower>()
    ];

    public void RefreshProjectedVigor()
    {
        if (!IsMutable)
            return;

        int projected = ThingsBowlbugProgenitor.CountLiveBowlbugs(Owner.CombatState) * Amount;
        DynamicVars["ProjectedVigor"].BaseValue = projected;
        InvokeDisplayAmountChanged();
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        RefreshProjectedVigor();
        if (Owner.IsAlive)
        {
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power == this && Owner.IsAlive)
        {
            RefreshProjectedVigor();
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        if (oldOwner.IsAlive)
        {
            Sts2VersionSeam.RefreshMonsterIntentsUi(oldOwner);
        }
        return Task.CompletedTask;
    }

    protected override decimal OnModifyDamageAdditive(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (dealer != Owner || !props.IsPoweredAttack())
            return 0m;
        if (Owner.CombatState?.CurrentSide != CombatSide.Player)
            return 0m;

        return ThingsBowlbugProgenitor.CountLiveBowlbugs(Owner.CombatState) * Amount;
    }

    public override Task AfterCreatureAddedToCombat(Creature creature)
    {
        if (Owner.IsAlive && ThingsBowlbugProgenitor.IsBowlbugMinion(creature))
        {
            RefreshProjectedVigor();
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
        return Task.CompletedTask;
    }

    protected override Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (Owner.IsAlive && ThingsBowlbugProgenitor.IsBowlbugMonster(creature.Monster))
        {
            RefreshProjectedVigor();
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player || !Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        int totalVigor = ThingsBowlbugProgenitor.CountLiveBowlbugs(Owner.CombatState) * Amount;
        if (totalVigor > 0)
        {
            Flash();
        }

        await PowerCmd.Remove(this);

        if (totalVigor > 0 && Owner.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await PowerCmd.Apply<ThingsBowlbugVigorPower>(choiceContext, Owner, totalVigor, Owner, null);
        }
    }
}
