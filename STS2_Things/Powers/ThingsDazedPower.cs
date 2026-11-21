using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
///     眩晕戳刺 — 造成未被格挡的伤害时，往目标弃牌堆放入1张眩晕(Dazed)
/// </summary>
public sealed class ThingsDazedPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("DazedCount", 1)
    ];

    protected override string SmartDescriptionLocKey
    {
        get
        {
            if (IsMutable)
                DynamicVars["DazedCount"].BaseValue = Math.Max(0, Amount);
            return base.SmartDescriptionLocKey;
        }
    }

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<Dazed>();

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer == Owner && result.UnblockedDamage > 0 && target.Side != Owner.Side && Amount > 0)
        {
            if ((target.Player ?? target.PetOwner)?.Creature.IsDead == true)
                return;

            Flash();
            await CardPileCmd.AddToCombatAndPreview<Dazed>(
                [target], PileType.Discard, Amount, null);
        }
    }
}
