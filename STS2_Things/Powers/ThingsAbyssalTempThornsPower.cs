using System.Collections.Generic;
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

namespace STS2_Things.Powers;

/// <summary>
/// 棘甲临时荆棘 (Abyssal Temporary Thorns) — 尖刺幼触在渊墨巨蛸蓄力回合施加给本体的 1 回合临时荆棘。
/// 严格遵循原版 v113.1 <c>TemporaryThornsPower</c> 机制并双版本兼容（v111.1 / v113.1）：
/// 施加时静默赋予等量原版 <see cref="ThornsPower"/>，持续覆盖接下来整个玩家回合，
/// 在玩家回合结束（<c>Owner.Side != side</c>）时自动移除并等量回退荆棘。
/// </summary>
public sealed class ThingsAbyssalTempThornsPower : ThingsPowerModel, ITemporaryPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public AbstractModel OriginModel => ModelDb.Monster<ThingsAbyssalSpikeTentacle>();
    public PowerModel InternallyAppliedPower => ModelDb.Power<ThornsPower>();

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<ThornsPower>()
    ];

    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        await PowerCmd.Apply<ThornsPower>(new ThrowingPlayerChoiceContext(), target, amount, applier, cardSource, silent: true);
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
            await PowerCmd.Apply<ThornsPower>(choiceContext, Owner, amount, applier, cardSource, silent: true);
        }
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (Owner.Side == side)
            return;

        Flash();
        int amountToRevert = Amount;
        await PowerCmd.Remove(this);
        if (Owner.IsAlive && !CombatManager.Instance.IsOverOrEnding && Owner.GetPower<ThornsPower>() is { } thorns)
        {
            await PowerCmd.ModifyAmount(choiceContext, thorns, -amountToRevert, Owner, null);
        }
    }
}
