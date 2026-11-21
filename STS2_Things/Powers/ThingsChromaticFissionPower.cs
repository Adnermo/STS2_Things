using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

/// <summary>Defeat strain driving which child Chromatic Slimes bud when the host splits.</summary>
public enum ChromaticStrain
{
    Crimson,
    Amber,
    Azure,
}

/// <summary>
/// Evaluates the lethal defeat condition on Large &amp; Medium Chromatic Slimes and splits them on death:
/// <list type="bullet">
///   <item><description>Powered Attack card with OverkillDamage &gt;= 5 -&gt; Crimson (aggressive burst slimes)</description></item>
///   <item><description>Powered Attack card with OverkillDamage &lt; 5 -&gt; Amber (armored bastion slimes)</description></item>
///   <item><description>Any non-Attack kill (Skills, Poison, Doom, Orbs, Thorns, Relics, Potions) -&gt; Azure (corrosive disruptor slimes)</description></item>
/// </list>
/// </summary>
public sealed class ThingsChromaticFissionPower : ThingsPowerModel
{
    public const int OverkillThreshold = 5;

    private static readonly string[] MediumSlotOrder = ["med_1", "med_2", "small_1", "small_2", "small_3", "small_4"];
    private static readonly string[] LeftSmallSlotOrder = ["small_1", "small_2", "small_3", "small_4"];
    private static readonly string[] RightSmallSlotOrder = ["small_3", "small_4", "small_1", "small_2"];

    private ChromaticStrain? _lethalStrain;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>Exposed for deterministic test probes and telemetry without mutating combat state.</summary>
    internal ChromaticStrain? RecordedLethalStrain => _lethalStrain;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _lethalStrain = null;
    }

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            yield return HoverTipFactory.Static(StaticHoverTip.Block);
            yield return HoverTipFactory.FromCard<MegaCrit.Sts2.Core.Models.Cards.Slimed>();
            yield return HoverTipFactory.FromPower<WeakPower>();
        }
    }

    public override bool ShouldStopCombatFromEnding() => true;

    public override Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        if (target != Owner || !result.WasTargetKilled || _lethalStrain != null)
            return Task.CompletedTask;

        if (cardSource?.Type == CardType.Attack && props.IsPoweredAttack())
        {
            _lethalStrain = result.OverkillDamage >= OverkillThreshold
                ? ChromaticStrain.Crimson
                : ChromaticStrain.Amber;
        }
        else
        {
            _lethalStrain = ChromaticStrain.Azure;
        }

        return Task.CompletedTask;
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || target != Owner)
            return;

        bool spawnMediums = Owner.Monster is ThingsChromaticSlimeLarge;
        bool spawnSmalls = Owner.Monster is ThingsChromaticSlimeMediumBase;
        if (!spawnMediums && !spawnSmalls)
            return;

        ChromaticStrain strain = _lethalStrain ?? ChromaticStrain.Azure;
        int splitCount = Math.Clamp(Amount, 1, 2);
        string[] candidateSlots = ResolveCandidateSlots(spawnMediums, Owner.SlotName);
        Vector2? parentOrigin = CaptureCenterPosition(Owner);

        var spawnedChildren = new List<Creature>(splitCount);
        int spawnedIndex = 0;

        foreach (string slotName in candidateSlots)
        {
            if (spawnedIndex >= splitCount)
                break;
            string? validSlot = CombatState.Encounter?.Slots.Contains(slotName) == true ? slotName : null;
            if (validSlot != null && CombatState.Enemies.Any(c => c.IsAlive && c.SlotName == validSlot))
                continue;

            ThingsChromaticSlimeBase childTemplate = CreateChildTemplate(spawnMediums, strain);
            childTemplate.StartMovePhase = spawnedIndex % 2;

            Creature child = await CreatureCmd.Add(childTemplate, CombatState, Owner.Side, validSlot);
            if (child.Monster is ThingsChromaticSlimeMediumBase)
                await PowerCmd.Apply<ThingsChromaticFissionPower>(choiceContext, child, 2m, child, null);
            if (child.Monster is ThingsChromaticSlimeBase slime && slime.OpeningBlock > 0)
                await CreatureCmd.GainBlock(child, slime.OpeningBlock, ValueProp.Move, null);

            Sts2VersionSeam.SetCreatureNodeVisible(child, visible: false);
            spawnedChildren.Add(child);
            spawnedIndex++;
        }

        if (spawnedChildren.Count > 0)
        {
            string strainKey = strain.ToString().ToLowerInvariant();
            _ = TaskHelper.RunSafely(RevealSplitWaveAsync(spawnedChildren, parentOrigin, strainKey, deathAnimLength));
        }
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

    private static async Task RevealSplitWaveAsync(
        IReadOnlyList<Creature> children,
        Vector2? parentOrigin,
        string strainKey,
        float deathAnimLength)
    {
        await Cmd.CustomScaledWait(deathAnimLength, deathAnimLength);

        var revealed = new List<Creature>(children.Count);
        foreach (Creature child in children)
        {
            if (!child.IsAlive)
                continue;

            Sts2VersionSeam.SetCreatureNodeVisible(child, visible: true);
            revealed.Add(child);
        }

        if (revealed.Count == 0)
            return;

        SfxCmd.Play("event:/sfx/enemy/enemy_attacks/slimed_berserker/slimed_berserker_slime");
        if (parentOrigin is { } origin)
            NChromaticSlimeSplitVfx.PlaySplitArcs(origin, revealed, strainKey);

        foreach (Creature child in revealed)
            await CreatureCmd.TriggerAnim(child, "Spawn", 0f);
    }
}
