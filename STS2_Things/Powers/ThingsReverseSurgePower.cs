using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Core.Models;

namespace STS2_Things.Powers;

/// <summary>
/// Hidden checksum-visible marker armed by the Salamander's Surging Flow move.
/// Consumed on the player's next turn after opening draw by <see cref="ThingsReverseCurrentPower"/>.
/// Uses Buff type so it does not consume player Artifact.
/// </summary>
public sealed class ThingsReverseSurgePower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    public override bool ShouldPlayVfx => false;
    protected override bool IsVisibleInternal => false;

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext, Creature creature, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (wasRemovalPrevented || !Owner.IsAlive) return;
        if (!CombatState.Enemies.Any(e => e.IsAlive && e.GetPower<ThingsReverseCurrentPower>() != null))
            await PowerCmd.Remove(this);
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Player && participants.Contains(Owner) &&
            !CombatState.Enemies.Any(e => e.IsAlive && e.GetPower<ThingsReverseCurrentPower>() != null))
            await PowerCmd.Remove(this);
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (Owner.HasPower<ThingsReverseSurgePower>())
            await PowerCmd.Remove(this);
    }
}
