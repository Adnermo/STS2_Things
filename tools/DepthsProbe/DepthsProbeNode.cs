using System.Reflection;
using System.Runtime.Loader;
using Godot;
using Godot.Bridge;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Visuals;

public partial class DepthsProbeNode : Node
{
    private static int _checks;
    private string _root = null!;
    private string _output = null!;
    private readonly List<Node> _resourceFixtures = [];
    public override void _Ready()
    {
        // Install dependency resolution before JIT touches the game's module initializer.
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("Sentry");
        EnsureRuntimeDependency("Sentry.Godot");
        _ = RunProbe();
    }

    private async Task RunProbe()
    {
        try
        {
            _root = System.Environment.GetEnvironmentVariable("THINGS_PROBE_ROOT")
                ?? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            _output = GetCmdOption("--output=")
                ?? System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT")
                ?? Path.Combine(_root, "build/depths/verification");
            Directory.CreateDirectory(_output);
            TestMode.TurnOnInternal();
            string gamePck = GetCmdOption("--game-pck=")
                ?? System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK")
                ?? "D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck";
            Assert(ProjectSettings.LoadResourcePack(gamePck), "Target game resources mounted.");
            string package = GetCmdOption("--pck=")
                ?? System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")
                ?? FirstExistingPath(
                    Path.Combine(_root, "build/v113.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/v111.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/unified/STS2_Things.pck"));
            Assert(ProjectSettings.LoadResourcePack(package, replaceFiles: true), "Packaged mod mounted.");
            AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
            EnsureRuntimeDependency("System.IO.Hashing");
            RegisterSyntheticMod(typeof(ThingsDepths).Assembly);
            // Same order as the mod loader: initialize Harmony before ModelDb builds its caches.
            STS2_ThingsInit.Initialize();
            InitializeModelDb();
            Assert(OS.GetUserDataDir().Contains("Depths",StringComparison.OrdinalIgnoreCase),"Isolated probe user data.");
            ResetSettings();
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language="zhs";LocManager.Initialize();
            if(OS.GetCmdlineUserArgs().Contains("--actual-victory-only"))
            {
                await VerifyActualDepthsVictorySave();await ReleaseProbeResources();
                GD.Print($"Depths real victory probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--ascension-only"))
            {
                await VerifyNativeActProgression();await ReleaseProbeResources();
                GD.Print($"Depths ascension probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--layout-only"))
            {
                await VerifyEncounterHudLayout();await ReleaseProbeResources();
                GD.Print($"Depths encounter layout probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--event-refresh-only"))
            {
                await VerifyEventRefresh();await ReleaseProbeResources();
                GD.Print($"Depths event refresh probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--cavegod-shackles-only"))
            {
                await VerifyCaveGodShackles();await ReleaseProbeResources();
                GD.Print($"Depths CaveGod Shackles probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--cavegod-doom-only"))
            {
                await VerifyCaveGodDoom();await ReleaseProbeResources();
                GD.Print($"Depths CaveGod Doom probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--native-migration-only"))
            {
                await VerifyNativeMigration();await ReleaseProbeResources();
                GD.Print($"Depths native migration probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--column-tracks-only"))
            {
                await VerifyColumnTracks();await ReleaseProbeResources();
                GD.Print($"Depths column tracks probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--balance-only"))
            {
                await SampleDepthsBalance();await ReleaseProbeResources();
                GD.Print($"Depths balance probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--fleeting-echo-combat-only"))
            {
                await VerifyFleetingEcho(render: false);await ReleaseProbeResources();
                GD.Print($"Depths Fleeting Echo combat probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--fleeting-echo-only"))
            {
                VerifyResources();
                await VerifyFleetingEcho();await ReleaseProbeResources();
                GD.Print($"Depths Fleeting Echo probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--column-variants-only"))
            {
                await VerifyStandaloneColumns();await ReleaseProbeResources();
                GD.Print($"Depths column variants probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--soul-roes-only"))
            {
                await VerifySoulRoesDeathBurst();await ReleaseProbeResources();
                GD.Print($"Depths Soul Roes probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--column-rules-only"))
            {
                await VerifyColumnRules();await ReleaseProbeResources();
                GD.Print($"Depths column rules probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--human-face-tower-only"))
            {
                await VerifyHumanFaceTowerRules();await ReleaseProbeResources();
                GD.Print($"Depths Human-Faced Tower probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--abyssal-inkmaw-only"))
            {
                await VerifyAbyssalInkmawRules();await ReleaseProbeResources();
                GD.Print($"Depths Abyssal Inkmaw probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--inkmaw-veil-only"))
            {
                await VerifyAbyssalInkBlackoutVeilLifecycleAsync();await ReleaseProbeResources();
                GD.Print($"Depths Abyssal Inkmaw veil lifecycle probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--fool-arrows-only"))
            {
                await VerifyFoolArrowRegressions();await ReleaseProbeResources();
                GD.Print($"Depths Fool arrow probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--leech-surrender-zindex-only"))
            {
                await VerifyLeechSurrenderAndZIndexRegressions();await ReleaseProbeResources();
                GD.Print($"Depths Leech/Surrender/ZIndex probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--depths-music-only"))
            {
                await VerifyDepthsMusicStages();await ReleaseProbeResources();
                GD.Print($"Depths music stage probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--modes-only"))
            {
                await VerifyBlitzAndDreadModes();await ReleaseProbeResources();
                GD.Print($"Depths Blitz & Dread modes probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--strong-only"))
            {
                VerifyResources();
                await VerifyStrongEncounters();await ReleaseProbeResources();
                GD.Print($"Depths strong encounters probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--effects-only"))
            {
                await VerifySpongeSprayRendering();
                VerifyBorrowedDeathAudio();
            await VerifyDepthsMusicStages();
                GD.Print($"Depths effects probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--loc-sweep-only"))
            {
                VerifyBilingualModelLocalization();await ReleaseProbeResources();
                GD.Print($"Depths localization sweep probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--camp-only"))
            {
                VerifyResources();await RenderCampSeats();await ReleaseProbeResources();
                GD.Print($"Depths camp seating probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--bestiary-boss-only"))
            {
                await DepthsBestiaryBossRegression.RunAsync(this, Assert);await ReleaseProbeResources();
                GD.Print($"Depths bestiary boss probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--event-random-only"))
            {
                DepthsEventRandomizationRegression.Run(Assert);await ReleaseProbeResources();
                GD.Print($"Depths event randomization probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            if(OS.GetCmdlineUserArgs().Contains("--external-sim-only"))
            {
                await DepthsExternalSimulationRegression.RunAsync(Assert);await ReleaseProbeResources();
                GD.Print($"Depths external simulation probe: PASS ({_checks} assertions)");GetTree().Quit(0);return;
            }
            VerifyCatalog();
            VerifyPoolsAndConfig();
            VerifyRoomsAndSaves();
            DepthsEventRandomizationRegression.Run(Assert);
            await VerifyNativeActProgression();
            VerifyResources();
            await DepthsBestiaryBossRegression.RunAsync(this, Assert);
            await DepthsExternalSimulationRegression.RunAsync(Assert);
            await VerifyColumnRules();
            await VerifyHumanFaceTowerRules();
            await VerifyAbyssalInkmawRules();
            await VerifyFoolArrowRegressions();
            await VerifyLeechSurrenderAndZIndexRegressions();
            await VerifyFleetingEcho();
            await VerifyStrongEncounters();
            await VerifySpongeSprayRendering();
            VerifyBorrowedDeathAudio();
            await VerifyDepthsMusicStages();
            await VerifyBlitzAndDreadModes();
            if(OS.GetCmdlineUserArgs().Contains("--variety-visual")) await RenderStrongVariety();
            if(OS.GetCmdlineUserArgs().Contains("--visual")) await RenderDepths();
            await ReleaseProbeResources();
            GD.Print($"Depths act probe: PASS ({_checks} assertions)");
            GetTree().Quit(0);
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }

    private static void ResetSettings() => ThingsModConfig.SetValues(
        ThingsModConfig.Entries.ToDictionary(entry=>entry.Key,entry=>(object?)entry.Default));
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);_checks++;}

    private async Task ReleaseProbeResources()
    {
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        // These resources are instantiated off-tree. Flush pending material updates
        // before disposing their cache entries, especially on the older game runtime.
        RenderingServer.ForceDraw();
        // VisualShader schedules shader assignment when the off-tree scenes are
        // instantiated. Retain them until those updates have actually flushed.
        foreach (var node in _resourceFixtures) node.Free();
        _resourceFixtures.Clear();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        RenderingServer.ForceDraw();
        var cache=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache;
        cache.GetType().GetMethod("UnloadMissedCacheAssets")?.Invoke(cache,null);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        GC.Collect();GC.WaitForPendingFinalizers();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        RenderingServer.ForceDraw();
    }

    private static void VerifyCatalog()
    {
        var act=ModelDb.Act<ThingsDepths>();
        Assert(act.Index==1 && !act.IsDefault && act.IsUnlocked(UnlockState.all),"Alternate act two is unlocked.");
        Assert(ModelDb.Acts.Count(a=>a is ThingsDepths)==1,"One canonical Depths entry.");
        Assert(ModelDb.ActsByIndex[1].Any(a=>a is ThingsDepths) && ModelDb.ActsByIndex[1].Any(a=>a is Hive),"Hive and Depths share second-act slot.");
        Assert(ModelDb.ActsByIndex.Count==3 && ActModel.GetDefaultList()[1] is Hive,"Default route and three-act length preserved.");
        var rolled=new HashSet<Type>();
        for(int seed=0;seed<128;seed++)
        {
            var a=ActModel.GetRandomList(new RunRngSet($"depths-{seed}").UpFront,UnlockState.all,true).ToArray();
            var b=ActModel.GetRandomList(new RunRngSet($"depths-{seed}").UpFront,UnlockState.all,true).ToArray();
            Assert(a.Select(x=>x.Id).SequenceEqual(b.Select(x=>x.Id)),"Seeded chapter list is deterministic.");
            Assert(a.Length==3 && a[0] is Overgrowth or Underdocks && a[1] is Hive or ThingsDepths && a[2] is Glory,"Depths never replaces act 1 or 3.");
            rolled.Add(a[1].GetType());
        }
        Assert(rolled.SetEquals([typeof(Hive),typeof(ThingsDepths)]),"Both second acts roll organically.");
        Assert(act.Title.GetFormattedText()=="深处","Localized chapter title resolves.");
        VerifyBilingualModelLocalization();
        GD.Print("PASS chapter registration, native random selection and localized title.");
    }

    private static void VerifyPoolsAndConfig()
    {
        var act=ModelDb.Act<ThingsDepths>();
        Assert(act.AllWeakEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals([
            typeof(ThingsLanternFishWeak),typeof(ThingsSanguineLeechWeak),typeof(ThingsCaveMawWeak),
            typeof(ThingsFleetingEchoWeak),typeof(ThingsSnailTrioWeak),typeof(ThingsScalePeelerWeak)]),
            "Six pure weak encounter entries; chromatic slime belongs to regular and column belongs to elite.");
        Assert(act.AllRegularEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals(StrongRosterContracts.Keys),"Independent regular encounter entries match the authored rosters.");
        var depthsEliteTypes=new HashSet<Type>{typeof(ThingsMycorrhizalTwinsElite),typeof(ThingsReverseSalamanderElite),typeof(ThingsRadioJellyfishElite),typeof(ThingsHumanFaceColumnEncounter)};
        Assert(act.AllEliteEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals(depthsEliteTypes),"Depths elite pool contains exclusively the four custom Depths elites.");
        Assert(!ModelDb.Act<Hive>().AllEncounters.Any(e=>depthsEliteTypes.Contains(e.GetType())) &&
               !act.AllEliteEncounters.Any(e=>ModelDb.Act<Hive>().AllEncounters.Select(x=>x.GetType()).Contains(e.GetType())),
            "Depths and Hive elite pools are strictly disjoint.");
        Assert(act.AllBossEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals([typeof(ThingsCaveGodBossEncounter),typeof(ThingsHumanFaceTowerBossEncounter),typeof(ThingsAbyssalInkmawBossEncounter)]),"Depths defaults to Cave God, Human-Faced Tower, and Abyssal Inkmaw.");
        Assert(ModelDb.Act<Hive>().AllBossEncounters.Any(e=>e is ThingsCaveGodBossEncounter),"Cave God remains in Hive.");
        Assert(!ModelDb.Act<Hive>().AllEncounters.Any(e=>e is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter),"Human-Faced Tower and Abyssal Inkmaw belong exclusively to Depths, not Hive.");
        var depthsCorridorTypes=act.AllWeakEncounters.Concat(act.AllRegularEncounters).Select(e=>e.GetType()).ToHashSet();
        Assert(!ModelDb.Act<Hive>().AllEncounters.Any(e=>depthsCorridorTypes.Contains(e.GetType())),"Custom hallway pools belong only to Depths.");
        var run=CreateRun("pair-contract");
        foreach(var (canonical,count,phases) in new[]{(ModelDb.Encounter<ThingsLanternFishWeak>() as EncounterModel,2,new[]{0,1}),
            (ModelDb.Encounter<ThingsLanternFishEncounter>(),3,new[]{0,1,3})})
        {
            var encounter=canonical.ToMutable();encounter.GenerateMonstersWithSlots(run);
            Assert(encounter.MonstersWithSlots.Count==count,"Correct fish count.");
            Assert(encounter.MonstersWithSlots.Select(x=>((ThingsLanternFish)x.Item1).OpeningPhase).SequenceEqual(phases),"Opening moves stagger.");
            Assert(encounter.Slots.Count==count && encounter.MonstersWithSlots.All(x=>encounter.Slots.Contains(x.Item2!)),"All creature slots resolve.");
            Assert(encounter.RoomType==RoomType.Monster && encounter.IsWeak==(count==2),"Native weak/regular classification.");
        }
        ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodEnabled,false);
        Assert(act.AllBossEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals([typeof(ThingsHumanFaceTowerBossEncounter),typeof(ThingsAbyssalInkmawBossEncounter)]),"Disabling Cave God leaves Human-Faced Tower and Abyssal Inkmaw in Depths.");
        ThingsModConfig.SetValue(ThingsModConfig.BossHumanFaceTowerEnabled,false);
        Assert(act.AllBossEncounters.Single() is ThingsAbyssalInkmawBossEncounter,"Disabling Cave God + Human-Faced Tower leaves Abyssal Inkmaw in Depths.");
        ThingsModConfig.SetValue(ThingsModConfig.BossAbyssalInkmawEnabled,false);
        Assert(act.AllBossEncounters.Single() is KaiserCrabBoss,"Disabling all Depths bosses falls back to Kaiser Crab.");
        ResetSettings();ThingsModConfig.SetValue(ThingsModConfig.BossHumanFaceTowerForced,true);
        Assert(act.AllBossEncounters.Single() is ThingsHumanFaceTowerBossEncounter,"Forcing Human-Faced Tower isolates it in Depths.");
        ThingsModConfig.SetValue(ThingsModConfig.BossAbyssalInkmawForced,true);
        Assert(!ThingsModConfig.IsForced(ThingsModConfig.BossAbyssalInkmawForced) && ThingsModConfig.IsForced(ThingsModConfig.BossHumanFaceTowerForced),
            "SlotDepths mutex keeps the first forced Depths boss and demotes a second simultaneous force.");
        ResetSettings();ThingsModConfig.SetValue(ThingsModConfig.BossAbyssalInkmawForced,true);
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossAbyssalInkmawForced) &&
               act.AllBossEncounters.Single() is ThingsAbyssalInkmawBossEncounter,"Forcing Abyssal Inkmaw alone isolates it in Depths.");
        ResetSettings();ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodForced,true);
        Assert(act.AllBossEncounters.Single() is ThingsCaveGodBossEncounter &&
               act.BossDiscoveryOrder.Single() is ThingsCaveGodBossEncounter,"Forcing Cave God alone isolates it in Depths.");
        ThingsModConfig.SetValue(ThingsModConfig.BossHumanFaceTowerForced,true);
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossHumanFaceTowerForced) &&
               ThingsModConfig.IsForced(ThingsModConfig.BossCaveGodForced),"Depths and Hive boss force slots can both be active.");
        Assert(act.AllBossEncounters.Select(e=>e.GetType()).SequenceEqual([typeof(ThingsHumanFaceTowerBossEncounter),typeof(ThingsCaveGodBossEncounter)]) &&
               act.BossDiscoveryOrder.Select(e=>e.GetType()).SequenceEqual([typeof(ThingsHumanFaceTowerBossEncounter),typeof(ThingsCaveGodBossEncounter)]),
            "Dual-forced Depths + Hive bosses keep both Human-Faced Tower and Cave God in Depths pool and discovery order.");
        ResetSettings();ThingsModConfig.SetValue(ThingsModConfig.BossBowlbugProgenitorForced,true);
        Assert(act.AllBossEncounters.Select(e=>e.GetType()).ToHashSet().SetEquals([typeof(ThingsCaveGodBossEncounter),typeof(ThingsHumanFaceTowerBossEncounter),typeof(ThingsAbyssalInkmawBossEncounter)]),"Hive-only force does not replace Depths bosses.");
        ResetSettings();
        VerifyCorridorTags(act);
        GD.Print("PASS 2/3 fish pools, corridor family tags, custom Depths elites, shared/dual-forced Cave God and config isolation.");
    }

    private static void VerifyCorridorTags(ThingsDepths act)
    {
        const EncounterTag Fish=(EncounterTag)1101,Leech=(EncounterTag)1102,Moth=(EncounterTag)1103,
            Maw=(EncounterTag)1104,Snail=(EncounterTag)1105,Sponge=(EncounterTag)1106,
            Peeler=(EncounterTag)1107,Slime=(EncounterTag)1108;
        var expected=new Dictionary<Type,EncounterTag[]>
        {
            [typeof(ThingsLanternFishWeak)]=[Fish],
            [typeof(ThingsSanguineLeechWeak)]=[Leech],
            [typeof(ThingsCaveMawWeak)]=[Maw],
            [typeof(ThingsFleetingEchoWeak)]=[],
            [typeof(ThingsSnailTrioWeak)]=[Snail],
            [typeof(ThingsScalePeelerWeak)]=[Peeler],
            [typeof(ThingsLanternFishEncounter)]=[Fish],
            [typeof(ThingsSanguineLeechEncounter)]=[Leech],
            [typeof(ThingsLeechMotherEncounter)]=[Leech],
            [typeof(ThingsSilkMothTrio)]=[Moth],
            [typeof(ThingsChromaticSlimeWeak)]=[Slime],
            [typeof(ThingsSpongeLeechEncounter)]=[Leech,Sponge],
            [typeof(ThingsLanternSpongeEncounter)]=[Fish,Sponge],
            [typeof(ThingsSilkSnailEncounter)]=[Moth,Snail],
            [typeof(ThingsScalePeelerEncounter)]=[Peeler,Snail],
            [typeof(ThingsCaveMawLanternEncounter)]=[Maw,Fish],
        };
        var vanillaTags=Enum.GetValues<EncounterTag>().ToHashSet();
        var corridor=act.AllWeakEncounters.Concat(act.AllRegularEncounters).ToArray();
        Assert(corridor.Length==16 && corridor.Select(e=>e.GetType()).ToHashSet().SetEquals(expected.Keys),
            "All 16 Depths corridor encounters are accounted for in tag contract.");
        foreach(var enc in corridor)
        {
            var actual=enc.Tags.ToArray();
            Assert(actual.Length<=2,"At most 2 primary family tags per corridor encounter: "+enc.Id.Entry);
            Assert(actual.All(t=>!vanillaTags.Contains(t)),"Depths custom EncounterTag must not collide with vanilla tags: "+enc.Id.Entry);
            Assert(actual.SequenceEqual(expected[enc.GetType()]),"Exact EncounterTag contract for "+enc.Id.Entry);
        }
    }

    private static RunState CreateRun(string seed,int players=1,int ascension=0)
    {
        var party=Enumerable.Range(1,players).Select(i=>Player.CreateForNewRun<Ironclad>(UnlockState.all,(ulong)i)).ToList();
        return NativeRun(party,[ModelDb.Act<Overgrowth>().ToMutable(),ModelDb.Act<ThingsDepths>().ToMutable(),ModelDb.Act<Glory>().ToMutable()],GameMode.Standard,ascension,seed);
    }

    private void VerifyRoomsAndSaves()
    {
        var weakSeen=new HashSet<ModelId>();
        var regularSeen=new HashSet<ModelId>();
        var eliteSeen=new HashSet<ModelId>();
        for(int players=1;players<=4;players++)
        for(int seed=0;seed<16;seed++)
        {
            var run=CreateRun($"rooms-{seed}",players);run.CurrentActIndex=1;
            var act=run.Act;act.GenerateRooms(run.Rng.UpFront,UnlockState.all,players>1);
            var save=act.ToSave();
            weakSeen.UnionWith(save.SerializableRooms.NormalEncounterIds.Take(2));
            regularSeen.UnionWith(save.SerializableRooms.NormalEncounterIds.Skip(2));
            eliteSeen.UnionWith(save.SerializableRooms.EliteEncounterIds);
            Assert(save.SerializableRooms.NormalEncounterIds.Count==(players==1?14:13),"Native room count.");
            Assert(save.SerializableRooms.NormalEncounterIds.Take(2).All(id=>act.AllWeakEncounters.Any(e=>e.Id==id)),"First two encounters use the weak pool.");
            Assert(save.SerializableRooms.NormalEncounterIds.Skip(2).All(id=>act.AllRegularEncounters.Any(e=>e.Id==id)),"Later encounters use the regular pool.");
            var firstThree=save.SerializableRooms.NormalEncounterIds.Take(3).Select(ModelDb.GetById<EncounterModel>).ToArray();
            Assert(!firstThree[0].SharesTagsWith(firstThree[1]) && !firstThree[1].SharesTagsWith(firstThree[2]),
                "Opening weak pair and weak-to-regular transition never repeat a Depths family tag.");
            int expectedUnique=Math.Min(save.SerializableRooms.NormalEncounterIds.Count-2,act.AllRegularEncounters.Count());
            Assert(save.SerializableRooms.NormalEncounterIds.Skip(2).Take(expectedUnique).Distinct().Count()==expectedUnique,"Regular entries do not repeat before the ten-entry bag is exhausted.");
            var eliteIds=save.SerializableRooms.EliteEncounterIds;
            Assert(eliteIds.Count==15 && save.SerializableRooms.AncientId!=null,"Elite and ancient room sets complete.");
            Assert(eliteIds.All(id=>act.AllEliteEncounters.Any(e=>e.Id==id) &&
                ModelDb.GetById<EncounterModel>(id).GetType().Assembly==typeof(ThingsDepths).Assembly),
                "All fifteen generated elite rooms belong exclusively to custom Depths elites (zero vanilla Hive elites).");
            int elitePoolCount=act.AllEliteEncounters.Count();
            for(int bagStart=0;bagStart<15;bagStart+=elitePoolCount)
            {
                int sliceLen=Math.Min(elitePoolCount,15-bagStart);
                Assert(eliteIds.Skip(bagStart).Take(sliceLen).Distinct().Count()==sliceLen,
                    "Each four-elite grab-bag cycle yields distinct custom Depths elites without duplicates.");
            }
            for(int i=1;i<eliteIds.Count;i++)
            {
                Assert(eliteIds[i]!=eliteIds[i-1],"Adjacent Depths elite rooms never repeat consecutively.");
            }
            var map=act.CreateMap(run,false);
            Assert(map.startMapPoints.Count>0 && map.GetAllMapPoints().Any(p=>p.PointType==MapPointType.RestSite),"Native map contains traversable starts and camps.");
            Assert(map.BossMapPoint.PointType==MapPointType.Boss,"Native boss destination exists.");
            act.MarkRoomVisited(RoomType.Monster);
            save=act.ToSave();save.SavedMap=SerializableActMap.FromActMap(map);
            var writer=new PacketWriter{WarnOnGrow=false};save.Serialize(writer);
            var reader=new PacketReader();reader.Reset(writer.Buffer);
            var restored=new SerializableActModel();restored.Deserialize(reader);
            var loaded=ActModel.FromSave(restored);
            Assert(loaded.ToSave().SerializableRooms.NormalEncounterIds.SequenceEqual(save.SerializableRooms.NormalEncounterIds),"Save/load preserves the entire encounter queue, including new IDs.");
            Assert(loaded.ToSave().SerializableRooms.EliteEncounterIds.SequenceEqual(eliteIds),"Save/load preserves the entire custom elite encounter queue.");
            Assert(loaded is ThingsDepths && loaded.BossEncounter is ThingsCaveGodBossEncounter or ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter,"Native binary act snapshot rehydrates Depths and boss.");
            Assert(loaded.ToSave().SerializableRooms.NormalEncountersVisited==1,"Visited encounter index survives reload.");
            Assert(loaded.PullNextEncounter(RoomType.Monster).IsWeak,"Reload continues remaining weak encounter.");
            Assert(new SavedActMap(restored.SavedMap!).GetRowCount()==map.GetRowCount(),"Saved map restores geometry.");
            if(seed==0 && players==1)System.IO.File.WriteAllBytes(Path.Combine(_output,"depths-act.snapshot"),writer.Buffer.Take(writer.BytePosition).ToArray());
        }
        Assert(regularSeen.Contains(ModelDb.Encounter<ThingsSpongeLeechEncounter>().Id),"Native room generation actually rolls the mixed sponge regular encounter.");
        Assert(regularSeen.Contains(ModelDb.Encounter<ThingsSilkMothTrio>().Id),"Native room generation rolls the moth trio regular encounter.");
        Assert(regularSeen.Contains(ModelDb.Encounter<ThingsLanternSpongeEncounter>().Id),"Native room generation rolls the lantern sponge regular encounter.");
        Assert(regularSeen.Contains(ModelDb.Encounter<ThingsScalePeelerEncounter>().Id),"Native room generation rolls the scale peeler regular encounter.");
        Assert(regularSeen.Contains(ModelDb.Encounter<ThingsChromaticSlimeWeak>().Id),"Native room generation rolls the chromatic slime regular encounter.");
        Assert(regularSeen.SetEquals(ModelDb.Act<ThingsDepths>().AllRegularEncounters.Select(e=>e.Id)),"All ten regular encounters roll organically.");
        Assert(weakSeen.SetEquals(ModelDb.Act<ThingsDepths>().AllWeakEncounters.Select(e=>e.Id)),"All six weak encounters remain reachable.");
        Assert(eliteSeen.SetEquals(ModelDb.Act<ThingsDepths>().AllEliteEncounters.Select(e=>e.Id)),"All four custom Depths elites roll organically.");
        GD.Print("PASS 64 native 1-4 player room sets, ten-entry regular & four-elite bags, generated maps and binary save/load round trips.");
    }

    private async Task VerifyNativeActProgression()
    {
        var run=CreateRun("native-depths-progression");
        RunManager.Instance.SetUpTest(run,new NetSingleplayerGameService(),shouldSave:false);
        RunManager.Instance.GenerateRooms();
        await RunManager.Instance.SetActInternal(0);
        Assert(run.Act is Overgrowth && run.Map!=null,"Native first act initialized.");
        await RunManager.Instance.SetActInternal(1);
        Assert(run.Act is ThingsDepths && run.CurrentActIndex==1 && run.Map!=null,"Native first-to-second progression reaches Depths.");
        AchievementsHelper.CheckForDefeatedAllEnemiesAchievement(run.Act,run.Players[0]);
        await RunManager.Instance.SetActInternal(2);
        Assert(run.Act is Glory,"Native progression continues into original third act.");
        VerifyDepthsAscensionUnlock();
        GD.Print("PASS RunManager initializes acts 1 -> Depths -> Glory and validates native ascension progression.");
    }

    private void VerifyResources()
    {
        var status=GDExtensionManager.LoadExtension(Path.Combine(_root,"addons/spine/spine_godot_extension.gdextension"));
        Assert(status is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded,"Spine loaded.");
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly);
        foreach(string title in new[]{"things_depths","things_lantern_fish_encounter","things_lantern_fish_weak"})
        {
            using var directory=DirAccess.Open($"res://scenes/backgrounds/{title}/layers");
            Assert(directory!=null,$"Background directory {title}.");
            var paths=directory!.GetFiles().Where(n=>n.Contains("_bg_00_") && n.EndsWith(".tscn",StringComparison.Ordinal)).ToArray();
            Assert(paths.Length==(title=="things_depths"?9:1),$"Act variants and fish-specific scene are separate: {title}: {string.Join(',',directory.GetFiles())}.");
            if(title!="things_depths")Assert(paths.Single().Contains("cave_riverbend"),"Fish stay at the riverbank.");
            foreach(string name in paths)
            {
                var layer=GD.Load<PackedScene>($"res://scenes/backgrounds/{title}/layers/{name}").Instantiate<Control>();
                bool hasArt = false;
                bool hasAmbience = false;
                var stack = new Stack<Node>();
                stack.Push(layer);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (node is TextureRect tr && tr.Texture != null) hasArt = true;
                    if (node is GpuParticles2D or CpuParticles2D or Light2D || node.Name.ToString().Contains("Ambien", StringComparison.OrdinalIgnoreCase) || node.Name.ToString().Contains("Mist", StringComparison.OrdinalIgnoreCase)) hasAmbience = true;
                    foreach (Node child in node.GetChildren()) stack.Push(child);
                }
                Assert(layer.HasMeta("depths_variant") && layer.GetChildCount()>=1 && hasArt && hasAmbience,
                    $"Atomic composition includes art and ambience ({name}: children={layer.GetChildCount()}, art={hasArt}, ambience={hasAmbience}).");
                _resourceFixtures.Add(layer);
            }
        }
        var act=ModelDb.Act<ThingsDepths>();
        Assert(act.MapTopBg.GetWidth()==2036 && act.MapMidBg.GetHeight()==1440 && act.MapBotBg.GetHeight()==1440,"Native map textures load.");
        var rest=act.CreateRestSiteBackground();Assert(rest.HasNode("%RestSiteLighting"),"Camp exposes native lighting contract.");_resourceFixtures.Add(rest);
        Assert(ResourceLoader.Exists(act.ChestSpineResourcePath),"Chest resource exists.");
        var backgrounds=new HashSet<string>();
        for(int seed=0;seed<128;seed++)
        {
            var bg=act.GenerateBackgroundAssets(new RunRngSet($"bg-{seed}").UpFront);
            Assert(bg.BgLayers.Count==1 && bg.FgLayer!=null,"Native generator chooses one whole cave.");
            backgrounds.Add(bg.BgLayers[0]);
        }
        Assert(backgrounds.Count==9,"Every variant is reachable.");
        var getBgAssets=typeof(EncounterModel).GetMethod("GetBackgroundAssets",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var lockedTypes=new HashSet<Type>
        {
            typeof(ThingsLanternFishWeak),typeof(ThingsLanternFishEncounter),typeof(ThingsReverseSalamanderElite),
            typeof(ThingsFleetingEchoWeak),typeof(ThingsHumanFaceColumnEncounter),
        };
        var pooledLayers=new HashSet<string>();
        var nonBossDepths=act.AllWeakEncounters.Concat(act.AllRegularEncounters).Concat(act.AllEliteEncounters).ToArray();
        Assert(nonBossDepths.Length==20,"Twenty non-boss Depths encounters.");
        foreach(var enc in nonBossDepths)
        {
            var resolved=(BackgroundAssets)getBgAssets.Invoke(enc.ToMutable(),[act,new Rng((uint)enc.Id.Entry.GetHashCode())])!;
            if(lockedTypes.Contains(enc.GetType()))
            {
                Assert(!resolved.BackgroundScenePath.Contains("/things_depths/",StringComparison.OrdinalIgnoreCase),
                    $"Habitat-locked encounter keeps its custom background: {enc.Id.Entry}");
            }
            else
            {
                Assert(resolved.BackgroundScenePath.Contains("/things_depths/",StringComparison.OrdinalIgnoreCase),
                    $"General Depths encounter uses the 9-variant act pool: {enc.Id.Entry}");
                for(int seed=0;seed<32;seed++)
                {
                    var sample=(BackgroundAssets)getBgAssets.Invoke(enc.ToMutable(),[act,new Rng((uint)(seed*747+13))])!;
                    pooledLayers.Add(sample.BgLayers[0]);
                }
            }
        }
        Assert(pooledLayers.Count==9 &&
               pooledLayers.Any(p=>p.Contains("hollow_grotto.tscn",StringComparison.Ordinal)) &&
               pooledLayers.Any(p=>p.Contains("hollow_grotto_ember.tscn",StringComparison.Ordinal)),
            $"Pooled Depths encounters reach all 9 cave variants in actual combat (reached {pooledLayers.Count}).");
        using var towerBgDir=DirAccess.Open("res://scenes/backgrounds/things_human_face_tower_boss_encounter/layers");
        Assert(towerBgDir!=null,"Human-Faced Tower background layers directory exists.");
        Assert(ResourceLoader.Exists("res://scenes/backgrounds/things_human_face_tower_boss_encounter/things_human_face_tower_boss_encounter_background.tscn"),
            "Human-Faced Tower master background scene exists.");
        using var inkmawBgDir=DirAccess.Open("res://scenes/backgrounds/things_abyssal_inkmaw_boss_encounter/layers");
        Assert(inkmawBgDir!=null,"Abyssal Inkmaw background layers directory exists.");
        Assert(ResourceLoader.Exists("res://scenes/backgrounds/things_abyssal_inkmaw_boss_encounter/things_abyssal_inkmaw_boss_encounter_background.tscn"),
            "Abyssal Inkmaw master background scene exists.");
        var inkmawBgNode=NCombatBackground.Create(new BackgroundAssets("things_abyssal_inkmaw_boss_encounter",new Rng()));
        Assert(inkmawBgNode.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter")==null,"Spine-rigged Abyssal Inkmaw boss background is never rescaled by viewport fitter.");
        _resourceFixtures.Add(inkmawBgNode);
        var leechMotherFg=GD.Load<PackedScene>("res://scenes/backgrounds/things_leech_mother_encounter/layers/things_leech_mother_encounter_fg_a.tscn").Instantiate<Control>();
        Assert(leechMotherFg.GetChildCount()==0,"Leech Mother foreground wrapper has no stray rootfungus occlusion overlay.");
        _resourceFixtures.Add(leechMotherFg);
        var depthsBgNode=NCombatBackground.Create(act.GenerateBackgroundAssets(new RunRngSet("fitter-depths").UpFront));
        Assert(depthsBgNode.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter")!=null,"Depths combat backgrounds attach viewport framing fitter.");
        _resourceFixtures.Add(depthsBgNode);
        var fogmogBgNode=NCombatBackground.Create(new BackgroundAssets("things_origin_fogmog_boss_encounter",new Rng()));
        Assert(fogmogBgNode.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter")!=null,"Non-Spine custom boss backgrounds attach viewport framing fitter.");
        _resourceFixtures.Add(fogmogBgNode);
        var caveGodBgNode=NCombatBackground.Create(new BackgroundAssets("things_cave_god_boss_encounter",new Rng()));
        Assert(caveGodBgNode.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter")==null,"Spine-rigged Cave God boss background is never rescaled by viewport fitter.");
        _resourceFixtures.Add(caveGodBgNode);
        var hiveBgNode=NCombatBackground.Create(ModelDb.Act<Hive>().GenerateBackgroundAssets(new RunRngSet("fitter-hive").UpFront));
        Assert(hiveBgNode.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter")==null,"Vanilla act backgrounds are untouched by custom framing fitter.");
        _resourceFixtures.Add(hiveBgNode);
        var leechCardTex=ModelDb.Card<ThingsLeechParasite>().Portrait;
        Assert(leechCardTex.GetWidth()==1000 && leechCardTex.GetHeight()==760,"Leech Parasite portrait upgraded to full 1000x760.");
        var strikeCard=ModelDb.Card<StrikeIronclad>();
        var bigStrike=Sts2VersionSeam.TryGetBigCardPortrait(strikeCard);
        Assert(bigStrike!=null && bigStrike.GetWidth()>=500 && !ReferenceEquals(bigStrike,strikeCard.Portrait),
            "BigPortrait seam resolves high-resolution standalone portrait distinct from 250x190 atlas on both targets.");
        GD.Print("PASS packaged chapter/background/camp/map asset contracts, viewport framing fitter, and crisp card portrait seam.");
    }

    private static string? GetCmdOption(string prefix)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }

    private static string FirstExistingPath(params string[] candidates)
    {
        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return candidates[0];
    }

}
