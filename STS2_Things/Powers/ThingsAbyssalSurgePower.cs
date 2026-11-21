using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
/// 暗潮狂涌 (Abyssal Surge) — 渊墨巨蛸半血二阶段常驻强化状态。
/// 高压渊墨扫射染黑整手新抽卡牌，深渊召腕直接砸穿古桥石板突袭。
/// </summary>
public sealed class ThingsAbyssalSurgePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<ThingsInkBlackoutPower>()
    ];
}
