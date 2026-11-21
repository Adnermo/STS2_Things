using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

/// <summary>
/// 诡笑面 passive: 【戏谑反讽】.
/// Tracks the previous CardType played by each player during the Player Turn while Mockery is active.
/// Playing two consecutive cards of the same CardType triggers Mockery: gains 4(5) Block and +1 permanent
/// Strength. Alternating CardTypes never triggers it.
/// </summary>
public sealed class ThingsMockeryEchoPower : ThingsPowerModel
{
    public const int StrengthPerRepeat = 1;

    private sealed class Data
    {
        public readonly Dictionary<ulong, CardType> LastCardTypeByPlayer = new();
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override object InitInternalData() => new Data();

    private ThingsHumanFaceTowerMockery? OwnerMockery =>
        IsMutable ? Owner?.Monster as ThingsHumanFaceTowerMockery : null;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("TauntBlock", 4),
        new IntVar("StrengthGain", StrengthPerRepeat)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block),
        HoverTipFactory.FromPower<StrengthPower>()
    ];

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (IsMutable && OwnerMockery is { } mockery)
                DynamicVars["TauntBlock"].BaseValue = mockery.TauntBlock;
            return base.SmartDescriptionLocKey;
        }
    }

    public void ResetTrackingOnRotation()
    {
        if (IsMutable)
            GetInternalData<Data>().LastCardTypeByPlayer.Clear();
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (OwnerMockery is not { IsActiveFace: true } mockery || !Owner.IsAlive)
            return;
        if (Owner.CombatState?.CurrentSide != CombatSide.Player)
            return;

        Data data = GetInternalData<Data>();
        Player actor = GetCardPlayActor(cardPlay);
        CardType playedType = cardPlay.Card.Type;
        bool repeatedSameType = data.LastCardTypeByPlayer.TryGetValue(actor.NetId, out CardType previousType)
            && previousType == playedType;
        data.LastCardTypeByPlayer[actor.NetId] = playedType;

        if (!repeatedSameType)
            return;

        int blockGain = mockery.TauntBlock;
        DynamicVars["TauntBlock"].BaseValue = blockGain;

        Flash();
        NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot("mockery_taunt", mockery.IdleAnimationName);
        await CreatureCmd.GainBlock(Owner, blockGain, ValueProp.Unpowered, null);
        if (Owner.IsAlive)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, StrengthPerRepeat, Owner, null);
        }
    }

    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Player)
            GetInternalData<Data>().LastCardTypeByPlayer.Clear();
        return Task.CompletedTask;
    }
}
