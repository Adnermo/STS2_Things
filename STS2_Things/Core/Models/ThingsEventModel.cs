using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for all <c>STS2_Things</c> events.
/// Centralizes owner validation, combat-layout state lookup, potion belt permissions,
/// and dual-version max-HP loss commands.
/// </summary>
public abstract class ThingsEventModel : EventModel
{
    protected Player EventOwner => Owner ?? throw new InvalidOperationException($"Event {Id} has no owner.");

    protected CombatState CombatLayoutState => EngineAccess.GetEventCombatLayoutState(this);

    protected bool PlayerCanModifyPotions
    {
        get => Sts2VersionSeam.GetPlayerCanModifyPotions(EventOwner);
        set => Sts2VersionSeam.SetPlayerCanModifyPotions(EventOwner, value);
    }

    protected IEnumerable<PotionModel> GetPlayerPotionOptions()
        => Sts2VersionSeam.GetPlayerPotionOptions(EventOwner);

    protected Task LoseEventOwnerMaxHpAsync(PlayerChoiceContext choiceContext, decimal amount)
        => Sts2VersionSeam.LoseMaxHp(choiceContext, EventOwner.Creature, amount);
}
