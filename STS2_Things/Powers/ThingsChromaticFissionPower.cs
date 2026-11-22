using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

/// <summary>Chromatic strain determining the color, moveset, and role of spawned Chromatic Slimes.</summary>
public enum ChromaticStrain
{
    Crimson,
    Amber,
    Azure,
}

/// <summary>
/// STS1 Slime-Boss-inspired half-HP interrupt fission for Large and Medium Chromatic Slimes.
/// <para>
/// <see cref="PowerModel.Amount"/> stores the exact HP threshold (<c>MaxHp / 2</c>). When the host survives
/// a hit that brings it to <c>&lt;= Amount</c> HP, it immediately interrupts its current intent into
/// <c>SPLIT_MOVE</c>, and on its turn splits into 2 smaller slimes that inherit its remaining HP:
/// </para>
/// <list type="bullet">
///   <item><description>Large crossed by Powered Attack pushing &gt;= 5 past threshold -&gt; Crimson + Azure</description></item>
///   <item><description>Large crossed by Powered Attack pushing &lt; 5 past threshold -&gt; Amber + Crimson</description></item>
///   <item><description>Large crossed by any non-Attack source -&gt; Azure + Amber</description></item>
///   <item><description>Medium crossed at &lt;= 50% HP -&gt; 2 same-color Smalls</description></item>
///   <item><description>Lethal burst from &gt; 50% straight to 0 HP skips splitting completely.</description></item>
/// </list>
/// </summary>
public sealed class ThingsChromaticFissionPower : ThingsPowerModel
{
    public const int OverkillThreshold = 5;

    private static readonly string[] MediumSlotOrder = ["med_1", "med_2", "small_1", "small_2", "small_3", "small_4"];
    private static readonly string[] LeftSmallSlotOrder = ["small_1", "small_2", "small_3", "small_4"];
    private static readonly string[] RightSmallSlotOrder = ["small_3", "small_4", "small_1", "small_2"];

    private bool _splitTriggered;
    private bool _splitExecuted;
    private ChromaticStrain? _primaryStrain;
    private ChromaticStrain? _secondaryStrain;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>
    /// Threshold equals <c>Owner.MaxHp / 2</c>, which is already scaled by multiplayer HP scaling.
    /// Must never scale a second time.
    /// </summary>
    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>
    /// Shows the half-HP split threshold while waiting, then tracks live inherited child HP once split is armed.
    /// </summary>
    public override int DisplayAmount =>
        IsMutable && _splitTriggered && Owner != null
            ? Math.Max(1, Owner.CurrentHp)
            : Amount;

    /// <summary>Exposed for deterministic test probes and telemetry without mutating combat state.</summary>
    internal bool IsSplitTriggered => _splitTriggered;
    internal bool IsSplitExecuted => _splitExecuted;
    internal ChromaticStrain? PrimaryStrain => _primaryStrain;
    internal ChromaticStrain? SecondaryStrain => _secondaryStrain;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("ExcessThreshold", OverkillThreshold),
        new IntVar("InheritedHp", 1),
        new StringVar("Child1", string.Empty),
        new StringVar("Child2", string.Empty),
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            yield return HoverTipFactory.Static(StaticHoverTip.Block);
            yield return HoverTipFactory.FromCard<MegaCrit.Sts2.Core.Models.Cards.Slimed>();
            yield return HoverTipFactory.FromPower<WeakPower>();
        }
    }

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (!IsMutable || Owner == null)
                return base.SmartDescriptionLocKey;

            RefreshPreviewVars();

            if (_splitTriggered)
            {
                return Owner.Monster is ThingsChromaticSlimeMediumBase
                    ? "THINGS_CHROMATIC_FISSION_POWER.pendingMediumSplitDescription"
                    : "THINGS_CHROMATIC_FISSION_POWER.pendingSplitDescription";
            }

            if (Owner.Monster is ThingsChromaticSlimeLarge)
                return "THINGS_CHROMATIC_FISSION_POWER.largeDescription";

            if (Owner.Monster is ThingsChromaticSlimeMediumBase)
                return "THINGS_CHROMATIC_FISSION_POWER.mediumDescription";

            return base.SmartDescriptionLocKey;
        }
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _splitTriggered = false;
        _splitExecuted = false;
        _primaryStrain = null;
        _secondaryStrain = null;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || !Owner.IsAlive || result.UnblockedDamage <= 0)
            return Task.CompletedTask;

        if (_splitTriggered)
        {
            RefreshPreviewVars();
            InvokeDisplayAmountChanged();
            return Task.CompletedTask;
        }

        if (Owner.CurrentHp > Amount)
            return Task.CompletedTask;

        bool isLarge = Owner.Monster is ThingsChromaticSlimeLarge;
        bool isMedium = Owner.Monster is ThingsChromaticSlimeMediumBase;
        if (!isLarge && !isMedium)
            return Task.CompletedTask;

        (_primaryStrain, _secondaryStrain) = isLarge
            ? ResolveLargePair(Amount - Owner.CurrentHp, props, cardSource)
            : ResolveMediumPair(Owner.Monster as ThingsChromaticSlimeBase);

        _splitTriggered = true;
        Flash();
        (Owner.Monster as ThingsChromaticSlimeBase)?.TriggerSplitIntent();
        RefreshPreviewVars();
        InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature == Owner && _splitTriggered && Owner.IsAlive)
        {
            RefreshPreviewVars();
            InvokeDisplayAmountChanged();
        }
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Player
            && _splitTriggered
            && !_splitExecuted
            && Owner is { IsAlive: true, Monster: ThingsChromaticSlimeBase slime }
            && !CombatManager.Instance.IsOverOrEnding
            && slime.NextMove?.Id is not (MonsterModel.stunnedMoveId or ThingsChromaticSlimeBase.SplitMoveId))
        {
            slime.TriggerSplitIntent();
            RefreshPreviewVars();
            InvokeDisplayAmountChanged();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the Enemy-Turn <c>SPLIT_MOVE</c>: plays the parent rupture animation, spawns 2 children
    /// inheriting the parent's remaining HP, launches per-child split arcs, and cleanly removes the parent
    /// without firing death hooks or escape penalties.
    /// </summary>
    internal async Task ExecuteSplitAsync(PlayerChoiceContext choiceContext)
    {
        if (_splitExecuted || !Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        bool spawnMediums = Owner.Monster is ThingsChromaticSlimeLarge;
        bool spawnSmalls = Owner.Monster is ThingsChromaticSlimeMediumBase;
        if (!spawnMediums && !spawnSmalls)
            return;

        _splitExecuted = true;

        Vector2? parentOrigin = CaptureCenterPosition(Owner);
        SfxCmd.Play("event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_slime");
        float burstDelay = spawnMediums ? 0.36f : 0.30f;
        await CreatureCmd.TriggerAnim(Owner, CreatureAnimator.deathTrigger, burstDelay);

        if (!Owner.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        int splitHp = Math.Max(1, Owner.CurrentHp);
        (ChromaticStrain primary, ChromaticStrain secondary) = ResolveExecutedPair(spawnMediums);
        ChromaticStrain[] childStrains = [primary, secondary];
        string[] candidateSlots = ResolveCandidateSlots(spawnMediums, Owner.SlotName);

        var spawnedEntries = new List<(Creature Child, string StrainKey)>(2);
        int spawnedIndex = 0;

        foreach (string slotName in candidateSlots)
        {
            if (spawnedIndex >= childStrains.Length)
                break;

            string? validSlot = CombatState.Encounter?.Slots.Contains(slotName) == true ? slotName : null;
            if (validSlot != null && CombatState.Enemies.Any(c => c.IsAlive && c.SlotName == validSlot))
                continue;

            ChromaticStrain childStrain = childStrains[spawnedIndex];
            ThingsChromaticSlimeBase childTemplate = CreateChildTemplate(spawnMediums, childStrain);
            childTemplate.StartMovePhase = spawnedIndex % 2;

            Creature child = CombatState.CreateCreature(childTemplate, Owner.Side, validSlot);
            child.SetMaxHpInternal(splitHp);
            child.SetCurrentHpInternal(splitHp);
            await CreatureCmd.Add(child);

            if (child.Monster is ThingsChromaticSlimeMediumBase && splitHp >= 2)
            {
                int childThreshold = splitHp / 2;
                await PowerCmd.Apply<ThingsChromaticFissionPower>(choiceContext, child, childThreshold, child, null);
            }

            if (child.Monster is ThingsChromaticSlimeBase slime && slime.OpeningBlock > 0)
                await CreatureCmd.GainBlock(child, slime.OpeningBlock, ValueProp.Move, null);

            spawnedEntries.Add((child, childStrain.ToString().ToLowerInvariant()));
            spawnedIndex++;
        }

        // Remove parent cleanly AFTER spawning children so combat never sees 0 enemies,
        // without triggering AfterDeath hooks or EscapedCreatures gold penalties.
        await Sts2VersionSeam.DislodgeImpaledArrowsAsync(Owner, choiceContext);
        Sts2VersionSeam.RemoveCreatureWithoutDeathOrEscape(Owner);

        if (spawnedEntries.Count > 0)
        {
            if (parentOrigin is { } origin)
                NChromaticSlimeSplitVfx.PlaySplitArcs(origin, spawnedEntries);

            foreach ((Creature child, _) in spawnedEntries)
            {
                if (child.IsAlive)
                    await CreatureCmd.TriggerAnim(child, "Spawn", 0f);
            }
        }
    }

    internal static (ChromaticStrain Primary, ChromaticStrain Secondary) ResolveLargePair(
        int excessPastThreshold,
        ValueProp props,
        CardModel? cardSource)
    {
        if (cardSource?.Type == CardType.Attack && props.IsPoweredAttack())
        {
            return excessPastThreshold >= OverkillThreshold
                ? (ChromaticStrain.Crimson, ChromaticStrain.Azure)
                : (ChromaticStrain.Amber, ChromaticStrain.Crimson);
        }

        return (ChromaticStrain.Azure, ChromaticStrain.Amber);
    }

    private static (ChromaticStrain Primary, ChromaticStrain Secondary) ResolveMediumPair(
        ThingsChromaticSlimeBase? slime)
    {
        ChromaticStrain own = slime?.OwnStrain ?? ChromaticStrain.Azure;
        return (own, own);
    }

    private (ChromaticStrain Primary, ChromaticStrain Secondary) ResolveExecutedPair(bool spawnMediums)
    {
        if (_primaryStrain is { } p && _secondaryStrain is { } s)
            return (p, s);

        return spawnMediums
            ? (ChromaticStrain.Azure, ChromaticStrain.Amber)
            : ResolveMediumPair(Owner.Monster as ThingsChromaticSlimeBase);
    }

    private void RefreshPreviewVars()
    {
        if (!IsMutable || Owner == null)
            return;

        DynamicVars["ExcessThreshold"].BaseValue = OverkillThreshold;
        DynamicVars["InheritedHp"].BaseValue = Math.Max(1, Owner.CurrentHp);

        bool spawnMediums = Owner.Monster is ThingsChromaticSlimeLarge;
        (ChromaticStrain primary, ChromaticStrain secondary) = _splitTriggered
            ? ResolveExecutedPair(spawnMediums)
            : ResolveMediumPair(Owner.Monster as ThingsChromaticSlimeBase);

        if (DynamicVars["Child1"] is StringVar child1Var)
            child1Var.StringValue = GetChildMonsterTitle(spawnMediums, primary);
        if (DynamicVars["Child2"] is StringVar child2Var)
            child2Var.StringValue = GetChildMonsterTitle(spawnMediums, secondary);
    }

    private static string GetChildMonsterTitle(bool spawnMediums, ChromaticStrain strain)
    {
        MonsterModel model = spawnMediums
            ? strain switch
            {
                ChromaticStrain.Crimson => ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>(),
                ChromaticStrain.Amber => ModelDb.Monster<ThingsChromaticSlimeMediumAmber>(),
                _ => ModelDb.Monster<ThingsChromaticSlimeMediumAzure>(),
            }
            : strain switch
            {
                ChromaticStrain.Crimson => ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>(),
                ChromaticStrain.Amber => ModelDb.Monster<ThingsChromaticSlimeSmallAmber>(),
                _ => ModelDb.Monster<ThingsChromaticSlimeSmallAzure>(),
            };
        return model.Title.GetFormattedText();
    }

    private static string[] ResolveCandidateSlots(bool spawnMediums, string? parentSlot)
    {
        if (spawnMediums)
            return MediumSlotOrder;
        return string.Equals(parentSlot, "med_1", StringComparison.Ordinal)
            ? LeftSmallSlotOrder
            : RightSmallSlotOrder;
    }

    private static ThingsChromaticSlimeBase CreateChildTemplate(bool spawnMediums, ChromaticStrain strain)
    {
        if (spawnMediums)
        {
            MonsterModel model = strain switch
            {
                ChromaticStrain.Crimson => ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>(),
                ChromaticStrain.Amber => ModelDb.Monster<ThingsChromaticSlimeMediumAmber>(),
                _ => ModelDb.Monster<ThingsChromaticSlimeMediumAzure>(),
            };
            return (ThingsChromaticSlimeBase)model.ToMutable();
        }

        MonsterModel smallModel = strain switch
        {
            ChromaticStrain.Crimson => ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>(),
            ChromaticStrain.Amber => ModelDb.Monster<ThingsChromaticSlimeSmallAmber>(),
            _ => ModelDb.Monster<ThingsChromaticSlimeSmallAzure>(),
        };
        return (ThingsChromaticSlimeBase)smallModel.ToMutable();
    }

    private static Vector2? CaptureCenterPosition(Creature creature)
    {
        var node = Sts2VersionSeam.GetCreatureNode(creature);
        if (node == null)
            return null;
        return node.Visuals?.GetNodeOrNull<Marker2D>("%CenterPos")?.GlobalPosition ?? node.VfxSpawnPosition;
    }
}
