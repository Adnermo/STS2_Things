using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Hooks;

public partial class ActLikeIt2CompatibilityProbeNode : Node
{
    private const string ActLikeIt2CompatHarmonyId = "Adnermo.STS2_Things.Compatibility.ActLikeIt2";
    private int _checks;

    public override void _Ready()
    {
        try
        {
            Require(
                OS.GetUserDataDir().Contains("ActLikeIt2 Compatibility Probe", StringComparison.Ordinal),
                "Refusing to run outside isolated probe profile.");

            string[] args = OS.GetCmdlineUserArgs();
            Require(args.Length >= 6,
                "Expected <gamePck> <thingsPck> <loaderDll> <variantDll> <actLikeItPck> <--standalone|--things-first|--actlikeit-first>.");

            string gamePck = Path.GetFullPath(args[0]);
            string thingsPck = Path.GetFullPath(args[1]);
            string loaderDll = Path.GetFullPath(args[2]);
            string variantDll = Path.GetFullPath(args[3]);
            string actLikeItPck = Path.GetFullPath(args[4]);
            string mode = args[5];

            VerifyZeroHardDependency();
            Require(ProjectSettings.LoadResourcePack(gamePck), "Native game PCK mounts.");
            Require(ProjectSettings.LoadResourcePack(thingsPck), "STS2_Things PCK mounts.");
            VerifyForkLocalizationInPck();

            ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(e => e.Key, e => (object?)e.Default));

            AssemblyLoadContext loadContext =
                AssemblyLoadContext.GetLoadContext(typeof(ModelDb).Assembly)
                ?? AssemblyLoadContext.Default;
            loadContext.Resolving += ResolveRuntimeDependency;
            EnsureRuntimeDependency("System.IO.Hashing");

            if (mode == "--standalone")
            {
                RunStandaloneMode();
            }
            else if (mode is "--things-first" or "--actlikeit-first")
            {
                Require(ProjectSettings.LoadResourcePack(actLikeItPck), "ActLikeIt2 PCK mounts.");
                RunCombinedMode(loadContext, loaderDll, variantDll, thingsFirst: mode == "--things-first");
            }
            else
            {
                throw new ArgumentException($"Unknown probe mode: {mode}");
            }

            GD.Print($"ACTLIKEIT2_COMPAT_PASS mode={mode} checks={_checks}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            Exception cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            GD.PrintErr($"ACTLIKEIT2_COMPAT_FAIL {cause}");
            GetTree().Quit(1);
        }
    }

    private void VerifyZeroHardDependency()
    {
        AssemblyName[] refs = typeof(ThingsDepths).Assembly.GetReferencedAssemblies();
        Require(
            !refs.Any(r => r.Name != null && r.Name.StartsWith("ActLikeIt2", StringComparison.OrdinalIgnoreCase)),
            "STS2_Things.dll must have zero compile-time dependency on ActLikeIt2.");
    }

    private void VerifyForkLocalizationInPck()
    {
        foreach (string lang in new[] { "zhs", "eng" })
        {
            string resPath = $"res://STS2_Things/localization/{lang}/acts.json";
            Require(Godot.FileAccess.FileExists(resPath), $"Expected localization file in PCK: {resPath}");
            string json = Godot.FileAccess.GetFileAsString(resPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            string? title = doc.RootElement.GetProperty("THINGS_DEPTHS.title").GetString();
            string? forkDesc = doc.RootElement.GetProperty("THINGS_DEPTHS.fork_description").GetString();
            Require(!string.IsNullOrWhiteSpace(title), $"{lang} THINGS_DEPTHS.title must be non-empty.");
            Require(!string.IsNullOrWhiteSpace(forkDesc), $"{lang} THINGS_DEPTHS.fork_description must be non-empty.");
            Require(!string.Equals(title, forkDesc, StringComparison.Ordinal),
                $"{lang} fork_description must differ from title to avoid duplicate labels.");
        }
    }

    private void RunStandaloneMode()
    {
        InitializeThingsCompatibilityAndPatches();
        InitializeModelDb(extraAssemblies: []);

        bool hasCompatPatch = Harmony.GetAllPatchedMethods()
            .Select(Harmony.GetPatchInfo)
            .Any(info => info != null && info.Owners.Contains(ActLikeIt2CompatHarmonyId));
        Require(!hasCompatPatch, "Standalone mode must install zero ActLikeIt2 compatibility patches.");

        Require(ModelDb.Acts.OfType<ThingsDepths>().Count() == 1, "ModelDb.Acts contains ThingsDepths once.");

        ThingsModConfig.SetValue(ThingsModConfig.FeatureGuaranteedDepthsEnabled, true);
        ActModel[] acts = ActModel.GetRandomList(new Rng(12345u), UnlockState.all, isMultiplayer: false).ToArray();
        Require(acts.Length == 3 && acts[1] is ThingsDepths, "Standalone Guaranteed Depths places ThingsDepths in Act 2.");
    }

    private void RunCombinedMode(
        AssemblyLoadContext loadContext,
        string loaderDll,
        string variantDll,
        bool thingsFirst)
    {
        // Load root loader stub (AssemblyName == "ActLikeIt2.Loader") first and verify it is ignored cleanly.
        Assembly loaderAssembly = loadContext.LoadFromAssemblyPath(loaderDll);
        Require(loaderAssembly.GetName().Name == "ActLikeIt2.Loader",
            $"Expected loader assembly name ActLikeIt2.Loader, got {loaderAssembly.GetName().Name}.");

        Assembly actLikeItAssembly;
        if (thingsFirst)
        {
            InitializeThingsCompatibilityAndPatches();
            Require(!HasActLikeItCompatPatches(),
                "ActLikeIt2.Loader stub alone must not install ActLikeIt2 compatibility patches.");

            actLikeItAssembly = loadContext.LoadFromAssemblyPath(variantDll);
            InvokeActLikeItInitialize(actLikeItAssembly);
        }
        else
        {
            actLikeItAssembly = loadContext.LoadFromAssemblyPath(variantDll);
            InvokeActLikeItInitialize(actLikeItAssembly);
            InitializeThingsCompatibilityAndPatches();
        }

        Require(HasActLikeItCompatPatches(),
            "ActLikeIt2 compatibility patches must be active once ActLikeIt2 variant is loaded.");

        InitializeModelDb(extraAssemblies: [actLikeItAssembly]);

        Require(ModelDb.Acts.OfType<ThingsDepths>().Count() == 1,
            "ModelDb.Acts contains ThingsDepths exactly once with ActLikeIt2 installed.");

        Type registryType = actLikeItAssembly.GetType("ActLikeIt2.ActRegistry", throwOnError: true)!;
        Type selectionLogicType = actLikeItAssembly.GetType("ActLikeIt2.Runtime.ActSelectionLogic", throwOnError: true)!;
        Type runStateActUtilType = actLikeItAssembly.GetType("ActLikeIt2.Runtime.RunStateActUtil", throwOnError: true)!;
        Type enterActForkPatchType = actLikeItAssembly.GetType("ActLikeIt2.Patches.EnterActForkPatch", throwOnError: true)!;

        // 1. Verify ActRegistry registration & OptionDescription
        var slot2Regs = ((IEnumerable)registryType
            .GetMethod("GetRegistrationsForSlot", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [2])!).Cast<object>().ToArray();
        object? depthsReg = slot2Regs.FirstOrDefault(r =>
            string.Equals((string)r.GetType().GetProperty("IdEntry")!.GetValue(r)!, "THINGS_DEPTHS", StringComparison.OrdinalIgnoreCase));
        Require(depthsReg is not null, "ThingsDepths must be registered in ActLikeIt2 slot 2.");

        var optionDesc = (LocString?)depthsReg!.GetType().GetProperty("OptionDescription")!.GetValue(depthsReg);
        Require(optionDesc is not null &&
                optionDesc.LocTable == "acts" &&
                optionDesc.LocEntryKey == "THINGS_DEPTHS.fork_description",
            "ThingsDepths registration must carry localized OptionDescription.");

        // 2. Default mode (FeatureGuaranteedDepthsEnabled = false): ActLikeIt2 Fork flow works cleanly
        ThingsModConfig.SetValue(ThingsModConfig.FeatureGuaranteedDepthsEnabled, false);
        ActModel[] rolledDefault = ActModel.GetRandomList(new Rng(2026u), UnlockState.all, isMultiplayer: false).ToArray();
        Require(rolledDefault.Length == 3 && rolledDefault[1] is Hive,
            $"Default ActLikeIt2 roll should place Hive in slot 2, got {rolledDefault[1].Id.Entry}.");

        RunState stdRun = CreateTestRunState(rolledDefault, GameMode.Standard, "ACTLIKEIT_STD");
        var selectableActs = ((IEnumerable)registryType
            .GetMethod("GetSelectableActs", BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(IRunState)])!
            .Invoke(null, [1, stdRun])!).Cast<ActModel>().ToArray();
        Require(selectableActs.Length >= 2 &&
                selectableActs.Any(a => a is Hive) &&
                selectableActs.Any(a => a is ThingsDepths),
            $"Act 2 selectable acts must include Hive and ThingsDepths; got [{string.Join(", ", selectableActs.Select(a => a.Id.Entry))}].");

        bool shouldOpenFork = (bool)selectionLogicType
            .GetMethod("ShouldOpenFork", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [1, stdRun.Acts.Count, selectableActs.Length])!;
        Require(shouldOpenFork, "ActSelectionLogic.ShouldOpenFork must return true for Act 2 with ThingsDepths registered.");

        // Verify Fork selection group uses THINGS_DEPTHS.fork_description (not duplicated title)
        var groups = ((IEnumerable)registryType
            .GetMethod("GetSelectableActGroups", BindingFlags.NonPublic | BindingFlags.Static, [typeof(int), typeof(IRunState)])!
            .Invoke(null, [1, stdRun])!).Cast<object>().ToArray();
        object depthsGroup = groups.Single(g => ((IReadOnlyList<ActModel>)g.GetType().GetProperty("Acts")!.GetValue(g)!).Any(a => a is ThingsDepths));
        var groupDesc = (LocString)depthsGroup.GetType().GetProperty("Description")!.GetValue(depthsGroup)!;
        Require(groupDesc.LocEntryKey == "THINGS_DEPTHS.fork_description",
            $"Fork group description should be THINGS_DEPTHS.fork_description, got {groupDesc.LocEntryKey}.");

        // Swap ThingsDepths via ActLikeIt2's exact RunStateActUtil path
        SetRunManagerState(stdRun);
        runStateActUtilType.GetMethod("ReplaceActAt", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [stdRun, 1, ModelDb.Act<ThingsDepths>().ToMutable()]);
        runStateActUtilType.GetMethod("RegenerateActRooms", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [stdRun, 1]);

        Require(stdRun.Acts[1] is ThingsDepths, "RunStateActUtil.ReplaceActAt must install mutable ThingsDepths in Act 2.");
        var swappedDepths = (ThingsDepths)stdRun.Acts[1];
        Require(
            swappedDepths.BossEncounter is ThingsCaveGodBossEncounter
                or ThingsHumanFaceTowerBossEncounter
                or ThingsAbyssalInkmawBossEncounter,
            $"Regenerated ThingsDepths boss should be a Depths boss, got {swappedDepths.BossEncounter?.Id.Entry}.");
        var swappedEvents = (List<EventModel>)typeof(ThingsDepths)
            .GetProperty("Rooms", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(swappedDepths)!
            .GetType()
            .GetField("events", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(typeof(ThingsDepths).GetProperty("Rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(swappedDepths))!;
        Require(swappedEvents.Count > 0 && swappedEvents.All(e => e.GetType().Assembly != typeof(ActModel).Assembly),
            "DepthsEventPool must strip native shared events after ActLikeIt2 regenerates ThingsDepths rooms.");

        // 3. Guaranteed Depths mode (FeatureGuaranteedDepthsEnabled = true)
        ThingsModConfig.SetValue(ThingsModConfig.FeatureGuaranteedDepthsEnabled, true);
        ActModel[] rolledGuaranteed = ActModel.GetRandomList(new Rng(2026u), UnlockState.all, isMultiplayer: false).ToArray();
        Require(rolledGuaranteed.Length == 3 && rolledGuaranteed[1] is ThingsDepths,
            $"Guaranteed Depths roll must beat VanillaActRollPatch.Finalizer and put ThingsDepths in slot 2; got {rolledGuaranteed[1].Id.Entry}.");

        RunState guaranteedRun = CreateTestRunState(rolledGuaranteed, GameMode.Standard, "ACTLIKEIT_GUARANTEED");
        SetRunManagerState(guaranteedRun);

        MethodInfo forkPrefix = enterActForkPatchType.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] prefixArgs = [1, true, null];
        bool allowVanillaEnterAct = (bool)forkPrefix.Invoke(null, prefixArgs)!;
        Require(allowVanillaEnterAct && prefixArgs[2] is null,
            "Guaranteed Depths must bypass Act 2 Fork and allow vanilla EnterAct(1) directly into ThingsDepths.");

        // If a run started with Hive (toggled Guaranteed Depths mid-run), do NOT silently bypass Act 2 Fork!
        RunState midRunHive = CreateTestRunState(rolledDefault, GameMode.Standard, "ACTLIKEIT_MIDRUN");
        SetRunManagerState(midRunHive);
        // Test BeforeEnterActForkPrefix directly without triggering Godot scene FadeOut from RunForkThenEnterAct:
        MethodInfo ourPrefix = typeof(ThingsDepths).Assembly
            .GetType("STS2_Things.Compatibility.Mods.ActLikeIt2Compatibility", throwOnError: true)!
            .GetMethod("BeforeEnterActForkPrefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] midRunArgs = [1, false];
        bool delegateToActLikeIt = (bool)ourPrefix.Invoke(null, midRunArgs)!;
        Require(delegateToActLikeIt,
            "BeforeEnterActForkPrefix must NOT bypass Act 2 Fork when runState.Acts[1] is Hive (mid-run toggle).");

        // Act 1 / Act 3 must NEVER be bypassed by BeforeEnterActForkPrefix
        SetRunManagerState(guaranteedRun);
        object?[] act1Args = [0, false];
        object?[] act3Args = [2, false];
        Require((bool)ourPrefix.Invoke(null, act1Args)!, "Act 1 must not be bypassed by Guaranteed Depths.");
        Require((bool)ourPrefix.Invoke(null, act3Args)!, "Act 3 must not be bypassed by Guaranteed Depths.");

#if STS2_V113_1
        // 4. Blitz mode room generation preservation on v0.113.1
        ThingsModConfig.SetValue(ThingsModConfig.FeatureGuaranteedDepthsEnabled, false);
        RunState blitzRun = CreateTestRunState(rolledDefault, GameMode.Blitz, "ACTLIKEIT_BLITZ");
        SetRunManagerState(blitzRun);
        runStateActUtilType.GetMethod("ReplaceActAt", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [blitzRun, 1, ModelDb.Act<ThingsDepths>().ToMutable()]);
        runStateActUtilType.GetMethod("RegenerateActRooms", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [blitzRun, 1]);

        var blitzDepths = (ThingsDepths)blitzRun.Acts[1];
        EncounterModel firstMonster = blitzDepths.PullNextEncounter(RoomType.Monster);
        blitzDepths.MarkRoomVisited(RoomType.Monster);
        EncounterModel secondMonster = blitzDepths.PullNextEncounter(RoomType.Monster);
        Require(firstMonster.IsWeak && !secondMonster.IsWeak,
            $"Blitz ThingsDepths should have 1 weak + 1 regular encounter, got first={firstMonster.Id.Entry}(weak={firstMonster.IsWeak}), second={secondMonster.Id.Entry}(weak={secondMonster.IsWeak}).");
#endif

        SetRunManagerState(null);
    }

    private static void InitializeThingsCompatibilityAndPatches()
    {
        Assembly thingsAssembly = typeof(ThingsDepths).Assembly;
        Type modCompat = thingsAssembly.GetType("STS2_Things.Compatibility.ModCompatibility", throwOnError: true)!;
        modCompat.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        var harmony = new Harmony("Adnermo.STS2_Things");
        harmony.PatchAll(thingsAssembly);
        typeof(ThingsDepths).Assembly
            .GetType("STS2_Things.Hooks.DepthsActCatalogPatch", throwOnError: true)!
            .GetMethod("InvalidateActsByIndexCache", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null);
    }

    private static void InvokeActLikeItInitialize(Assembly actLikeItAssembly)
    {
        Type modType = actLikeItAssembly.GetType("ActLikeIt2.ActLikeIt2Mod", throwOnError: true)!;
        modType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
    }

    private static bool HasActLikeItCompatPatches()
    {
        return Harmony.GetAllPatchedMethods()
            .Select(Harmony.GetPatchInfo)
            .Any(info => info != null && info.Owners.Contains(ActLikeIt2CompatHarmonyId));
    }

    private static RunState CreateTestRunState(ActModel[] acts, GameMode mode, string seed)
    {
        Player player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1UL);
        ActModel[] mutableActs = acts.Select(a => a.ToMutable()).ToArray();
        return ProbeRuntimeApi.CreateForNewRun([player], mutableActs, [], mode, 0, seed);
    }

    private static void SetRunManagerState(RunState? state)
    {
        FieldInfo? stateField = typeof(RunManager).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
        if (stateField is not null)
        {
            stateField.SetValue(RunManager.Instance, state);
            return;
        }

        PropertyInfo? stateProp = typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        stateProp?.SetValue(RunManager.Instance, state);
    }

    private static void InitializeModelDb(Assembly[] extraAssemblies)
    {
        Assembly thingsAssembly = typeof(ThingsDepths).Assembly;
        Assembly[] allModAssemblies = new[] { thingsAssembly }.Concat(extraAssemblies).ToArray();
        Type[] modTypes = allModAssemblies.SelectMany(a => a.GetTypes()).ToArray();
        Type[] modelTypes = AbstractModelSubtypes.All
            .Concat(modTypes.Where(t => !t.IsAbstract && typeof(AbstractModel).IsAssignableFrom(t)))
            .Distinct()
            .ToArray();

        typeof(ReflectionHelper).GetField("_modTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, modTypes);
        RegisterSyntheticMods(allModAssemblies);

        PropertyInfo? managerState = typeof(ModManager).GetProperty("State", BindingFlags.Public | BindingFlags.Static);
        if (managerState != null)
        {
            managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
        }

        typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo")
            ?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null);
        typeof(ModelDb).GetMethod("ResetForTest", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

        MethodInfo init = typeof(ModelDb).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [modelTypes]);

        Type serializationCache = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache",
            throwOnError: true)!;
        serializationCache.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
    }

    private static void RegisterSyntheticMods(Assembly[] assemblies)
    {
        Type modType = typeof(ModManager).Assembly.GetType("MegaCrit.Sts2.Core.Modding.Mod", throwOnError: true)!;
        Type manifestType = typeof(ModManager).Assembly.GetType("MegaCrit.Sts2.Core.Modding.ModManifest", throwOnError: true)!;
        IList mods = (IList)typeof(ModManager).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        mods.Clear();

        foreach (Assembly asm in assemblies)
        {
            string id = asm.GetName().Name ?? "Mod";
            object mod = Activator.CreateInstance(modType)!;
            object manifest = Activator.CreateInstance(manifestType)!;
            manifestType.GetField("id")!.SetValue(manifest, id);
            manifestType.GetField("name")?.SetValue(manifest, id);
            manifestType.GetField("affectsGameplay")?.SetValue(manifest, true);
            modType.GetField("path")!.SetValue(mod, $"probe://{id}");
            modType.GetField("manifest")!.SetValue(mod, manifest);
            FieldInfo stateField = modType.GetField("state")!;
            stateField.SetValue(mod, Enum.Parse(stateField.FieldType, "Loaded"));

            FieldInfo? assembliesField = modType.GetField("assemblies");
            if (assembliesField?.GetValue(mod) is IList modAssemblies)
            {
                modAssemblies.Add(asm);
            }
            else
            {
                modType.GetField("assembly")!.SetValue(mod, asm);
            }

            mods.Add(mod);
        }
    }

    private static void EnsureRuntimeDependency(string assemblyName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(
                a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal)))
        {
            return;
        }

        string path = FindRuntimeDependencyPath(assemblyName)
            ?? throw new FileNotFoundException($"Could not locate probe runtime dependency {assemblyName}.dll.");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    private static Assembly? ResolveRuntimeDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        string? path = FindRuntimeDependencyPath(assemblyName.Name);
        return path == null ? null : context.LoadFromAssemblyPath(path);
    }

    private static string? FindRuntimeDependencyPath(string assemblyName)
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? assemblyDirectory = Path.GetDirectoryName(typeof(ActLikeIt2CompatibilityProbeNode).Assembly.Location);
        string[] candidates =
        [
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug", $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release", $"{assemblyName}.dll"),
            Path.Combine(assemblyDirectory ?? string.Empty, $"{assemblyName}.dll"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _checks++;
    }
}
