using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Cards;

public partial class BugReportProbeNode : Node
{
    private static int _checks;
    private static readonly Type Recorder = typeof(LeechParasite).Assembly.GetType("STS2_Things.Diagnostics.BugRunRecorder")!;
    private static string DirectoryPath => Path.Combine(OS.GetUserDataDir(),"mod_data/STS2_Things/bug_reports");
    public override void _Ready() { AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency; _ = Run(); }
    private static object? Invoke(string method, params object?[] args) => Recorder.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
    private static JsonObject Current() => JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath,"current-run.json")))!.AsObject();
    private static void Check(bool test,string name) { if(!test)throw new Exception(name);_checks++; }
    private async Task Run()
    {
        try
        {
            TestMode.TurnOnInternal();
            Check(OS.GetUserDataDir().Contains("ReportTest"),"probe uses independent user directory");
            ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK")
                ?? "D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck");
            InitializeModelDb();
            SaveManager.Instance.InitSettingsDataForTest();SaveManager.Instance.InitPrefsDataForTest();
            var first=await Scenario("report-first");
            Invoke("Initialize");
            var initial=Current();
            string firstId=initial["client_run_id"]!.GetValue<string>();
            Check(initial["schema"]!.GetValue<int>()==3,"new diagnostic schema");
            Check(initial["game_code_mvid"]!.GetValue<string>().Length==32,"actual game code identity recorded");
            Check(initial["state"]!["local_player_index"]!.GetValue<int>()==0,"local identity is an anonymous player slot");
            Check(initial["state"]!["rng"]?["rngs"] is JsonObject || initial["state"]!["rng"]?["counters"] is JsonObject,"full current RNG streams recorded");
            foreach(var act in first.Run.Acts) act.GenerateRooms(first.Run.Rng.UpFront,first.Run.UnlockState,false);
            var replay=typeof(LeechParasite).Assembly.GetType("STS2_Things.Diagnostics.BugReplayCapture")!;
            replay.GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[first.Run.CurrentRoom]);
            var checkpoint=Current()["checkpoint"]!.AsObject();
            Check(checkpoint["available"]!.GetValue<bool>(),"native pre-combat checkpoint can be captured");
            using(var zipped=new MemoryStream(Convert.FromBase64String(checkpoint["data"]!.GetValue<string>())))
            using(var gzip=new System.IO.Compression.GZipStream(zipped,System.IO.Compression.CompressionMode.Decompress))
            using(var reader=new StreamReader(gzip))
            {
                string native=reader.ReadToEnd();var save=JsonNode.Parse(native)!;
                Check(save["rng"]!["seed"]!.GetValue<string>()=="report-first","checkpoint preserves original seed");
                Check(save["players"]![0]!["net_id"]!.GetValue<ulong>()==1,"checkpoint contains portable local identity");
                Check(save["rng"]?["rngs"] is JsonObject || save["rng"]?["counters"] is JsonObject,"checkpoint contains native RNG state");
                var parsed=System.Text.Json.JsonSerializer.Deserialize(native,JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
                var restored=RunState.FromSerializable(parsed);
                var restoredRng=System.Text.Json.JsonSerializer.SerializeToNode(restored.Rng.ToSerializable(),JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet>());
                Check(JsonNode.DeepEquals(restoredRng,save["rng"]),"native load restores full RNG bits and counters exactly");
                Check(restored.Players[0].Deck.Cards.Count==first.Player.Deck.Cards.Count,"native load restores actual deck");
            }
            var hand=initial["state"]!["players"]![0]!["combat"]!["hand"]!.AsArray();
            Check(hand.Count==2,"actual model hand captured");
            Check(hand[0]!["instance_id"]!.GetValue<string>()!=hand[1]!["instance_id"]!.GetValue<string>(),"same-name cards have distinct IDs");
            Check(hand[0]!["energy"]!.GetValue<int>()==3,"native temporary cost recorded");
            Check(initial["state"]!["players"]![0]!["powers"]![0]!["amount"]!.GetValue<int>()==3,"Power amounts recorded");
            var oldExecutor=RunManager.Instance.ActionExecutor;
            var action=new PlayCardAction(first.Player.PlayerCombatState!.Hand.Cards[0],null);
            Invoke("BeforeAction",action);
            Check(Current()["events"]!.AsArray().Any(e=>e?["kind"]?.GetValue<string>()=="action_started"),"unfinished actions have a starting event");
            Invoke("BeforeChoice",action);
            Check(Current()["events"]!.AsArray().Any(e=>e?["kind"]?.GetValue<string>()=="action_waiting_for_choice"),"waiting choice is retained before completion");
            var second=await Scenario("report-second");
            Invoke("OnStarted",second.Run);
            var current=Current();
            Check(current["client_run_id"]!.GetValue<string>()!=firstId,"second run gets a distinct ID without recursive serialization");
            var previous=JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath,"last-run.json")))!;
            Check(previous["client_run_id"]!.GetValue<string>()==firstId,"previous run is archived correctly");
            var handler=typeof(ActionExecutor).GetField("BeforeActionExecuted",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(oldExecutor) as Delegate;
            Check(handler?.GetInvocationList().All(d=>d.Method.DeclaringType!=Recorder)!=false,"old executor is detached");
            Check(current["initial_state"]!["seed"]!.GetValue<string>()=="report-second","new initial snapshot belongs to second run");
            // Room/turn startup tasks run outside ActionExecutor. Exercise the actual
            // game's error logger while no action is running, just like a faulted
            // fire-and-forget combat-start task.
            Log.Error("System.MissingMethodException: Method not found: 'UInt32 MegaCrit.Sts2.Core.Runs.RunRngSet.get_Seed()'.\n"
                + "   at ExampleMod.CombatStart.BeforeHandDraw(Player player) in C:\\PrivatePlayer\\Game\\test.cs:line 42\n"
                + "Authorization: Bearer PRIVATE_TEST_CREDENTIAL\nSteamId: 76561199999999999");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Invoke("PollFailedAction");
            var faults=Current()["events"]!.AsArray().Where(e=>e?["kind"]?.GetValue<string>()=="runtime_error").ToArray();
            Check(faults.Length==1,"startup error outside action queue must be recorded");
            string serialized=faults[0]!.ToJsonString();
            Check(serialized.Contains("MissingMethodException") && serialized.Contains("ExampleMod.CombatStart.BeforeHandDraw"),"fault type and useful frames retained");
            Check(!serialized.Contains("PrivatePlayer") && !serialized.Contains("PRIVATE_TEST_CREDENTIAL") && !serialized.Contains("76561199999999999"),"paths, auth and Steam identity excluded");
            Check(faults[0]?["detail"]?["member"]?.GetValue<string>()?.Contains("get_Seed")==true,"missing API signature retained without raw error text");
            GD.PushError("System.InvalidOperationException: private engine error\n at ExampleSkin.Orb.UpdateVisuals() in C:\\PrivatePlayer\\skin.cs:line 9");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Invoke("PollFailedAction");
            Check(Current()["events"]!.AsArray().Any(e=>e?["kind"]?.GetValue<string>()=="runtime_error" && e?["detail"]?["type"]?.GetValue<string>()=="System.InvalidOperationException"),"native Godot errors also recorded");
            try { await MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Task.FromException(new MissingMethodException("Method not found: 'Example.Mod.Draw()'."))); }
            catch (MissingMethodException) { }
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);Invoke("PollFailedAction");
            Check(Current()["runtime_errors"]!.AsArray().Any(e=>e?["member"]?.GetValue<string>()=="Example.Mod.Draw()"),"real native fire-and-forget task fault reaches report");
            using(var image=Image.CreateEmpty(1200,700,false,Image.Format.Rgb8))
            {
                image.Fill(Colors.CornflowerBlue);
                using var png=new MemoryStream(image.SavePngToBuffer());
                var screenshot=typeof(LeechParasite).Assembly.GetType("STS2_Things.Diagnostics.BugScreenshotCapture")!
                    .GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[png]) as JsonObject;
                Check(screenshot?["available"]?.GetValue<bool>()==true && screenshot["bytes"]!.GetValue<int>()<=60_000,"native F2 image encodes a bounded screenshot");
                Check(screenshot!["width"]!.GetValue<int>()==960,"F2 thumbnail preserves aspect ratio within native size limit");
            }
            foreach(string name in new[]{"ThingsFeedbackCategoryPatch","ThingsFeedbackSendPatch","ThingsFeedbackReceiptPatch","ThingsFeedbackFailurePatch"})
                Check(typeof(LeechParasite).Assembly.GetType("STS2_Things.Diagnostics."+name) is not null,"shipping F2 patch exists: "+name);
            Check(typeof(MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen.NSendFeedbackScreen).GetMethod("SendFeedback",BindingFlags.NonPublic|BindingFlags.Static)!.GetParameters().Length==3,"current native F2 signature includes both owned streams");
            Invoke("DetachExecutor");DeactivateSyntheticCombat();
            GD.Print($"BugReport native probe: PASS ({_checks} assertions)");GetTree().Quit();
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
    private static async Task<(RunState Run,Player Player)> Scenario(string seed)
    {
        DeactivateSyntheticCombat();
        var player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);
        var run=RunState.CreateForNewRun([player],ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),[],GameMode.Standard,0,seed);
        typeof(RunManager).GetProperty("State",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(RunManager.Instance,run);
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance,new NetSingleplayerGameService());
        typeof(RunManager).GetProperty("ActionExecutor")!.SetValue(RunManager.Instance,new ActionExecutor(new ActionQueueSet([player])));
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance,new AscensionManager(0));
        var encounter=ModelDb.AllEncounters.First(e=>e.Id.Entry=="SLIMES_WEAK").ToMutable();
        var room=new CombatRoom(encounter,run);run.PushRoom(room);room.CombatState.AddPlayer(player);player.ResetCombatState();
        player.Creature.SetMaxHpInternal(70);player.Creature.SetCurrentHpInternal(70);
        encounter.GenerateMonstersWithSlots(run);
        foreach(var (monster,slot) in encounter.MonstersWithSlots)
        {
            var creature=room.CombatState.CreateCreature(monster,CombatSide.Enemy,slot);room.CombatState.AddCreature(creature);
            monster.SetUpForCombat();monster.RollMove([player.Creature]);
        }
        room.CombatState.CurrentSide=CombatSide.Player;ActivateSyntheticCombat(room.CombatState);
        var card=room.CombatState.CreateCard<LeechParasite>(player);card.EnergyCost.SetUntilPlayed(3);
        player.PlayerCombatState!.Hand.AddInternal(card,silent:true);
        player.PlayerCombatState.Hand.AddInternal(room.CombatState.CreateCard<LeechParasite>(player),silent:true);
        player.PlayerCombatState.Energy=3;player.PlayerCombatState.Phase=PlayerTurnPhase.Play;
        NetCombatCardDb.Instance.StartCombat([player]);
        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(),player.Creature,3,player.Creature,null,silent:true);
        return (run,player);
    }
}
