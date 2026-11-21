using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using STS2_Things.Config;
using STS2_Things.Platform;

namespace STS2_Things.Hooks.DefensiveGuards;

/// <summary>
/// Defensive multiplayer lobby &amp; dead-player card choice guards:
/// 1. Unregisters <c>LobbyGameModeChangedMessage</c> in <see cref="StartRunLobby.CleanUp"/> (fixing the v113 handler leak).
/// 2. Short-circuits <see cref="CardSelectCmd.FromHandForUpgrade"/> when <c>player.Creature.IsDead</c> (matching all sibling
///    <c>CardSelectCmd.FromHand*</c> methods so dead multiplayer players never stall choice synchronization).
/// </summary>
internal static class LobbyAndChoiceSafetyGuard
{
    private static bool IsActive => ThingsModConfig.IsEnabled(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled);

    [HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.CleanUp))]
    private static class StartRunLobbyCleanUpPatch
    {
        private static void Postfix(StartRunLobby __instance)
        {
            if (!IsActive)
                return;
            EngineAccess.UnregisterLeakedLobbyGameModeHandler(__instance);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
    private static class DeadPlayerHandUpgradeGuardPatch
    {
        private static bool Prefix(Player player, ref Task<CardModel?> __result)
        {
            if (!IsActive || !player.Creature.IsDead)
                return true;

            __result = Task.FromResult<CardModel?>(null);
            return false;
        }
    }
}
