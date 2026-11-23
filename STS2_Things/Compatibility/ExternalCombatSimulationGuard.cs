using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace STS2_Things.Compatibility;

/// <summary>
/// Detects third-party live-state combat preview simulators (such as <c>damage-preview</c>)
/// that execute combat hooks directly against the live <c>CombatState</c> while gating
/// presentation and rolling back vanilla <see cref="Creature"/> fields, without rolling back
/// custom private state on mod <c>MonsterModel</c> or <c>PowerModel</c> instances.
/// </summary>
public static class ExternalCombatSimulationGuard
{
    private static readonly AsyncLocal<int> SimulatedPreviewDepth = new();

    private static readonly HashSet<string> ExternalFlagMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsSimulating",
        "InSimulation",
        "IsActive",
        "Active",
        "SimulationActive",
        "_isSimulating",
        "_inSimulation",
        "_isActive",
        "_active",
        "_simulationActive",
    };

    private static readonly string[] IgnoredAssemblyPrefixes =
    [
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "Godot",
        "sts2",
        "0Harmony",
        "STS2_Things",
        "DepthsProbe",
        "MonoMod",
        "Sentry",
        "Vortice",
        "SharpGen",
        "SmartFormat",
        "Steamworks",
        "JetBrains",
    ];

    private static readonly object ProbeInitLock = new();
    private static readonly HashSet<Assembly> ScannedAssemblies = [];
    private static readonly HashSet<MemberInfo> RegisteredMembers = [];
    private static readonly List<Func<bool>> ProbeList = [];
    private static volatile bool _probesInitialized;
    private static volatile Func<bool>[] _externalFlagProbes = [];

    /// <summary>
    /// Enters a scoped test-only simulated external preview context for headless probes.
    /// </summary>
    public static IDisposable BeginSimulatedExternalPreviewForTest()
    {
        SimulatedPreviewDepth.Value = SimulatedPreviewDepth.Value + 1;
        return new SimulatedPreviewScope();
    }

    /// <summary>
    /// Returns <c>true</c> when an external live-state combat preview simulator is active
    /// so mod hooks can avoid mutating private combat state on live models.
    /// </summary>
    public static bool IsActive(Creature? creature = null)
    {
        if (SimulatedPreviewDepth.Value > 0)
            return true;

        if (creature != null && IsPresentationGateActive(creature))
            return true;

        return IsExternalSimulationFlagActive();
    }

    /// <summary>
    /// Signal A (generic, zero-reflection): in live play (<c>!TestMode.IsOn</c>),
    /// <see cref="Creature.GetCreatureNode"/> delegates to <c>NCombatRoom.Instance?.GetCreatureNode(this)</c>.
    /// Preview simulators such as <c>damage-preview</c> suppress <c>Creature.GetCreatureNode</c>
    /// via a presentation gate without removing the live <see cref="NCreature"/> from <see cref="NCombatRoom"/>.
    /// </summary>
    private static bool IsPresentationGateActive(Creature creature)
    {
        if (TestMode.IsOn)
            return false;

        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
            return false;

        NCreature? roomNode = room.GetCreatureNode(creature);
        return roomNode != null &&
            GodotObject.IsInstanceValid(roomNode) &&
            creature.GetCreatureNode() == null;
    }

    /// <summary>
    /// Signal B (cached reflection fallback): inspects loaded <c>DamagePreview</c> / <c>damage-preview</c>
    /// simulation gate flags once on load and caches zero-allocation delegates.
    /// </summary>
    private static bool IsExternalSimulationFlagActive()
    {
        EnsureExternalProbesInitialized();

        Func<bool>[] probes = _externalFlagProbes;
        for (int i = 0; i < probes.Length; i++)
        {
            try
            {
                if (probes[i]())
                    return true;
            }
            catch (Exception)
            {
                // Ignore third-party reflection failures so combat hooks never break.
            }
        }

        return false;
    }

    private static void EnsureExternalProbesInitialized()
    {
        if (_probesInitialized)
            return;

        lock (ProbeInitLock)
        {
            if (_probesInitialized)
                return;

            try
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    ScanAssemblyLocked(assembly);
            }
            catch (Exception)
            {
                // Best-effort discovery only.
            }

            _probesInitialized = true;
        }
    }

    private static void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        if (args.LoadedAssembly == null)
            return;

        lock (ProbeInitLock)
        {
            try
            {
                ScanAssemblyLocked(args.LoadedAssembly);
            }
            catch (Exception)
            {
                // Ignore late-load scanning errors.
            }
        }
    }

    private static void ScanAssemblyLocked(Assembly assembly)
    {
        if (!ScannedAssemblies.Add(assembly))
            return;

        string? assemblyName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName) || ShouldIgnoreAssembly(assemblyName))
            return;

        bool matchesPreviewAssembly = IsDamagePreviewAssemblyName(assemblyName);
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch (Exception)
        {
            return;
        }

        bool addedAny = false;
        foreach (Type? type in types)
        {
            if (type == null || !IsCandidateSimulationType(type, matchesPreviewAssembly))
                continue;

            addedAny |= RegisterStaticFlagProbesLocked(type);
        }

        if (addedAny)
            _externalFlagProbes = ProbeList.ToArray();
    }

    private static bool ShouldIgnoreAssembly(string assemblyName)
    {
        for (int i = 0; i < IgnoredAssemblyPrefixes.Length; i++)
        {
            string prefix = IgnoredAssemblyPrefixes[i];
            if (assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDamagePreviewAssemblyName(string assemblyName) =>
        assemblyName.Contains("DamagePreview", StringComparison.OrdinalIgnoreCase) ||
        assemblyName.Contains("damage-preview", StringComparison.OrdinalIgnoreCase) ||
        assemblyName.Contains("damage_preview", StringComparison.OrdinalIgnoreCase);

    private static bool IsCandidateSimulationType(Type type, bool matchesPreviewAssembly)
    {
        string? ns = type.Namespace;
        if (!string.IsNullOrEmpty(ns) &&
            ns.StartsWith("DamagePreview.Simulation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!matchesPreviewAssembly)
            return false;

        return type.Name.Contains("Simulation", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("PresentationGate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegisterStaticFlagProbesLocked(Type type)
    {
        const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        bool added = false;

        try
        {
            foreach (PropertyInfo property in type.GetProperties(StaticFlags))
            {
                if (property.PropertyType != typeof(bool) ||
                    property.GetIndexParameters().Length != 0 ||
                    !ExternalFlagMemberNames.Contains(property.Name))
                {
                    continue;
                }

                MethodInfo? getter = property.GetGetMethod(nonPublic: true);
                if (getter == null || !RegisteredMembers.Add(property))
                    continue;

                Func<bool> probe = CreateBoolPropertyProbe(getter);
                ProbeList.Add(probe);
                added = true;
            }

            foreach (FieldInfo field in type.GetFields(StaticFlags))
            {
                if (!ExternalFlagMemberNames.Contains(field.Name))
                    continue;

                if (field.FieldType == typeof(bool) && RegisteredMembers.Add(field))
                {
                    FieldInfo captured = field;
                    ProbeList.Add(() => captured.GetValue(null) is true);
                    added = true;
                }
                else if (field.FieldType == typeof(AsyncLocal<bool>) && RegisteredMembers.Add(field))
                {
                    FieldInfo captured = field;
                    ProbeList.Add(() => (captured.GetValue(null) as AsyncLocal<bool>)?.Value == true);
                    added = true;
                }
            }
        }
        catch (Exception)
        {
            // Ignore unreadable metadata on third-party types.
        }

        return added;
    }

    private static Func<bool> CreateBoolPropertyProbe(MethodInfo getter)
    {
        try
        {
            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
        }
        catch (Exception)
        {
            return () => getter.Invoke(null, null) is true;
        }
    }

    private sealed class SimulatedPreviewScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            SimulatedPreviewDepth.Value = Math.Max(0, SimulatedPreviewDepth.Value - 1);
        }
    }
}
