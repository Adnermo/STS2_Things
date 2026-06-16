using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

/// <summary>
/// 当玩家拥有 DrawFromDiscardPower 时，优先从弃牌堆抽牌，弃牌堆为空时回退到抽牌堆
/// </summary>
[HarmonyPatch]
public static class DrawFromDiscardPatch
{
    [HarmonyTargetMethod]
    public static MethodBase Target(HarmonyPatchType _, Harmony __)
    {
        return typeof(CardPileCmd).GetMethod("Draw",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(decimal),
                typeof(Player),
                typeof(bool)
            },
            null);
    }

    /// <summary>
    /// 当玩家拥有 DrawFromDiscardPower 时，优先从弃牌堆抽牌；
    /// 弃牌堆为空时回退到原始抽牌逻辑
    /// </summary>
    private static bool Prefix(PlayerChoiceContext choiceContext, decimal count, Player player,
        bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
    {
        var power = player.Creature.Powers.FirstOrDefault(p => p is RecallPower);
        if (power == null)
            return true; // 正常抽牌

        var discardPile = PileType.Discard.GetPile(player);
        if (discardPile.Cards.Count == 0)
            return true; // 弃牌堆为空，回退到正常抽牌

        __result = DrawFromDiscardThenFallback(choiceContext, count, player, fromHandDraw);
        return false;
    }

    private static async Task<IEnumerable<CardModel>> DrawFromDiscardThenFallback(
        PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw)
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null || !combatState.IsLiveCombat())
            return Array.Empty<CardModel>();

        CardPile hand = PileType.Hand.GetPile(player);
        CardPile discardPile = PileType.Discard.GetPile(player);
        int drawsRequested = count > 0m ? (int)count : 0;
        var result = new List<CardModel>();

        for (int i = 0; i < drawsRequested; i++)
        {
            if (hand.Cards.Count >= CardPile.MaxCardsInHand)
                break;

            if (discardPile.Cards.Count > 0)
            {
                // 优先从弃牌堆随机抽一张
                var card = discardPile.Cards.ElementAtOrDefault(
                    player.RunState.Rng.CombatCardGeneration.NextInt(discardPile.Cards.Count));
                if (card == null) continue;

                result.Add(card);
                await CardPileCmd.Add(card, hand);
            }
            else
            {
                // 弃牌堆已空，回退到正常抽牌堆抽剩余的牌
                int remaining = drawsRequested - i;
                if (remaining > 0)
                {
                    var fallback = await CardPileCmd.Draw(choiceContext, remaining, player, fromHandDraw);
                    result.AddRange(fallback);
                }
                break;
            }
        }

        return result;
    }
}
