using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2_Things.Events;

namespace STS2_Things.Hooks;

/// <summary>
/// Clears blocking overlays before jumping into <see cref="ThingsCuttingItClose"/> via console,
/// and prevents duplicate console reentry while an unfinished instance is already active.
/// </summary>
[HarmonyPatch(typeof(EventConsoleCmd), nameof(EventConsoleCmd.Process))]
internal static class CuttingItCloseConsoleReentryPatch
{
    private static bool Prefix(Player? issuingPlayer, string[] args, ref CmdResult __result)
    {
        bool duplicateActive = issuingPlayer is not null &&
                               issuingPlayer.RunState.CurrentRoom is EventRoom currentRoom &&
                               ShouldBlockDuplicate(
                                   args,
                                   currentRoom.CanonicalEvent,
                                   RunManager.Instance.EventSynchronizer.Events);

        if (duplicateActive)
        {
            __result = new CmdResult(
                success: false,
                "THINGS_CUTTING_IT_CLOSE is already active; finish its current option before jumping again.");
            return false;
        }

        if (ShouldPrepareScreens(
                args,
                issuingPlayer is not null,
                RunManager.Instance.IsInProgress,
                duplicateActive))
        {
            ClearBlockingScreens();
        }

        return true;
    }

    internal static bool ShouldPrepareScreens(
        string[] args,
        bool hasIssuingPlayer,
        bool runInProgress,
        bool duplicateActive)
    {
        return args.Length > 0 &&
               string.Equals(args[0], "THINGS_CUTTING_IT_CLOSE", StringComparison.OrdinalIgnoreCase) &&
               hasIssuingPlayer &&
               runInProgress &&
               !duplicateActive;
    }

    private static void ClearBlockingScreens()
    {
        if (TestMode.IsOn)
            return;

        NOverlayStack.Instance?.Clear();
        NCapstoneContainer.Instance?.Close();
        NMapScreen.Instance?.Close(animateOut: false);
    }

    internal static bool ShouldBlockDuplicate(
        string[] args,
        EventModel canonicalEvent,
        IEnumerable<EventModel> mutableEvents)
    {
        return args.Length > 0 &&
               string.Equals(args[0], "THINGS_CUTTING_IT_CLOSE", StringComparison.OrdinalIgnoreCase) &&
               canonicalEvent is ThingsCuttingItClose &&
               mutableEvents.Any(mutableEvent =>
                   mutableEvent is ThingsCuttingItClose && !mutableEvent.IsFinished);
    }
}
