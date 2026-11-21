using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// One native counter displays shared column layers and height-dependent protection.
/// All damage negated by the top (100%) and middle (50%) layers is redirected to the bottom layer.
/// </summary>
public sealed class ThingsHumanFaceColumnPower : ThingsPowerModel
{
    private sealed class PendingRedirect(Creature bottomTarget, decimal amount, ValueProp props)
    {
        public Creature BottomTarget { get; } = bottomTarget;
        public decimal Amount { get; set; } = amount;
        public ValueProp Props { get; } = props;
    }

    private sealed class RedirectData
    {
        public bool IsBypassingReduction;
        public decimal StagedNegatedDamage;
        public ValueProp? StagedProps;
        public readonly Queue<PendingRedirect> PendingRedirects = new();
        public PendingRedirect? LastEnqueuedRedirect;
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool ShouldPlayVfx => false;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Stun)];

    protected override object InitInternalData() => new RedirectData();

    public void RefreshHeightDisplay() => InvokeDisplayAmountChanged();

    private int Level => IsMutable && Owner?.Monster is ThingsHumanFaceColumn column ? column.Level : 0;

    internal bool IsBypassingReduction
    {
        get => IsMutable && GetInternalData<RedirectData>().IsBypassingReduction;
        set
        {
            if (IsMutable)
                GetInternalData<RedirectData>().IsBypassingReduction = value;
        }
    }

    internal void StageModifyNegatedDamage(decimal negated, ValueProp props)
    {
        if (!IsMutable)
            return;
        RedirectData data = GetInternalData<RedirectData>();
        data.StagedNegatedDamage = Math.Max(0m, negated);
        data.StagedProps = props;
    }

    protected override string SmartDescriptionLocKey => !IsMutable ? base.SmartDescriptionLocKey :
        Level switch
        {
            2 => "THINGS_HUMAN_FACE_COLUMN_POWER.topDescription",
            1 => "THINGS_HUMAN_FACE_COLUMN_POWER.middleDescription",
            _ => "THINGS_HUMAN_FACE_COLUMN_POWER.bottomDescription",
        };

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || IsBypassingReduction)
            return 1m;
        return Level >= 2 ? 0m : Level == 1 ? .5m : 1m;
    }

    protected override decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || IsBypassingReduction)
            return decimal.MaxValue;
        return Level >= 2 ? 0m : decimal.MaxValue;
    }

    public override Task BeforeDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || !IsMutable)
            return Task.CompletedTask;

        RedirectData data = GetInternalData<RedirectData>();
        decimal staged = data.StagedNegatedDamage;
        ValueProp? stagedProps = data.StagedProps;
        data.StagedNegatedDamage = 0m;
        data.StagedProps = null;

        if (Level < 1 || Owner.Monster is not ThingsHumanFaceColumn column)
            return Task.CompletedTask;

        Creature? bottom = column.BottomLivingCreature;
        if (bottom == null || bottom == Owner)
            return Task.CompletedTask;

        var entry = new PendingRedirect(bottom, staged, stagedProps ?? props);
        data.PendingRedirects.Enqueue(entry);
        data.LastEnqueuedRedirect = entry;
        return Task.CompletedTask;
    }

    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || Level < 2)
            return amount;

        // If direct/unmodified HP loss reached AfterOsty without being negated in ModifyDamage,
        // fold the prevented amount into the active redirect entry for this hit.
        if (IsMutable && amount > 0m)
        {
            RedirectData data = GetInternalData<RedirectData>();
            if (data.LastEnqueuedRedirect != null && data.LastEnqueuedRedirect.Amount <= 0m)
                data.LastEnqueuedRedirect.Amount = decimal.Truncate(amount);
        }

        return 0m;
    }

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        if (target != Owner || !IsMutable)
            return;

        RedirectData data = GetInternalData<RedirectData>();
        if (!data.PendingRedirects.TryDequeue(out PendingRedirect? redirect))
            return;

        if (ReferenceEquals(data.LastEnqueuedRedirect, redirect))
            data.LastEnqueuedRedirect = null;

        if (redirect.Amount <= 0m || CombatManager.Instance.IsOverOrEnding)
            return;

        Creature bottom = redirect.BottomTarget;
        if (!bottom.IsAlive || bottom == Owner)
            return;

        Flash();
        ValueProp redirectProps = ValueProp.Unpowered | (redirect.Props & ValueProp.Unblockable);
        Creature? redirectDealer = dealer is { IsAlive: true } ? dealer : null;
        await CreatureCmd.Damage(choiceContext, bottom, redirect.Amount, redirectProps, redirectDealer, null, null);
    }

    protected override Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature == Owner && IsMutable)
        {
            RedirectData data = GetInternalData<RedirectData>();
            data.StagedNegatedDamage = 0m;
            data.PendingRedirects.Clear();
            data.LastEnqueuedRedirect = null;
        }
        return Task.CompletedTask;
    }
}
