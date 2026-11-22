using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 触腕庇护 (Tentacle Cover) — 渊墨巨蛸本体的联动庇护能力。
/// 只要场上有存活的主触腕，即提供 25% 攻击减伤（不按触腕数叠加，直接显示于层数）；
/// 斩断任意主触腕对本体造成 20 点反伤、施加 2 层易伤并冲净全部染黑手牌。
/// </summary>
public sealed class ThingsAbyssalCoverPower : ThingsPowerModel
{
    public const int ActiveReductionPercent = 25;
    public const int SeverRecoilDamage = 20;
    public const int SeverVulnerableStacks = 2;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<VulnerablePower>(),
        HoverTipFactory.FromPower<ThingsInkBlackoutPower>()
    ];

    protected override decimal OnModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredAttack())
            return 1m;
        if (dealer == null || dealer.Side == CombatSide.Enemy)
            return 1m;

        int reductionPct = ComputeActiveReductionPercent();
        return reductionPct > 0 ? Math.Max(0.25m, 1m - reductionPct / 100m) : 1m;
    }

    public override Task AfterModifyingDamageAmount(CardModel? cardSource)
    {
        if (ComputeActiveReductionPercent() > 0)
            Flash();
        return Task.CompletedTask;
    }

    private int ComputeActiveReductionPercent()
    {
        ICombatState? combatState = Owner.CombatState;
        if (combatState == null)
            return Math.Clamp(Amount, 0, ActiveReductionPercent);

        bool hasLivingMajorTentacle = combatState.Enemies.Any(e =>
            e.IsAlive && e != Owner &&
            (e.Monster is ThingsAbyssalCrushingTentacle || e.Monster is ThingsAbyssalInkTentacle));

        return hasLivingMajorTentacle ? ActiveReductionPercent : 0;
    }
}
