using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Monsters;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

public sealed class ThingsCaveMawAppetitePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    public void ShowMeal() => Flash();
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new IntVar("BlockPerCard", ThingsCaveMaw.BlockPerCard), new IntVar("StrengthCap", ThingsCaveMaw.StrengthPerMeal)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
         HoverTipFactory.Static(StaticHoverTip.Block),
         HoverTipFactory.FromPower<StrengthPower>()];
}
