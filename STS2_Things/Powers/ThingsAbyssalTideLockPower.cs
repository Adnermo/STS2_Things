using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using STS2_Things.Core.Models;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
/// 深渊锁潮 (Abyssal Tide Lock) — 渊墨巨蛸一阶段半血锁血能力。
/// 战斗开始即以层数标明精确的 50% 半血阈值；首次降至阈值时锁定生命并切换血条为 ∞，
/// 直到二阶段转场【暗潮狂涌】执行时解除锁血并召唤全部空缺触腕。
/// </summary>
public sealed class ThingsAbyssalTideLockPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<ThingsAbyssalSurgePower>()
    ];

    protected override string SmartDescriptionLocKey =>
        IsMutable && Owner?.Monster is ThingsAbyssalInkmaw { IsPhase2Locked: true }
            ? $"{Id.Entry}.lockedDescription"
            : base.SmartDescriptionLocKey;

    public void NotifyLockTriggered() => Flash();
}
