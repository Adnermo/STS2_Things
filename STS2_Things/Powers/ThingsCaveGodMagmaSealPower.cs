using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 熔核锁印 (Magma Core Seal) — 活体巨岩一阶段半血锁血能力。
/// 战斗开始即以层数标明精确的 50% 半血阈值；首次降至阈值时锁定生命并切换血条为 ∞，
/// 在当回合苏醒进入二阶段、恢复双臂并重塑磐岩护体时移除。
/// </summary>
public sealed class ThingsCaveGodMagmaSealPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<StrengthPower>(),
        HoverTipFactory.FromPower<ThingsCaveGodAncientCorePower>()
    ];

    protected override string SmartDescriptionLocKey =>
        IsMutable && Owner?.Monster is ThingsCaveGodBody { IsEnrageLocked: true }
            ? $"{Id.Entry}.lockedDescription"
            : base.SmartDescriptionLocKey;

    public void NotifyLockTriggered() => Flash();
}
