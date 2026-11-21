using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsReturnReceipt : ThingsRelicModel
{
    public const int RewardGold = 20;
    public const int RewardHeal = 3;
    public const string ReturnOptionId = "THINGS_RETURN";

    public override RelicRarity Rarity => RelicRarity.Event;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new GoldVar(RewardGold), new HealVar(RewardHeal)];

    public override bool TryModifyCardRewardAlternatives(
        Player player,
        CardReward cardReward,
        List<CardRewardAlternative> alternatives)
    {
        if (player != Owner)
            return false;
        // CardRewardAlternative throws if more than 2 alternatives exist (e.g. Reroll + PaelsWing).
        if (alternatives.Count >= 2
            || alternatives.Any(existing => existing.OptionId == ReturnOptionId)
            || Owner.Relics.OfType<PaelsWing>().Any())
        {
            return false;
        }

        alternatives.Add(new CardRewardAlternative(
            ReturnOptionId,
            CollectReturnAsync,
            PostAlternateCardRewardAction.EndSelectionAndCompleteReward));
        return true;
    }

    private async Task CollectReturnAsync()
    {
        Flash();
        await PlayerCmd.GainGold(RewardGold, Owner);
        await CreatureCmd.Heal(Owner.Creature, RewardHeal);
    }
}
