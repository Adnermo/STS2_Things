using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

/// <summary>
/// Event relic from the Warm Royal Cell: grants +1 card draw at the start of each
/// player turn during the first two turns of combat.
/// </summary>
public sealed class ThingsAmberCombCore : ThingsRelicModel
{
    public const int ExtraCards = 1;
    public const int ActiveTurns = 2;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CardsVar(ExtraCards), new IntVar("Turns", ActiveTurns)];

    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        if (player != Owner || Owner.PlayerCombatState == null)
            return count;
        if (Owner.PlayerCombatState.TurnNumber > DynamicVars["Turns"].IntValue)
            return count;
        return count + DynamicVars.Cards.BaseValue;
    }

    public override Task AfterModifyingHandDraw()
    {
        Flash();
        return Task.CompletedTask;
    }
}
