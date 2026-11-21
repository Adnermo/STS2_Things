using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 缄默面 passive: 【石栓合缝】.
/// Gains 14(18) Block upon rotating into center and retains Block while bolted &amp; active.
/// Breaking its Block while bolted triggers 破栓 (Unbolted): suffers 2 Vulnerable and weakens its
/// heavy slam until re-bolted by 镇岳蓄压 or its next rotation into center.
/// </summary>
public sealed class ThingsBastionSealPower : ThingsPowerModel
{
    public const int UnboltVulnerableTurns = 2;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    private ThingsHumanFaceTowerBastion? OwnerBastion =>
        IsMutable ? Owner?.Monster as ThingsHumanFaceTowerBastion : null;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("EntryBlock", 14),
        new IntVar("VulnerableTurns", UnboltVulnerableTurns)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.Static(StaticHoverTip.Block),
        HoverTipFactory.FromPower<VulnerablePower>()
    ];

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (!IsMutable || OwnerBastion is not { } bastion)
                return base.SmartDescriptionLocKey;

            DynamicVars["EntryBlock"].BaseValue = bastion.EntrySealBlock;
            return bastion.IsBolted
                ? "THINGS_BASTION_SEAL_POWER.boltedDescription"
                : "THINGS_BASTION_SEAL_POWER.unboltedDescription";
        }
    }

    public void RefreshSealDisplay() => InvokeDisplayAmountChanged();

    public override bool ShouldClearBlock(Creature creature)
    {
        if (creature == Owner && OwnerBastion is { IsActiveFace: true, IsBolted: true })
            return false;
        return true;
    }

    protected override async Task OnBlockBrokenAsync(
        PlayerChoiceContext choiceContext, Creature target, Creature? breaker)
    {
        if (target != Owner || !Owner.IsAlive || OwnerBastion is not { IsActiveFace: true, IsBolted: true } bastion)
            return;

        bastion.SetBolted(false, playUnboltAnim: true);
        RefreshSealDisplay();
        Flash();
        await PowerCmd.Apply<VulnerablePower>(choiceContext, Owner, UnboltVulnerableTurns, Owner, null);
    }
}
