using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>Native duration display; only completed attacks advance this timer.</summary>
public sealed class ThingsFleetingFadePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
}
