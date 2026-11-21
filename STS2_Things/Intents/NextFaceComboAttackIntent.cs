using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using STS2_Things.Monsters;

namespace STS2_Things.Intents;

/// <summary>
/// Second attack intent displayed on post-break rotation turns (转向连击).
/// Dynamically previews the incoming next living stone face's follow-up attack using that next face's own
/// Strength / Weak modifiers so players can plan exact block before the wall whips 90°–270°.
/// </summary>
public sealed class NextFaceComboAttackIntent : AttackIntent
{
    private readonly ThingsHumanFaceTowerFace _currentFace;

    public NextFaceComboAttackIntent(ThingsHumanFaceTowerFace currentFace)
    {
        _currentFace = currentFace;
        DamageCalc = () => FollowUpFace?.ComboFollowUpDamage ?? _currentFace.ComboFollowUpDamage;
    }

    private ThingsHumanFaceTowerFace? FollowUpFace => _currentFace.PeekComboFollowUpFace();

    public override int Repeats => Math.Max(1, FollowUpFace?.ComboFollowUpHits ?? 1);

    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner)
    {
        Creature effectiveOwner = FollowUpFace?.Creature ?? owner;
        return GetSingleDamage(targets, effectiveOwner) * Repeats;
    }

    public override LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner)
    {
        Creature effectiveOwner = FollowUpFace?.Creature ?? owner;
        int singleDamage = GetSingleDamage(targets, effectiveOwner);
        int hits = Repeats;
        if (hits > 1)
        {
            var multi = new LocString("intents", "FORMAT_DAMAGE_MULTI");
            multi.Add("Damage", singleDamage);
            multi.Add("Repeat", hits);
            return multi;
        }

        var single = new LocString("intents", "FORMAT_DAMAGE_SINGLE");
        single.Add("Damage", singleDamage);
        return single;
    }

    protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
    {
        ThingsHumanFaceTowerFace? nextFace = FollowUpFace;
        Creature effectiveOwner = nextFace?.Creature ?? owner;
        int singleDamage = GetSingleDamage(targets, effectiveOwner);
        int hits = Repeats;
        string key = hits > 1
            ? "THINGS_TOWER_PIVOT_POWER.comboMultiIntentDescription"
            : "THINGS_TOWER_PIVOT_POWER.comboSingleIntentDescription";
        var description = new LocString("powers", key);
        description.Add("NextFace", nextFace?.Title.GetFormattedText() ?? owner.Name);
        description.Add("Damage", singleDamage);
        description.Add("Repeat", hits);
        return description;
    }
}
