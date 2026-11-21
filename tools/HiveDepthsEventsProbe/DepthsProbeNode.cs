using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
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
using MegaCrit.Sts2.Core.Entities.Relics;
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
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Enchantments;
using STS2_Things.Events;
using STS2_Things.Relics;

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
            {
                Assert(ProjectSettings.LoadResourcePack(modPck), "Load STS2_Things PCK");
            }

            ModHelper.AddModelToPool<TokenCardPool, ThingsStickyBeeswax>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsAmberCombCore>();

            STS2_Things.Platform.Sts2VersionSeam.InitializeBeforeModelDatabase();
            InitializeModelDb();
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsWarmRoyalCell).Assembly);

            modHarmony = new Harmony("HiveDepthsEventsProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsWarmRoyalCell).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyCatalogAndResources();
            VerifyLocalizationInBothLanguages();
            await VerifyWarmRoyalCellAsync();
            await VerifyAmberCombCoreCombatAsync();
            await VerifyStickyBeeswaxCombatAsync();
            await VerifySpliceBoothAsync();

            if (_hasVanillaPck)
            {
                await RenderLiveUiAndAssetsAsync();
            }

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
                    events = new[] { "THINGS_WARM_ROYAL_CELL", "THINGS_SPLICE_BOOTH" },
                    relic = "THINGS_AMBER_COMB_CORE",
                    card = "THINGS_STICKY_BEESWAX",
                    enchantment = "THINGS_OVERCLOCKED",
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.Print($"Hive & Depths events probe: PASS ({_checks} assertions)");
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
        CharacterModel[] roster =
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
        ];
        for (int i = 0; i < playerCount; i++)
        {
            Player p = Player.CreateForNewRun(roster[i % roster.Length], UnlockState.all, (ulong)(i + 1));
            players.Add(p);
        }

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

    private static RelicModel ObtainRelicSync(RelicModel relic, Player player)
    {
        RelicModel mutable = relic.IsMutable ? relic : relic.ToMutable();
        RelicCmd.Obtain(mutable, player).GetAwaiter().GetResult();
        return mutable;
    }

    private static SerializableRelic RoundTripRelic(RelicModel relic)
    {
        var writer = new PacketWriter { WarnOnGrow = false };
        relic.ToSerializable().Serialize(writer);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        var serial = new SerializableRelic();
        serial.Deserialize(reader);
        return serial;
    }

    private void VerifyCatalogAndResources()
    {
        Hive hive = ModelDb.Act<Hive>();
        Assert(hive.AllEvents.Contains(ModelDb.Event<ThingsWarmRoyalCell>()),
            "Hive.AllEvents includes ThingsWarmRoyalCell when enabled");

        ThingsDepths depths = ModelDb.Act<ThingsDepths>();
        Assert(depths.AllEvents.Contains(ModelDb.Event<ThingsSpliceBooth>()),
            "ThingsDepths.AllEvents includes ThingsSpliceBooth when enabled");

        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.EventWarmRoyalCellEnabled] = false,
            [ThingsModConfig.EventSpliceBoothEnabled] = false,
        });
        Assert(!hive.AllEvents.Contains(ModelDb.Event<ThingsWarmRoyalCell>()),
            "Hive.AllEvents excludes ThingsWarmRoyalCell when disabled");
        Assert(!depths.AllEvents.Contains(ModelDb.Event<ThingsSpliceBooth>()),
            "ThingsDepths.AllEvents excludes ThingsSpliceBooth when disabled");
        ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(entry => entry.Key, entry => (object?)entry.Default));

        RelicModel core = ModelDb.Relic<ThingsAmberCombCore>();
        Assert(core.Rarity == RelicRarity.Event && !core.IsTradable, "AmberCombCore has Event rarity and IsTradable=false");
        Assert(ModelDb.RelicPool<EventRelicPool>().AllRelics.Contains(core), "AmberCombCore in EventRelicPool");

        CardModel beeswax = ModelDb.Card<ThingsStickyBeeswax>();
        Assert(beeswax.Type == CardType.Status && beeswax.Rarity == CardRarity.Status, "StickyBeeswax is Status");
        Assert(beeswax.EnergyCost.Canonical == 0 && beeswax.MaxUpgradeLevel == 0 && !beeswax.CanBeGeneratedInCombat,
            "StickyBeeswax is 0-cost non-upgradable non-combat-gen");
        Assert(beeswax.CanonicalKeywords.Contains(CardKeyword.Exhaust), "StickyBeeswax has Exhaust");
        Assert(ModelDb.CardPool<TokenCardPool>().AllCards.Contains(beeswax), "StickyBeeswax in TokenCardPool");

        EnchantmentModel over = ModelDb.Enchantment<ThingsOverclocked>();
        Assert(over.HasExtraCardText, "ThingsOverclocked shows extra card text");

        if (_hasVanillaPck)
        {
            foreach ((string path, Vector2I size) in new (string, Vector2I)[]
            {
                ("res://images/events/things_warm_royal_cell.png", new Vector2I(3440, 1616)),
                ("res://images/events/things_splice_booth.png", new Vector2I(3440, 1616)),
                ("res://images/relics/things_amber_comb_core.png", new Vector2I(256, 256)),
                ("res://images/enchantments/things_overclocked.png", new Vector2I(64, 64)),
                ("res://images/packed/card_portraits/token/things_sticky_beeswax.png", new Vector2I(1000, 760)),
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
        Assert(!Regex.IsMatch(text, @"\{[A-Za-z0-9_:.]+\}"),
            $"Unresolved placeholder in {label}: {text}");
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

            RunState run = SetupTestRun($"loc-{lang}");
            Player p = run.Players[0];

            var cell = BeginEventForPlayer<ThingsWarmRoyalCell>(p, run);
            AssertTextClean(cell.Title.GetFormattedText(), $"WarmRoyalCell.Title ({lang})");
            AssertTextClean(cell.Description!.GetFormattedText(), $"WarmRoyalCell.Init ({lang})");
            foreach (string optKey in new[] { "EAT_JELLY", "REFORGE", "REFORGE_NO_CARD", "TAKE_CORE", "CORE_OWNED" })
            {
                AssertTextClean(FormatEventLoc(cell, $"THINGS_WARM_ROYAL_CELL.pages.INITIAL.options.{optKey}.title"), $"WRC {optKey}.title ({lang})");
                AssertTextClean(FormatEventLoc(cell, $"THINGS_WARM_ROYAL_CELL.pages.INITIAL.options.{optKey}.description"), $"WRC {optKey}.desc ({lang})");
            }
            foreach (string pageKey in new[] { "EAT_JELLY.description", "REFORGE.description", "REFORGE.loss", "TAKE_CORE.description" })
            {
                AssertTextClean(FormatEventLoc(cell, $"THINGS_WARM_ROYAL_CELL.pages.{pageKey}"), $"WRC page {pageKey} ({lang})");
            }

            var booth = BeginEventForPlayer<ThingsSpliceBooth>(p, run);
            AssertTextClean(booth.Title.GetFormattedText(), $"SpliceBooth.Title ({lang})");
            AssertTextClean(booth.Description!.GetFormattedText(), $"SpliceBooth.Init ({lang})");
            foreach (string optKey in new[] { "OVERCLOCK", "OVERCLOCK_LOW_HP", "OVERCLOCK_NO_CARD", "REWIRE", "REWIRE_NO_CARDS", "SALVAGE", "LEAVE" })
            {
                AssertTextClean(FormatEventLoc(booth, $"THINGS_SPLICE_BOOTH.pages.INITIAL.options.{optKey}.title"), $"SB {optKey}.title ({lang})");
                AssertTextClean(FormatEventLoc(booth, $"THINGS_SPLICE_BOOTH.pages.INITIAL.options.{optKey}.description"), $"SB {optKey}.desc ({lang})");
            }
            foreach (string pageKey in new[] { "OVERCLOCK.description", "OVERCLOCK.loss", "REWIRE.description", "REWIRE.loss", "SALVAGE.description", "LEAVE.description", "ABORTED.description" })
            {
                AssertTextClean(FormatEventLoc(booth, $"THINGS_SPLICE_BOOTH.pages.{pageKey}"), $"SB page {pageKey} ({lang})");
            }

            RelicModel core = ModelDb.Relic<ThingsAmberCombCore>().ToMutable();
            AssertTextClean(core.Title.GetFormattedText(), $"AmberCombCore.Title ({lang})");
            AssertTextClean(core.DynamicDescription.GetFormattedText(), $"AmberCombCore.Desc ({lang})");
            AssertTextClean(core.Flavor.GetFormattedText(), $"AmberCombCore.Flavor ({lang})");

            CardModel wax = run.CreateCard<ThingsStickyBeeswax>(p);
            AssertTextClean(wax.Title, $"StickyBeeswax.Title ({lang})");
            AssertTextClean(wax.GetDescriptionForPile(PileType.Deck), $"StickyBeeswax.Desc ({lang})");

            EnchantmentModel over = ModelDb.Enchantment<ThingsOverclocked>().ToMutable();
            AssertTextClean(over.Title.GetFormattedText(), $"Overclocked.Title ({lang})");
            AssertTextClean(over.DynamicDescription.GetFormattedText(), $"Overclocked.Desc ({lang})");
            CardModel previewBash = run.CreateCard<Bash>(p);
            CardCmd.Enchant<ThingsOverclocked>(previewBash, 1m);
            AssertTextClean(previewBash.Enchantment!.DynamicExtraCardText!.GetFormattedText(), $"Overclocked.Extra ({lang})");
        }

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
    }

    private async Task VerifyWarmRoyalCellAsync()
    {
        // Option 1: Eat Jelly (+22 Heal, +6 MaxHP, +1 StickyBeeswax)
        RunState run1 = SetupTestRun("wrc-jelly");
        Player p1 = run1.Players[0];
        int maxBefore = p1.Creature.MaxHp;
        p1.Creature.SetCurrentHpInternal(20);
        int deckBefore1 = p1.Deck.Cards.Count;
        var ev1 = BeginEventForPlayer<ThingsWarmRoyalCell>(p1, run1);
        Assert(ev1.CurrentOptions.Count == 3 && ev1.CurrentOptions.All(o => !o.IsLocked),
            "WarmRoyalCell unlocks all 3 initial options with starter deck");
        await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished, "Eat Jelly finishes event");
        Assert(p1.Creature.MaxHp == maxBefore + ThingsWarmRoyalCell.JellyMaxHp, "Eat Jelly grants +6 Max HP");
        Assert(p1.Creature.CurrentHp == 20 + ThingsWarmRoyalCell.JellyHeal + ThingsWarmRoyalCell.JellyMaxHp,
            "Eat Jelly heals 22 HP plus native +6 GainMaxHp heal");
        Assert(p1.Deck.Cards.Count == deckBefore1 + 1 && p1.Deck.Cards.Count(c => c is ThingsStickyBeeswax) == 1,
            "Eat Jelly adds 1 StickyBeeswax to Deck");
        Assert(p1.Deck.Cards.Single(c => c is ThingsStickyBeeswax).IsRemovable,
            "StickyBeeswax in Deck can be removed later");

        // Option 2: Reforge Basics (-6 HP, up to 2 Basic Strike/Defend -> unupgraded Common character cards)
        RunState run2 = SetupTestRun("wrc-reforge");
        Player p2 = run2.Players[0];
        int hpBefore2 = p2.Creature.CurrentHp;
        int basicBefore2 = p2.Deck.Cards.Count(c => c.Rarity == CardRarity.Basic);
        int commonBefore2 = p2.Deck.Cards.Count(c => c.Rarity == CardRarity.Common);
        var ev2 = BeginEventForPlayer<ThingsWarmRoyalCell>(p2, run2);
        await ev2.CurrentOptions[1].Chosen();
        Assert(ev2.IsFinished, "Reforge Basics finishes event");
        Assert(p2.Creature.CurrentHp == hpBefore2 - ThingsWarmRoyalCell.ReforgeDamage, "Reforge deals 6 HP damage");
        Assert(p2.Deck.Cards.Count(c => c.Rarity == CardRarity.Basic) == basicBefore2 - 2,
            "Reforge replaces 2 Basic cards");
        Assert(p2.Deck.Cards.Count(c => c.Rarity == CardRarity.Common && !c.IsUpgraded) == commonBefore2 + 2,
            "Reforge transforms into 2 unupgraded Common cards");

        // Option 2 edge cases: 1 Basic left, 0 Basics left (locked), lethal 6 damage
        RunState run2One = SetupTestRun("wrc-reforge-one");
        Player p2One = run2One.Players[0];
        foreach (CardModel c in p2One.Deck.Cards.Where(c => c.Rarity == CardRarity.Basic && (c.Tags.Contains(CardTag.Strike) || c.Tags.Contains(CardTag.Defend))).Skip(1).ToList())
            await CardPileCmd.RemoveFromDeck(c);
        var ev2One = BeginEventForPlayer<ThingsWarmRoyalCell>(p2One, run2One);
        Assert(!ev2One.CurrentOptions[1].IsLocked, "1 Strike/Defend left keeps Reforge unlocked");
        await ev2One.CurrentOptions[1].Chosen();
        Assert(p2One.Deck.Cards.Count(c => c.Rarity == CardRarity.Basic && (c.Tags.Contains(CardTag.Strike) || c.Tags.Contains(CardTag.Defend))) == 0,
            "1 Strike/Defend transformed cleanly without crash");

        var ev2Locked = BeginEventForPlayer<ThingsWarmRoyalCell>(p2One, run2One);
        Assert(ev2Locked.CurrentOptions[1].IsLocked && ev2Locked.CurrentOptions[1].Title.LocEntryKey.Contains("REFORGE_NO_CARD"),
            "0 Basic Strike/Defends locks Reforge");

        RunState run2Lethal = SetupTestRun("wrc-reforge-lethal");
        Player p2Lethal = run2Lethal.Players[0];
        p2Lethal.Creature.SetCurrentHpInternal(5);
        int basicsLethalBefore = p2Lethal.Deck.Cards.Count(c => c.Rarity == CardRarity.Basic);
        var ev2Lethal = BeginEventForPlayer<ThingsWarmRoyalCell>(p2Lethal, run2Lethal);
        await ev2Lethal.CurrentOptions[1].Chosen();
        Assert(p2Lethal.Creature.IsDead && ev2Lethal.IsFinished, "Lethal 6 dmg kills 5 HP player cleanly");
        Assert(p2Lethal.Deck.Cards.Count(c => c.Rarity == CardRarity.Basic) == basicsLethalBefore,
            "Lethal Reforge does not transform after death");

        // Option 3: Take Comb Core + 2 StickyBeeswax, and CORE_OWNED lock in co-op
        RunState coop = SetupTestRun("wrc-core-coop", playerCount: 2);
        Player owner = coop.Players[0];
        Player peer = coop.Players[1];
        int ownerDeckBefore = owner.Deck.Cards.Count;
        var evOwner = BeginEventForPlayer<ThingsWarmRoyalCell>(owner, coop);
        await evOwner.CurrentOptions[2].Chosen();
        Assert(evOwner.IsFinished, "Take Core finishes event");
        Assert(owner.GetRelic<ThingsAmberCombCore>() != null, "Take Core grants ThingsAmberCombCore");
        Assert(owner.Deck.Cards.Count == ownerDeckBefore + ThingsWarmRoyalCell.CoreBeeswaxCount
               && owner.Deck.Cards.Count(c => c is ThingsStickyBeeswax) == 2,
            "Take Core adds 2 StickyBeeswax to owner Deck");

        var evOwnerAgain = BeginEventForPlayer<ThingsWarmRoyalCell>(owner, coop);
        Assert(evOwnerAgain.CurrentOptions[2].IsLocked && evOwnerAgain.CurrentOptions[2].Title.LocEntryKey.Contains("CORE_OWNED"),
            "Owning AmberCombCore locks option 2");
        var evPeer = BeginEventForPlayer<ThingsWarmRoyalCell>(peer, coop);
        Assert(!evPeer.CurrentOptions[2].IsLocked, "Co-op peer without AmberCombCore sees option 2 unlocked");
    }

    private Task VerifyAmberCombCoreCombatAsync()
    {
        RunState run = SetupTestRun("comb-core-combat", playerCount: 2);
        Player owner = run.Players[0];
        Player peer = run.Players[1];

        var core = (ThingsAmberCombCore)ObtainRelicSync(ModelDb.Relic<ThingsAmberCombCore>(), owner);
        SerializableRelic saved = RoundTripRelic(core);
        var restored = (ThingsAmberCombCore)RelicModel.FromSerializable(saved);
        restored.Owner = owner;
        Assert(restored.Id == core.Id && restored.Rarity == RelicRarity.Event, "AmberCombCore round-trips cleanly");

        EncounterModel enc = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom room = CreateCombatRoom(enc, run);
        owner.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        peer.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        ActivateSyntheticCombat(room.CombatState);
        try
        {
            Assert(owner.PlayerCombatState!.TurnNumber == 1, "PlayerCombatState starts combat at TurnNumber 1");
            Assert(Hook.ModifyHandDraw(room.CombatState, owner, 5, out IEnumerable<AbstractModel> ownerT1Mods) == 6m
                   && ownerT1Mods.Any(m => m is ThingsAmberCombCore),
                "AmberCombCore draws +1 Turn 1 for owner");
            Hook.ModifyHandDraw(room.CombatState, peer, 5, out IEnumerable<AbstractModel> peerT1Mods);
            Assert(!peerT1Mods.Any(m => m is ThingsAmberCombCore),
                "AmberCombCore ignores co-op peer");

            owner.PlayerCombatState.IncrementTurnNumber(); // Turn 2
            Assert(Hook.ModifyHandDraw(room.CombatState, owner, 5, out IEnumerable<AbstractModel> ownerT2Mods) == 6m
                   && ownerT2Mods.Any(m => m is ThingsAmberCombCore),
                "AmberCombCore draws +1 Turn 2 for owner");

            owner.PlayerCombatState.IncrementTurnNumber(); // Turn 3
            Assert(Hook.ModifyHandDraw(room.CombatState, owner, 5, out IEnumerable<AbstractModel> ownerT3Mods) == 5m
                   && !ownerT3Mods.Any(m => m is ThingsAmberCombCore),
                "AmberCombCore stops Turn 3+");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(room, run);
        }
        return Task.CompletedTask;
    }

    private async Task VerifyStickyBeeswaxCombatAsync()
    {
        RunState run = SetupTestRun("beeswax-combat");
        Player owner = run.Players[0];
        EncounterModel enc = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom room = CreateCombatRoom(enc, run);
        owner.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        ActivateSyntheticCombat(room.CombatState);
        try
        {
            CardPile hand = PileType.Hand.GetPile(owner);
            CardPile draw = PileType.Draw.GetPile(owner);
            CardPile exhaust = PileType.Exhaust.GetPile(owner);
            CardPile play = PileType.Play.GetPile(owner);

            // Case A: Drawn when another eligible card (Strike) and ineligible card (Wound) are in hand
            foreach (CardModel c in hand.Cards.ToList()) c.RemoveFromCurrentPile();
            foreach (CardModel c in draw.Cards.ToList()) c.RemoveFromCurrentPile();
            CardModel strike = room.CombatState.CreateCard<StrikeIronclad>(owner);
            CardModel wound = room.CombatState.CreateCard<Wound>(owner);
            var waxA = (ThingsStickyBeeswax)room.CombatState.CreateCard<ThingsStickyBeeswax>(owner);
            hand.AddInternal(strike);
            hand.AddInternal(wound);
            draw.AddInternal(waxA);

            int baseStrikeCost = strike.EnergyCost.GetWithModifiers(CostModifiers.All);
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 1m, owner, fromHandDraw: false);
            Assert(hand.Cards.Contains(waxA), "WaxA drawn into hand");
            Assert(strike.EnergyCost.GetWithModifiers(CostModifiers.All) == baseStrikeCost + 1,
                "WaxA increases eligible other card in hand by +1 this turn");

            // End of turn clears temporary +1 cost
            strike.EnergyCost.EndOfTurnCleanup();
            Assert(strike.EnergyCost.GetWithModifiers(CostModifiers.All) == baseStrikeCost,
                "EndOfTurnCleanup restores Strike cost");

            // Exhaust cleanly when played (simulated pile move to Play -> Exhaust)
            play.AddInternal(waxA);
            await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), waxA);
            Assert(exhaust.Cards.Contains(waxA), "StickyBeeswax moves to Exhaust pile cleanly");

            // Case B: Drawn FIRST into empty Hand, then next card (Defend) drawn receives the cost increase
            foreach (CardModel c in hand.Cards.ToList()) c.RemoveFromCurrentPile();
            foreach (CardModel c in draw.Cards.ToList()) c.RemoveFromCurrentPile();
            var waxB = (ThingsStickyBeeswax)room.CombatState.CreateCard<ThingsStickyBeeswax>(owner);
            CardModel defend = room.CombatState.CreateCard<DefendIronclad>(owner);
            int baseDefendCost = defend.EnergyCost.GetWithModifiers(CostModifiers.All);
            draw.AddInternal(waxB);
            draw.AddInternal(defend);
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 2m, owner, fromHandDraw: true);
            Assert(defend.EnergyCost.GetWithModifiers(CostModifiers.All) == baseDefendCost + 1,
                "WaxB drawn first in batch penalizes next drawn eligible card");

            // Case C: Drawn alone on empty turn, EndOfTurn clears _pendingPenalty so next turn draws aren't hit
            foreach (CardModel c in hand.Cards.ToList()) c.RemoveFromCurrentPile();
            foreach (CardModel c in draw.Cards.ToList()) c.RemoveFromCurrentPile();
            var waxC = (ThingsStickyBeeswax)room.CombatState.CreateCard<ThingsStickyBeeswax>(owner);
            CardModel cleanStrike = room.CombatState.CreateCard<StrikeIronclad>(owner);
            int baseCleanCost = cleanStrike.EnergyCost.GetWithModifiers(CostModifiers.All);
            draw.AddInternal(waxC);
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 1m, owner, fromHandDraw: false);
            await DispatchAfterSideTurnEndHook(
                room.CombatState,
                CombatSide.Player,
                room.CombatState.Players.Select(p => p.Creature).ToArray());
            draw.AddInternal(cleanStrike);
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 1m, owner, fromHandDraw: false);
            Assert(cleanStrike.EnergyCost.GetWithModifiers(CostModifiers.All) == baseCleanCost,
                "Pending Beeswax penalty expires cleanly at turn end");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(room, run);
        }
    }

    private async Task VerifySpliceBoothAsync()
    {
        // Case 1: Overclock option filtering, cancel rollback, apply, Save/Load, Upgrade/Downgrade, and combat 3 unblockable HP loss
        RunState run1 = SetupTestRun("sb-overclock");
        Player p1 = run1.Players[0];
        CardModel curse = run1.CreateCard<Injury>(p1);
        CardModel xCard = run1.CreateCard<Whirlwind>(p1);
        CardModel twoCost = p1.Deck.Cards.First(c => c is Bash);
        CardModel threeCost = run1.CreateCard<Bludgeon>(p1);
        CardModel enchantedTwo = run1.CreateCard<Uppercut>(p1);
        CardCmd.Enchant<Sharp>(enchantedTwo, 1m);
        await CardPileCmd.Add([curse, xCard, threeCost, enchantedTwo], PileType.Deck);

        var ev1 = BeginEventForPlayer<ThingsSpliceBooth>(p1, run1);
        Assert(ev1.CurrentOptions.Count == 3, "SpliceBooth exposes 3 initial options");
        Assert(!ev1.CurrentOptions[0].IsLocked && !ev1.CurrentOptions[1].IsLocked,
            "Overclock and Rewire unlocked initially");
        Assert(ev1.CurrentOptions[2].Title.LocEntryKey.Contains("LEAVE"),
            "With 0 upgraded cards, Option 2 falls back cleanly to LEAVE");

        // Cancel Overclock preserves MaxHp & stays open
        int maxHpBefore = p1.Creature.MaxHp;
        var cancelSelector = new ProbeCardSelector(-1);
        using (ProbeRuntimeApi.UseSelector(cancelSelector))
        {
            await ev1.CurrentOptions[0].Chosen();
        }
        Assert(!ev1.IsFinished && p1.Creature.MaxHp == maxHpBefore,
            "Canceling Overclock leaves event open without losing Max HP");
        Assert(!cancelSelector.LastOffered.Contains(curse)
               && !cancelSelector.LastOffered.Contains(xCard)
               && !cancelSelector.LastOffered.Contains(enchantedTwo)
               && !cancelSelector.LastOffered.Any(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) < 2),
            "Overclock excludes Curse, X-cost, enchanted, and <2 cost cards");
        Assert(cancelSelector.LastOffered.Contains(twoCost) && cancelSelector.LastOffered.Contains(threeCost),
            "Overclock includes Bash (cost 2) and Bludgeon (cost 3)");

        // Confirm Overclock on Bash
        var pickBashSelector = new ProbeCardSelector(targetPredicate: c => c == twoCost);
        using (ProbeRuntimeApi.UseSelector(pickBashSelector))
        {
            await ev1.CurrentOptions[0].Chosen();
        }
        Assert(ev1.IsFinished, "Confirming Overclock finishes event");
        Assert(p1.Creature.MaxHp == maxHpBefore - ThingsSpliceBooth.OverclockMaxHpLoss,
            "Overclock deducts 8 Max HP");
        Assert(twoCost.Enchantment is ThingsOverclocked, "Bash receives ThingsOverclocked enchantment");
        Assert(twoCost.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
            "Overclock reduces Bash cost 2 -> 1");

        // Save & reload preserves cost 1 cleanly (no double reduction, no revert)
        CardModel reloaded = CardModel.FromSerializable(twoCost.ToSerializable());
        Assert(reloaded.Enchantment is ThingsOverclocked && reloaded.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
            "Overclocked cost reduction survives ToSerializable/FromSerializable cleanly");

        // Upgrade -> Downgrade keeps -1 Overclocked cost
        CardCmd.Upgrade(reloaded);
        Assert(reloaded.IsUpgraded && reloaded.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
            "Upgrading Overclocked Bash preserves cost 1");
        CardCmd.Downgrade(reloaded);
        Assert(!reloaded.IsUpgraded && reloaded.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
            "Downgrading Overclocked Bash keeps Overclocked -1 cost");

        // Play Overclocked card in combat: deals 3 unblockable HP loss even with 20 Block
        EncounterModel enc = ModelDb.Encounter<ThingsLanternFishWeak>().ToMutable();
        CombatRoom room = CreateCombatRoom(enc, run1);
        p1.PopulateCombatState(run1.Rng.Shuffle, room.CombatState);
        ActivateSyntheticCombat(room.CombatState);
        try
        {
            CardModel combatTwoCost = PileType.Draw.GetPile(p1).Cards.First(c => c.Id == twoCost.Id);
            Assert(combatTwoCost.Enchantment is ThingsOverclocked && combatTwoCost.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
                "Combat clone preserves ThingsOverclocked and reduced cost");
            await CreatureCmd.GainBlock(p1.Creature, 20m, ValueProp.Move, null);
            int hpBeforePlay = p1.Creature.CurrentHp;
            await combatTwoCost.Enchantment!.OnPlay(new BlockingPlayerChoiceContext(), null);
            Assert(p1.Creature.Block == 20 && p1.Creature.CurrentHp == hpBeforePlay - ThingsOverclocked.HpLossAmount,
                "Overclocked OnPlay causes 3 unblockable HP loss ignoring Block");
        }
        finally
        {
            DeactivateSyntheticCombat();
            DetachPlayers(room, run1);
        }

        // Lock states for Option 0 (low MaxHp & no eligible cards)
        RunState runLow = SetupTestRun("sb-lowmax");
        Player pLow = runLow.Players[0];
        pLow.Creature.SetMaxHpInternal(8);
        var evLow = BeginEventForPlayer<ThingsSpliceBooth>(pLow, runLow);
        Assert(evLow.CurrentOptions[0].IsLocked && evLow.CurrentOptions[0].Title.LocEntryKey.Contains("OVERCLOCK_LOW_HP"),
            "MaxHp <= 8 locks Overclock with OVERCLOCK_LOW_HP");

        RunState runNoHigh = SetupTestRun("sb-nohigh");
        Player pNoHigh = runNoHigh.Players[0];
        foreach (CardModel c in pNoHigh.Deck.Cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) >= 2).ToList())
            await CardPileCmd.RemoveFromDeck(c);
        var evNoHigh = BeginEventForPlayer<ThingsSpliceBooth>(pNoHigh, runNoHigh);
        Assert(evNoHigh.CurrentOptions[0].IsLocked && evNoHigh.CurrentOptions[0].Title.LocEntryKey.Contains("OVERCLOCK_NO_CARD"),
            "Deck with no >=2 cost cards locks Overclock with OVERCLOCK_NO_CARD");

        // Case 2: Rewire Pair (-8 HP, pick 2 non-curse -> same class, same rarity, upgraded)
        RunState run2 = SetupTestRun("sb-rewire");
        Player p2 = run2.Players[0];
        CardModel commonCard = run2.CreateCard<PommelStrike>(p2);
        CardModel rareCard = run2.CreateCard<DemonForm>(p2);
        CardModel curse2 = run2.CreateCard<Clumsy>(p2);
        CardModel wax2 = run2.CreateCard<ThingsStickyBeeswax>(p2);
        await CardPileCmd.Add([commonCard, rareCard, curse2, wax2], PileType.Deck);
        int hpBefore2 = p2.Creature.CurrentHp;
        var ev2 = BeginEventForPlayer<ThingsSpliceBooth>(p2, run2);

        // Cancel preserves HP
        using (ProbeRuntimeApi.UseSelector(new ProbeCardSelector(-1)))
        {
            await ev2.CurrentOptions[1].Chosen();
        }
        Assert(!ev2.IsFinished && p2.Creature.CurrentHp == hpBefore2,
            "Canceling Rewire preserves HP and keeps event open");

        // Select exact PommelStrike (Common) + DemonForm (Rare); verify Curse/Status excluded from selector list
        var rewireSelector = new ProbeMultiCardSelector([commonCard, rareCard]);
        using (ProbeRuntimeApi.UseSelector(rewireSelector))
        {
            await ev2.CurrentOptions[1].Chosen();
        }
        Assert(!rewireSelector.LastOffered.Contains(curse2) && !rewireSelector.LastOffered.Contains(wax2),
            "Rewire excludes Curse and Status cards without class pool replacements");
        Assert(ev2.IsFinished && p2.Creature.CurrentHp == hpBefore2 - ThingsSpliceBooth.RewireDamage,
            "Rewire deals 8 HP damage and finishes event");
        Assert(!p2.Deck.Cards.Contains(commonCard) && !p2.Deck.Cards.Contains(rareCard),
            "Original Common and Rare cards removed by Transform");
        // Last two added cards (transformed replacements sit in Deck)
        List<CardModel> newRare = p2.Deck.Cards.Where(c => c.Rarity == CardRarity.Rare && c.Pool == p2.Character.CardPool).ToList();
        Assert(newRare.Count == 1 && newRare[0].Id != rareCard.Id && newRare[0].IsUpgraded,
            "Rare Rewire replacement is different same-class Upgraded Rare card");
        Assert(p2.Deck.Cards.Any(c => c.Rarity == CardRarity.Common && c.Pool == p2.Character.CardPool && c.Id != commonCard.Id && c.IsUpgraded),
            "Common Rewire replacement is different same-class Upgraded Common card");

        // Lethal 8 damage & <2 eligible cards lock
        RunState run2Thin = SetupTestRun("sb-thin");
        Player p2Thin = run2Thin.Players[0];
        foreach (CardModel c in p2Thin.Deck.Cards.Skip(1).ToList())
            await CardPileCmd.RemoveFromDeck(c);
        var ev2Thin = BeginEventForPlayer<ThingsSpliceBooth>(p2Thin, run2Thin);
        Assert(ev2Thin.CurrentOptions[1].IsLocked && ev2Thin.CurrentOptions[1].Title.LocEntryKey.Contains("REWIRE_NO_CARDS"),
            "<2 eligible cards locks Rewire with REWIRE_NO_CARDS");

        // Case 3: Salvage random upgraded card -> Downgrade + 85 Gold; Leave when no upgraded cards
        RunState run3 = SetupTestRun("sb-salvage");
        Player p3 = run3.Players[0];
        CardModel upgradedStrike = p3.Deck.Cards.First(c => c is StrikeIronclad);
        CardCmd.Upgrade(upgradedStrike);
        int goldBefore3 = p3.Gold;
        var ev3 = BeginEventForPlayer<ThingsSpliceBooth>(p3, run3);
        Assert(ev3.CurrentOptions[2].Title.LocEntryKey.Contains("SALVAGE"),
            "Upgraded card in deck shows SALVAGE option 2");
        await ev3.CurrentOptions[2].Chosen();
        Assert(ev3.IsFinished && !upgradedStrike.IsUpgraded && p3.Gold == goldBefore3 + ThingsSpliceBooth.DowngradeGold,
            "Salvage downgrades upgraded card and grants 85 Gold");

        RunState runLeave = SetupTestRun("sb-leave");
        Player pLeave = runLeave.Players[0];
        int leaveGoldBefore = pLeave.Gold;
        var evLeave = BeginEventForPlayer<ThingsSpliceBooth>(pLeave, runLeave);
        Assert(evLeave.CurrentOptions[2].Title.LocEntryKey.Contains("LEAVE"),
            "Deck with no upgraded cards shows LEAVE option 2");
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Gold == leaveGoldBefore,
            "LEAVE finishes cleanly without changes");
    }

    private async Task RenderLiveUiAndAssetsAsync()
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

        foreach (string lang in new[] { "zhs", "eng" })
        foreach (bool isBooth in new[] { false, true })
        {
            RunState run = SetupTestRun($"render-{lang}-{isBooth}");
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            EventModel model = isBooth
                ? BeginEventForPlayer<ThingsSpliceBooth>(run.Players[0], run)
                : BeginEventForPlayer<ThingsWarmRoyalCell>(run.Players[0], run);

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

            await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Assert(layout.OptionButtons.Count() == 3, $"Native layout renders 3 options for {(isBooth ? "SpliceBooth" : "WarmRoyalCell")}");

            using Image img = eventView.GetTexture().GetImage();
            string pngName = $"native-event-{(isBooth ? "splice_booth" : "warm_royal_cell")}-{lang}.png";
            Assert(img.SavePng(Path.Combine(output, pngName)) == Error.Ok, $"Save {pngName}");

            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        eventView.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async Task DispatchAfterSideTurnEndHook(
        CombatState combatState,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        System.Reflection.MethodInfo dispatcher = typeof(Hook).GetMethod(
                "AfterSideTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(Hook).GetMethod(
                "AfterTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(Hook).FullName, "AfterSideTurnEnd/AfterTurnEnd");
        Task task = (Task)(dispatcher.Invoke(null, [combatState, side, participants])
            ?? throw new InvalidOperationException($"{dispatcher.Name} returned null."));
        await task;
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

    private sealed class ProbeMultiCardSelector(IReadOnlyList<CardModel> targets) : ICardSelector
    {
        public List<CardModel> LastOffered { get; } = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            LastOffered.Clear();
            LastOffered.AddRange(options);
            return Task.FromResult<IEnumerable<CardModel>>(targets.Where(LastOffered.Contains).ToArray());
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            new() { card = options.FirstOrDefault()?.Card };
    }
}
