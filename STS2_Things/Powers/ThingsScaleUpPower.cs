using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
///     放大 — Boss Buff，每层放大 1%，伤害增 1%
/// </summary>
public sealed class ThingsScaleUpPower : ThingsPowerModel
{
    private const double ScaleTweenDuration = 0.75;
    private const float MinIntentContainerGlobalY = 165f;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new[] { new DynamicVar("PercentPerStack", 1m) };

    public float VisualScaleFactor =>
        Amount <= 0 ? 1f : 1f + 0.32f * (1f - MathF.Exp(-Amount / 35f));

    protected override void AfterCloned()
    {
        base.AfterCloned();
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount,
        Creature? applier, CardModel? cardSource)
    {
        if (power == this) ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        // 每回合开始时重新应用缩放，确保视觉与层数同步
        ApplyScale();
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        SafeScale(oldOwner, 1f);
        return Task.CompletedTask;
    }

    private void ApplyScale()
    {
        SafeScale(Owner, VisualScaleFactor);
    }

    private void SafeScale(Creature creature, float target)
    {
        try
        {
            NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (node == null || !GodotObject.IsInstanceValid(node))
                return;

            node.ScaleTo(target, ScaleTweenDuration);
        }
        catch (ObjectDisposedException)
        {
            Log.Warn("[ThingsScaleUpPower] 尝试缩放已释放的战斗节点，忽略。");
        }
        catch (NullReferenceException)
        {
            // NCombatRoom 或节点可能已被释放，忽略。
        }
    }

    internal static void SyncIntentPositionForNode(NCreature node, float target, double duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        if (node.Entity is not { IsMonster: true, Monster: ThingsScaleBeetle monster } || !monster.CanChangeScale)
            return;

        Control? intentContainer = node.IntentContainer;
        Marker2D? intentPos = node.Visuals?.IntentPosition;
        if (intentContainer == null || !GodotObject.IsInstanceValid(intentContainer) ||
            intentPos == null || !GodotObject.IsInstanceValid(intentPos))
            return;

        float defaultScale = node.Visuals?.DefaultScale ?? 1f;
        float baseLocalY = intentPos.Position.Y - intentContainer.Size.Y * 0.5f;
        float desiredLocalY = baseLocalY * (target * defaultScale);

        float scaleY = node.GetGlobalTransform().Scale.Y;
        if (scaleY <= 0.001f)
            scaleY = 1f;

        float minLocalY = (MinIntentContainerGlobalY - node.GlobalPosition.Y) / scaleY;
        float targetLocalY = MathF.Max(desiredLocalY, minLocalY);

        if (Mathf.IsEqualApprox(intentContainer.Position.Y, targetLocalY))
            return;

        if (duration <= 0.0)
        {
            intentContainer.Position = new Vector2(intentContainer.Position.X, targetLocalY);
            return;
        }

        Tween tween = node.CreateTween();
        tween
            .TweenProperty(intentContainer, "position:y", targetLocalY, duration)
            .SetEase(Tween.EaseType.InOut)
            .SetTrans(Tween.TransitionType.Sine);
    }

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (Owner != dealer || !props.IsPoweredAttack())
            return 1m;
        return (100m + DynamicVars["PercentPerStack"].BaseValue * Amount) / 100m;
    }
}
