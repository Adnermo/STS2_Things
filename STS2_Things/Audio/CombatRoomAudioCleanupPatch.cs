using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Hooks;

namespace STS2_Things.Audio;

/// <summary>
/// 离开战斗房间时立即终止残留的自定义音乐/循环音效（先于场景节点替换与跨幕切歌执行）。
/// </summary>
[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.Exit))]
internal static class CombatRoomExitAudioCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        CustomMusicPlayPatch.StartAfterCombatSetup = false;
        NativeSfxPlayer.StopSequentialLoop();
        NativeSfxPlayer.StopMusic();
    }
}

/// <summary>
/// 本 Mod 的本地循环音频生命周期清理。与怪物/遭遇注册分离，避免注册层接管房间视觉。
/// 不再在此盲目调用 StopCustomMusic()，防止跨幕切歌后旧 NCombatRoom._ExitTree()
/// 把新幕音乐误重置为 Progress=7 (CombatEnd)。
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._ExitTree))]
internal static class CombatRoomAudioCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!CustomMusicPlayPatch.IsPendingForActiveCombat())
            CustomMusicPlayPatch.StartAfterCombatSetup = false;
        NativeSfxPlayer.StopSequentialLoop();
        if (!CombatManager.Instance.IsInProgress)
            NativeSfxPlayer.StopMusic();
        NativeSfxPlayer.CleanupActivePlayers();
        SfxHooks.ResetDeathSfx();
    }
}
