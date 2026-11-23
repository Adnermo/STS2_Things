using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using Godot.Bridge;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Powers;

public partial class DepthsProbeNode : Node
{
    private static int _checks;
    private string _root = null!;
    private static void Assert(bool okay, string message)
    {
        if (!okay) throw new InvalidOperationException(message);
        _checks++;
    }
    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("Sentry");EnsureRuntimeDependency("Sentry.Godot");
        _ = Run();
    }
    private async Task Run()
    {
        try
        {
            _root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            TestMode.TurnOnInternal();
            Assert(ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_VANILLA_PCK")!), "Matching native resource pack");
            Assert(ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")!), "Final mod pack");
            InitializeModelDb();ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            SaveManager.Instance.InitSettingsDataForTest();SaveManager.Instance.InitPrefsDataForTest();
            var extension = GDExtensionManager.LoadExtension(Path.Combine(_root,"addons/spine/spine_godot_extension.gdextension"));
            Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded,"Native Spine extension");
            var atlasType=typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
            var atlas=atlasType==null?null:(ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
            if(atlas!=null)ResourceLoader.AddResourceFormatLoader(atlas,true);
            foreach (int players in new[] {1,2,3,4})
            foreach (int ascension in new[] {0,20})
            {
                var party = Enumerable.Range(1,players).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all,(ulong)i)).ToList();
                var run = ProbeRuntimeApi.CreateForNewRun(party,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),[],GameMode.Standard,ascension,"merchant-pair");
                typeof(RunManager).GetProperty("State",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(RunManager.Instance,run);
                typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance,new NetSingleplayerGameService());
                typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance,ProbeRuntimeApi.Ascension(ascension));
                LocalContext.NetId = party[0].NetId;
                var old = ModelDb.Encounter<FakeMerchantEventEncounter>().ToMutable();old.GenerateMonstersWithSlots(run);
                Assert(old.MonstersWithSlots.Count == 1, "Native Fake Merchant event remains single");
                var pair = ModelDb.Encounter<ThingsRobberyFakeMerchantEncounter>().ToMutable();pair.GenerateMonstersWithSlots(run);
                Assert(pair.MonstersWithSlots.Count == 2 && pair.MonstersWithSlots.All(p=>p.Item1 is FakeMerchantMonster), "Two native merchants");
                Assert(!ReferenceEquals(pair.MonstersWithSlots[0].Item1,pair.MonstersWithSlots[1].Item1), "Separate mutable monsters");
                Assert(pair.Slots.SequenceEqual(new[]{"merchant_left","merchant_right"}), "Distinct slots");
                Assert(pair.MinGoldReward == 120 && pair.MaxGoldReward == 120, "Updated encounter gold reward is 120");
                var room = new CombatRoom(pair,run);
                foreach(var p in party){room.CombatState.AddPlayer(p);}
                Assert(party.All(p=>p.PlayerCombatState==null), "EventLayoutType.Combat layout state starts with null PlayerCombatState before SetUpCombat");
                foreach(var (model,slot) in pair.MonstersWithSlots)
                {
                    var creature = room.CombatState.CreateCreature(model,CombatSide.Enemy,slot);room.CombatState.AddCreature(creature);
                }
                int[] original = room.CombatState.Enemies.Select(c=>c.MaxHp).ToArray();
                var prepare = typeof(ThingsRobberyFakeMerchantEncounter).GetMethod("PrepareForFight",BindingFlags.Instance|BindingFlags.NonPublic)!;
                for(int callback=0;callback<=players;callback++)await (Task)prepare.Invoke(pair,[room.CombatState])!;
                // Simulate CombatManager.SetUpCombat after EnterCombatWithoutExitingEvent: players get combat state and monsters roll opening moves.
                foreach(var p in party){p.ResetCombatState();}
                foreach(var (model,_) in pair.MonstersWithSlots)
                {
                    model.SetUpForCombat();model.RollMove(party.Select(p=>p.Creature));
                }
                Assert(room.CombatState.Enemies.Select(c=>c.MaxHp).SequenceEqual(original.Select(h=>Math.Max(1,h/3))), "Health cut to a third once despite all player callbacks");
                Assert(room.CombatState.Enemies.All(c=>c.CurrentHp==c.MaxHp && c.Monster!.NextMove.Intents.Any()), "Both have correct HP and native move intents");
                Assert(room.CombatState.Enemies.All(c=>c.Powers.OfType<ThingsShiftyPartnersPower>().Count()==1 && !c.GetPower<ThingsShiftyPartnersPower>()!.IsVisible
                    && c.GetPower<StrengthPower>()==null), "Each merchant gets one hidden damage reduction and no Strength despite all player callbacks");
                Assert(room.CombatState.Enemies.All(c=>c.Monster!.NextMove.Intents.OfType<AttackIntent>().Single().GetTotalDamage(party.Select(p=>p.Creature),c)==(ascension==0?7:9)),
                    "Opening Swipe intent shows the reduced damage (13/15 x 0.6, rounded down)");
                var scene = pair.CreateScene();
                Assert(pair.Slots.All(s=>scene.HasNode(s)), "Both encounter markers present");scene.Free();
                var bg = pair.CreateBackground(run.Act,run.Rng.UpFront);
                Assert(bg != null, "Native shop background factory loads new event alias");bg!.Free();
                foreach(string path in pair.GetAssetPaths(run))Assert(ResourceLoader.Exists(path), "Pair asset: "+path);
            }
            Assert(ModelDb.Event<ThingsRobberyFakeMerchant>().IsShared && ModelDb.Event<ThingsRobberyFakeMerchant>().LayoutType==EventLayoutType.Combat,
                "Native shared combat event layout retained");
            Assert(ModelDb.Event<ThingsRobberyFakeMerchant>().CanonicalEncounter is ThingsRobberyFakeMerchantEncounter, "Event points to pair encounter");
            Assert(ThingsRobberyFakeMerchant.StealDamage == 7 && ThingsRobberyFakeMerchant.GoldsCount == 45 && ThingsRobberyFakeMerchant.TrueRelicsCount == 1,
                "Shifty Shop constants reflect 7 steal damage, 45 gold, and 1 true relic");
            System.Reflection.MethodInfo generateInitialOptions = typeof(EventModel).GetMethod("GenerateInitialOptions", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach(string lang in new[]{"zhs","eng"})
            {
                SaveManager.Instance.SettingsSave.Language=lang;LocManager.Initialize();
                var model=ModelDb.Event<ThingsRobberyFakeMerchant>();
                Assert(model.InitialDescription.GetFormattedText().Contains(lang=="zhs"?"两名":"Two merchants"), "Pair introductory text");
                var mutableEvent = (ThingsRobberyFakeMerchant)model.ToMutable();
                var initialOptions = (IReadOnlyList<EventOption>)generateInitialOptions.Invoke(mutableEvent, null)!;
                Assert(initialOptions.Count == 4 && initialOptions.Select(o => o.TextKey).SequenceEqual(new[]
                {
                    "THINGS_ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.TAKERELICS",
                    "THINGS_ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.TAKEGOLDS",
                    "THINGS_ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.BEGINFIGHT",
                    "THINGS_ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.LEAVE",
                }), $"Shifty Shop exposes 4 options ({lang})");
                var desc=new LocString("events","THINGS_ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.BEGINFIGHT.description");
                model.DynamicVars.AddTo(desc);
                Assert(desc.GetFormattedText().Contains(lang=="zhs"?"两名":"2 Fake Merchants"), "Battle option advertises both enemies");
                var split=new LocString("events","THINGS_CUTTING_IT_CLOSE.pages.IMPROVISE.selectionScreenPrompt").GetFormattedText();
                Assert(!split.Contains(lang=="zhs"?"技能":"Skill"), "Attack-only selection text");
            }
            var output=System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT")!;
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new
            {
                assertions=_checks,playerCounts=new[]{1,2,3,4},ascensions=new[]{0,20},
                scope="Native monster instances, unmodified original encounter, pair slots/assets/HP/intents, shared preparation idempotence, bilingual text",
                limitation="Single-process model probe; live event transition verified separately"
            },new JsonSerializerOptions{WriteIndented=true}));
            if(atlas!=null)ResourceLoader.RemoveResourceFormatLoader(atlas);
            GD.Print($"Merchant pair probe: PASS ({_checks} assertions)");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
