using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Cards;

/// <summary>
/// Full-deck blackout &amp; deterministic 0-3 energy-cost randomization state for 渊墨巨蛸's
/// 【墨浊覆牌】 (Ink Blackout) turn-wide debuff.
/// All energy rolls strictly consume <see cref="Player.RunState"/> <c>Rng.CombatEnergyCosts</c>
/// inside synchronized combat hooks (never UI hooks or <c>LocalContext.IsMe</c>) and track the
/// exact <see cref="LocalCostModifier"/> appended so severing a tentacle surgically restores
/// original costs without wiping unrelated modifiers (Bullet Time, Enlightenment, etc.).
/// </summary>
public static class AbyssalInkBlackout
{
    public const int MinRandomCost = 0;
    public const int MaxRandomCostInclusive = 3;

    private static readonly PileType[] DeterministicPileOrder =
    [
        PileType.Hand,
        PileType.Draw,
        PileType.Discard,
        PileType.Exhaust,
        PileType.Play,
    ];

    private sealed class InkedMark
    {
        public required ICombatState Combat { get; init; }
        public LocalCostModifier? CostModifier { get; set; }
        public int? RolledCost { get; set; }
        public bool PlayedSinceLastRoll { get; set; }
    }

    private static readonly ConditionalWeakTable<CardModel, InkedMark> Marks = new();
    private static readonly ConditionalWeakTable<CardModel, ICombatState> PendingDrawVeils = new();
    private static readonly ConditionalWeakTable<CardModel, ICombatState> WashedThisTurn = new();
    private static readonly HashSet<ThingsInkBlackoutPower> ActivePowers = [];
    private static int _markCount;
    private static int _washedCount;

    private static readonly AccessTools.FieldRef<CardEnergyCost, List<LocalCostModifier>> LocalModifiersRef =
        AccessTools.FieldRefAccess<CardEnergyCost, List<LocalCostModifier>>("_localModifiers");

    internal static bool HasActivePower => ActivePowers.Count > 0;

    internal static bool HasPotentialState => _markCount > 0 || ActivePowers.Count > 0;

    internal static bool HasAnyTrackedCards => _markCount > 0 || _washedCount > 0;

    internal static void TrackPowerApplied(ThingsInkBlackoutPower power) => ActivePowers.Add(power);

    internal static void TrackPowerRemoved(ThingsInkBlackoutPower power) => ActivePowers.Remove(power);

    internal static void ResetCombatTracking()
    {
        ActivePowers.Clear();
        _markCount = 0;
        _washedCount = 0;
    }

    private static void DecrementClamped(ref int counter)
    {
        if (counter > 0)
            counter--;
    }

    private static void SetMark(CardModel card, InkedMark mark)
    {
        if (!Marks.Remove(card))
            _markCount++;
        Marks.Add(card, mark);
    }

    private static void SetPendingDrawVeil(CardModel card, ICombatState combat)
    {
        if (!PendingDrawVeils.Remove(card))
            _markCount++;
        PendingDrawVeils.Add(card, combat);
    }

    private static void SetWashedThisTurn(CardModel card, ICombatState combat)
    {
        if (!WashedThisTurn.Remove(card))
            _washedCount++;
        WashedThisTurn.Add(card, combat);
    }

    private static bool RemovePendingDrawVeil(CardModel card)
    {
        if (!PendingDrawVeils.Remove(card))
            return false;
        DecrementClamped(ref _markCount);
        return true;
    }

    private static void RemoveWashedThisTurn(CardModel card)
    {
        if (WashedThisTurn.Remove(card))
            DecrementClamped(ref _washedCount);
    }

    public static bool IsWashedThisTurn(CardModel? card) =>
        _washedCount > 0 &&
        CombatManager.Instance.IsInProgress &&
        card is { IsMutable: true } &&
        WashedThisTurn.TryGetValue(card, out ICombatState? washedCombat) &&
        ReferenceEquals(card.CombatState, washedCombat);

    public static bool IsVeiled(CardModel? card)
    {
        if (!HasPotentialState || !CombatManager.Instance.IsInProgress || card is not { IsMutable: true })
            return false;

        if (_markCount > 0 && IsInked(card))
            return true;

        if (_washedCount > 0 && IsWashedThisTurn(card))
            return false;

        if (_markCount > 0 &&
            PendingDrawVeils.TryGetValue(card, out ICombatState? pendingCombat) &&
            ReferenceEquals(card.CombatState, pendingCombat))
        {
            return true;
        }

        // If the card's owner currently has active turn-wide Ink Blackout, cover immediately even
        // before AfterCardEnteredCombat / AfterCardDrawn runs (prevents 1-frame draw/create flicker).
        if (ActivePowers.Count > 0 &&
            card.Owner?.Creature?.GetPower<ThingsInkBlackoutPower>() is { IsActiveThisTurn: true, Amount: > 0 } power &&
            ReferenceEquals(power.Owner, card.Owner.Creature) &&
            card.CombatState != null &&
            card.Pile != null &&
            card.Pile.Type != PileType.Deck)
        {
            return true;
        }

        return false;
    }

    public static bool IsInked(CardModel? card) =>
        _markCount > 0 &&
        CombatManager.Instance.IsInProgress &&
        card is { IsMutable: true } &&
        Marks.TryGetValue(card, out InkedMark? mark) &&
        ReferenceEquals(card.CombatState, mark.Combat);

    public static int? GetRolledCost(CardModel? card) =>
        _markCount > 0 && card != null && Marks.TryGetValue(card, out InkedMark? mark) && ReferenceEquals(card.CombatState, mark.Combat)
            ? mark.RolledCost
            : null;

    public static bool WasPlayedSinceLastRoll(CardModel? card) =>
        _markCount > 0 && card != null && Marks.TryGetValue(card, out InkedMark? mark) &&
        ReferenceEquals(card.CombatState, mark.Combat) && mark.PlayedSinceLastRoll;

    public static void MarkPlayedThisTurn(CardModel? card)
    {
        if (_markCount > 0 && card != null && Marks.TryGetValue(card, out InkedMark? mark) && ReferenceEquals(card.CombatState, mark.Combat))
        {
            mark.PlayedSinceLastRoll = true;
        }
    }

    public static bool CanRandomizeEnergyCost(CardModel? card) =>
        card is { IsMutable: true } &&
        !card.EnergyCost.CostsX &&
        card.EnergyCost.GetWithModifiers(CostModifiers.None) >= 0 &&
        !card.Keywords.Contains(CardKeyword.Unplayable);

    internal static void PrimeDrawVeil(CardModel card, CardPile destination)
    {
        if (ActivePowers.Count == 0 ||
            !CombatManager.Instance.IsInProgress ||
            !card.IsMutable ||
            IsInked(card) ||
            IsWashedThisTurn(card) ||
            destination.Type == PileType.Deck ||
            card.Owner?.Creature?.GetPower<ThingsInkBlackoutPower>() is not { IsActiveThisTurn: true, Amount: > 0 } power ||
            !ReferenceEquals(power.Owner, card.Owner.Creature) ||
            card.CombatState is not { } combat)
        {
            return;
        }

        RemoveWashedThisTurn(card);
        SetPendingDrawVeil(card, combat);
    }

    /// <summary>
    /// Deterministically inks and cost-randomizes every card across all 5 combat piles of
    /// <paramref name="player"/> at the start of their blackened turn.
    /// </summary>
    public static int InkEntirePlayerDeck(Player? player)
    {
        if (player?.PlayerCombatState == null)
            return 0;

        int inkedCount = 0;
        foreach (CardModel card in EnumerateAllCombatCardsDeterministic(player))
        {
            if (InkCard(card, randomizeCost: true, rerollIfAlreadyInked: false))
            {
                inkedCount++;
            }
        }

        return inkedCount;
    }

    /// <summary>
    /// Marks a single combat card as inked and (when eligible) deterministically rolls a 0-3
    /// temporary energy cost using <c>RunState.Rng.CombatEnergyCosts</c>.
    /// </summary>
    public static bool InkCard(CardModel? card, bool randomizeCost = true, bool rerollIfAlreadyInked = false)
    {
        if (card is not { IsMutable: true } || card.CombatState is not { } combat)
            return false;

        RemovePendingDrawVeil(card);
        RemoveWashedThisTurn(card);

        if (Marks.TryGetValue(card, out InkedMark? existing) && ReferenceEquals(existing.Combat, combat))
        {
            if (!rerollIfAlreadyInked)
            {
                NInkBlackoutCardVeil.RefreshAllForModel(card);
                return false;
            }

            RemoveTrackedCostModifier(card, existing);
            existing.PlayedSinceLastRoll = false;
            if (randomizeCost)
            {
                RollAndApplyCost(card, existing);
            }
            NInkBlackoutCardVeil.RefreshAllForModel(card);
            return true;
        }

        var mark = new InkedMark
        {
            Combat = combat,
            PlayedSinceLastRoll = false,
        };
        SetMark(card, mark);

        if (randomizeCost)
        {
            RollAndApplyCost(card, mark);
        }

        NInkBlackoutCardVeil.RefreshAllForModel(card);
        return true;
    }

    /// <summary>
    /// Backwards-compatible alias for marking a card without changing cost randomization rules.
    /// </summary>
    public static void Mark(CardModel card) => InkCard(card, randomizeCost: true, rerollIfAlreadyInked: false);

    public static void Forget(CardModel card, bool restoreTrackedCost = false) =>
        _ = ForgetAndReport(card, restoreTrackedCost);

    internal static bool ForgetAndReport(CardModel card, bool restoreTrackedCost = false)
    {
        if (!HasAnyTrackedCards)
            return false;

        bool removedMark = false;
        if (Marks.TryGetValue(card, out InkedMark? mark))
        {
            if (restoreTrackedCost)
            {
                RemoveTrackedCostModifier(card, mark);
            }

            if (Marks.Remove(card))
            {
                DecrementClamped(ref _markCount);
                removedMark = true;
            }
        }

        if (RemovePendingDrawVeil(card))
        {
            removedMark = true;
        }

        RemoveWashedThisTurn(card);
        return removedMark;
    }

    /// <summary>
    /// Washes ALL blackened cards clean across EVERY combat pile of a single player (restoring
    /// original energy costs surgically without disturbing unrelated local cost modifiers) and
    /// triggers the diagonal seafoam wave-wash dissolve on visible cards.
    /// </summary>
    public static int WashPlayerCards(Player? player, bool playWaveWash = true)
    {
        if (player?.PlayerCombatState == null)
            return 0;

        List<CardModel>? washedCards = null;
        foreach (CardModel card in EnumerateAllCombatCardsDeterministic(player))
        {
            if (!ForgetAndReport(card, restoreTrackedCost: true))
                continue;

            washedCards ??= [];
            washedCards.Add(card);
        }

        if (playWaveWash)
        {
            NInkBlackoutCardVeil.WashAllActiveVeils(player);
        }

        if (washedCards != null)
        {
            foreach (CardModel card in washedCards)
            {
                card.InvokeEnergyCostChanged();
            }
        }

        if (!playWaveWash)
        {
            NInkBlackoutCardVeil.RefreshActiveVeils(player);
        }

        return washedCards?.Count ?? 0;
    }

    /// <summary>
    /// Immediately washes ALL blackened cards clean across every player's ENTIRE combat deck
    /// (Hand, Draw, Discard, Exhaust, Play) when a major bridge tentacle is severed or Boss dies.
    /// </summary>
    public static void WashAllCards(ICombatState? combatState)
    {
        if (combatState == null)
            return;

        List<CardModel>? washedCards = null;
        foreach (Player player in combatState.Players)
        {
            if (player.PlayerCombatState == null)
                continue;

            foreach (CardModel card in EnumerateAllCombatCardsDeterministic(player))
            {
                if (!ForgetAndReport(card, restoreTrackedCost: true))
                    continue;

                washedCards ??= [];
                washedCards.Add(card);
            }
        }

        NInkBlackoutCardVeil.WashAllActiveVeils();

        if (washedCards != null)
        {
            foreach (CardModel card in washedCards)
            {
                card.InvokeEnergyCostChanged();
            }
        }

        NAbyssalInkSprayOverlay.SetPersistentInkBorder(false, washClean: true);
    }

    /// <summary>
    /// Clears turn-end blackout marks across a player's entire combat deck cleanly.
    /// </summary>
    public static void ClearPlayerMarksAtTurnEnd(Player? player)
    {
        if (player?.PlayerCombatState == null)
            return;

        foreach (CardModel card in EnumerateAllCombatCardsDeterministic(player))
        {
            if (ForgetAndReport(card, restoreTrackedCost: true))
            {
                card.InvokeEnergyCostChanged();
            }
        }

        NInkBlackoutCardVeil.RefreshActiveVeils(player);
    }

    /// <summary>
    /// Washes 1 deterministically chosen blackened card clean in each player's hand and restores
    /// its randomized energy cost (triggered when a small bridge tendril is severed).
    /// </summary>
    public static int WashOneRandomCardPerPlayer(ICombatState? combatState, Rng? rng)
    {
        if (combatState == null)
            return 0;

        int washedCount = 0;
        foreach (Player player in combatState.Players)
        {
            CardPile? hand = PileType.Hand.GetPile(player);
            if (hand == null)
                continue;

            List<CardModel> veiled = hand.Cards.Where(IsInked).ToList();
            if (veiled.Count == 0)
                continue;

            int idx = rng != null ? rng.NextInt(veiled.Count) : 0;
            CardModel picked = veiled[idx];
            Forget(picked, restoreTrackedCost: true);
            if (picked.CombatState != null)
            {
                SetWashedThisTurn(picked, picked.CombatState);
            }
            NInkBlackoutCardVeil.WashAllForModel(picked);
            picked.InvokeEnergyCostChanged();
            washedCount++;
        }

        return washedCount;
    }

    internal static void Copy(CardModel source, CardModel clone)
    {
        if (!HasAnyTrackedCards)
            return;

        if (_markCount > 0 && PendingDrawVeils.TryGetValue(source, out ICombatState? pendingCombat))
        {
            SetPendingDrawVeil(clone, pendingCombat);
        }

        if (_washedCount > 0 && WashedThisTurn.TryGetValue(source, out ICombatState? washedCombat))
        {
            SetWashedThisTurn(clone, washedCombat);
        }

        if (!IsInked(source) || !Marks.TryGetValue(source, out InkedMark? srcMark))
            return;

        LocalCostModifier? clonedMod = null;
        if (srcMark.CostModifier != null)
        {
            List<LocalCostModifier> srcList = LocalModifiersRef(source.EnergyCost);
            List<LocalCostModifier> dstList = LocalModifiersRef(clone.EnergyCost);
            int idx = srcList.IndexOf(srcMark.CostModifier);
            if (idx >= 0 && idx < dstList.Count)
            {
                clonedMod = dstList[idx];
            }
        }

        SetMark(clone, new InkedMark
        {
            Combat = srcMark.Combat,
            CostModifier = clonedMod,
            RolledCost = srcMark.RolledCost,
            PlayedSinceLastRoll = srcMark.PlayedSinceLastRoll,
        });
    }

    private static IEnumerable<CardModel> EnumerateAllCombatCardsDeterministic(Player player)
    {
        // Deterministic pile ordering across all peers for multiplayer checksum safety.
        var seen = new HashSet<CardModel>();
        foreach (PileType pileType in DeterministicPileOrder)
        {
            CardPile? pile = pileType.GetPile(player);
            if (pile == null)
                continue;

            foreach (CardModel card in pile.Cards.ToList())
            {
                if (seen.Add(card))
                    yield return card;
            }
        }
    }

    private static void RollAndApplyCost(CardModel card, InkedMark mark)
    {
        if (!CanRandomizeEnergyCost(card))
        {
            mark.CostModifier = null;
            mark.RolledCost = null;
            return;
        }

        Rng? rng = card.Owner?.RunState?.Rng?.CombatEnergyCosts;
        int rolled = rng != null
            ? rng.NextInt(MinRandomCost, MaxRandomCostInclusive + 1)
            : MinRandomCost;

        List<LocalCostModifier> list = LocalModifiersRef(card.EnergyCost);
        int countBefore = list.Count;
        card.EnergyCost.SetThisTurn(rolled);
        if (list.Count > countBefore)
        {
            mark.CostModifier = list[^1];
            mark.RolledCost = rolled;
            card.InvokeEnergyCostChanged();
        }
    }

    private static void RemoveTrackedCostModifier(CardModel card, InkedMark mark)
    {
        if (mark.CostModifier == null)
            return;

        try
        {
            List<LocalCostModifier> list = LocalModifiersRef(card.EnergyCost);
            list.Remove(mark.CostModifier);
        }
        catch (Exception)
        {
            // Defensive: ignore if card energy state is already torn down.
        }
        finally
        {
            mark.CostModifier = null;
            mark.RolledCost = null;
        }
    }
}

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), typeof(CardModel), typeof(CardPile),
    typeof(CardPilePosition), typeof(AbstractModel), typeof(bool))]
internal static class AbyssalInkBlackoutDrawVisualPrimePatch
{
    private static void Prefix(CardModel card, CardPile newPile)
    {
        if (AbyssalInkBlackout.HasActivePower)
            AbyssalInkBlackout.PrimeDrawVeil(card, newPile);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.EndOfTurnCleanup))]
internal static class AbyssalInkBlackoutTurnCleanupPatch
{
    private static void Prefix(CardModel __instance, out bool __state)
    {
        if (!AbyssalInkBlackout.HasAnyTrackedCards)
        {
            __state = false;
            return;
        }
        __state = AbyssalInkBlackout.ForgetAndReport(__instance, restoreTrackedCost: false);
    }

    private static void Postfix(CardModel __instance, bool __state)
    {
        if (__state)
            NInkBlackoutCardVeil.RefreshAllForModel(__instance);
    }
}

[HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.Clone))]
internal static class AbyssalInkBlackoutClonePatch
{
    private static void Postfix(CardModel ____card, CardModel newCard)
    {
        if (AbyssalInkBlackout.HasAnyTrackedCards)
            AbyssalInkBlackout.Copy(____card, newCard);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.ApplyPowerInternal))]
internal static class AbyssalInkBlackoutPowerAppliedPatch
{
    private static void Postfix(PowerModel power)
    {
        if (power is ThingsInkBlackoutPower blackout)
            AbyssalInkBlackout.TrackPowerApplied(blackout);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
internal static class AbyssalInkBlackoutPowerRemovedPatch
{
    private static void Postfix(PowerModel power)
    {
        if (power is ThingsInkBlackoutPower blackout)
            AbyssalInkBlackout.TrackPowerRemoved(blackout);
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class AbyssalInkBlackoutCombatSetupPatch
{
    private static void Prefix() => AbyssalInkBlackout.ResetCombatTracking();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class AbyssalInkBlackoutCombatResetPatch
{
    private static void Postfix() => AbyssalInkBlackout.ResetCombatTracking();
}
