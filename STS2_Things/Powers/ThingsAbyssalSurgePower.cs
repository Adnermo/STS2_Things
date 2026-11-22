using MegaCrit.Sts2.Core.Entities.Powers;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
/// 暗潮狂涌 (Abyssal Surge) — 渊墨巨蛸半血二阶段常驻强化状态。
/// 强化喷墨/浪涌伤害与减益层数，并使深渊召腕可同时召唤至多 2 条小触腕破桥突袭。
/// </summary>
public sealed class ThingsAbyssalSurgePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
}
