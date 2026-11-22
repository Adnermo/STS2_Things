using System.Reflection;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
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

public partial class MycorrhizalTwinsProbeNode : Node
{
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    private static int _checks;
    private string _root=null!;
    public override void _Ready(){AssemblyLoadContext.Default.Resolving+=ResolveRuntimeDependency;_ = RunProbe();}
    private async Task RunProbe()
    {
        try
        {
            _root=Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),"../.."));
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
            Assert(ProjectSettings.LoadResourcePack(pack),"Shipping PCK mounts.");
            SaveManager.Instance.InitSettingsDataForTest();SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language="eng";LocManager.Initialize();InitializeModelDb();LocManager.Initialize();
            await CheckPoolsAndScaling();await CheckExchange();await CheckCycles();await CheckDeathsAndStun();
            if(OS.GetCmdlineUserArgs().Contains("--visual"))await RenderProbe();
            DeactivateSyntheticCombat();GD.Print($"Mycorrhizal Twins probe: PASS ({_checks} assertions)");GetTree().Quit(0);
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
    private static async Task<Fixture> Scenario(int players=1,int ascension=0)
    {
        DeactivateSyntheticCombat();
        var party=Enumerable.Range(1,players).Select(i=>Player.CreateForNewRun<Ironclad>(UnlockState.all,(ulong)i)).ToList();
        var run=ProbeRuntimeApi.CreateForNewRun(party,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),[],GameMode.Standard,ascension,"mycorrhizal-twins");run.CurrentActIndex=1;
        typeof(RunManager).GetProperty("State",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(RunManager.Instance,run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance,ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance,new NetSingleplayerGameService());
        EncounterModel encounter=ModelDb.Encounter<ThingsMycorrhizalTwinsElite>().ToMutable();
        run.AppendToMapPointHistory(MapPointType.Elite,RoomType.Elite,encounter.Id);var room=new CombatRoom(encounter,run);run.PushRoom(room);
        foreach(var p in party){p.ResetCombatState();room.CombatState.AddPlayer(p);p.PopulateCombatState(run.Rng.Shuffle,room.CombatState);}
        encounter.GenerateMonstersWithSlots(run);
        foreach(var(m,slot) in encounter.MonstersWithSlots){var e=room.CombatState.CreateCreature(m,CombatSide.Enemy,slot);room.CombatState.AddCreature(e);m.SetUpForCombat();m.RollMove(party.Select(p=>p.Creature));}
        room.CombatState.CurrentSide=CombatSide.Player;ActivateSyntheticCombat(room.CombatState);await Hook.BeforeCombatStart(run,room.CombatState);
        return new(run,room,party,encounter);
    }
    private static async Task EndRound(Fixture s)
    {
        await Hook.AfterSideTurnEnd(s.State,CombatSide.Enemy,s.State.Enemies.ToArray());
    }
    private static async Task CheckPoolsAndScaling()
    {
        var act=ModelDb.Act<ThingsDepths>();
        Assert(act.AllEliteEncounters.Count(e=>e is ThingsMycorrhizalTwinsElite)==1,"Depths native elite pool has exactly one twin encounter.");
        Assert(!act.AllWeakEncounters.Any(e=>e is ThingsMycorrhizalTwinsElite)&&!act.AllRegularEncounters.Any(e=>e is ThingsMycorrhizalTwinsElite),"Never selected as normal fight.");
        foreach(int count in new[]{1,2,4})foreach(int asc in new[]{0,20})
        {
            var s=await Scenario(count,asc);
            Assert(s.Encounter.MonstersWithSlots.Count==2&&s.Encounter.RoomType==RoomType.Elite,"Two elite monsters.");
            Assert(s.Tall.Creature.MaxHp==s.Short.Creature.MaxHp,"Equal scaled pools.");
            Assert(s.Tall.MinInitialHp==(asc==0?76:84),"Hive-scale HP and ascension.");
            Assert(s.Tall.IsRobust&&!s.Short.IsRobust,"Exactly one robust form starts combat.");
            Assert(s.Tall.NextMove.Intents.Single() is SingleAttackIntent&&
                   s.Short.NextMove.Intents.OfType<DefendIntent>().Count()==1&&
                   s.Short.NextMove.Intents.OfType<DebuffIntent>().Count()==1,"Roles start attack and shelter+debuff.");
            int expected=((AttackIntent)s.Tall.NextMove.Intents.Single()).GetTotalDamage(s.Players.Select(p=>p.Creature),s.Tall.Creature);
            int hp=s.Player.Creature.CurrentHp;await s.Tall.PerformMove();Assert(hp-s.Player.Creature.CurrentHp==expected,"Native intent matches robust damage, multiplayer and ascension.");
            await s.Short.PerformMove();
            Assert(s.Tall.Creature.Block>0&&s.Short.Creature.Block==s.Tall.Creature.Block,"Guard protects both with native multiplayer scaling.");
            Assert(s.Players.All(p=>p.Creature.GetPower<FrailPower>()?.Amount==1),"Shelter applies 1 Frail to all players.");
        }
    }
    private static async Task CheckExchange()
    {
        var s=await Scenario();await CreatureCmd.SetCurrentHp(s.Tall.Creature,21);await CreatureCmd.SetCurrentHp(s.Short.Creature,67);
        await CreatureCmd.GainBlock(s.Tall.Creature,9,ValueProp.Unpowered,null);
        await PowerCmd.Apply<StrengthPower>(Choice,s.Tall.Creature,2,s.Tall.Creature,null);
        await s.Tall.Creature.GetPower<ThingsMycorrhizalBondPower>()!.AfterSideTurnEnd(Choice,CombatSide.Player,s.Players.Select(p=>p.Creature));
        Assert(s.Tall.Creature.CurrentHp==21,"Player-side end never swaps HP.");
        await EndRound(s);
        // Uneven pre-swap HP (21 vs 67) swaps to (67, 21), grants no free swap Strength, and heals +3 each (-> 70, 24).
        Assert(s.Tall.Creature.CurrentHp==70&&s.Short.Creature.CurrentHp==24,
            $"Atomic swap plus uneven nutrient heal (+3 each): expected (70,24), got ({s.Tall.Creature.CurrentHp},{s.Short.Creature.CurrentHp}).");
        Assert(s.Tall.IsRobust&&!s.Short.IsRobust,"Higher pool is robust for this round.");
        Assert(s.Tall.Creature.Block==9&&s.Tall.Creature.GetPower<StrengthPower>()?.Amount==2&&s.Short.Creature.GetPower<StrengthPower>()==null,
            "Block and Strength do not move, and swap no longer grants free Strength.");
        await EndRound(s);Assert(s.Tall.Creature.CurrentHp==70,"Repeated callback cannot swap twice in a round.");
        s.State.RoundNumber++;await EndRound(s);
        Assert(s.Tall.Creature.CurrentHp==27&&s.Short.Creature.CurrentHp==73&&!s.Tall.IsRobust&&s.Short.IsRobust,
            $"Next uneven round exchanges HP and heals +3 each (27,73), got ({s.Tall.Creature.CurrentHp},{s.Short.Creature.CurrentHp}).");
        Assert(s.Tall.StrengthGift==1&&s.Short.ArmorGift==3&&s.Short.FrailGift==1,"Weak attack buff and defense/frail buffs follow form.");
        int weakIntent=((AttackIntent)s.Tall.NextMove.Intents.Single()).GetTotalDamage(s.Players.Select(p=>p.Creature),s.Tall.Creature);
        int hp=s.Player.Creature.CurrentHp;await s.Tall.PerformMove();
        // Base 14 + 2 Strength = 16; Withered 0.75x = 12!
        Assert(hp-s.Player.Creature.CurrentHp==weakIntent&&weakIntent==12,
            $"Swapped weak form scales native damage including accumulated Strength (expected 12, got {weakIntent}).");
        await s.Short.PerformMove();
        Assert(s.Player.Creature.GetPower<FrailPower>()?.Amount==1,"Robust shelter applies 1 Frail.");
        s.Short.RollMove(s.Players.Select(p=>p.Creature));
        int armorHpBefore=s.Player.Creature.CurrentHp;
        await s.Short.PerformMove();
        Assert(armorHpBefore-s.Player.Creature.CurrentHp>0,"Armor move deals damage alongside granting Plating.");
        Assert(s.Tall.Creature.GetPower<PlatingPower>()?.Amount==3&&s.Short.Creature.GetPower<PlatingPower>()?.Amount==3,"Robust defender's Plating (+3) reaches both twins.");

        // Equal HP swap: still swaps forms, triggers ZERO nutrient heal and ZERO Strength!
        var fresh=await Scenario();
        await CreatureCmd.SetCurrentHp(fresh.Tall.Creature,50);
        await CreatureCmd.SetCurrentHp(fresh.Short.Creature,50);
        await EndRound(fresh);
        Assert(!fresh.Tall.IsRobust&&fresh.Short.IsRobust,"Equal HP still swaps forms.");
        Assert(fresh.Tall.Creature.CurrentHp==50&&fresh.Short.Creature.CurrentHp==50,"Equal HP swap triggers zero nutrient heal.");
        Assert(fresh.Tall.Creature.GetPower<StrengthPower>()==null&&fresh.Short.Creature.GetPower<StrengthPower>()==null,"Equal HP swap grants zero free Strength.");
        Assert(s.Short.IsRobust,"A second combat cannot share the first power's internal form data.");
        await CreatureCmd.SetCurrentHp(fresh.Tall.Creature,1);fresh.State.RoundNumber++;await EndRound(fresh);
        Assert(fresh.Short.Creature.CurrentHp==4&&fresh.Short.Creature.IsAlive,"One HP survives swap and receives uneven nutrient heal (+3 on A0).");

        // A20 scaling check: +4 Heal on uneven swap, zero swap Strength.
        var a20=await Scenario(1,20);
        await CreatureCmd.SetCurrentHp(a20.Tall.Creature,40);
        await CreatureCmd.SetCurrentHp(a20.Short.Creature,60);
        await EndRound(a20);
        Assert(a20.Tall.Creature.CurrentHp==64&&a20.Short.Creature.CurrentHp==44,"A20 uneven swap heals +4 each.");
        Assert(a20.Tall.Creature.GetPower<StrengthPower>()==null&&a20.Short.Creature.GetPower<StrengthPower>()==null,"A20 swap grants zero free Strength.");
    }
    private static async Task CheckCycles()
    {
        var s=await Scenario();var targets=s.Players.Select(p=>p.Creature).ToArray();
        foreach(var expected in new[]{"LASH_MOVE","DOUBLE_MOVE","WAR_SPORES_MOVE","LASH_MOVE"})
        {
            Assert(s.Tall.NextMove.Id==expected,"Offensive cycle "+expected);
            Assert(s.Tall.NextMove.Intents.OfType<AttackIntent>().Any(),"Every Vanguard move deals damage (zero dead turns): "+expected);
            await s.Tall.PerformMove();s.Tall.RollMove(targets);
        }
        Assert(s.Tall.Creature.GetPower<StrengthPower>()?.Amount==2&&s.Short.Creature.GetPower<StrengthPower>()?.Amount==2,"Robust elder grants 2 Strength to both.");
        s=await Scenario();targets=s.Players.Select(p=>p.Creature).ToArray();
        foreach(var expected in new[]{"SHELTER_MOVE","ARMOR_MOVE","BASH_MOVE","SHELTER_MOVE"})
        {Assert(s.Short.NextMove.Id==expected,"Defensive cycle "+expected);await s.Short.PerformMove();s.Short.RollMove(targets);}
        Assert(s.Tall.Creature.GetPower<PlatingPower>()?.Amount==2&&s.Short.Creature.GetPower<PlatingPower>()?.Amount==2,"Withered younger grants 2 native Plating to both.");
    }
    private static async Task CheckDeathsAndStun()
    {
        foreach(bool killTall in new[]{true,false})
        {
            var s=await Scenario();MycorrhizalTwin dead=killTall?s.Tall:s.Short,alive=killTall?s.Short:s.Tall;
            string announced=alive.NextMove.Id;await CreatureCmd.Damage(Choice,dead.Creature,9999,ValueProp.Unpowered,s.Player.Creature);
            Assert(alive.IsFurious&&alive.IsRobust,"Survivor becomes robust and furious.");
            Assert(alive.Creature.GetPower<StrengthPower>()?.Amount==3&&alive.Creature.Block==10&&alive.Creature.GetPower<ThingsMycorrhizalBondPower>()==null,
                "Death grants 3 Strength + 10 Block (A0) exactly once and severs bond.");
            Assert(alive.NextMove.Id==announced,"Already announced move is preserved.");
            await alive.PerformMove();alive.RollMove(s.Players.Select(p=>p.Creature));
            Assert(alive.NextMove.Id==(killTall?"RAMPART_MOVE":"FURY_LASH_MOVE"),"Correct specialization's fury cycle.");
            int expected=alive.NextMove.Intents.OfType<AttackIntent>().Single().GetTotalDamage(s.Players.Select(p=>p.Creature),alive.Creature);
            int hp=s.Player.Creature.CurrentHp;await alive.PerformMove();
            Assert(hp-s.Player.Creature.CurrentHp==expected,"Fury attack intent matches native damage with 4 Strength and robust form.");
            alive.RollMove(s.Players.Select(p=>p.Creature));
            Assert(alive.NextMove.Id==(killTall?"FORTIFY_MOVE":"RAGE_SPORES_MOVE"),"Second fury move follows.");
            Assert(alive.NextMove.Intents.OfType<AttackIntent>().Any(),"Second fury move also attacks (zero downtime in Severed Fury).");
            await EndRound(s);Assert(dead.Creature.IsDead,"No dead twin revives.");
            int before=alive.Creature.GetPower<StrengthPower>()!.Amount;
            await EndRound(s);Assert(alive.Creature.GetPower<StrengthPower>()!.Amount==before,"No repeated death bonus.");
        }
        var stunned=await Scenario();await CreatureCmd.Stun(stunned.Tall.Creature);string stun=stunned.Tall.NextMove.Id;
        await CreatureCmd.Damage(Choice,stunned.Short.Creature,9999,ValueProp.Unpowered,stunned.Player.Creature);
        Assert(stunned.Tall.NextMove.Id==stun,"Fury does not steal a native stun turn.");
        await stunned.Tall.PerformMove();stunned.Tall.RollMove(stunned.Players.Select(p=>p.Creature));
        Assert(stunned.Tall.Creature.IsAlive&&stunned.Tall.NextMove.Id!=stun,"Native stun resumes successfully.");
    }
    private static void Assert(bool condition,string text){if(!condition)throw new Exception(text);_checks++;}
    private sealed record Fixture(RunState Run,CombatRoom Room,List<Player> Players,EncounterModel Encounter)
    {
        public Player Player=>Players[0];public CombatState State=>Room.CombatState;
        public ThingsMycorrhizalVanguard Tall=>Encounter.MonstersWithSlots.Select(x=>x.Item1).OfType<ThingsMycorrhizalVanguard>().Single();
        public ThingsMycorrhizalBulwark Short=>Encounter.MonstersWithSlots.Select(x=>x.Item1).OfType<ThingsMycorrhizalBulwark>().Single();
    }
}
