using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Cards;

/// <summary>Presentation metadata only. Real costs use native end-of-turn modifiers.</summary>
public static class LanternBlindness
{
    internal sealed class DrawScopeState(Player? player, int previousDepth, DrawScopeState? previousScope)
    {
        public Player? Player { get; } = player;
        public int PreviousDepth { get; } = previousDepth;
        public DrawScopeState? PreviousScope { get; } = previousScope;
        public bool CallerRestored { get; set; }
        public List<CardModel>? PrimedCards { get; private set; }

        public void TrackPrimedCard(CardModel card)
        {
            PrimedCards ??= [];
            PrimedCards.Add(card);
        }
    }

    internal readonly record struct SuspendedDrawScope(int Depth, DrawScopeState? Scope);

    private sealed record MarkedCard(ICombatState Combat, int? RolledCost);
    private static readonly ConditionalWeakTable<CardModel, MarkedCard> Marks = new();
    // Draw animates the card before AfterCardDrawn runs. Pre-cover its visuals without
    // rolling costs or consuming charges; the native draw hook still owns gameplay.
    private static readonly ConditionalWeakTable<CardModel, MarkedCard> PendingDrawVeils = new();
    private static readonly HashSet<ThingsLanternBlindnessPower> ActivePowers = [];
    private static readonly AsyncLocal<int> DrawScopeDepth = new();
    private static readonly AsyncLocal<DrawScopeState?> CurrentDrawScope = new();
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

    internal static DrawScopeState EnterDrawScope(Player? player)
    {
        int previousDepth = DrawScopeDepth.Value;
        DrawScopeState? previousScope = CurrentDrawScope.Value;
        var scope = new DrawScopeState(player, previousDepth, previousScope);
        DrawScopeDepth.Value = previousDepth + 1;
        CurrentDrawScope.Value = scope;
        return scope;
    }

    internal static void RestoreCallerDrawScope(DrawScopeState scope)
    {
        if (scope.CallerRestored)
            return;
        scope.CallerRestored = true;
        DrawScopeDepth.Value = scope.PreviousDepth;
        CurrentDrawScope.Value = scope.PreviousScope;
    }

    internal static SuspendedDrawScope SuspendDrawScope()
    {
        var saved = new SuspendedDrawScope(DrawScopeDepth.Value, CurrentDrawScope.Value);
        if (saved.Depth > 0 || saved.Scope != null)
        {
            DrawScopeDepth.Value = 0;
            CurrentDrawScope.Value = null;
        }
        return saved;
    }

    internal static void ResumeDrawScope(SuspendedDrawScope saved)
    {
        if (saved.Depth > 0 || saved.Scope != null)
        {
            DrawScopeDepth.Value = saved.Depth;
            CurrentDrawScope.Value = saved.Scope;
        }
    }

    internal static async Task<IEnumerable<CardModel>> CompleteDrawScopeAsync(
        Task<IEnumerable<CardModel>> drawTask,
        DrawScopeState scope)
    {
        try
        {
            return await drawTask;
        }
        finally
        {
            CleanupUnconsumedPendingVeils(scope);
        }
    }

    internal static void CleanupUnconsumedPendingVeils(DrawScopeState scope)
    {
        List<CardModel>? primedCards = scope.PrimedCards;
        if (primedCards == null)
            return;

        for (int i = 0; i < primedCards.Count; i++)
        {
            CardModel card = primedCards[i];
            if (PendingDrawVeils.Remove(card))
            {
                DecrementClamped(ref _markCount);
                NBlindCardVeil.RefreshOnTable(card);
            }
        }
        primedCards.Clear();
    }

    private static bool TryGetMatchingDrawScope(Player? owner, out DrawScopeState scope)
    {
        if (DrawScopeDepth.Value > 0 &&
            CurrentDrawScope.Value is { } active &&
            ReferenceEquals(active.Player, owner))
        {
            scope = active;
            return true;
        }

        scope = null!;
        return false;
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
            !CombatManager.Instance.IsInProgress ||
            destination.Type != PileType.Hand ||
            (card.Pile?.Type != PileType.Draw && card.Pile?.Type != PileType.Discard) ||
            !card.IsMutable ||
            IsBlinded(card) ||
            !TryGetMatchingDrawScope(card.Owner, out DrawScopeState scope) ||
            card.Owner.Creature.GetPower<ThingsLanternBlindnessPower>() is not { Amount: > 0 } power ||
            !ReferenceEquals(power.Owner, card.Owner.Creature) ||
            card.CombatState is not { } combat)
        {
            return;
        }

        if (!PendingDrawVeils.Remove(card))
            _markCount++;
        PendingDrawVeils.Add(card, new MarkedCard(combat, null));
        scope.TrackPrimedCard(card);
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

// Scope genuine CardPileCmd.Draw calls (including DrawFromDiscardPatch) so tutoring cards
// that call CardPileCmd.Add(card, PileType.Hand) directly (Wish, Tutor, SecretTechnique, etc.)
// never prime a false Lantern Blindness veil.
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
    typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool))]
internal static class LanternBlindnessDrawScopePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player player, out LanternBlindness.DrawScopeState __state)
    {
        __state = LanternBlindness.EnterDrawScope(player);
    }

    private static void Postfix(ref Task<IEnumerable<CardModel>> __result, LanternBlindness.DrawScopeState __state)
    {
        LanternBlindness.RestoreCallerDrawScope(__state);
        if (__result == null || __result.IsCompleted)
        {
            LanternBlindness.CleanupUnconsumedPendingVeils(__state);
            return;
        }

        __result = LanternBlindness.CompleteDrawScopeAsync(__result, __state);
    }

    private static Exception? Finalizer(Exception? __exception, LanternBlindness.DrawScopeState __state)
    {
        LanternBlindness.RestoreCallerDrawScope(__state);
        if (__exception != null)
            LanternBlindness.CleanupUnconsumedPendingVeils(__state);
        return __exception;
    }
}

// Suspend the active draw scope while AfterCardDrawn hooks execute so a tutor triggered
// inside an on-draw hook does not inherit the enclosing draw scope (while nested draws still work).
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn),
    typeof(ICombatState), typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool))]
internal static class LanternBlindnessAfterCardDrawnScopePatch
{
    private static void Prefix(out LanternBlindness.SuspendedDrawScope __state)
    {
        __state = LanternBlindness.SuspendDrawScope();
    }

    private static Exception? Finalizer(Exception? __exception, LanternBlindness.SuspendedDrawScope __state)
    {
        LanternBlindness.ResumeDrawScope(__state);
        return __exception;
    }
}

// CardPileCmd.Draw awaits the entire Add animation before invoking AfterCardDrawn.
// Prime only genuine draw moves, ahead of NCard creation and its first frame.
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
