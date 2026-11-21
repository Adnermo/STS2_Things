using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for all <c>STS2_Things</c> monsters.
/// Unifies <c>AfterDeath</c>, <c>ModifyDamage*</c>, <c>IsInteractable</c>/<c>IsHealthBarVisible</c>,
/// <c>creature_visuals</c> slug resolution, deterministic combat beat waits, and safe debuff clearing.
/// </summary>
public abstract class ThingsMonsterModel : MonsterModel
{
    private string? _cachedAssetSlug;

    protected virtual string AssetSlug
    {
        get
        {
            if (_cachedAssetSlug != null)
                return _cachedAssetSlug;

            string fullSlug = Id.Entry.ToLowerInvariant();
            if (!fullSlug.StartsWith("things_", StringComparison.Ordinal))
                return _cachedAssetSlug = fullSlug;

            if (ResourceLoader.Exists(SceneHelper.GetScenePath("creature_visuals/" + fullSlug)))
                return _cachedAssetSlug = fullSlug;

            string bareSlug = fullSlug["things_".Length..];
            return _cachedAssetSlug = ResourceLoader.Exists(SceneHelper.GetScenePath("creature_visuals/" + bareSlug))
                ? bareSlug
                : fullSlug;
        }
    }

    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + AssetSlug);

    protected virtual bool IsMonsterInteractable => true;

#if STS2_V113_1
    public sealed override bool IsInteractable => IsMonsterInteractable;
    public bool IsHealthBarVisible => IsMonsterInteractable;
#else
    public sealed override bool IsHealthBarVisible => IsMonsterInteractable;
#endif

    protected virtual void OnBeforeAnimPlayedInCompendium(string id) { }

#if STS2_V113_1
    public override void BeforeAnimPlayedInCompendium(string id)
    {
        base.BeforeAnimPlayedInCompendium(id);
        OnBeforeAnimPlayedInCompendium(id);
    }
#endif

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
        => BaseAfterDeathAsync(choiceContext, creature, source, wasRemovalPrevented, deathAnimLength);

    protected Task BaseAfterDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
#if STS2_V113_1
        return base.AfterDeath(choiceContext, creature, source, wasRemovalPrevented, deathAnimLength);
#else
        _ = source;
        return base.AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength);
#endif
    }

    public sealed override decimal ModifyDamageAdditive(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageAdditive(target, amount, props, dealer, cardSource, cardPlay);

    public sealed override decimal ModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageMultiplicative(target, amount, props, dealer, cardSource, cardPlay);

    public sealed override decimal ModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => OnModifyDamageCap(target, props, dealer, cardSource, cardPlay);

    protected virtual decimal OnModifyDamageAdditive(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => 0m;

    protected virtual decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => 1m;

    protected virtual decimal OnModifyDamageCap(
        Creature? target, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => decimal.MaxValue;

    protected static Task WaitForCombatBeatAsync(float fixedSeconds, bool ignoreCombatEnd = false)
        => Cmd.Wait(Math.Max(0f, fixedSeconds), ignoreCombatEnd);

    protected async Task WaitForSpineMomentAsync(string animation, float moment, float headlessFallback)
    {
        var sprite = (Sts2VersionSeam.GetCreatureNode(Creature) ?? Creature.GetCreatureNode())?.Visuals?.SpineBody;
        float waitSeconds = headlessFallback;
        if (sprite != null && GodotObject.IsInstanceValid(sprite.BoundObject))
        {
            using (Sts2VersionSeam.TrackEntryScope(sprite.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? before))
            {
                if (before != null && before.GetAnimationName() == animation)
                {
                    waitSeconds = MathF.Max(0f, moment - before.GetTrackTime());
                }
            }
        }

        await WaitForCombatBeatAsync(waitSeconds);

        if (sprite != null && GodotObject.IsInstanceValid(sprite.BoundObject))
        {
            using (Sts2VersionSeam.TrackEntryScope(sprite.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? current))
            {
                if (current != null && current.GetAnimationName() == animation && current.GetTrackTime() < moment)
                {
                    current.SetMixDuration(0f);
                    current.SetTrackTime(moment);
                    sprite.BoundObject.Call("update_skeleton", 0f);
                }
            }
        }
    }

    /// <summary>
    /// Safely removes debuffs from <paramref name="target"/>, refunding paired negative
    /// <see cref="TemporaryStrengthPower"/> first, matching <see cref="PowerModel.TypeForCurrentAmount"/>,
    /// and zeroing <see cref="DoomPower"/> stacks before removal so queued turn listeners cannot
    /// treat a removed Doom instance as active.
    /// </summary>
    public static async Task ClearDebuffsSafelyAsync(Creature target)
    {
        foreach (TemporaryStrengthPower temporary in target.Powers
                     .OfType<TemporaryStrengthPower>()
                     .Where(p => p.TypeForCurrentAmount == PowerType.Debuff)
                     .ToList())
        {
            int refund = temporary.Amount;
            temporary.SetAmount(0, silent: true);
            if (refund != 0 && target.GetPower<StrengthPower>() is { } strength)
            {
                await PowerCmd.ModifyAmount(
                    new ThrowingPlayerChoiceContext(),
                    strength,
                    refund,
                    target,
                    null,
                    silent: true);
            }
            if (target.Powers.Contains(temporary))
                await PowerCmd.Remove(temporary);
        }

        foreach (PowerModel debuff in target.Powers
                     .Where(p => p.TypeForCurrentAmount == PowerType.Debuff)
                     .ToList())
        {
            if (!target.Powers.Contains(debuff))
                continue;
            if (debuff is DoomPower)
                debuff.SetAmount(0, silent: true);
            await PowerCmd.Remove(debuff);
        }
    }
}
