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
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
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
            await VerifyLargeStatsMovesAndMultiplayerScaling();
            await VerifyAsymmetricHalfHpSplitsAndInheritedHp();
            await VerifyLethalBypassAndEdgeCases();
            await VerifyMediumSameColorSplitsAndSimultaneousEnemyTurn();

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

    private static async Task RunEnemyTurnAndPrepareNextPlayerTurn(Fixture s)
    {
        s.Room.CombatState.CurrentSide = CombatSide.Enemy;
        var actors = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        foreach (Creature enemy in actors)
        {
            if (s.Room.CombatState.ContainsCreature(enemy) && enemy.IsAlive)
                await enemy.Monster!.PerformMove();
        }
        s.Room.CombatState.CurrentSide = CombatSide.Player;
        foreach (Creature enemy in s.Room.CombatState.Enemies.Where(c => c.IsAlive))
            enemy.PrepareForNextTurn(s.Players.Select(p => p.Creature));
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

        var guardType = typeof(ThingsChromaticSlimeWeak).Assembly.GetType("STS2_Things.Hooks.ThreadedScenePreloadGuardPatch");
        Assert(guardType != null, "ThreadedScenePreloadGuardPatch type exists.");
        var shouldSerialize = guardType!.GetMethod("ShouldSerializeScene", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert((bool)shouldSerialize.Invoke(null, ["res://scenes/creature_visuals/chromatic_slime_medium.tscn"])!,
            "Preload guard serializes chromatic_slime_medium.tscn.");
        Assert((bool)shouldSerialize.Invoke(null, ["res://scenes/backgrounds/things_chromatic_slime_weak/things_chromatic_slime_weak_background.tscn"])!,
            "Preload guard serializes Chromatic Slime background scene.");
        Assert(!(bool)shouldSerialize.Invoke(null, ["res://scenes/creature_visuals/ironclad.tscn"])!,
            "Preload guard leaves vanilla creature scenes untouched.");

        var power = ModelDb.Power<ThingsChromaticFissionPower>();
        Assert(!power.ShouldScaleInMultiplayer, "Chromatic Fission threshold must NOT double-scale in multiplayer.");
        foreach (string lang in new[] { "eng", "zhs" })
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            foreach (string key in new[]
            {
                "THINGS_CHROMATIC_FISSION_POWER.largeDescription",
                "THINGS_CHROMATIC_FISSION_POWER.mediumDescription",
                "THINGS_CHROMATIC_FISSION_POWER.pendingSplitDescription",
                "THINGS_CHROMATIC_FISSION_POWER.pendingMediumSplitDescription",
            })
            {
                Assert(LocString.Exists("powers", key), $"Missing power key {key} ({lang}).");
            }
            foreach (string splitMoveKey in new[]
            {
                "THINGS_CHROMATIC_SLIME_LARGE.moves.SPLIT.title",
                "THINGS_CHROMATIC_SLIME_MEDIUM_CRIMSON.moves.SPLIT.title",
                "THINGS_CHROMATIC_SLIME_MEDIUM_AMBER.moves.SPLIT.title",
                "THINGS_CHROMATIC_SLIME_MEDIUM_AZURE.moves.SPLIT.title",
            })
            {
                Assert(LocString.Exists("monsters", splitMoveKey), $"Missing monster move key {splitMoveKey} ({lang}).");
            }
        }

        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        Assert(ModelDb.Monster<ThingsChromaticSlimeLarge>().Title.GetFormattedText() == "Chromatic Slime (L)", "ENG large title.");
        Assert(power.Title.GetFormattedText() == "Chromatic Fission", "ENG power title.");
        Assert(power.Description.GetFormattedText().Contains("Crimson"), "ENG power description lists Crimson.");
        Assert(power.HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("BLOCK", StringComparison.OrdinalIgnoreCase)),
            "Power includes Block hover tip.");
        Assert(power.HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("WEAK", StringComparison.OrdinalIgnoreCase)),
            "Power includes Weak hover tip.");

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
        Assert(ModelDb.Monster<ThingsChromaticSlimeLarge>().Title.GetFormattedText() == "异彩史莱姆（大）", "ZHS large title.");
        Assert(power.Title.GetFormattedText() == "异彩裂变", "ZHS power title.");
        Assert(power.Description.GetFormattedText().Contains("炽红"), "ZHS power description lists 炽红.");

        SaveManager.Instance.SettingsSave.Language = "eng";
        LocManager.Initialize();
        GD.Print("PASS Depths regular pool, 7-slot layout, ZHS/ENG localization, and visual scene resources.");
    }

    private static async Task VerifyLargeStatsMovesAndMultiplayerScaling()
    {
        // 1P A0 stats + original 3-move prismatic cycle (Prism Splash -> Refractive Gel -> Chromatic Slam)
        var s = await Scenario(players: 1, ascension: 0);
        Assert(s.Large.MinInitialHp == 112 && s.Large.MaxInitialHp == 120,
            $"Large A0 HP is 112..120 (got {s.Large.MinInitialHp}..{s.Large.MaxInitialHp}).");
        Assert(s.Large.Creature.HasPower<ThingsChromaticFissionPower>(), "Large starts combat with Chromatic Fission.");
        var fission = s.Large.Creature.GetPower<ThingsChromaticFissionPower>()!;
        Assert(fission.Amount == s.Large.Creature.MaxHp / 2,
            $"Chromatic Fission threshold equals MaxHp / 2 ({s.Large.Creature.MaxHp / 2}, got {fission.Amount}).");
        Assert(fission.DisplayAmount == fission.Amount, "DisplayAmount shows threshold before split triggers.");
        Assert(s.Large.Creature.SlotName == "large", "Large occupies 'large' slot.");

        string[] cycle = ["PRISM_SPLASH_MOVE", "REFRACTIVE_GEL_MOVE", "CHROMATIC_SLAM_MOVE"];
        for (int turn = 0; turn < 6; turn++)
        {
            if (turn > 0)
            {
                s.Large.MoveStateMachine!.OnMovePerformed(s.Large.NextMove);
                s.Large.RollMove([s.Player.Creature]);
            }
            Assert(s.Large.NextMove.Id == cycle[turn % 3], $"Large deterministic cycle turn {turn}: {s.Large.NextMove.Id}");
        }

        // Execute all 3 moves in live turn progression and check Slimed / Block / Strength
        var sExec = await Scenario(players: 1, ascension: 0);
        await CreatureCmd.SetMaxHp(sExec.Player.Creature, 200);
        await CreatureCmd.Heal(sExec.Player.Creature, 200);
        Assert(sExec.Large.NextMove.Id == "PRISM_SPLASH_MOVE", "Opens with PRISM_SPLASH_MOVE.");
        await RunEnemyTurnAndPrepareNextPlayerTurn(sExec);
        int slimedAfterT1 = sExec.Player.PlayerCombatState!.DiscardPile.Cards.Count(c => c is Slimed);
        Assert(slimedAfterT1 == 2, $"PRISM_SPLASH_MOVE adds 2 Slimed to Discard (got {slimedAfterT1}).");
        Assert(sExec.Large.NextMove.Id == "REFRACTIVE_GEL_MOVE", "T2 rolls REFRACTIVE_GEL_MOVE.");

        await RunEnemyTurnAndPrepareNextPlayerTurn(sExec);
        Assert(sExec.Large.Creature.Block == 12, $"REFRACTIVE_GEL_MOVE grants 12 Block at A0 (got {sExec.Large.Creature.Block}).");
        Assert(sExec.Large.Creature.GetPowerAmount<StrengthPower>() == 2,
            $"REFRACTIVE_GEL_MOVE grants +2 Strength at A0 (got {sExec.Large.Creature.GetPowerAmount<StrengthPower>()}).");
        Assert(sExec.Large.NextMove.Id == "CHROMATIC_SLAM_MOVE", "T3 rolls CHROMATIC_SLAM_MOVE.");

        int hpBeforeSlam = sExec.Player.Creature.CurrentHp;
        await RunEnemyTurnAndPrepareNextPlayerTurn(sExec);
        Assert(hpBeforeSlam - sExec.Player.Creature.CurrentHp == 26,
            $"CHROMATIC_SLAM_MOVE deals 24 + 2 Str = 26 dmg at A0 (got {hpBeforeSlam - sExec.Player.Creature.CurrentHp}).");
        int slimedAfterT3 = sExec.Player.PlayerCombatState!.DiscardPile.Cards.Count(c => c is Slimed);
        Assert(slimedAfterT3 == 3, $"CHROMATIC_SLAM_MOVE adds 1 more Slimed (total 3, got {slimedAfterT3}).");

        // A20 Tough/Deadly stats + 2P & 4P multiplayer threshold sanity (no double-scaling!)
        var sA20 = await Scenario(players: 1, ascension: 20);
        Assert(sA20.Large.MinInitialHp == 120 && sA20.Large.MaxInitialHp == 128, "Large A20 HP is 120..128.");

        foreach (int pCount in new[] { 2, 4 })
        {
            var sMp = await Scenario(players: pCount, ascension: 0);
            var mpFission = sMp.Large.Creature.GetPower<ThingsChromaticFissionPower>()!;
            Assert(mpFission.Amount == sMp.Large.Creature.MaxHp / 2,
                $"{pCount}P split threshold must equal scaled MaxHp/2 ({sMp.Large.Creature.MaxHp / 2}, got {mpFission.Amount}).");
            Assert(mpFission.Amount < sMp.Large.Creature.MaxHp, $"{pCount}P threshold is strictly below MaxHp.");
        }

        GD.Print("PASS Large stats, original 3-move prismatic cycle, and 1P/2P/4P threshold scaling.");
    }

    private static async Task VerifyAsymmetricHalfHpSplitsAndInheritedHp()
    {
        // 1. Powered Attack pushing >= 5 past half-HP (exact boundary excess == 5) -> Crimson + Azure
        {
            var s = await Scenario(players: 1, ascension: 0);
            var large = s.Large.Creature;
            var fission = large.GetPower<ThingsChromaticFissionPower>()!;
            int threshold = fission.Amount;

            // Chip down to threshold + 2 first (must NOT trigger split yet)
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - (threshold + 2));
            Assert(!GetIsSplitTriggered(fission), "Staying above 50% HP does NOT trigger Split.");
            Assert(s.Large.NextMove.Id == "PRISM_SPLASH_MOVE", "Intent stays PRISM_SPLASH_MOVE above 50%.");

            // Cross with 7 damage -> lands at threshold - 5 (exact excess == 5)
            await AttackCardHit(MakeOwnedAttack(s.Player), large, 7);
            Assert(large.CurrentHp == threshold - 5, "Large HP reached threshold - 5.");
            Assert(GetIsSplitTriggered(fission), "Crossing <= 50% immediately arms Split.");
            Assert(GetPrimaryStrain(fission) == ChromaticStrain.Crimson && GetSecondaryStrain(fission) == ChromaticStrain.Azure,
                $"Attack excess == 5 locks Crimson + Azure (got {GetPrimaryStrain(fission)} + {GetSecondaryStrain(fission)}).");
            Assert(s.Large.NextMove.Id == "SPLIT_MOVE", "Intent immediately interrupts to SPLIT_MOVE.");
            Assert(s.Large.NextMove.Intents.Single() is SummonIntent, "SPLIT_MOVE shows SummonIntent.");
            Assert(fission.DisplayAmount == large.CurrentHp, "DisplayAmount tracks live remaining HP once armed.");
            string pendingText = fission.HoverTips.OfType<HoverTip>().First().Description;
            Assert(pendingText.Contains("Crimson Slime (M)") && pendingText.Contains("Azure Slime (M)") && pendingText.Contains(large.CurrentHp.ToString()),
                $"Pending split tooltip previews locked pair and inherited HP: {pendingText}");

            // Additional chip damage BEFORE Enemy Turn updates inherited HP live without changing locked colors
            await CreatureCmd.Damage(Choice, large, 8, ValueProp.Unpowered, s.Player.Creature);
            int expectedSplitHp = large.CurrentHp;
            Assert(fission.DisplayAmount == expectedSplitHp, "Additional damage updates live DisplayAmount.");
            Assert(GetPrimaryStrain(fission) == ChromaticStrain.Crimson && GetSecondaryStrain(fission) == ChromaticStrain.Azure,
                "Locked color pair does NOT mutate on follow-up hits.");

            // Stun must NOT overwrite armed SPLIT_MOVE
            await CreatureCmd.Stun(large);
            Assert(s.Large.NextMove.Id == "SPLIT_MOVE", "Armed SPLIT_MOVE cannot be overwritten by Stun.");

            // Enemy Turn executes SPLIT_MOVE -> spawns 2 asymmetric Mediums inheriting expectedSplitHp
            await RunEnemyTurnAndPrepareNextPlayerTurn(s);
            Assert(!s.Room.CombatState.ContainsCreature(large), "Parent Large cleanly removed after splitting.");
            Assert(!s.Room.CombatState.EscapedCreatures.Any(), "Splitting must NOT record parent as escaped (zero gold loss).");

            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2, $"Large split spawns 2 Mediums (got {meds.Count}).");
            Creature left = meds.Single(c => c.SlotName == "med_1");
            Creature right = meds.Single(c => c.SlotName == "med_2");
            Assert(left.Monster is ThingsChromaticSlimeMediumCrimson, "med_1 is Primary Medium Crimson.");
            Assert(right.Monster is ThingsChromaticSlimeMediumAzure, "med_2 is Secondary Medium Azure.");
            Assert(left.MaxHp == expectedSplitHp && left.CurrentHp == expectedSplitHp,
                $"Left medium inherits exact remaining HP ({expectedSplitHp}, got {left.CurrentHp}/{left.MaxHp}).");
            Assert(right.MaxHp == expectedSplitHp && right.CurrentHp == expectedSplitHp,
                $"Right medium inherits exact remaining HP ({expectedSplitHp}, got {right.CurrentHp}/{right.MaxHp}).");
            Assert(left.GetPower<ThingsChromaticFissionPower>()?.Amount == expectedSplitHp / 2,
                $"Medium Crimson carries half-HP fission threshold ({expectedSplitHp / 2}).");
            Assert(right.GetPower<ThingsChromaticFissionPower>()?.Amount == expectedSplitHp / 2,
                $"Medium Azure carries half-HP fission threshold ({expectedSplitHp / 2}).");
            Assert(left.Monster!.NextMove.Id == "CRIMSON_REND_MOVE" && right.Monster!.NextMove.Id == "TIDE_MIST_MOVE",
                "Asymmetric Medium pair staggers opening moves.");
        }

        // 2a. Powered Attack pushing < 5 past threshold (excess == 4) -> Amber + Crimson
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            int threshold = large.GetPower<ThingsChromaticFissionPower>()!.Amount;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - (threshold - 4));
            int splitHp = large.CurrentHp;
            Assert(s.Large.NextMove.Id == "SPLIT_MOVE", "Excess == 4 interrupts into SPLIT_MOVE.");
            await RunEnemyTurnAndPrepareNextPlayerTurn(s);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Single(c => c.SlotName == "med_1").Monster is ThingsChromaticSlimeMediumAmber,
                "Attack excess == 4 spawns Primary Medium Amber in med_1.");
            Assert(meds.Single(c => c.SlotName == "med_1").Block == 8, "Medium Amber enters with 8 Opening Block.");
            Assert(meds.Single(c => c.SlotName == "med_2").Monster is ThingsChromaticSlimeMediumCrimson,
                "Attack excess == 4 spawns Secondary Medium Crimson in med_2.");
            Assert(meds.All(c => c.MaxHp == splitHp && c.CurrentHp == splitHp), "Both inherit exact remaining HP.");
        }

        // 2b. Powered Attack landing EXACTLY on threshold (excess == 0) -> Amber + Crimson
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            int threshold = large.GetPower<ThingsChromaticFissionPower>()!.Amount;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - threshold);
            var fission = large.GetPower<ThingsChromaticFissionPower>()!;
            Assert(GetIsSplitTriggered(fission) && GetPrimaryStrain(fission) == ChromaticStrain.Amber && GetSecondaryStrain(fission) == ChromaticStrain.Crimson,
                "Exact half-HP Attack hit (excess == 0) locks Amber + Crimson.");
        }

        // 3a. Skill card crossing half-HP (even deep excess == 20) -> Azure + Amber
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            int threshold = large.GetPower<ThingsChromaticFissionPower>()!.Amount;
            await AttackCardHit(MakeOwnedSkill(s.Player), large, large.CurrentHp - (threshold - 20));
            await RunEnemyTurnAndPrepareNextPlayerTurn(s);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Single(c => c.SlotName == "med_1").Monster is ThingsChromaticSlimeMediumAzure,
                "Skill card crossing spawns Primary Medium Azure.");
            Assert(meds.Single(c => c.SlotName == "med_2").Monster is ThingsChromaticSlimeMediumAmber,
                "Skill card crossing spawns Secondary Medium Amber.");
        }

        // 3b. Unpowered / Poison damage crossing an ALREADY-stunned Large -> overrides Stun & spawns Azure + Amber
        {
            var s = await Scenario(players: 2, ascension: 0);
            var large = s.Large.Creature;
            int threshold = large.GetPower<ThingsChromaticFissionPower>()!.Amount;
            await CreatureCmd.Stun(large);
            Assert(s.Large.NextMove.Id == "STUNNED", "Large initially stunned.");
            await CreatureCmd.Damage(Choice, large, large.CurrentHp - (threshold - 10), ValueProp.Unpowered, s.Player.Creature);
            int expectedMpSplitHp = large.CurrentHp;
            Assert(s.Large.NextMove.Id == "SPLIT_MOVE", "Half-HP crossing overrides Stun into SPLIT_MOVE.");
            await RunEnemyTurnAndPrepareNextPlayerTurn(s);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Single(c => c.SlotName == "med_1").Monster is ThingsChromaticSlimeMediumAzure,
                "2P Unpowered crossing spawns Medium Azure.");
            Assert(meds.Single(c => c.SlotName == "med_2").Monster is ThingsChromaticSlimeMediumAmber,
                "2P Unpowered crossing spawns Medium Amber.");
            Assert(meds.All(c => c.MaxHp == expectedMpSplitHp && c.CurrentHp == expectedMpSplitHp),
                $"2P Mediums inherit exact remaining HP without double-scaling ({expectedMpSplitHp}).");
        }

        GD.Print("PASS all asymmetric half-HP split pairings (Crimson+Azure, Amber+Crimson, Azure+Amber), live HP preview, Stun rules, and 2P HP inheritance.");
    }

    private static async Task VerifyLethalBypassAndEdgeCases()
    {
        // 1. High-burst lethal from > 50% HP straight to 0 -> skips Split completely!
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp + 20);
            Assert(!large.IsAlive, "Large killed in one burst.");
            Assert(s.Room.CombatState.Enemies.All(c => !c.IsAlive),
                "1-shot lethal kill from >50% skips Split cleanly (0 Mediums spawned).");
        }

        // 2. Finishing off an already-armed Large BEFORE Enemy Turn -> skips Split completely!
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            int threshold = large.GetPower<ThingsChromaticFissionPower>()!.Amount;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - (threshold - 10));
            Assert(s.Large.NextMove.Id == "SPLIT_MOVE", "Large armed for Split.");
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp + 5);
            Assert(!large.IsAlive && s.Room.CombatState.Enemies.All(c => !c.IsAlive),
                "Killing armed Large before Enemy Turn prevents Split cleanly.");
        }

        // 3. 1-HP split edge case -> 1/1 HP Mediums do NOT carry dead 0-threshold fission power
        {
            var s = await Scenario();
            var large = s.Large.Creature;
            await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - 1);
            Assert(large.CurrentHp == 1 && s.Large.NextMove.Id == "SPLIT_MOVE", "1-HP Large arms SPLIT_MOVE.");
            await RunEnemyTurnAndPrepareNextPlayerTurn(s);
            var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
            Assert(meds.Count == 2 && meds.All(c => c.MaxHp == 1 && c.CurrentHp == 1),
                "1-HP Large splits into two 1/1 HP Mediums.");
            Assert(meds.All(c => !c.HasPower<ThingsChromaticFissionPower>()),
                "1-HP Mediums omit Chromatic Fission cleanly (splitHp < 2).");
        }

        GD.Print("PASS lethal 1-shot bypass, pre-turn armed-split kill, and 1-HP split edge case.");
    }

    private static async Task VerifyMediumSameColorSplitsAndSimultaneousEnemyTurn()
    {
        var s = await Scenario();
        var large = s.Large.Creature;
        // Drop Large to 40 HP via deep Attack -> Crimson (med_1) + Azure (med_2) at 40/40 HP (threshold 20)
        await AttackCardHit(MakeOwnedAttack(s.Player), large, large.CurrentHp - 40);
        await RunEnemyTurnAndPrepareNextPlayerTurn(s);

        var meds = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Creature medLeft = meds.Single(c => c.SlotName == "med_1");
        Creature medRight = meds.Single(c => c.SlotName == "med_2");
        Assert(medLeft.MaxHp == 40 && medRight.MaxHp == 40, "Both Mediums inherited 40 HP.");

        // Push BOTH Mediums <= 20 HP in the same Player Turn (Crimson to 14 HP via Poison, Azure to 12 HP via Attack)
        // Mediums MUST split into their OWN color regardless of hit source!
        await CreatureCmd.Damage(Choice, medLeft, 26, ValueProp.Unpowered, s.Player.Creature);
        await AttackCardHit(MakeOwnedAttack(s.Player), medRight, 28);
        Assert(medLeft.CurrentHp == 14 && medLeft.Monster!.NextMove.Id == "SPLIT_MOVE",
            "Medium Crimson at 14 HP immediately arms SPLIT_MOVE.");
        Assert(medRight.CurrentHp == 12 && medRight.Monster!.NextMove.Id == "SPLIT_MOVE",
            "Medium Azure at 12 HP immediately arms SPLIT_MOVE.");

        // Check medium pending split descriptions
        string leftTip = medLeft.GetPower<ThingsChromaticFissionPower>()!.HoverTips.OfType<HoverTip>().First().Description;
        Assert(leftTip.Contains("Crimson Slime (S)") && leftTip.Contains("14"),
            $"Medium Crimson pending tooltip previews 2x Small Crimson at 14 HP: {leftTip}");

        // Simultaneous Enemy Turn splits both Mediums into 4 same-color Smalls across small_1..small_4
        await RunEnemyTurnAndPrepareNextPlayerTurn(s);
        var smalls = s.Room.CombatState.Enemies.Where(c => c.IsAlive).ToList();
        Assert(smalls.Count == 4, $"Simultaneous Medium splits yield 4 Smalls (got {smalls.Count}).");
        Assert(smalls.Select(c => c.SlotName).OrderBy(n => n).SequenceEqual(["small_1", "small_2", "small_3", "small_4"]),
            "All 4 Smalls occupy small_1..small_4 without slot collisions.");

        var leftSmalls = smalls.Where(c => c.SlotName is "small_1" or "small_2").ToList();
        var rightSmalls = smalls.Where(c => c.SlotName is "small_3" or "small_4").ToList();
        Assert(leftSmalls.All(c => c.Monster is ThingsChromaticSlimeSmallCrimson && c.MaxHp == 14 && c.CurrentHp == 14),
            "Medium Crimson splits into 2x Small Crimson inheriting 14/14 HP (own color even when crossed by Poison).");
        Assert(rightSmalls.All(c => c.Monster is ThingsChromaticSlimeSmallAzure && c.MaxHp == 12 && c.CurrentHp == 12),
            "Medium Azure splits into 2x Small Azure inheriting 12/12 HP (own color even when crossed by Attack).");
        Assert(smalls.All(c => !c.HasPower<ThingsChromaticFissionPower>()),
            "Terminal Smalls never carry Chromatic Fission.");
        Assert(!s.Room.CombatState.EscapedCreatures.Any(), "Zero escaped-creature gold loss across full 1->2->4 chain.");

        // Killing all 4 Smalls ends combat cleanly with no further splits
        foreach (Creature sm in smalls)
            await CreatureCmd.Damage(Choice, sm, sm.Block + sm.CurrentHp + 10, ValueProp.Unpowered, s.Player.Creature);
        Assert(s.Room.CombatState.Enemies.All(c => !c.IsAlive), "All 4 Smalls cleanly defeated.");

        // Also verify bursting a Medium cleanly from >50% to 0 skips its Small split!
        var s2 = await Scenario();
        await AttackCardHit(MakeOwnedAttack(s2.Player), s2.Large.Creature, s2.Large.Creature.CurrentHp - 36);
        await RunEnemyTurnAndPrepareNextPlayerTurn(s2);
        var burstTarget = s2.Room.CombatState.Enemies.First(c => c.IsAlive);
        await AttackCardHit(MakeOwnedAttack(s2.Player), burstTarget, burstTarget.CurrentHp + 20);
        Assert(s2.Room.CombatState.Enemies.Count(c => c.IsAlive) == 1,
            "Bursting one Medium from full HP to 0 skips its Small split cleanly.");

        GD.Print("PASS Medium same-color half-HP splits, simultaneous 4-Small Enemy Turn, Medium 1-shot bypass, and terminal cleanup.");
    }

    private static bool GetIsSplitTriggered(ThingsChromaticFissionPower power) =>
        (bool)typeof(ThingsChromaticFissionPower).GetProperty("IsSplitTriggered", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(power)!;

    private static ChromaticStrain? GetPrimaryStrain(ThingsChromaticFissionPower power) =>
        (ChromaticStrain?)typeof(ThingsChromaticFissionPower).GetProperty("PrimaryStrain", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(power);

    private static ChromaticStrain? GetSecondaryStrain(ThingsChromaticFissionPower power) =>
        (ChromaticStrain?)typeof(ThingsChromaticFissionPower).GetProperty("SecondaryStrain", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(power);

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
