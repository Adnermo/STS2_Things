using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Platform;

namespace STS2_Things.Powers;

/// <summary>
/// Retains the Wyrm's own Block across turns and siphons all remaining player Block
/// at the end of every enemy turn (after enemy attacks resolve and before player Block
/// naturally expires).
/// </summary>
public sealed class ThingsArmorSiphonPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get { yield return HoverTipFactory.Static(StaticHoverTip.Block); }
    }

    public override bool ShouldClearBlock(Creature creature) => creature != Owner;

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || Owner is not { IsAlive: true } owner ||
            CombatState == null || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        List<Creature> blockedPlayers = CombatState.Players
            .Select(player => player.Creature)
            .Where(creature => creature.IsAlive && creature.Block > 0)
            .ToList();
        if (blockedPlayers.Count == 0)
            return;

        ThingsScalePeeler? peeler = owner.Monster as ThingsScalePeeler;
        if (peeler != null)
            await peeler.BeginArmorSiphonAsync(blockedPlayers);

        int totalStolen = 0;
        foreach (Creature target in blockedPlayers)
        {
            if (!target.IsAlive || target.Block <= 0)
                continue;
            int stolen = target.Block;
            totalStolen += stolen;
            await Sts2VersionSeam.LoseBlock(choiceContext, target, stolen, owner);
        }

        if (totalStolen > 0 && owner.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            Flash();
            await CreatureCmd.GainBlock(owner, totalStolen, ValueProp.Unpowered, null);
        }

        if (peeler != null)
            await peeler.FinishArmorSiphonAsync();
    }

    public override Task AfterBlockGained(
        Creature creature,
        decimal amount,
        ValueProp props,
        CardModel? cardSource)
    {
        if (creature == Owner)
            SyncIntentDisplay();
        return Task.CompletedTask;
    }

    public override Task AfterBlockCleared(Creature creature)
    {
        if (creature == Owner)
            SyncIntentDisplay();
        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Owner && result.BlockedDamage > 0)
            SyncIntentDisplay();
        return Task.CompletedTask;
    }

    protected override Task OnBlockBrokenAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        Creature? breaker)
    {
        if (target == Owner)
            SyncIntentDisplay();
        return Task.CompletedTask;
    }

    internal void SyncIntentDisplay()
    {
        if (Owner is { IsAlive: true, CombatState: not null })
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
    }
}
