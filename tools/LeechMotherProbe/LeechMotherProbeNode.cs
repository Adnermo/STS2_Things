using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class LeechMotherProbeNode : Node
{
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    private static int _checks;
    public override void _Ready() { AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency; _ = Run(); }
    private async Task Run()
    {
        try
        {
            TestMode.TurnOnInternal();
            ProjectSettings.LoadResourcePack("D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck");
            string? pack = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK");
            if (pack != null) Assert(ProjectSettings.LoadResourcePack(pack), "Shipping resource package mounts");
            SaveManager.Instance.InitSettingsDataForTest(); SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs"; LocManager.Initialize(); InitializeModelDb();
            Assert(ModelDb.Act<ThingsDepths>().AllRegularEncounters.Any(e => e is ThingsLeechMotherEncounter), "Strong pool includes mother");
            Assert(!ModelDb.Act<ThingsDepths>().AllWeakEncounters.Any(e => e is ThingsLeechMotherEncounter), "Mother never enters weak pool");
            foreach (int players in new[] { 1, 2, 3, 4 })
            {
                await SleepingExit(players);
                await AwakenedCombat(players);
            }
            await LateWake();
            await KillSleeping();
            await FullHandAndStun();
            await SingleSlotBroodFallback();
            DeactivateSyntheticCombat();
            GD.Print($"Leech Mother probe: PASS ({_checks} assertions)"); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static async Task<Fixture> Scenario(int players = 1, int ascension = 0)
    {
        DeactivateSyntheticCombat();
        var party = Enumerable.Range(1, players).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i)).ToList();
        var run = ProbeRuntimeApi.CreateForNewRun(party, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), [], GameMode.Standard, ascension, "leech-mother-native");
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, new NetSingleplayerGameService());
        var encounter = (ThingsLeechMotherEncounter)ModelDb.Encounter<ThingsLeechMotherEncounter>().ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run); run.PushRoom(room);
        foreach (var p in party) { p.ResetCombatState(); room.CombatState.AddPlayer(p); p.PopulateCombatState(run.Rng.Shuffle, room.CombatState); p.Creature.SetMaxHpInternal(10000); p.Creature.SetCurrentHpInternal(10000); }
        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            var creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot); room.CombatState.AddCreature(creature); monster.SetUpForCombat(); monster.RollMove(party.Select(p => p.Creature));
        }
        room.CombatState.CurrentSide = CombatSide.Player; ActivateSyntheticCombat(room.CombatState);
        var mother = (ThingsLeechMother)encounter.MonstersWithSlots.Single().Item1;
        await mother.AfterAddedToRoom(); await Hook.BeforeCombatStart(run, room.CombatState);
        return new(run, room, encounter, mother, party);
    }
    private static async Task EnemyEnd(Fixture f)
    {
        f.State.CurrentSide = CombatSide.Enemy;
        var hook = typeof(Hook).GetMethod("AfterSideTurnEnd", BindingFlags.Public | BindingFlags.Static)
            ?? typeof(Hook).GetMethod("AfterTurnEnd", BindingFlags.Public | BindingFlags.Static)!;
        await (Task)hook.Invoke(null, [f.State, CombatSide.Enemy, f.State.Enemies.ToArray()])!;
    }
    private static async Task SleepingExit(int players)
    {
        var f = await Scenario(players);
        Assert(!f.Mother.IsAwake && f.Mother.NextMove.Intents.Single().GetType().Name == "SleepIntent", "Starts asleep with native sleep intent");
        Assert(f.Encounter.ShouldGiveRewards && f.State.Enemies.Count() == 1, "Only one initial enemy, normal reward eligibility");
        Assert(f.Players.All(p => p.PlayerCombatState!.Hand.Cards.Count == 0), "Sleeping mother creates no opening parasites");
        for (int turn = 1; turn <= 3; turn++)
        {
            await EnemyEnd(f);
            if (turn < 3) Assert(f.State.Enemies.Contains(f.Mother.Creature) && f.Mother.Creature.GetPower<ThingsLeechMotherSlumberPower>()?.Amount == 3-turn, "Two full opportunities before departure");
        }
        Assert(!f.State.Enemies.Contains(f.Mother.Creature) && !f.Encounter.ShouldGiveRewards, "Third undisturbed round removes enemy and disables all rewards");
        Assert(f.State.EscapedCreatures.Contains(f.Mother.Creature), "Exit uses native escape accounting");
        var restored = (ThingsLeechMotherEncounter)ModelDb.Encounter<ThingsLeechMotherEncounter>().ToMutable(); restored.LoadCustomState(f.Encounter.SaveCustomState());
        Assert(!restored.ShouldGiveRewards && ModelDb.Encounter<ThingsLeechMotherEncounter>().ShouldGiveRewards, "No-reward state persists without canonical contamination");
    }
    private static async Task AwakenedCombat(int players)
    {
        var f = await Scenario(players, ascension:20);
        Assert(f.Mother.MinInitialHp == 70, "High ascension reference HP");
        await CreatureCmd.GainBlock(f.Mother.Creature, 1000, ValueProp.Unpowered, null);
        int hp = f.Mother.Creature.CurrentHp;
        var card = f.State.CreateCard<StrikeIronclad>(f.Players[0]);
        await DamageCmd.Attack(1).FromCard(card, null).Targeting(f.Mother.Creature).Execute(Choice);
        Assert(f.Mother.Creature.CurrentHp == hp && f.Mother.IsAwake, "Even a fully blocked player attack wakes her");
        Assert(!f.Mother.Creature.HasPower<ThingsLeechMotherSlumberPower>() && !f.Mother.Creature.HasPower<PlatingPower>(), "Wake clears sleep and plating through native commands");
        Assert(f.Mother.NextMove.Id == ThingsLeechMother.BroodMoveId, "First advertised awake action is brood");
        f.State.CurrentSide = CombatSide.Enemy;
        await f.Mother.PerformMove();
        var brood = f.State.Enemies.Select(c => c.Monster).OfType<ThingsSanguineLeech>().ToArray();
        Assert(brood.Length == 2 && brood.All(l => l.SpawnedThisTurn), "Exactly two native summons; neither acts immediately");
        Assert(brood.All(l => l.Creature.HasPower<ThingsLeechInfestationPower>()), "Summoned leeches retain delayed reinfestation hook");
        Assert(f.Players.All(p => p.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().Count() == 2), "Exactly two parasites in each player's hand");
        Assert(f.Players.All(p =>
        {
            var parasites = p.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().ToArray();
            return parasites[0].OwnerLeech == brood[0].Creature && parasites[1].OwnerLeech == brood[1].Creature;
        }), "Brood binds parasite 0 to brood[0] and parasite 1 to brood[1]");
        foreach (var p in f.Players) await Hook.BeforeHandDraw(f.State, p, Choice);
        Assert(f.Players.All(p => p.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().Count() == 2), "Summoned leeches do not add a second opening delivery");
        foreach (var l in brood) l.RollMove(f.Players.Select(p => p.Creature));
        var firstBroodParasite = f.Players[0].PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().First();
        await CardCmd.Exhaust(Choice, firstBroodParasite, skipVisuals: true);
        foreach (var p in f.Players)
            foreach (var status in p.PlayerCombatState!.Hand.Cards.ToArray()) await CardCmd.Discard(Choice, status);
        foreach (var l in brood) await l.PerformMove();
        await EnemyEnd(f);
        foreach (var l in brood) l.RollMove(f.Players.Select(p => p.Creature));
        Assert(brood[0].NextMove.Id == ThingsSanguineLeech.ReinfestMoveId && brood[1].NextMove.Id == "CURL_MOVE",
            "Exhausting brood[0]'s parasite queues reinfestation ONLY on brood[0], keeping brood[1]'s normal cycle");
        foreach (var p in f.Players)
            foreach (var status in p.PlayerCombatState!.Hand.Cards.ToArray()) await CardCmd.Discard(Choice, status);
        string[] cycle = ["SIP_MOVE", "CRUSH_MOVE", "REST_MOVE", "SIP_MOVE"];
        foreach (var id in cycle)
        {
            f.Mother.RollMove(f.Players.Select(p => p.Creature)); Assert(f.Mother.NextMove.Id == id, "Finite awake cycle without more summons");
            await f.Mother.PerformMove();
        }
        Assert(f.State.Enemies.Count(c => c.Monster is ThingsSanguineLeech) == 2 && f.Encounter.ShouldGiveRewards, "Awake fight remains normally rewarded and capped at two brood");
        Assert(f.Players.All(p => !p.Deck.Cards.OfType<ThingsLeechParasite>().Any()), "Parasites never enter permanent decks");
    }
    private static async Task LateWake()
    {
        var f = await Scenario(); await EnemyEnd(f); await EnemyEnd(f);
        await f.Mother.PerformMove();
        await f.Mother.Wake(); await EnemyEnd(f);
        Assert(f.Mother.IsAwake && f.State.Enemies.Contains(f.Mother.Creature) && f.Encounter.ShouldGiveRewards, "Attack in third player window prevents peaceful exit");
        f.Mother.RollMove(f.Players.Select(p=>p.Creature));
        Assert(f.Mother.NextMove.Id==ThingsLeechMother.BroodMoveId,"Waking during enemy-side damage does not skip the first summon");
    }
    private static async Task KillSleeping()
    {
        var f = await Scenario(); await CreatureCmd.Damage(Choice, f.Mother.Creature, 9999, ValueProp.Unpowered, f.Players[0].Creature);
        Assert(!f.Mother.Creature.IsAlive && f.Encounter.ShouldGiveRewards && f.State.Enemies.All(c => c.Monster is not ThingsSanguineLeech), "One-shot kill gives normal rewards without summoning");
    }
    private static async Task FullHandAndStun()
    {
        var f=await Scenario(players:2);
        var full=f.Players[0]; var free=f.Players[1];
        for(int i=0;i<CardPile.MaxCardsInHand;i++)
            await CardPileCmd.AddGeneratedCardToCombat(f.State.CreateCard<StrikeIronclad>(full),PileType.Hand,full);
        await f.Mother.Wake();
        await CreatureCmd.Stun(f.Mother.Creature);
        Assert(f.Mother.NextMove.Id=="STUNNED","Brood remains interruptible by native stun");
        await f.Mother.PerformMove();
        await EnemyEnd(f);
        f.Mother.RollMove(f.Players.Select(p=>p.Creature));
        Assert(f.Mother.NextMove.Id==ThingsLeechMother.BroodMoveId,"Stun postpones rather than skips the first brood action");
        f.State.CurrentSide=CombatSide.Enemy;
        await f.Mother.PerformMove();
        Assert(full.PlayerCombatState!.Hand.Cards.Count==CardPile.MaxCardsInHand &&
            full.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Count()==2,"Brood statuses respect native full-hand overflow");
        Assert(free.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().Count()==2,"Free player's brood statuses remain in hand");
        var brood=f.State.Enemies.Select(c=>c.Monster).OfType<ThingsSanguineLeech>().ToArray();
        Assert(full.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Select(c=>c.OwnerLeech).SequenceEqual(brood.Select(l=>l.Creature)) &&
            free.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Select(c=>c.OwnerLeech).SequenceEqual(brood.Select(l=>l.Creature)),
            "Overflowed and hand brood parasites preserve per-brood owner binding");
    }
    private static async Task SingleSlotBroodFallback()
    {
        var f=await Scenario();
        var blocker=(ThingsSanguineLeech)ModelDb.Monster<ThingsSanguineLeech>().ToMutable();
        blocker.SuppressOpeningParasites(f.Players);
        await CreatureCmd.Add(blocker,f.State,CombatSide.Enemy,"brood_rear");
        await f.Mother.Wake();
        f.State.CurrentSide=CombatSide.Enemy;
        await f.Mother.PerformMove();
        var spawned=f.State.Enemies.Single(c=>c.SlotName=="brood_front");
        var parasites=f.Players[0].PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().ToArray();
        Assert(parasites.Length==2 && parasites.All(c=>c.OwnerLeech==spawned),
            "When only one brood slot is free, both Brood parasites fallback-bind to the single spawned leech");
    }
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message);_checks++; }
    private sealed record Fixture(RunState Run,CombatRoom Room,ThingsLeechMotherEncounter Encounter,ThingsLeechMother Mother,List<Player> Players)
    { public CombatState State => Room.CombatState; }
}
