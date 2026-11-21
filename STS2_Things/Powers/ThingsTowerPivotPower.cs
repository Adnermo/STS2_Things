using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
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
/// Shared core mechanism power for 人面塔 (The Visage Tower): 【塔枢轮转】.
/// Tracks the 2-turn stay countdown, 8-turn full-cycle resonance countdown (while all 4 faces live),
/// post-break rotational 2-hit combo preview, and complete damage immunity for rotated-away faces.
/// </summary>
public sealed class ThingsTowerPivotPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool ShouldPlayVfx => false;

    private ThingsHumanFaceTowerFace? OwnerFace =>
        IsMutable ? Owner?.Monster as ThingsHumanFaceTowerFace : null;
    private bool IsOwnerActive => OwnerFace?.IsActiveFace ?? true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("CycleRemaining", HumanFaceTowerController.FullCycleActiveTurns),
        new StringVar("NextFace", string.Empty)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Stun)
    ];

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (!IsMutable || OwnerFace?.Controller is not { } ctrl)
                return base.SmartDescriptionLocKey;

            RefreshDynamicVars(ctrl);
            if (!OwnerFace.IsActiveFace)
                return "THINGS_TOWER_PIVOT_POWER.dormantDescription";
            if (ctrl.LivingCount <= 1)
                return "THINGS_TOWER_PIVOT_POWER.finalStandDescription";
            if (ctrl.LivingCount == 4)
                return "THINGS_TOWER_PIVOT_POWER.activeCycleDescription";
            return "THINGS_TOWER_PIVOT_POWER.activeComboDescription";
        }
    }

    public void RefreshPivotDisplay()
    {
        if (IsMutable && OwnerFace?.Controller is { } ctrl)
            RefreshDynamicVars(ctrl);
        InvokeDisplayAmountChanged();
    }

    private void RefreshDynamicVars(HumanFaceTowerController ctrl)
    {
        DynamicVars["CycleRemaining"].BaseValue = ctrl.TurnsUntilFullCycleClimax;
        if (DynamicVars["NextFace"] is StringVar nextVar)
        {
            nextVar.StringValue = ctrl.PeekNextLivingFace(OwnerFace)?.Title.GetFormattedText() ?? string.Empty;
        }
    }

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target == Owner && !IsOwnerActive ? 0m : 1m;

    protected override decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => target == Owner && !IsOwnerActive ? 0m : decimal.MaxValue;

    public override decimal ModifyHpLostAfterOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => target == Owner && !IsOwnerActive ? 0m : amount;

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || OwnerFace is not { Controller: { } ctrl } face)
            return;

        await ctrl.OnEnemySideTurnEndedAsync(face, choiceContext);
    }
}
