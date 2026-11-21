using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Afflictions;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    // Independent specification: include the old entries as regression controls.
    private static readonly Dictionary<Type, Type[]> StrongRosterContracts = new()
    {
        [typeof(ThingsLanternFishEncounter)] = [typeof(ThingsLanternFish), typeof(ThingsLanternFish), typeof(ThingsLanternFish)],
        [typeof(ThingsSanguineLeechEncounter)] = [typeof(ThingsSanguineLeech), typeof(ThingsSanguineLeech), typeof(ThingsSanguineLeech)],
        [typeof(ThingsLeechMotherEncounter)] = [typeof(ThingsLeechMother)],
        [typeof(ThingsSilkMothTrio)] = [typeof(ThingsSilkMoth), typeof(ThingsGreatSilkMoth), typeof(ThingsSilkMoth)],
        [typeof(ThingsChromaticSlimeWeak)] = [typeof(ThingsChromaticSlimeLarge)],
        [typeof(ThingsSpongeLeechEncounter)] = [typeof(ThingsWaterSponge), typeof(ThingsSanguineLeech), typeof(ThingsSanguineLeech)],
        [typeof(ThingsLanternSpongeEncounter)] = [typeof(ThingsLanternFish), typeof(ThingsWaterSponge), typeof(ThingsLanternFish)],
        [typeof(ThingsSilkSnailEncounter)] = [typeof(ThingsSilkMoth), typeof(ThingsRockSnail), typeof(ThingsSlimeSnail)],
        [typeof(ThingsScalePeelerEncounter)] = [typeof(ThingsScalePeeler), typeof(ThingsRockSnail), typeof(ThingsSlimeSnail)],
        [typeof(ThingsCaveMawLanternEncounter)] = [typeof(ThingsCaveMaw), typeof(ThingsLanternFish)],
    };
    private static readonly Type[] NewStrongTypes = [typeof(ThingsLanternSpongeEncounter), typeof(ThingsSilkSnailEncounter),
        typeof(ThingsCaveMawLanternEncounter), typeof(ThingsScalePeelerEncounter), typeof(ThingsChromaticSlimeWeak)];

    private sealed record Battle(RunState Run, CombatRoom Room, EncounterModel Encounter)
    {
        public CombatState State => Room.CombatState;
        public IReadOnlyList<Player> Players => Run.Players;
        public Creature[] Enemies => State.Enemies.Where(c => c.IsAlive).ToArray();
        public T Monster<T>() where T : MonsterModel => Enemies.Select(c => c.Monster).OfType<T>().Single();
    }

    private static async Task<Battle> StrongBattle(EncounterModel canonical, int players = 1, int ascension = 0, string? seed = null)
    {
        DeactivateSyntheticCombat();
        var run = CreateRun(seed ?? "variety-" + canonical.Id.Entry, players, ascension);
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, NativeAscension(ascension));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, new NetSingleplayerGameService());
        var encounter = canonical.ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);
        foreach (var player in run.Players)
        {
            player.ResetCombatState();
            room.CombatState.AddPlayer(player);
            player.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        }
        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            var creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(creature);
            // CombatState.CreateCreature applies the native multiplayer HP scale.
            monster.SetUpForCombat();
            monster.RollMove(run.Players.Select(p => p.Creature));
        }
        room.CombatState.CurrentSide = CombatSide.Player;
        ActivateSyntheticCombat(room.CombatState);
        await Hook.BeforeCombatStart(run, room.CombatState);
        return new Battle(run, room, encounter);
    }

    private static Task SideEnded(Battle battle, CombatSide side, Creature[] participants)
    {
        var method = typeof(Hook).GetMethod("AfterSideTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(Hook).GetMethod("AfterTurnEnd", BindingFlags.Public | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [battle.State, side, participants])!;
    }

    private static Task SideEnding(Battle battle, CombatSide side, Creature[] participants)
    {
        var method = typeof(Hook).GetMethod("BeforeSideTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(Hook).GetMethod("BeforeTurnEnd", BindingFlags.Public | BindingFlags.Static)!;
        return (Task)method.Invoke(null, [battle.State, side, participants])!;
    }

    private static async Task EndPlayer(Battle battle, Player player)
    {
        var method = typeof(CombatManager).GetMethod("DoTurnEnd", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object?[] args = method.GetParameters().Length == 3
            ? [typeof(CombatManager).GetField("_turnState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(CombatManager.Instance), player, Choice]
            : [player, Choice];
        await (Task)method.Invoke(CombatManager.Instance, args)!;
    }

    private async Task VerifyStrongEncounters()
    {
        Type? atlasType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        ResourceFormatLoader? atlas = atlasType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
        if (atlas != null) ResourceLoader.AddResourceFormatLoader(atlas, true);
        var records = new List<object>();
        var normal = ModelDb.Act<ThingsDepths>().AllRegularEncounters.ToArray();
        Assert(!ModelDb.Act<MegaCrit.Sts2.Core.Models.Acts.Hive>().AllEncounters.Any(e => StrongRosterContracts.ContainsKey(e.GetType())), "Strong additions stay in Depths.");
        foreach (var canonical in normal)
        foreach (int players in new[] { 1, 2, 3, 4 })
        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(canonical, players, ascension);
            Type[] actual = b.Encounter.MonstersWithSlots.Select(x => x.Item1.GetType()).ToArray();
            Type[] expected = StrongRosterContracts[canonical.GetType()];
            Assert(actual.Take(expected.Length).SequenceEqual(expected), "Roster contract: " + canonical.Id.Entry);
            Assert(actual.Length == expected.Length, "No missing/extra enemy.");
            if (canonical is ThingsLanternSpongeEncounter)
            {
                Assert(b.Encounter.Slots.SequenceEqual(["fish_1", "sponge", "fish_2"]),
                    "LanternSpongeEncounter uses ['fish_1', 'sponge', 'fish_2'] slots.");
                var fishes = b.Encounter.MonstersWithSlots
                    .Where(x => x.Item1 is ThingsLanternFish)
                    .ToArray();
                Assert(fishes.Length == 2
                    && fishes[0].Item2 == "fish_1" && ((ThingsLanternFish)fishes[0].Item1).OpeningPhase == 0
                    && fishes[1].Item2 == "fish_2" && ((ThingsLanternFish)fishes[1].Item1).OpeningPhase == 1,
                    "LanternSpongeEncounter staggers fish_1 (phase 0) and fish_2 (phase 1).");
            }
            Assert(b.Enemies.Select(c => c.SlotName).Distinct().Count() == actual.Length, "Occupied slots are unique.");
            Assert(b.Enemies.All(c => b.Encounter.Slots.Contains(c.SlotName!)), "Every enemy has a declared slot.");
            var slots = b.Encounter.CreateScene();
            Assert(b.Enemies.All(c => slots.HasNode(c.SlotName!)), "Shipping scene contains every occupied slot.");
            slots.Free();
            if (players == 1 && ascension == 0)
            {
                foreach (var creature in b.Enemies)
                {
                    var visuals = creature.Monster!.CreateVisuals();
                    Assert(visuals != null && visuals.HasNode("Bounds") && visuals.HasNode("IntentPos"), "Native creature resource and HUD anchors load.");
                    _resourceFixtures.Add(visuals!);
                    foreach (string path in creature.Monster!.AssetPaths)
                        Assert(ResourceLoader.Exists(path), "Monster asset exists: " + path);
                }
                var bg = b.Encounter.CreateBackground(b.Run.Act, b.Run.Rng.UpFront);
                _resourceFixtures.Add(bg);
                Assert(bg.HasNode("Layer_00") && bg.HasNode("Foreground"), "Native custom background loads.");
            }
            records.Add(await ExerciseStrongBattle(b, ascension));
        }
        await VerifySnailInteractions();
        await VerifyMawInteractions();
        await VerifyEliteMechanics();
        if (atlas != null) ResourceLoader.RemoveResourceFormatLoader(atlas);
        File.WriteAllText(Path.Combine(_output, "strong-combat-cases.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PASS {records.Count} native strong combat cases: 1-4 players, A0/A20, eight rounds, defeat and temporary-card isolation.");
    }

    private static async Task<object> ExerciseStrongBattle(Battle b, int ascension, string? seed = null)
    {
        foreach (var player in b.Players) { player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000); }
        var moves = new Dictionary<string, HashSet<string>>();
        for (int turn = 0; turn < 8; turn++)
        {
            b.State.CurrentSide = CombatSide.Player;
            foreach (var player in b.Players)
            {
                await player.Creature.AfterTurnStart(CombatSide.Player);
                await PlayerCmd.SetEnergy(3, player);
                await Hook.BeforeHandDraw(b.State, player, Choice);
                await CardPileCmd.Draw(Choice, 5, player);
                await Hook.AfterPlayerTurnStart(b.State, Choice, player);
                Assert(player.PlayerCombatState!.Hand.Cards.All(card => card.Owner == player), "Card ownership across combined powers.");
            }
            await SideEnding(b, CombatSide.Player, b.Players.Select(p => p.Creature).ToArray());
            foreach (var player in b.Players) await EndPlayer(b, player);
            await SideEnded(b, CombatSide.Player, b.Players.Select(p => p.Creature).ToArray());
            b.State.CurrentSide = CombatSide.Enemy;
            var participants = b.Enemies;
            foreach (var creature in participants)
            {
                await creature.AfterTurnStart(CombatSide.Enemy);
                var monster = creature.Monster!;
                if (!moves.TryGetValue(monster.Id.Entry, out var seen)) moves[monster.Id.Entry] = seen = [];
                seen.Add(monster.NextMove.Id);
                Assert(monster.NextMove.Intents.Any(), "Every move supplies an intent.");
                await monster.PerformMove();
            }
            await SideEnding(b, CombatSide.Enemy, participants);
            await SideEnded(b, CombatSide.Enemy, participants);
            b.State.CurrentSide = CombatSide.Player;
            foreach (var creature in b.Enemies) creature.Monster!.RollMove(b.Players.Select(p => p.Creature));
        }
        Assert(b.Players.All(p => p.Creature.IsAlive), "Fixture survives full state cycles.");
        foreach (var player in b.Players)
            Assert(!player.Deck.Cards.Any(c => c.Type == CardType.Status || c is ThingsSnailCrystalChip), "Temporary enemy cards never enter permanent decks.");
        int kills = 0;
        while (b.Enemies.Length > 0 && kills < 12)
        {
            var target = b.Enemies.OrderBy(c => c.Monster is ThingsHumanFaceColumn column ? column.Level : -1).First();
            await CreatureCmd.Damage(Choice, target, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(!target.IsAlive, "Native lethal damage finishes the targeted enemy.");
            kills++;
        }
        Assert(b.Enemies.Length == 0, "All enemies, including column reserves and chromatic slime splits, can be defeated.");
        if (b.Encounter is ThingsChromaticSlimeWeak)
            Assert(kills == 7, $"Chromatic Slime splits 1 -> 2 -> 4 (7 total kills), got {kills}.");
        DeactivateSyntheticCombat();
        return new { encounter = b.Encounter.Id.Entry, players = b.Players.Count, ascension, seed, turns = 8, kills,
            moves = moves.ToDictionary(kv => kv.Key, kv => kv.Value.Order().ToArray()) };
    }

    private async Task VerifyStandaloneColumns()
    {
        var canonical = ModelDb.Encounter<ThingsHumanFaceColumnEncounter>();
        Assert(canonical.RoomType == RoomType.Elite, "Human-Faced Column belongs to the Depths elite pool.");
        var records = new List<object>();
        foreach (string seed in new[] { "column-solo-0", "column-solo-1", "column-solo-2" })
        foreach (int players in new[] { 1, 2, 3, 4 })
        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(canonical, players, ascension, seed);
            Assert(b.Enemies.Length == 3 && b.Enemies.All(c => c.Monster is ThingsHumanFaceColumn),
                "Standalone strong column has three visible discs and no companion, for every tested seed");
            var slots = b.Encounter.CreateScene();
            Assert(b.Enemies.Select(c => c.SlotName).Distinct().Count() == 3 &&
                b.Enemies.All(c => slots.HasNode(c.SlotName!)), "All three discs occupy their native scene slots");
            slots.Free();
            records.Add(await ExerciseStrongBattle(b, ascension, seed));
        }
        File.WriteAllText(Path.Combine(_output, "column-standalone-cases.json"),
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PASS {records.Count} standalone strong-column combat cases: three seeds, 1-4 players, A0/A20, eight rounds and defeat.");
    }

    private static async Task VerifySnailInteractions()
    {
        foreach (int players in new[] { 1, 4 })
        foreach (var canonical in new EncounterModel[] { ModelDb.Encounter<ThingsScalePeelerEncounter>(), ModelDb.Encounter<ThingsSilkSnailEncounter>() })
        {
            var b = await StrongBattle(canonical, players);
            var mender = b.Monster<ThingsSlimeSnail>();
            var rock = b.Monster<ThingsRockSnail>();
            Assert(rock.HasShell, "Mender begins with a repairable ally.");
            await mender.PerformMove();
            mender.RollMove(b.Players.Select(p => p.Creature));
            Assert(mender.NextMove.Id == "REPAIR_MOVE", "Mender advertises repair with an intact shell.");
            decimal block = rock.Creature.Block;
            await mender.PerformMove();
            decimal expectedRepair = players == 1 ? 5 : 24; // Native 4 * 1.2 scale, then truncate.
            Assert(rock.Creature.Block == block + expectedRepair, $"Repair applies native multiplayer scaling: {players}p, {block} -> {rock.Creature.Block}.");
            mender.RollMove(b.Players.Select(p => p.Creature));
            await mender.PerformMove();
            mender.RollMove(b.Players.Select(p => p.Creature));
            int crawl = rock.CrawlsRemaining;
            await CreatureCmd.Damage(Choice, rock.Creature, rock.Creature.Block, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(!rock.HasShell && rock.Creature.IsAlive, "Exact-block hit breaks shell without killing.");
            Assert(mender.NextMove.Id == "BITE_MOVE", "Repair intent becomes bite when no repair target remains.");
            Assert(rock.CrawlsRemaining == crawl + 1, "Broken rock shell delays crush once.");
            DeactivateSyntheticCombat();
        }
        foreach (int players in new[] { 1, 4 })
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsSnailTrioWeak>(), players);
            var crystal = b.Monster<ThingsCrystalSnail>();
            Assert(crystal.HasShell, "Crystal snail begins with an intact shell.");
            await CreatureCmd.Damage(Choice, crystal.Creature, crystal.Creature.Block, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(!crystal.HasShell && crystal.Creature.IsAlive, "Exact-block hit breaks crystal shell without killing.");
            Assert(crystal.NextMove.Id == "BARE_BUMP_MOVE", "Broken crystal shell stops retreating.");
            foreach (var player in b.Players)
                Assert(player.PlayerCombatState!.DiscardPile.Cards.OfType<ThingsSnailCrystalChip>().Count() == 1,
                    "Each player receives exactly one crystal chip.");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS snail repair, live intent refresh, exact shell breaks, per-player rewards and crawl delay.");
    }

    private static async Task VerifyMawInteractions()
    {
        foreach (var canonical in new EncounterModel[] { ModelDb.Encounter<ThingsCaveMawWeak>(), ModelDb.Encounter<ThingsCaveMawLanternEncounter>() })
        foreach (int players in new[] { 1, 4 })
        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(canonical, players, ascension);
            foreach (var player in b.Players) { player.Creature.SetMaxHpInternal(1000); player.Creature.SetCurrentHpInternal(1000); }
            var maw = b.Monster<ThingsCaveMaw>();
            Assert(maw.MinInitialHp == (ascension == 0 ? 88 : 96) && maw.MaxInitialHp == (ascension == 0 ? 94 : 102),
                "Cave Maw uses updated 88-94 (A0) / 96-102 (A8+) HP band.");
            Assert(ThingsCaveMaw.BlockPerCard == 10 && ThingsCaveMaw.StrengthPerMeal == 3 &&
                   maw.Creature.GetPower<ThingsCaveMawAppetitePower>()?.DynamicVars["BlockPerCard"].IntValue == 10,
                "Cave Maw exposes BlockPerCard=10 and StrengthPerMeal=3.");
            await maw.PerformMove();
            foreach (var player in b.Players)
            {
                var debris = player.PlayerCombatState!.Hand.Cards.OfType<Debris>().ToArray();
                Assert(debris.Length == 2 && debris.All(c => c.Keywords.Contains(CardKeyword.Retain)), "Maw offers two retained vanilla Debris to each player.");
                await EndPlayer(b, player);
            }
            maw.RollMove(b.Players.Select(p => p.Creature));
            Assert(maw.NextMove.Id == "DEVOUR_MOVE", "Offer transitions to Devour.");
            int expectedDevourDamage = ascension == 0 ? 6 : 8;
            Assert(maw.NextMove.Intents.OfType<SingleAttackIntent>().Single().GetTotalDamage([b.Players[0].Creature], maw.Creature) == expectedDevourDamage &&
                   maw.NextMove.Intents.OfType<BuffIntent>().Count() == 1 &&
                   maw.NextMove.Intents.OfType<DefendIntent>().Count() == 1,
                "Devour advertises SingleAttackIntent(6/8) + BuffIntent + DefendIntent.");
            await maw.Creature.AfterTurnStart(CombatSide.Enemy);
            int playerHpBefore = b.Players[0].Creature.CurrentHp;
            await maw.PerformMove();
            Assert(playerHpBefore - b.Players[0].Creature.CurrentHp == expectedDevourDamage,
                "Devour lands its 6/8 light attack before consuming Status cards.");
            int consumed = players * 2;
            decimal expectedMealBlock = consumed * 10;
            Assert(maw.Creature.Block == expectedMealBlock,
                $"Devour grants 10 Block per consumed Status card linearly via ValueProp.Unpowered ({maw.Creature.Block} vs {expectedMealBlock}).");
            foreach (var player in b.Players)
                Assert(player.PlayerCombatState!.ExhaustPile.Cards.OfType<Debris>().Count() == 2, "Devour exhausts retained Debris for every player.");
            Assert(maw.Creature.GetPower<StrengthPower>()?.Amount == Math.Min(consumed, 3), "Maw receives native Strength capped at 3 after the meal.");
            DeactivateSyntheticCombat();
        }
        Assert(!ModelDb.Card<Debris>().Keywords.Contains(CardKeyword.Retain), "Canonical Debris remains unchanged.");
        GD.Print("PASS both maw encounters, 88-94/96-102 HP, 6/8 Devour attack, 10 Block per Status card, multiplayer consumption and canonical-card isolation.");
    }

    private static async Task VerifyEliteMechanics()
    {
        foreach (int players in new[] { 1, 4 })
        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsReverseSalamanderElite>(), players, ascension);
            var salamander = b.Monster<ThingsReverseSalamander>();
            int expectedBlockPerPlayer = ascension == 0 ? 5 : 7;
            Assert(salamander.UnplayedRecallBlock == expectedBlockPerPlayer, "Salamander UnplayedRecallBlock is 5 (A0) / 7 (A8+).");
            var playerCreatures = b.Players.Select(p => p.Creature).ToArray();
            foreach (var p in b.Players) { p.Creature.SetMaxHpInternal(1000); p.Creature.SetCurrentHpInternal(1000); }

            // Turn 1: Play a Defend so Turn 2 offers UpstreamRecall; ending Turn 1 has no active offer -> 0 Block.
            b.State.RoundNumber = 1;
            b.State.CurrentSide = CombatSide.Player;
            foreach (var p in b.Players)
            {
                if (p.PlayerCombatState!.TurnNumber == 0) p.PlayerCombatState.IncrementTurnNumber();
                await PlayerCmd.SetEnergy(3, p);
                var defend = p.PlayerCombatState.AllCards.OfType<DefendIronclad>().First();
                if (defend.Pile?.Type != PileType.Hand) await CardPileCmd.Add(defend, PileType.Hand);
                await CardCmd.AutoPlay(Choice, defend, null, skipCardPileVisuals: true);
            }
            await SideEnding(b, CombatSide.Player, playerCreatures);
            Assert(salamander.Creature.Block == 0, "Ending Turn 1 with no offered Reflux card grants 0 Block.");
            foreach (var p in b.Players) await EndPlayer(b, p);
            await SideEnded(b, CombatSide.Player, playerCreatures);

            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            await salamander.Creature.AfterTurnStart(CombatSide.Enemy);
            await Hook.AfterBlockCleared(b.State, salamander.Creature);
            await salamander.PerformMove();
            await SideEnding(b, CombatSide.Enemy, b.Enemies);
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            salamander.RollMove(playerCreatures);

            // Turn 2: UpstreamRecall is offered into Hand; leave it UNPLAYED and end Player Turn!
            b.State.RoundNumber = 2;
            b.State.CurrentSide = CombatSide.Player;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Player, playerCreatures);
            foreach (var p in b.Players)
            {
                p.PlayerCombatState!.IncrementTurnNumber();
                await p.Creature.AfterTurnStart(CombatSide.Player);
                await PlayerCmd.SetEnergy(3, p);
                await Hook.AfterPlayerTurnStart(b.State, Choice, p);
                Assert(p.PlayerCombatState.Hand.Cards.Count(c => c.Affliction is ThingsUpstreamRecall) == 1,
                    "Reverse Current offers last turn's played card with UpstreamRecall.");
            }
            await SideEnding(b, CombatSide.Player, playerCreatures);
            int expectedTotalBlock = players * expectedBlockPerPlayer;
            Assert(salamander.Creature.Block == expectedTotalBlock,
                $"Unplayed UpstreamRecall grants {expectedTotalBlock} immediate Block before turn-end orbs.");
            Assert(b.Players.All(p => !p.PlayerCombatState!.AllCards.Any(c => c.Affliction is ThingsUpstreamRecall)),
                "UpstreamRecall affliction clears at turn end.");
            foreach (var p in b.Players) await EndPlayer(b, p);
            await SideEnded(b, CombatSide.Player, playerCreatures);

            // Enemy Turn 2: ClearBlock runs on AfterTurnStart, and AfterBlockCleared carries the unplayed-Reflux Block to Turn 3!
            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            await salamander.Creature.AfterTurnStart(CombatSide.Enemy);
            await Hook.AfterBlockCleared(b.State, salamander.Creature);
            Assert(salamander.Creature.Block == expectedTotalBlock,
                "Unplayed UpstreamRecall Block survives Enemy-turn ClearBlock.");
            await salamander.PerformMove(); // TAIL_WAVE_MOVE (pure attack)
            await SideEnding(b, CombatSide.Enemy, b.Enemies);
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            salamander.RollMove(playerCreatures);

            b.State.RoundNumber = 3;
            b.State.CurrentSide = CombatSide.Player;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Player, playerCreatures);
            Assert(salamander.Creature.Block == expectedTotalBlock,
                $"Salamander still holds {expectedTotalBlock} Block on the next Player turn after leaving UpstreamRecall unplayed.");

            // Playing the offered UpstreamRecall card grants Strength and 0 unplayed Block.
            foreach (var p in b.Players)
            {
                p.PlayerCombatState!.IncrementTurnNumber();
                await PlayerCmd.SetEnergy(3, p);
                var defend = p.PlayerCombatState.AllCards.OfType<DefendIronclad>().First();
                if (defend.Pile?.Type != PileType.Hand) await CardPileCmd.Add(defend, PileType.Hand);
                await CardCmd.AutoPlay(Choice, defend, null, skipCardPileVisuals: true);
            }
            await SideEnding(b, CombatSide.Player, playerCreatures);
            foreach (var p in b.Players) await EndPlayer(b, p);
            await SideEnded(b, CombatSide.Player, playerCreatures);
            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            await salamander.Creature.AfterTurnStart(CombatSide.Enemy);
            await Hook.AfterBlockCleared(b.State, salamander.Creature);
            Assert(salamander.Creature.Block == 0, "Previous turn's carried Block expires on the following Enemy turn.");
            await salamander.PerformMove(); // BACKWASH_MOVE
            await SideEnding(b, CombatSide.Enemy, b.Enemies);
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            salamander.RollMove(playerCreatures);

            b.State.RoundNumber = 4;
            b.State.CurrentSide = CombatSide.Player;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Player, playerCreatures);
            foreach (var p in b.Players)
            {
                p.PlayerCombatState!.IncrementTurnNumber();
                await p.Creature.AfterTurnStart(CombatSide.Player);
                await PlayerCmd.SetEnergy(3, p);
                await Hook.AfterPlayerTurnStart(b.State, Choice, p);
                var offered = p.PlayerCombatState.Hand.Cards.Single(c => c.Affliction is ThingsUpstreamRecall);
                await CardCmd.AutoPlay(Choice, offered, null, skipCardPileVisuals: true);
            }
            Assert(salamander.Creature.GetPower<StrengthPower>()?.Amount == players * salamander.GrowthAmount,
                "Playing UpstreamRecall grants Strength per player.");
            await SideEnding(b, CombatSide.Player, playerCreatures);
            Assert(salamander.Creature.Block == 0, "Playing UpstreamRecall incurs zero unplayed-Reflux Block.");
            foreach (var p in b.Players) await EndPlayer(b, p);
            await SideEnded(b, CombatSide.Player, playerCreatures);
            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            await salamander.Creature.AfterTurnStart(CombatSide.Enemy);
            await Hook.AfterBlockCleared(b.State, salamander.Creature);
            Assert(salamander.NextMove.Id == "GATHER_MOVE", "Salamander Turn 4 executes GATHER_MOVE.");
            int strBeforeGather = salamander.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            await salamander.PerformMove();
            Assert(salamander.Creature.GetPower<StrengthPower>()?.Amount == strBeforeGather + 1,
                "Salamander GATHER_MOVE grants +1 Strength for soft-enrage tempo.");
            DeactivateSyntheticCombat();
        }

        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsRadioJellyfishElite>(), 1, ascension);
            var jelly = b.Monster<ThingsRadioJellyfish>();
            var player = b.Players[0];
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(1000);
            int expectedHit = ascension == 0 ? 11 : 12;
            int expectedSkillBlock = ascension == 0 ? 7 : 8;
            Assert(jelly.NextMove.Intents.OfType<SingleAttackIntent>().Single().GetTotalDamage([player.Creature], jelly.Creature) == expectedHit,
                "Radio Jellyfish opening intent reflects 11 (A0) / 12 (A9+) damage.");
            Assert(jelly.ShieldPerSkill == expectedSkillBlock,
                "Radio Jellyfish opening ShieldPerSkill reflects 7 (A0) / 8 (A8+) Block.");

            b.State.CurrentSide = CombatSide.Player;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Player, [player.Creature]);
            await PlayerCmd.SetEnergy(3, player);
            var skill = player.PlayerCombatState!.AllCards.OfType<DefendIronclad>().First();
            var attack = player.PlayerCombatState.AllCards.OfType<StrikeIronclad>().First();
            if (skill.Pile?.Type != PileType.Hand) await CardPileCmd.Add(skill, PileType.Hand);
            if (attack.Pile?.Type != PileType.Hand) await CardPileCmd.Add(attack, PileType.Hand);
            await CardCmd.AutoPlay(Choice, skill, null, skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, attack, jelly.Creature, skipCardPileVisuals: true);
            await SideEnding(b, CombatSide.Player, [player.Creature]);
            await EndPlayer(b, player);
            await SideEnded(b, CombatSide.Player, [player.Creature]);

            int totalMitigationAndHpBefore = player.Creature.CurrentHp + (int)player.Creature.Block;
            b.State.CurrentSide = CombatSide.Enemy;
            await jelly.Creature.AfterTurnStart(CombatSide.Enemy);
            await jelly.PerformMove();
            int totalMitigationAndHpAfter = player.Creature.CurrentHp + (int)player.Creature.Block;
            Assert(jelly.Creature.Block == expectedSkillBlock,
                $"Radio Jellyfish Playback grants {expectedSkillBlock} Block for one recorded Skill.");
            Assert(totalMitigationAndHpBefore - totalMitigationAndHpAfter == expectedHit * 2,
                $"Radio Jellyfish Playback deals {expectedHit}x2 damage for one recorded Attack plus finale.");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS Reverse Salamander unplayed-Reflux Block carry (5/7) & Radio Jellyfish 11/12 damage and 7/8 Skill Block.");
    }

    private static async Task VerifyBowlbugProgenitorMechanics()
    {
        foreach (int ascension in new[] { 0, 20 })
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>(), 1, ascension);
            foreach (var creature in b.Enemies) await creature.Monster!.AfterAddedToRoom();
            var boss = b.Monster<ThingsBowlbugProgenitor>();
            var player = b.Players[0];
            player.Creature.SetMaxHpInternal(2000);
            player.Creature.SetCurrentHpInternal(2000);
            Creature[] targets = [player.Creature];

            var instinct = boss.Creature.GetPower<ThingsBowlbugProgenitorPower>()!;
            int threshold = instinct.Amount;

            // 1. SUMMON_MOVE: gains 20 Block + summons 1 Bowlbug; fully-blocked hit must NOT tick down instinct!
            Assert(boss.NextMove.Id == "SUMMON_MOVE", "Progenitor Turn 1 starts on SUMMON_MOVE.");
            b.State.CurrentSide = CombatSide.Enemy;
            await boss.Creature.AfterTurnStart(CombatSide.Enemy);
            await boss.PerformMove();
            Assert(boss.Creature.Block == 20, "SUMMON_MOVE grants 20 Block.");
            var strike = player.PlayerCombatState!.AllCards.OfType<StrikeIronclad>().First();
            if (strike.Pile?.Type != PileType.Hand) await CardPileCmd.Add(strike, PileType.Hand);
            await PlayerCmd.SetEnergy(3, player);
            await CardCmd.AutoPlay(Choice, strike, boss.Creature, skipCardPileVisuals: true);
            Assert(instinct.Amount == threshold,
                $"Fully blocked attack must not advance Nest-Protecting Instinct ({instinct.Amount} vs {threshold}).");

            // 2. DISRUPT_MOVE -> 3. RALLY_MOVE
            boss.RollMove(targets);
            Assert(boss.NextMove.Id == "DISRUPT_MOVE", "Progenitor Turn 2 is DISRUPT_MOVE.");
            await boss.Creature.AfterTurnStart(CombatSide.Enemy);
            await boss.PerformMove();
            // Clear Weak from player so player attacks below are exact, and clear Weak does not affect boss damage.
            if (player.Creature.GetPower<WeakPower>() is { } weak) await PowerCmd.Remove(weak);

            boss.RollMove(targets);
            Assert(boss.NextMove.Id == "RALLY_MOVE", "Progenitor Turn 3 is RALLY_MOVE.");
            await boss.Creature.AfterTurnStart(CombatSide.Enemy);
            await boss.PerformMove();
            var siphon = boss.Creature.GetPower<ThingsBowlbugSiphonPower>();
            Assert(siphon is { Amount: 4 }, "RALLY_MOVE grants Siphon(4) to Progenitor.");
            Assert(boss.Creature.GetPower<StrengthPower>()?.Amount == 3, "RALLY_MOVE grants +3 Strength.");

            // Roll Turn 4: HEAVY_SLAM_MOVE!
            boss.RollMove(targets);
            Assert(boss.NextMove.Id == "HEAVY_SLAM_MOVE", "Progenitor Turn 4 replaces Prepare with HEAVY_SLAM_MOVE.");
            int baseSlam = ascension == 0 ? 14 : 16;
            int liveBugs = ThingsBowlbugProgenitor.CountLiveBowlbugs(b.State);
            Assert(liveBugs >= 2, $"Expected at least 2 live Bowlbugs on Turn 4, got {liveBugs}.");

            b.State.CurrentSide = CombatSide.Player;
            siphon!.RefreshProjectedVigor();
            int expectedIntentWithAllBugs = baseSlam + 3 + 4 * liveBugs;
            Assert(boss.NextMove.Intents.OfType<SingleAttackIntent>().Single().GetTotalDamage(targets, boss.Creature) == expectedIntentWithAllBugs,
                $"Siphon live-previews {expectedIntentWithAllBugs} Heavy Slam damage during Player Turn 4.");
            Assert(siphon.DynamicVars["ProjectedVigor"].IntValue == 4 * liveBugs,
                "Siphon ProjectedVigor matches 4x live Bowlbugs.");

            // Kill 1 Bowlbug during Player Turn 4 -> live intent drops by 4!
            var victimBug = b.State.Enemies.First(c => c.IsAlive && c.Monster is (MegaCrit.Sts2.Core.Models.Monsters.BowlbugEgg or MegaCrit.Sts2.Core.Models.Monsters.BowlbugNectar or MegaCrit.Sts2.Core.Models.Monsters.BowlbugRock or MegaCrit.Sts2.Core.Models.Monsters.BowlbugSilk));
            await CreatureCmd.Kill(victimBug);
            int remainingBugs = ThingsBowlbugProgenitor.CountLiveBowlbugs(b.State);
            Assert(remainingBugs == liveBugs - 1, "Killing one Bowlbug decrements live Bowlbug count.");
            Assert(boss.NextMove.Intents.OfType<SingleAttackIntent>().Single().GetTotalDamage(targets, boss.Creature) == baseSlam + 3 + 4 * remainingBugs,
                "Killing a Bowlbug immediately lowers Progenitor's Heavy Slam intent by 4.");

            // End Player Turn 4 -> Siphon converts to Swarm Vigor without double-counting!
            await SideEnding(b, CombatSide.Player, targets);
            await EndPlayer(b, player);
            await SideEnded(b, CombatSide.Player, targets);
            Assert(boss.Creature.GetPower<ThingsBowlbugSiphonPower>() == null, "Siphon expires at Player Turn end.");
            Assert(boss.Creature.GetPower<ThingsBowlbugVigorPower>()?.Amount == 4 * remainingBugs,
                "Siphon converts into 4x remaining Bowlbugs of Swarm Vigor.");

            // Enemy Turn 4: HEAVY_SLAM_MOVE deals exact damage, consumes Swarm Vigor, and applies Swarm Carapace!
            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            await boss.Creature.AfterTurnStart(CombatSide.Enemy);
            await Hook.AfterBlockCleared(b.State, boss.Creature);
            int hpBeforeSlam = player.Creature.CurrentHp;
            await boss.PerformMove();
            int expectedSlamDealt = baseSlam + 3 + 4 * remainingBugs;
            Assert(hpBeforeSlam - player.Creature.CurrentHp == expectedSlamDealt,
                $"HEAVY_SLAM_MOVE deals {expectedSlamDealt} damage ({hpBeforeSlam - player.Creature.CurrentHp}).");
            Assert(boss.Creature.GetPower<ThingsBowlbugVigorPower>() == null,
                "Swarm Vigor is consumed after Heavy Slam.");

            int carapacePerBug = ascension == 0 ? 6 : 8;
            Assert(boss.Creature.GetPower<ThingsBowlbugCarapacePower>()?.Amount == carapacePerBug,
                "HEAVY_SLAM_MOVE applies Swarm Carapace (6 A0 / 8 A8+).");
            Assert(boss.Creature.Block == remainingBugs * carapacePerBug,
                $"Swarm Carapace immediately grants {remainingBugs * carapacePerBug} Block.");

            // Player Turn 5: Unblocked damage ticks down instinct & spawning a Bowlbug dynamically grants +Carapace Block!
            b.State.CurrentSide = CombatSide.Player;
            int blockBeforeSpawn = (int)boss.Creature.Block;
            string openSlot = b.Encounter.Slots.First(slot => b.State.Enemies.All(e => !e.IsAlive || e.SlotName != slot));
            await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.BowlbugEgg>(b.State, openSlot);
            Assert(boss.Creature.Block == blockBeforeSpawn + carapacePerBug,
                "Spawning a Bowlbug while Swarm Carapace is active grants +6/+8 Block.");

            // Deal unblocked damage past Block -> instinct ticks down by UnblockedDamage only!
            int currentBlock = (int)boss.Creature.Block;
            int hpBeforeHit = boss.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, boss.Creature, currentBlock + 10m, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, player.Creature);
            int actualUnblocked = hpBeforeHit - boss.Creature.CurrentHp;
            Assert(actualUnblocked == 10 && instinct.Amount == threshold - actualUnblocked,
                $"Nest-Protecting Instinct decrements strictly by unblocked damage ({instinct.Amount} vs {threshold - actualUnblocked}).");

            // Enemy Turn 5: REST_MOVE & Swarm Carapace expires at BeforeSideTurnStart(CombatSide.Enemy)!
            boss.RollMove(targets);
            Assert(boss.NextMove.Id == "REST_MOVE", "Progenitor Turn 5 is REST_MOVE.");
            b.State.CurrentSide = CombatSide.Enemy;
            await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, b.Enemies);
            Assert(boss.Creature.GetPower<ThingsBowlbugCarapacePower>() == null,
                "Swarm Carapace expires at the start of Progenitor's next turn.");

            DeactivateSyntheticCombat();
        }
        GD.Print("PASS Bowlbug Progenitor unblocked-damage instinct, Siphon->Vigor live intent & conversion, and Heavy Slam + Swarm Carapace.");
    }
}
