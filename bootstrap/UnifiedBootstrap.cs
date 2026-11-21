using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Modding;

namespace STS2_Things.Bootstrap;

[ModInitializer(nameof(Initialize))]
public static class UnifiedBootstrap
{
    private const string ModId = "STS2_Things";
    private const string V1111Target = "v111.1";
    private const string V1131Target = "v113.1";
    private const string V1111Resource = "STS2_Things.Implementations.v111.1.dll";
    private const string V1131Resource = "STS2_Things.Implementations.v113.1.dll";
    private const string ImplementationAssemblyName = "STS2_Things";

    private static readonly object InitLock = new();
    private static Assembly? _gameAssembly;
    private static Assembly? _implementationAssembly;
    private static bool _initialized;

    public static string? SelectedTarget { get; private set; }

    public static void Initialize()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                Console.WriteLine("[STS2_Things] Unified bootstrap duplicate initialization ignored.");
                return;
            }

            _gameAssembly = typeof(ModInitializerAttribute).Assembly;
            SelectedTarget = DetectTarget(_gameAssembly);
            _implementationAssembly = LoadImplementation(SelectedTarget);

            AssociateImplementation(_gameAssembly, _implementationAssembly);
            InvokeImplementationInitializer(_implementationAssembly);
            _initialized = true;
            Console.WriteLine(
                $"[STS2_Things] Unified package selected {SelectedTarget} implementation " +
                $"({_implementationAssembly.GetName().Name}).");
        }
    }

    public static string SelectImplementationResource(Assembly gameAssembly)
    {
        return DetectTarget(gameAssembly) switch
        {
            V1111Target => V1111Resource,
            V1131Target => V1131Resource,
            _ => throw new InvalidOperationException("Unsupported STS2 target.")
        };
    }

    private static string DetectTarget(Assembly gameAssembly)
    {
        var abstractModel = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Models.AbstractModel", throwOnError: true)!;
        var parameterCounts = abstractModel
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "ModifyDamageMultiplicative")
            .Select(method => method.GetParameters().Length)
            .Distinct()
            .Order()
            .ToArray();

        bool hasCombatId = gameAssembly.GetType("MegaCrit.Sts2.Core.Combat.CombatId") is not null;
        bool hasHandshakeManager = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Connection.HandshakeManager") is not null;
        var deathHooks = abstractModel
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "AfterDeath")
            .ToArray();
        var standardMerchantRoom = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Nodes.Rooms.NStandardMerchantRoom");
        var monsterModel = gameAssembly.GetType("MegaCrit.Sts2.Core.Models.MonsterModel");
        bool hasCompendiumAnimHook = monsterModel?.GetMethod(
            "BeforeAnimPlayedInCompendium",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null) is not null;

        if (parameterCounts.SequenceEqual([6]) &&
            hasCombatId &&
            hasHandshakeManager &&
            deathHooks.Length == 1)
        {
            var deathParameters = deathHooks[0].GetParameters();
            if (deathParameters.Length == 5 &&
                deathParameters[2].ParameterType.FullName == "MegaCrit.Sts2.Core.Models.CardModel" &&
                standardMerchantRoom is not null &&
                hasCompendiumAnimHook)
            {
                return V1131Target;
            }

            if (deathParameters.Length == 4 &&
                deathParameters[2].ParameterType == typeof(bool) &&
                standardMerchantRoom is null)
            {
                return V1111Target;
            }
        }

        throw new NotSupportedException(
            "Unsupported STS2 runtime API fingerprint " +
            $"(ModifyDamageMultiplicative=[{string.Join(", ", parameterCounts)}], " +
            $"CombatId={hasCombatId}, HandshakeManager={hasHandshakeManager}, " +
            $"AfterDeathOverloads={deathHooks.Length}, " +
            $"NStandardMerchantRoom={standardMerchantRoom is not null}, " +
            $"BeforeAnimPlayedInCompendium={hasCompendiumAnimHook}). " +
            "Supported targets: v0.111.1 and v0.113.1.");
    }

    private static Assembly LoadImplementation(string target)
    {
        var resourceName = target == V1111Target ? V1111Resource : V1131Resource;
        var bootstrapAssembly = typeof(UnifiedBootstrap).Assembly;
        using var stream = bootstrapAssembly.GetManifestResourceStream(resourceName)
            ?? throw new MissingManifestResourceException(
                $"Embedded implementation resource is missing: {resourceName}");
        var loadContext = AssemblyLoadContext.GetLoadContext(bootstrapAssembly)
            ?? AssemblyLoadContext.Default;
        var implementation = loadContext.LoadFromStream(stream);
        if (!string.Equals(
                implementation.GetName().Name,
                ImplementationAssemblyName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Embedded {target} implementation has assembly name " +
                $"'{implementation.GetName().Name}', expected '{ImplementationAssemblyName}'.");
        }
        return implementation;
    }

    private static void AssociateImplementation(
        Assembly gameAssembly,
        Assembly implementationAssembly)
    {
        var modManager = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Modding.ModManager", throwOnError: true)!;
        var associate = modManager.GetMethod(
            "AssociateAssemblyWithMod",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(Assembly)],
            modifiers: null)
            ?? throw new MissingMethodException(
                modManager.FullName, "AssociateAssemblyWithMod(string, Assembly)");
        InvokeAndUnwrap(associate, null, [ModId, implementationAssembly]);
    }

    private static void InvokeImplementationInitializer(Assembly implementationAssembly)
    {
        var initializerType = implementationAssembly.GetType(
            "STS2_ThingsInit", throwOnError: true)!;
        var initializer = initializerType.GetMethod(
            "Initialize", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(initializerType.FullName, "Initialize");
        InvokeAndUnwrap(initializer, null, null);
    }

    private static object? InvokeAndUnwrap(
        MethodInfo method,
        object? instance,
        object?[]? arguments)
    {
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
