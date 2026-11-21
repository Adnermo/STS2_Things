using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

/// <summary>Persistent native Block is the shell; native block breaking resolves it once.</summary>
public sealed class ThingsSnailShellPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            yield return HoverTipFactory.Static(StaticHoverTip.Block);
            if (!IsMutable || Owner?.Monster == null) yield break;
            string kind = Owner.Monster is ThingsCrystalSnail ? "crystalBreak" : "rockBreak";
            yield return new HoverTip(new LocString("powers", "THINGS_SNAIL_SHELL_POWER.breakTitle"),
                new LocString("powers", $"THINGS_SNAIL_SHELL_POWER.{kind}"));
            if (Owner.Monster is ThingsCrystalSnail)
                foreach (var tip in HoverTipFactory.FromCardWithCardHoverTips<ThingsSnailCrystalChip>()) yield return tip;
        }
    }

    public override bool ShouldClearBlock(Creature creature) => !IsMutable || creature != Owner;

    protected override Task OnBlockBrokenAsync(PlayerChoiceContext choiceContext, Creature target, Creature? breaker) =>
        Break(choiceContext, target);

    private async Task Break(PlayerChoiceContext context, Creature target)
    {
        if (target != Owner || target.Block > 0 || !ReferenceEquals(target.GetPower<ThingsSnailShellPower>(), this)) return;
        // Remove before reward/retreat hooks, including simultaneous AoE breaks.
        await PowerCmd.Remove(this);
        NSnailShellBreakVfx.Play(target);
        if (target.Monster is DepthsSnail snail)
        {
            if (target.IsAlive && !snail.IsPerformingMove)
                await CreatureCmd.TriggerAnim(target, "ShellBreak", 0);
            await snail.OnShellBroken(context);
            snail.NotifyMenders();
        }
    }
}
