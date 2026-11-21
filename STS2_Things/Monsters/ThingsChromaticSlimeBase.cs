using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Core.Models;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
/// Shared Spine animator, slime SFX damage typing, and split phase tracking for all Chromatic Slime tiers.
/// </summary>
public abstract class ThingsChromaticSlimeBase : ThingsSpineMonster
{
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Slime;

    /// <summary>True while this slime is alive and combat is still running.</summary>
    protected bool CanAct => Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

    /// <summary>Determines staggered move cycle offset for pairs spawned from a split.</summary>
    public int StartMovePhase { get; set; }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        StartMovePhase = 0;
    }

    /// <summary>Optional initial Block granted immediately when spawned (used by Amber strains).</summary>
    internal virtual int OpeningBlock => 0;

    /// <summary>Optional strain-bespoke Spine animation overrides when present on the shared multi-skin skeleton.</summary>
    protected virtual string? PreferredAttackAnimation => null;
    protected virtual string? PreferredCastAnimation => null;
    protected virtual string? PreferredPowerUpAnimation => null;

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var idle = new AnimState("idle_loop", isLooping: true);
        var attack = ReturnToIdle(ResolveAnimation(controller, PreferredAttackAnimation, "attack"), idle);
        var cast = ReturnToIdle(ResolveAnimation(controller, PreferredCastAnimation, "cast"), idle);
        var hurt = ReturnToIdle("hurt", idle);
        var powerUp = ReturnToIdle(ResolveAnimation(controller, PreferredPowerUpAnimation, "power_up"), idle);
        var revive = ReturnToIdle("revive", idle);
        var die = new AnimState("die");

        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState(CreatureAnimator.idleTrigger, idle);
        animator.AddAnyState(CreatureAnimator.attackTrigger, attack);
        animator.AddAnyState(CreatureAnimator.castTrigger, cast);
        animator.AddAnyState(CreatureAnimator.hitTrigger, hurt);
        animator.AddAnyState(CreatureAnimator.deathTrigger, die);
        animator.AddAnyState(CreatureAnimator.powerUpTrigger, powerUp);
        animator.AddAnyState(CreatureAnimator.reviveTrigger, revive);

        if (controller.HasAnimation("summon"))
            animator.AddAnyState("Summon", ReturnToIdle("summon", idle));
        if (controller.HasAnimation("spawn"))
            animator.AddAnyState("Spawn", ReturnToIdle("spawn", idle));

        AddExtraAnimationStates(animator, idle);
        return animator;
    }

    private static AnimState ReturnToIdle(string animationName, AnimState idle) =>
        new(animationName) { NextState = idle };

    private static string ResolveAnimation(MegaSprite controller, string? preferred, string fallback) =>
        !string.IsNullOrEmpty(preferred) && controller.HasAnimation(preferred) ? preferred : fallback;
}

/// <summary>Shared base for the three Medium Chromatic Slime split variants (<c>Crimson</c>, <c>Amber</c>, <c>Azure</c>).</summary>
public abstract class ThingsChromaticSlimeMediumBase : ThingsChromaticSlimeBase
{
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/leaf_slime_m/leaf_slime_m_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/leaf_slime_m/leaf_slime_m_cast";
    public override string HurtSfx => "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_slime";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/leaf_slime_m/leaf_slime_m_die";

    public override IEnumerable<string> AssetPaths => base.AssetPaths
        .Concat(ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>().AssetPaths)
        .Concat(ModelDb.Monster<ThingsChromaticSlimeSmallAmber>().AssetPaths)
        .Concat(ModelDb.Monster<ThingsChromaticSlimeSmallAzure>().AssetPaths)
        .Append(ModelDb.Power<ThingsChromaticFissionPower>().ResolvedBigIconPath)
        .Distinct();
}

/// <summary>Shared base for the three terminal Small Chromatic Slimelets.</summary>
public abstract class ThingsChromaticSlimeSmallBase : ThingsChromaticSlimeBase
{
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/leaf_slime_s/leaf_slime_s_attack";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/twig_slime_s/twig_slime_s_cast";
    public override string HurtSfx => "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_slime";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/leaf_slime_s/leaf_slime_s_die";
}
