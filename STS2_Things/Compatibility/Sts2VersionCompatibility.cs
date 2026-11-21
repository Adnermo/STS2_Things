using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2_Things.Platform;

namespace STS2_Things.Compatibility;

/// <summary>
/// Forwarding facade to <see cref="Sts2VersionSeam"/> kept for visual/VFX callers.
/// </summary>
internal static class Sts2VersionCompatibility
{
    public static IDisposable? TrackEntryScope(MegaTrackEntry? entry, out MegaTrackEntry? track)
        => Sts2VersionSeam.TrackEntryScope(entry, out track);

    public static void InitializeBeforeModelDatabase()
        => Sts2VersionSeam.InitializeBeforeModelDatabase();

#if STS2_V113_1
    public static void ToggleIsInteractable(this MegaCrit.Sts2.Core.Nodes.Combat.NCreature node, bool on)
        => Sts2VersionSeam.ToggleIsInteractable(node, on);

    public static void AnimEnableUi(this MegaCrit.Sts2.Core.Nodes.Combat.NCreature node)
        => Sts2VersionSeam.AnimEnableUi(node);
#endif

    public static Task LoseMaxHp(PlayerChoiceContext context, Creature creature, decimal amount)
        => Sts2VersionSeam.LoseMaxHp(context, creature, amount);

    public static void SetCreatureNodeVisible(Creature creature, bool visible)
        => Sts2VersionSeam.SetCreatureNodeVisible(creature, visible);

    public static void RemoveCreatureWithoutDeathOrEscape(Creature creature)
        => Sts2VersionSeam.RemoveCreatureWithoutDeathOrEscape(creature);
}
