using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsMagicGlove : ThingsRelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CardsVar(2),
        new HpLossVar(9)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            yield return HoverTipFactory.Static(StaticHoverTip.Transform);
            foreach (var tip in HoverTipFactory.FromCardWithCardHoverTips<FlashOfSteel>())
                yield return tip;
            foreach (var tip in HoverTipFactory.FromCardWithCardHoverTips<Finesse>())
                yield return tip;
        }
    }

    public override async Task AfterObtained()
    {
        await LoseOwnerMaxHpAsync(
            new ThrowingPlayerChoiceContext(),
            DynamicVars.HpLoss.BaseValue);
        if (Owner.Creature.IsDead)
            return;

        int transformableCount = Owner.Deck.Cards.Count(c => c.Type != CardType.Quest && c.IsTransformable);
        int selectCount = Math.Min(DynamicVars.Cards.IntValue, transformableCount);
        if (selectCount <= 0)
            return;

        var list = (await CardSelectCmd.FromDeckForTransformation(
            prefs: new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, selectCount),
            player: Owner)).ToList();

        if (list.Count >= 1)
        {
            CardModel finesse = Owner.RunState.CreateCard<Finesse>(Owner);
            await CardCmd.Transform(list[0], finesse);
        }
        if (list.Count >= 2)
        {
            CardModel flashOfSteel = Owner.RunState.CreateCard<FlashOfSteel>(Owner);
            await CardCmd.Transform(list[1], flashOfSteel);
        }
    }
}