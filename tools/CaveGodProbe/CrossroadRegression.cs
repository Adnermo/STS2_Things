using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Map;
using STS2_Things.Modifiers;

public sealed class CrossroadFixtureMap : ActMap
{
    protected override MapPoint?[,] Grid { get; } = new MapPoint?[7, 15];
    public override MapPoint StartingMapPoint { get; } = new(3, 0) { PointType = MapPointType.Ancient };
    public override MapPoint BossMapPoint { get; } = new(3, 15) { PointType = MapPointType.Boss };
    public CrossroadFixtureMap()
    {
        for(int row=1;row<15;row++)
        foreach(int col in new[]{0,2,4,6})
        {
            var type=row switch {3=>col<3?MapPointType.Shop:MapPointType.RestSite,
                7=>col<3?MapPointType.Treasure:MapPointType.Unknown,
                10=>col<3?MapPointType.Unknown:MapPointType.RestSite,11=>MapPointType.Elite,14=>MapPointType.RestSite,_=>MapPointType.Monster};
            Grid[col,row]=new MapPoint(col,row){PointType=type};
        }
        foreach(int col in new[]{0,2,4,6})
        {
            StartingMapPoint.AddChildPoint(Grid[col,1]!);
            for(int row=1;row<14;row++)Grid[col,row]!.AddChildPoint(Grid[col,row+1]!);
            Grid[col,14]!.AddChildPoint(BossMapPoint);
        }
    }
}

public partial class CaveGodProbeNode
{
    private static void VerifyCrossroadSaveFiltering()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,IgnoreReadOnlyProperties=true};
        foreach(var type in new[]{MapPointType.Treasure,MapPointType.RestSite,MapPointType.Shop,
            MapPointType.Unknown,MapPointType.Monster,MapPointType.Elite})
        {
            var run=RunState.CreateForTest(seed:$"same-room-road-{type}");
            var map=new CrossroadFixtureMap();run.Map=map;
            var same=new CrossroadRecord{Row=7,Left=0,Right=2};
            map.GetPoint(same.A)!.PointType=type;map.GetPoint(same.B)!.PointType=type;
            var useful=new CrossroadRecord{Row=3,Left=2,Right=4};
            var history=new List<MapCoord>{map.StartingMapPoint.coord,useful.A};
            var old=new ThingsCrossroads.Ledger{Acts=[new CrossroadActPlan{Act=0,Fingerprint=CrossroadPlan.Fingerprint(map),
                Roads=[useful,same],History=history}]};
            var ledger=Crossroads.Ledger(run)!;ledger.Data=JsonSerializer.Serialize(old,options);
            var filtered=ledger.EnsurePlan(run,map,0)!;
            Assert(filtered.Roads.Count==1&&filtered.Roads[0].A==useful.A&&filtered.Roads[0].B==useful.B,
                $"Saved locked {type}-to-{type} road was not removed, or a useful road was changed");
            Assert(filtered.History.SequenceEqual(history),"Filtering roads changed visit history");
            string once=ledger.Data;ledger.EnsurePlan(run,map,0);
            Assert(ledger.Data==once,"Saved route filtering is not idempotent");
            var restored=(ThingsCrossroads)ModifierModel.FromSerializable(ledger.ToSerializable());
            Assert(restored.EnsurePlan(run,map,0)!.Roads.Count==1,"Removed route returned after native save reload");

            same.FromColumn=same.Left;same.Payer=42;
            ledger.Data=JsonSerializer.Serialize(old,options);
            var paid=ledger.EnsurePlan(run,map,0)!;
            Assert(paid.Roads.Any(r=>r.A==same.A&&r.IsOpen&&r.From==same.From&&r.Payer==42),"Filtering removed a previously paid road");
            Assert(map.GetPoint(same.From)!.Children.Contains(map.GetPoint(same.To)!),"Previously paid road did not remain traversable");
            AssertAcyclic(map);

            // No same-type fallback just to fill the three-road quota.
            var uniform=new CrossroadFixtureMap();
            foreach(var point in uniform.GetAllMapPoints())point.PointType=type;
            Assert(CrossroadPlan.Generate(uniform,run.Rng.Seed,0).Count==0,$"Uniform {type} map still generated same-type roads");
        }
    }

    private static void AssertAcyclic(ActMap map)
    {
        var active=new HashSet<MapPoint>();var done=new HashSet<MapPoint>();
        void Visit(MapPoint point)
        {
            if(done.Contains(point))return;
            Assert(active.Add(point),"A horizontal route introduced a map cycle");
            foreach(var child in point.Children)Visit(child);
            active.Remove(point);done.Add(point);
        }
        foreach(var point in map.GetAllMapPoints())Visit(point);
    }

    private static async Task VerifyCrossroadActPricing()
    {
        Assert(CrossroadPlan.PriceForAct(-1) == 50, "Negative act index must clamp to 50 Gold");
        Assert(CrossroadPlan.PriceForAct(0) == 50, "Act 1 crossroad price must be 50 Gold");
        Assert(CrossroadPlan.PriceForAct(1) == 100, "Act 2 crossroad price must be 100 Gold");
        Assert(CrossroadPlan.PriceForAct(2) == 150, "Act 3 crossroad price must be 150 Gold");
        Assert(CrossroadPlan.PriceForAct(3) == 150, "Act 4 crossroad price must cap at 150 Gold");
        Assert(CrossroadPlan.PriceForAct(4) == 150, "Act 5+ crossroad price must cap at 150 Gold");

        var defaultActs = ActModel.GetDefaultList();
        var expectedByAct = new (int Act, int Price)[] { (0, 50), (1, 100), (2, 150), (3, 150) };
        foreach (var (act, expectedPrice) in expectedByAct)
        {
            var p1 = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var p2 = Player.CreateForNewRun<Silent>(UnlockState.all, 2);
            var acts = defaultActs.Concat([defaultActs[0]]).ToList();
            var run = RunState.CreateForTest([p1, p2], acts: acts, seed: $"act-price-{act}");
            run.CurrentActIndex = act;
            var map = new CrossroadFixtureMap();
            run.Map = map;
            var ledger = Crossroads.Ledger(run)!;
            var plan = ledger.EnsurePlan(run, map, act)!;
            var road = plan.Roads[0];
            run.AddVisitedMapCoord(map.StartingMapPoint.coord);
            run.AddVisitedMapCoord(road.A);

            p1.Gold = expectedPrice + 25;
            p2.Gold = expectedPrice + 40;
            Assert(await Crossroads.Purchase(p1, act, plan.Fingerprint, road.A, road.B, expectedPrice - 1) == CrossroadPurchaseResult.Stale,
                $"Act {act + 1} accepted wrong quoted price {expectedPrice - 1}");

            // Initiator can afford, teammate cannot -> strict reject with zero gold lost.
            p2.Gold = expectedPrice - 1;
            Assert(await Crossroads.Purchase(p1, act, plan.Fingerprint, road.A, road.B, expectedPrice) == CrossroadPurchaseResult.InsufficientGold,
                $"Act {act + 1} allowed purchase while teammate lacked gold");
            Assert(!road.IsOpen && p1.Gold == expectedPrice + 25 && p2.Gold == expectedPrice - 1,
                $"Act {act + 1} mutated road or gold on teammate shortfall");

            // Teammate can afford, initiator cannot -> strict reject with zero gold lost.
            p1.Gold = expectedPrice - 1;
            p2.Gold = expectedPrice + 40;
            Assert(await Crossroads.Purchase(p1, act, plan.Fingerprint, road.A, road.B, expectedPrice) == CrossroadPurchaseResult.InsufficientGold,
                $"Act {act + 1} allowed purchase while initiator lacked gold");
            Assert(!road.IsOpen && p1.Gold == expectedPrice - 1 && p2.Gold == expectedPrice + 40,
                $"Act {act + 1} mutated road or gold on initiator shortfall");

            // Both afford -> both pay exact act price.
            p1.Gold = expectedPrice + 25;
            p2.Gold = expectedPrice + 40;
            Assert(await Crossroads.Purchase(p2, act, plan.Fingerprint, road.A, road.B, expectedPrice) == CrossroadPurchaseResult.Purchased,
                $"Act {act + 1} rejected valid all-players crossroad purchase");
            Assert(road.IsOpen && p1.Gold == 25 && p2.Gold == 40,
                $"Act {act + 1} did not deduct {expectedPrice} Gold from every player (got p1={p1.Gold}, p2={p2.Gold})");
        }
    }

    private static void VerifyCrossroadOpenEdgeGenerationInvariance()
    {
        foreach (bool fromRight in new[] { false, true })
        {
            var run = RunState.CreateForTest(seed: $"open-edge-invariance-{fromRight}");
            var map = new CrossroadFixtureMap();
            var baseline = CrossroadPlan.Generate(map, run.Rng.Seed, 0);
            Assert(baseline.Count == 3, "Fixture must generate 3 baseline crossroads");

            var target = baseline[0];
            MapCoord from = fromRight ? target.B : target.A;
            MapCoord to = target.Other(from);
            map.GetPoint(from)!.AddChildPoint(map.GetPoint(to)!);

            // Simulate a reloaded SavedActMap that already has the horizontal edge baked in (9249a16a).
            var savedMap = new SavedActMap(SerializableActMap.FromActMap(map));
            var regenerated = CrossroadPlan.Generate(savedMap, run.Rng.Seed, 0);
            Assert(regenerated.Count == baseline.Count &&
                   regenerated.Zip(baseline).All(pair =>
                       pair.First.Row == pair.Second.Row &&
                       pair.First.Left == pair.Second.Left &&
                       pair.First.Right == pair.Second.Right),
                $"CrossroadPlan.Generate drifted after horizontal edge {from}->{to} was baked into SavedActMap");

            // Simulate fallback EnsurePlan when no ledger existed yet on the reloaded SavedActMap.
            var freshLedger = (ThingsCrossroads)ModelDb.Modifier<ThingsCrossroads>().ToMutable();
            freshLedger.Bind(run);
            var recoveredPlan = freshLedger.EnsurePlan(run, savedMap, 0)!;
            var recoveredRoad = recoveredPlan.Roads.First(r => r.Row == target.Row && r.Left == target.Left && r.Right == target.Right);
            Assert(recoveredRoad.IsOpen && recoveredRoad.FromColumn == from.col,
                $"EnsurePlan fallback failed to recover opened edge direction {from}->{to} (got FromColumn={recoveredRoad.FromColumn})");
            AssertAcyclic(savedMap);
        }
    }

    private static void VerifyCrossroadJsonSerializationRoundtrip()
    {
        var p1 = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var p2 = Player.CreateForNewRun<Silent>(UnlockState.all, 2);
        var run = RunState.CreateForTest([p1, p2], seed: "json-ledger-roundtrip");
        foreach (var actModel in run.Acts)
            actModel.GenerateRooms(run.Rng.UpFront, UnlockState.all);
        var map = new CrossroadFixtureMap();
        run.Map = map;
        var ledger = Crossroads.Ledger(run)!;
        var plan = ledger.EnsurePlan(run, map, 0)!;
        var road = plan.Roads[0];
        run.AddVisitedMapCoord(map.StartingMapPoint.coord);
        run.AddVisitedMapCoord(road.A);
        road.FromColumn = road.Left;
        road.Payer = p1.NetId;
        map.GetPoint(road.A)!.AddChildPoint(map.GetPoint(road.B)!);
        ledger.Commit();

        var save = new SerializableRun
        {
            SchemaVersion = 1,
            Acts = run.Acts.Select(a => a.ToSave()).ToList(),
            Modifiers = [],
            CurrentActIndex = run.CurrentActIndex,
            EventsSeen = run.VisitedEventIds.ToList(),
            SerializableOdds = run.Odds.ToSerializable(),
            SerializableSharedRelicGrabBag = run.SharedRelicGrabBag.ToSerializable(),
            Players = run.Players.Select(p => p.ToSerializable()).ToList(),
            SerializableRng = run.Rng.ToSerializable(),
            VisitedMapCoords = run.VisitedMapCoords.ToList(),
            MapPointHistory = run.MapPointHistory.Select(l => l.ToList()).ToList(),
            ExtraFields = run.ExtraFields.ToSerializable(),
            GameMode = GameMode.Standard,
        };
        save.Acts[0].SavedMap = SerializableActMap.FromActMap(map);

        MethodInfo setLedger = typeof(Crossroads).GetMethod("SetSerializedLedger", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo tryGetLedger = typeof(Crossroads).GetMethod("TryGetSerializedLedger", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo isLedgerId = typeof(Crossroads).GetMethod("IsLedgerId", BindingFlags.NonPublic | BindingFlags.Static)!;

        setLedger.Invoke(null, [save, ledger.ToSerializable()]);
        string json = JsonSerializationUtility.ToJson(save);
        Assert(json.Contains("THINGS_CROSSROADS", StringComparison.Ordinal),
            "JsonSerializationUtility.ToJson omitted THINGS_CROSSROADS ledger");
        Assert(save.Modifiers.Count == 0,
            "JsonSerializationUtility.ToJson mutated SerializableRun.Modifiers");

        var loaded = JsonSerializationUtility.FromJson<SerializableRun>(json);
        Assert(loaded.Success && loaded.SaveData != null,
            $"JsonSerializationUtility.FromJson failed: {loaded.Status} {loaded.ErrorMessage}");
        Assert(loaded.SaveData!.Modifiers.All(m => !(bool)isLedgerId.Invoke(null, [m.Id])!),
            "JsonSerializationUtility.FromJson left THINGS_CROSSROADS inside SerializableRun.Modifiers");

        object?[] tryArgs = [loaded.SaveData, null];
        bool hasLedger = (bool)tryGetLedger.Invoke(null, tryArgs)!;
        var restoredMod = (SerializableModifier?)tryArgs[1];
        Assert(hasLedger && restoredMod != null,
            "JsonSerializationUtility.FromJson did not extract THINGS_CROSSROADS into SerializedLedgers");
        var restoredLedger = (ThingsCrossroads)ModifierModel.FromSerializable(restoredMod!);
        Assert(restoredLedger.Data == ledger.Data,
            "JSON roundtrip altered THINGS_CROSSROADS ledger payload");

        // Second roundtrip must also preserve THINGS_CROSSROADS without mutation.
        string jsonAgain = JsonSerializationUtility.ToJson(loaded.SaveData);
        Assert(jsonAgain.Contains("THINGS_CROSSROADS", StringComparison.Ordinal) && loaded.SaveData.Modifiers.Count == 0,
            "Re-serializing loaded SerializableRun dropped THINGS_CROSSROADS or mutated Modifiers");

        // Rehydrating RunState from JSON-loaded save must attach the exact ledger.
        var rehydrated = RunState.FromSerializable(loaded.SaveData);
        Assert(Crossroads.Ledger(rehydrated)?.Data == ledger.Data,
            "RunState.FromSerializable after JSON roundtrip lost THINGS_CROSSROADS ledger");
    }

    private static void VerifyCrossroadUnknownRoomHistoryAndFloors()
    {
        var run = RunState.CreateForTest(seed: "crossroads-unknown-history");
        var map = new CrossroadFixtureMap();
        // Configure row 3 right (4,3) and row 4 right (4,4) as Unknown (?) rooms.
        var row3Left = new MapCoord(2, 3);
        var row3Unknown = new MapCoord(4, 3);
        var row4Unknown = new MapCoord(4, 4);
        map.GetPoint(row3Left)!.PointType = MapPointType.Shop;
        map.GetPoint(row3Unknown)!.PointType = MapPointType.Unknown;
        map.GetPoint(row4Unknown)!.PointType = MapPointType.Unknown;
        run.Map = map;
        var ledger = Crossroads.Ledger(run)!;
        ledger.EnsurePlan(run, map, 0);

        // Visit Ancient (3,0), row 1 (2,1), row 2 (2,2), row 3 left (2,3) [Shop].
        void Visit(MapCoord coord, MapPointType pointType, RoomType roomType)
        {
            run.AddVisitedMapCoord(coord);
            run.AppendToMapPointHistory(pointType, roomType, null);
        }

        Visit(map.StartingMapPoint.coord, MapPointType.Ancient, RoomType.Event);
        Visit(new MapCoord(2, 1), MapPointType.Monster, RoomType.Monster);
        Visit(new MapCoord(2, 2), MapPointType.Monster, RoomType.Monster);
        Visit(row3Left, MapPointType.Shop, RoomType.Shop);

        // Pre-entry (simulating NNormalMapPoint.OnSelected before AddVisitedMapCoord):
        // must return null instead of falling back to coord.row=3 (which is row3Left's Shop!).
        Assert(!Crossroads.TryResolveHistoryIndex(run, new MapLocation(row3Unknown, 0), out _),
            "Unvisited same-row ? node unexpectedly resolved a history index");
        Assert(run.GetHistoryEntryFor(new MapLocation(row3Unknown, 0)) == null,
            "Pre-entry ? node returned previous same-row room history (coord.row fallback regression)");

        // Mid-transition (after AddVisitedMapCoord, before AppendToMapPointHistory):
        // visit index is 4 while MapPointHistory[0].Count is 4 -> must still return null.
        run.AddVisitedMapCoord(row3Unknown);
        Assert(Crossroads.TryResolveHistoryIndex(run, new MapLocation(row3Unknown, 0), out int midIdx) && midIdx == 4,
            "Visited same-row ? node did not resolve to history index 4");
        Assert(run.GetHistoryEntryFor(new MapLocation(row3Unknown, 0)) == null,
            "Mid-transition ? node returned out-of-range history entry before AppendToMapPointHistory");

        // Post-entry: (4,3) is Treasure at visit index 4 (Floor 5), (2,3) remains Shop at index 3 (Floor 4).
        run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Treasure, null);
        Assert(run.GetHistoryEntryFor(new MapLocation(row3Left, 0))?.Rooms[0].RoomType == RoomType.Shop,
            "Crossroad origin room lost its Shop history entry");
        Assert(run.GetHistoryEntryFor(new MapLocation(row3Unknown, 0))?.Rooms[0].RoomType == RoomType.Treasure,
            "Crossroad ? destination did not resolve its Treasure history entry");
        Assert(Crossroads.TryComputeFloorNumber(run, new MapLocation(row3Left, 0), out int shopFloor) && shopFloor == 4,
            $"Crossroad origin floor mismatch (expected 4, got {shopFloor})");
        Assert(Crossroads.TryComputeFloorNumber(run, new MapLocation(row3Unknown, 0), out int unknownFloor) && unknownFloor == 5,
            $"Crossroad ? destination floor mismatch (expected 5, got {unknownFloor})");

        // Subsequent row ? node (4,4): history.Count == 5 > row 4, so vanilla coord.row=4 would leak row3Unknown!
        Assert(run.GetHistoryEntryFor(new MapLocation(row4Unknown, 0)) == null,
            "Pre-entry subsequent-row ? node leaked earlier crossroad room history");
        Visit(row4Unknown, MapPointType.Unknown, RoomType.Monster);
        Assert(run.GetHistoryEntryFor(new MapLocation(row4Unknown, 0))?.Rooms[0].RoomType == RoomType.Monster,
            "Subsequent-row ? node did not resolve its own Monster history entry");
        Assert(Crossroads.TryComputeFloorNumber(run, new MapLocation(row4Unknown, 0), out int nextRowFloor) && nextRowFloor == 6,
            $"Subsequent-row ? node floor mismatch (expected 6, got {nextRowFloor})");

        // Advance to Act 2 (index 1): prior act had 6 entries, so Act 2 Ancient starts at Floor 7.
        run.CurrentActIndex = 1;
        var act2Map = new CrossroadFixtureMap();
        run.Map = act2Map;
        ledger.EnsurePlan(run, act2Map, 1);
        Assert(run.GetHistoryEntryFor(new MapLocation(row3Unknown, 0))?.Rooms[0].RoomType == RoomType.Treasure,
            "Prior-act crossroad ? node lost its history entry after advancing acts");
        Assert(run.GetHistoryEntryFor(new MapLocation(new MapCoord(0, 3), 0)) == null,
            "Unvisited prior-act node fell back to coord.row indexing despite ledger history");
        Assert(Crossroads.TryComputeFloorNumber(run, new MapLocation(row3Unknown, 0), out int priorActFloor) && priorActFloor == 5,
            $"Prior-act crossroad floor mismatch (expected 5, got {priorActFloor})");

        Visit(act2Map.StartingMapPoint.coord, MapPointType.Ancient, RoomType.Event);
        Assert(Crossroads.TryComputeFloorNumber(run, new MapLocation(act2Map.StartingMapPoint.coord, 1), out int act2Floor) && act2Floor == 7,
            $"Act 2 starting floor did not include prior-act crossroad room count (expected 7, got {act2Floor})");
    }

    private async Task VerifyCrossroads()
    {
        string root=Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),"../.."));
        string output=Path.Combine(root,"build/crossroads-20261003");Directory.CreateDirectory(output);
        var harmony=new Harmony("Things.Crossroads.Probe");
        foreach(var name in new[]{"CrossroadRunPatch","CrossroadFromSerializablePatch","CrossroadSaveLoadPatches","CrossroadHistoryRecordPatch","CrossroadHistoryLookupPatch","CrossroadUnknownIconPatch","CrossroadMapPointHoverFloorPatch","CrossroadHistoryHoverTipCreatePatch","CrossroadFreeTravelPatch"})
            harmony.CreateClassProcessor(ImplementationAssembly.GetType("STS2_Things.Map."+name,true)!).Patch();
        ActionTypes.Initialize();
        var results=new List<object>();
        try
        {
            // Real native map generation across seeded acts, separate from the
            // hand-built transaction fixture below.
            var generated=new List<object>();
            for(int seed=0;seed<120;seed++)
            {
                var run=RunState.CreateForTest(seed:$"crossroads-{seed}");
                for(int act=0;act<run.Acts.Count;act++)
                {
                    var map=run.Acts[act].CreateMap(run,false);
                    var first=CrossroadPlan.Generate(map,run.Rng.Seed,act);
                    var again=CrossroadPlan.Generate(map,run.Rng.Seed,act);
                    Assert(JsonSerializer.Serialize(first)==JsonSerializer.Serialize(again),"Route generation is not deterministic");
                    Assert(first.Count<=3&&first.Select(r=>r.Row).Distinct().Count()==first.Count,"Too many routes or duplicate rows");
                    foreach(var road in first)
                    {
                        var a=map.GetPoint(road.A)!;var b=map.GetPoint(road.B)!;
                        Assert(a.PointType!=b.PointType,$"Seed {seed}, act {act}: generated a {a.PointType}-to-{b.PointType} road");
                        Assert(road.Left<road.Right&&road.Right-road.Left<=3&&road.Row>=2,"Invalid horizontal span");
                        Assert(a.PointType is MapPointType.RestSite or MapPointType.Shop or MapPointType.Treasure or MapPointType.Unknown ||
                            b.PointType is MapPointType.RestSite or MapPointType.Shop or MapPointType.Treasure or MapPointType.Unknown ||
                            a.Children.Concat(b.Children).Any(c=>c.PointType==MapPointType.Elite),"Route misses every preferred destination");
                    }
                    generated.Add(new{seed,act,roads=first.Select(r=>new{r.Row,r.Left,r.Right}).ToArray()});
                }
            }
            VerifyCrossroadSaveFiltering();
            await VerifyCrossroadActPricing();
            VerifyCrossroadOpenEdgeGenerationInvariance();
            VerifyCrossroadJsonSerializationRoundtrip();
            VerifyCrossroadUnknownRoomHistoryAndFloors();
            foreach(int count in new[]{1,2,4})
            foreach(bool fromRight in new[]{false,true})
            {
                var players=Enumerable.Range(1,count).Select(i=>Player.CreateForNewRun<Ironclad>(UnlockState.all,(ulong)i)).ToArray();
                var run=RunState.CreateForTest(players,seed:"paid-roads");
                var ledger=Crossroads.Ledger(run) ?? throw new InvalidOperationException("Route ledger was not attached to the run");
                var map=new CrossroadFixtureMap();run.Map=map;
                var plan=ledger.EnsurePlan(run,map,0)!;
                Assert(plan.Roads.Count==3,"Fixture must expose three separate opportunities");
                var road=plan.Roads[0];var from=fromRight?road.B:road.A;var to=road.Other(from);
                run.AddVisitedMapCoord(map.StartingMapPoint.coord);
                run.AddVisitedMapCoord(from);
                foreach(var player in players)player.Gold=125;
                var payer=players[^1];
                Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint,from,to,1)==CrossroadPurchaseResult.Stale,"Client changed the price");
                Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint+1,from,to,50)==CrossroadPurchaseResult.Stale,"Stale map accepted");
                payer.Gold=49;
                Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint,from,to,50)==CrossroadPurchaseResult.InsufficientGold,"Insufficient gold accepted");
                Assert(!road.IsOpen&&payer.Gold==49&&players.Where(p=>p!=payer).All(p=>p.Gold==125),"Rejected purchase mutated the route or gold");
                payer.Gold=125;
                if(count>1)
                {
                    players[0].Gold=49;
                    Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint,from,to,50)==CrossroadPurchaseResult.InsufficientGold,"Teammate shortfall accepted");
                    Assert(!road.IsOpen&&players[0].Gold==49&&players.Skip(1).All(p=>p.Gold==125),"Teammate shortfall mutated the route or gold");
                    players[0].Gold=125;
                }
                var action=new ThingsNetUnlockCrossroadAction{Act=0,Fingerprint=plan.Fingerprint,From=from,To=to,Price=50};
                Assert(action.ToId()>=0,"Native action discovery missed the unlock action");
                var writer=new PacketWriter();action.Serialize(writer);var reader=new PacketReader();reader.Reset(writer.Buffer);
                var decoded=new ThingsNetUnlockCrossroadAction();decoded.Deserialize(reader);
                Assert(decoded.Act==action.Act&&decoded.Fingerprint==action.Fingerprint&&decoded.From==from&&decoded.To==to&&decoded.Price==50,"Action packet changed");
                var nativeAction=decoded.ToGameAction(payer);nativeAction.OnEnqueued(_=>{},1);await nativeAction.Execute();
                Assert(nativeAction.Exception==null&&road.IsOpen,"Native unlock action failed");
                Assert(players.All(p=>p.Gold==75),"Every player must pay the crossroad toll");
                Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint,from,to,50)==CrossroadPurchaseResult.AlreadyOpen,"Duplicate purchase was not idempotent");
                Assert(players.All(p=>p.Gold==75),"Duplicate purchase charged again");
                Assert(map.GetPoint(from)!.Children.Contains(map.GetPoint(to)!),"Paid edge missing");AssertAcyclic(map);
                var fingerprint=CrossroadPlan.Fingerprint(map);Assert(fingerprint==plan.Fingerprint,"Opening a route changed its map identity");

                var saved=ledger.ToSerializable();writer.Reset();saved.Serialize(writer);reader.Reset(writer.Buffer);
                var restoredData=new SerializableModifier();restoredData.Deserialize(reader);
                var restored=(ThingsCrossroads)ModifierModel.FromSerializable(restoredData);
                var regenerated=new CrossroadFixtureMap();restored.Bind(run);restored.EnsurePlan(run,regenerated,0);
                Assert(regenerated.GetPoint(from)!.Children.Contains(regenerated.GetPoint(to)!),"Reload/regeneration lost the paid edge");
                var nativeSavedMap=new SavedActMap(SerializableActMap.FromActMap(map));
                restored.EnsurePlan(run,nativeSavedMap,0);AssertAcyclic(nativeSavedMap);
                run.AddVisitedMapCoord(to);
                Assert(await Crossroads.Purchase(payer,0,plan.Fingerprint,to,from,50)==CrossroadPurchaseResult.NotAtRoad,"Reverse travel allowed reward farming");
                Assert(ledger.HistoryIndex(new MapLocation(to,0))==2,"Same-row room history did not retain visit order");
                results.Add(new{count,fromRight,gold=payer.Gold,from=from.ToString(),to=to.ToString(),serializedBytes=writer.BytePosition});
            }
            File.WriteAllText(Path.Combine(output,"generation.json"),JsonSerializer.Serialize(generated,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(output,"transactions.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            GD.Print($"CROSSROADS_RULES_PASS maps={generated.Count} transactions={results.Count}; different-room routes only, six same-type save migrations/paid-edge preservation, act 1/2/3/4 pricing (50/100/150/150), all-players gold deduction, open-edge invariance, JSON ledger roundtrip, unknown-room history & floor numbers, save/network roundtrip, no cycles/revisits");
        }
        finally{harmony.UnpatchAll(harmony.Id);}
    }
}
