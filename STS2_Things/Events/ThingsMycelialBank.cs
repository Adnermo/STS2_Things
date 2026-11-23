using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsMycelialBank : ThingsEventModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new HpLossVar(ThingsMycelialDeposit.ShortPrincipal), new HealVar(ThingsMycelialDeposit.ShortRepayment),
        new IntVar("Payments", ThingsMycelialDeposit.ShortPayments), new IntVar("MaxHpCost", ThingsMycelialDeposit.LongPrincipal),
        new IntVar("MaxHpReturn", ThingsMycelialDeposit.LongRepayment),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && player.GetRelic<ThingsMycelialDeposit>() == null
            && player.Creature.CurrentHp > ThingsMycelialDeposit.ShortPrincipal);
    private bool HasAccount => EventOwner.GetRelic<ThingsMycelialDeposit>() != null;
    private bool CanShort => !HasAccount && !EventOwner.Creature.IsDead && EventOwner.Creature.CurrentHp > ThingsMycelialDeposit.ShortPrincipal;
    private bool CanLong => !HasAccount && !EventOwner.Creature.IsDead && EventOwner.Creature.MaxHp > ThingsMycelialDeposit.LongPrincipal;

    private IEnumerable<IHoverTip> Terms(bool longTerm)
    {
        var preview = (ThingsMycelialDeposit)ModelDb.Relic<ThingsMycelialDeposit>().ToMutable();
        preview.Open(longTerm, EventOwner.RunState.CurrentActIndex);
        return preview.HoverTips;
    }

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SHORT", "LONG", "LEAVE");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, CanShort ? DepositShort : null, InitialOptionKey(CanShort ? "SHORT" : "SHORT_LOCKED"), Terms(false)),
        new EventOption(this, CanLong ? DepositLong : null, InitialOptionKey(CanLong ? "LONG" : "LONG_LOCKED"), Terms(true)),
        new EventOption(this, Leave, InitialOptionKey("LEAVE")),
    ];

    private async Task DepositShort()
    {
        if (IsFinished || !CanShort) return;
        await OpenAccount(false);
        await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), EventOwner.Creature,
            ThingsMycelialDeposit.ShortPrincipal, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        Finish(EventOwner.Creature.IsDead ? "DEATH" : "SHORT");
    }

    private async Task DepositLong()
    {
        if (IsFinished || !CanLong) return;
        await OpenAccount(true);
        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), ThingsMycelialDeposit.LongPrincipal);
        Finish(EventOwner.Creature.IsDead ? "DEATH" : "LONG");
    }

    private async Task OpenAccount(bool longTerm)
    {
        var receipt = (ThingsMycelialDeposit)ModelDb.Relic<ThingsMycelialDeposit>().ToMutable();
        receipt.Open(longTerm, EventOwner.RunState.CurrentActIndex);
        await RelicCmd.Obtain(receipt, EventOwner);
    }

    private Task Leave()
    {
        if (IsFinished) return Task.CompletedTask;
        Finish("LEAVE");
        return Task.CompletedTask;
    }

    private void Finish(string page) => SetEventFinished(L10NLookup($"THINGS_MYCELIAL_BANK.pages.{page}.description"));
}
