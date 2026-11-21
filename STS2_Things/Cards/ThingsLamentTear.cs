using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

/// <summary>
/// 【哀泪】Status card inflicted by Lament Visage.
/// Costs 1 Energy to Exhaust; if left in Hand at Turn End while any Lament Visage is alive,
/// permanently inflicts 1 stack of <see cref="ThingsLamentSorrowPower"/> (悲叹) on its owner.
/// </summary>
public sealed class ThingsLamentTear : ThingsCardModel
{
    public const int SorrowStacks = 1;

    public ThingsLamentTear()
        : base(1, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;
    public override bool CanBeGeneratedInCombat => false;
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override bool HasTurnEndInHandEffect => true;

    protected override IEnumerable<string> ExtraRunAssetPaths => [PortraitPath];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<ThingsLamentSorrowPower>(SorrowStacks)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<ThingsLamentSorrowPower>()];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        if (CombatState == null || Owner.Creature.IsDead)
            return;
        if (!CombatState.Enemies.Any(e => e.IsAlive && e.Monster is ThingsHumanFaceTowerLament))
            return;

        await PowerCmd.Apply<ThingsLamentSorrowPower>(
            choiceContext,
            Owner.Creature,
            DynamicVars["ThingsLamentSorrowPower"].BaseValue,
            null,
            this);
    }
}
