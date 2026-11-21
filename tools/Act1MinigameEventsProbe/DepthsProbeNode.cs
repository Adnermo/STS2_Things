using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Config;
using STS2_Things.Events;
using STS2_Things.Events.Minigames;

public partial class DepthsProbeNode : Node
{
    private static int _checks;
    private string _root = null!;
    private bool _hasVanillaPck;

    private static void Assert(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
        _checks++;
    }

    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("Sentry");
        EnsureRuntimeDependency("Sentry.Godot");
        _ = RunAll();
    }

    private async Task RunAll()
    {
        Harmony? modHarmony = null;
        bool originalHoverBlock = NHoverTipSet.shouldBlockHoverTips;
        try
        {
            _root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            TestMode.TurnOnInternal();

            string? vanillaPck = System.Environment.GetEnvironmentVariable("THINGS_VANILLA_PCK");
            if (!string.IsNullOrWhiteSpace(vanillaPck) && File.Exists(vanillaPck))
            {
                Assert(ProjectSettings.LoadResourcePack(vanillaPck), "Load vanilla game PCK");
                _hasVanillaPck = true;
            }

            string? modPck = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK");
            if (!string.IsNullOrWhiteSpace(modPck) && File.Exists(modPck))
                Assert(ProjectSettings.LoadResourcePack(modPck), "Load STS2_Things PCK");

            STS2_Things.Platform.Sts2VersionSeam.InitializeBeforeModelDatabase();
            InitializeModelDb();
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsBilgeMines).Assembly);

            modHarmony = new Harmony("Act1MinigameEventsProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsBilgeMines).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyCatalogAndResources();
            VerifyLocalizationInBothLanguages();
            await VerifyBilgeMinesAsync();
            await VerifyMossRuneLoomAsync();
            await VerifyClamClawAsync();
            await VerifyForestPlinkoAsync();
            await VerifyBronzeCauldronAsync();
            await VerifyLendingVineAsync();
            await VerifyOverstuffedBroodAsync();
            await VerifyScalesOfVanityAsync();
            await VerifyWallCrackLedgerAsync();
            await VerifySpikedDoorAsync();
            VerifyEncounterGates();
            VerifyMultiplayerPacketSerialization();

            if (_hasVanillaPck)
                await RenderLiveUiAndOverlaysAsync();

            string? output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
                {
                    assertions = _checks,
                    target =
#if STS2_V111_1
                        "v111.1",
#else
                        "v113.1",
#endif
                    vanillaPckLoaded = _hasVanillaPck,
                    events = new[]
                    {
                        "THINGS_BILGE_MINES",
                        "THINGS_MOSS_RUNE_LOOM",
                        "THINGS_CLAM_CLAW",
                        "THINGS_FOREST_PLINKO",
                        "THINGS_BRONZE_CAULDRON",
                        "THINGS_LENDING_VINE",
                        "THINGS_OVERSTUFFED_BROOD",
                        "THINGS_SCALES_OF_VANITY",
                        "THINGS_WALL_CRACK_LEDGER",
                        "THINGS_SPIKED_DOOR",
                    },
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.Print($"Act 1 minigame events probe: PASS ({_checks} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony?.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.PushError(ex.ToString());
            GetTree().Quit(1);
        }
    }

    private static void ClearRunState()
    {
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, null);
    }

    private static RunState SetupTestRun(string seed, int playerCount = 1)
    {
        ClearRunState();
        List<Player> players = [];
        CharacterModel[] roster = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>()];
        for (int i = 0; i < playerCount; i++)
            players.Add(Player.CreateForNewRun(roster[i % roster.Length], UnlockState.all, (ulong)(i + 1)));

        List<ActModel> acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
        RunState run = ProbeRuntimeApi.CreateForNewRun(players, acts, [], GameMode.Standard, 0, seed);
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
        LocalContext.NetId = run.Players[0].NetId;
        return run;
    }

    private static TEvent BeginEventForPlayer<TEvent>(Player player, RunState run) where TEvent : EventModel
    {
        LocalContext.NetId = player.NetId;
        EventModel canonical = ModelDb.Event<TEvent>();
        run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, canonical.Id);
        RunManager.Instance.EventSynchronizer.BeginEvent(canonical, false);
        EventModel? local = RunManager.Instance.EventSynchronizer.GetEventForPlayer(player);
        Assert(local is TEvent, $"BeginEvent created mutable instance for {typeof(TEvent).Name}");
        return (TEvent)local!;
    }

    private static void SetTestDriver<TEvent, TGame>(TEvent ev, Func<TGame, Task> driver)
    {
        System.Reflection.PropertyInfo prop = typeof(TEvent).GetProperty("MinigameTestDriver", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Missing MinigameTestDriver on {typeof(TEvent).Name}");
        prop.SetValue(ev, driver);
    }

    private void VerifyCatalogAndResources()
    {
        Underdocks underdocks = ModelDb.Act<Underdocks>();
        Overgrowth overgrowth = ModelDb.Act<Overgrowth>();
        Hive hive = ModelDb.Act<Hive>();
        Glory glory = ModelDb.Act<Glory>();
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();

        EventModel bilge = ModelDb.Event<ThingsBilgeMines>();
        EventModel loom = ModelDb.Event<ThingsMossRuneLoom>();
        EventModel clam = ModelDb.Event<ThingsClamClaw>();
        EventModel plinko = ModelDb.Event<ThingsForestPlinko>();
        EventModel cauldron = ModelDb.Event<ThingsBronzeCauldron>();
        EventModel lendingVine = ModelDb.Event<ThingsLendingVine>();
        EventModel overstuffedBrood = ModelDb.Event<ThingsOverstuffedBrood>();
        EventModel scalesOfVanity = ModelDb.Event<ThingsScalesOfVanity>();
        EventModel wallCrackLedger = ModelDb.Event<ThingsWallCrackLedger>();
        EventModel spikedDoor = ModelDb.Event<ThingsSpikedDoor>();

        Assert(underdocks.AllEvents.Contains(bilge) && underdocks.AllEvents.Contains(clam), "Underdocks.AllEvents includes BilgeMines & ClamClaw");
        Assert(!underdocks.AllEvents.Contains(loom) && !underdocks.AllEvents.Contains(plinko), "Underdocks.AllEvents excludes Overgrowth minigames");
        Assert(overgrowth.AllEvents.Contains(loom) && overgrowth.AllEvents.Contains(plinko), "Overgrowth.AllEvents includes MossRuneLoom & ForestPlinko");
        Assert(overgrowth.AllEvents.Contains(lendingVine), "Overgrowth.AllEvents includes ThingsLendingVine");
        Assert(!underdocks.AllEvents.Contains(lendingVine) && !hive.AllEvents.Contains(lendingVine) && !glory.AllEvents.Contains(lendingVine) && !depths.AllEvents.Contains(lendingVine), "Only Overgrowth includes ThingsLendingVine");
        Assert(!overgrowth.AllEvents.Contains(bilge) && !overgrowth.AllEvents.Contains(clam), "Overgrowth.AllEvents excludes Underdocks minigames");
        Assert(!hive.AllEvents.Contains(bilge) && !hive.AllEvents.Contains(loom) && !hive.AllEvents.Contains(clam) && !hive.AllEvents.Contains(plinko) && !hive.AllEvents.Contains(cauldron), "Hive excludes Act 1 & Depths minigame events");
        Assert(hive.AllEvents.Contains(overstuffedBrood) && hive.AllEvents.Contains(spikedDoor), "Hive.AllEvents includes ThingsOverstuffedBrood & ThingsSpikedDoor");
        Assert(!glory.AllEvents.Contains(bilge) && !glory.AllEvents.Contains(loom) && !glory.AllEvents.Contains(clam) && !glory.AllEvents.Contains(plinko) && !glory.AllEvents.Contains(cauldron), "Glory excludes Act 1 & Depths minigame events");
        Assert(glory.AllEvents.Contains(scalesOfVanity) && glory.AllEvents.Contains(spikedDoor), "Glory.AllEvents includes ThingsScalesOfVanity & ThingsSpikedDoor");
        Assert(!depths.AllEvents.Contains(bilge) && !depths.AllEvents.Contains(loom) && !depths.AllEvents.Contains(clam) && !depths.AllEvents.Contains(plinko), "Depths excludes Act 1 minigame events");
        Assert(depths.AllEvents.Contains(cauldron) && depths.AllEvents.Contains(wallCrackLedger) && depths.AllEvents.Contains(spikedDoor), "Depths.AllEvents includes BronzeCauldron, WallCrackLedger & SpikedDoor");
        Assert(!underdocks.AllEvents.Contains(cauldron) && !overgrowth.AllEvents.Contains(cauldron), "Act 1 excludes ThingsBronzeCauldron");
        Assert(!underdocks.AllEvents.Contains(spikedDoor) && !overgrowth.AllEvents.Contains(spikedDoor), "Act 1 excludes ThingsSpikedDoor");

        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.EventBilgeMinesEnabled] = false,
            [ThingsModConfig.EventMossRuneLoomEnabled] = false,
            [ThingsModConfig.EventClamClawEnabled] = false,
            [ThingsModConfig.EventForestPlinkoEnabled] = false,
            [ThingsModConfig.EventBronzeCauldronEnabled] = false,
            [ThingsModConfig.EventLendingVineEnabled] = false,
            [ThingsModConfig.EventOverstuffedBroodEnabled] = false,
            [ThingsModConfig.EventScalesOfVanityEnabled] = false,
            [ThingsModConfig.EventWallCrackLedgerEnabled] = false,
            [ThingsModConfig.EventSpikedDoorEnabled] = false,
        });
        Assert(!underdocks.AllEvents.Contains(bilge), "Disabling EventBilgeMinesEnabled excludes ThingsBilgeMines");
        Assert(!underdocks.AllEvents.Contains(clam), "Disabling EventClamClawEnabled excludes ThingsClamClaw");
        Assert(!overgrowth.AllEvents.Contains(loom), "Disabling EventMossRuneLoomEnabled excludes ThingsMossRuneLoom");
        Assert(!overgrowth.AllEvents.Contains(plinko), "Disabling EventForestPlinkoEnabled excludes ThingsForestPlinko");
        Assert(!overgrowth.AllEvents.Contains(lendingVine), "Disabling EventLendingVineEnabled excludes ThingsLendingVine");
        Assert(!hive.AllEvents.Contains(overstuffedBrood), "Disabling EventOverstuffedBroodEnabled excludes ThingsOverstuffedBrood");
        Assert(!glory.AllEvents.Contains(scalesOfVanity), "Disabling EventScalesOfVanityEnabled excludes ThingsScalesOfVanity");
        Assert(!depths.AllEvents.Contains(wallCrackLedger), "Disabling EventWallCrackLedgerEnabled excludes ThingsWallCrackLedger");
        Assert(!depths.AllEvents.Contains(spikedDoor) && !hive.AllEvents.Contains(spikedDoor) && !glory.AllEvents.Contains(spikedDoor), "Disabling EventSpikedDoorEnabled excludes ThingsSpikedDoor across all Acts");
        Assert(!depths.AllEvents.Contains(cauldron), "Disabling EventBronzeCauldronEnabled excludes ThingsBronzeCauldron");
        ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(e => e.Key, e => (object?)e.Default));

        if (_hasVanillaPck)
        {
            foreach ((string path, Vector2I size) in new (string, Vector2I)[]
            {
                ("res://images/events/things_bilge_mines.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_moss_rune_loom.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_clam_claw.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_forest_plinko.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_bronze_cauldron.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_lending_vine.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_overstuffed_brood.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_scales_of_vanity.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_wall_crack_ledger.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_spiked_door.png", new Vector2I(3440, 1616)),
                ("res://images/events/minigames/things_bilge_sweep_bg.png", new Vector2I(1920, 1080)),
                ("res://images/events/minigames/things_rune_loom_bg.png", new Vector2I(1920, 1080)),
                ("res://images/events/minigames/things_clam_claw_bg.png", new Vector2I(1920, 1080)),
                ("res://images/events/minigames/things_forest_plinko_bg.png", new Vector2I(1920, 1080)),
                ("res://images/events/minigames/things_bronze_cauldron_bg.png", new Vector2I(1920, 1080)),
                ("res://images/events/minigames/things_bilge_tile_cover.png", new Vector2I(256, 256)),
                ("res://images/events/minigames/things_bilge_tile_glint.png", new Vector2I(256, 256)),
                ("res://images/events/minigames/things_rune_tile_stone.png", new Vector2I(256, 256)),
                ("res://images/events/minigames/things_bilge_flag_icon.png", new Vector2I(128, 128)),
                ("res://images/events/minigames/things_bilge_probe_icon.png", new Vector2I(128, 128)),
                ("res://images/events/minigames/things_clam_shell_upper.png", new Vector2I(320, 320)),
                ("res://images/events/minigames/things_clam_shell_lower.png", new Vector2I(320, 320)),
                ("res://images/events/minigames/things_clam_mouth_back.png", new Vector2I(320, 320)),
                ("res://images/events/minigames/things_clam_hinge.png", new Vector2I(220, 260)),
                ("res://images/events/minigames/things_plinko_seed.png", new Vector2I(128, 128)),
                ("res://images/events/minigames/things_plinko_peg.png", new Vector2I(128, 128)),
                ("res://images/events/minigames/things_plinko_shroom.png", new Vector2I(160, 160)),
                ("res://images/events/minigames/things_plinko_bowl.png", new Vector2I(256, 200)),
                ("res://images/events/minigames/things_loot_relic_rare_box.png", new Vector2I(256, 256)),
            })
            {
                Texture2D? tex = ResourceLoader.Load<Texture2D>(path);
                Assert(tex != null && tex.GetSize() == size, $"Loaded {path} at {size.X}x{size.Y}");
            }

            foreach (string vanillaIconPath in new[]
            {
                "res://images/events/crystal_sphere/crystal_sphere_relic.png",
                "res://images/events/crystal_sphere/crystal_sphere_gold.png",
                "res://images/events/crystal_sphere/crystal_sphere_big_gold.png",
                "res://images/events/crystal_sphere/crystal_sphere_rare_potion.png",
                "res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png",
                "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres",
                "res://images/powers/regen_power.png",
                "res://images/powers/hammer_time_power.png",
                "res://images/relics/old_coin.png",
            })
            {
                Texture2D? vtex = ResourceLoader.Load<Texture2D>(vanillaIconPath);
                Assert(vtex != null && vtex.GetSize().X > 0 && vtex.GetSize().Y > 0, $"Loaded vanilla icon {vanillaIconPath}");
            }

            foreach (string shaderPath in new[]
            {
                "res://shaders/things_bilge_water.gdshader",
                "res://shaders/things_rune_sap_bed.gdshader",
            })
            {
                Shader? shader = ResourceLoader.Load<Shader>(shaderPath);
                Assert(shader != null, $"Loaded shader {shaderPath}");
            }
        }
    }

    private static void AssertTextClean(string text, string label)
    {
        Assert(!string.IsNullOrWhiteSpace(text), $"Empty loc text: {label}");
        Assert(!Regex.IsMatch(text, @"\{[A-Za-z0-9_:.]+\}"), $"Unresolved placeholder in {label}: {text}");
    }

    private static string FormatEventLoc(EventModel model, string fullKey)
    {
        var s = new LocString("events", fullKey);
        model.DynamicVars.AddTo(s);
        return s.GetFormattedText();
    }

    private void VerifyLocalizationInBothLanguages()
    {
        if (!_hasVanillaPck)
            return;

        foreach (string lang in new[] { "zhs", "eng" })
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            RunState run = SetupTestRun($"loc-act1-{lang}");
            Player player = run.Players[0];

            var bilge = BeginEventForPlayer<ThingsBilgeMines>(player, run);
            AssertTextClean(bilge.Title.GetFormattedText(), $"THINGS_BILGE_MINES.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_BILGE_MINES.pages.INITIAL.description",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP.title",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP.description",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP_PAID.title",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP_PAID.description",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP_PAID_NO_GOLD.title",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SWEEP_PAID_NO_GOLD.description",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SKIM_EDGE.title",
                "THINGS_BILGE_MINES.pages.INITIAL.options.SKIM_EDGE.description",
                "THINGS_BILGE_MINES.pages.SWEEP_CLEAN.description",
                "THINGS_BILGE_MINES.pages.SWEEP_EMPTY.description",
                "THINGS_BILGE_MINES.pages.SWEEP_BURST_LOOT.description",
                "THINGS_BILGE_MINES.pages.SWEEP_BURST_EMPTY.description",
                "THINGS_BILGE_MINES.pages.SWEEP_BURST.loss",
                "THINGS_BILGE_MINES.pages.SKIM_EDGE.description",
                "THINGS_BILGE_MINES.ui.boardHeader",
                "THINGS_BILGE_MINES.ui.controlsHint",
                "THINGS_BILGE_MINES.ui.modeProbe",
                "THINGS_BILGE_MINES.ui.modeFlag",
                "THINGS_BILGE_MINES.ui.haulTitle",
                "THINGS_BILGE_MINES.ui.legendGlint",
                "THINGS_BILGE_MINES.ui.btnClaim",
                "THINGS_BILGE_MINES.ui.btnContinue",
                "THINGS_BILGE_MINES.ui.btnCashOut",
                "THINGS_BILGE_MINES.ui.btnLeaveEmpty",
                "THINGS_BILGE_MINES.ui.cellSac",
                "THINGS_BILGE_MINES.ui.cellPotion",
                "THINGS_BILGE_MINES.ui.cellCard",
                "THINGS_BILGE_MINES.ui.cellRelic",
                "THINGS_BILGE_MINES.ui.statusReady",
            })
            {
                AssertTextClean(FormatEventLoc(bilge, key), $"{key} ({lang})");
            }

            var loom = BeginEventForPlayer<ThingsMossRuneLoom>(player, run);
            AssertTextClean(loom.Title.GetFormattedText(), $"THINGS_MOSS_RUNE_LOOM.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_MOSS_RUNE_LOOM.selectPrompt",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.description",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.WEAVE.title",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.WEAVE.description",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.WEAVE_NO_CARD.title",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.WEAVE_NO_CARD.description",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.GATHER_SAP.title",
                "THINGS_MOSS_RUNE_LOOM.pages.INITIAL.options.GATHER_SAP.description",
                "THINGS_MOSS_RUNE_LOOM.pages.WEAVE_CLEAN.description",
                "THINGS_MOSS_RUNE_LOOM.pages.WEAVE_BLOOD.description",
                "THINGS_MOSS_RUNE_LOOM.pages.WEAVE_MOSS.description",
                "THINGS_MOSS_RUNE_LOOM.pages.WEAVE.loss",
                "THINGS_MOSS_RUNE_LOOM.pages.GATHER_SAP.description",
                "THINGS_MOSS_RUNE_LOOM.pages.ABORTED.description",
                "THINGS_MOSS_RUNE_LOOM.ui.boardHint",
                "THINGS_MOSS_RUNE_LOOM.ui.gainsHeader",
                "THINGS_MOSS_RUNE_LOOM.ui.costsHeader",
                "THINGS_MOSS_RUNE_LOOM.ui.lethalWarning",
                "THINGS_MOSS_RUNE_LOOM.ui.btnUndo",
                "THINGS_MOSS_RUNE_LOOM.ui.btnReset",
                "THINGS_MOSS_RUNE_LOOM.ui.btnConfirm",
                "THINGS_MOSS_RUNE_LOOM.ui.btnCancel",
                "THINGS_MOSS_RUNE_LOOM.ui.statusReady",
                "THINGS_MOSS_RUNE_LOOM.ui.rejectMaxLength",
                "THINGS_MOSS_RUNE_LOOM.ui.rejectNotAdjacent",
                "THINGS_MOSS_RUNE_LOOM.ui.rejectEnchantLimit",
                "THINGS_MOSS_RUNE_LOOM.ui.rejectNoGold",
                "THINGS_MOSS_RUNE_LOOM.ui.rejectNoMaxHp",
            })
            {
                AssertTextClean(FormatEventLoc(loom, key), $"{key} ({lang})");
            }

            var clam = BeginEventForPlayer<ThingsClamClaw>(player, run);
            AssertTextClean(clam.Title.GetFormattedText(), $"THINGS_CLAM_CLAW.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_CLAM_CLAW.pages.INITIAL.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_1.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_1.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_1_NO_GOLD.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_1_NO_GOLD.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_2.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_2.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_2_NO_GOLD.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_2_NO_GOLD.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_3.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_3.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_3_NO_GOLD.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.GRAB_3_NO_GOLD.description",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.LEAVE.title",
                "THINGS_CLAM_CLAW.pages.INITIAL.options.LEAVE.description",
                "THINGS_CLAM_CLAW.pages.CLAW_HAUL.description",
                "THINGS_CLAM_CLAW.pages.CLAW_EMPTY.description",
                "THINGS_CLAM_CLAW.pages.LEAVE.description",
                "THINGS_CLAM_CLAW.ui.controlsHint",
                "THINGS_CLAM_CLAW.ui.btnDrop",
                "THINGS_CLAM_CLAW.ui.haulTitle",
                "THINGS_CLAM_CLAW.ui.rules",
                "THINGS_CLAM_CLAW.ui.btnClaim",
                "THINGS_CLAM_CLAW.ui.btnFinishEarly",
                "THINGS_CLAM_CLAW.ui.btnLeaveEmpty",
                "THINGS_CLAM_CLAW.ui.statusReady",
            })
            {
                AssertTextClean(FormatEventLoc(clam, key), $"{key} ({lang})");
            }

            var plinko = BeginEventForPlayer<ThingsForestPlinko>(player, run);
            AssertTextClean(plinko.Title.GetFormattedText(), $"THINGS_FOREST_PLINKO.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_FOREST_PLINKO.pages.INITIAL.description",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_1.title",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_1.description",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_2.title",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_2.description",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_3.title",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.DROP_3.description",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.LEAVE.title",
                "THINGS_FOREST_PLINKO.pages.INITIAL.options.LEAVE.description",
                "THINGS_FOREST_PLINKO.pages.DROP.loss",
                "THINGS_FOREST_PLINKO.pages.PLINKO_HAUL.description",
                "THINGS_FOREST_PLINKO.pages.PLINKO_EMPTY.description",
                "THINGS_FOREST_PLINKO.pages.LEAVE.description",
                "THINGS_FOREST_PLINKO.ui.controlsHint",
                "THINGS_FOREST_PLINKO.ui.btnDrop",
                "THINGS_FOREST_PLINKO.ui.haulTitle",
                "THINGS_FOREST_PLINKO.ui.btnClaim",
                "THINGS_FOREST_PLINKO.ui.btnFinishEarly",
                "THINGS_FOREST_PLINKO.ui.btnLeaveEmpty",
                "THINGS_FOREST_PLINKO.ui.binRelic",
                "THINGS_FOREST_PLINKO.ui.binPotion",
                "THINGS_FOREST_PLINKO.ui.binCard",
                "THINGS_FOREST_PLINKO.ui.binUpgrade",
                "THINGS_FOREST_PLINKO.ui.summaryNone",
                "THINGS_FOREST_PLINKO.ui.statusReady",
            })
            {
                AssertTextClean(FormatEventLoc(plinko, key), $"{key} ({lang})");
            }

            RunState mpRun = SetupTestRun($"loc-cauldron-{lang}", playerCount: 2);
            var cauldron = BeginEventForPlayer<ThingsBronzeCauldron>(mpRun.Players[0], mpRun);
            AssertTextClean(cauldron.Title.GetFormattedText(), $"THINGS_BRONZE_CAULDRON.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_BRONZE_CAULDRON.pages.INITIAL.description",
                "THINGS_BRONZE_CAULDRON.pages.INITIAL.options.SHARE.title",
                "THINGS_BRONZE_CAULDRON.pages.INITIAL.options.SHARE.description",
                "THINGS_BRONZE_CAULDRON.pages.INITIAL.options.SCRAMBLE.title",
                "THINGS_BRONZE_CAULDRON.pages.INITIAL.options.SCRAMBLE.description",
                "THINGS_BRONZE_CAULDRON.pages.SHARE.description",
                "THINGS_BRONZE_CAULDRON.pages.SCRAMBLE_WIN.description",
                "THINGS_BRONZE_CAULDRON.pages.SCRAMBLE_EMPTY.description",
                "THINGS_BRONZE_CAULDRON.ui.controlsHint",
                "THINGS_BRONZE_CAULDRON.ui.standingsTitle",
                "THINGS_BRONZE_CAULDRON.ui.btnClaim",
                "THINGS_BRONZE_CAULDRON.ui.tagRareRelic",
                "THINGS_BRONZE_CAULDRON.ui.tagUncommonRelic",
                "THINGS_BRONZE_CAULDRON.ui.tagCommonRelic",
                "THINGS_BRONZE_CAULDRON.ui.statusCountdown",
                "THINGS_BRONZE_CAULDRON.ui.statusGo",
                "THINGS_BRONZE_CAULDRON.ui.statusComplete",
            })
            {
                AssertTextClean(FormatEventLoc(cauldron, key), $"{key} ({lang})");
            }

            // Malicious choice events localization coverage (zhs & eng)
            RunState malRun = SetupTestRun($"loc-malicious-{lang}");
            Player malPlayer = malRun.Players[0];
            var vine = BeginEventForPlayer<ThingsLendingVine>(malPlayer, malRun);
            AssertTextClean(vine.Title.GetFormattedText(), $"THINGS_LENDING_VINE.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_LENDING_VINE.pages.INITIAL.description",
                "THINGS_LENDING_VINE.pages.INITIAL.options.GIVE.title",
                "THINGS_LENDING_VINE.pages.INITIAL.options.GIVE.description",
                "THINGS_LENDING_VINE.pages.INITIAL.options.GIVE_LOCKED.title",
                "THINGS_LENDING_VINE.pages.INITIAL.options.GIVE_LOCKED.description",
                "THINGS_LENDING_VINE.pages.INITIAL.options.PULL.title",
                "THINGS_LENDING_VINE.pages.INITIAL.options.PULL.description",
                "THINGS_LENDING_VINE.pages.INITIAL.options.ESCAPE_POTION.title",
                "THINGS_LENDING_VINE.pages.INITIAL.options.ESCAPE_POTION.description",
                "THINGS_LENDING_VINE.pages.INITIAL.options.ESCAPE_CURSE.title",
                "THINGS_LENDING_VINE.pages.INITIAL.options.ESCAPE_CURSE.description",
                "THINGS_LENDING_VINE.pages.PULL_0.description",
                "THINGS_LENDING_VINE.pages.PULL_1.description",
                "THINGS_LENDING_VINE.pages.PULL_2.description",
                "THINGS_LENDING_VINE.pages.PULL_LOOP.description",
                "THINGS_LENDING_VINE.pages.GIVE.description",
                "THINGS_LENDING_VINE.pages.GIVE_NO_UPGRADE.description",
                "THINGS_LENDING_VINE.pages.ESCAPE_POTION.description",
                "THINGS_LENDING_VINE.pages.ESCAPE_CURSE.description",
                "THINGS_LENDING_VINE.pages.PULL.loss",
            })
                AssertTextClean(FormatEventLoc(vine, key), $"{key} ({lang})");

            var brood = BeginEventForPlayer<ThingsOverstuffedBrood>(malPlayer, malRun);
            AssertTextClean(brood.Title.GetFormattedText(), $"THINGS_OVERSTUFFED_BROOD.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_OVERSTUFFED_BROOD.clonePrompt",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.description",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.REACH_CORE.title",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.REACH_CORE.description",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.REACH_CORE_GOLD.title",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.REACH_CORE_GOLD.description",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.CLONE_SAC.title",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.CLONE_SAC.description",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.CLONE_LOCKED.title",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.CLONE_LOCKED.description",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.SLASH_FLEE.title",
                "THINGS_OVERSTUFFED_BROOD.pages.INITIAL.options.SLASH_FLEE.description",
                "THINGS_OVERSTUFFED_BROOD.pages.REACH_CORE.description",
                "THINGS_OVERSTUFFED_BROOD.pages.CLONE.description",
                "THINGS_OVERSTUFFED_BROOD.pages.CLONE.loss",
                "THINGS_OVERSTUFFED_BROOD.pages.FLEE.description",
                "THINGS_OVERSTUFFED_BROOD.pages.FLEE.loss",
                "THINGS_OVERSTUFFED_BROOD.pages.ABORTED.description",
            })
                AssertTextClean(FormatEventLoc(brood, key), $"{key} ({lang})");

            var scales = BeginEventForPlayer<ThingsScalesOfVanity>(malPlayer, malRun);
            AssertTextClean(scales.Title.GetFormattedText(), $"THINGS_SCALES_OF_VANITY.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.description",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE.title",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE.description",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE_GOLD.title",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE_GOLD.description",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE_LOCKED.title",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.EMPTY_PURSE_LOCKED.description",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.BLOOD_WEIGHT.title",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.BLOOD_WEIGHT.description",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.CRAWL_UNDER.title",
                "THINGS_SCALES_OF_VANITY.pages.INITIAL.options.CRAWL_UNDER.description",
                "THINGS_SCALES_OF_VANITY.pages.BLOOD_WEIGHT.selectionScreenPrompt",
                "THINGS_SCALES_OF_VANITY.pages.EMPTY_PURSE.description",
                "THINGS_SCALES_OF_VANITY.pages.BLOOD_WEIGHT.description",
                "THINGS_SCALES_OF_VANITY.pages.BLOOD_WEIGHT.loss",
                "THINGS_SCALES_OF_VANITY.pages.CRAWL_UNDER.description",
                "THINGS_SCALES_OF_VANITY.pages.CRAWL_UNDER_EMPTY.description",
                "THINGS_SCALES_OF_VANITY.pages.ABORTED.description",
            })
                AssertTextClean(FormatEventLoc(scales, key), $"{key} ({lang})");

            RelicModel uncommonForLoc = ModelDb.AllRelics.First(r => r.IsTradable && r.Rarity == RelicRarity.Uncommon && r.IsAllowed(malRun)).ToMutable();
            malPlayer.AddRelicInternal(uncommonForLoc);
            var ledger = BeginEventForPlayer<ThingsWallCrackLedger>(malPlayer, malRun);
            AssertTextClean(ledger.Title.GetFormattedText(), $"THINGS_WALL_CRACK_LEDGER.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.description",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.SIGN_LOAN.title",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.SIGN_LOAN.description",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.PAWN_RELIC.title",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.PAWN_RELIC.description",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.PAWN_LOCKED.title",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.PAWN_LOCKED.description",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.SHOVE_BACK.title",
                "THINGS_WALL_CRACK_LEDGER.pages.INITIAL.options.SHOVE_BACK.description",
                "THINGS_WALL_CRACK_LEDGER.pages.SIGN_LOAN.description",
                "THINGS_WALL_CRACK_LEDGER.pages.SIGN_LOAN.loss",
                "THINGS_WALL_CRACK_LEDGER.pages.PAWN_RELIC.description",
                "THINGS_WALL_CRACK_LEDGER.pages.SHOVE_BACK.description",
                "THINGS_WALL_CRACK_LEDGER.pages.SHOVE_BACK.loss",
            })
                AssertTextClean(FormatEventLoc(ledger, key), $"{key} ({lang})");

            RunState mpDoorRun = SetupTestRun($"loc-spiked-{lang}", playerCount: 2);
            var spiked = BeginEventForPlayer<ThingsSpikedDoor>(mpDoorRun.Players[0], mpDoorRun);
            AssertTextClean(spiked.Title.GetFormattedText(), $"THINGS_SPIKED_DOOR.title ({lang})");
            foreach (string key in new[]
            {
                "THINGS_SPIKED_DOOR.pages.INITIAL.description",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.PUSH_TOGETHER.title",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.PUSH_TOGETHER.description",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.DRAW_LOTS.title",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.DRAW_LOTS.description",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.SQUEEZE_PAST.title",
                "THINGS_SPIKED_DOOR.pages.INITIAL.options.SQUEEZE_PAST.description",
                "THINGS_SPIKED_DOOR.pages.PUSH_TOGETHER.description",
                "THINGS_SPIKED_DOOR.pages.PUSH_TOGETHER.loss",
                "THINGS_SPIKED_DOOR.pages.DRAW_LOTS_VICTIM.description",
                "THINGS_SPIKED_DOOR.pages.DRAW_LOTS_VICTIM.loss",
                "THINGS_SPIKED_DOOR.pages.DRAW_LOTS_SAFE.description",
                "THINGS_SPIKED_DOOR.pages.SQUEEZE_DOWNGRADE.description",
                "THINGS_SPIKED_DOOR.pages.SQUEEZE_GOLD.description",
            })
                AssertTextClean(FormatEventLoc(spiked, key), $"{key} ({lang})");
        }
    }

    private async Task VerifyBilgeMinesAsync()
    {
        // 1. Verify board invariants, flagging, 0-flood, and clean loot collection on Free Sweep
        RunState runFree = SetupTestRun("bilge-free-clean");
        Player pFree = runFree.Players[0];
        pFree.Gold = 10; // < 25 -> Paid Sweep should be locked!
        var evFree = BeginEventForPlayer<ThingsBilgeMines>(pFree, runFree);
        Assert(!evFree.IsDeterministic, "ThingsBilgeMines must be non-deterministic like CrystalSphere");
        Assert(evFree.CurrentOptions.Count == 3, "ThingsBilgeMines presents 3 options");
        Assert(evFree.CurrentOptions[1].IsLocked, "Paid sweep locks when player has < 25 Gold");

        int relicsBefore = pFree.Relics.Count;
        int goldBefore = pFree.Gold;
        SetTestDriver<ThingsBilgeMines, BilgeSweepMinigame>(evFree, game =>
        {
            Assert(game.MaxProbes == BilgeSweepMinigame.FreeProbes && game.ProbesRemaining == 5,
                "Free sweep starts with 5 probes");
            var all = game.AllCells.ToList();
            Assert(all.Count == 36, "Bilge board has 36 cells");
            Assert(all.Count(c => c.IsMine) == BilgeSweepMinigame.MineCount, "Board has exactly 6 toxin sacs");
            Assert(all.Count(c => c.IsDecoyGlint) == BilgeSweepMinigame.DecoyGlintMineCount, "Board has 2 decoy glint mines");
            Assert(all.Count(c => c.LootKind != BilgeLootKind.None) == BilgeSweepMinigame.TotalLootCount, "Board has 5 loot caches");
            Assert(all.Count(c => c.HasGlint) == 7, "Board has 7 total glints (5 loot + 2 decoy sacs)");
            Assert(all.Count(c => c.IsPreRevealed) >= 3, "At least 3 safe starter cells are pre-revealed");
            Assert(all.Where(c => c.IsPreRevealed).All(c => !c.IsMine && c.LootKind == BilgeLootKind.None),
                "Starter clues contain no mines or loot");

            // Verify exact Minesweeper adjacency math on every single cell
            foreach (BilgeCell c in all)
            {
                int actual = all.Count(o => o.IsMine && Math.Abs(o.X - c.X) <= 1 && Math.Abs(o.Y - c.Y) <= 1 && (o.X != c.X || o.Y != c.Y));
                Assert(c.AdjacentMines == actual, $"Exact adjacent mine count at ({c.X},{c.Y})");
            }

            // Flagging a mine consumes 0 probes and blocks accidental probing
            BilgeCell mine = all.First(c => c.IsMine && !c.IsRevealed);
            Assert(game.ToggleFlag(mine.X, mine.Y) && mine.IsFlagged && game.ProbesRemaining == 5,
                "Flagging uses 0 probes");
            Assert(!game.ProbeCell(mine.X, mine.Y) && game.ProbesRemaining == 5 && !game.HitMine,
                "Flagged cell cannot be accidentally probed");

            // Probe Gold + Common Relic safely, then Cash Out early!
            BilgeCell goldCell = all.First(c => c.LootKind is BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium);
            BilgeCell relicCell = all.First(c => c.LootKind == BilgeLootKind.CommonRelic);
            Assert(game.ProbeCell(goldCell.X, goldCell.Y), "Probe gold cell");
            Assert(game.ProbeCell(relicCell.X, relicCell.Y), "Probe common relic cell");
            Assert(game.CollectedGold == goldCell.GoldValue && game.CollectedCommonRelics == 1,
                "Loot tallies update immediately");
            game.CashOutAndFinish();
            return Task.CompletedTask;
        });

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(0)))
            await evFree.CurrentOptions[0].Chosen();

        Assert(evFree.IsFinished, "Free sweep finishes event cleanly");
        Assert(pFree.Gold > goldBefore, $"Collected gold was granted ({goldBefore} -> {pFree.Gold})");
        Assert(pFree.Relics.Count == relicsBefore + 1, "Collected Common Relic was granted");

        // 2. Paid 7-probe sweep deducts 25 Gold and handles mine burst while retaining already-collected loot
        RunState runBurst = SetupTestRun("bilge-paid-burst");
        Player pBurst = runBurst.Players[0];
        pBurst.Gold = 60;
        int hpBefore = pBurst.Creature.CurrentHp;
        var evBurst = BeginEventForPlayer<ThingsBilgeMines>(pBurst, runBurst);
        Assert(!evBurst.CurrentOptions[1].IsLocked, "Paid sweep unlocked with 60 Gold");

        int expectedCollectedGold = 0;
        SetTestDriver<ThingsBilgeMines, BilgeSweepMinigame>(evBurst, game =>
        {
            Assert(game.MaxProbes == BilgeSweepMinigame.PaidProbes, "Paid sweep starts with 7 probes");
            BilgeCell goldCell = game.AllCells.First(c => c.LootKind is BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium);
            BilgeCell mineCell = game.AllCells.First(c => c.IsMine);
            Assert(game.ProbeCell(goldCell.X, goldCell.Y), "Probe gold before hitting mine");
            expectedCollectedGold = goldCell.GoldValue;
            Assert(game.ProbeCell(mineCell.X, mineCell.Y), "Probe mine triggers burst");
            Assert(game.HitMine && game.IsComplete, "Hitting mine sets HitMine & IsComplete");
            Assert(game.AllCells.Where(c => c.IsMine).All(c => c.IsRevealed), "All mines revealed after burst");
            return Task.CompletedTask;
        });

        await evBurst.CurrentOptions[1].Chosen();
        Assert(evBurst.IsFinished, "Burst sweep finishes cleanly");
        Assert(pBurst.Creature.CurrentHp == hpBefore - ThingsBilgeMines.MineBurstDamage,
            $"Mine burst dealt {ThingsBilgeMines.MineBurstDamage} unblockable damage");
        Assert(pBurst.Gold == 60 - ThingsBilgeMines.PaidProbeGoldCost + expectedCollectedGold,
            "Paid sweep deducted 25 Gold and kept gold found before burst");

        // 3. Lethal mine burst transitions to death/loss cleanly
        RunState runLethal = SetupTestRun("bilge-lethal");
        Player pLethal = runLethal.Players[0];
        pLethal.Creature.SetCurrentHpInternal(5);
        var evLethal = BeginEventForPlayer<ThingsBilgeMines>(pLethal, runLethal);
        SetTestDriver<ThingsBilgeMines, BilgeSweepMinigame>(evLethal, game =>
        {
            BilgeCell mine = game.AllCells.First(c => c.IsMine);
            game.ProbeCell(mine.X, mine.Y);
            return Task.CompletedTask;
        });
        await evLethal.CurrentOptions[0].Chosen();
        Assert(evLethal.IsFinished && pLethal.Creature.IsDead, "Lethal mine burst kills player and finishes cleanly");

        // 4. Lossless Act 1 fallback option grants 20 Gold
        RunState runEdge = SetupTestRun("bilge-edge");
        Player pEdge = runEdge.Players[0];
        int edgeGoldBefore = pEdge.Gold;
        var evEdge = BeginEventForPlayer<ThingsBilgeMines>(pEdge, runEdge);
        await evEdge.CurrentOptions[2].Chosen();
        Assert(evEdge.IsFinished && pEdge.Gold == edgeGoldBefore + ThingsBilgeMines.EdgeSkimGold,
            "Skim Edge grants 20 Gold safely");
    }

    private async Task VerifyMossRuneLoomAsync()
    {
        // 1. Verify Attack / Skill / Power board legality + path rules + Cancel returns cleanly to INITIAL
        RunState runCancel = SetupTestRun("loom-cancel-and-rules");
        Player pCancel = runCancel.Players[0];
        pCancel.Gold = 15; // < 30 -> SapDrain must reject with NotEnoughGold!
        CardModel strike = pCancel.Deck.Cards.First(c => c.Type == CardType.Attack && c.IsUpgradable);
        CardModel defend = pCancel.Deck.Cards.First(c => c.Type == CardType.Skill && c.IsUpgradable);
        CardModel inflame = runCancel.CreateCard<Inflame>(pCancel);
        await CardPileCmd.Add(inflame, PileType.Deck);

        // Verify card-type legality on all 3 card types
        foreach (CardModel card in new[] { strike, defend, inflame })
        {
            var probeGame = new RuneLoomMinigame(pCancel, card, 0, runCancel.Rng.Niche);
            foreach (RuneLoomTile tile in probeGame.AllTiles.Where(t => t.Kind == RuneTileKind.Enchantment))
            {
                EnchantmentModel? ench = RuneLoomMinigame.GetCanonicalEnchantment(tile.EnchantmentKind);
                Assert(ench != null && ench.CanEnchant(card),
                    $"Board enchantment {tile.EnchantmentKind} is strictly legal for {card.Id.Entry} ({card.Type})");
            }
            // Board must hold all 4 distinct cost kinds AND every <=4-step path from Upgrade to any Enchantment must cross >=1 cost tile!
            var boardCosts = probeGame.AllTiles.Where(t => t.IsCost).Select(t => t.Kind).ToHashSet();
            Assert(boardCosts.SetEquals([RuneTileKind.ThornCut, RuneTileKind.SapDrain, RuneTileKind.BlightVine, RuneTileKind.ClingingTwig]),
                "Board holds all 4 distinct cost tiles");
            RuneLoomTile upgTile = probeGame.AllTiles.First(t => t.Kind == RuneTileKind.Upgrade);
            List<RuneLoomTile>? freeUpgToEnch = FindPath(probeGame, upgTile, t => t.Kind == RuneTileKind.Enchantment, avoidCosts: true);
            Assert(freeUpgToEnch == null, "Cannot connect Upgrade + Enchantment in <=4 steps without paying >=1 cost tile");
            List<RuneLoomTile>? paidUpgToEnch = FindPath(probeGame, upgTile, t => t.Kind == RuneTileKind.Enchantment, avoidCosts: false);
            Assert(paidUpgToEnch != null && paidUpgToEnch.Any(t => t.IsCost),
                "Upgrade + Enchantment IS connectable in 4 steps by crossing a barrier cost tile");
        }

        // Cancel on card picker returns to INITIAL without finishing
        var evCancel = BeginEventForPlayer<ThingsMossRuneLoom>(pCancel, runCancel);
        Assert(!evCancel.IsDeterministic, "ThingsMossRuneLoom must be non-deterministic");
        Assert(evCancel.CurrentOptions.Count == 2, "ThingsMossRuneLoom presents 2 initial options");
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await evCancel.CurrentOptions[0].Chosen();
        Assert(!evCancel.IsFinished && evCancel.CurrentOptions.Count == 2,
            "Canceling card selection returns cleanly to INITIAL");

        // Cancel on Rune Loom overlay also returns cleanly to INITIAL, while verifying path rejections
        SetTestDriver<ThingsMossRuneLoom, RuneLoomMinigame>(evCancel, game =>
        {
            RuneLoomTile t00 = game.GetTile(0, 0);
            Assert(game.ClickTile(0, 0, out _), "Start path at (0,0)");
            Assert(!game.ClickTile(1, 1, out RuneClickRejection diagRej) && diagRej == RuneClickRejection.NotAdjacent,
                "Diagonal move rejected as NotAdjacent");

            // Unaffordable SapDrain (Gold=15 < 30) must reject cleanly
            RuneLoomTile sapDrain = game.AllTiles.First(t => t.Kind == RuneTileKind.SapDrain);
            game.ResetPath();
            Assert(!game.ClickTile(sapDrain.X, sapDrain.Y, out RuneClickRejection goldRej) && goldRej == RuneClickRejection.NotEnoughGold,
                "SapDrain rejects when player Gold < 30");

            // Max length 4 enforcement (pick any affordable 4-step path starting at Upgrade)
            game.ResetPath();
            RuneLoomTile upgStart = game.AllTiles.First(t => t.Kind == RuneTileKind.Upgrade);
            List<RuneLoomTile>? fourStep = FindPath(game, upgStart, t => t.Kind == RuneTileKind.Enchantment, avoidCosts: false);
            if (fourStep == null || fourStep.Any(t => t.Kind == RuneTileKind.SapDrain))
            {
                // Find any 4-step path avoiding SapDrain
                fourStep = FindAffordableFourStep(game);
            }
            Assert(fourStep != null && fourStep.Count == 4, "Found affordable 4-step path for max-length test");
            foreach (RuneLoomTile step in fourStep!)
                Assert(game.ClickTile(step.X, step.Y, out _), $"Click 4-step tile ({step.X},{step.Y})");
            RuneLoomTile tail = fourStep[^1];
            RuneLoomTile? fifth = game.AllTiles.FirstOrDefault(t =>
                !fourStep.Contains(t) && Math.Abs(t.X - tail.X) + Math.Abs(t.Y - tail.Y) == 1);
            Assert(fifth != null && !game.ClickTile(fifth.X, fifth.Y, out RuneClickRejection maxRej) && maxRej == RuneClickRejection.MaxLengthReached,
                "5th tile rejected with MaxLengthReached");

            // Enchantment limit: start at one enchantment and step to an adjacent enchantment? They're diagonal,
            // so step Ench -> neighbor -> second Ench (3 steps) and assert EnchantmentLimitReached!
            game.ResetPath();
            RuneLoomTile firstEnch = game.AllTiles.First(t => t.Kind == RuneTileKind.Enchantment);
            List<RuneLoomTile>? enchBridge = FindPathToSecondEnchantment(game, firstEnch);
            if (enchBridge != null && enchBridge.Count == 3)
            {
                Assert(game.ClickTile(enchBridge[0].X, enchBridge[0].Y, out _), "Ench limit step 1");
                Assert(game.ClickTile(enchBridge[1].X, enchBridge[1].Y, out _), "Ench limit step 2");
                Assert(!game.ClickTile(enchBridge[2].X, enchBridge[2].Y, out RuneClickRejection enchRej) &&
                       enchRej == RuneClickRejection.EnchantmentLimitReached,
                    "Second enchantment in path rejected with EnchantmentLimitReached");
            }

            Assert(game.UndoLast(), "UndoLast pops last tile");
            game.Cancel();
            return Task.CompletedTask;
        });

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == strike)))
            await evCancel.CurrentOptions[0].Chosen();
        Assert(!evCancel.IsFinished && !strike.IsUpgraded && strike.Enchantment == null,
            "Canceling Rune Loom overlay leaves card untouched and returns to INITIAL");

        // 2. Full path crossing interior ThornCut to chain Upgrade + Enchantment, plus save/reload roundtrip!
        RunState runWeave = SetupTestRun("loom-weave-full");
        Player pWeave = runWeave.Players[0];
        pWeave.Gold = 100;
        pWeave.Creature.SetCurrentHpInternal(60);
        CardModel targetCard = pWeave.Deck.Cards.First(c => c.Type == CardType.Attack && c.IsUpgradable);
        int hpStart = pWeave.Creature.CurrentHp;
        int goldStart = pWeave.Gold;
        var evWeave = BeginEventForPlayer<ThingsMossRuneLoom>(pWeave, runWeave);

        ThingsRuneLoomOutcome? capturedOutcome = null;
        SetTestDriver<ThingsMossRuneLoom, RuneLoomMinigame>(evWeave, game =>
        {
            // Find a 3-or-4 step path starting at Upgrade corner, stepping through an interior cost tile,
            // and reaching an Enchantment tile!
            RuneLoomTile upg = game.AllTiles.First(t => t.Kind == RuneTileKind.Upgrade);
            List<RuneLoomTile>? bestPath = FindPath(game, upg, t => t.Kind == RuneTileKind.Enchantment);
            Assert(bestPath != null && bestPath.Count is >= 2 and <= 4,
                "Found valid orthogonal path from Upgrade to an Enchantment tile");
            foreach (RuneLoomTile step in bestPath!)
                Assert(game.ClickTile(step.X, step.Y, out _), $"Click path tile ({step.X},{step.Y}) [{step.Kind}]");
            capturedOutcome = game.BuildOutcome();
            Assert(game.ConfirmPath(), "ConfirmPath succeeds");
            return Task.CompletedTask;
        });

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == targetCard)))
            await evWeave.CurrentOptions[0].Chosen();

        Assert(evWeave.IsFinished, "Weave finishes event");
        Assert(targetCard.IsUpgraded, "Target card was Upgraded by Rune Loom");
        Assert(targetCard.Enchantment != null, "Target card received a vanilla Enchantment by Rune Loom");
        Assert(pWeave.Creature.CurrentHp == Math.Min(pWeave.Creature.MaxHp, hpStart - capturedOutcome!.DamageAmount + capturedOutcome.HealAmount + capturedOutcome.MaxHpGain),
            "HP reflects exact path damage/heal/maxHP deltas");
        Assert(pWeave.Gold == goldStart - capturedOutcome.GoldLoss + capturedOutcome.GoldGain,
            "Gold reflects exact path gold gain/loss deltas");

        // Save/Reload roundtrip proof!
        SerializableCard saved = targetCard.ToSerializable();
        CardModel restored = CardModel.FromSerializable(saved);
        Assert(restored.CurrentUpgradeLevel == targetCard.CurrentUpgradeLevel && restored.IsUpgraded,
            "Save/reload preserves card upgrade");
        Assert(restored.Enchantment != null && restored.Enchantment.Id == targetCard.Enchantment!.Id &&
               restored.Enchantment.Amount == targetCard.Enchantment.Amount,
            "Save/reload preserves card enchantment and exact amount");

        // 3. Lossless Act 1 Gather Sap heals 8 HP
        RunState runSap = SetupTestRun("loom-sap-heal");
        Player pSap = runSap.Players[0];
        pSap.Creature.SetCurrentHpInternal(40);
        var evSap = BeginEventForPlayer<ThingsMossRuneLoom>(pSap, runSap);
        await evSap.CurrentOptions[1].Chosen();
        Assert(evSap.IsFinished && pSap.Creature.CurrentHp == 40 + ThingsMossRuneLoom.GatherSapHeal,
            "Gather Sap heals 8 HP safely");
    }

    private static List<RuneLoomTile>? FindAffordableFourStep(RuneLoomMinigame game)
    {
        foreach (RuneLoomTile start in game.AllTiles.Where(t => t.Kind != RuneTileKind.SapDrain))
        {
            var queue = new Queue<List<RuneLoomTile>>();
            queue.Enqueue([start]);
            while (queue.Count > 0)
            {
                List<RuneLoomTile> cur = queue.Dequeue();
                if (cur.Count == 4)
                    return cur;
                RuneLoomTile tail = cur[^1];
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = tail.X + dx, ny = tail.Y + dy;
                    if (nx < 0 || nx >= RuneLoomMinigame.GridWidth || ny < 0 || ny >= RuneLoomMinigame.GridHeight)
                        continue;
                    RuneLoomTile next = game.GetTile(nx, ny);
                    if (cur.Contains(next) || next.Kind == RuneTileKind.SapDrain)
                        continue;
                    if (next.Kind == RuneTileKind.Enchantment && cur.Any(t => t.Kind == RuneTileKind.Enchantment))
                        continue;
                    queue.Enqueue([.. cur, next]);
                }
            }
        }
        return null;
    }

    private static List<RuneLoomTile>? FindPathToSecondEnchantment(RuneLoomMinigame game, RuneLoomTile startEnch)
    {
        foreach ((int dx1, int dy1) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int mx = startEnch.X + dx1, my = startEnch.Y + dy1;
            if (mx < 0 || mx >= RuneLoomMinigame.GridWidth || my < 0 || my >= RuneLoomMinigame.GridHeight)
                continue;
            RuneLoomTile mid = game.GetTile(mx, my);
            if (mid.Kind is RuneTileKind.Enchantment or RuneTileKind.SapDrain)
                continue;
            foreach ((int dx2, int dy2) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int ex = mid.X + dx2, ey = mid.Y + dy2;
                if (ex < 0 || ex >= RuneLoomMinigame.GridWidth || ey < 0 || ey >= RuneLoomMinigame.GridHeight)
                    continue;
                RuneLoomTile end = game.GetTile(ex, ey);
                if (end != startEnch && end.Kind == RuneTileKind.Enchantment)
                    return [startEnch, mid, end];
            }
        }
        return null;
    }

    private static List<RuneLoomTile>? FindPath(
        RuneLoomMinigame game,
        RuneLoomTile start,
        Func<RuneLoomTile, bool> goal,
        bool avoidCosts = false)
    {
        var queue = new Queue<List<RuneLoomTile>>();
        queue.Enqueue([start]);
        while (queue.Count > 0)
        {
            List<RuneLoomTile> cur = queue.Dequeue();
            RuneLoomTile tail = cur[^1];
            if (cur.Count >= 2 && goal(tail))
                return cur;
            if (cur.Count >= RuneLoomMinigame.MaxPathLength)
                continue;
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = tail.X + dx;
                int ny = tail.Y + dy;
                if (nx < 0 || nx >= RuneLoomMinigame.GridWidth || ny < 0 || ny >= RuneLoomMinigame.GridHeight)
                    continue;
                RuneLoomTile next = game.GetTile(nx, ny);
                if (cur.Contains(next))
                    continue;
                if (avoidCosts && next.IsCost)
                    continue;
                // Avoid double-enchantment mid-path
                if (next.Kind == RuneTileKind.Enchantment && cur.Any(t => t.Kind == RuneTileKind.Enchantment))
                    continue;
                queue.Enqueue([.. cur, next]);
            }
        }
        return null;
    }

    private static void VerifyMultiplayerPacketSerialization()
    {
        // Roundtrip ThingsBilgeSweepResultMessage
        var bilgeMsg = new ThingsBilgeSweepResultMessage
        {
            Location = new RunLocation(0, null, 0),
            HitMine = true,
            GoldAmount = 54,
            PotionCount = 1,
            CardCount = 1,
            CommonRelicCount = 1,
            ProbesUsed = 4,
            CashedOut = false,
        };
        var writer = new PacketWriter();
        bilgeMsg.Serialize(writer);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        var decodedBilge = new ThingsBilgeSweepResultMessage();
        decodedBilge.Deserialize(reader);
        Assert(decodedBilge.HitMine && decodedBilge.GoldAmount == 54 && decodedBilge.PotionCount == 1 &&
               decodedBilge.CardCount == 1 && decodedBilge.CommonRelicCount == 1 && decodedBilge.ProbesUsed == 4,
            "ThingsBilgeSweepResultMessage roundtrips cleanly");

        // Roundtrip ThingsRuneLoomResultMessage (both confirmed & cancelled)
        var loomMsg = new ThingsRuneLoomResultMessage
        {
            Location = new RunLocation(0, null, 1),
            Cancelled = false,
            DeckCardIndex = 3,
            UpgradeCard = true,
            EnchantmentKind = (byte)RuneLoomEnchantmentKind.Sharp,
            EnchantmentAmount = 2,
            HealAmount = 5,
            GoldGain = 20,
            MaxHpGain = 3,
            DamageAmount = 6,
            GoldLoss = 30,
            MaxHpLoss = 0,
            CurseCount = 1,
        };
        var writer2 = new PacketWriter();
        loomMsg.Serialize(writer2);
        var reader2 = new PacketReader();
        reader2.Reset(writer2.Buffer);
        var decodedLoom = new ThingsRuneLoomResultMessage();
        decodedLoom.Deserialize(reader2);
        ThingsRuneLoomOutcome sanitized = decodedLoom.ToSanitizedOutcome();
        Assert(!sanitized.Cancelled && sanitized.DeckCardIndex == 3 && sanitized.UpgradeCard &&
               sanitized.EnchantmentKind == RuneLoomEnchantmentKind.Sharp && sanitized.EnchantmentAmount == 2 &&
               sanitized.HealAmount == 5 && sanitized.GoldGain == 20 && sanitized.MaxHpGain == 3 &&
               sanitized.DamageAmount == 6 && sanitized.GoldLoss == 30 && sanitized.CurseCount == 1,
            "ThingsRuneLoomResultMessage roundtrips cleanly");

        // Roundtrip ThingsClamClawResultMessage
        var clamMsg = new ThingsClamClawResultMessage
        {
            Location = new RunLocation(0, null, 2),
            GoldAmount = 58,
            PotionCount = 1,
            CardCount = 1,
            CommonRelicCount = 0,
            RareRelicCount = 1,
            GrabsUsed = 3,
            SlippedCount = 1,
        };
        var writer3 = new PacketWriter();
        clamMsg.Serialize(writer3);
        var reader3 = new PacketReader();
        reader3.Reset(writer3.Buffer);
        var decodedClam = new ThingsClamClawResultMessage();
        decodedClam.Deserialize(reader3);
        ThingsClamClawOutcome clamOut = decodedClam.ToSanitizedOutcome();
        Assert(clamOut.GoldAmount == 58 && clamOut.PotionCount == 1 && clamOut.CardCount == 1 &&
               clamOut.CommonRelicCount == 0 && clamOut.RareRelicCount == 1 && clamOut.GrabsUsed == 3 && clamOut.SlippedCount == 1,
            "ThingsClamClawResultMessage roundtrips cleanly");

        // Roundtrip ThingsForestPlinkoResultMessage
        var plinkoMsg = new ThingsForestPlinkoResultMessage
        {
            Location = new RunLocation(0, null, 3),
            GoldAmount = 40,
            HealAmount = 5,
            MaxHpGain = 3,
            PotionCount = 1,
            CardCount = 1,
            CommonRelicCount = 0,
            RareRelicCount = 1,
            UpgradeCount = 1,
            DropsUsed = 3,
        };
        var writer4 = new PacketWriter();
        plinkoMsg.Serialize(writer4);
        var reader4 = new PacketReader();
        reader4.Reset(writer4.Buffer);
        var decodedPlinko = new ThingsForestPlinkoResultMessage();
        decodedPlinko.Deserialize(reader4);
        ThingsForestPlinkoOutcome plinkoOut = decodedPlinko.ToSanitizedOutcome();
        Assert(plinkoOut.GoldAmount == 40 && plinkoOut.HealAmount == 5 && plinkoOut.MaxHpGain == 3 &&
               plinkoOut.PotionCount == 1 && plinkoOut.CardCount == 1 && plinkoOut.RareRelicCount == 1 &&
               plinkoOut.UpgradeCount == 1 && plinkoOut.DropsUsed == 3,
            "ThingsForestPlinkoResultMessage roundtrips cleanly");

        // Roundtrip Bronze Cauldron multiplayer messages (Hand, GrabReq, GrabAck 64-bit mask, FinalOutcome)
        var handMsg = new ThingsCauldronHandMessage { Location = new RunLocation(0, null, 4), HandX = 315.5f, HandY = 280.25f, IsClenched = true };
        var wh = new PacketWriter(); handMsg.Serialize(wh);
        var rh = new PacketReader(); rh.Reset(wh.Buffer);
        var handDec = new ThingsCauldronHandMessage(); handDec.Deserialize(rh);
        Assert(MathF.Abs(handDec.HandX - 315.5f) < 0.01f && MathF.Abs(handDec.HandY - 280.25f) < 0.01f && handDec.IsClenched,
            "ThingsCauldronHandMessage roundtrips cleanly");

        var reqMsg = new ThingsCauldronGrabRequestMessage { Location = new RunLocation(0, null, 4), GrabX = 290f, GrabY = 330f };
        var wr = new PacketWriter(); reqMsg.Serialize(wr);
        var rr = new PacketReader(); rr.Reset(wr.Buffer);
        var reqDec = new ThingsCauldronGrabRequestMessage(); reqDec.Deserialize(rr);
        Assert(MathF.Abs(reqDec.GrabX - 290f) < 0.01f && MathF.Abs(reqDec.GrabY - 330f) < 0.01f,
            "ThingsCauldronGrabRequestMessage roundtrips cleanly");

        ulong highMask = (1UL << 55) | 0x15UL;
        var ackMsg = new ThingsCauldronGrabAckMessage { Location = new RunLocation(0, null, 4), GrabberNetId = 2UL, GrabX = 300f, GrabY = 310f, ClaimedBitmask = highMask, GrabsRemaining = 2 };
        var wa = new PacketWriter(); ackMsg.Serialize(wa);
        var ra = new PacketReader(); ra.Reset(wa.Buffer);
        var ackDec = new ThingsCauldronGrabAckMessage(); ackDec.Deserialize(ra);
        Assert(ackDec.GrabberNetId == 2UL && ackDec.ClaimedBitmask == highMask && ackDec.GrabsRemaining == 2,
            "ThingsCauldronGrabAckMessage 64-bit mask roundtrips cleanly");

        var finalMsg = ThingsCauldronFinalOutcomeMessage.FromOutcomes(new RunLocation(0, null, 4),
        [
            new ThingsBronzeCauldronPlayerOutcome(1UL, 80, 6, 1, 1, 0, 1, 0, 1, 4),
            new ThingsBronzeCauldronPlayerOutcome(2UL, 90, 0, 0, 0, 1, 1, 1, 0, 3),
        ]);
        var wf = new PacketWriter(); finalMsg.Serialize(wf);
        var rf = new PacketReader(); rf.Reset(wf.Buffer);
        var finalDec = new ThingsCauldronFinalOutcomeMessage(); finalDec.Deserialize(rf);
        List<ThingsBronzeCauldronPlayerOutcome> decList = finalDec.ToSanitizedOutcomes();
        Assert(decList.Count == 2 && decList[0].PlayerNetId == 1UL && decList[0].RareRelicCount == 1 && decList[0].MaxHpGain == 6 &&
               decList[1].PlayerNetId == 2UL && decList[1].UncommonRelicCount == 1 && decList[1].CardCount == 1,
            "ThingsCauldronFinalOutcomeMessage roundtrips cleanly");
    }

    private async Task VerifyBronzeCauldronAsync()
    {
        using IDisposable _selector = ProbeRuntimeApi.UseSelector(new ProbeCardSelector(0));
        // 1. Multiplayer-only gating: IsAllowed MUST be false in 1-player run and true in 2-player run!
        RunState soloRun = SetupTestRun("cauldron-solo-gate", playerCount: 1);
        EventModel canonical = ModelDb.Event<ThingsBronzeCauldron>();
        Assert(canonical.IsShared, "ThingsBronzeCauldron must be IsShared = true");
        Assert(!canonical.IsAllowed(soloRun), "ThingsBronzeCauldron is disallowed in 1-player runs");

        // 2. Peaceful Share option in 2-player run: both players receive Common Relic + 45G + Potion
        RunState mpShare = SetupTestRun("cauldron-mp-share", playerCount: 2);
        Assert(canonical.IsAllowed(mpShare), "ThingsBronzeCauldron is allowed in 2-player runs");
        Player p1 = mpShare.Players[0];
        Player p2 = mpShare.Players[1];
        var evShare1 = BeginEventForPlayer<ThingsBronzeCauldron>(p1, mpShare);
        var evShare2 = (ThingsBronzeCauldron)RunManager.Instance.EventSynchronizer.GetEventForPlayer(p2)!;
        Assert(evShare1.CurrentOptions.Count == 2, "ThingsBronzeCauldron presents 2 shared options (Share / Scramble)");

        LocalContext.NetId = p1.NetId;
        int p1GoldBefore = p1.Gold;
        int p2GoldBefore = p2.Gold;
        int p1RelicsBefore = p1.Relics.Count;
        RunLocation shareLoc = default;
        Task shareP1 = evShare1.CurrentOptions[0].Chosen();
        Task shareP2 = evShare2.CurrentOptions[0].Chosen();
        // Simulate remote Peer 2 selecting GoldReward (index 0) and finishing its reward set via native sync messages.
        RunManager.Instance.RewardsSetSynchronizer.HandleRewardSelectedMessage(
            new RewardSelectedMessage { location = shareLoc, setId = 0, rewardIndex = 0 }, p2.NetId);
        RunManager.Instance.RewardsSetSynchronizer.HandleRewardSetSkippedMessage(
            new RewardSetSkippedMessage { location = shareLoc, setId = 0 }, p2.NetId);
        await Task.WhenAll(shareP1, shareP2);
        Assert(evShare1.IsFinished && evShare2.IsFinished, "Both players finish peaceful Share option cleanly");
        Assert(p1.Gold >= p1GoldBefore + ThingsBronzeCauldron.ShareGoldAmount && p1.Relics.Count >= p1RelicsBefore + 1,
            "P1 received peaceful Share gold and Common Relic");
        Assert(p2.Gold >= p2GoldBefore + ThingsBronzeCauldron.ShareGoldAmount,
            "Remote P2 received peaceful Share gold cleanly through RewardsSetSynchronizer");

        // 3. Competitive Scramble in 2-player run: false-start stun + 3-2-1 countdown + non-duplicating grab conflict!
        RunState mpScramble = SetupTestRun("cauldron-mp-scramble", playerCount: 2);
        Player sp1 = mpScramble.Players[0];
        Player sp2 = mpScramble.Players[1];
        var evSc1 = BeginEventForPlayer<ThingsBronzeCauldron>(sp1, mpScramble);
        var evSc2 = (ThingsBronzeCauldron)RunManager.Instance.EventSynchronizer.GetEventForPlayer(sp2)!;
        ThingsBronzeCauldronPlayerOutcome? capturedP2Outcome = null;

        SetTestDriver<ThingsBronzeCauldron, BronzeCauldronMinigame>(evSc1, session =>
        {
            Assert(session.Phase == CauldronPhase.Countdown, "Scramble begins in 3-2-1 Countdown phase");
            Assert(session.Hands.Count == 2, "2-player scramble spawns 2 player hands");
            Assert(session.Hands[0].Y > BronzeCauldronMinigame.BoardSize && session.Hands[1].Y < 0f,
                "Multiplayer hands start resting at the outer screen edges like Treasure Room");
            int relicBoxes = session.Items.Count(i => i.Kind == CauldronTreasureKind.CommonRelic || i.Kind == CauldronTreasureKind.RareRelic);
            List<CauldronTreasureItem> coins = session.Items.Where(i => i.Kind == CauldronTreasureKind.Gold).ToList();
            Assert(session.Items.Count >= 50 && relicBoxes == 2 && coins.Count >= 35 &&
                   coins.All(c => c.Value >= BronzeCauldronMinigame.SmallCoinMinGold && c.Value <= BronzeCauldronMinigame.SmallCoinMaxGold),
                $"Enlarged cauldron spawns 50+ items carpeted with 3-10G coins & 2 random Common/Rare relic boxes (total={session.Items.Count}, coins={coins.Count}, relics={relicBoxes})");

            // Clicking before the opening 3-2-1 countdown ends MUST NOT trigger a grab, clench, or penalty!
            CauldronTreasureItem box0 = session.Items.First(i => i.Kind == CauldronTreasureKind.RareRelic || i.Kind == CauldronTreasureKind.CommonRelic);
            Assert(session.Items.Where(i => i.Kind == CauldronTreasureKind.RareRelic || i.Kind == CauldronTreasureKind.CommonRelic).All(i => i.OrbitRadius >= 88f),
                "All Relic cardboard boxes sit on active outer/mid orbits so rapid rotation makes sniping challenging");
            session.UpdateLocalHandPosition(box0.X, box0.Y);
            Assert(!session.TryLocalGrab(out _, out _, out _), "Early click during countdown does not trigger grab");
            Assert(!box0.IsClaimed && !session.LocalHand!.IsClenched && session.LocalHand.CooldownTimer <= 0f && session.LocalHand.GrabsRemaining == 3,
                "Countdown ignores left-click cleanly without claiming, clenching, stunning, or consuming grabs");

            // Verify rapid clockwise rotation around cauldron center (in screen coords +Y down, v0 x v1 > 0 is clockwise)
            Vector2 v0 = new(box0.X - BronzeCauldronMinigame.Center, box0.Y - BronzeCauldronMinigame.Center);
            for (int t = 0; t < 15; t++)
                session.Advance(1f / 60f);
            Vector2 v1 = new(box0.X - BronzeCauldronMinigame.Center, box0.Y - BronzeCauldronMinigame.Center);
            float crossCw = v0.X * v1.Y - v0.Y * v1.X;
            Assert(session.OrbitAngle > 0.35f && crossCw > 0f,
                $"Unclaimed treasures rapidly rotate clockwise around center (orbitAngle={session.OrbitAngle:F2}, cross={crossCw:F1})");

            // Advance past the remaining 3.0s countdown into live Scramble
            for (int t = 0; t < 180; t++)
                session.Advance(1f / 60f);
            Assert(session.Phase == CauldronPhase.Scramble, "Phase enters live Scramble after 3-2-1 countdown");

            // P1 snatches Relic Cardboard Box #1 cleanly under open palm
            session.UpdateLocalHandPosition(box0.X, box0.Y);
            Assert(session.TryLocalGrab(out _, out _, out _), "P1 grabs Relic box after GO");
            Assert(box0.IsClaimed && box0.ClaimedByNetId == sp1.NetId, "Relic box claimed exclusively by P1");

            // P2 tries grabbing exact same spot — MUST NOT duplicate P1's already-claimed Relic box!
            ulong p2MaskOnSame = session.ResolveAuthoritativeGrab(sp2.NetId, box0.X, box0.Y);
            Assert((p2MaskOnSame & (1UL << box0.Index)) == 0UL, "Host prevents P2 from duplicating P1's Relic box");

            // P2 grabs Relic Cardboard Box #2 (or remaining special + coins) cleanly
            CauldronTreasureItem box1 = session.Items.First(i => !i.IsClaimed && (i.Kind == CauldronTreasureKind.CommonRelic || i.Kind == CauldronTreasureKind.RareRelic));
            ulong p2Mask2 = session.ResolveAuthoritativeGrab(sp2.NetId, box1.X, box1.Y);
            Assert((p2Mask2 & (1UL << box1.Index)) != 0UL && box1.ClaimedByNetId == sp2.NetId,
                "P2 claims second Relic cardboard box cleanly");

            session.FinalizeAsHost();
            capturedP2Outcome = session.BuildPlayerOutcome(sp2.NetId);
            Assert(session.IsComplete, "Session finalizes cleanly");
            return Task.CompletedTask;
        });

        LocalContext.NetId = sp1.NetId;
        RunLocation scrambleLoc = default;
        Task sc1Task = evSc1.CurrentOptions[1].Chosen();
        Task sc2Task = evSc2.CurrentOptions[1].Chosen();
        // Remote Peer 2 completes its scramble rewards set via native sync message.
        RunManager.Instance.RewardsSetSynchronizer.HandleRewardSetSkippedMessage(
            new RewardSetSkippedMessage { location = scrambleLoc, setId = 0 }, sp2.NetId);
        await Task.WhenAll(sc1Task, sc2Task);
        Assert(evSc1.IsFinished && evSc2.IsFinished && capturedP2Outcome != null &&
               (capturedP2Outcome.CommonRelicCount + capturedP2Outcome.RareRelicCount) >= 1,
            "Both P1 and remote P2 completed shared Scramble session and finished cleanly");
    }

    private async Task VerifyClamClawAsync()
    {
        using IDisposable _selector = ProbeRuntimeApi.UseSelector(new ProbeCardSelector(0));
        // 1. Gold tier locking thresholds (25 / 45 / 65) + Leave (zero-reward)
        RunState runLock = SetupTestRun("clam-lock-leave");
        Player pLock = runLock.Players[0];
        pLock.Gold = 40; // >= 25 (unlocked), < 45 & < 65 (locked!)
        var evLock = BeginEventForPlayer<ThingsClamClaw>(pLock, runLock);
        Assert(!evLock.IsDeterministic, "ThingsClamClaw is non-deterministic");
        Assert(evLock.CurrentOptions.Count == 4, "ThingsClamClaw presents 4 options (3 gold tiers + Leave)");
        Assert(!evLock.CurrentOptions[0].IsLocked, "Tier 1 (25G) unlocked at 40G");
        Assert(evLock.CurrentOptions[1].IsLocked, "Tier 2 (45G) locked at 40G");
        Assert(evLock.CurrentOptions[2].IsLocked, "Tier 3 (65G) locked at 40G");
        Assert(!evLock.CurrentOptions[3].IsLocked, "Leave option always unlocked");

        await evLock.CurrentOptions[3].Chosen();
        Assert(evLock.IsFinished && pLock.Gold == 40, "Leave finishes with 0 cost and 0 reward");

        // 2. Tier 3 (65G, 3 grabs): center grab upper Gold + clear/snipe Relic + confirm rewards
        RunState runGrab = SetupTestRun("clam-tier3-grab");
        Player pGrab = runGrab.Players[0];
        pGrab.Gold = 100;
        int relicsBefore = pGrab.Relics.Count;
        var evGrab = BeginEventForPlayer<ThingsClamClaw>(pGrab, runGrab);
        Assert(!evGrab.CurrentOptions[2].IsLocked, "Tier 3 (65G) unlocked at 100G");

        SetTestDriver<ThingsClamClaw, ClamClawMinigame>(evGrab, game =>
        {
            Assert(game.MaxGrabs == 3 && game.GrabsRemaining == 3, "Tier 3 grants 3 grabs");
            Assert(game.Prizes.Count == ClamClawMinigame.TotalPrizeCount, "Clam tank spawns 28 gravity-stacked prizes");
            Assert(game.Prizes.All(p => p.Y >= 340f && p.Y <= ClamClawMinigame.PileFloorY),
                "All 28 prizes settle naturally at the bottom of the pit");
            Assert(game.Prizes.Count(p => p.Kind == ClamPrizeKind.CommonRelic || p.Kind == ClamPrizeKind.RareRelic) == 1,
                "Clam tank has 1 mystery Common/Rare cardboard relic parcel");

            // Grab 1: cleanly clamp a topmost reachable prize right at center
            ClamPrize topPrize = game.Prizes.OrderBy(p => p.Y).First();
            Assert(game.DropAtX(topPrize.X), "Drop 1 fires at topmost prize X");
            game.StepUntilIdle();
            Assert(game.GrabsRemaining == 2 && game.BuildOutcome().HasAnyLoot,
                $"Center grab clamps topmost prize cleanly inside shell (gold={game.CollectedGold})");

            // Grab 2 & 3: drop directly over the Mystery Relic box column (digging down through stacked cover if needed)
            ClamPrize relic = game.Prizes.First(p => p.Kind == ClamPrizeKind.CommonRelic || p.Kind == ClamPrizeKind.RareRelic);
            for (int tries = 0; tries < 2 && game.CanDrop(); tries++)
            {
                Assert(game.DropAtX(relic.X), $"Drop over Relic column succeeds (try {tries + 1})");
                game.StepUntilIdle();
            }
            Assert(game.IsComplete && game.GrabsUsed == 3, "All 3 grabs complete cleanly");
            Assert(game.BuildOutcome().HasAnyLoot, "3 center grabs collect real loot");
            return Task.CompletedTask;
        });

        await evGrab.CurrentOptions[2].Chosen();
        Assert(evGrab.IsFinished, "ClamClaw Tier 3 finishes cleanly");
        Assert(pGrab.Gold != 100 || pGrab.Relics.Count > relicsBefore, "Gold spent and clam salvage applied");

        // 3. Tier 1 (25G, 1 grab): empty-water miss yields CLAW_EMPTY cleanly
        RunState runMiss = SetupTestRun("clam-tier1-miss");
        Player pMiss = runMiss.Players[0];
        pMiss.Gold = 50;
        var evMiss = BeginEventForPlayer<ThingsClamClaw>(pMiss, runMiss);
        SetTestDriver<ThingsClamClaw, ClamClawMinigame>(evMiss, game =>
        {
            Assert(game.MaxGrabs == 1 && game.GrabsRemaining == 1, "Tier 1 grants 1 grab");
            // Find an x with no intersecting prize (or finish early cleanly)
            game.FinishEarly();
            Assert(!game.BuildOutcome().HasAnyLoot, "Empty finish yields 0 loot");
            return Task.CompletedTask;
        });
        await evMiss.CurrentOptions[0].Chosen();
        Assert(evMiss.IsFinished && pMiss.Gold == 25, "Tier 1 spent exactly 25G and finished cleanly");
    }

    private async Task VerifyForestPlinkoAsync()
    {
        using IDisposable _selector = ProbeRuntimeApi.UseSelector(new ProbeCardSelector(0));
        // 1. Options & zero-reward Leave
        RunState runLeave = SetupTestRun("plinko-leave");
        Player pLeave = runLeave.Players[0];
        int hpBefore = pLeave.Creature.CurrentHp;
        var evLeave = BeginEventForPlayer<ThingsForestPlinko>(pLeave, runLeave);
        Assert(!evLeave.IsDeterministic, "ThingsForestPlinko is non-deterministic");
        Assert(evLeave.CurrentOptions.Count == 4, "ThingsForestPlinko presents 4 options (3 HP tiers + Leave)");
        await evLeave.CurrentOptions[3].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == hpBefore, "Leave costs 0 HP and gives 0 reward");

        // 2. Tier 3 (13 HP, 3 drops): drop across left/center/right notches & verify physics + rewards
        RunState runPlay = SetupTestRun("plinko-tier3");
        Player pPlay = runPlay.Players[0];
        int hpStart = pPlay.Creature.CurrentHp;
        var evPlay = BeginEventForPlayer<ThingsForestPlinko>(pPlay, runPlay);
        int hpAfterCost = 0;

        SetTestDriver<ThingsForestPlinko, ForestPlinkoMinigame>(evPlay, game =>
        {
            hpAfterCost = pPlay.Creature.CurrentHp;
            Assert(hpAfterCost == hpStart - ForestPlinkoMinigame.Tier3HpCost,
                $"Tier 3 deducted exactly {ForestPlinkoMinigame.Tier3HpCost} HP ({hpStart} -> {hpAfterCost})");
            Assert(game.MaxDrops == 3 && game.DropsRemaining == 3, "Tier 3 grants 3 amber seeds");
            Assert(game.Pegs.Count == 45 && game.Bins.Count == 7, "Plinko board has 45 pegs and 7 bins");
            Assert(game.Pegs.Count(peg => peg.IsBumper) == 2, "Plinko board has 2 glow-shroom bumpers");

            int[] slots = [0, 3, 6];
            foreach (int slot in slots)
            {
                Assert(game.TryDropSeed(slot), $"Drop seed from notch {slot}");
                game.StepUntilBallLands();
                Assert(!game.ActiveBall.Active && game.ActiveBall.LastBinIndex is >= 0 and < 7,
                    $"Seed from notch {slot} landed cleanly in bin {game.ActiveBall.LastBinIndex}");
            }
            Assert(game.IsComplete && game.DropsUsed == 3, "All 3 seeds settled cleanly");
            Assert(game.BuildOutcome().HasAnyLoot, "3 Plinko drops always produce positive Act 1 boons");
            return Task.CompletedTask;
        });

        await evPlay.CurrentOptions[2].Chosen();
        Assert(evPlay.IsFinished, "ForestPlinko Tier 3 finished cleanly");

        // 3. Lethal HP check: spending Tier1HpCost (6 HP) at 6 HP triggers DROP.loss without opening minigame
        RunState runLethal = SetupTestRun("plinko-lethal");
        Player pLethal = runLethal.Players[0];
        pLethal.Creature.SetCurrentHpInternal(ForestPlinkoMinigame.Tier1HpCost);
        var evLethal = BeginEventForPlayer<ThingsForestPlinko>(pLethal, runLethal);
        bool driverCalled = false;
        SetTestDriver<ThingsForestPlinko, ForestPlinkoMinigame>(evLethal, _ =>
        {
            driverCalled = true;
            return Task.CompletedTask;
        });
        await evLethal.CurrentOptions[0].Chosen();
        Assert(pLethal.Creature.IsDead && evLethal.IsFinished && !driverCalled,
            "Lethal HP cost ends event with DROP.loss without opening overlay");

        // 4. Verify Relic/Upgrade bin fallback + Rare Relic support cleanly
        RunState runCap = SetupTestRun("plinko-cap-upgrade");
        Player pCap = runCap.Players[0];
        foreach (CardModel card in pCap.Deck.Cards.Where(c => c.IsUpgradable).ToList())
            CardCmd.Upgrade(card);
        int goldBeforeCap = pCap.Gold;
        var evCap = BeginEventForPlayer<ThingsForestPlinko>(pCap, runCap);
        var applyMethod = typeof(ThingsForestPlinko).GetMethod("ApplyOutcomeAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing ApplyOutcomeAsync on ThingsForestPlinko");
        await (Task)applyMethod.Invoke(evCap, [pCap, new ThingsForestPlinkoOutcome(
            GoldAmount: 20,
            HealAmount: 0,
            MaxHpGain: 3,
            PotionCount: 0,
            CardCount: 0,
            CommonRelicCount: 0,
            RareRelicCount: 1,
            UpgradeCount: 1,
            DropsUsed: 2)])!;
        Assert(evCap.IsFinished && pCap.Creature.MaxHp >= 83,
            "ApplyOutcomeAsync applies Max HP, Rare Relic, and Upgrade fallback cleanly");
    }

    private async Task VerifyLendingVineAsync()
    {
        // 1. Option 1 (GIVE): removes grabbed card and immediately upgrades EXACTLY 1 random card
        RunState runGive = SetupTestRun("vine-give");
        Player pGive = runGive.Players[0];
        var evGive = BeginEventForPlayer<ThingsLendingVine>(pGive, runGive);
        Assert(evGive.CurrentOptions.Count == 3, "ThingsLendingVine shows 3 options");
        CardModel? grabbed = evGive.GrabbedCard;
        Assert(grabbed != null && pGive.Deck.Cards.Contains(grabbed), "ThingsLendingVine picks a deck card");
        int deckBefore = pGive.Deck.Cards.Count;
        int upgradedBefore = pGive.Deck.Cards.Count(c => c.IsUpgraded);
        await evGive.CurrentOptions[0].Chosen();
        Assert(evGive.IsFinished, "GIVE finishes ThingsLendingVine");
        Assert(!pGive.Deck.Cards.Contains(grabbed!), "GIVE removes the exact grabbed card");
        Assert(pGive.Deck.Cards.Count == deckBefore - 1, "Deck shrunk by 1");
        Assert(pGive.Deck.Cards.Count(c => c.IsUpgraded) == upgradedBefore + 1, "GIVE upgrades exactly 1 card");

        // 2. Option 2 (PULL repeatable): escalates 5 -> 8 -> 11, keeps previous card, picks a new card
        RunState runPull = SetupTestRun("vine-pull");
        Player pPull = runPull.Players[0];
        var evPull = BeginEventForPlayer<ThingsLendingVine>(pPull, runPull);
        CardModel firstCard = evPull.GrabbedCard!;
        int hp0 = pPull.Creature.CurrentHp;
        Assert(evPull.CurrentHpLoss == 5, "Initial pull HP loss is 5");
        await evPull.CurrentOptions[1].Chosen();
        Assert(!evPull.IsFinished, "PULL keeps ThingsLendingVine open for next stage");
        Assert(pPull.Creature.CurrentHp == hp0 - 5, "First pull deals 5 unblockable damage");
        Assert(pPull.Deck.Cards.Contains(firstCard), "First pull preserves the grabbed card");
        Assert(evPull.CurrentHpLoss == 8, "Second pull escalates HP loss to 8");
        CardModel secondCard = evPull.GrabbedCard!;
        Assert(secondCard != firstCard, "Second pull grabs a different card");

        await evPull.CurrentOptions[1].Chosen();
        Assert(!evPull.IsFinished, "Second PULL keeps event open");
        Assert(pPull.Creature.CurrentHp == hp0 - 13, "Second pull deals 8 damage (total 13)");
        Assert(evPull.CurrentHpLoss == 11, "Third pull escalates HP loss to 11");

        // 3. Option 3 with Potion: discards 1 potion, adds zero curses
        RunState runPot = SetupTestRun("vine-escape-potion");
        Player pPot = runPot.Players[0];
        PotionModel samplePot = PotionFactory.CreateRandomPotionOutOfCombat(pPot, runPot.Rng.Niche)!.ToMutable();
        await PotionCmd.TryToProcure(samplePot, pPot);
        int potCountBefore = pPot.Potions.Count();
        Assert(potCountBefore >= 1, "Test player starts with at least 1 potion");
        var evPot = BeginEventForPlayer<ThingsLendingVine>(pPot, runPot);
        Assert(evPot.CurrentOptions[2].TextKey.EndsWith("ESCAPE_POTION"), "Shows ESCAPE_POTION when player owns a potion");
        await evPot.CurrentOptions[2].Chosen();
        Assert(evPot.IsFinished, "ESCAPE_POTION finishes event");
        Assert(pPot.Potions.Count() == potCountBefore - 1, "ESCAPE_POTION discards 1 potion");
        Assert(pPot.Deck.Cards.All(c => c.Type != CardType.Curse), "ESCAPE_POTION adds no curse");

        // 4. Option 3 without Potion: grants Clumsy (lighter non-eternal curse, NOT Normality!)
        RunState runCurse = SetupTestRun("vine-escape-curse");
        Player pCurse = runCurse.Players[0];
        var evCurse = BeginEventForPlayer<ThingsLendingVine>(pCurse, runCurse);
        Assert(evCurse.CurrentOptions[2].TextKey.EndsWith("ESCAPE_CURSE"), "Shows ESCAPE_CURSE when player owns 0 potions");
        await evCurse.CurrentOptions[2].Chosen();
        Assert(evCurse.IsFinished, "ESCAPE_CURSE finishes event");
        Assert(pCurse.Deck.Cards.Any(c => c is Clumsy), "ESCAPE_CURSE adds Clumsy");
        Assert(pCurse.Deck.Cards.All(c => c is not Normality), "ESCAPE_CURSE never adds Normality");
    }

    private async Task VerifyOverstuffedBroodAsync()
    {
        // 1. Option 1 (REACH_CORE): 1 Rare Relic + 2 Decay curses
        RunState runCore = SetupTestRun("brood-core");
        Player pCore = runCore.Players[0];
        int relicsBefore = pCore.Relics.Count;
        var evCore = BeginEventForPlayer<ThingsOverstuffedBrood>(pCore, runCore);
        Assert(evCore.CurrentOptions.Count == 3, "ThingsOverstuffedBrood shows 3 options");
        await evCore.CurrentOptions[0].Chosen();
        Assert(evCore.IsFinished, "REACH_CORE finishes event");
        Assert(pCore.Relics.Count == relicsBefore + 1 && pCore.Relics[^1].Rarity == RelicRarity.Rare,
            "REACH_CORE grants 1 Rare Relic");
        Assert(pCore.Deck.Cards.Count(c => c is Decay) == 2, "REACH_CORE adds 2 Decay curses");

        // 2. Option 2 (CLONE_SAC): cancel returns safely; confirm adds 2 full-fidelity clones & loses 10 Max HP
        RunState runClone = SetupTestRun("brood-clone");
        Player pClone = runClone.Players[0];
        CardModel upgradedStrike = pClone.Deck.Cards.First(c => c.IsUpgradable);
        CardCmd.Upgrade(upgradedStrike);
        int maxHpBefore = pClone.Creature.MaxHp;
        int deckBefore = pClone.Deck.Cards.Count;
        var evClone = BeginEventForPlayer<ThingsOverstuffedBrood>(pClone, runClone);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await evClone.CurrentOptions[1].Chosen();
        Assert(!evClone.IsFinished && pClone.Creature.MaxHp == maxHpBefore && pClone.Deck.Cards.Count == deckBefore,
            "Canceling CLONE_SAC returns to initial page without losing Max HP");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == upgradedStrike)))
            await evClone.CurrentOptions[1].Chosen();
        Assert(evClone.IsFinished, "CLONE_SAC finishes event");
        Assert(pClone.Creature.MaxHp == maxHpBefore - ThingsOverstuffedBrood.CloneMaxHpLoss, "CLONE_SAC loses 10 Max HP");
        Assert(pClone.Deck.Cards.Count == deckBefore + 2, "CLONE_SAC adds 2 copies");
        Assert(pClone.Deck.Cards.Count(c => c.Id == upgradedStrike.Id && c.IsUpgraded) >= 3,
            "CLONE_SAC preserves upgrades on both cloned copies");

        // 3. Option 3 (SLASH_FLEE): deals 9 unblockable damage
        RunState runFlee = SetupTestRun("brood-flee");
        Player pFlee = runFlee.Players[0];
        int hpBefore = pFlee.Creature.CurrentHp;
        var evFlee = BeginEventForPlayer<ThingsOverstuffedBrood>(pFlee, runFlee);
        await evFlee.CurrentOptions[2].Chosen();
        Assert(evFlee.IsFinished && pFlee.Creature.CurrentHp == hpBefore - ThingsOverstuffedBrood.FleeDamage,
            "SLASH_FLEE deals 9 unblockable damage and finishes");
    }

    private async Task VerifyScalesOfVanityAsync()
    {
        // 1. Option 1 locked when Gold < 250; works & cancel-safe when Gold >= 250
        RunState runLock = SetupTestRun("scales-locked");
        Player pLock = runLock.Players[0];
        pLock.Gold = 249;
        var evLock = BeginEventForPlayer<ThingsScalesOfVanity>(pLock, runLock);
        Assert(evLock.CurrentOptions[0].IsLocked, "EMPTY_PURSE locks when Gold < 250");

        RunState runPurse = SetupTestRun("scales-purse");
        Player pPurse = runPurse.Players[0];
        pPurse.Gold = 275;
        int relicsBefore = pPurse.Relics.Count;
        int deckBefore = pPurse.Deck.Cards.Count;
        var evPurse = BeginEventForPlayer<ThingsScalesOfVanity>(pPurse, runPurse);
        Assert(!evPurse.CurrentOptions[0].IsLocked, "EMPTY_PURSE unlocks when Gold >= 250");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await evPurse.CurrentOptions[0].Chosen();
        Assert(!evPurse.IsFinished && pPurse.Gold == 275 && pPurse.Deck.Cards.Count == deckBefore,
            "Canceling EMPTY_PURSE removal keeps all gold and cards intact");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(0)))
            await evPurse.CurrentOptions[0].Chosen();
        Assert(evPurse.IsFinished, "EMPTY_PURSE finishes");
        Assert(pPurse.Gold == 0, "EMPTY_PURSE removes ALL gold");
        Assert(pPurse.Deck.Cards.Count == deckBefore - 2, "EMPTY_PURSE removes 2 cards");
        Assert(pPurse.Relics.Count == relicsBefore + 1 && pPurse.Relics[^1].Rarity == RelicRarity.Rare,
            "EMPTY_PURSE grants 1 Rare Relic");

        // 2. Option 2 (BLOOD_WEIGHT): 35% current HP damage + pick 1 of 3 Upgraded class Rare cards
        RunState runBlood = SetupTestRun("scales-blood");
        Player pBlood = runBlood.Players[0];
        int hpBefore = pBlood.Creature.CurrentHp;
        int expectedDmg = ThingsScalesOfVanity.ComputeBloodDamage(pBlood);
        int deckBloodBefore = pBlood.Deck.Cards.Count;
        var evBlood = BeginEventForPlayer<ThingsScalesOfVanity>(pBlood, runBlood);
        var selector = new ProbeCardSelector(0);
        using (ProbeRuntimeApi.UseSelector(selector))
            await evBlood.CurrentOptions[1].Chosen();
        Assert(evBlood.IsFinished, "BLOOD_WEIGHT finishes");
        Assert(pBlood.Creature.CurrentHp == hpBefore - expectedDmg, "BLOOD_WEIGHT deals 35% current HP damage");
        Assert(selector.LastOffered.Count == 3 && selector.LastOffered.All(c => c.Rarity == CardRarity.Rare && c.IsUpgraded),
            "BLOOD_WEIGHT offers 3 Upgraded Rare cards");
        Assert(pBlood.Deck.Cards.Count == deckBloodBefore + 1 && pBlood.Deck.Cards[^1].Rarity == CardRarity.Rare && pBlood.Deck.Cards[^1].IsUpgraded,
            "BLOOD_WEIGHT adds chosen Upgraded Rare card to deck");

        // 3. Option 3 (CRAWL_UNDER): transforms 1 random non-basic card when available
        RunState runCrawl = SetupTestRun("scales-crawl");
        Player pCrawl = runCrawl.Players[0];
        CardModel nonBasic = pCrawl.RunState.CreateCard(
            pCrawl.Character.CardPool.AllCards.First(c => c.Rarity == CardRarity.Uncommon),
            pCrawl);
        await CardPileCmd.Add(nonBasic, PileType.Deck);
        var evCrawl = BeginEventForPlayer<ThingsScalesOfVanity>(pCrawl, runCrawl);
        await evCrawl.CurrentOptions[2].Chosen();
        Assert(evCrawl.IsFinished, "CRAWL_UNDER finishes");
        Assert(!pCrawl.Deck.Cards.Contains(nonBasic), "CRAWL_UNDER transforms the non-basic card");
    }

    private async Task VerifyWallCrackLedgerAsync()
    {
        // 1. Option 2 locked when player owns only Starter/Common relics
        RunState runLock = SetupTestRun("ledger-locked");
        Player pLock = runLock.Players[0];
        var evLock = BeginEventForPlayer<ThingsWallCrackLedger>(pLock, runLock);
        Assert(evLock.CurrentOptions.Count == 3, "ThingsWallCrackLedger shows 3 options");
        Assert(evLock.CurrentOptions[1].IsLocked, "PAWN_RELIC locks when player has no tradable Rare/Uncommon relic");

        // 2. Option 1 (SIGN_LOAN): +220 Gold, +1 Relic, -8 HP, +2 Debt curses
        int goldBefore = pLock.Gold;
        int hpBefore = pLock.Creature.CurrentHp;
        int relicsBefore = pLock.Relics.Count;
        await evLock.CurrentOptions[0].Chosen();
        Assert(evLock.IsFinished, "SIGN_LOAN finishes");
        Assert(pLock.Creature.CurrentHp == hpBefore - ThingsWallCrackLedger.LoanDamage, "SIGN_LOAN deals 8 damage");
        Assert(pLock.Gold == goldBefore + ThingsWallCrackLedger.LoanGold, "SIGN_LOAN grants 220 Gold");
        Assert(pLock.Relics.Count == relicsBefore + 1, "SIGN_LOAN grants 1 random Relic");
        Assert(pLock.Deck.Cards.Count(c => c is Debt) == 2, "SIGN_LOAN adds 2 Debt curses");

        // 3. Option 2 (PAWN_RELIC): loses tradable Rare/Uncommon relic -> Heals to FULL HP + gains 1 Common Relic
        RunState runPawn = SetupTestRun("ledger-pawn");
        Player pPawn = runPawn.Players[0];
        RelicModel rareRelic = ModelDb.AllRelics.First(r => r.IsTradable && r.Rarity == RelicRarity.Rare && r.IsAllowed(runPawn)).ToMutable();
        pPawn.AddRelicInternal(rareRelic);
        pPawn.Creature.SetCurrentHpInternal(20);
        var evPawn = BeginEventForPlayer<ThingsWallCrackLedger>(pPawn, runPawn);
        Assert(!evPawn.CurrentOptions[1].IsLocked && evPawn.PawnRelic == rareRelic,
            "PAWN_RELIC unlocks and targets owned Rare/Uncommon relic");
        await evPawn.CurrentOptions[1].Chosen();
        Assert(evPawn.IsFinished, "PAWN_RELIC finishes");
        Assert(!pPawn.Relics.Contains(rareRelic), "PAWN_RELIC removes the traded Rare/Uncommon relic");
        Assert(pPawn.Creature.CurrentHp == pPawn.Creature.MaxHp, "PAWN_RELIC heals player to FULL HP");
        Assert(pPawn.Relics[^1].Rarity == RelicRarity.Common, "PAWN_RELIC grants 1 Common Relic");

        // 4. Option 3 (SHOVE_BACK): deals 8 unblockable damage
        RunState runShove = SetupTestRun("ledger-shove");
        Player pShove = runShove.Players[0];
        int shoveHpBefore = pShove.Creature.CurrentHp;
        var evShove = BeginEventForPlayer<ThingsWallCrackLedger>(pShove, runShove);
        await evShove.CurrentOptions[2].Chosen();
        Assert(evShove.IsFinished && pShove.Creature.CurrentHp == shoveHpBefore - ThingsWallCrackLedger.RefuseDamage,
            "SHOVE_BACK deals 8 damage and finishes");
    }

    private async Task VerifySpikedDoorAsync()
    {
        // 1. IsAllowed: false for solo, true for 2-player & 3-player
        RunState soloRun = SetupTestRun("door-solo", playerCount: 1);
        Assert(!ModelDb.Event<ThingsSpikedDoor>().IsAllowed(soloRun), "ThingsSpikedDoor disallowed in solo");

        RunState mpPush = SetupTestRun("door-push", playerCount: 2);
        Assert(ModelDb.Event<ThingsSpikedDoor>().IsAllowed(mpPush), "ThingsSpikedDoor allowed in multiplayer");
        Assert(ModelDb.Event<ThingsSpikedDoor>().IsShared, "ThingsSpikedDoor is shared voting event");

        // Option 1 (PUSH_TOGETHER): every player loses 12 HP and gains 1 random Relic
        var evPush1 = BeginEventForPlayer<ThingsSpikedDoor>(mpPush.Players[0], mpPush);
        var evPush2 = (ThingsSpikedDoor)RunManager.Instance.EventSynchronizer.GetEventForPlayer(mpPush.Players[1])!;
        int[] pushHpBefore = mpPush.Players.Select(p => p.Creature.CurrentHp).ToArray();
        int[] pushRelicsBefore = mpPush.Players.Select(p => p.Relics.Count).ToArray();
        await evPush1.CurrentOptions[0].Chosen();
        await evPush2.CurrentOptions[0].Chosen();
        for (int i = 0; i < 2; i++)
        {
            Assert(mpPush.Players[i].Creature.CurrentHp == pushHpBefore[i] - ThingsSpikedDoor.PushAllDamage,
                $"PUSH_TOGETHER deals 12 dmg to P{i + 1}");
            Assert(mpPush.Players[i].Relics.Count == pushRelicsBefore[i] + 1,
                $"PUSH_TOGETHER grants 1 Relic to P{i + 1}");
        }

        // Option 2 (DRAW_LOTS): deterministic single scapegoat across ALL peers (tested with 3 players!)
        RunState mpLots = SetupTestRun("door-lots-3p", playerCount: 3);
        System.Reflection.MethodInfo pickScapegoat = typeof(ThingsSpikedDoor).GetMethod("PickDeterministicScapegoatNetId", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing PickDeterministicScapegoatNetId");
        ulong scapegoatId = (ulong)pickScapegoat.Invoke(null, [mpLots])!;
        Assert(mpLots.Players.Count(p => p.NetId == scapegoatId) == 1, "Scapegoat is exactly 1 party member");
        BeginEventForPlayer<ThingsSpikedDoor>(mpLots.Players[0], mpLots);
        int[] lotsHpBefore = mpLots.Players.Select(p => p.Creature.CurrentHp).ToArray();
        int[] lotsGoldBefore = mpLots.Players.Select(p => p.Gold).ToArray();
        int[] lotsRelicsBefore = mpLots.Players.Select(p => p.Relics.Count).ToArray();
        int[] lotsPotsBefore = mpLots.Players.Select(p => p.Potions.Count()).ToArray();
        foreach (Player p in mpLots.Players)
        {
            var evP = (ThingsSpikedDoor)RunManager.Instance.EventSynchronizer.GetEventForPlayer(p)!;
            await evP.CurrentOptions[1].Chosen();
            Assert(evP.IsFinished, $"DRAW_LOTS finishes for P{p.NetId}");
        }

        for (int i = 0; i < mpLots.Players.Count; i++)
        {
            Player p = mpLots.Players[i];
            if (p.NetId == scapegoatId)
            {
                Assert(p.Creature.CurrentHp == lotsHpBefore[i] - ThingsSpikedDoor.ScapegoatDamage,
                    "Scapegoat takes 22 damage");
                Assert(p.Deck.Cards.Any(c => c is Injury), "Scapegoat receives Injury curse");
                Assert(p.Relics.Count == lotsRelicsBefore[i] + 1 && p.Relics[^1].Rarity == RelicRarity.Rare,
                    "Scapegoat receives 1 Rare Relic");
                Assert(p.Gold == lotsGoldBefore[i] + ThingsSpikedDoor.ScapegoatGold,
                    "Scapegoat receives 100 Gold");
            }
            else
            {
                Assert(p.Creature.CurrentHp == lotsHpBefore[i], "Bystander takes 0 damage");
                Assert(p.Deck.Cards.All(c => c is not Injury), "Bystander gets no Injury");
                Assert(p.Gold == lotsGoldBefore[i] + ThingsSpikedDoor.BystanderGold,
                    "Bystander receives 50 Gold");
                Assert(p.Potions.Count() == lotsPotsBefore[i] + 1, "Bystander receives 1 Potion");
            }
        }

        // Option 3 (SQUEEZE_PAST): P1 has upgraded card -> downgrades 1; P2 has none -> loses 40 Gold
        RunState mpSqueeze = SetupTestRun("door-squeeze", playerCount: 2);
        Player pUpg = mpSqueeze.Players[0];
        Player pPlain = mpSqueeze.Players[1];
        CardCmd.Upgrade(pUpg.Deck.Cards.First(c => c.IsUpgradable));
        pUpg.Gold = 100;
        pPlain.Gold = 100;
        BeginEventForPlayer<ThingsSpikedDoor>(pUpg, mpSqueeze);
        await ((ThingsSpikedDoor)RunManager.Instance.EventSynchronizer.GetEventForPlayer(pUpg)!).CurrentOptions[2].Chosen();
        await ((ThingsSpikedDoor)RunManager.Instance.EventSynchronizer.GetEventForPlayer(pPlain)!).CurrentOptions[2].Chosen();
        Assert(pUpg.Deck.Cards.All(c => !c.IsUpgraded) && pUpg.Gold == 100,
            "SQUEEZE_PAST downgrades P1 upgraded card without losing gold");
        Assert(pPlain.Gold == 60, "SQUEEZE_PAST takes 40 Gold from P2 who has no upgraded cards");
    }

    private void VerifyEncounterGates()
    {
        // 1. ThingsScalesOfVanity: Gold >= 250 across ALL players + >= 2 removable cards
        RunState runScales = SetupTestRun("gate-scales", playerCount: 2);
        runScales.Players[0].Gold = 250;
        runScales.Players[1].Gold = 249;
        Assert(!ModelDb.Event<ThingsScalesOfVanity>().IsAllowed(runScales),
            "ThingsScalesOfVanity requires Gold >= 250 for ALL players");
        runScales.Players[1].Gold = 250;
        Assert(ModelDb.Event<ThingsScalesOfVanity>().IsAllowed(runScales),
            "ThingsScalesOfVanity allows when all players have Gold >= 250 and >= 2 removable cards");

        // 2. ThingsClamClaw: Gold >= 45
        RunState runClam = SetupTestRun("gate-clam");
        runClam.Players[0].Gold = 44;
        Assert(!ModelDb.Event<ThingsClamClaw>().IsAllowed(runClam), "ThingsClamClaw blocks Gold < 45");
        runClam.Players[0].Gold = 45;
        Assert(ModelDb.Event<ThingsClamClaw>().IsAllowed(runClam), "ThingsClamClaw allows Gold >= 45");

        // 3. ThingsForestPlinko: CurrentHp > 16
        RunState runPlinko = SetupTestRun("gate-plinko");
        runPlinko.Players[0].Creature.SetCurrentHpInternal(16);
        Assert(!ModelDb.Event<ThingsForestPlinko>().IsAllowed(runPlinko), "ThingsForestPlinko blocks HP <= 16");
        runPlinko.Players[0].Creature.SetCurrentHpInternal(17);
        Assert(ModelDb.Event<ThingsForestPlinko>().IsAllowed(runPlinko), "ThingsForestPlinko allows HP > 16");

        // 4. ThingsWallCrackLedger: CurrentHp > 8 && owns tradable Rare/Uncommon relic
        RunState runLedger = SetupTestRun("gate-ledger");
        Assert(!ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(runLedger),
            "ThingsWallCrackLedger blocks when player owns no tradable Rare/Uncommon relic");
        RelicModel rareRelic = ModelDb.AllRelics.First(r => r.IsTradable && r.Rarity == RelicRarity.Rare && r.IsAllowed(runLedger)).ToMutable();
        runLedger.Players[0].AddRelicInternal(rareRelic);
        runLedger.Players[0].Creature.SetCurrentHpInternal(8);
        Assert(!ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(runLedger),
            "ThingsWallCrackLedger blocks HP <= 8 even with rare relic");
        runLedger.Players[0].Creature.SetCurrentHpInternal(9);
        Assert(ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(runLedger),
            "ThingsWallCrackLedger allows HP > 8 with tradable Rare/Uncommon relic");

        // 5. ThingsSpikedDoor: co-op AND every player CurrentHp > 12
        RunState runDoor = SetupTestRun("gate-door", playerCount: 2);
        runDoor.Players[1].Creature.SetCurrentHpInternal(12);
        Assert(!ModelDb.Event<ThingsSpikedDoor>().IsAllowed(runDoor),
            "ThingsSpikedDoor blocks co-op when any teammate has HP <= 12");
        runDoor.Players[1].Creature.SetCurrentHpInternal(13);
        Assert(ModelDb.Event<ThingsSpikedDoor>().IsAllowed(runDoor),
            "ThingsSpikedDoor allows co-op when all players have HP > 12");

        // 6. ThingsCrowdedWard: healing-only event blocks full HP, allows <= 75% MaxHP
        RunState runWard = SetupTestRun("gate-ward");
        Assert(!ModelDb.Event<ThingsCrowdedWard>().IsAllowed(runWard),
            "ThingsCrowdedWard blocks full-HP players");
        runWard.Players[0].Creature.SetCurrentHpInternal((int)(runWard.Players[0].Creature.MaxHp * 0.75m));
        Assert(ModelDb.Event<ThingsCrowdedWard>().IsAllowed(runWard),
            "ThingsCrowdedWard allows injured players (<= 75% MaxHP)");

        // 7. ThingsLendingVine: TotalFloor >= 5 && CurrentHp > 5
        RunState runVine = SetupTestRun("gate-vine");
        Assert(!ModelDb.Event<ThingsLendingVine>().IsAllowed(runVine),
            "ThingsLendingVine blocks early floors (< 5)");
        for (int f = 0; f < 5; f++)
            runVine.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
        runVine.Players[0].Creature.SetCurrentHpInternal(5);
        Assert(!ModelDb.Event<ThingsLendingVine>().IsAllowed(runVine),
            "ThingsLendingVine blocks HP <= 5 even on floor >= 5");
        runVine.Players[0].Creature.SetCurrentHpInternal(6);
        Assert(ModelDb.Event<ThingsLendingVine>().IsAllowed(runVine),
            "ThingsLendingVine allows floor >= 5 with HP > 5 and removable card");

        // 8. ThingsOverstuffedBrood: CurrentHp > 9 && MaxHp > 10
        RunState runBrood = SetupTestRun("gate-brood");
        runBrood.Players[0].Creature.SetCurrentHpInternal(9);
        Assert(!ModelDb.Event<ThingsOverstuffedBrood>().IsAllowed(runBrood),
            "ThingsOverstuffedBrood blocks HP <= 9");
        runBrood.Players[0].Creature.SetCurrentHpInternal(10);
        Assert(ModelDb.Event<ThingsOverstuffedBrood>().IsAllowed(runBrood),
            "ThingsOverstuffedBrood allows HP > 9 && MaxHp > 10");

        // 9. ThingsRelicWorkshop, ThingsMedusa, ThingsSilveredGallery, ThingsShadowCloakroom, ThingsPotionTasting, ThingsGrudgeVendingMachine, ThingsReturnsCounter
        RunState runShop = SetupTestRun("gate-economy");
        Player pe = runShop.Players[0];
        pe.Gold = 30;
        Assert(!ModelDb.Event<ThingsRelicWorkshop>().IsAllowed(runShop), "ThingsRelicWorkshop blocks low gold / no tradable relic");
        Assert(!ModelDb.Event<ThingsMedusa>().IsAllowed(runShop), "ThingsMedusa blocks Gold < 75 without PetrifiedToad");
        Assert(!ModelDb.Event<ThingsSilveredGallery>().IsAllowed(runShop), "ThingsSilveredGallery blocks Gold < 95");
        Assert(!ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(runShop), "ThingsShadowCloakroom blocks Gold < 45");
        Assert(!ModelDb.Event<ThingsPotionTasting>().IsAllowed(runShop), "ThingsPotionTasting blocks Gold < 40 and 0 potions");
        Assert(!ModelDb.Event<ThingsGrudgeVendingMachine>().IsAllowed(runShop), "ThingsGrudgeVendingMachine blocks Gold < 35");
        Assert(!ModelDb.Event<ThingsReturnsCounter>().IsAllowed(runShop), "ThingsReturnsCounter blocks Gold < 40");

        pe.Gold = 100;
        RelicModel tradable = ModelDb.AllRelics.First(r => r.IsTradable && r.Rarity == RelicRarity.Common && r.IsAllowed(runShop)).ToMutable();
        pe.AddRelicInternal(tradable);
        Assert(ModelDb.Event<ThingsRelicWorkshop>().IsAllowed(runShop), "ThingsRelicWorkshop allows tradable relic + Gold >= 40");
        Assert(ModelDb.Event<ThingsMedusa>().IsAllowed(runShop), "ThingsMedusa allows Gold >= 75 + Steady target");
        Assert(ModelDb.Event<ThingsSilveredGallery>().IsAllowed(runShop), "ThingsSilveredGallery allows Gold >= 95 + HP > 16");
        Assert(ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(runShop), "ThingsShadowCloakroom allows Gold >= 45 + eligible card");
        Assert(ModelDb.Event<ThingsPotionTasting>().IsAllowed(runShop), "ThingsPotionTasting allows Gold >= 40");
        Assert(ModelDb.Event<ThingsGrudgeVendingMachine>().IsAllowed(runShop), "ThingsGrudgeVendingMachine allows Gold >= 35 + HP > 8");
        Assert(ModelDb.Event<ThingsReturnsCounter>().IsAllowed(runShop), "ThingsReturnsCounter allows Gold >= 40 + HP/MaxHP safe");
    }

    private async Task RenderLiveUiAndOverlaysAsync()
    {
        string? output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);

        var view = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(view);
        GetTree().Root.Size = view.Size;

        foreach (string lang in new[] { "zhs", "eng" })
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();

            // 1. Native NEventLayout screenshots for all 10 events (5 minigame + 5 malicious choice events)
            foreach ((string key, Func<Player, RunState, EventModel> begin, int expectedOpts, int players) in new (string, Func<Player, RunState, EventModel>, int, int)[]
            {
                ("bilge_mines", (p, r) => BeginEventForPlayer<ThingsBilgeMines>(p, r), 3, 1),
                ("moss_rune_loom", (p, r) => BeginEventForPlayer<ThingsMossRuneLoom>(p, r), 2, 1),
                ("clam_claw", (p, r) => BeginEventForPlayer<ThingsClamClaw>(p, r), 4, 1),
                ("forest_plinko", (p, r) => BeginEventForPlayer<ThingsForestPlinko>(p, r), 4, 1),
                ("bronze_cauldron", (p, r) => BeginEventForPlayer<ThingsBronzeCauldron>(p, r), 2, 2),
                ("lending_vine", (p, r) => BeginEventForPlayer<ThingsLendingVine>(p, r), 3, 1),
                ("overstuffed_brood", (p, r) => BeginEventForPlayer<ThingsOverstuffedBrood>(p, r), 3, 1),
                ("scales_of_vanity", (p, r) => { p.Gold = 280; return BeginEventForPlayer<ThingsScalesOfVanity>(p, r); }, 3, 1),
                ("wall_crack_ledger", (p, r) =>
                {
                    RelicModel rare = ModelDb.AllRelics.First(rel => rel.IsTradable && rel.Rarity == RelicRarity.Rare && rel.IsAllowed(r)).ToMutable();
                    p.AddRelicInternal(rare);
                    p.Creature.SetCurrentHpInternal(42);
                    return BeginEventForPlayer<ThingsWallCrackLedger>(p, r);
                }, 3, 1),
                ("spiked_door", (p, r) => BeginEventForPlayer<ThingsSpikedDoor>(p, r), 3, 2),
            })
            {
                RunState run = SetupTestRun($"render-ev-{key}-{lang}", playerCount: players);
                Player p = run.Players[0];
                p.Gold = 99;
                EventModel model = begin(p, run);

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = Colors.Black });
                NEventLayout layout = GD.Load<PackedScene>(NEventLayout.defaultScenePath).Instantiate<NEventLayout>();
                world.AddChild(layout);
                layout.SetEvent(model);
                layout.SetTitle(model.Title.GetFormattedText());
                model.DynamicVars.AddTo(model.Description!);
                layout.SetDescription(model.Description!.GetFormattedText());
                layout.AddOptions(model.CurrentOptions);
                layout.OnSetupComplete();

                await ToSignal(GetTree().CreateTimer(2.2), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                var opts = layout.OptionButtons.ToList();
                Assert(opts.Count == expectedOpts, $"Native layout renders {expectedOpts} options for {key} ({lang})");
                float bottomY = opts[^1].GlobalPosition.Y + opts[^1].Size.Y;
                Assert(bottomY <= 1048f, $"Layout {key} ({lang}) bottomY={bottomY:F1} fits in 1080p safe zone");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-event-{key}-{lang}.png")) == Error.Ok,
                    $"Save native-event-{key}-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 2. Live 1920x1080 screenshot of NBilgeSweepScreen mid-play (with flag + found loot + glints!)
            {
                RunState run = SetupTestRun($"render-overlay-bilge-{lang}");
                Player p = run.Players[0];
                var game = new BilgeSweepMinigame(p, run.Rng.Niche, BilgeSweepMinigame.FreeProbes);
                BilgeCell goldCell = game.AllCells.First(c => c.LootKind is BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium);
                BilgeCell mineCell = game.AllCells.First(c => c.IsMine);
                game.ProbeCell(goldCell.X, goldCell.Y);
                game.ToggleFlag(mineCell.X, mineCell.Y);

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = new Color("#08141a") });
                NBilgeSweepScreen screen = NBilgeSweepScreen.Create(game);
                screen.Size = view.Size;
                world.AddChild(screen);
                screen.AfterOverlayOpened();
                screen.AfterOverlayShown();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                TextureRect bilgeBg = screen.FindChild("BilgeBackdrop", true, false) as TextureRect
                    ?? throw new InvalidOperationException("Missing BilgeBackdrop");
                Control bilgePit = screen.FindChild("BilgePlayfield", true, false) as Control
                    ?? throw new InvalidOperationException("Missing BilgePlayfield");
                Control bilgeRight = screen.FindChild("RightUi", true, false) as Control
                    ?? throw new InvalidOperationException("Missing Bilge RightUi");
                Rect2 bilgeBgRect = bilgeBg.GetGlobalRect();
                Rect2 bilgePitRect = bilgePit.GetGlobalRect();
                Rect2 bilgeRightRect = bilgeRight.GetGlobalRect();
                Assert(bilgeBg.Texture != null && bilgeBgRect.Size == new Vector2(1920f, 1080f),
                    $"NBilgeSweepScreen ({lang}) renders full-bleed 1920x1080 backdrop: {bilgeBgRect}");
                Assert(bilgePitRect.Position == new Vector2(500f, 215f) && bilgePitRect.Size == new Vector2(630f, 630f),
                    $"NBilgeSweepScreen ({lang}) pit locked cleanly at (500,215,630x630): {bilgePitRect}");
                Assert(bilgeRightRect.Position.X >= 1240f && bilgeRightRect.End.X <= 1900f &&
                       bilgeRightRect.Position.Y >= 20f && bilgeRightRect.End.Y <= 1060f,
                    $"NBilgeSweepScreen ({lang}) RightUi cleanly right of pit in 1080p: {bilgeRightRect}");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-overlay-bilge_sweep-{lang}.png")) == Error.Ok,
                    $"Save native-overlay-bilge_sweep-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 3. Live 1920x1080 screenshot of NRuneLoomScreen mid-play (with 3-step glowing amber vine path!)
            {
                RunState run = SetupTestRun($"render-overlay-loom-{lang}");
                Player p = run.Players[0];
                p.Gold = 99;
                CardModel strike = p.Deck.Cards.First(c => c.Type == CardType.Attack && c.IsUpgradable);
                var game = new RuneLoomMinigame(p, strike, 0, run.Rng.Niche);
                RuneLoomTile upg = game.AllTiles.First(t => t.Kind == RuneTileKind.Upgrade);
                List<RuneLoomTile>? path = FindPath(game, upg, t => t.Kind == RuneTileKind.Enchantment);
                if (path != null)
                {
                    foreach (RuneLoomTile step in path)
                        game.ClickTile(step.X, step.Y, out _);
                }

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = new Color("#09150d") });
                NRuneLoomScreen screen = NRuneLoomScreen.Create(game);
                screen.Size = view.Size;
                world.AddChild(screen);
                screen.AfterOverlayOpened();
                screen.AfterOverlayShown();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                TextureRect loomBg = screen.FindChild("LoomBackdrop", true, false) as TextureRect
                    ?? throw new InvalidOperationException("Missing LoomBackdrop");
                Control loomPit = screen.FindChild("LoomPlayfield", true, false) as Control
                    ?? throw new InvalidOperationException("Missing LoomPlayfield");
                Control loomRight = screen.FindChild("RightUi", true, false) as Control
                    ?? throw new InvalidOperationException("Missing Loom RightUi");
                Rect2 loomBgRect = loomBg.GetGlobalRect();
                Rect2 loomPitRect = loomPit.GetGlobalRect();
                Rect2 loomRightRect = loomRight.GetGlobalRect();
                Assert(loomBg.Texture != null && loomBgRect.Size == new Vector2(1920f, 1080f),
                    $"NRuneLoomScreen ({lang}) renders full-bleed 1920x1080 backdrop: {loomBgRect}");
                Assert(loomPitRect.Position == new Vector2(500f, 215f) && loomPitRect.Size == new Vector2(630f, 630f),
                    $"NRuneLoomScreen ({lang}) pit locked cleanly at (500,215,630x630): {loomPitRect}");
                Assert(loomRightRect.Position.X >= 1240f && loomRightRect.End.X <= 1900f &&
                       loomRightRect.Position.Y >= 20f && loomRightRect.End.Y <= 1060f,
                    $"NRuneLoomScreen ({lang}) RightUi cleanly right of pit in 1080p: {loomRightRect}");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-overlay-rune_loom-{lang}.png")) == Error.Ok,
                    $"Save native-overlay-rune_loom-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 4. Live 1920x1080 screenshot of NClamClawScreen mid-ascent (Sewer Clam clamping a prize INSIDE its shell!)
            {
                RunState run = SetupTestRun($"render-overlay-clam-{lang}");
                Player p = run.Players[0];
                p.Gold = 54;
                var game = new ClamClawMinigame(p, run.Rng.Niche, ClamClawMinigame.Tier3Grabs);
                ClamPrize firstTop = game.Prizes.OrderBy(pr => pr.Y).First();
                game.DropAtX(firstTop.X);
                game.StepUntilIdle();

                // Grab #2: drop squarely onto a topmost remaining Potion/Gold and pause mid-ascent with prize cradled INSIDE the Sewer Clam!
                ClamPrize target2 = game.Prizes
                    .Where(pr => pr.State == ClamPrizeState.InTank)
                    .OrderBy(pr => pr.Y)
                    .First();
                float startY = target2.Y;
                game.DropAtX(target2.X);
                for (int step = 0; step < 300; step++)
                {
                    game.Advance(1f / 60f);
                    if (game.Phase == ClamCranePhase.Ascending && game.HeldPrize != null && game.CraneY <= startY - 145f)
                        break;
                }

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = new Color("#040d11") });
                NClamClawScreen screen = NClamClawScreen.Create(game);
                screen.Size = view.Size;
                world.AddChild(screen);
                screen.AfterOverlayOpened();
                screen.AfterOverlayShown();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                TextureRect clamBg = screen.FindChild("ClamBackdrop", true, false) as TextureRect
                    ?? throw new InvalidOperationException("Missing ClamBackdrop");
                Control clamPit = screen.FindChild("ClamPlayfield", true, false) as Control
                    ?? throw new InvalidOperationException("Missing ClamPlayfield");
                Control clamRight = screen.FindChild("RightUi", true, false) as Control
                    ?? throw new InvalidOperationException("Missing Clam RightUi");
                Assert(clamBg.Texture != null && clamBg.GetGlobalRect().Size == new Vector2(1920f, 1080f),
                    $"NClamClawScreen ({lang}) renders full-bleed 1920x1080 backdrop");
                Rect2 clamRect = clamPit.GetGlobalRect();
                Assert(clamRect.Position.DistanceTo(new Vector2(552f, 264f)) < 1f && MathF.Abs(clamRect.Size.X - 546f) < 1f && MathF.Abs(clamRect.Size.Y - 546f) < 1f,
                    $"NClamClawScreen ({lang}) pit locked cleanly inside verdigris window at (552,264,546x546), got {clamRect}");
                Assert(clamRight.GetGlobalRect().Position.X >= 1240f && clamRight.GetGlobalRect().End.X <= 1900f,
                    $"NClamClawScreen ({lang}) RightUi cleanly right of pit");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-overlay-clam_claw-{lang}.png")) == Error.Ok,
                    $"Save native-overlay-clam_claw-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 5. Live 1920x1080 screenshot of NForestPlinkoScreen mid-bounce (golden amber seed + sap trail!)
            {
                RunState run = SetupTestRun($"render-overlay-plinko-{lang}");
                Player p = run.Players[0];
                var game = new ForestPlinkoMinigame(p, run.Rng.Niche, ForestPlinkoMinigame.Tier3Drops);
                game.TryDropSeed(1);
                game.StepUntilBallLands();

                // Launch Seed #2 from notch 4 and freeze mid-board so the golden trail & flashing pegs shine!
                game.TryDropSeed(4);
                for (int step = 0; step < 120; step++)
                {
                    game.Advance(1f / 60f);
                    if (game.ActiveBall.Active && game.ActiveBall.Y >= 295f)
                        break;
                }

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = new Color("#040d07") });
                NForestPlinkoScreen screen = NForestPlinkoScreen.Create(game);
                screen.Size = view.Size;
                world.AddChild(screen);
                screen.AfterOverlayOpened();
                screen.AfterOverlayShown();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                TextureRect plinkoBg = screen.FindChild("PlinkoBackdrop", true, false) as TextureRect
                    ?? throw new InvalidOperationException("Missing PlinkoBackdrop");
                Control plinkoPit = screen.FindChild("PlinkoPlayfield", true, false) as Control
                    ?? throw new InvalidOperationException("Missing PlinkoPlayfield");
                Control plinkoRight = screen.FindChild("RightUi", true, false) as Control
                    ?? throw new InvalidOperationException("Missing Plinko RightUi");
                Assert(plinkoBg.Texture != null && plinkoBg.GetGlobalRect().Size == new Vector2(1920f, 1080f),
                    $"NForestPlinkoScreen ({lang}) renders full-bleed 1920x1080 backdrop");
                Rect2 plinkoRect = plinkoPit.GetGlobalRect();
                Assert(plinkoRect.Position.DistanceTo(new Vector2(563f, 255f)) < 1f && MathF.Abs(plinkoRect.Size.X - 566f) < 1f && MathF.Abs(plinkoRect.Size.Y - 566f) < 1f,
                    $"NForestPlinkoScreen ({lang}) pit locked cleanly inside ironwood recess at (563,255,566x566), got {plinkoRect}");
                Assert(plinkoRight.GetGlobalRect().Position.X >= 1240f && plinkoRight.GetGlobalRect().End.X <= 1900f,
                    $"NForestPlinkoScreen ({lang}) RightUi cleanly right of pit");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-overlay-forest_plinko-{lang}.png")) == Error.Ok,
                    $"Save native-overlay-forest_plinko-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 6. Live 1920x1080 screenshot of NBronzeCauldronScreen (2-player co-op: Ironclad open palm & Silent fist grabbing!)
            {
                RunState run = SetupTestRun($"render-overlay-cauldron-{lang}", playerCount: 2);
                Player p1 = run.Players[0];
                Player p2 = run.Players[1];
                // Pick a deterministic seed showcasing BOTH a gold-taped Rare Cardboard Box AND a plain Common Cardboard Box!
                BronzeCauldronMinigame game = null!;
                for (uint s = 0; s < 32; s++)
                {
                    var cand = new BronzeCauldronMinigame(run.Players, p1.NetId, isHostAuthority: true, sharedSeed: 20261120u + s);
                    if (cand.Items.Any(i => i.Kind == CauldronTreasureKind.RareRelic) &&
                        cand.Items.Any(i => i.Kind == CauldronTreasureKind.CommonRelic))
                    {
                        game = cand;
                        break;
                    }
                }
                game ??= new BronzeCauldronMinigame(run.Players, p1.NetId, isHostAuthority: true, sharedSeed: 20261120u);

                // Advance past 3-2-1 countdown into live Scramble!
                for (int step = 0; step < 200; step++)
                    game.Advance(1f / 60f);

                // P2 (Silent) reaches down from top edge and grabs a fistful of upper coins;
                // P1 (Ironclad) reaches up from bottom-right hovering open palm beside the gleaming Rare Cardboard Box!
                CauldronTreasureItem rare = game.Items.First(i => i.Kind == CauldronTreasureKind.RareRelic || i.Kind == CauldronTreasureKind.CommonRelic);
                CauldronTreasureItem topCoin = game.Items
                    .Where(i => !i.IsClaimed && i.Kind == CauldronTreasureKind.Gold && i.Y < 260f)
                    .OrderBy(i => i.Y)
                    .First();
                game.ResolveAuthoritativeGrab(p2.NetId, topCoin.X, topCoin.Y);
                game.UpdateLocalHandPosition(rare.X + 125f, rare.Y + 125f);

                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = new Color("#050912") });
                NBronzeCauldronScreen screen = NBronzeCauldronScreen.Create(game);
                screen.Size = view.Size;
                world.AddChild(screen);
                screen.AfterOverlayOpened();
                screen.AfterOverlayShown();

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                TextureRect bg = screen.FindChild("CauldronBackdrop", true, false) as TextureRect
                    ?? throw new InvalidOperationException("Missing CauldronBackdrop");
                Control pit = screen.FindChild("CauldronPlayfield", true, false) as Control
                    ?? throw new InvalidOperationException("Missing CauldronPlayfield");
                Control handsLayer = screen.FindChild("HandsLayer", true, false) as Control
                    ?? throw new InvalidOperationException("Missing HandsLayer");
                Control right = screen.FindChild("RightUi", true, false) as Control
                    ?? throw new InvalidOperationException("Missing Cauldron RightUi");
                Assert(bg.Texture != null && bg.GetGlobalRect().Size == new Vector2(1920f, 1080f),
                    $"NBronzeCauldronScreen ({lang}) renders full-bleed 1920x1080 backdrop");
                Assert(pit.GetGlobalRect().Position == new Vector2(400f, 180f) && pit.GetGlobalRect().Size == new Vector2(720f, 720f),
                    $"NBronzeCauldronScreen ({lang}) colossal cauldron locked cleanly at (400,180,720x720)");
                Assert(handsLayer.GetGlobalRect().Size == new Vector2(1920f, 1080f),
                    $"NBronzeCauldronScreen ({lang}) off-screen HandsLayer covers full 1920x1080 stage");
                Assert(right.GetGlobalRect().Position.X >= 1340f && right.GetGlobalRect().End.X <= 1900f,
                    $"NBronzeCauldronScreen ({lang}) RightUi cleanly right of colossal cauldron");

                using Image img = view.GetTexture().GetImage();
                Assert(img.SavePng(Path.Combine(output, $"native-overlay-bronze_cauldron-{lang}.png")) == Error.Ok,
                    $"Save native-overlay-bronze_cauldron-{lang}.png");

                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        view.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private sealed class ProbeCardSelector(int pickIndex = -1, Func<CardModel, bool>? targetPredicate = null) : ICardSelector
    {
        public List<CardModel> LastOffered { get; } = [];
        public List<CardModel> LastRewardCards { get; } = [];

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            LastOffered.Clear();
            LastOffered.AddRange(options);
            if (targetPredicate != null)
            {
                CardModel? match = LastOffered.FirstOrDefault(targetPredicate);
                return Task.FromResult<IEnumerable<CardModel>>(match != null ? [match] : []);
            }
            if (pickIndex < 0 || pickIndex >= LastOffered.Count)
                return Task.FromResult<IEnumerable<CardModel>>([]);
            int count = Math.Min(Math.Max(1, minSelect), LastOffered.Count - pickIndex);
            return Task.FromResult<IEnumerable<CardModel>>(LastOffered.Skip(pickIndex).Take(count).ToList());
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            LastRewardCards.Clear();
            LastRewardCards.AddRange(options.Select(o => o.Card));
            return new() { card = options.FirstOrDefault()?.Card };
        }
    }
}
