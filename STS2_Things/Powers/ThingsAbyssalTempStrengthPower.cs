using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;
using STS2_Things.Platform;

namespace STS2_Things.Powers;

/// <summary>
/// 墨涌临时力量 (Abyssal Temporary Strength) — 墨腺触腕在渊墨巨蛸蓄力回合施加给本体的 1 回合临时力量。
/// 严格遵循原版 <see cref="TemporaryStrengthPower"/> + <see cref="RitualPower"/> 机制：
/// 施加时静默赋予等量原版 <see cref="StrengthPower"/>，跳过蓄力当回合的敌方回合结束，
/// 在下回合（高压渊墨扫射）的敌方回合结束时自动移除并等量回退力量。
/// </summary>
public sealed class ThingsAbyssalTempStrengthPower : ThingsPowerModel, ITemporaryPower
{
    private bool _wasJustAppliedByEnemy;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public AbstractModel OriginModel => ModelDb.Monster<ThingsAbyssalInkTentacle>();
    public PowerModel InternallyAppliedPower => ModelDb.Power<StrengthPower>();

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<StrengthPower>()
    ];

    private bool WasJustAppliedByEnemy
    {
        get => _wasJustAppliedByEnemy;
        set
        {
            AssertMutable();
            _wasJustAppliedByEnemy = value;
        }
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _wasJustAppliedByEnemy = false;
    }

    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), target, amount, applier, cardSource, silent: true);
    }

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (Owner.IsEnemy && Owner.CombatState?.CurrentSide == CombatSide.Enemy)
        {
            WasJustAppliedByEnemy = true;
        }

        if (Owner.IsAlive)
        {
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }

        return Task.CompletedTask;
    }

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (ReferenceEquals(power, this) && amount != Amount)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, amount, applier, cardSource, silent: true);
            if (Owner.IsAlive)
            {
                Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
            }
        }
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner))
            return;

        if (WasJustAppliedByEnemy)
        {
            WasJustAppliedByEnemy = false;
            return;
        }

        Flash();
        int amountToRevert = Amount;
        await PowerCmd.Remove(this);
        if (Owner.IsAlive && !CombatManager.Instance.IsOverOrEnding && amountToRevert != 0)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, -amountToRevert, Owner, null);
            Sts2VersionSeam.RefreshMonsterIntentsUi(Owner);
        }
    }
}
