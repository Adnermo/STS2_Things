using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace STS2_Things.Compatibility;

/// <summary>Single startup entry for optional third-party compatibility.</summary>
internal static class ModCompatibility
{
    private static readonly object Gate = new();
    private static readonly HashSet<(Assembly, string)> Examined = [];
    private static bool _initialized;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) TryAttach(assembly);
        }
    }

    private static void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        lock (Gate) TryAttach(args.LoadedAssembly);
    }

    private static void TryAttach(Assembly assembly)
    {
        string name = assembly.GetName().Name ?? string.Empty;
        foreach (ModCompatibilityAdapter adapter in Mods.KnownModAdapters.All)
        {
            if (name != adapter.AssemblyName || !Examined.Add((assembly, adapter.Id))) continue;
            string harmonyId = "Adnermo.STS2_Things.Compatibility." + adapter.Id;
            var harmony = new Harmony(harmonyId);
            try
            {
                adapter.Install(assembly, harmony);
                Log.Info($"STS2_Things: compatibility enabled for {adapter.Id} ({name}).");
            }
            catch (Exception exception)
            {
                // Each adapter owns a separate Harmony ID. A changed optional
                // mod must not remove another adapter or abort Things startup.
                harmony.UnpatchAll(harmonyId);
                Log.Warn($"STS2_Things: compatibility skipped for {adapter.Id}: " + exception.Message);
            }
        }
    }
}

internal sealed record ModCompatibilityAdapter(string Id, string AssemblyName, Action<Assembly, Harmony> Install);
