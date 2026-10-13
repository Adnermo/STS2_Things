using System.Reflection;
using System.Collections.Concurrent;
using HarmonyLib;
using STS2_Things.Audio;

namespace STS2_Things.Compatibility.Mods;

/// <summary>MomoLib 1.3.7 treats every resource music path as an MP3.</summary>
internal static class MomoLibCompatibility
{
    private static readonly ConcurrentDictionary<MethodBase, Action> StopPreviousMusic = new();
    internal static void Install(Assembly assembly, Harmony harmony)
    {
        MethodInfo? prefix = assembly.GetType("MomoLib.Patch.MomoCustomBgmPatch", false)
            ?.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        ParameterInfo[]? args = prefix?.GetParameters();
        if (prefix?.ReturnType != typeof(bool) || args is not { Length: 3 } || args[1].ParameterType != typeof(string))
            throw new NotSupportedException("MomoLib custom music API differs from the verified contract.");
        MethodInfo? stop = prefix.DeclaringType?.GetMethod("StopBgm", BindingFlags.NonPublic | BindingFlags.Static);
        if (stop?.ReturnType != typeof(void) || stop.GetParameters().Length != 0)
            throw new NotSupportedException("MomoLib music cleanup API differs from the verified contract.");
        StopPreviousMusic[prefix] = stop.CreateDelegate<Action>();
        harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(MomoLibCompatibility), nameof(BeforeMomoPrefix)));
    }

    private static bool BeforeMomoPrefix(string __1, ref bool __result, MethodBase __originalMethod)
    {
        if (!string.Equals(__1, CustomMusicPlayPatch.GravetideTheme, StringComparison.Ordinal)) return true;
        if (StopPreviousMusic.TryGetValue(__originalMethod, out Action? stop)) stop();
        // Defer this exact Things-owned WAV to CustomMusicPlayPatch. Everything
        // else, including other mods' MP3 music, retains MomoLib's own behavior.
        __result = true;
        return false;
    }
}
