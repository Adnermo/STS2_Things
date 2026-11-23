using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using STS2_Things.Core.Models;

namespace STS2_Things.Cards;

public sealed class ThingsSurrender : ThingsCardModel
{
    public ThingsSurrender() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        new[] { CardKeyword.Exhaust };

    protected override List<DynamicVar> CanonicalVars =>
    [
        new HealVar(3),
        new PowerVar<WeakPower>(1m)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<WeakPower>()
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);

        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue); //回血

        var target = cardPlay.Target;
        var monster = target?.Monster;
        if (target == null || target.IsDead || monster == null) return;
        if (monster.NextMove == null || !monster.NextMove.CanTransitionAway) return;
        var moveStateMachine = monster.MoveStateMachine;
        if (moveStateMachine == null) return;
        var originalStateId = ResolveOriginalStateId(monster, moveStateMachine);
        if (string.IsNullOrEmpty(originalStateId)) return;
        var weakAmount = DynamicVars.Weak.BaseValue;
        var applyWeak = new MoveState(
            "DEBUFF_WEAK",
            async targets =>
            {
                foreach (var t in targets)
                {
                    if (!t.IsDead)
                    {
                        await PowerCmd.Apply<WeakPower>(
                            new ThrowingPlayerChoiceContext(),
                            t,
                            weakAmount,
                            target,
                            null);
                    }
                }
            },
            new DebuffIntent()
        )
        {
            FollowUpStateId = originalStateId,
            MustPerformOnceBeforeTransitioning = true
        };
        monster.SetMoveImmediate(applyWeak, forceTransition: false);
    }

    private static string? ResolveOriginalStateId(MonsterModel monster, MonsterMoveStateMachine moveStateMachine)
    {
        if (monster.NextMove is { } liveMove)
        {
            if (!string.IsNullOrEmpty(liveMove.StateId) && moveStateMachine.States.ContainsKey(liveMove.StateId))
                return liveMove.StateId;

            if (liveMove.FollowUpState is { } follow &&
                !string.IsNullOrEmpty(follow.Id) &&
                moveStateMachine.States.ContainsKey(follow.Id))
                return follow.Id;

            if (!string.IsNullOrEmpty(liveMove.FollowUpStateId) &&
                moveStateMachine.States.ContainsKey(liveMove.FollowUpStateId))
                return liveMove.FollowUpStateId;
        }

        IReadOnlyList<MonsterState> stateLog = moveStateMachine.StateLog;
        if (stateLog.Count > 0)
        {
            string loggedId = stateLog[stateLog.Count - 1].Id;
            if (!string.IsNullOrEmpty(loggedId))
                return loggedId;
        }

        return null;
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
