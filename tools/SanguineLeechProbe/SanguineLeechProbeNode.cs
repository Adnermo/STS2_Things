using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Monsters;

public partial class SanguineLeechProbeNode : Node
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
            if (File.Exists(pack)) ProjectSettings.LoadResourcePack(pack);
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            LocManager.Initialize();
            InitializeModelDb();
            VerifyPools();
            await VerifyOpeningHand();
            await VerifyParasite();
            await VerifyMoves();
            await VerifyHandOverflow();
            await VerifyOwnerBoundReinfestation();
            if (OS.GetCmdlineUserArgs().Contains("--visual")) await RenderProbe();
            DeactivateSyntheticCombat();
            GD.Print($"Sanguine Leech probe: PASS ({_checks} assertions)");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }

    private static Fixture Scenario(bool weak = false, int players = 1, int ascension = 0)
    {
        DeactivateSyntheticCombat();
        var party = Enumerable.Range(1,players).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all,(ulong)i)).ToList();
        var run = ProbeRuntimeApi.CreateForNewRun(party, ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),[],GameMode.Standard,ascension,"leech-native-probe");
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(RunManager.Instance,run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance,ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance,new NetSingleplayerGameService());
        EncounterModel encounter = (weak ? ModelDb.Encounter<ThingsSanguineLeechWeak>() as EncounterModel : ModelDb.Encounter<ThingsSanguineLeechEncounter>()).ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster,RoomType.Monster,encounter.Id);
        var room = new CombatRoom(encounter,run);run.PushRoom(room);
        foreach (var p in party) {p.ResetCombatState();room.CombatState.AddPlayer(p);p.PopulateCombatState(run.Rng.Shuffle,room.CombatState);}
        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster,slot) in encounter.MonstersWithSlots)
        {
            var entity=room.CombatState.CreateCreature(monster,CombatSide.Enemy,slot);
            room.CombatState.AddCreature(entity);monster.SetUpForCombat();
        }
        room.CombatState.CurrentSide=CombatSide.Player;
        ActivateSyntheticCombat(room.CombatState);
        return new(run,room,party,encounter);
    }

    private static void VerifyPools()
    {
        var depths=ModelDb.Act<ThingsDepths>();
        Assert(depths.AllWeakEncounters.Any(e=>e is ThingsSanguineLeechWeak),"Leech pair in Depths weak pool.");
        Assert(depths.AllRegularEncounters.Any(e=>e is ThingsSanguineLeechEncounter),"Leech trio in Depths regular pool.");
        Assert(depths.AllWeakEncounters.Any(e=>e is ThingsLanternFishWeak) && depths.AllRegularEncounters.Any(e=>e is ThingsLanternFishEncounter),"Existing fish encounters retained.");
        foreach(bool weak in new[]{true,false})
        {
            var s=Scenario(weak);int expected=weak?2:3;
            Assert(s.Leeches.Length==expected && s.Encounter.IsWeak==weak,"Correct encounter size/classification.");
            Assert(s.Encounter.Slots.Count==expected && s.Leeches.All(l=>l.Creature.SlotName!=null),"Every leech has a slot.");
            string[] cycle=["SIP_MOVE","INFEST_MOVE","SIP_AGAIN_MOVE","CURL_MOVE"];
            foreach (var l in s.Leeches)
            {
                Assert(l.MinInitialHp==24 && l.MaxInitialHp==28,"A0 HP range.");
                for(int turn=0;turn<8;turn++)
                {
                    var move=l.MoveStateMachine!.RollMove([s.Player.Creature],l.Creature,s.Run.Rng.MonsterAi);
                    Assert(move.Id==cycle[(l.OpeningPhase+turn)%4],"Deterministic staggered cycle.");
                    l.MoveStateMachine.OnMovePerformed(move);
                }
            }
        }
        var high=Scenario(ascension:20);
        Assert(high.Leeches.All(l=>l.MinInitialHp==27 && l.MaxInitialHp==31),"Tough-enemy HP range.");
        GD.Print("PASS pools, counts, HP and staggered move state machines.");
    }

    private static async Task VerifyOpeningHand()
    {
        foreach (bool weak in new[]{true,false})
        foreach (int players in new[]{1,2,3,4})
        {
            var s=Scenario(weak,players:players);
            int deck=s.Player.Deck.Cards.Count;
            await Hook.BeforeCombatStart(s.Run,s.Room.CombatState);
            foreach(var p in s.Players)
            {
                Assert(p.PlayerCombatState!.Hand.Cards.Count==0,"Pre-combat hooks do not silently populate the hand before native UI setup.");
                await SetupPlayerTurn(p);
                var openingParasites=p.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().ToArray();
                Assert(openingParasites.Length==s.Leeches.Length,"Native turn setup gives each player one opening parasite per leech.");
                Assert(openingParasites.Select(c=>c.OwnerLeech).SequenceEqual(s.Leeches.Select(l=>l.Creature)),"Opening parasites bind 1-to-1 to their respective owner leeches.");
                Assert(p.PlayerCombatState.Hand.Cards.Count==5+s.Leeches.Length,"Five normal opening cards are drawn in addition to the parasites.");
                Assert(p.PlayerCombatState.Energy==3,"Opening generation preserves native energy reset.");
                Assert(!p.PlayerCombatState.DrawPile.Cards.OfType<ThingsLeechParasite>().Any(),"Opening parasites never enter the draw pile.");
                Assert(p.Deck.Cards.Count==deck && !p.Deck.Cards.OfType<ThingsLeechParasite>().Any(),"Permanent deck unchanged.");
                await Hook.BeforeHandDraw(s.Room.CombatState,p,Choice);
                Assert(p.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Count()==s.Leeches.Length,"Repeating the first-turn hook does not duplicate parasites.");
            }
        }
        // A fresh mutable encounter reuses the same player IDs but owns its own
        // opening-delivery state, including after previous fixtures generated cards.
        var full=Scenario();
        await Hook.BeforeCombatStart(full.Run,full.Room.CombatState);
        for(int i=0;i<CardPile.MaxCardsInHand;i++)
            await CardPileCmd.AddGeneratedCardToCombat(full.Room.CombatState.CreateCard<StrikeIronclad>(full.Player),PileType.Hand,full.Player);
        await Hook.BeforeHandDraw(full.Room.CombatState,full.Player,Choice);
        Assert(full.Player.PlayerCombatState!.Hand.Cards.Count==CardPile.MaxCardsInHand,"Opening generation observes the native hand limit.");
        Assert(full.Player.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Count()==full.Leeches.Length,"Opening overflow goes to discard through the native generation command.");
        Assert(full.Player.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Select(c=>c.OwnerLeech).SequenceEqual(full.Leeches.Select(l=>l.Creature)),"Overflowed opening parasites retain owner-leech binding.");
        await Hook.BeforeHandDraw(full.Room.CombatState,full.Player,Choice);
        Assert(full.Player.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Count()==full.Leeches.Length,"Overflow is delivered only once per leech and player.");
        GD.Print("PASS native opening turn setup, 1-4 player ownership, full normal draw, energy, fresh encounters, duplicate hooks and full-hand overflow.");
    }

    private static async Task SetupPlayerTurn(Player player)
    {
        var method=typeof(CombatManager).GetMethod("SetupPlayerTurn",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var ctx=new HookPlayerChoiceContext(player,player.NetId,GameActionType.Combat);
        object?[] args=method.GetParameters().Length==3
            ? [typeof(CombatManager).GetField("_turnState",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(CombatManager.Instance),player,ctx]
            : [player,ctx];
        await (Task)method.Invoke(CombatManager.Instance,args)!;
    }

    private static ThingsLeechParasite Parasite(Fixture s,Player? player=null,PileType pile=PileType.Hand)
    {
        player??=s.Player;
        var card=s.Room.CombatState.CreateCard<ThingsLeechParasite>(player);
        pile.GetPile(player).AddInternal(card,silent:true);
        return card;
    }
    private static async Task EndHand(Fixture s,Player? player=null)
    {
        player??=s.Player;
        var method=typeof(CombatManager).GetMethod("DoTurnEnd",BindingFlags.Instance|BindingFlags.NonPublic)!;
        object?[] args=method.GetParameters().Length==3
            ? [typeof(CombatManager).GetField("_turnState",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(CombatManager.Instance),player,Choice]
            : [player,Choice];
        await (Task)method.Invoke(CombatManager.Instance,args)!;
    }
    private static async Task VerifyParasite()
    {
        var canonicalClone=(ThingsLeechParasite)ModelDb.Card<ThingsLeechParasite>().ToMutable();
        Assert(canonicalClone.OwnerLeech==null && !canonicalClone.SuppressReinfestOnExhaust,"Canonical ToMutable starts unbound with suppression cleared.");
        foreach(int players in new[]{1,2,4})
        {
            var s=Scenario(players:players);var card=Parasite(s);
            card.BindOwnerLeech(s.Leeches[0].Creature);
            card.SuppressReinfestOnExhaust=true;
            var combatClone=(ThingsLeechParasite)card.CreateClone();
            Assert(combatClone.OwnerLeech==s.Leeches[0].Creature && !combatClone.SuppressReinfestOnExhaust,"Combat cloning preserves OwnerLeech and resets SuppressReinfestOnExhaust.");
            card.SuppressReinfestOnExhaust=false;
            Assert(card.Type==CardType.Status && card.EnergyCost.Canonical==1 && card.MaxUpgradeLevel==0,"Status cost/upgrade rules.");
            Assert(!card.Keywords.Contains(CardKeyword.Innate) && card.Keywords.Contains(CardKeyword.Exhaust),"Parasite has Exhaust and no Innate keyword.");
            await EndHand(s);
            Assert(s.Leeches.All(l=>l.Creature.GetPower<RegenPower>()?.Amount==2 && l.Creature.GetPower<StrengthPower>()?.Amount==2),
                $"Exactly two native buffs with {players} players: "+string.Join(",",s.Leeches.Select(l=>$"{l.Creature.GetPower<RegenPower>()?.Amount}/{l.Creature.GetPower<StrengthPower>()?.Amount}")));
            Assert(s.Player.PlayerCombatState!.DiscardPile.Cards.Contains(card),"Native end-turn moves status through Play into Discard, without exhausting.");
            Parasite(s);Parasite(s);await EndHand(s);
            Assert(s.Leeches.All(l=>l.Creature.GetPower<RegenPower>()?.Amount==6 && l.Creature.GetPower<StrengthPower>()?.Amount==6),"Two simultaneous retained statuses stack independently.");
            Parasite(s,pile:PileType.Draw);await EndHand(s);
            Assert(s.Leeches.All(l=>l.Creature.GetPower<StrengthPower>()?.Amount==6),"Off-hand statuses never feed.");
        }
        {
            var s=Scenario();var card=Parasite(s);var pc=s.Player.PlayerCombatState!;
            pc.Energy=3;
            await card.SpendResources();
            Assert(pc.Energy==2,"Removing a parasite costs one energy.");
            await CardCmd.AutoPlay(Choice,card,null,skipCardPileVisuals:true);
            Assert(pc.ExhaustPile.Cards.Contains(card) && !pc.Hand.Cards.Contains(card),"Native play exhausts the status.");
            await EndHand(s);
            Assert(s.Leeches.All(l=>l.Creature.GetPower<StrengthPower>()==null),"Played parasite does not feed.");
            var discard=Parasite(s);await CardCmd.Discard(Choice,discard);
            await EndHand(s);
            Assert(s.Leeches.All(l=>l.Creature.GetPower<StrengthPower>()==null),"Discarding before turn end prevents feeding.");
        }
        {
            var s=Scenario();var card=Parasite(s);var state=s.Room.CombatState;
            var other=state.CreateCreature(ModelDb.Monster<LeafSlimeS>().ToMutable(),CombatSide.Enemy,null);
            state.AddCreature(other);other.Monster!.SetUpForCombat();
            await CreatureCmd.Kill(s.Leeches[0].Creature);
            await EndHand(s);
            Assert(s.Leeches[0].Creature.GetPower<RegenPower>()==null,"Dead leech not resurrected/fed.");
            Assert(other.GetPower<RegenPower>()==null && other.GetPower<StrengthPower>()==null,"Unrelated enemies do not receive buffs.");
            Assert(s.Leeches.Skip(1).All(l=>l.Creature.GetPower<StrengthPower>()?.Amount==2),"Other living leeches are fed.");
            var living=s.Leeches[1].Creature;
            living.SetCurrentHpInternal(living.MaxHp-10);
            state.CurrentSide=CombatSide.Enemy;
            foreach(var names in new[]{new[]{"BeforeSideTurnEnd","BeforeTurnEnd"},new[]{"AfterSideTurnEnd","AfterTurnEnd"}})
            {
                var hook=names.Select(name=>typeof(Hook).GetMethod(name,BindingFlags.Public|BindingFlags.Static)).First(m=>m!=null)!;
                await (Task)hook.Invoke(null,[state,CombatSide.Enemy,new[]{living}])!;
            }
            Assert(living.CurrentHp==living.MaxHp-8 && living.GetPower<RegenPower>()?.Amount==1,"Native Regen heals and decrements at enemy turn end.");
        }
        GD.Print("PASS fixed buff amounts, living-leech filter, play/discard counterplay and native Regeneration.");
    }

    private static async Task VerifyMoves()
    {
        foreach(int ascension in new[]{0,20})
        {
            var s=Scenario(ascension:ascension);var l=s.Leeches[0];var enemy=l.Creature;
            enemy.SetCurrentHpInternal(enemy.MaxHp-12);int playerHp=s.Player.Creature.CurrentHp;
            await ((MoveState)l.MoveStateMachine!.States["SIP_MOVE"]).PerformMove([s.Player.Creature]);
            int damage=ascension==0?9:10;
            Assert(s.Player.Creature.CurrentHp==playerHp-damage,"Native siphon damage and ascension.");
            Assert(enemy.CurrentHp==enemy.MaxHp-12+damage,"Siphon heals actual life damage.");
            await CreatureCmd.GainBlock(s.Player.Creature,50,ValueProp.Unpowered,null);
            int hp=enemy.CurrentHp;playerHp=s.Player.Creature.CurrentHp;
            await ((MoveState)l.MoveStateMachine.States["SIP_MOVE"]).PerformMove([s.Player.Creature]);
            Assert(enemy.CurrentHp==hp && s.Player.Creature.CurrentHp==playerHp,"Full block prevents both damage and lifesteal.");
            await ((MoveState)l.MoveStateMachine.States["CURL_MOVE"]).PerformMove([s.Player.Creature]);
            Assert(enemy.Block==5,"Curl grants five block.");
            int parasites=s.Player.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().Count();
            await ((MoveState)l.MoveStateMachine.States["INFEST_MOVE"]).PerformMove([s.Player.Creature]);
            var handParasites=s.Player.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().ToArray();
            Assert(handParasites.Length==parasites+1,"Infest generates one temporary status directly in hand.");
            Assert(handParasites[^1].OwnerLeech==enemy,"Infest binds the generated parasite to the casting leech.");
        }
        GD.Print("PASS native moves, ascension, healing and full-block prevention.");
    }

    private static async Task VerifyHandOverflow()
    {
        var s=Scenario(players:2);
        var full=s.Players[0];var open=s.Players[1];
        for(int i=0;i<CardPile.MaxCardsInHand;i++)
            await CardPileCmd.AddGeneratedCardToCombat(s.Room.CombatState.CreateCard<StrikeIronclad>(full),PileType.Hand,null);
        await PowerCmd.Apply<STS2_Things.Powers.ThingsSpongeRinsePower>(Choice,open.Creature,1,s.Leeches[0].Creature,null);
        var move=(MoveState)s.Leeches[0].MoveStateMachine!.States["INFEST_MOVE"];
        await move.PerformMove(s.Players.Select(p=>p.Creature));
        Assert(full.PlayerCombatState!.Hand.Cards.Count==CardPile.MaxCardsInHand,"Infest respects the native full-hand limit.");
        Assert(full.PlayerCombatState.DiscardPile.Cards.OfType<ThingsLeechParasite>().Count()==1,"Full-hand parasite goes to discard using the native overflow rule.");
        Assert(open.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().Count()==1,"Another player's free hand still receives the parasite.");
        Assert(s.Players.All(p=>!p.PlayerCombatState!.DrawPile.Cards.OfType<ThingsLeechParasite>().Any()),"Neither normal delivery nor overflow sends parasites to the draw pile.");
        Assert(open.Creature.GetPower<STS2_Things.Powers.ThingsSpongeRinsePower>()?.Amount==1,"Direct hand generation is not a draw and does not consume Rinsed.");
        GD.Print("PASS native hand generation, multiplayer ownership, overflow and draw-only Rinsed semantics.");
    }

    private static async Task VerifyOwnerBoundReinfestation()
    {
        var s=Scenario(weak:false);
        await Hook.BeforeCombatStart(s.Run,s.Room.CombatState);
        foreach(var l in s.Leeches) l.RollMove([s.Player.Creature]);
        await Hook.BeforeHandDraw(s.Room.CombatState,s.Player,Choice);
        var opening=s.Player.PlayerCombatState!.Hand.Cards.OfType<ThingsLeechParasite>().ToArray();
        Assert(opening.Length==3 && opening.Select(c=>c.OwnerLeech).SequenceEqual(s.Leeches.Select(l=>l.Creature)),
            "3-leech encounter binds each opening parasite to its distinct leech.");

        // Exhaust ONLY Leech #0's parasite; discard Leech #1/#2's parasites.
        await CardCmd.Exhaust(Choice,opening[0],skipVisuals:true);
        await CardCmd.Discard(Choice,opening[1]);
        await CardCmd.Discard(Choice,opening[2]);

        s.Room.CombatState.CurrentSide=CombatSide.Enemy;
        foreach(var l in s.Leeches) await l.PerformMove();
        var afterSideTurnEnd=typeof(Hook).GetMethod("AfterSideTurnEnd",BindingFlags.Public|BindingFlags.Static)
            ?? typeof(Hook).GetMethod("AfterTurnEnd",BindingFlags.Public|BindingFlags.Static)!;
        await (Task)afterSideTurnEnd.Invoke(null,[s.Room.CombatState,CombatSide.Enemy,s.Leeches.Select(l=>l.Creature).ToArray()])!;
        foreach(var l in s.Leeches) l.RollMove([s.Player.Creature]);
        Assert(s.Leeches[0].NextMove.Id==ThingsSanguineLeech.ReinfestMoveId &&
               s.Leeches[1].NextMove.Id=="SIP_AGAIN_MOVE" &&
               s.Leeches[2].NextMove.Id=="SIP_MOVE",
            "Exhausting Leech #0's parasite queues REINFEST_MOVE ONLY on Leech #0 while Leech #1/#2 keep their normal staggered cycle.");

        // Kill Leech #0, then exhaust a parasite bound to dead Leech #0 AND an unbound parasite.
        s.Room.CombatState.CurrentSide=CombatSide.Player;
        var deadBound=Parasite(s);
        deadBound.BindOwnerLeech(s.Leeches[0].Creature);
        var unbound=Parasite(s);
        Assert(unbound.OwnerLeech==null,"Synthetic test parasite starts unbound.");
        await CreatureCmd.Kill(s.Leeches[0].Creature);
        await CardCmd.Exhaust(Choice,deadBound,skipVisuals:true);
        await CardCmd.Exhaust(Choice,unbound,skipVisuals:true);

        s.Room.CombatState.CurrentSide=CombatSide.Enemy;
        var survivors=s.Leeches.Skip(1).ToArray();
        foreach(var l in survivors) await l.PerformMove();
        await (Task)afterSideTurnEnd.Invoke(null,[s.Room.CombatState,CombatSide.Enemy,survivors.Select(l=>l.Creature).ToArray()])!;
        foreach(var l in survivors) l.RollMove([s.Player.Creature]);
        Assert(s.Leeches[1].NextMove.Id=="CURL_MOVE" && s.Leeches[2].NextMove.Id=="INFEST_MOVE",
            "Exhausting a dead leech's parasite or an unbound parasite triggers NO reinfestation on surviving leeches.");
        GD.Print("PASS 3-leech owner-bound reinfestation isolation, dead-owner no-op, and unbound no-op.");
    }

    private static void Assert(bool condition,string message) { if(!condition)throw new Exception(message);_checks++; }
    private sealed record Fixture(RunState Run, CombatRoom Room, List<Player> Players, EncounterModel Encounter)
    {
        public Player Player=>Players[0];
        public ThingsSanguineLeech[] Leeches=>Encounter.MonstersWithSlots.Select(m=>(ThingsSanguineLeech)m.Item1).ToArray();
    }
}
