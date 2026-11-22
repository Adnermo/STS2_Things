using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsNarrowViewfinder : ThingsRelicModel
{
    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool TryModifyCardRewardOptions(
        Player player,
        List<CardCreationResult> cardRewards,
        CardCreationOptions options)
    {
        if (player != Owner)
            return false;
        if (!options.Flags.HasFlag(CardCreationFlags.IsCardReward)
            || options.Flags.HasFlag(CardCreationFlags.NoCardModelModifications))
        {
            return false;
        }
        if (cardRewards.Count <= 1)
            return false;

        cardRewards.RemoveAt(cardRewards.Count - 1);
        return true;
    }

    public override bool TryModifyCardRewardOptionsLate(
        Player player,
        List<CardCreationResult> cardRewards,
        CardCreationOptions options)
    {
        if (player != Owner)
            return false;
        if (!options.Flags.HasFlag(CardCreationFlags.IsCardReward)
            || options.Flags.HasFlag(CardCreationFlags.NoHookUpgrades))
        {
            return false;
        }

        bool modified = false;
        foreach (CardCreationResult result in cardRewards)
        {
            if (!result.Card.IsUpgradable)
                continue;
            CardModel upgraded = Owner.RunState.CloneCard(result.Card);
            CardCmd.Upgrade(upgraded);
            result.ModifyCard(upgraded, this);
            modified = true;
        }
        return modified;
    }
}
