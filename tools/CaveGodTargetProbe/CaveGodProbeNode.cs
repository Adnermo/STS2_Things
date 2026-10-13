using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Monsters;
using STS2_Things.Encounters;
using STS2_Things.Powers;
using STS2_Things.Cards;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Visuals;

public partial class CaveGodProbeNode : Node
{
    private static readonly Assembly ImplementationAssembly = typeof(STS2_ThingsInit).Assembly;
    private static bool _scriptsLookedUp;
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    private sealed record Fixture(CombatState State, ThingsCaveGodBody Body, ThingsCaveGodLeftHand Left, ThingsCaveGodRightHand Right);
    private static Task Hit(Fixture s, Creature target, decimal amount = 10000m) =>
        CreatureCmd.Damage(Choice, target, amount, ValueProp.Unpowered | ValueProp.Unblockable, s.State.Players[0].Creature);
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        try
        {
            TestMode.TurnOnInternal();
            Assert(ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK")!), "Native pack mounts.");
            Assert(ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")!), "Things pack mounts.");
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            LocManager.Initialize();
            ImplementationAssembly.GetType("STS2_Things.Compatibility.Sts2VersionCompatibility", true)!
                .GetMethod("InitializeBeforeModelDatabase")!.Invoke(null, null);
            InitializeModelDb();
            EnsureScriptsLookedUp();
            await VerifyCaveGodRandomTargets();
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
        finally { DeactivateSyntheticCombat(); }
    }
    private static Fixture Scenario(int count = 1, int ascension = 0, bool mixedCharacters = false)
    {
        var players = Enumerable.Range(1, count).Select(i => !mixedCharacters ? Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i) : i switch
        {
            2 => Player.CreateForNewRun<Silent>(UnlockState.all, (ulong)i),
            3 => Player.CreateForNewRun<Regent>(UnlockState.all, (ulong)i),
            4 => Player.CreateForNewRun<Defect>(UnlockState.all, (ulong)i),
            _ => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i),
        }).ToArray();
        var run = RunState.CreateForTest(players, seed: "cavegod-regression", ascensionLevel: ascension);
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, new AscensionManager(ascension));
        var room = new CombatRoom(ModelDb.Encounter<CaveGodBossEncounter>().ToMutable(), run);
        run.PushRoom(room);
        foreach (var player in players)
        {
            player.ResetCombatState(); room.CombatState.AddPlayer(player);
            player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000);
        }
        var left = Add<ThingsCaveGodLeftHand>(room.CombatState, CaveGodBossEncounter.LeftHandSlot);
        var right = Add<ThingsCaveGodRightHand>(room.CombatState, CaveGodBossEncounter.RightHandSlot);
        var body = Add<ThingsCaveGodBody>(room.CombatState, CaveGodBossEncounter.BodySlot);
        ActivateSyntheticCombat(room.CombatState);
        left.EnsureAgingPower().GetAwaiter().GetResult();
        right.EnsureAgingPower().GetAwaiter().GetResult();
        return new Fixture(room.CombatState, body, left, right);
    }

    private static T Add<T>(CombatState state, string slot) where T : MonsterModel
    {
        var monster = (T)ModelDb.Monster<T>().ToMutable();
        var creature = state.CreateCreature(monster, CombatSide.Enemy, slot);
        state.AddCreature(creature);
        monster.SetUpForCombat();
        monster.RollMove(state.PlayerCreatures);
        return monster;
    }

    private static async Task Act(Fixture s)
    {
        s.State.CurrentSide = CombatSide.Enemy;
        await Hook.BeforeSideTurnStart(s.State, CombatSide.Enemy, s.State.Enemies.ToArray());
        foreach (var enemy in s.State.Enemies.ToList())
            if (enemy.IsAlive && enemy.Monster != null) await enemy.Monster.PerformMove();
        // The after-side hook was added in v111. These fixtures have no turn-end
        // powers; retain the hook where the native target exposes it.
        if (typeof(Hook).GetMethod("AfterSideTurnEnd") is { } afterSide)
            await (Task)afterSide.Invoke(null, [s.State, CombatSide.Enemy, s.State.Enemies.ToArray()])!;
        s.State.CurrentSide = CombatSide.Player;
        await Hook.BeforeSideTurnStart(s.State, CombatSide.Player, s.State.PlayerCreatures);
        foreach (var enemy in s.State.Enemies.ToList())
            if (enemy.IsAlive) enemy.Monster!.RollMove(s.State.PlayerCreatures);
        await Hook.AfterSideTurnStart(s.State, CombatSide.Player, s.State.PlayerCreatures);
    }

    private static void InitializeModelDb()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("System.IO.Hashing");

        Type[] modTypes = ImplementationAssembly.GetTypes();
        Type[] modelTypes = AbstractModelSubtypes.All
            .Concat(modTypes.Where(type =>
                !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type)))
            .Distinct()
            .ToArray();
        typeof(ReflectionHelper).GetField(
                "_modTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, modTypes);
        RegisterSyntheticMod(ImplementationAssembly);

        PropertyInfo? managerState = typeof(ModManager).GetProperty(
            "State", BindingFlags.Public | BindingFlags.Static);
        if (managerState != null)
        {
            managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
        }

        Type? assemblyInfo = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Modding.AssemblyInfo");
        assemblyInfo?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
        typeof(ModelDb).GetMethod("ResetForTest", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);

        MethodInfo init = typeof(ModelDb).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [modelTypes]);

        Type serializationCache = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache",
            throwOnError: true)!;
        serializationCache.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);

        // The normal loader calls STS2_ThingsInit before ModelDb setup. The
        // isolated probe intentionally builds its own model database, so apply
        // the narrowly scoped Ravenous adapter here as well.
        // CaveGod uses native model hooks; unrelated registration patches are not needed here.
    }

    private static void RegisterSyntheticMod(Assembly implementationAssembly)
    {
        Assembly gameAssembly = typeof(ModManager).Assembly;
        Type modType = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Modding.Mod", throwOnError: true)!;
        Type manifestType = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Modding.ModManifest", throwOnError: true)!;
        object mod = Activator.CreateInstance(modType)!;
        object manifest = Activator.CreateInstance(manifestType)!;
        manifestType.GetField("id")!.SetValue(manifest, "STS2_Things");
        manifestType.GetField("name")?.SetValue(manifest, "STS2_Things Gravetide Probe");
        manifestType.GetField("affectsGameplay")?.SetValue(manifest, true);
        modType.GetField("path")!.SetValue(mod, "probe://STS2_Things");
        modType.GetField("manifest")!.SetValue(mod, manifest);
        FieldInfo stateField = modType.GetField("state")!;
        stateField.SetValue(mod, Enum.Parse(stateField.FieldType, "Loaded"));

        if (modType.GetField("assemblies")?.GetValue(mod) is IList assemblies)
        {
            assemblies.Add(implementationAssembly);
        }
        else
        {
            modType.GetField("assembly")!.SetValue(mod, implementationAssembly);
        }

        IList mods = (IList)typeof(ModManager)
            .GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        mods.Clear();
        mods.Add(mod);
    }

    private static void ActivateSyntheticCombat(CombatState state)
    {
        LocalContext.NetId = state.Players[0].NetId;
        CombatManager manager = CombatManager.Instance;
        FieldInfo? legacyStateField = typeof(CombatManager).GetField(
            "_state", BindingFlags.NonPublic | BindingFlags.Instance);
        if (legacyStateField != null)
        {
            legacyStateField.SetValue(manager, state);
            typeof(CombatManager).GetField(
                    "<IsInProgress>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(manager, true);
        }
        else
        {
            FieldInfo turnStateField = typeof(CombatManager).GetField(
                    "_turnState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(typeof(CombatManager).FullName, "_turnState");
            object turnState = Activator.CreateInstance(
                    turnStateField.FieldType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: [state],
                    culture: null)
                ?? throw new InvalidOperationException("Could not create the V111 combat turn state.");
            turnState.GetType().GetProperty("IsInProgress")!.SetValue(turnState, true);
            turnState.GetType().GetProperty("IsStarting")!.SetValue(turnState, false);
            turnStateField.SetValue(manager, turnState);
        }
        state.MultiplayerScalingModel?.OnCombatEntered(state);
        manager.StateTracker.SetState(state);
    }

    private static void DeactivateSyntheticCombat()
    {
        LocalContext.NetId = null;
        CombatManager manager = CombatManager.Instance;
        FieldInfo? legacyStateField = typeof(CombatManager).GetField(
            "_state", BindingFlags.NonPublic | BindingFlags.Instance);
        if (legacyStateField != null)
        {
            typeof(CombatManager).GetField(
                    "<IsInProgress>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)?
                .SetValue(manager, false);
            legacyStateField.SetValue(manager, null);
            return;
        }

        FieldInfo turnStateField = typeof(CombatManager).GetField(
                "_turnState", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(CombatManager).FullName, "_turnState");
        object? turnState = turnStateField.GetValue(manager);
        turnState?.GetType().GetMethod("Cancel", BindingFlags.Public | BindingFlags.Instance)?
            .Invoke(turnState, null);
        turnStateField.SetValue(manager, null);
    }

    private static void EnsureRuntimeDependency(string assemblyName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
        {
            return;
        }

        string path = FindRuntimeDependencyPath(assemblyName)
            ?? throw new FileNotFoundException(
                $"Could not locate probe runtime dependency {assemblyName}.dll.");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    private static Assembly? ResolveRuntimeDependency(
        AssemblyLoadContext context,
        AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }
        string? path = FindRuntimeDependencyPath(assemblyName.Name);
        return path == null ? null : context.LoadFromAssemblyPath(path);
    }

    private static string? FindRuntimeDependencyPath(string assemblyName)
    {
        string configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? assemblyDirectory = Path.GetDirectoryName(
            typeof(CaveGodProbeNode).Assembly.Location);
        string[] candidates =
        [
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", configuration,
                $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug",
                $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release",
                $"{assemblyName}.dll"),
            Path.Combine(assemblyDirectory ?? string.Empty, $"{assemblyName}.dll")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void EnsureScriptsLookedUp()
    {
        if (_scriptsLookedUp) return;
        _scriptsLookedUp = true;
        try { Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly); } catch { }
        try { Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(ImplementationAssembly); } catch { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
