using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Acts;
using STS2_Things.Audio;
using STS2_Things.Config;
using STS2_Things.Encounters;

public partial class DepthsProbeNode
{
    private const BindingFlags MusicPrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags MusicPrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private async Task VerifyDepthsMusicStages()
    {
        DeactivateSyntheticCombat();
        ResetSettings();

        // 1. Verify packaged QOA stream contracts & deterministic loop metadata.
        MethodInfo loadStream = typeof(NativeSfxPlayer).GetMethod("LoadLoopingMusicStream", MusicPrivateStatic)
            ?? throw new MissingMethodException(nameof(NativeSfxPlayer), "LoadLoopingMusicStream");

        (string Path, double ExpectedSec)[] expectedStreams =
        [
            ("res://music/depths/depths_a1_explore.wav", 59.00),
            ("res://music/depths/depths_a1_combat.wav", 106.95),
            ("res://music/depths/depths_a2_explore.wav", 28.58),
            ("res://music/depths/depths_a2_combat.wav", 91.55),
        ];

        foreach ((string path, double expectedSec) in expectedStreams)
        {
            var wav = loadStream.Invoke(null, [path]) as AudioStreamWav;
            Assert(wav != null, $"Loaded looping AudioStreamWav for {path}.");
            Assert(wav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward, $"Forward loop enabled for {path}.");
            Assert(wav.MixRate == 48000 && wav.Stereo, $"48kHz stereo contract for {path}.");
            double len = wav.GetLength();
            Assert(Math.Abs(len - expectedSec) < 0.25, $"Expected ~{expectedSec:F2}s for {path}, got {len:F2}s.");
        }

        // 2. Verify deterministic SplitMix64 seed selection & Floor >= 8 companion suite evolution.
        MethodInfo resolveSuite = typeof(DepthsActMusicController).GetMethod("ResolveTargetSuiteIndex", MusicPrivateStatic)
            ?? throw new MissingMethodException(nameof(DepthsActMusicController), "ResolveTargetSuiteIndex");

        int suite0Count = 0;
        int suite1Count = 0;
        for (ulong s = 0; s < 64; s++)
        {
            int upper = (int)resolveSuite.Invoke(null, [s * 0x123456789ABCDEFUL + s, 1])!;
            int mid = (int)resolveSuite.Invoke(null, [s * 0x123456789ABCDEFUL + s, 7])!;
            int lower = (int)resolveSuite.Invoke(null, [s * 0x123456789ABCDEFUL + s, 8])!;
            Assert(upper is 0 or 1, "Initial suite index in [0, 1].");
            Assert(mid == upper, "Floors 1..7 keep initial suite.");
            Assert(lower == 1 - upper, "Floor >= 8 evolves to companion suite.");
            if (upper == 0) suite0Count++;
            else suite1Count++;
        }
        Assert(suite0Count >= 16 && suite1Count >= 16, $"Balanced seed distribution ({suite0Count}/{suite1Count}).");

        // 3. Spin up real NRunMusicController + GDScript Proxy + DepthsActMusicController in SceneTree.
        var proxyScript = new GDScript
        {
            SourceCode = """
                extends Node
                var _musicEv = null
                var _currentTrack: String = "event:/music/act2_a1_v2"
                var stop_count: int = 0
                var update_calls: int = 0
                var last_progress: int = -1

                func _ready() -> void:
                    _musicEv = self

                func stop_music() -> void:
                    _musicEv = null
                    stop_count += 1

                func update_music(track: String) -> void:
                    _currentTrack = track
                    _musicEv = self
                    update_calls += 1

                func update_global_parameter(param: String, val: Variant) -> void:
                    if param == "Progress":
                        last_progress = int(val)

                func stop_custom_music() -> void:
                    pass

                func unload_act_banks() -> void:
                    pass
                """,
        };
        Assert(proxyScript.Reload() == Error.Ok, "Compiled GDScript FMOD Proxy double.");

        var proxy = new Node { Name = "Proxy" };
        proxy.SetScript(proxyScript);
        var musicController = new NRunMusicController { Name = "ProbeRunMusicController" };
        musicController.AddChild(proxy);
        AddChild(musicController);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var run = CreateRun("depths-music-stage-probe", players: 1, ascension: 0);
        run.CurrentActIndex = 1; // ThingsDepths
        run.ActFloor = 1;
        typeof(RunManager).GetProperty("State", MusicPrivateInstance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, NativeAscension(0));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, new NetSingleplayerGameService());

        MethodInfo syncMain = typeof(DepthsActMusicController).GetMethod("SyncOnMainThread", MusicPrivateStatic)
            ?? throw new MissingMethodException(nameof(DepthsActMusicController), "SyncOnMainThread");
        MethodInfo ensureInst = typeof(DepthsActMusicController).GetMethod("EnsureInstance", MusicPrivateStatic)!;
        MethodInfo stopAll = typeof(DepthsActMusicController).GetMethod("StopAllImmediate", MusicPrivateInstance)!;
        FieldInfo explorePlayerField = typeof(DepthsActMusicController).GetField("_explorePlayer", MusicPrivateInstance)!;
        FieldInfo combatPlayerField = typeof(DepthsActMusicController).GetField("_combatPlayer", MusicPrivateInstance)!;
        FieldInfo exploreFilterField = typeof(DepthsActMusicController).GetField("_exploreFilter", MusicPrivateInstance)!;
        FieldInfo combatFilterField = typeof(DepthsActMusicController).GetField("_combatFilter", MusicPrivateInstance)!;
        FieldInfo elitePhaseField = typeof(DepthsActMusicController).GetField("_eliteSecondPhase", MusicPrivateStatic)!;

        int initialSuite = (int)resolveSuite.Invoke(null, [run.Rng.Seed, 1])!;
        int companionSuite = 1 - initialSuite;

        void PumpAudioController(int ticks = 60, double dt = 0.05)
        {
            var inst = (DepthsActMusicController?)ensureInst.Invoke(null, null);
            Assert(inst != null, "DepthsActMusicController instance exists.");
            for (int i = 0; i < ticks; i++)
                inst!._Process(dt);
        }

        void PopAllRooms()
        {
            while (run.CurrentRoom != null)
                run.PopCurrentRoom();
        }

        CombatRoom PushCombatRoom(EncounterModel canonical)
        {
            var room = new CombatRoom(canonical.ToMutable(), run);
            run.Players[0].ResetCombatState();
            room.CombatState.AddPlayer(run.Players[0]);
            run.PushRoom(room);
            return room;
        }

        try
        {
            // Stage A: Act Entry (CurrentRoom == null, Floor 1) -> ExploreInit
            PopAllRooms();
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(45);

            var inst = (DepthsActMusicController)ensureInst.Invoke(null, null)!;
            var explorePlayer = (AudioStreamPlayer)explorePlayerField.GetValue(inst)!;
            var combatPlayer = (AudioStreamPlayer)combatPlayerField.GetValue(inst)!;
            var exploreFilter = (AudioEffectLowPassFilter)exploreFilterField.GetValue(inst)!;
            var combatFilter = (AudioEffectLowPassFilter)combatFilterField.GetValue(inst)!;

            Assert(AudioServer.GetBusIndex("ThingsDepthsExplore") >= 0 && AudioServer.GetBusIndex("ThingsDepthsCombat") >= 0,
                "Dedicated Explore & Combat audio buses registered on AudioServer.");
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.ExploreInit, "Act entry resolves ExploreInit.");
            Assert(DepthsActMusicController.ActiveExploreSuite == initialSuite, "ExploreInit loads seeded suite.");
            Assert(proxy.Get("stop_count").AsInt32() == 1 && proxy.Get("_musicEv").VariantType == Variant.Type.Nil,
                "Native FMOD _musicEv suppressed cleanly on ExploreInit.");
            Assert(explorePlayer.Playing && !explorePlayer.StreamPaused && explorePlayer.VolumeLinear > 0.5f,
                "Explore deck actively playing at nominal volume.");
            Assert(combatPlayer.VolumeLinear == 0f, "Combat deck silent during ExploreInit.");

            // Stage B: Floor 1 Monster Combat -> CombatFull
            var fishRoom = PushCombatRoom(ModelDb.Encounter<ThingsLanternFishWeak>());
            ActivateSyntheticCombat(fishRoom.CombatState);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(40);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatFull, "Active monster fight resolves CombatFull.");
            Assert(DepthsActMusicController.ActiveCombatSuite == initialSuite, "CombatFull loads matching seeded suite.");
            Assert(combatPlayer.Playing && !combatPlayer.StreamPaused && combatPlayer.VolumeLinear > 0.7f, "Combat deck playing full bandwidth.");
            Assert(combatFilter.CutoffHz > 20000f, $"Combat LPF wide open ({combatFilter.CutoffHz:F0} Hz).");
            Assert(explorePlayer.VolumeLinear == 0f && explorePlayer.StreamPaused, "Explore deck faded out and paused during combat.");

            // Verify idempotency: repeated UpdateTrack inside same combat (even if ActFloor advances to 8) never restarts combat stream.
            run.ActFloor = 8;
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatFull &&
                   DepthsActMusicController.ActiveCombatSuite == initialSuite,
                "Mid-combat UpdateTrack is idempotent and never swaps suites mid-fight.");
            run.ActFloor = 1;

            // Stage C: Combat Victory / Rewards / Map -> CombatEndMuffled (continuous timeline + 920 Hz LPF + -6.5 dB duck)
            DeactivateSyntheticCombat();
            float fullCombatVol = combatPlayer.VolumeLinear;
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(50);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatEndMuffled,
                "Post-combat rewards/map resolves CombatEndMuffled.");
            Assert(combatPlayer.Playing && !combatPlayer.StreamPaused, "Combat track stays rolling continuously across CombatEnd.");
            Assert(combatFilter.CutoffHz > 880f && combatFilter.CutoffHz < 960f,
                $"CombatEnd LPF settles at ~920 Hz ({combatFilter.CutoffHz:F1} Hz).");
            Assert(combatPlayer.VolumeLinear > 0.15f && combatPlayer.VolumeLinear < fullCombatVol * 0.65f,
                $"CombatEnd ducks gain smoothly ({combatPlayer.VolumeLinear:F3} vs {fullCombatVol:F3}).");

            // Stage D: Consecutive Floor 2 Monster Combat -> sweeps LPF back open without resetting suite
            PopAllRooms();
            run.ActFloor = 2;
            var fishRoom2 = PushCombatRoom(ModelDb.Encounter<ThingsLanternFishWeak>());
            ActivateSyntheticCombat(fishRoom2.CombatState);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(40);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatFull &&
                   combatFilter.CutoffHz > 20000f && combatPlayer.VolumeLinear > 0.7f,
                "Next Monster room sweeps LPF smoothly back open to full bandwidth.");

            // Stage E: Unknown Event Room -> ExploreUnknown
            DeactivateSyntheticCombat();
            PopAllRooms();
            run.ActFloor = 3;
            EventModel anyEvent = ModelDb.Act<ThingsDepths>().AllEvents.First();
            run.PushRoom(new EventRoom(anyEvent));
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(55);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.ExploreUnknown, "EventRoom resolves ExploreUnknown.");
            Assert(explorePlayer.Playing && !explorePlayer.StreamPaused && explorePlayer.VolumeLinear > 0.6f, "Explore deck crossfaded back in for Event.");
            Assert(combatPlayer.VolumeLinear == 0f && combatPlayer.StreamPaused, "Combat deck faded out and paused in Event.");
            Assert(exploreFilter.CutoffHz > 15000f && exploreFilter.CutoffHz < 16000f, $"Event LPF at ~15500 Hz ({exploreFilter.CutoffHz:F0} Hz).");

            // Stage F: Ancient Event Room -> ExploreInit
            PopAllRooms();
            AncientEventModel ancient = ModelDb.Act<ThingsDepths>().AllAncients.First();
            run.PushRoom(new EventRoom(ancient));
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.ExploreInit, "AncientEventModel resolves ExploreInit.");

            // Stage G: Rest Site (Campfire) -> RestSilent (complete fade-out for campfire ambience)
            PopAllRooms();
            run.ActFloor = 4;
            run.PushRoom(new RestSiteRoom());
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(80);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.RestSilent, "RestSiteRoom resolves RestSilent.");
            Assert(explorePlayer.VolumeLinear == 0f && combatPlayer.VolumeLinear == 0f,
                "Both music decks fade completely silent at RestSite so campfire ambience owns the room.");

            // Stage H: Merchant Room -> DelegateFmodMerchant (restores FMOD Proxy + Progress = 2)
            PopAllRooms();
            run.ActFloor = 5;
            run.PushRoom(new MerchantRoom());
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(30);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.DelegateFmodMerchant, "MerchantRoom delegates to FMOD.");
            Assert(proxy.Get("update_calls").AsInt32() == 1 && proxy.Get("last_progress").AsInt32() == 2,
                "FMOD Proxy restored with Progress=2 (Merchant).");

            // Stage I: Elite Combat -> DelegateFmodElite (Progress = 6 -> Phase 2 Progress = 8)
            PopAllRooms();
            run.ActFloor = 6;
            var eliteRoom = PushCombatRoom(ModelDb.Act<ThingsDepths>().AllEliteEncounters.First());
            ActivateSyntheticCombat(eliteRoom.CombatState);
            elitePhaseField.SetValue(null, false);
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.DelegateFmodElite &&
                   proxy.Get("last_progress").AsInt32() == 6,
                "Elite combat delegates to FMOD Progress=6.");

            elitePhaseField.SetValue(null, true);
            syncMain.Invoke(null, [musicController]);
            Assert(proxy.Get("last_progress").AsInt32() == 8, "Elite second phase updates FMOD Progress=8.");

            // Stage J: Mid-Act Treasure Room at Floor >= 8 -> evolves to companion suite B!
            DeactivateSyntheticCombat();
            PopAllRooms();
            run.ActFloor = 9;
            run.PushRoom(new TreasureRoom(1));
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(50);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.ExploreTreasure, "TreasureRoom resolves ExploreTreasure.");
            Assert(DepthsActMusicController.ActiveExploreSuite == companionSuite,
                "Floor >= 8 TreasureRoom evolves Explore deck to companion suite B.");
            Assert(proxy.Get("_musicEv").VariantType == Variant.Type.Nil, "FMOD suppressed again when returning to Depths-owned room.");

            // Stage K: Lower Depths Monster Combat (Floor 10) -> CombatFull with companion suite B!
            PopAllRooms();
            run.ActFloor = 10;
            var lowerCombat = PushCombatRoom(ModelDb.Encounter<ThingsLanternFishEncounter>());
            ActivateSyntheticCombat(lowerCombat.CombatState);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(40);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatFull &&
                   DepthsActMusicController.ActiveCombatSuite == companionSuite,
                "Lower Depths combat plays companion suite B.");

            // Stage L: Live Mod Settings Toggle (FeatureCustomBgmEnabled = false -> true)
            ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, false);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(35);

            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.Inactive,
                "Disabling FeatureCustomBgmEnabled transitions Depths controller to Inactive.");
            Assert(proxy.Get("_musicEv").VariantType != Variant.Type.Nil && proxy.Get("last_progress").AsInt32() == 1,
                "Disabling FeatureCustomBgmEnabled restores native FMOD Act music at Progress=1 (Enemy).");
            Assert(combatPlayer.VolumeLinear == 0f, "Custom combat deck fades out when disabled.");

            ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, true);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(35);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.CombatFull &&
                   combatPlayer.VolumeLinear > 0.7f,
                "Re-enabling FeatureCustomBgmEnabled resumes Depths custom combat music.");

            // Stage M: Boss Encounters (Mod WAV Boss & Native FMOD Boss) -> YieldToCustom
            DeactivateSyntheticCombat();
            PopAllRooms();
            run.ActFloor = 16;
            var towerBoss = new CombatRoom(ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>().ToMutable(), run);
            run.PushRoom(towerBoss);
            syncMain.Invoke(null, [musicController]);
            PumpAudioController(35);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.YieldToCustom &&
                   combatPlayer.VolumeLinear == 0f && explorePlayer.VolumeLinear == 0f,
                "Mod WAV Boss yields cleanly to CustomMusicHooks.");

            PopAllRooms();
            var caveGodBoss = new CombatRoom(ModelDb.Encounter<ThingsCaveGodBossEncounter>().ToMutable(), run);
            run.PushRoom(caveGodBoss);
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.YieldToCustom,
                "Native FMOD Boss (Cave God) yields cleanly to FMOD custom event.");

            // Stage N: Leaving ThingsDepths into Act 3 (Glory) -> Inactive
            PopAllRooms();
            run.CurrentActIndex = 2; // Glory
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.Inactive,
                "Advancing to Act 3 deactivates DepthsActMusicController.");

            GD.Print("PASS Depths multi-stage BGM: 4 QOA loops, SplitMix64 seed & Floor-8 suite evolution, Explore/Combat/Muffled-LPF/Event/Ancient/Treasure/Campfire/Merchant/Elite/Boss transitions, and live config toggle.");
        }
        finally
        {
            DeactivateSyntheticCombat();
            PopAllRooms();
            ResetSettings();
            var inst = (DepthsActMusicController?)ensureInst.Invoke(null, null);
            if (inst != null)
            {
                stopAll.Invoke(inst, null);
                inst.Free();
            }
            musicController.Free();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
