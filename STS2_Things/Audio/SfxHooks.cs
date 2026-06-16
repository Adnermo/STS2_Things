using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Audio;
using Array = Godot.Collections.Array;

namespace STS2_Things.Hooks;

public static class SfxHooks
{
    private static readonly HashSet<int> _deathSfxPlayed = new();

    private static string MapToNativePath(string fmodPath)
    {
        var segments = fmodPath.Split('/');
        if (segments.Length < 2) return null;
        var actionName = segments[^1];
        var monsterId = CustomSfxMonsters.Entries.FirstOrDefault(e => fmodPath.ToLowerInvariant().Contains(e));
        if (monsterId == null) return null;
        return $"res://sfx/{monsterId}/{actionName}";
    }

    private static string MapToNativePathForMonster(string monsterId, string actionName)
    {
        return $"res://sfx/{monsterId}/{monsterId}_{actionName}";
    }

    private static void PreloadMonsterSfx(string monsterId)
    {
        var resDir = $"res://sfx/{monsterId}";
        var files = ListAudioFiles(resDir);
        if (files.Count == 0)
        {
            var modRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var absDir = Path.Combine(modRoot, "sfx", monsterId);
            if (Directory.Exists(absDir))
                foreach (var filePath in Directory.GetFiles(absDir))
                    if (IsAudioFile(filePath))
                        files.Add($"{resDir}/{Path.GetFileName(filePath)}");
        }

        foreach (var resPath in files) NativeSfxPlayer.Preload(resPath);
    }

    private static List<string> ListAudioFiles(string resDir)
    {
        var result = new List<string>();
        var da = DirAccess.Open(resDir);
        if (da == null) return result;
        da.ListDirBegin();
        var fileName = da.GetNext();
        while (fileName != string.Empty)
        {
            if (!da.CurrentIsDir() && IsAudioFile(fileName)) result.Add($"{resDir}/{fileName}");
            fileName = da.GetNext();
        }

        da.ListDirEnd();
        return result;
    }

    private static bool IsAudioFile(string fileName)
    {
        if (!fileName.Contains('.')) return false;
        var ext = fileName[fileName.LastIndexOf('.')..].ToLowerInvariant();
        return ext == ".ogg" || ext == ".wav" || ext == ".mp3";
    }

    [HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), typeof(string), typeof(float))]
    public static class SfxCmdPlayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref string sfx, float volume)
        {
            if (sfx == null) return true;
            var entries = CustomSfxMonsters.Entries;
            if (entries.Count == 0) return true;
            // 避免 ToLower 分配，直接用 OrdinalIgnoreCase 比较
            foreach (var e in entries)
                if (sfx.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var nativePath = MapToNativePath(sfx);
                    if (nativePath != null) NativeSfxPlayer.Play(nativePath);
                    return false;
                }

            return true;
        }
    }

    [HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.PlayDamage))]
    public static class SfxCmdPlayDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MonsterModel? monster, int damageAmount)
        {
            if (monster == null) return true;
            var entry = monster.Id.Entry.ToLowerInvariant();
            if (!CustomSfxMonsters.Entries.Contains(entry)) return true;
            var nativePath = MapToNativePathForMonster(entry, "hurt");
            if (nativePath != null) NativeSfxPlayer.Play(nativePath);
            return false;
        }
    }

    [HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.PlayDeath), typeof(MonsterModel))]
    public static class SfxCmdPlayDeathPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MonsterModel? monster)
        {
            if (monster == null) return true;
            var entry = monster.Id.Entry.ToLowerInvariant();
            if (!CustomSfxMonsters.Entries.Contains(entry)) return true;
            var nativePath = MapToNativePathForMonster(entry, "die");
            if (nativePath != null) NativeSfxPlayer.Play(nativePath);
            return false;
        }
    }

    [HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))]
    public static class MonsterCreateVisualsPostfix
    {
        public static void Postfix(MonsterModel __instance)
        {
            var entry = __instance.Id.Entry.ToLowerInvariant();
            if (CustomSfxMonsters.Entries.Contains(entry)) PreloadMonsterSfx(entry);
        }
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
    public static class PlayCustomMusicPatch
    {
        private static readonly FieldInfo _proxyField = AccessTools.Field(typeof(NRunMusicController), "_proxy");
        private static readonly HashSet<string> _loadedBankActs = new();

        [HarmonyPrefix]
        public static bool Prefix(NRunMusicController __instance, string customMusic)
        {
            if (customMusic == null) return true;

            // res:// → 原生播放
            if (customMusic.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                NativeSfxPlayer.PlayMusic(customMusic);
                return false;
            }

            // event:/music/ → 预加载对应 act bank（每个 act 只加载一次），然后交给 FMOD
            if (customMusic.StartsWith("event:/music/", StringComparison.OrdinalIgnoreCase))
            {
                var actKey = customMusic.Contains("/act1_b1") || customMusic.Contains("/act1_b_") ? "act1_b"
                    : customMusic.Contains("/act1") ? "act1"
                    : customMusic.Contains("/act2") ? "act2"
                    : customMusic.Contains("/act3") ? "act3"
                    : null;

                if (actKey != null && _loadedBankActs.Add(actKey))
                {
                    var proxy = _proxyField?.GetValue(__instance) as Node;
                    if (proxy != null)
                    {
                        var bankPaths = actKey == "act1_b"
                            ? new[] { "res://banks/desktop/act1_b1.bank" }
                            : actKey == "act1"
                                ? new[] { "res://banks/desktop/act1_a1.bank", "res://banks/desktop/act1_a2.bank" }
                                : actKey == "act2"
                                    ? new[] { "res://banks/desktop/act2_a1.bank", "res://banks/desktop/act2_a2.bank" }
                                    : new[] { "res://banks/desktop/act3_a1.bank", "res://banks/desktop/act3_a2.bank" };
                        var arr = new Array();
                        foreach (var bp in bankPaths) arr.Add(bp);
                        proxy.Call("load_act_banks", arr);
                    }
                }
            }

            return true; // FMOD 处理
        }
    }

    [HarmonyPatch(typeof(NCreature), nameof(NCreature.StartDeathAnim))]
    public static class StartDeathAnimPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCreature __instance, bool shouldRemove)
        {
            var entity = __instance.Entity;
            var monster = entity?.Monster;
            if (monster == null) return;
            var entry = monster.Id.Entry.ToLowerInvariant();
            if (!CustomSfxMonsters.Entries.Contains(entry)) return;
            var hash = monster.GetHashCode();
            lock (_deathSfxPlayed)
            {
                if (!_deathSfxPlayed.Add(hash)) return;
            }

            if (monster.HasDeathSfx)
            {
                var nativePath = MapToNativePathForMonster(entry, "die");
                if (nativePath != null) NativeSfxPlayer.Play(nativePath);
            }
        }
    }
}