using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Cards;
using STS2_Things.Monsters;
using STS2_Things.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Powers;

public sealed class ThingsLeechInfestationPower : ThingsPowerModel
{
    private sealed class Data
    {
        public readonly HashSet<ulong> ExhaustedPlayersSinceInfest = [];
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override bool IsVisibleInternal => false;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<ThingsLeechParasite>()];

    protected override object InitInternalData() => new Data();

    internal void ResetExtendedExhaustTracking()
    {
        if (IsMutable)
            GetInternalData<Data>().ExhaustedPlayersSinceInfest.Clear();
    }

    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if (card is ThingsLeechParasite { SuppressReinfestOnExhaust: false } parasite &&
            parasite.OwnerLeech == Owner && card.CombatState == CombatState && Owner.IsAlive &&
            !CombatManager.Instance.IsOverOrEnding && Owner.Monster is ThingsSanguineLeech leech)
        {
            if (!MultiplayerCompat.IsExtendedParty(CombatState))
            {
                leech.RequestReinfestation();
                return Task.CompletedTask;
            }

            Data data = GetInternalData<Data>();
            if (parasite.Owner != null)
                data.ExhaustedPlayersSinceInfest.Add(parasite.Owner.NetId);

            int alivePlayers = CombatState.Players.Count(p => p.Creature.IsAlive);
            int threshold = Math.Max(2, (alivePlayers + 1) / 2);
            if (data.ExhaustedPlayersSinceInfest.Count >= threshold || !AnyLivingPlayerHoldsBoundParasite(card))
            {
                data.ExhaustedPlayersSinceInfest.Clear();
                leech.RequestReinfestation();
            }
        }
        return Task.CompletedTask;
    }

    private bool AnyLivingPlayerHoldsBoundParasite(CardModel exhaustedCard)
    {
        foreach (Player player in CombatState.Players)
        {
            if (!player.Creature.IsAlive || player.PlayerCombatState == null)
                continue;

            foreach (CardModel candidate in CardPile.GetCards(player, PileType.Hand, PileType.Draw, PileType.Discard))
            {
                if (!ReferenceEquals(candidate, exhaustedCard) &&
                    candidate is ThingsLeechParasite other &&
                    other.OwnerLeech == Owner)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
