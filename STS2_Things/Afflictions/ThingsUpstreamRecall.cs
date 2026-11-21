using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_Things.Afflictions;

public sealed class ThingsUpstreamRecall : AfflictionModel
{
    public override bool HasExtraCardText => true;
    public override bool CanAfflictUnplayableCards => false;
    public override bool CanAfflict(CardModel card) => base.CanAfflict(card);
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];
}
