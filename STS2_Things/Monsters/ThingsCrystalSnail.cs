using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Cards;

namespace STS2_Things.Monsters;

public sealed class ThingsCrystalSnail : DepthsSnail
{
    private MoveState? _bareBump;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _bareBump = null;
    }
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 33, 30);
    public override int MaxInitialHp => MinInitialHp + 3;
    private int BumpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 9);
    private int OpeningShell => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6);
    private int RetreatShell => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6);
    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([ModelDb.Card<ThingsSnailCrystalChip>().PortraitPath]);

    public override Task BeforeCombatStart() => GrowShell(new ThrowingPlayerChoiceContext(), OpeningShell, initial: true);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var bump = new MoveState("BUMP_MOVE", _ => Hit(BumpDamage), new SingleAttackIntent(BumpDamage));
        var retreat = new MoveState("RETREAT_MOVE", Retreat, new DefendIntent());
        var bare = new MoveState("BARE_BUMP_MOVE", _ => Hit(BumpDamage), new SingleAttackIntent(BumpDamage))
            { MustPerformOnceBeforeTransitioning = true };
        var afterBump = new ConditionalBranchState("AFTER_BUMP");
        afterBump.AddState(retreat, () => HasShell);
        afterBump.AddState(bare, () => true);
        bump.FollowUpState = afterBump;
        retreat.FollowUpState = bump;
        bare.FollowUpState = bare;
        _bareBump = bare;
        return new MonsterMoveStateMachine([bump, retreat, bare, afterBump], bump);
    }

    private async Task Retreat(IReadOnlyList<Creature> targets)
    {
        if (!CanAct || !HasShell) return;
        await BeginPose("Retreat", "retreat", .55f);
        if (CanAct && HasShell)
            await GrowShell(new ThrowingPlayerChoiceContext(), RetreatShell);
        await WaitForPose("retreat", 1.35f, .80f);
    }

    public override async Task OnShellBroken(PlayerChoiceContext context)
    {
        if (Creature.IsAlive && _bareBump != null)
        {
            if (NextMove.Id == MonsterModel.stunnedMoveId) NextMove.FollowUpState = _bareBump;
            else if (!IsPerformingMove) SetMoveImmediate(_bareBump, forceTransition: true);
        }
        // Shell breaking, including a lethal hit, rewards each living player once.
        if (!CombatManager.Instance.IsInProgress)
            return;

        // Complete all deterministic combat-state mutations across every player BEFORE any local UI preview,
        // so a client/host UI exception can never leave multiplayer discard piles out of sync.
        var localPreviews = new List<(CardModel Card, CardPileAddResult Result)>();
        foreach (var player in CombatState.Players.Where(p => p.Creature.IsAlive).ToList())
        {
            var card = CombatState.CreateCard<ThingsSnailCrystalChip>(player);
            var result = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Discard, creator: null);
            if (LocalContext.IsMe(player))
                localPreviews.Add((card, result));
        }

        if (CombatManager.Instance.IsOverOrEnding || !CombatState.Enemies.Any(e => e.IsAlive))
            return;

        foreach (var (card, result) in localPreviews)
            PreviewAddedCardSafely(card, result);
    }

    private static void PreviewAddedCardSafely(CardModel card, CardPileAddResult result)
    {
        try
        {
            CardCmd.PreviewCardPileAdd(result);
        }
        catch (Exception ex)
        {
            // CardCmd.PreviewInternal adds NCard to CardPreviewContainer before UpdateVisuals;
            // clean up any half-initialized preview node so it never freezes mid-screen.
            var ui = NCombatRoom.Instance?.Ui;
            foreach (Control? container in new[] { ui?.CardPreviewContainer, ui?.MessyCardPreviewContainer })
            {
                if (container == null || !GodotObject.IsInstanceValid(container)) continue;
                foreach (NCard child in container.GetChildren().OfType<NCard>().Where(n => ReferenceEquals(n.Model, card)).ToList())
                    child.QueueFreeSafely();
            }
            Log.Warn($"STS2_Things: SnailCrystalChip local preview failed after state commit: {ex.Message}");
        }
    }
}
