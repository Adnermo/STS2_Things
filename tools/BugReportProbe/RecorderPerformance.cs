using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

public partial class BugReportProbeNode
{
    private static int _observedCanPlay;
    private static int _observedGlobalCost;
    private static int _observedHexCost;
    private static Assembly? _performanceHex;
    private static void CountCanPlay() => _observedCanPlay++;
    private static void CountGlobalCost() => _observedGlobalCost++;
    private static void CountHexCost() => _observedHexCost++;

    private static Type[] LoadPerformanceHexTypes()
    {
        string? path = System.Environment.GetEnvironmentVariable("THINGS_PERF_HEX_DLL");
        if (string.IsNullOrEmpty(path)) return [];
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => name.Name switch
        {
            "GodotSharp" => typeof(Node).Assembly,
            "sts2" => typeof(CardModel).Assembly,
            "0Harmony" => typeof(Harmony).Assembly,
            _ => null
        };
        _performanceHex = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(CardModel).Assembly)!
            .LoadFromAssemblyPath(Path.GetFullPath(path));
        return _performanceHex.GetTypes();
    }

    private static List<ModifierModel> PerformanceModifiers() => _performanceHex is null ? [] :
        [(ModifierModel)ModelDb.GetById<ModifierModel>(ModelDb.GetId(_performanceHex.GetType("HextechRunes.HextechMayhemModifier", true)!)).ToMutable()];

    private async Task VerifyRecorderPerformance()
    {
        var scenario = await Scenario("report-performance");
        var state = scenario.Player.PlayerCombatState!;
        var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.DebugOnlyGetState()!;
        foreach (var old in state.Hand.Cards.ToArray()) state.Hand.RemoveInternal(old, silent: true);
        for (int i = 0; i < 8; i++) state.Hand.AddInternal(combat.CreateCard<StrikeIronclad>(scenario.Player), silent: true);
        for (int i = 0; i < 60; i++) state.DrawPile.AddInternal(combat.CreateCard<StrikeIronclad>(scenario.Player), silent: true);
        MegaCrit.Sts2.Core.GameActions.Multiplayer.NetCombatCardDb.Instance.StartCombat([scenario.Player]);
        Invoke("Initialize");
        var store = Recorder.GetProperty("Store", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var storeType = store.GetType();
        for (int i = 0; i < 700; i++)
            storeType.GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [new JsonObject {
                ["at"] = "2026-10-26T12:00:00Z", ["kind"] = "action_completed", ["detail"] = new JsonObject {
                    ["action_id"] = i, ["action_type"] = "PlayCardAction", ["action_state"] = "Finished", ["card"] = "CARD.STRIKE_IRONCLAD"
                }
            }]);

        var harmony = new Harmony("Things.ReportPerformance.Observer");
        harmony.Patch(AccessTools.Method(typeof(CardModel), nameof(CardModel.CanPlay)), prefix: new HarmonyMethod(typeof(BugReportProbeNode), nameof(CountCanPlay)));
        harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "ModifyEnergyCostInCombat"), prefix: new HarmonyMethod(typeof(BugReportProbeNode), nameof(CountGlobalCost)));
        if (_performanceHex is not null)
        {
            var hexCost = AccessTools.Method(_performanceHex.GetType("HextechRunes.HextechMayhemModifier", true), "TryModifyEnergyCostInCombat");
            harmony.Patch(hexCost, prefix: new HarmonyMethod(typeof(BugReportProbeNode), nameof(CountHexCost)));
            state.Hand.Cards[0].EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All);
            Check(_observedHexCost > 0, "The real Hextech modifier participates in native global cost dispatch.");
        }
        try
        {
            var action = new PlayCardAction(state.Hand.Cards[0], combat.Enemies[0]);
            for (int i = 0; i < 4; i++) { Invoke("BeforeAction", action); Invoke("AfterAction", action); }
            _observedCanPlay = 0; _observedGlobalCost = 0; _observedHexCost = 0;
            GC.Collect();
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var times = new List<double>();
            for (int i = 0; i < 100; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Invoke("BeforeAction", action);
                Invoke("AfterAction", action);
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            times.Sort();
            int eventCalls = _observedCanPlay;
            GD.Print($"REPORT_PERF action_callbacks=200 elapsed_ms={watch.Elapsed.TotalMilliseconds:F3} p50_pair_ms={times[50]:F3} p95_pair_ms={times[95]:F3} main_thread_allocated={allocated} can_play_calls={eventCalls} global_cost_calls={_observedGlobalCost} hex_cost_calls={_observedHexCost} hex_loaded={_performanceHex is not null}");
            // Exercise the real periodic flush, plus the explicit F2 barrier.
            _observedCanPlay = 0; _observedGlobalCost = 0; _observedHexCost = 0;
            await Task.Delay(550);
            Invoke("PollFailedAction");
            Current();
            GD.Print($"REPORT_SNAPSHOT can_play_calls={_observedCanPlay} global_cost_calls={_observedGlobalCost} hex_cost_calls={_observedHexCost}");
            if (!OS.GetCmdlineUserArgs().Contains("--measure"))
            {
                Check(eventCalls == 0, "Passive recording must never call gameplay CanPlay hooks while observing an action.");
                Check(_observedCanPlay == 0 && _observedGlobalCost == 0 && _observedHexCost == 0,
                    "Periodic and explicit snapshots must not invoke playability, global cost or Hextech cost hooks.");
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); Invoke("DetachExecutor"); DeactivateSyntheticCombat(); }
    }
}
