using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
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
using STS2_Things.Platform;
using STS2_Things.Powers;

public partial class ScalePeelerProbeNode : Node
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
            ProjectSettings.LoadResourcePack("D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck");
#if STS2_V111_1
            const string defaultTarget = "v111.1";
#else
            const string defaultTarget = "v113.1";
#endif
            string pack = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")
                ?? new[]
                {
                    Path.Combine(_root, $"build/{defaultTarget}/STS2_Things.pck"),
                    Path.Combine(_root, "build/v113.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/v111.1/STS2_Things.pck"),
                    Path.Combine(_root, "build/unified/STS2_Things.pck"),
                }.FirstOrDefault(File.Exists)
                ?? Path.Combine(_root, $"build/{defaultTarget}/STS2_Things.pck");
            Assert(File.Exists(pack), $"Shipping mod PCK exists at {pack}.");
            Assert(ProjectSettings.LoadResourcePack(pack), "Shipping mod PCK mounts cleanly.");

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            LocManager.Initialize();
            InitializeModelDb();

            VerifyPoolsAndLocalization();
            await VerifyOpeningAndMoveCycle();
            await VerifyArmorSiphonAndRetention();
            await VerifyPlatedSlamAndCoop();
            if (OS.GetCmdlineUserArgs().Contains("--visual"))
                await RenderProbe();

            DeactivateSyntheticCombat();
            GD.Print($"Scale Peeler probe: PASS ({_checks} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static async Task<Fixture> Scenario(bool weak = true, int players = 1, int ascension = 0)
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
            $"scale-peeler-{(weak ? "weak" : "strong")}-p{players}-a{ascension}");
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!
            .SetValue(RunManager.Instance, ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!
            .SetValue(RunManager.Instance, new NetSingleplayerGameService());

        EncounterModel encounter = (weak
            ? ModelDb.Encounter<ThingsScalePeelerWeak>() as EncounterModel
            : ModelDb.Encounter<ThingsScalePeelerEncounter>()).ToMutable();
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

    private static void VerifyPoolsAndLocalization()
    {
        var depths = ModelDb.Act<ThingsDepths>();
        var weakEncounters = depths.AllWeakEncounters.ToArray();
        var regularEncounters = depths.AllRegularEncounters.ToArray();
        Assert(weakEncounters.Any(e => e is ThingsScalePeelerWeak),
            "ThingsScalePeelerWeak is registered in Depths weak encounter pool.");
        Assert(regularEncounters.Any(e => e is ThingsScalePeelerEncounter),
            "ThingsScalePeelerEncounter is registered in Depths regular encounter pool.");

        var weak = ModelDb.Encounter<ThingsScalePeelerWeak>();
        var strong = ModelDb.Encounter<ThingsScalePeelerEncounter>();
        Assert(weak.IsWeak && !strong.IsWeak, "Weak/normal encounter flags match specification.");
        Assert(weak.Slots.SequenceEqual(["peeler"]), "Weak encounter has single 'peeler' slot.");
        Assert(strong.Slots.SequenceEqual(["peeler", "rock", "slime"]),
            "Strong encounter has ['peeler', 'rock', 'slime'] slots.");

        var monster = ModelDb.Monster<ThingsScalePeeler>();
        var power = ModelDb.Power<ThingsArmorSiphonPower>();

        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        Assert(monster.Title.GetFormattedText() == "Scale-Peeler Wyrm", "English monster title.");
        Assert(power.Title.GetFormattedText() == "Armor Siphon", "English power title.");
        Assert(power.Description.GetFormattedText().Contains("Block"), "English power description.");
        Assert(power.HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("BLOCK", StringComparison.OrdinalIgnoreCase)),
            "Power exposes native Block hover tip.");

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
        Assert(monster.Title.GetFormattedText() == "刮甲石蚓", "Chinese monster title.");
        Assert(power.Title.GetFormattedText() == "噬甲", "Chinese power title.");
        Assert(power.Description.GetFormattedText().Contains("格挡"), "Chinese power description.");

        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        GD.Print("PASS Depths encounter pools, slot layout, localization, and hover tips.");
    }

    private static async Task VerifyOpeningAndMoveCycle()
    {
        var a0 = await Scenario(weak: true, ascension: 0);
        Assert(a0.Peeler.MinInitialHp == 48 && a0.Peeler.MaxInitialHp == 52, "A0 HP range is 48..52.");
        Assert(a0.Peeler.OpeningBlock == 6 && a0.Peeler.Creature.Block == 6, "A0 opening Block is 6.");
        Assert(a0.Peeler.Creature.HasPower<ThingsArmorSiphonPower>(), "Wyrm starts combat with Armor Siphon.");

        string[] cycle = ["CHISEL_BITE_MOVE", "PLATED_SLAM_MOVE", "BURROW_SCRAPE_MOVE"];
        for (int turn = 0; turn < 6; turn++)
        {
            if (turn > 0)
            {
                a0.Peeler.MoveStateMachine!.OnMovePerformed(a0.Peeler.NextMove);
                a0.Peeler.RollMove([a0.Player.Creature]);
            }
            Assert(a0.Peeler.NextMove.Id == cycle[turn % 3],
                $"Deterministic 3-move cycle at turn {turn}: got {a0.Peeler.NextMove.Id}.");
        }

        var strong = await Scenario(weak: false, ascension: 0);
        Assert(strong.Encounter.MonstersWithSlots.Count == 3, "Strong encounter spawns 3 enemies.");
        Assert(strong.Encounter.MonstersWithSlots.Any(m => m.Item1 is ThingsScalePeeler && m.Item2 == "peeler"),
            "Strong encounter places Wyrm in peeler slot.");
        Assert(strong.Encounter.MonstersWithSlots.Any(m => m.Item1 is ThingsRockSnail && m.Item2 == "rock"),
            "Strong encounter places Rock Snail in rock slot.");
        Assert(strong.Encounter.MonstersWithSlots.Any(m => m.Item1 is ThingsSlimeSnail && m.Item2 == "slime"),
            "Strong encounter places Slime Snail in slime slot.");

        var a20 = await Scenario(weak: true, ascension: 20);
        Assert(a20.Peeler.MinInitialHp == 52 && a20.Peeler.MaxInitialHp == 56, "A20 HP range is 52..56.");
        Assert(a20.Peeler.OpeningBlock == 8 && a20.Peeler.Creature.Block == 8, "A20 opening Block is 8.");
        GD.Print("PASS A0/A20 opening HP/Block, innate power, trio composition, and 3-move cycle.");
    }

    private static async Task InvokeEnemySideTurnEnd(Fixture s)
    {
        s.Room.CombatState.CurrentSide = CombatSide.Enemy;
        var afterSideTurnEnd = typeof(Hook).GetMethod("AfterSideTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(Hook).GetMethod("AfterTurnEnd", BindingFlags.Public | BindingFlags.Static)!;
        await (Task)afterSideTurnEnd.Invoke(null, [s.Room.CombatState, CombatSide.Enemy, s.Enemies])!;
    }

    private static async Task VerifyArmorSiphonAndRetention()
    {
        var s = await Scenario(weak: true, players: 1, ascension: 0);
        var wyrm = s.Peeler.Creature;
        var player = s.Player.Creature;

        await CreatureCmd.GainBlock(player, 20, ValueProp.Move, null);
        Assert(player.Block == 20 && wyrm.Block == 6, "Setup Block before enemy turn.");

        // 1) Chisel Bite hits BEFORE siphon, so player's 20 Block absorbs the 11 damage leaving 9 Block.
        s.Room.CombatState.CurrentSide = CombatSide.Enemy;
        Assert(s.Peeler.NextMove.Id == "CHISEL_BITE_MOVE", "Turn 1 move is CHISEL_BITE_MOVE.");
        await s.Peeler.PerformMove();
        Assert(player.CurrentHp == player.MaxHp && player.Block == 9,
            "Player Block absorbs Chisel Bite (11) first, leaving 9 Block.");
        Assert(wyrm.Block == 12, "Chisel Bite grants Wyrm +6 Block (6 -> 12).");

        // 2) Enemy turn end siphons all 9 remaining player Block and grants it to Wyrm.
        await InvokeEnemySideTurnEnd(s);
        Assert(player.Block == 0, "Remaining player Block is stolen at end of enemy turn.");
        Assert(wyrm.Block == 21, "Wyrm gains total stolen Block (12 + 9 = 21).");

        // 3) Next enemy turn start: ClearBlock keeps Wyrm Block intact via ShouldClearBlock.
        await wyrm.AfterTurnStart(CombatSide.Enemy);
        Assert(wyrm.Block == 21, "Wyrm retains its Block when ClearBlock fires on enemy turn start.");

        // 4) Even if the player has Barricade, Armor Siphon still steals their remaining Block.
        await PowerCmd.Apply<BarricadePower>(Choice, player, 1, player, null);
        await CreatureCmd.GainBlock(player, 15, ValueProp.Move, null);
        await InvokeEnemySideTurnEnd(s);
        Assert(player.Block == 0 && wyrm.Block == 36,
            "Armor Siphon steals remaining Block even from a Barricade player.");

        // 5) Once dead in a multi-enemy combat, Wyrm stops siphoning player Block.
        var multi = await Scenario(weak: false, players: 1, ascension: 0);
        await CreatureCmd.Kill(multi.Peeler.Creature);
        await CreatureCmd.GainBlock(multi.Player.Creature, 10, ValueProp.Move, null);
        Assert(multi.Player.Creature.Block == 10, "Live combat after Wyrm death allows player to hold 10 Block.");
        await InvokeEnemySideTurnEnd(multi);
        Assert(multi.Player.Creature.Block == 10, "Dead Wyrm does not siphon player Block.");
        GD.Print("PASS enemy turn-end siphon timing, Block retention, Barricade piercing, and death guard.");
    }

    private static async Task VerifyPlatedSlamAndCoop()
    {
        // Single-player Plated Slam scaling + recoil halving.
        var s = await Scenario(weak: true, players: 1, ascension: 0);
        var wyrm = s.Peeler.Creature;
        var player = s.Player.Creature;

        await CreatureCmd.GainBlock(wyrm, 16, ValueProp.Unpowered, null); // 6 + 16 = 22
        Assert(wyrm.Block == 22 && s.Peeler.PlatedSlamDamage == 30,
            "Plated Slam scales 8 + 22 = 30 in 1P.");
        int startHp = player.CurrentHp;
        await ((MoveState)s.Peeler.MoveStateMachine!.States["PLATED_SLAM_MOVE"]).PerformMove([player]);
        Assert(player.CurrentHp == startHp - 30, "Plated Slam deals full scaled damage.");
        Assert(wyrm.Block == 11, "Plated Slam recoil halves 22 Block -> 11 remaining.");

        // Odd Block rounding: 7 Block -> lose 3 (7/2), retain 4.
        await Sts2VersionSeam.LoseBlock(Choice, wyrm, 4, wyrm);
        Assert(wyrm.Block == 7, "Setup odd 7 Block.");
        startHp = player.CurrentHp;
        await ((MoveState)s.Peeler.MoveStateMachine.States["PLATED_SLAM_MOVE"]).PerformMove([player]);
        Assert(player.CurrentHp == startHp - 15 && wyrm.Block == 4,
            "Odd Block (7): deals 15 and loses Floor(7/2)=3 leaving 4 Block.");

        // Burrow Scrape 2-hit + Defend.
        startHp = player.CurrentHp;
        await ((MoveState)s.Peeler.MoveStateMachine.States["BURROW_SCRAPE_MOVE"]).PerformMove([player]);
        Assert(player.CurrentHp == startHp - 10 && wyrm.Block == 10,
            "Burrow Scrape deals 5x2=10 damage and grants +6 Block (4 -> 10).");

        // Co-op 2-player scaling and multi-target siphon (both v111.1 and v113.1 scale <=2 players natively via Hook.ModifyBlock).
        var coop = await Scenario(weak: true, players: 2, ascension: 0);
        int expectedCoopOpening = (int)Hook.ModifyBlock(
            coop.Room.CombatState, coop.Peeler.Creature, 6m, ValueProp.Move, null, null, out _);
        Assert(expectedCoopOpening >= 12 && coop.Peeler.Creature.Block == expectedCoopOpening,
            $"2P co-op scales OpeningBlock (6, ValueProp.Move) natively to {expectedCoopOpening} (actual {coop.Peeler.Creature.Block}).");
        await CreatureCmd.GainBlock(coop.Players[0].Creature, 5, ValueProp.Move, null);
        await CreatureCmd.GainBlock(coop.Players[1].Creature, 9, ValueProp.Move, null);
        await InvokeEnemySideTurnEnd(coop);
        Assert(coop.Players.All(p => p.Creature.Block == 0), "Both co-op players have Block stolen.");
        int expectedAfterSiphon = expectedCoopOpening + 14;
        Assert(coop.Peeler.Creature.Block == expectedAfterSiphon,
            $"Wyrm sums unscaled stolen Block on top of scaled opening Block ({expectedCoopOpening} + 5 + 9 = {expectedAfterSiphon}).");

        await Sts2VersionSeam.LoseBlock(Choice, coop.Peeler.Creature, 11, coop.Peeler.Creature);
        int expectedPostLose = expectedAfterSiphon - 11;
        int expectedSlam = 8 + (int)Math.Ceiling((decimal)expectedPostLose / 2m);
        Assert(coop.Peeler.Creature.Block == expectedPostLose && coop.Peeler.PlatedSlamDamage == expectedSlam,
            $"2P co-op scales Plated Slam as 8 + Ceil({expectedPostLose}/2) = {expectedSlam}.");

        // A20 Burrow Scrape numbers.
        var a20 = await Scenario(weak: true, players: 1, ascension: 20);
        startHp = a20.Player.Creature.CurrentHp;
        await ((MoveState)a20.Peeler.MoveStateMachine!.States["BURROW_SCRAPE_MOVE"]).PerformMove([a20.Player.Creature]);
        Assert(a20.Player.Creature.CurrentHp == startHp - 12 && a20.Peeler.Creature.Block == 16,
            "A20 Burrow Scrape deals 6x2=12 and grants +8 Block (8 -> 16).");

        GD.Print("PASS Plated Slam 1P/2P scaling, odd/even Block recoil, Burrow Scrape A0/A20, and co-op siphon.");
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
        public ThingsScalePeeler Peeler => (ThingsScalePeeler)Encounter.MonstersWithSlots
            .First(m => m.Item1 is ThingsScalePeeler).Item1;
        public Creature[] Enemies => Encounter.MonstersWithSlots.Select(m => m.Item1.Creature).ToArray();
    }
}
