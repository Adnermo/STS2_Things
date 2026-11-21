using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Afflictions;
using STS2_Things.Monsters;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>Native history supplies the real card; native afflictions carry the visible offer.</summary>
public sealed class ThingsReverseCurrentPower : ThingsPowerModel
{
    private int _pendingCarryBlock;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new IntVar("StrengthAmount", 1), new IntVar("UnplayedBlock", 5)];

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _pendingCarryBlock = 0;
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        DynamicVars["StrengthAmount"].BaseValue = Owner.Monster is ThingsReverseSalamander monster ? monster.GrowthAmount : 1;
        DynamicVars["UnplayedBlock"].BaseValue = Owner.Monster is ThingsReverseSalamander m ? m.UnplayedRecallBlock : 5;
        return Task.CompletedTask;
    }
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [new HoverTip(new LocString("powers", "THINGS_REVERSE_CURRENT_POWER.refluxTitle"),
            new LocString("powers", "THINGS_REVERSE_CURRENT_POWER.refluxDescription")),
         HoverTipFactory.FromPower<StrengthPower>(),
         HoverTipFactory.Static(StaticHoverTip.Block),
         HoverTipFactory.FromKeyword(CardKeyword.Exhaust)];
    private bool IsReturnSource => Owner.IsAlive && CombatState.Enemies
        .FirstOrDefault(c=>c.IsAlive&&c.GetPower<ThingsReverseCurrentPower>()!=null)==Owner;

    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext,Player player)
    {
        if(!IsReturnSource||!player.Creature.IsAlive||player.PlayerCombatState==null)return;
        ClearFor(player);
        // Same history predicate as vanilla HistoryCourse. Drawing/autoplay at
        // the start of this turn cannot overwrite the previous turn's history.
        var card=CombatManager.Instance.History.CardPlaysFinished.LastOrDefault(e=>
            e.Actor==player.Creature&&e.CardPlay.Card.Owner==player&&e.HappenedLastPlayerTurn(player)&&!e.CardPlay.Card.IsDupe&&
            IsAvailable(e.CardPlay.Card))?.CardPlay.Card;
        if(card==null)return;
        if(card.Pile?.Type!=PileType.Hand&&player.PlayerCombatState.Hand.Cards.Count>=CardPile.MaxCardsInHand)return;
        Flash();
        if(Owner.Monster is ThingsReverseSalamander monster)await monster.RecallPose();
        if(!Owner.IsAlive||!IsAvailable(card))return;
        if(card.Pile?.Type!=PileType.Hand)await CardPileCmd.Add(card,PileType.Hand);
        if(card.Pile?.Type!=PileType.Hand||!Owner.IsAlive)return;
        await CardCmd.AfflictAndPreview<ThingsUpstreamRecall>([card],Amount,CardPreviewStyle.None);
        if(card.Affliction is not ThingsUpstreamRecall)return;
        // Native "this turn" convention: expires on use or at turn end.
        card.EnergyCost.AddThisTurnOrUntilPlayed(-1,reduceOnly:true);
        card.InvokeEnergyCostChanged();
    }

    private static bool IsAvailable(CardModel card) =>
        card.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard &&
        ModelDb.Affliction<ThingsUpstreamRecall>().CanAfflict(card);

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext,CardPlay cardPlay)
    {
        if(!IsReturnSource||cardPlay.Card.IsDupe||cardPlay.Card.Affliction is not ThingsUpstreamRecall)return;
        // Clearing all copies of the offer also limits the growth to once per
        // owning player per turn, without unsynchronized card-reference caches.
        ClearFor(cardPlay.Card.Owner);
        foreach(var enemy in CombatState.Enemies.Where(c=>c.IsAlive&&c.GetPower<ThingsReverseCurrentPower>()!=null).ToArray())
        {
            var current=enemy.GetPower<ThingsReverseCurrentPower>()!;current.Flash();
            await PowerCmd.Apply<StrengthPower>(choiceContext,enemy,current.Amount,enemy,null);
            if(enemy.IsAlive)await CreatureCmd.TriggerAnim(enemy,"PowerUp",.18f);
        }
    }

    public override Task AfterCardChangedPiles(CardModel card,PileType oldPileType,AbstractModel? clonedBy)
    {
        if(IsReturnSource&&card.Pile?.Type==PileType.Exhaust&&card.Affliction is ThingsUpstreamRecall)CardCmd.ClearAffliction(card);
        return Task.CompletedTask;
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext,CombatSide side,IEnumerable<Creature> participants)
    {
        if(!IsReturnSource||side!=CombatSide.Player)return;
        int unplayedOffers=0;
        foreach(var creature in participants)
        {
            if(creature.Player is not { } player)continue;
            if(creature.IsAlive&&player.PlayerCombatState?.AllCards.Any(c=>c.Affliction is ThingsUpstreamRecall)==true)
                unplayedOffers++;
            ClearFor(player);
        }
        for(int i=0;i<unplayedOffers;i++)
        {
            foreach(var enemy in CombatState.Enemies.Where(c=>c.IsAlive&&c.GetPower<ThingsReverseCurrentPower>()!=null).ToArray())
            {
                if(CombatManager.Instance.IsOverOrEnding)return;
                await enemy.GetPower<ThingsReverseCurrentPower>()!.GrantUnplayedRecallBlockAsync();
            }
        }
    }

    private async Task GrantUnplayedRecallBlockAsync()
    {
        int unplayedBlock=Owner.Monster is ThingsReverseSalamander m?m.UnplayedRecallBlock:DynamicVars["UnplayedBlock"].IntValue;
        Flash();
        decimal gained=await CreatureCmd.GainBlock(Owner,unplayedBlock,ValueProp.Unpowered,null);
        _pendingCarryBlock+=(int)gained;
    }

    public override Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,CombatSide side,IReadOnlyList<Creature> participants,ICombatState combatState)
    {
        if(side==CombatSide.Enemy&&participants.Contains(Owner))
            _pendingCarryBlock=Math.Clamp(_pendingCarryBlock,0,(int)Owner.Block);
        else if(side==CombatSide.Player)
            _pendingCarryBlock=0;
        return Task.CompletedTask;
    }

    public override async Task AfterBlockCleared(Creature creature)
    {
        if(creature!=Owner||_pendingCarryBlock<=0)return;
        int carry=Math.Max(0,_pendingCarryBlock-(int)Owner.Block);
        _pendingCarryBlock=0;
        if(carry>0&&Owner.IsAlive)
            await CreatureCmd.GainBlock(Owner,carry,ValueProp.Unpowered,null,fast:true);
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        _pendingCarryBlock=0;
        var state=oldOwner.CombatState;
        if(state!=null&&!state.Enemies.Any(c=>c.IsAlive&&c.GetPower<ThingsReverseCurrentPower>()!=null))
            foreach(var player in state.Players)ClearFor(player);
        return Task.CompletedTask;
    }

    private static void ClearFor(Player player)
    {
        foreach(var card in player.PlayerCombatState?.AllCards.ToArray()??[])
            if(card.Affliction is ThingsUpstreamRecall)CardCmd.ClearAffliction(card);
    }
}
