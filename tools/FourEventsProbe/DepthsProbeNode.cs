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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Config;
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
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsChronometerRig).Assembly);

            modHarmony = new Harmony("FourEventsProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsChronometerRig).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyCatalogAndResources();
            VerifyLocalizationInBothLanguages();
            await VerifyChronometerRigAsync();
            await VerifySilveredGalleryAsync();
            await VerifyCrackedBrazierAsync();
            await VerifySiltWinchAsync();

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
                        "THINGS_CHRONOMETER_RIG",
                        "THINGS_SILVERED_GALLERY",
                        "THINGS_CRACKED_BRAZIER",
                        "THINGS_SILT_WINCH",
                    },
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.Print($"Four events probe: PASS ({_checks} assertions)");
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

    private void VerifyCatalogAndResources()
    {
        Glory glory = ModelDb.Act<Glory>();
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();

        Assert(glory.AllEvents.Contains(ModelDb.Event<ThingsChronometerRig>()),
            "Glory.AllEvents includes ThingsChronometerRig");
        Assert(glory.AllEvents.Contains(ModelDb.Event<ThingsSilveredGallery>()),
            "Glory.AllEvents includes ThingsSilveredGallery");
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsCrackedBrazier>()),
            "ThingsDepths.AllEvents includes ThingsCrackedBrazier");
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsSiltWinch>()),
            "ThingsDepths.AllEvents includes ThingsSiltWinch");

        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.EventChronometerRigEnabled] = false,
            [ThingsModConfig.EventSilveredGalleryEnabled] = false,
            [ThingsModConfig.EventCrackedBrazierEnabled] = false,
            [ThingsModConfig.EventSiltWinchEnabled] = false,
        });
        Assert(!glory.AllEvents.Contains(ModelDb.Event<ThingsChronometerRig>()),
            "Disabling excludes ThingsChronometerRig from Glory");
        Assert(!glory.AllEvents.Contains(ModelDb.Event<ThingsSilveredGallery>()),
            "Disabling excludes ThingsSilveredGallery from Glory");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsCrackedBrazier>()),
            "Disabling excludes ThingsCrackedBrazier from Depths");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsSiltWinch>()),
            "Disabling excludes ThingsSiltWinch from Depths");
        ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(e => e.Key, e => (object?)e.Default));

        if (_hasVanillaPck)
        {
            foreach ((string path, Vector2I size) in new (string, Vector2I)[]
            {
                ("res://images/events/things_chronometer_rig.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_silvered_gallery.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_cracked_brazier.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_silt_winch.png", new Vector2I(3440, 1616)),
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

            RunState run = SetupTestRun($"loc4-{lang}");
            Player p = run.Players[0];

            var cr = BeginEventForPlayer<ThingsChronometerRig>(p, run);
            AssertTextClean(cr.Title.GetFormattedText(), $"CR Title ({lang})");
            AssertTextClean(cr.Description!.GetFormattedText(), $"CR Init ({lang})");
            foreach (string opt in new[] { "GEAR", "GEAR_NO_CARD", "GEAR_NO_GOLD", "PIN", "PIN_LOW_HP", "PIN_NO_CARD", "OIL_POTION", "OIL_GOLD" })
                AssertTextClean(FormatEventLoc(cr, $"THINGS_CHRONOMETER_RIG.pages.INITIAL.options.{opt}.description"), $"CR opt {opt} ({lang})");
            foreach (string page in new[] { "GEAR.description", "PIN.description", "PIN.loss", "OIL_POTION.description", "OIL_GOLD.description", "ABORTED.description" })
                AssertTextClean(FormatEventLoc(cr, $"THINGS_CHRONOMETER_RIG.pages.{page}"), $"CR page {page} ({lang})");

            var sg = BeginEventForPlayer<ThingsSilveredGallery>(p, run);
            AssertTextClean(sg.Title.GetFormattedText(), $"SG Title ({lang})");
            AssertTextClean(sg.Description!.GetFormattedText(), $"SG Init ({lang})");
            foreach (string opt in new[] { "DUPLICATE_RARE", "DUPLICATE_ANY", "DUPLICATE_LOCKED", "PAWN", "PAWN_NO_RELIC", "PAWN_NO_CARD", "PAWN_NO_GOLD", "POLISH_UPGRADE", "POLISH_HEAL" })
                AssertTextClean(FormatEventLoc(sg, $"THINGS_SILVERED_GALLERY.pages.INITIAL.options.{opt}.description"), $"SG opt {opt} ({lang})");
            foreach (string page in new[] { "DUPLICATE.description", "DUPLICATE.loss", "PAWN.description", "POLISH_UPGRADE.description", "POLISH_HEAL.description", "ABORTED.description" })
                AssertTextClean(FormatEventLoc(sg, $"THINGS_SILVERED_GALLERY.pages.{page}"), $"SG page {page} ({lang})");

            var cb = BeginEventForPlayer<ThingsCrackedBrazier>(p, run);
            AssertTextClean(cb.Title.GetFormattedText(), $"CB Title ({lang})");
            AssertTextClean(cb.Description!.GetFormattedText(), $"CB Init ({lang})");
            foreach (string opt in new[] { "TEMPER", "TEMPER_LOW_HP", "TEMPER_NO_CARD", "BURN_CARD", "BURN_NO_CARD", "WARM_HANDS" })
                AssertTextClean(FormatEventLoc(cb, $"THINGS_CRACKED_BRAZIER.pages.INITIAL.options.{opt}.description"), $"CB opt {opt} ({lang})");
            foreach (string page in new[] { "TEMPER.description", "TEMPER.loss", "BURN_CARD.description", "BURN_CARD.loss", "BURN_RARE.description", "WARM_HANDS.description", "ABORTED.description" })
                AssertTextClean(FormatEventLoc(cb, $"THINGS_CRACKED_BRAZIER.pages.{page}"), $"CB page {page} ({lang})");

            var sw = BeginEventForPlayer<ThingsSiltWinch>(p, run);
            AssertTextClean(sw.Title.GetFormattedText(), $"SW Title ({lang})");
            AssertTextClean(sw.Description!.GetFormattedText(), $"SW Init ({lang})");
            foreach (string opt in new[] { "HAUL_1", "CUT_POUCH" })
                AssertTextClean(FormatEventLoc(sw, $"THINGS_SILT_WINCH.pages.INITIAL.options.{opt}.description"), $"SW opt {opt} ({lang})");
            foreach (string sub in new[]
            {
                "DEPTH_2.description", "DEPTH_2.options.HAUL_2_POTION.description", "DEPTH_2.options.HAUL_2_GOLD.description", "DEPTH_2.options.STOP_1.description",
                "DEPTH_3.description", "DEPTH_3.options.HAUL_3.description", "DEPTH_3.options.HAUL_3_LOW_HP.description", "DEPTH_3.options.HAUL_3_EMPTY.description", "DEPTH_3.options.STOP_2.description",
                "HAUL_1.loss", "HAUL_2.loss", "HAUL_3.description", "HAUL_3.loss", "STOP_1.description", "STOP_2.description", "CUT_POUCH.description"
            })
                AssertTextClean(FormatEventLoc(sw, $"THINGS_SILT_WINCH.pages.{sub}"), $"SW sub {sub} ({lang})");
        }
    }

    private async Task VerifyChronometerRigAsync()
    {
        // Option 1: Gear (Imbued on Skill for 75 Gold) + cancel before spending Gold
        RunState run1 = SetupTestRun("cr-gear");
        Player p1 = run1.Players[0];
        p1.Gold = 100;
        CardModel defend = p1.Deck.Cards.First(c => c is DefendIronclad);
        CardModel strike = p1.Deck.Cards.First(c => c is StrikeIronclad);
        var ev1 = BeginEventForPlayer<ThingsChronometerRig>(p1, run1);
        Assert(ev1.CurrentOptions.Count == 3, "ChronometerRig shows 3 initial options");

        var cancel = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancel))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p1.Gold == 100 && !cancel.LastOffered.Contains(strike) && cancel.LastOffered.Contains(defend),
            "Imbued only offers Skills and canceling preserves 100 Gold");

        var pickDefend = new ProbeCardSelector(targetPredicate: c => c == defend);
        using (ProbeRuntimeApi.UseSelector(pickDefend))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && p1.Gold == 100 - ThingsChronometerRig.GearGoldCost && defend.Enchantment is Imbued,
            "Gear deducts 75 Gold and enchants Defend with Imbued");

        // Option 2: Pin (PerfectFit for 8 Max HP) + cancel before Max HP loss
        RunState run2 = SetupTestRun("cr-pin");
        Player p2 = run2.Players[0];
        int maxHpBefore = p2.Creature.MaxHp;
        CardModel bash = p2.Deck.Cards.First(c => c is Bash);
        var ev2 = BeginEventForPlayer<ThingsChronometerRig>(p2, run2);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(!ev2.IsFinished && p2.Creature.MaxHp == maxHpBefore, "Canceling Pin preserves Max HP");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == bash)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && p2.Creature.MaxHp == maxHpBefore - ThingsChronometerRig.PinMaxHpLoss && bash.Enchantment is PerfectFit,
            "Pin deducts 8 Max HP and enchants Bash with PerfectFit");

        // Option 3: Oil Potion + fallback 35 Gold when potion slots full
        RunState run3 = SetupTestRun("cr-oil");
        Player p3 = run3.Players[0];
        int potsBefore = p3.Potions.Count();
        var ev3 = BeginEventForPlayer<ThingsChronometerRig>(p3, run3);
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && p3.Potions.Count() == potsBefore + 1, "Oil option grants 1 random potion");

        RunState run4 = SetupTestRun("cr-oil-full");
        Player p4 = run4.Players[0];
        while (p4.HasOpenPotionSlots)
            await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(), p4);
        int gBefore = p4.Gold;
        var ev4 = BeginEventForPlayer<ThingsChronometerRig>(p4, run4);
        Assert(ev4.CurrentOptions[2].TextKey.Contains("OIL_GOLD", StringComparison.Ordinal),
            "ChronometerRig switches option 3 to OIL_GOLD when potion belt is full");
        await ev4.CurrentOptions[2].Chosen();
        Assert(ev4.IsFinished && p4.Gold == gBefore + ThingsChronometerRig.OilFallbackGold,
            "Full-belt fallback grants 35 Gold");

        // Locks check: low gold & low max HP
        RunState runLocks = SetupTestRun("cr-locks");
        Player pLocks = runLocks.Players[0];
        pLocks.Gold = ThingsChronometerRig.GearGoldCost - 1;
        pLocks.Creature.SetMaxHpInternal(ThingsChronometerRig.PinMaxHpLoss);
        var evLocks = BeginEventForPlayer<ThingsChronometerRig>(pLocks, runLocks);
        Assert(evLocks.CurrentOptions[0].IsLocked && evLocks.CurrentOptions[1].IsLocked && !evLocks.CurrentOptions[2].IsLocked,
            "Low gold & low Max HP lock options 0 and 1 cleanly");
    }

    private async Task VerifySilveredGalleryAsync()
    {
        // Option 1: Duplicate Rare (preserves Upgrade + Enchantment!)
        RunState run1 = SetupTestRun("sg-rare");
        Player p1 = run1.Players[0];
        CardModel rareCard = run1.CreateCard<Feed>(p1);
        CardCmd.Upgrade(rareCard);
        CardCmd.Enchant<Sharp>(rareCard, 2m);
        await CardPileCmd.Add(rareCard, PileType.Deck);
        int hp1 = p1.Creature.CurrentHp;
        int deckBefore = p1.Deck.Cards.Count;

        var ev1 = BeginEventForPlayer<ThingsSilveredGallery>(p1, run1);
        var cancelDup = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelDup))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p1.Creature.CurrentHp == hp1 && cancelDup.LastOffered.All(c => c.Rarity == CardRarity.Rare),
            "Duplicate only offers Rare cards when Rare exists, and cancel preserves HP");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == rareCard)))
            await ev1.CurrentOptions[0].Chosen();
        List<CardModel> clones = p1.Deck.Cards.Where(c => c.Id == rareCard.Id).ToList();
        Assert(ev1.IsFinished && p1.Creature.CurrentHp == hp1 - ThingsSilveredGallery.DuplicateDamage
               && p1.Deck.Cards.Count == deckBefore + 1 && clones.Count == 2
               && clones.All(c => c.IsUpgraded && c.Enchantment is Sharp),
            "Duplicate deals 16 HP and clones Rare card preserving Upgrade & Enchantment");

        // Fallback duplicate when NO Rare card in deck -> any card offered
        RunState runNoRare = SetupTestRun("sg-norare");
        Player pNoRare = runNoRare.Players[0];
        CardModel bashNoRare = pNoRare.Deck.Cards.First(c => c is Bash);
        var evNoRare = BeginEventForPlayer<ThingsSilveredGallery>(pNoRare, runNoRare);
        Assert(!evNoRare.CurrentOptions[0].IsLocked && evNoRare.CurrentOptions[0].TextKey.Contains("DUPLICATE_ANY", StringComparison.Ordinal),
            "Without Rare cards, Duplicate smoothly falls back to DUPLICATE_ANY");
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == bashNoRare)))
            await evNoRare.CurrentOptions[0].Chosen();
        Assert(evNoRare.IsFinished && pNoRare.Deck.Cards.Count(c => c.Id == bashNoRare.Id) == 2,
            "Fallback duplicate clones chosen non-Rare card");

        // Option 2: Pawn 95 Gold + Upgraded card -> Rare Relic (cancel returns cleanly)
        RunState run2 = SetupTestRun("sg-pawn");
        Player p2 = run2.Players[0];
        p2.Gold = 120;
        CardModel upgradedStrike = p2.Deck.Cards.First(c => c is StrikeIronclad);
        CardCmd.Upgrade(upgradedStrike);
        int relicsBefore = p2.Relics.Count;
        var ev2 = BeginEventForPlayer<ThingsSilveredGallery>(p2, run2);
        var cancelPawn = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelPawn))
            await ev2.CurrentOptions[1].Chosen();
        Assert(!ev2.IsFinished && p2.Gold == 120 && cancelPawn.LastOffered.All(c => c.IsUpgraded),
            "Pawn only offers Upgraded cards and cancel preserves Gold");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == upgradedStrike)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && p2.Gold == 120 - ThingsSilveredGallery.PawnGoldCost
               && !p2.Deck.Cards.Contains(upgradedStrike) && p2.Relics.Count == relicsBefore + 1
               && p2.Relics[^1].Rarity == RelicRarity.Rare,
            "Pawn spends 95 Gold, removes upgraded Strike, and grants 1 Rare Relic");

        // Option 3: Polish upgrade + fallback heal
        RunState run3 = SetupTestRun("sg-polish");
        Player p3 = run3.Players[0];
        var ev3 = BeginEventForPlayer<ThingsSilveredGallery>(p3, run3);
        int upgradedCountBefore = p3.Deck.Cards.Count(c => c.IsUpgraded);
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && p3.Deck.Cards.Count(c => c.IsUpgraded) == upgradedCountBefore + 1,
            "Polish upgrades 1 random card");

        RunState run4 = SetupTestRun("sg-polish-heal");
        Player p4 = run4.Players[0];
        foreach (CardModel card in p4.Deck.Cards)
            if (card.IsUpgradable) CardCmd.Upgrade(card);
        p4.Creature.SetCurrentHpInternal(40);
        var ev4 = BeginEventForPlayer<ThingsSilveredGallery>(p4, run4);
        Assert(ev4.CurrentOptions[2].TextKey.Contains("POLISH_HEAL", StringComparison.Ordinal),
            "All upgraded falls back to POLISH_HEAL");
        await ev4.CurrentOptions[2].Chosen();
        Assert(ev4.IsFinished && p4.Creature.CurrentHp == 40 + ThingsSilveredGallery.PolishFallbackHeal,
            "Polish heal grants 10 HP");
    }

    private async Task VerifyCrackedBrazierAsync()
    {
        // Option 1: Temper -> Tezcatara's Ember (0 cost + Eternal + non-X filter)
        RunState run1 = SetupTestRun("cb-temper");
        Player p1 = run1.Players[0];
        CardModel bash = p1.Deck.Cards.First(c => c is Bash);
        CardModel whirlwind = run1.CreateCard<Whirlwind>(p1);
        await CardPileCmd.Add(whirlwind, PileType.Deck);
        int maxHpBefore = p1.Creature.MaxHp;

        var ev1 = BeginEventForPlayer<ThingsCrackedBrazier>(p1, run1);
        var cancelSel = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelSel))
            await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p1.Creature.MaxHp == maxHpBefore && !cancelSel.LastOffered.Contains(whirlwind),
            "Temper excludes X-cost cards and cancel preserves Max HP");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == bash)))
            await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && p1.Creature.MaxHp == maxHpBefore - ThingsCrackedBrazier.TemperMaxHpLoss
               && bash.Enchantment is TezcatarasEmber
               && bash.EnergyCost.GetWithModifiers(CostModifiers.None) == 0
               && bash.Keywords.Contains(CardKeyword.Eternal),
            "Temper deducts 10 Max HP, drops cost to 0, and grants Eternal via Tezcatara's Ember");

        // Option 2a: Burn Common card -> 9 HP damage, remove, NO Common Relic
        RunState run2 = SetupTestRun("cb-burn-common");
        Player p2 = run2.Players[0];
        CardModel strike = p2.Deck.Cards.First(c => c is StrikeIronclad);
        int hp2 = p2.Creature.CurrentHp;
        int relics2 = p2.Relics.Count;
        var ev2 = BeginEventForPlayer<ThingsCrackedBrazier>(p2, run2);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(!ev2.IsFinished && p2.Creature.CurrentHp == hp2, "Cancel burn preserves HP");

        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == strike)))
            await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished && p2.Creature.CurrentHp == hp2 - ThingsCrackedBrazier.BurnDamage
               && !p2.Deck.Cards.Contains(strike) && p2.Relics.Count == relics2,
            "Burning non-Rare removes card for 9 HP without extra relic");

        // Option 2b: Burn Rare card -> 9 HP damage, remove, AND grants 1 Common Relic!
        RunState run3 = SetupTestRun("cb-burn-rare");
        Player p3 = run3.Players[0];
        CardModel rareBurn = run3.CreateCard<Feed>(p3);
        await CardPileCmd.Add(rareBurn, PileType.Deck);
        int hp3 = p3.Creature.CurrentHp;
        int relics3 = p3.Relics.Count;
        var ev3 = BeginEventForPlayer<ThingsCrackedBrazier>(p3, run3);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(targetPredicate: c => c == rareBurn)))
            await ev3.CurrentOptions[1].Chosen();
        Assert(ev3.IsFinished && p3.Creature.CurrentHp == hp3 - ThingsCrackedBrazier.BurnDamage
               && !p3.Deck.Cards.Contains(rareBurn) && p3.Relics.Count == relics3 + 1
               && p3.Relics[^1].Rarity == RelicRarity.Common,
            "Burning Rare card grants 1 bonus Common Relic");

        // Option 3: Warm Hands -> Heal 10 HP
        RunState run4 = SetupTestRun("cb-warm");
        Player p4 = run4.Players[0];
        p4.Creature.SetCurrentHpInternal(40);
        var ev4 = BeginEventForPlayer<ThingsCrackedBrazier>(p4, run4);
        await ev4.CurrentOptions[2].Chosen();
        Assert(ev4.IsFinished && p4.Creature.CurrentHp == 40 + ThingsCrackedBrazier.WarmHeal,
            "Warm Hands heals 10 HP");
    }

    private async Task VerifySiltWinchAsync()
    {
        // Full 3-stage escalation: Haul 1 (5 dmg + 3 upgraded Common/Uncommon card choices) -> Haul 2 (10 dmg + 45G + Potion) -> Haul 3 (10 Max HP + Uncommon/Rare Relic + Decay)
        RunState run1 = SetupTestRun("sw-deep");
        Player p1 = run1.Players[0];
        p1.Gold = 50;
        int hp0 = p1.Creature.CurrentHp;
        int maxHp0 = p1.Creature.MaxHp;
        int deckBefore = p1.Deck.Cards.Count;
        int potsBefore = p1.Potions.Count();
        int relicsBefore = p1.Relics.Count;

        var ev1 = BeginEventForPlayer<ThingsSiltWinch>(p1, run1);
        Assert(ev1.CurrentOptions.Count == 2, "SiltWinch shows 2 initial options");
        var rewardSel = new ProbeCardSelector();
        using (ProbeRuntimeApi.UseSelector(rewardSel))
            await ev1.CurrentOptions[0].Chosen();

        Assert(!ev1.IsFinished && p1.Creature.CurrentHp == hp0 - ThingsSiltWinch.Pull1Damage,
            "Haul 1 deals 5 HP damage and keeps event open on DEPTH_2");
        Assert(rewardSel.LastRewardCards.Count == 3
               && rewardSel.LastRewardCards.All(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon && (!c.IsUpgradable || c.IsUpgraded)),
            "Haul 1 offers 3 upgraded Common/Uncommon card choices");
        Assert(p1.Deck.Cards.Count == deckBefore + 1, "Haul 1 card reward adds selected upgraded card to deck");
        Assert(ev1.CurrentOptions.Count == 2 && ev1.CurrentOptions[0].TextKey.Contains("HAUL_2_POTION", StringComparison.Ordinal),
            "DEPTH_2 offers HAUL_2_POTION and STOP_1");

        await ev1.CurrentOptions[0].Chosen();
        Assert(!ev1.IsFinished && p1.Creature.CurrentHp == hp0 - ThingsSiltWinch.Pull1Damage - ThingsSiltWinch.Pull2Damage
               && p1.Gold == 50 + ThingsSiltWinch.Pull2Gold && p1.Potions.Count() == potsBefore + 1,
            "Haul 2 deals 10 HP damage, grants 45 Gold + 1 Potion, and transitions to DEPTH_3");
        Assert(ev1.CurrentOptions.Count == 2, "DEPTH_3 offers HAUL_3 and STOP_2");

        await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished && p1.Creature.MaxHp == maxHp0 - ThingsSiltWinch.Pull3MaxHpLoss
               && p1.Relics.Count == relicsBefore + 1
               && p1.Relics[^1].Rarity is RelicRarity.Uncommon or RelicRarity.Rare
               && p1.Deck.Cards.OfType<Decay>().Count() == 1,
            "Haul 3 deducts 10 Max HP, grants Uncommon/Rare Relic, and adds 1 Decay");

        // Haul 2 full-potion fallback grants +25 extra Gold (total +70 Gold), and Stop works cleanly
        RunState runFull = SetupTestRun("sw-full-pot");
        Player pFull = runFull.Players[0];
        while (pFull.HasOpenPotionSlots)
            await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(), pFull);
        pFull.Gold = 50;
        var evFull = BeginEventForPlayer<ThingsSiltWinch>(pFull, runFull);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector()))
            await evFull.CurrentOptions[0].Chosen();
        Assert(evFull.CurrentOptions[0].TextKey.Contains("HAUL_2_GOLD", StringComparison.Ordinal),
            "Full potion slots shows HAUL_2_GOLD upfront");
        await evFull.CurrentOptions[0].Chosen();
        Assert(!evFull.IsFinished && pFull.Gold == 50 + ThingsSiltWinch.Pull2Gold + ThingsSiltWinch.Pull2PotionFallbackGold,
            "Haul 2 full-potion fallback grants 70 Gold total");
        await evFull.CurrentOptions[1].Chosen();
        Assert(evFull.IsFinished, "STOP_2 finishes event cleanly");

        // Early STOP_1 works + lossless Cut Loose Pouch grants 25 Gold
        RunState runStop1 = SetupTestRun("sw-stop1");
        Player pStop1 = runStop1.Players[0];
        var evStop1 = BeginEventForPlayer<ThingsSiltWinch>(pStop1, runStop1);
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector()))
            await evStop1.CurrentOptions[0].Chosen();
        await evStop1.CurrentOptions[1].Chosen();
        Assert(evStop1.IsFinished, "STOP_1 finishes cleanly after Haul 1");

        RunState runPouch = SetupTestRun("sw-pouch");
        Player pPouch = runPouch.Players[0];
        int gBefore = pPouch.Gold;
        var evPouch = BeginEventForPlayer<ThingsSiltWinch>(pPouch, runPouch);
        await evPouch.CurrentOptions[1].Chosen();
        Assert(evPouch.IsFinished && pPouch.Gold == gBefore + ThingsSiltWinch.PouchGold,
            "Cut Loose Pouch grants 25 Gold");
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

        (string Key, Func<Player, RunState, EventModel> Begin, int ExpectedOpts)[] items =
        [
            ("chronometer_rig", (p, r) => BeginEventForPlayer<ThingsChronometerRig>(p, r), 3),
            ("silvered_gallery", (p, r) => BeginEventForPlayer<ThingsSilveredGallery>(p, r), 3),
            ("cracked_brazier", (p, r) => BeginEventForPlayer<ThingsCrackedBrazier>(p, r), 3),
            ("silt_winch", (p, r) => BeginEventForPlayer<ThingsSiltWinch>(p, r), 2),
        ];

        foreach (string lang in new[] { "zhs", "eng" })
        foreach (var (key, begin, expectedOpts) in items)
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            RunState run = SetupTestRun($"render4-{key}-{lang}");
            Player p = run.Players[0];
            p.Gold = 120;
            CardModel previewRare = run.CreateCard<Feed>(p);
            CardCmd.Upgrade(previewRare);
            await CardPileCmd.Add(previewRare, PileType.Deck);
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
            Assert(opts.Count == expectedOpts, $"Native layout renders {expectedOpts} options for {key} ({lang})");
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
            return Task.FromResult<IEnumerable<CardModel>>([LastOffered[pickIndex]]);
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            LastRewardCards.Clear();
            LastRewardCards.AddRange(options.Select(o => o.Card));
            return new() { card = options.FirstOrDefault()?.Card };
        }
    }
}
