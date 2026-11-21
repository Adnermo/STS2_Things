using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class ChromaticSlimeProbeNode : Node
{
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    private static int _checks;
    private string _root = null!;

    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        _ = RunProbe();
    }

    private async Task RunProbe()
    {
        try
        {
            _root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            TestMode.TurnOnInternal();
            string mcpPck = Path.GetFullPath(Path.Combine(_root, "../STS2-MCP/.state/runtimes/v113.1/76cc75b6f097/game/SlayTheSpire2.pck"));
            string vanillaPck = File.Exists(mcpPck)
                ? mcpPck
                : "D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck";
            ProjectSettings.LoadResourcePack(vanillaPck);
            string pack = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")
                ?? new[]
                {
                    Path.Combine(_root, "build/v113.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/v111.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/unified/STS2_Things.pck"),
                }.FirstOrDefault(File.Exists)
                ?? Path.Combine(_root, "build/v113.1/STS2_Things.pck");
            Assert(File.Exists(pack), $"Shipping mod PCK exists at {pack}.");
            Assert(ProjectSettings.LoadResourcePack(pack), "Shipping mod PCK mounts cleanly.");

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            LocManager.Initialize();
            InitializeModelDb();

            VerifyPoolsLocalizationAndResources();
            await VerifyLargeMovesAndCycle();
            await VerifyDefeatModesOnLarge();
            await VerifyMediumSplitsAndMixedModesAndTermination();
            await VerifyAoEMultiMediumSplit();

            if (OS.GetCmdlineUserArgs().Contains("--visual"))
                await RenderVisualProbe();

            DeactivateSyntheticCombat();
            GD.Print($"Chromatic Slime probe: PASS ({_checks} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static async Task<Fixture> Scenario(int players = 1, int ascension = 0)
    {
        DeactivateSyntheticCombat();
        var party = Enumerable.Range(1, players)
            .Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i))
            .ToList();
        var run = ProbeRuntimeApi.CreateForNewRun(
            party,
            ActModel.GetDefaultList().Select(act => act.ToMutable()).ToList(),
            [],
            GameMode.Standard,
            ascension,
            $"chromatic-slime-p{players}-a{ascension}");
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!
            .SetValue(RunManager.Instance, ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!
            .SetValue(RunManager.Instance, new NetSingleplayerGameService());

        var encounter = (ThingsChromaticSlimeWeak)ModelDb.Encounter<ThingsChromaticSlimeWeak>().ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);

        foreach (var player in party)
        {
            player.ResetCombatState();
            room.CombatState.AddPlayer(player);
            player.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        }

        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            var entity = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(entity);
            monster.SetUpForCombat();
            monster.RollMove(party.Select(player => player.Creature));
        }

        room.CombatState.CurrentSide = CombatSide.Player;
        ActivateSyntheticCombat(room.CombatState);
        await Hook.BeforeCombatStart(run, room.CombatState);
        foreach (var player in party)
            await Hook.BeforeHandDraw(room.CombatState, player, Choice);
        return new Fixture(run, room, party, encounter);
    }

    private static Task AttackCardHit(CardModel card, Creature target, decimal damage)
    {
        return DamageCmd.Attack(damage).FromCard(card, null).Targeting(target).Execute(Choice);
    }

    private static CardModel MakeOwnedAttack(Player owner)
    {
        CardModel card = ModelDb.Card<StrikeIronclad>().ToMutable();
        card.Owner = owner;
        return card;
    }

    private static CardModel MakeOwnedSkill(Player owner)
    {
        CardModel card = ModelDb.Card<DefendIronclad>().ToMutable();
        card.Owner = owner;
        return card;
    }

    private static void VerifyPoolsLocalizationAndResources()
    {
        var depths = ModelDb.Act<ThingsDepths>();
        Assert(depths.AllRegularEncounters.Any(e => e is ThingsChromaticSlimeWeak),
            "ThingsChromaticSlimeWeak is registered in Depths regular encounter pool.");
        Assert(!depths.AllWeakEncounters.Any(e => e is ThingsChromaticSlimeWeak),
            "ThingsChromaticSlimeWeak is NOT in Depths weak pool.");

        var enc = ModelDb.Encounter<ThingsChromaticSlimeWeak>();
        Assert(!enc.IsWeak, "Encounter IsWeak is false.");
        Assert(enc.Slots.SequenceEqual(["small_1", "med_1", "small_2", "large", "small_3", "med_2", "small_4"]),
            "Encounter defines all 7 non-overlapping split slots.");

        MonsterModel[] monsters =
        [
            ModelDb.Monster<ThingsChromaticSlimeLarge>(),
            ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>(),
            ModelDb.Monster<ThingsChromaticSlimeMediumAmber>(),
            ModelDb.Monster<ThingsChromaticSlimeMediumAzure>(),
            ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>(),
            ModelDb.Monster<ThingsChromaticSlimeSmallAmber>(),
            ModelDb.Monster<ThingsChromaticSlimeSmallAzure>(),
        ];
        foreach (var m in monsters)
        {
            string vp = (string)typeof(MonsterModel).GetProperty("VisualsPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(m)!;
            Assert(ResourceLoader.Exists(vp), $"Visuals scene exists for {m.Id.Entry}: {vp}");
        }

        string[] distinctVisuals = monsters
            .Select(m => (string)typeof(MonsterModel).GetProperty("VisualsPath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(m)!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        Assert(
            distinctVisuals.SequenceEqual([
                "res://scenes/creature_visuals/chromatic_slime_large.tscn",
                "res://scenes/creature_visuals/chromatic_slime_medium.tscn",
                "res://scenes/creature_visuals/chromatic_slime_small.tscn",
            ]),
            $"Chromatic Slime family shares exactly 3 tier scenes (got {string.Join(", ", distinctVisuals)}).");

        // Verify AssetLoadingSession preload guard routes mod scenes through sequential _vfxScenes queue without touching vanilla scenes.
        var guardType = typeof(ThingsChromaticSlimeWeak).Assembly.GetType("STS2_Things.Hooks.ThreadedScenePreloadGuardPatch");
        Assert(guardType != null, "ThreadedScenePreloadGuardPatch type exists.");
        var shouldSerialize = guardType!.GetMethod("ShouldSerializeScene", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert((bool)shouldSerialize.Invoke(null, ["res://scenes/creature_visuals/chromatic_slime_medium.tscn"])!,
            "Preload guard serializes chromatic_slime_medium.tscn.");
        Assert((bool)shouldSerialize.Invoke(null, ["res://scenes/backgrounds/things_chromatic_slime_weak/things_chromatic_slime_weak_background.tscn"])!,
            "Preload guard serializes Chromatic Slime background scene.");
        Assert((bool)shouldSerialize.Invoke(null, ["res://scenes/backgrounds/things_robbery_fake_merchant_encounter/things_robbery_fake_merchant_encounter_background.tscn"])!,
            "Preload guard serializes Robbery Fake Merchant background scene.");
        Assert(!(bool)shouldSerialize.Invoke(null, ["res://scenes/creature_visuals/ironclad.tscn"])!,
            "Preload guard leaves vanilla creature scenes untouched.");
        Assert(!(bool)shouldSerialize.Invoke(null, ["res://images/powers/things_chromatic_fission_power.png"])!,
            "Preload guard leaves non-.tscn assets untouched.");

        var power = ModelDb.Power<ThingsChromaticFissionPower>();
        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        Assert(ModelDb.Monster<ThingsChromaticSlimeLarge>().Title.GetFormattedText() == "Chromatic Slime (L)", "ENG large title.");
        Assert(power.Title.GetFormattedText() == "Chromatic Fission", "ENG power title.");
        Assert(power.Description.GetFormattedText().Contains("Crimson"), "ENG power description lists Crimson.");
        Assert(power.HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("BLOCK", StringComparison.OrdinalIgnoreCase)),
            "Power includes Block hover tip.");

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
        Assert(ModelDb.Monster<ThingsChromaticSlimeLarge>().Title.GetFormattedText() == "异彩史莱姆（大）", "ZHS large title.");
        Assert(power.Title.GetFormattedText() == "异彩裂变", "ZHS power title.");
        Assert(power.Description.GetFormattedText().Contains("炽红"), "ZHS power description lists 炽红.");

        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        GD.Print("PASS Depths regular pool, 7-slot layout, ZHS/ENG localization, and visual scene resources.");
    }

    private static async Task VerifyLargeMovesAndCycle()
    {
        var s = await Scenario(players: 1, ascension: 0);
        Assert(s.Large.MinInitialHp == 38 && s.Large.MaxInitialHp == 42, "Large A0 HP is 38..42.");
        Assert(s.Large.Creature.HasPower<ThingsChromaticFissionPower>(), "Large starts combat with Chromatic Fission.");
        Assert(s.Large.Creature.GetPower<ThingsChromaticFissionPower>()!.Amount == 2, "Chromatic Fission shows 2 stacks.");
        Assert(s.Large.Creature.SlotName == "large", "Large occupies 'large' slot.");

        string[] cycle = ["PRISM_SPLASH_MOVE", "CHROMATIC_SLAM_MOVE", "REFRACTIVE_GEL_MOVE"];
        for (int turn = 0; turn < 6; turn++)
        {
            if (turn > 0)
            {
                s.Large.MoveStateMachine!.OnMovePerformed(s.Large.NextMove);
                s.Large.RollMove([s.Player.Creature]);
            }
            Assert(s.Large.NextMove.Id == cycle[turn % 3], $"Large deterministic cycle turn {turn}: {s.Large.NextMove.Id}");
        }
        GD.Print("PASS Large opening power & 3-move cycle.");
    }

    private static async Task VerifyDefeatModesOnLarge()
    {
        // 1. Powered Attack Overkill >= 5 (exact threshold 5) -> 2x Medium Crimson
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp + 5);
            Assert(!large.IsAlive, "Large defeated cleanly.");
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2, $"Overkill >= 5 spawns 2 mediums (got {meds.Count}).");
            Assert(meds.All(c => c.Monster is ThingsChromaticSlimeMediumCrimson), "Both mediums are Crimson.");
            Assert(meds.Select(c => c.SlotName).OrderBy(n => n).SequenceEqual(["med_1", "med_2"]), "Spawned into med_1 & med_2.");
            Assert(meds.All(c => c.HasPower<ThingsChromaticFissionPower>()), "Mediums inherit Chromatic Fission.");
            Assert(meds[0].Monster!.NextMove.Id == "CRIMSON_REND_MOVE" && meds[1].Monster!.NextMove.Id == "CRYSTAL_SURGE_MOVE",
                "Spawned Crimson mediums stagger opening moves.");
            Assert(!CombatManager.Instance.IsOverOrEnding, "ShouldStopCombatFromEnding keeps combat active after Large split.");
        }

        // 2a. Powered Attack Overkill == 4 (< 5) -> 2x Medium Amber (with 8 Opening Block)
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp + 4);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.Monster is ThingsChromaticSlimeMediumAmber),
                "Overkill == 4 spawns 2 Medium Amber.");
            Assert(meds.All(c => c.Block == 8), "Both Medium Amber gain 8 opening Block.");
        }

        // 2b. Powered Attack exact lethal Overkill == 0 -> 2x Medium Amber
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.Monster is ThingsChromaticSlimeMediumAmber),
                "Overkill == 0 spawns 2 Medium Amber.");
        }

        // 3a. Skill card damage (even huge overkill >= 5) -> 2x Medium Azure
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedSkill(s.Player), large, large.CurrentHp + 20);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.Monster is ThingsChromaticSlimeMediumAzure),
                "Non-Attack Skill card kill (even with high overkill) spawns 2 Medium Azure.");
        }

        // 3b. Unpowered / Orb / Poison damage (null cardSource) -> 2x Medium Azure
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await CreatureCmd.Damage(Choice, large, large.CurrentHp + 15, ValueProp.Unpowered, s.Player.Creature);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.Monster is ThingsChromaticSlimeMediumAzure),
                "Unpowered / Orb / Poison kill spawns 2 Medium Azure.");
        }

        // 3c. Direct Kill (Doom / Judgment bypassing DamageCmd) -> 2x Medium Azure
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await CreatureCmd.Kill(large);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.Monster is ThingsChromaticSlimeMediumAzure),
                "Direct Kill (Doom) defaults cleanly to 2 Medium Azure.");
        }

        GD.Print("PASS all defeat-mode classification rules (Crimson >=5, Amber <5 & exact 0, Azure skill/unpowered/Doom).");
    }

    private static async Task VerifyMediumSplitsAndMixedModesAndTermination()
    {
        var s = await Scenario();
        // Split Large via Poison -> 2x Medium Azure (med_1, med_2)
        await CreatureCmd.Damage(Choice, s.Large.Creature, s.Large.Creature.CurrentHp, ValueProp.Unpowered, s.Player.Creature);
        var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Assert(meds.Count == 2, "2 Azure mediums present.");
        Creature medLeft = meds.Single(c => c.SlotName == "med_1");
        Creature medRight = meds.Single(c => c.SlotName == "med_2");

        // Kill left medium via Overkill Attack -> 2x Small Crimson in small_1, small_2
        await AttackCardHit(MakeOwnedAttack(s.Player), medLeft, medLeft.CurrentHp + 7);
        var aliveAfterFirst = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Assert(aliveAfterFirst.Count == 3, "medRight + 2 Small Crimson active simultaneously.");
        var smallsCrimson = aliveAfterFirst.Where(c => c.Monster is ThingsChromaticSlimeSmallCrimson).ToList();
        Assert(smallsCrimson.Count == 2, "Left medium split into 2 Small Crimson.");
        Assert(smallsCrimson.Select(c => c.SlotName).OrderBy(x => x).SequenceEqual(["small_1", "small_2"]),
            "Left medium splits into small_1 and small_2.");
        Assert(smallsCrimson.All(c => !c.HasPower<ThingsChromaticFissionPower>()),
            "Terminal Small slimes do NOT carry Chromatic Fission.");

        // Kill right medium via Precision Attack (0 overkill) -> 2x Small Amber in small_3, small_4
        await AttackCardHit(MakeOwnedAttack(s.Player), medRight, medRight.CurrentHp);
        var allSmalls = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Assert(allSmalls.Count == 4, "4 terminal Small slimes active across small_1..small_4.");
        var smallsAmber = allSmalls.Where(c => c.Monster is ThingsChromaticSlimeSmallAmber).ToList();
        Assert(smallsAmber.Count == 2, "Right medium split into 2 Small Amber.");
        Assert(smallsAmber.All(c => c.Block == 5), "Both Small Amber start with 5 Block.");
        Assert(smallsAmber.Select(c => c.SlotName).OrderBy(x => x).SequenceEqual(["small_3", "small_4"]),
            "Right medium splits into small_3 and small_4 with zero slot overlap.");
        Assert(allSmalls.Select(c => c.SlotName).Distinct().Count() == 4, "All 4 Small slots distinct.");

        // Kill all 4 Small slimes -> none split further and zero enemies remain
        foreach (Creature sm in allSmalls)
            await CreatureCmd.Damage(Choice, sm, sm.Block + sm.CurrentHp + 20, ValueProp.Unpowered, s.Player.Creature);

        Assert(s.Room.CombatState.Enemies.All(c => !c.IsAlive), "Small slimes never split further.");
        GD.Print("PASS independent Medium splits, mixed strains across waves, and terminal Small cleanup.");
    }

    private static async Task VerifyAoEMultiMediumSplit()
    {
        var s = await Scenario();
        await AttackCardHit(MakeOwnedAttack(s.Player), s.Large.Creature, s.Large.Creature.CurrentHp + 10);
        var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToArray();
        Assert(meds.Length == 2, "Both Mediums alive before simultaneous AoE.");

        var card = MakeOwnedAttack(s.Player);
        await DamageCmd.Attack(200).FromCard(card, null).TargetingAllOpponents(s.Room.CombatState).Execute(Choice);
        var afterAoe = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Assert(afterAoe.Count == 4, $"Simultaneous AoE killing both Mediums spawns all 4 Smalls (got {afterAoe.Count}).");
        Assert(afterAoe.All(c => c.Monster is ThingsChromaticSlimeSmallCrimson), "All 4 Smalls are Crimson.");
        Assert(afterAoe.Select(c => c.SlotName).OrderBy(n => n).SequenceEqual(["small_1", "small_2", "small_3", "small_4"]),
            "Simultaneous AoE places all 4 Smalls across small_1..small_4 without slot collisions.");
        GD.Print("PASS simultaneous AoE multi-medium kill cleanly populates small_1..small_4.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        _checks++;
    }

    private sealed record Fixture(RunState Run, CombatRoom Room, List<Player> Players, EncounterModel Encounter)
    {
        public Player Player => Players[0];
        public ThingsChromaticSlimeLarge Large => (ThingsChromaticSlimeLarge)Encounter.MonstersWithSlots
            .First(m => m.Item1 is ThingsChromaticSlimeLarge).Item1;
    }
}
