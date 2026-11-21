using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Cards;

/// <summary>Presentation metadata only. Real costs use native end-of-turn modifiers.</summary>
public static class LanternBlindness
{
    private sealed record MarkedCard(ICombatState Combat, int? RolledCost);
    private static readonly ConditionalWeakTable<CardModel, MarkedCard> Marks = new();
    // Draw animates the card before AfterCardDrawn runs. Pre-cover its visuals without
    // rolling costs or consuming charges; the native draw hook still owns gameplay.
    private static readonly ConditionalWeakTable<CardModel, MarkedCard> PendingDrawVeils = new();
    private static readonly HashSet<ThingsLanternBlindnessPower> ActivePowers = [];
    private static int _markCount;

    internal static bool HasActivePower => ActivePowers.Count > 0;

    internal static bool HasMarkedCards => _markCount > 0;

    internal static bool HasPotentialState => _markCount > 0 || ActivePowers.Count > 0;

    internal static void TrackPowerApplied(ThingsLanternBlindnessPower power) => ActivePowers.Add(power);

    internal static void TrackPowerRemoved(ThingsLanternBlindnessPower power) => ActivePowers.Remove(power);

    internal static void ResetCombatTracking()
    {
        ActivePowers.Clear();
        _markCount = 0;
    }

    private static void DecrementClamped(ref int counter)
    {
        if (counter > 0)
            counter--;
    }

    public static bool IsVeiled(CardModel? card) =>
        _markCount > 0 &&
        (IsBlinded(card) ||
         (CombatManager.Instance.IsInProgress && card is { IsMutable: true } &&
          PendingDrawVeils.TryGetValue(card, out var pending) &&
          ReferenceEquals(card.CombatState, pending.Combat) && card.Pile?.Type == PileType.Hand));

    internal static void PrimeDrawVeil(CardModel card, CardPile destination)
    {
        if (ActivePowers.Count == 0 ||
            !CombatManager.Instance.IsInProgress || destination.Type != PileType.Hand ||
            card.Pile?.Type != PileType.Draw || !card.IsMutable || IsBlinded(card) ||
            card.Owner.Creature.GetPower<ThingsLanternBlindnessPower>() is not { Amount: > 0 } power ||
            !ReferenceEquals(power.Owner, card.Owner.Creature) || card.CombatState is not { } combat)
            return;

        if (!PendingDrawVeils.Remove(card))
            _markCount++;
        PendingDrawVeils.Add(card, new MarkedCard(combat, null));
    }

    public static bool IsBlinded(CardModel? card) =>
        _markCount > 0 &&
        CombatManager.Instance.IsInProgress && card is { IsMutable: true } &&
        Marks.TryGetValue(card, out var mark) && ReferenceEquals(card.CombatState, mark.Combat);

    public static void Mark(CardModel card, int? rolledCost)
    {
        if (card.CombatState is not { } combat) return;
        if (PendingDrawVeils.Remove(card))
            DecrementClamped(ref _markCount);
        if (!Marks.Remove(card))
            _markCount++;
        Marks.Add(card, new MarkedCard(combat, rolledCost));
    }

    public static void Forget(CardModel card) => _ = ForgetAndReport(card);

    internal static bool ForgetAndReport(CardModel card)
    {
        if (_markCount == 0)
            return false;

        bool removed = false;
        if (Marks.Remove(card))
        {
            DecrementClamped(ref _markCount);
            removed = true;
        }

        if (PendingDrawVeils.Remove(card))
        {
            DecrementClamped(ref _markCount);
            removed = true;
        }

        return removed;
    }

    internal static void Copy(CardModel source, CardModel clone)
    {
        if (_markCount == 0 || !IsBlinded(source) || !Marks.TryGetValue(source, out var mark))
            return;
        if (!Marks.Remove(clone))
            _markCount++;
        Marks.Add(clone, mark);
    }
}

// CardPileCmd.Draw awaits the entire Add animation before invoking AfterCardDrawn.
// Prime only draw-pile -> hand moves, ahead of NCard creation and its first frame.
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), typeof(CardModel), typeof(CardPile),
    typeof(CardPilePosition), typeof(AbstractModel), typeof(bool))]
internal static class LanternBlindnessDrawVisualPrimePatch
{
    private static void Prefix(CardModel card, CardPile newPile)
    {
        if (LanternBlindness.HasActivePower)
            LanternBlindness.PrimeDrawVeil(card, newPile);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.EndOfTurnCleanup))]
internal static class LanternBlindnessTurnCleanupPatch
{
    // Clear before the native cost-change event refreshes any visible card.
    private static void Prefix(CardModel __instance, out bool __state)
    {
        if (!LanternBlindness.HasMarkedCards)
        {
            __state = false;
            return;
        }
        __state = LanternBlindness.ForgetAndReport(__instance);
    }

    private static void Postfix(CardModel __instance, bool __state)
    {
        if (__state)
            NBlindCardVeil.RefreshOnTable(__instance);
    }
}

[HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.Clone))]
internal static class LanternBlindnessClonePatch
{
    private static void Postfix(CardModel ____card, CardModel newCard)
    {
        if (LanternBlindness.HasMarkedCards)
            LanternBlindness.Copy(____card, newCard);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.ApplyPowerInternal))]
internal static class LanternBlindnessPowerAppliedPatch
{
    private static void Postfix(PowerModel power)
    {
        if (power is ThingsLanternBlindnessPower blindness)
            LanternBlindness.TrackPowerApplied(blindness);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
internal static class LanternBlindnessPowerRemovedPatch
{
    private static void Postfix(PowerModel power)
    {
        if (power is ThingsLanternBlindnessPower blindness)
            LanternBlindness.TrackPowerRemoved(blindness);
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class LanternBlindnessCombatSetupPatch
{
    private static void Prefix() => LanternBlindness.ResetCombatTracking();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class LanternBlindnessCombatResetPatch
{
    private static void Postfix() => LanternBlindness.ResetCombatTracking();
}
