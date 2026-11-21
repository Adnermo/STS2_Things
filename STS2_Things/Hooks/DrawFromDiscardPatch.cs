using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using STS2_Things.Powers;

namespace STS2_Things.Hooks;

/// <summary>
/// Draws from the discard pile first while the player has <see cref="ThingsRecallPower"/>,
/// falling back to the draw pile (with native hand-full / empty-pile thought bubbles) once discard is exhausted.
/// </summary>
[HarmonyPatch]
public static class DrawFromDiscardPatch
{
    [HarmonyTargetMethod]
    public static MethodBase Target(HarmonyPatchType _, Harmony __)
    {
        return typeof(CardPileCmd).GetMethod(
            "Draw",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)],
            null) ?? throw new MissingMethodException(typeof(CardPileCmd).FullName, "Draw");
    }

    private static bool Prefix(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (player.Creature.GetPower<ThingsRecallPower>() == null)
            return true;

        var discardPile = PileType.Discard.GetPile(player);
        if (discardPile.Cards.Count == 0)
            return true;

        __result = DrawFromDiscardThenFallbackAsync(choiceContext, count, player, fromHandDraw);
        return false;
    }

    private static async Task<IEnumerable<CardModel>> DrawFromDiscardThenFallbackAsync(
        PlayerChoiceContext choiceContext,
        decimal count,
        Player player,
        bool fromHandDraw)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return Array.Empty<CardModel>();

        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return Array.Empty<CardModel>();

        if (!Hook.ShouldDraw(combatState, player, fromHandDraw, out var modifier))
        {
            if (modifier != null)
                await Hook.AfterPreventingDraw(combatState, modifier);
            return Array.Empty<CardModel>();
        }

        var result = new List<CardModel>();
        int drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
            return result;

        CardPile hand = PileType.Hand.GetPile(player);
        CardPile discardPile = PileType.Discard.GetPile(player);
        CardPile drawPile = PileType.Draw.GetPile(player);

        if (hand.Cards.Count >= CardPile.MaxCardsInHand)
        {
            CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player);
            return result;
        }

        for (int i = 0; i < drawsRequested; i++)
        {
            if (CombatManager.Instance.IsOverOrEnding)
                break;

            if (!CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player))
                break;

            CardModel? card = discardPile.Cards.Count > 0
                ? player.RunState.Rng.CombatCardSelection.NextItem(discardPile.Cards)
                : drawPile.Cards.FirstOrDefault();

            if (card == null || hand.Cards.Count >= CardPile.MaxCardsInHand)
                break;

            result.Add(card);
            await CardPileCmd.Add(card, hand);
            CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
            await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
            card.InvokeDrawn();
            NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);
        }

        return result;
    }

    private static bool CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(Player player)
    {
        if (PileType.Draw.GetPile(player).Cards.Count + PileType.Discard.GetPile(player).Cards.Count == 0)
        {
            ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
            return false;
        }
        if (PileType.Hand.GetPile(player).Cards.Count >= CardPile.MaxCardsInHand)
        {
            ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
            return false;
        }
        return true;
    }
}
