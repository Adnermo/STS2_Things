using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 悲叹面 passive: 【叹息淤积】.
/// Exposes the Lament Tear + Lamentation debuff rule cleanly in Lament Visage's passive slot and ensures
/// any lingering <see cref="ThingsLamentSorrowPower"/> stacks are stripped from all players when Lament dies.
/// </summary>
public sealed class ThingsLamentSighPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new IntVar("SorrowGain", ThingsLamentTear.SorrowStacks)];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<ThingsLamentTear>().Concat([
            HoverTipFactory.FromPower<ThingsLamentSorrowPower>()
        ]);

    public override async Task AfterRemoved(Creature oldOwner)
    {
        if (oldOwner.Monster is not ThingsHumanFaceTowerLament || oldOwner.CombatState is not { } combatState)
            return;

        if (combatState.Enemies.Any(e => e != oldOwner && e.IsAlive && e.Monster is ThingsHumanFaceTowerLament))
            return;

        foreach (Player player in combatState.Players)
        {
            if (player.Creature.IsAlive && player.Creature.GetPower<ThingsLamentSorrowPower>() is { } sorrow)
            {
                await PowerCmd.Remove(sorrow);
            }
        }
    }
}
