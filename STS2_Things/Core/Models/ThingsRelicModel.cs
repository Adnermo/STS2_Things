using System.Threading.Tasks;
#if !STS2_V113_1
using MegaCrit.Sts2.Core.Entities.Cards;
#endif
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Platform;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for all <c>STS2_Things</c> relics.
/// </summary>
public abstract class ThingsRelicModel : RelicModel
{
    protected Task LoseOwnerMaxHpAsync(PlayerChoiceContext choiceContext, decimal amount)
        => Sts2VersionSeam.LoseMaxHp(choiceContext, Owner.Creature, amount);

    protected static bool IsCardAllowedInRun(CardModel card, IRunState runState)
    {
#if STS2_V113_1
        return card.IsAllowedInRun(runState);
#else
        return runState.Players.Count > 1
            ? card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly
            : card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly;
#endif
    }
}
