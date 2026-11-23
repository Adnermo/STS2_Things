using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Enchantments;
using STS2_Things.Events;

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
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsGildedPress).Assembly);

            modHarmony = new Harmony("FiveEventsProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsGildedPress).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyCatalogAndResources();
            VerifyLocalizationInBothLanguages();
            await VerifyGildedPressAsync();
            await VerifyCrookedCounterweightAsync();
            await VerifyAntSortingTableAsync();
            await VerifyGrudgeVendingMachineAsync();
            await VerifyBlindCrabInkwellAsync();

            if (_hasVanillaPck)
                await RenderLiveUiAsync();

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
                        "THINGS_GILDED_PRESS",
                        "THINGS_CROOKED_COUNTERWEIGHT",
                        "THINGS_ANT_SORTING_TABLE",
                        "THINGS_GRUDGE_VENDING_MACHINE",
                        "THINGS_BLIND_CRAB_INKWELL",
                    },
                    enchantment = "THINGS_WEIGHTED",
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.Print($"Five events probe: PASS ({_checks} assertions)");
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
        RunManager.Instance.EventSynchronizer.BeginEvent(canonical, false);
        EventModel? local = RunManager.Instance.EventSynchronizer.GetEventForPlayer(player);
        Assert(local is TEvent, $"BeginEvent created mutable instance for {typeof(TEvent).Name}");
        return (TEvent)local!;
    }

    private static CombatRoom CreateCombatRoom(EncounterModel enc, RunState run)
    {
        var room = new CombatRoom(enc, run);
        run.PushRoom(room);
        foreach (Player pl in run.Players)
        {
            pl.Creature.CombatState?.RemoveCreature(pl.Creature);
            pl.ResetCombatState();
            room.CombatState.AddPlayer(pl);
        }
        enc.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in enc.MonstersWithSlots)
        {
            Creature creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(creature);
            monster.SetUpForCombat();
        }
        room.CombatState.CurrentSide = CombatSide.Player;
        return room;
    }

    private static void DetachPlayers(CombatRoom room, RunState run)
    {
        foreach (Player pl in run.Players)
        {
            if (pl.Creature.CombatState == room.CombatState)
                room.CombatState.RemoveCreature(pl.Creature);
        }
    }

    private void VerifyCatalogAndResources()
    {
        Overgrowth overgrowth = ModelDb.Act<Overgrowth>();
        Underdocks underdocks = ModelDb.Act<Underdocks>();
        Hive hive = ModelDb.Act<Hive>();
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();

        Assert(overgrowth.AllEvents.Contains(ModelDb.Event<ThingsBlindCrabInkwell>()),
            "Overgrowth.AllEvents includes ThingsBlindCrabInkwell");
        Assert(underdocks.AllEvents.Contains(ModelDb.Event<ThingsBlindCrabInkwell>()),
            "Underdocks.AllEvents includes ThingsBlindCrabInkwell");
        Assert(hive.AllEvents.Contains(ModelDb.Event<ThingsAntSortingTable>()),
            "Hive.AllEvents includes ThingsAntSortingTable");
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsGildedPress>()),
            "ThingsDepths.AllEvents includes ThingsGildedPress");
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsCrookedCounterweight>()),
            "ThingsDepths.AllEvents includes ThingsCrookedCounterweight");
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsGrudgeVendingMachine>()),
            "ThingsDepths.AllEvents includes ThingsGrudgeVendingMachine");

        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.EventGildedPressEnabled] = false,
            [ThingsModConfig.EventCrookedCounterweightEnabled] = false,
            [ThingsModConfig.EventAntSortingTableEnabled] = false,
            [ThingsModConfig.EventGrudgeVendingMachineEnabled] = false,
            [ThingsModConfig.EventBlindCrabInkwellEnabled] = false,
        });
        Assert(!overgrowth.AllEvents.Contains(ModelDb.Event<ThingsBlindCrabInkwell>()),
            "Disabling excludes ThingsBlindCrabInkwell from Overgrowth");
        Assert(!underdocks.AllEvents.Contains(ModelDb.Event<ThingsBlindCrabInkwell>()),
            "Disabling excludes ThingsBlindCrabInkwell from Underdocks");
        Assert(!hive.AllEvents.Contains(ModelDb.Event<ThingsAntSortingTable>()),
            "Disabling excludes ThingsAntSortingTable from Hive");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsGildedPress>()),
            "Disabling excludes ThingsGildedPress from Depths");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsCrookedCounterweight>()),
            "Disabling excludes ThingsCrookedCounterweight from Depths");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsGrudgeVendingMachine>()),
            "Disabling excludes ThingsGrudgeVendingMachine from Depths");
        ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(e => e.Key, e => (object?)e.Default));

        if (_hasVanillaPck)
        {
            foreach ((string path, Vector2I size) in new (string, Vector2I)[]
            {
                ("res://images/events/things_gilded_press.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_crooked_counterweight.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_ant_sorting_table.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_grudge_vending_machine.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_blind_crab_inkwell.png", new Vector2I(3440, 1616)),
                ("res://images/enchantments/things_weighted.png", new Vector2I(64, 64)),
            })
            {
                Texture2D? tex = ResourceLoader.Load<Texture2D>(path);
                Assert(tex != null && tex.GetSize() == size, $"Loaded {path} at {size.X}x{size.Y}");
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

            RunState run = SetupTestRun($"loc5-{lang}");
            Player p = run.Players[0];

            var gp = BeginEventForPlayer<ThingsGildedPress>(p, run);
            AssertTextClean(gp.Title.GetFormattedText(), $"GP Title ({lang})");
            AssertTextClean(gp.Description!.GetFormattedText(), $"GP Init ({lang})");
            foreach (string opt in new[] { "SEAL", "SEAL_LOW_HP", "SEAL_NO_CARD", "WAX_MOLD", "WAX_MOLD_NO_CARD", "WAX_MOLD_NO_GOLD", "LEAVE" })
                AssertTextClean(FormatEventLoc(gp, $"THINGS_GILDED_PRESS.pages.INITIAL.options.{opt}.description"), $"GP opt {opt} ({lang})");
            AssertTextClean(FormatEventLoc(gp, "THINGS_GILDED_PRESS.pages.INITIAL.options.LEAVE.title"), $"GP opt LEAVE title ({lang})");
            AssertTextClean(FormatEventLoc(gp, "THINGS_GILDED_PRESS.pages.LEAVE.description"), $"GP page LEAVE ({lang})");

            var cc = BeginEventForPlayer<ThingsCrookedCounterweight>(p, run);
            AssertTextClean(cc.Title.GetFormattedText(), $"CC Title ({lang})");
            AssertTextClean(cc.Description!.GetFormattedText(), $"CC Init ({lang})");
            foreach (string opt in new[] { "WEIGHT", "WEIGHT_NO_CARD", "WATCH", "WATCH_LOW_HP", "WATCH_NO_CARD", "LEAVE" })
                AssertTextClean(FormatEventLoc(cc, $"THINGS_CROOKED_COUNTERWEIGHT.pages.INITIAL.options.{opt}.description"), $"CC opt {opt} ({lang})");
            AssertTextClean(FormatEventLoc(cc, "THINGS_CROOKED_COUNTERWEIGHT.pages.INITIAL.options.LEAVE.title"), $"CC opt LEAVE title ({lang})");
            AssertTextClean(FormatEventLoc(cc, "THINGS_CROOKED_COUNTERWEIGHT.pages.LEAVE.description"), $"CC page LEAVE ({lang})");

            var ast = BeginEventForPlayer<ThingsAntSortingTable>(p, run);
            AssertTextClean(ast.Title.GetFormattedText(), $"AST Title ({lang})");
            AssertTextClean(ast.Description!.GetFormattedText(), $"AST Init ({lang})");
            foreach (string opt in new[] { "CULL", "CULL_NO_PAIRS", "GLUE", "GLUE_NO_CARD", "GLUE_NO_GOLD", "LEAVE" })
                AssertTextClean(FormatEventLoc(ast, $"THINGS_ANT_SORTING_TABLE.pages.INITIAL.options.{opt}.description"), $"AST opt {opt} ({lang})");
            AssertTextClean(FormatEventLoc(ast, "THINGS_ANT_SORTING_TABLE.pages.INITIAL.options.LEAVE.title"), $"AST opt LEAVE title ({lang})");
            AssertTextClean(FormatEventLoc(ast, "THINGS_ANT_SORTING_TABLE.pages.LEAVE.description"), $"AST page LEAVE ({lang})");

            var gvm = BeginEventForPlayer<ThingsGrudgeVendingMachine>(p, run);
            AssertTextClean(gvm.Title.GetFormattedText(), $"GVM Title ({lang})");
            AssertTextClean(gvm.Description!.GetFormattedText(), $"GVM Init ({lang})");
            foreach (string opt in new[] { "INSERT_COIN", "COIN_FULL", "COIN_NO_GOLD", "KICK_ONCE", "LEAVE" })
                AssertTextClean(FormatEventLoc(gvm, $"THINGS_GRUDGE_VENDING_MACHINE.pages.INITIAL.options.{opt}.description"), $"GVM opt {opt} ({lang})");
            AssertTextClean(FormatEventLoc(gvm, "THINGS_GRUDGE_VENDING_MACHINE.pages.INITIAL.options.LEAVE.title"), $"GVM opt LEAVE title ({lang})");
            AssertTextClean(FormatEventLoc(gvm, "THINGS_GRUDGE_VENDING_MACHINE.pages.LEAVE.description"), $"GVM page LEAVE ({lang})");
            AssertTextClean(FormatEventLoc(gvm, "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_1.loss"), $"GVM Kick1 loss ({lang})");
            AssertTextClean(FormatEventLoc(gvm, "THINGS_GRUDGE_VENDING_MACHINE.pages.KICK_2.options.KICK_THRICE.description"), $"GVM Kick3 ({lang})");

            var bci = BeginEventForPlayer<ThingsBlindCrabInkwell>(p, run);
            AssertTextClean(bci.Title.GetFormattedText(), $"BCI Title ({lang})");
            AssertTextClean(bci.Description!.GetFormattedText(), $"BCI Init ({lang})");
            foreach (string opt in new[] { "SOAK", "SOAK_NO_CARD", "REWRITE", "REWRITE_NO_CARD", "LEAVE" })
                AssertTextClean(FormatEventLoc(bci, $"THINGS_BLIND_CRAB_INKWELL.pages.INITIAL.options.{opt}.description"), $"BCI opt {opt} ({lang})");
            AssertTextClean(FormatEventLoc(bci, "THINGS_BLIND_CRAB_INKWELL.pages.INITIAL.options.LEAVE.title"), $"BCI opt LEAVE title ({lang})");
            AssertTextClean(FormatEventLoc(bci, "THINGS_BLIND_CRAB_INKWELL.pages.LEAVE.description"), $"BCI page LEAVE ({lang})");
            AssertTextClean(FormatEventLoc(bci, "THINGS_BLIND_CRAB_INKWELL.pages.REWRITE.loss"), $"BCI Rewrite loss ({lang})");

            EnchantmentModel w = ModelDb.Enchantment<ThingsWeighted>().ToMutable();
            AssertTextClean(w.Title.GetFormattedText(), $"Weighted Title ({lang})");
            AssertTextClean(w.DynamicDescription.GetFormattedText(), $"Weighted Desc ({lang})");
        }
        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
    }

    private async Task VerifyGildedPressAsync()
    {
        RunState run = SetupTestRun("gp-seal");
        Player p = run.Players[0];
        p.Gold = 100;
        CardModel inflame = run.CreateCard<Inflame>(p); // Power -> RoyallyApproved rejects Powers, accepts Clone
        await CardPileCmd.Add(inflame, PileType.Deck);
        CardModel strike = p.Deck.Cards.First(c => c is StrikeIronclad);
        var ev1 = BeginEventForPlayer<ThingsGildedPress>(p, run);
        Assert(ev1.CurrentOptions.Count == 3 && !ev1.CurrentOptions[0].IsLocked && !ev1.CurrentOptions[1].IsLocked,
            "GildedPress unlocks all 3 initial options");

        int maxHpBefore = p.Creature.MaxHp;
        var cancelSel = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelSel))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p.Creature.MaxHp == maxHpBefore, "Canceling SEAL keeps event open");
        Assert(!cancelSel.LastOffered.Contains(inflame) && cancelSel.LastOffered.Contains(strike),
            "RoyallyApproved excludes Power Inflame and includes Attack Strike");

        var pickStrike = new ProbeCardSelector(targetPredicate: c => c == strike);
        using (ProbeRuntimeApi.UseSelector(pickStrike))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && ThingsGildedPress.SealMaxHpLoss == 8
               && p.Creature.MaxHp == maxHpBefore - ThingsGildedPress.SealMaxHpLoss,
            "SEAL deducts 8 Max HP and finishes");
        Assert(strike.Enchantment is RoyallyApproved
               && strike.Keywords.Contains(CardKeyword.Innate)
               && strike.Keywords.Contains(CardKeyword.Retain),
            "SEAL grants RoyallyApproved + Innate + Retain");

        // Wax Mold (Clone enchantment + CloneRestSiteOption actually duplicates at rest site)
        RunState run2 = SetupTestRun("gp-clone");
        Player p2 = run2.Players[0];
        p2.Gold = 100;
        CardModel bash = p2.Deck.Cards.First(c => c is Bash);
        int bashCountBefore = p2.Deck.Cards.Count(c => c.Id == bash.Id);
        var ev2 = BeginEventForPlayer<ThingsGildedPress>(p2, run2);
        var pickBash = new ProbeCardSelector(targetPredicate: c => c == bash);
        using (ProbeRuntimeApi.UseSelector(pickBash))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && ThingsGildedPress.MoldGoldCost == 75
               && p2.Gold == 100 - ThingsGildedPress.MoldGoldCost && bash.Enchantment is Clone,
            "Wax Mold deducts 75 Gold and grants Clone");

        var cloneOption = new CloneRestSiteOption(p2);
        bool cloneOk = await cloneOption.OnSelect();
        Assert(cloneOk && p2.Deck.Cards.Count(c => c.Id == bash.Id) == bashCountBefore + 1,
            "Clone enchantment allows duplicating the cloned card at RestSite");

        // Leave option (pure leave, 0 gold gained)
        RunState run3 = SetupTestRun("gp-leave");
        Player p3 = run3.Players[0];
        int gBefore = p3.Gold;
        var ev3 = BeginEventForPlayer<ThingsGildedPress>(p3, run3);
        Assert(!ev3.CurrentOptions[2].IsLocked && ev3.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "GildedPress option 3 is pure LEAVE");
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && p3.Gold == gBefore,
            "Leave finishes cleanly with 0 gold gained");

        // Low gold & low max HP locks
        RunState runLocked = SetupTestRun("gp-locks");
        Player pLocked = runLocked.Players[0];
        pLocked.Creature.SetMaxHpInternal(ThingsGildedPress.SealMaxHpLoss);
        pLocked.Gold = ThingsGildedPress.MoldGoldCost - 1;
        var evLocked = BeginEventForPlayer<ThingsGildedPress>(pLocked, runLocked);
        Assert(evLocked.CurrentOptions[0].IsLocked && evLocked.CurrentOptions[1].IsLocked
               && !evLocked.CurrentOptions[2].IsLocked && evLocked.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "Low Max HP + low Gold locks Options 0 & 1 while leaving LEAVE open");
    }

    private async Task VerifyCrookedCounterweightAsync()
    {
        RunState run = SetupTestRun("cc-weight");
        Player p = run.Players[0];
        CardModel strike = p.Deck.Cards.First(c => c is StrikeIronclad);
        CardModel defend = p.Deck.Cards.First(c => c is DefendIronclad);
        CardModel utility = run.CreateCard<BattleTrance>(p); // Skill with no Damage or Block -> excluded from Weighted
        CardModel whirlwind = run.CreateCard<Whirlwind>(p);  // X-cost -> excluded
        await CardPileCmd.Add([utility, whirlwind], PileType.Deck);

        var ev1 = BeginEventForPlayer<ThingsCrookedCounterweight>(p, run);
        var cancelSel = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelSel))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && !cancelSel.LastOffered.Contains(utility) && !cancelSel.LastOffered.Contains(whirlwind)
               && cancelSel.LastOffered.Contains(strike) && cancelSel.LastOffered.Contains(defend),
            "Weighted filters out non-dmg/block Skills and X-cost cards while keeping Attacks & Defends");

        // Apply Weighted to Strike
        int hpBefore = p.Creature.CurrentHp;
        int strikeCostBefore = strike.EnergyCost.GetWithModifiers(CostModifiers.None);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == strike)))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && ThingsCrookedCounterweight.WeightDamage == 11
               && p.Creature.CurrentHp == hpBefore - ThingsCrookedCounterweight.WeightDamage,
            "Weight deals 11 HP damage and finishes");
        Assert(strike.Enchantment is ThingsWeighted
               && strike.EnergyCost.GetWithModifiers(CostModifiers.None) == strikeCostBefore + 1,
            "ThingsWeighted permanently increases cost by +1");

        // Also apply Weighted to Defend in another event to test BOTH damage & block 2x in real combat!
        var evDef = BeginEventForPlayer<ThingsCrookedCounterweight>(p, run);
        int defCostBefore = defend.EnergyCost.GetWithModifiers(CostModifiers.None);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == defend)))
            await evDef.CurrentOptions[0].Chosen();
        Assert(defend.Enchantment is ThingsWeighted
               && defend.EnergyCost.GetWithModifiers(CostModifiers.None) == defCostBefore + 1,
            "ThingsWeighted applies cleanly to Defend as well");

        // Serialization round-trip preserves Weighted cost + enchantment
        CardModel reloadedStrike = CardModel.FromSerializable(strike.ToSerializable());
        Assert(reloadedStrike.Enchantment is ThingsWeighted && reloadedStrike.EnergyCost.GetWithModifiers(CostModifiers.None) == strikeCostBefore + 1,
            "Weighted survives ToSerializable/FromSerializable");

        // Combat check: Hook damage multiplier = 2x and Hook block multiplier = 2x
        EncounterModel enc = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom room = CreateCombatRoom(enc, run);
        p.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        ActivateSyntheticCombat(room.CombatState);
        try
        {
            CardModel combatStrike = PileType.Draw.GetPile(p).Cards.First(c => c.Id == strike.Id && c.Enchantment is ThingsWeighted);
            CardModel combatDefend = PileType.Draw.GetPile(p).Cards.First(c => c.Id == defend.Id && c.Enchantment is ThingsWeighted);
            Creature target = room.CombatState.Enemies.First();
            decimal modifiedDmg = Hook.ModifyDamage(run, room.CombatState, target, p.Creature, 6m, ValueProp.Move, combatStrike, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
            decimal modifiedBlk = Hook.ModifyBlock(room.CombatState, p.Creature, 5m, ValueProp.Move, combatDefend, null, out _);
            Assert(modifiedDmg == 12m, $"Weighted Attack deals double damage (expected 12, got {modifiedDmg})");
            Assert(modifiedBlk == 10m, $"Weighted Block card grants double block (expected 10, got {modifiedBlk})");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(room, run);
        }

        // Option 2: Watch (SlumberingEssence)
        RunState run2 = SetupTestRun("cc-watch");
        Player p2 = run2.Players[0];
        CardModel bludgeon = run2.CreateCard<Bludgeon>(p2);
        await CardPileCmd.Add(bludgeon, PileType.Deck);
        int maxHp2 = p2.Creature.MaxHp;
        var ev2 = BeginEventForPlayer<ThingsCrookedCounterweight>(p2, run2);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == bludgeon)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && ThingsCrookedCounterweight.WatchMaxHpLoss == 7
               && p2.Creature.MaxHp == maxHp2 - ThingsCrookedCounterweight.WatchMaxHpLoss
               && bludgeon.Enchantment is SlumberingEssence,
            "Watch deducts 7 Max HP and grants SlumberingEssence");

        // Option 3: Leave (pure leave, 0 upgrades and 0 healing)
        RunState run3 = SetupTestRun("cc-leave");
        Player p3 = run3.Players[0];
        p3.Creature.SetCurrentHpInternal(40);
        int hp3 = p3.Creature.CurrentHp;
        int upgradedBefore = p3.Deck.Cards.Count(c => c.IsUpgraded);
        var ev3 = BeginEventForPlayer<ThingsCrookedCounterweight>(p3, run3);
        Assert(!ev3.CurrentOptions[2].IsLocked && ev3.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "CrookedCounterweight option 3 is pure LEAVE");
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && p3.Deck.Cards.Count(c => c.IsUpgraded) == upgradedBefore
               && p3.Creature.CurrentHp == hp3,
            "Leave finishes cleanly with 0 upgrades and 0 healing");
    }

    private async Task VerifyAntSortingTableAsync()
    {
        // Case 1: Cull Duplicate Pair deals 18 HP damage & removes BOTH matching copies
        RunState run1 = SetupTestRun("ast-cull");
        Player p1 = run1.Players[0];
        CardModel strike1 = p1.Deck.Cards.First(c => c is StrikeIronclad);
        CardModel strike2 = p1.Deck.Cards.Skip(1).First(c => c is StrikeIronclad);
        int strikeCountBefore = p1.Deck.Cards.Count(c => c is StrikeIronclad);
        int deckBefore = p1.Deck.Cards.Count;
        int hpBefore = p1.Creature.CurrentHp;

        var ev1 = BeginEventForPlayer<ThingsAntSortingTable>(p1, run1);
        Assert(!ev1.CurrentOptions[0].IsLocked, "Cull unlocked with duplicate Strikes");

        // Cancel on 2nd pick preserves HP and cards
        using (ProbeRuntimeApi.UseSelector(new SequentialCardSelector([strike1, null])))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p1.Creature.CurrentHp == hpBefore && p1.Deck.Cards.Count == deckBefore,
            "Canceling 2nd duplicate card aborts cleanly without damage or removal");

        using (ProbeRuntimeApi.UseSelector(new SequentialCardSelector([strike1, strike2])))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && p1.Creature.CurrentHp == hpBefore - ThingsAntSortingTable.CullDamage
               && ThingsAntSortingTable.CullDamage == 18,
            "Cull deals exactly 18 HP damage");
        Assert(p1.Deck.Cards.Count == deckBefore - 2
               && p1.Deck.Cards.Count(c => c is StrikeIronclad) == strikeCountBefore - 2,
            "Cull removes 2 matching Strike copies simultaneously");

        // Lethal 18 damage check on 18 HP player
        RunState runLethal = SetupTestRun("ast-cull-lethal");
        Player pLethal = runLethal.Players[0];
        pLethal.Creature.SetCurrentHpInternal(18);
        CardModel ls1 = pLethal.Deck.Cards.First(c => c is StrikeIronclad);
        CardModel ls2 = pLethal.Deck.Cards.Skip(1).First(c => c is StrikeIronclad);
        var evLethal = BeginEventForPlayer<ThingsAntSortingTable>(pLethal, runLethal);
        using (ProbeRuntimeApi.UseSelector(new SequentialCardSelector([ls1, ls2])))
            await evLethal.CurrentOptions[0].Chosen();
        Assert(evLethal.IsFinished && pLethal.Creature.IsDead,
            "18 HP damage kills 18 HP player cleanly on Cull");

        // Lock when no duplicate removable cards remain + Glue (Goopy) + pure LEAVE
        RunState run2 = SetupTestRun("ast-glue-leave");
        Player p2 = run2.Players[0];
        p2.Gold = 100;
        HashSet<ModelId> seen = [];
        foreach (CardModel c in p2.Deck.Cards.ToList())
        {
            if (!seen.Add(c.Id) && c.IsRemovable)
                await CardPileCmd.RemoveFromDeck(c);
        }
        CardModel defend = p2.Deck.Cards.First(c => c is DefendIronclad);
        var evNoPairs = BeginEventForPlayer<ThingsAntSortingTable>(p2, run2);
        Assert(evNoPairs.CurrentOptions[0].IsLocked && evNoPairs.CurrentOptions[0].Title.LocEntryKey.Contains("CULL_NO_PAIRS"),
            "Unique deck locks CULL with CULL_NO_PAIRS");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == defend)))
            await evNoPairs.CurrentOptions[1].Chosen();
        Assert(evNoPairs.IsFinished && ThingsAntSortingTable.GlueGoldCost == 60
               && p2.Gold == 100 - ThingsAntSortingTable.GlueGoldCost
               && defend.Enchantment is Goopy && defend.Keywords.Contains(CardKeyword.Exhaust),
            "Glue deducts 60 Gold and grants Goopy + Exhaust to Defend");

        int maxHp2Before = p2.Creature.MaxHp;
        var evLeave = BeginEventForPlayer<ThingsAntSortingTable>(p2, run2);
        Assert(!evLeave.CurrentOptions[2].IsLocked && evLeave.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "AntSortingTable option 3 is pure LEAVE");
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && p2.Creature.MaxHp == maxHp2Before,
            "Leave finishes cleanly with 0 Max HP gained");
    }

    private async Task VerifyGrudgeVendingMachineAsync()
    {
        // Case 1: Buy Potion via TRAY sub-page & pure LEAVE
        RunState run1 = SetupTestRun("gvm-buy");
        Player p1 = run1.Players[0];
        p1.Gold = 100;
        var ev1 = BeginEventForPlayer<ThingsGrudgeVendingMachine>(p1, run1);
        Assert(!ev1.CurrentOptions[0].IsLocked, "Insert Coin unlocked when gold >= 55 and open potion slots");
        await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && ev1.CurrentOptions.Count >= 2,
            "Insert Coin opens potion tray sub-page with potion choices + BACK");
        // Cancel via BACK keeps 100 Gold
        await ev1.CurrentOptions[^1].Chosen();
        Assert(!ev1.IsFinished && p1.Gold == 100, "Tray BACK returns to INITIAL without spending Gold");

        await ev1.CurrentOptions[0].Chosen();
        await ev1.CurrentOptions[0].Chosen(); // Pick 1st offered potion
        Assert(ev1.IsFinished && ThingsGrudgeVendingMachine.CoinCost == 55
               && p1.Gold == 100 - ThingsGrudgeVendingMachine.CoinCost
               && p1.Potions.Any(),
            "Buying potion deducts 55 Gold and adds 1 chosen Potion");

        // Full potion belt locks INSERT_COIN with COIN_FULL, while LEAVE finishes with 0 gold gained
        while (p1.HasOpenPotionSlots)
            await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(), p1);
        var evFull = BeginEventForPlayer<ThingsGrudgeVendingMachine>(p1, run1);
        Assert(evFull.CurrentOptions[0].IsLocked && evFull.CurrentOptions[0].Title.LocEntryKey.Contains("COIN_FULL"),
            "Full potion belt locks Option 0 with COIN_FULL");
        Assert(!evFull.CurrentOptions[2].IsLocked && evFull.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "GrudgeVendingMachine option 3 is pure LEAVE");
        int gBeforeLeave = p1.Gold;
        await evFull.CurrentOptions[2].Chosen();
        Assert(evFull.IsFinished && p1.Gold == gBeforeLeave,
            "Leave finishes cleanly with 0 gold gained");

        // Case 2: 3-Kick progression (-6 HP + 35 Gold -> -12 HP + Common Relic -> -18 HP + Rare Relic + 1 Shame curse)
        RunState runKick = SetupTestRun("gvm-kick3");
        Player pK = runKick.Players[0];
        int gKick0 = pK.Gold;
        int relic0 = pK.Relics.Count;
        int hp0 = pK.Creature.CurrentHp;
        var evKick = BeginEventForPlayer<ThingsGrudgeVendingMachine>(pK, runKick);
        await evKick.CurrentOptions[1].Chosen(); // KICK_ONCE
        Assert(!evKick.IsFinished
               && ThingsGrudgeVendingMachine.Kick1Damage == 6
               && ThingsGrudgeVendingMachine.Kick1Gold == 35
               && pK.Creature.CurrentHp == hp0 - ThingsGrudgeVendingMachine.Kick1Damage
               && pK.Gold == gKick0 + ThingsGrudgeVendingMachine.Kick1Gold
               && evKick.CurrentOptions.Count == 2,
            "Kick 1 deals 6 HP damage, grants 35 Gold, and reveals KICK_TWICE / STOP");

        int maxHpBeforeKick2 = pK.Creature.MaxHp;
        await evKick.CurrentOptions[0].Chosen(); // KICK_TWICE
        int maxHpGainKick2 = Math.Max(0, pK.Creature.MaxHp - maxHpBeforeKick2);
        Assert(!evKick.IsFinished
               && ThingsGrudgeVendingMachine.Kick2Damage == 12
               && pK.Creature.CurrentHp == hp0 - ThingsGrudgeVendingMachine.Kick1Damage - ThingsGrudgeVendingMachine.Kick2Damage + maxHpGainKick2
               && pK.Relics.Count == relic0 + 1,
            "Kick 2 deals 12 HP damage, grants Common relic, and opens KICK_THRICE / STOP");
        Assert(pK.Relics[^1].Rarity == RelicRarity.Common, "Kick 2 relic has Common rarity");

        int hpAfterKick2 = pK.Creature.CurrentHp;
        int maxHpBeforeKick3 = pK.Creature.MaxHp;
        await evKick.CurrentOptions[0].Chosen(); // KICK_THRICE
        int maxHpGainKick3 = Math.Max(0, pK.Creature.MaxHp - maxHpBeforeKick3);
        Assert(evKick.IsFinished
               && ThingsGrudgeVendingMachine.Kick3Damage == 18
               && pK.Creature.CurrentHp == hpAfterKick2 - ThingsGrudgeVendingMachine.Kick3Damage + maxHpGainKick3,
            "Kick 3 deals 18 HP damage and finishes event");
        Assert(pK.Relics.Count == relic0 + 2 && pK.Relics[^1].Rarity == RelicRarity.Rare,
            "Kick 3 grants Rare relic");
        Assert(pK.Deck.Cards.Count(c => c is Shame) == 1,
            "Kick 3 adds exactly 1 Shame curse to Deck");

        // Early stop on Kick 1 & lethal Kick 1 / Kick 3 checks
        RunState runStop = SetupTestRun("gvm-kick-stop");
        Player pStop = runStop.Players[0];
        var evStop = BeginEventForPlayer<ThingsGrudgeVendingMachine>(pStop, runStop);
        await evStop.CurrentOptions[1].Chosen();
        await evStop.CurrentOptions[1].Chosen(); // STOP
        Assert(evStop.IsFinished, "STOP after Kick 1 finishes cleanly");

        RunState runDead1 = SetupTestRun("gvm-kick1-lethal");
        Player pDead1 = runDead1.Players[0];
        pDead1.Creature.SetCurrentHpInternal(ThingsGrudgeVendingMachine.Kick1Damage);
        int goldBeforeDeath1 = pDead1.Gold;
        var evDead1 = BeginEventForPlayer<ThingsGrudgeVendingMachine>(pDead1, runDead1);
        await evDead1.CurrentOptions[1].Chosen();
        Assert(pDead1.Creature.IsDead && evDead1.IsFinished && pDead1.Gold == goldBeforeDeath1,
            "Lethal Kick 1 kills player without granting Kick1Gold post-mortem");

        RunState runDead = SetupTestRun("gvm-kick3-lethal");
        Player pDead = runDead.Players[0];
        var evDead = BeginEventForPlayer<ThingsGrudgeVendingMachine>(pDead, runDead);
        await evDead.CurrentOptions[1].Chosen();
        await evDead.CurrentOptions[0].Chosen();
        pDead.Creature.SetCurrentHpInternal(ThingsGrudgeVendingMachine.Kick3Damage);
        int relicsBeforeDeath = pDead.Relics.Count;
        await evDead.CurrentOptions[0].Chosen();
        Assert(pDead.Creature.IsDead && evDead.IsFinished && pDead.Relics.Count == relicsBeforeDeath && !pDead.Deck.Cards.Any(c => c is Shame),
            "Lethal Kick 3 kills player without adding Rare relic or Shame post-mortem");
    }

    private async Task VerifyBlindCrabInkwellAsync()
    {
        // Case 1: Soak Attack / enemy-targeting Skill with Inky (-10 HP) + verify target filtering + verify combat Weak application!
        RunState run1 = SetupTestRun("bci-inky");
        Player p1 = run1.Players[0];
        CardModel strike = p1.Deck.Cards.First(c => c is StrikeIronclad);
        CardModel randomAttack = run1.CreateCard<SwordBoomerang>(p1); // TargetType.RandomEnemy -> must be excluded!
        CardModel taunt = run1.CreateCard<Taunt>(p1); // TargetType.AnyEnemy Skill -> must be included!
        CardModel shockwave = run1.CreateCard<Shockwave>(p1); // TargetType.AllEnemies Skill -> must be included!
        CardModel defend = p1.Deck.Cards.First(c => c is DefendIronclad);
        await CardPileCmd.Add(randomAttack, PileType.Deck);
        await CardPileCmd.Add(taunt, PileType.Deck);
        await CardPileCmd.Add(shockwave, PileType.Deck);

        var ev1 = BeginEventForPlayer<ThingsBlindCrabInkwell>(p1, run1);
        var cancelSel = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelSel))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && !cancelSel.LastOffered.Contains(defend) && !cancelSel.LastOffered.Contains(randomAttack)
               && cancelSel.LastOffered.Contains(strike) && cancelSel.LastOffered.Contains(taunt)
               && cancelSel.LastOffered.Contains(shockwave),
            "Inky excludes Defend (Self) and SwordBoomerang (RandomEnemy) while including Strike, Taunt (AnyEnemy Skill), and Shockwave (AllEnemies Skill)");

        int hpBefore = p1.Creature.CurrentHp;
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == strike)))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && ThingsBlindCrabInkwell.PinchDamage == 10
               && p1.Creature.CurrentHp == hpBefore - ThingsBlindCrabInkwell.PinchDamage
               && strike.Enchantment is Inky,
            "Soak deals 10 HP damage and grants Inky to Strike");

        // Verify Inky applies WeakPower in combat cleanly
        EncounterModel enc = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom room = CreateCombatRoom(enc, run1);
        p1.PopulateCombatState(run1.Rng.Shuffle, room.CombatState);
        ActivateSyntheticCombat(room.CombatState);
        try
        {
            CardModel combatStrike = PileType.Draw.GetPile(p1).Cards.First(c => c.Enchantment is Inky);
            Creature enemy = room.CombatState.Enemies.First();
            CardPlay fakePlay = new()
            {
                Card = combatStrike,
                Player = p1,
                Target = enemy,
                ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = false,
                PlayIndex = 0,
                PlayCount = 1,
            };
            await combatStrike.Enchantment!.OnPlay(new BlockingPlayerChoiceContext(), fakePlay);
            Assert(enemy.GetPowerAmount<WeakPower>() == ThingsBlindCrabInkwell.InkyWeakAmount,
                "Inky-enchanted Strike applies 1 Weak in combat");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(room, run1);
        }

        // Case 1b: Soak AnyEnemy Skill (Taunt) with Inky cleanly + verify combat Weak application
        RunState run1Skill = SetupTestRun("bci-inky-skill");
        Player p1Skill = run1Skill.Players[0];
        CardModel tauntSkill = run1Skill.CreateCard<Taunt>(p1Skill);
        CardModel randomAttackSkill = run1Skill.CreateCard<SwordBoomerang>(p1Skill);
        CardModel defendSkill = p1Skill.Deck.Cards.First(c => c is DefendIronclad);
        await CardPileCmd.Add(tauntSkill, PileType.Deck);
        await CardPileCmd.Add(randomAttackSkill, PileType.Deck);

        var ev1Skill = BeginEventForPlayer<ThingsBlindCrabInkwell>(p1Skill, run1Skill);
        int hpSkillBefore = p1Skill.Creature.CurrentHp;
        var skillSel = new ProbeCardSelector(targetPredicate: c => c == tauntSkill);
        using (ProbeRuntimeApi.UseSelector(skillSel))
            await ev1Skill.CurrentOptions[0].Chosen();
        Assert(ev1Skill.IsFinished && p1Skill.Creature.CurrentHp == hpSkillBefore - ThingsBlindCrabInkwell.PinchDamage
               && tauntSkill.Enchantment is Inky
               && skillSel.LastOffered.Contains(tauntSkill)
               && !skillSel.LastOffered.Contains(defendSkill)
               && !skillSel.LastOffered.Contains(randomAttackSkill),
            "Soak enchants AnyEnemy Skill (Taunt) with Inky while excluding Defend and SwordBoomerang");

        EncounterModel encSkill = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom roomSkill = CreateCombatRoom(encSkill, run1Skill);
        p1Skill.PopulateCombatState(run1Skill.Rng.Shuffle, roomSkill.CombatState);
        ActivateSyntheticCombat(roomSkill.CombatState);
        try
        {
            CardModel combatTaunt = PileType.Draw.GetPile(p1Skill).Cards.First(c => c is Taunt && c.Enchantment is Inky);
            Creature enemy = roomSkill.CombatState.Enemies.First();
            CardPlay fakePlay = new()
            {
                Card = combatTaunt,
                Player = p1Skill,
                Target = enemy,
                ResultPile = PileType.Discard,
                Resources = new ResourceInfo { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 },
                IsAutoPlay = false,
                PlayIndex = 0,
                PlayCount = 1,
            };
            await combatTaunt.Enchantment!.OnPlay(new BlockingPlayerChoiceContext(), fakePlay);
            Assert(enemy.GetPowerAmount<WeakPower>() == ThingsBlindCrabInkwell.InkyWeakAmount,
                "Inky-enchanted Taunt applies 1 Weak in combat");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(roomSkill, run1Skill);
        }

        // Case 2: Rewrite 1 non-Curse card -> 1 Colorless card (-7 HP; cancel preserves HP)
        RunState run2 = SetupTestRun("bci-rewrite");
        Player p2 = run2.Players[0];
        CardModel injury = run2.CreateCard<Injury>(p2);
        await CardPileCmd.Add(injury, PileType.Deck);
        CardModel strike2 = p2.Deck.Cards.First(c => c is StrikeIronclad);
        int deckCount2 = p2.Deck.Cards.Count;
        int hp2Before = p2.Creature.CurrentHp;

        var ev2 = BeginEventForPlayer<ThingsBlindCrabInkwell>(p2, run2);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(!ev2.IsFinished && p2.Creature.CurrentHp == hp2Before && p2.Deck.Cards.Contains(strike2),
            "Canceling Rewrite keeps event open and preserves HP");

        var rewriteSelector = new ProbeCardSelector(targetPredicate: c => c == strike2);
        using (ProbeRuntimeApi.UseSelector(rewriteSelector))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && ThingsBlindCrabInkwell.RewriteDamage == 7
               && p2.Creature.CurrentHp == hp2Before - ThingsBlindCrabInkwell.RewriteDamage
               && p2.Deck.Cards.Count == deckCount2,
            "Rewrite deals 7 HP damage and transforms 1 card without changing total deck size");
        Assert(!rewriteSelector.LastOffered.Contains(injury),
            "Rewrite excludes Curse cards");
        Assert(!p2.Deck.Cards.Contains(strike2) && p2.Deck.Cards.Any(c => c.Pool is ColorlessCardPool),
            "Rewrite transforms chosen Strike into 1 random Colorless card");

        RunState run2Lethal = SetupTestRun("bci-rewrite-lethal");
        Player p2Lethal = run2Lethal.Players[0];
        p2Lethal.Creature.SetCurrentHpInternal(ThingsBlindCrabInkwell.RewriteDamage);
        CardModel strikeLethal = p2Lethal.Deck.Cards.First(c => c is StrikeIronclad);
        var ev2Lethal = BeginEventForPlayer<ThingsBlindCrabInkwell>(p2Lethal, run2Lethal);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == strikeLethal)))
            await ev2Lethal.CurrentOptions[1].Chosen();
        Assert(p2Lethal.Creature.IsDead && ev2Lethal.IsFinished && p2Lethal.Deck.Cards.Contains(strikeLethal),
            "Lethal Rewrite kills player without transforming card post-mortem");

        // Case 3: Leave (pure leave, 0 healing)
        RunState run3 = SetupTestRun("bci-leave");
        Player p3 = run3.Players[0];
        p3.Creature.SetCurrentHpInternal(40);
        var ev3 = BeginEventForPlayer<ThingsBlindCrabInkwell>(p3, run3);
        Assert(!ev3.CurrentOptions[2].IsLocked && ev3.CurrentOptions[2].TextKey.Contains("LEAVE", StringComparison.Ordinal),
            "BlindCrabInkwell option 3 is pure LEAVE");
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && p3.Creature.CurrentHp == 40,
            "Leave finishes cleanly with 0 healing");
    }

    private async Task RenderLiveUiAsync()
    {
        string? output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);

        var eventView = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(eventView);
        GetTree().Root.Size = eventView.Size;

        (string Key, Func<Player, RunState, EventModel> Begin)[] items =
        [
            ("gilded_press", (p, r) => BeginEventForPlayer<ThingsGildedPress>(p, r)),
            ("crooked_counterweight", (p, r) => BeginEventForPlayer<ThingsCrookedCounterweight>(p, r)),
            ("ant_sorting_table", (p, r) => BeginEventForPlayer<ThingsAntSortingTable>(p, r)),
            ("grudge_vending_machine", (p, r) => BeginEventForPlayer<ThingsGrudgeVendingMachine>(p, r)),
            ("blind_crab_inkwell", (p, r) => BeginEventForPlayer<ThingsBlindCrabInkwell>(p, r)),
        ];

        foreach (string lang in new[] { "zhs", "eng" })
        foreach (var (key, begin) in items)
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            RunState run = SetupTestRun($"render5-{key}-{lang}");
            Player p = run.Players[0];
            p.Gold = 100;
            EventModel model = begin(p, run);

            var world = new Control { Size = eventView.Size };
            eventView.AddChild(world);
            world.AddChild(new ColorRect { Size = eventView.Size, Color = Colors.Black });
            NEventLayout layout = GD.Load<PackedScene>(NEventLayout.defaultScenePath).Instantiate<NEventLayout>();
            world.AddChild(layout);
            layout.SetEvent(model);
            layout.SetTitle(model.Title.GetFormattedText());
            layout.SetDescription(model.Description!.GetFormattedText());
            layout.AddOptions(model.CurrentOptions);
            layout.OnSetupComplete();

            await ToSignal(GetTree().CreateTimer(2.4), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var opts = layout.OptionButtons.ToList();
            Assert(opts.Count == 3, $"Native layout renders 3 options for {key} ({lang})");
            var lastBtn = opts[^1];
            float bottomY = lastBtn.GlobalPosition.Y + lastBtn.Size.Y;
            Assert(bottomY <= 1048f, $"Layout {key} ({lang}) bottomY={bottomY:F1} fits in 1080p safe zone");

            using Image img = eventView.GetTexture().GetImage();
            string pngName = $"native-event-{key}-{lang}.png";
            Assert(img.SavePng(Path.Combine(output, pngName)) == Error.Ok, $"Save {pngName}");

            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        eventView.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private sealed class ProbeCardSelector(int pickIndex = -1, Func<CardModel, bool>? targetPredicate = null) : ICardSelector
    {
        public List<CardModel> LastOffered { get; } = [];
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
            return Task.FromResult<IEnumerable<CardModel>>([LastOffered[pickIndex]]);
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            new() { card = options.FirstOrDefault()?.Card };
    }

    private sealed class SequentialCardSelector(IReadOnlyList<CardModel?> picks) : ICardSelector
    {
        private int _index;
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            CardModel? wanted = _index < picks.Count ? picks[_index++] : null;
            List<CardModel> list = options.ToList();
            if (wanted == null || !list.Contains(wanted))
                return Task.FromResult<IEnumerable<CardModel>>([]);
            return Task.FromResult<IEnumerable<CardModel>>([wanted]);
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            new() { card = options.FirstOrDefault()?.Card };
    }
}
