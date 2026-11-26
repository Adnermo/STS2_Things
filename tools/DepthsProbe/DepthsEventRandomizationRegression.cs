using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Config;
using STS2_Things.Events;
using STS2_Things.Relics;

internal static class DepthsEventRandomizationRegression
{
    private static readonly string[] AllDepthsEventConfigKeys =
    [
        ThingsModConfig.EventRealityAlignedHousesEnabled,
        ThingsModConfig.EventShadowCloakroomEnabled,
        ThingsModConfig.EventEchoingWellEnabled,
        ThingsModConfig.EventPoliteMawEnabled,
        ThingsModConfig.EventMycelialBankEnabled,
        ThingsModConfig.EventUnlitFireEnabled,
        ThingsModConfig.EventRelicWorkshopEnabled,
        ThingsModConfig.EventPotionTastingEnabled,
        ThingsModConfig.EventNarrowGateEnabled,
        ThingsModConfig.EventBitingChestEnabled,
        ThingsModConfig.EventCrowdedWardEnabled,
        ThingsModConfig.EventReturnsCounterEnabled,
        ThingsModConfig.EventHotWaxBoxEnabled,
        ThingsModConfig.EventSpliceBoothEnabled,
        ThingsModConfig.EventGildedPressEnabled,
        ThingsModConfig.EventCrookedCounterweightEnabled,
        ThingsModConfig.EventGrudgeVendingMachineEnabled,
        ThingsModConfig.EventCrackedBrazierEnabled,
        ThingsModConfig.EventSiltWinchEnabled,
        ThingsModConfig.EventBronzeCauldronEnabled,
        ThingsModConfig.EventWallCrackLedgerEnabled,
        ThingsModConfig.EventSpikedDoorEnabled,
    ];

    private sealed class SyntheticThirdPartyAct2Event : EventModel
    {
        protected override IReadOnlyList<EventOption> GenerateInitialOptions() => [];
    }

    private static SyntheticThirdPartyAct2Event? _syntheticThirdPartyEvent;

    private static void AppendSyntheticThirdPartyEvent(ref IEnumerable<EventModel> __result)
    {
        if (_syntheticThirdPartyEvent != null)
        {
            __result = __result.Append(_syntheticThirdPartyEvent);
        }
    }

    public static void Run(Action<bool, string> assert)
    {
        try
        {
            ResetConfig();
            VerifyNoHiveEventLeakAndThirdPartyPreservation(assert);
            VerifyDepthsEventPoolFallbackAndCursorStability(assert);
            VerifyRelaxedIsAllowedGates(assert);
            VerifyThingsBackroomsAndUnlitFireGates(assert);
            GD.Print("PASS Depths event randomization, catalog isolation, and IsAllowed regressions.");
        }
        finally
        {
            ResetConfig();
        }
    }

    private static void ResetConfig() =>
        ThingsModConfig.SetValues(
            ThingsModConfig.Entries.ToDictionary(entry => entry.Key, entry => (object?)entry.Default));

    private static void SetOnlyDepthsEventsEnabled(params string[] enabledKeys)
    {
        HashSet<string> enabled = enabledKeys.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, object?> values = ThingsModConfig.Entries
            .ToDictionary(entry => entry.Key, entry => (object?)entry.Default);
        foreach (string key in AllDepthsEventConfigKeys)
        {
            values[key] = enabled.Contains(key);
        }
        ThingsModConfig.SetValues(values);
    }

    private static RunState CreateRun(string seed, int playerCount = 1)
    {
        List<Player> party = Enumerable.Range(1, playerCount)
            .Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i))
            .ToList();
        ActModel[] acts =
        [
            ModelDb.Act<Overgrowth>().ToMutable(),
            ModelDb.Act<ThingsDepths>().ToMutable(),
            ModelDb.Act<Glory>().ToMutable(),
        ];
        RunState run = ProbeRuntimeApi.CreateForNewRun(party, acts, [], GameMode.Standard, 0, seed);
        run.CurrentActIndex = 1;
        foreach (Player player in run.Players)
        {
            player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
        }
        return run;
    }

    private static RoomSet GetRoomSet(ActModel act) =>
        (RoomSet)typeof(ActModel).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(act)!;

    private static bool IsBaseGameEvent(EventModel model) =>
        model.GetType().Assembly == typeof(ActModel).Assembly;

    private static void VerifyNoHiveEventLeakAndThirdPartyPreservation(Action<bool, string> assert)
    {
        ResetConfig();
        Hive hive = ModelDb.Act<Hive>();
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();

        EventModel[] hiveExclusiveEvents =
        [
            ModelDb.Event<ThingsMedusa>(),
            ModelDb.Event<ThingsWarmRoyalCell>(),
            ModelDb.Event<ThingsAntSortingTable>(),
            ModelDb.Event<ThingsOverstuffedBrood>(),
        ];

        HashSet<ModelId> hiveEventIds = hive.AllEvents.Select(e => e.Id).ToHashSet();
        foreach (EventModel hiveOnly in hiveExclusiveEvents)
        {
            assert(hiveEventIds.Contains(hiveOnly.Id),
                $"Hive.AllEvents must retain Hive-exclusive event {hiveOnly.Id.Entry}.");
        }

        EventModel[] depthsEvents = depths.AllEvents.ToArray();
        HashSet<ModelId> depthsEventIds = depthsEvents.Select(e => e.Id).ToHashSet();
        foreach (EventModel hiveOnly in hiveExclusiveEvents)
        {
            assert(!depthsEventIds.Contains(hiveOnly.Id),
                $"ThingsDepths.AllEvents must NOT leak Hive-exclusive event {hiveOnly.Id.Entry}.");
        }

        assert(depthsEvents.Length == AllDepthsEventConfigKeys.Length,
            $"ThingsDepths.AllEvents expected {AllDepthsEventConfigKeys.Length} authored Depths events by default, got {depthsEvents.Length}.");
        assert(depthsEventIds.Contains(ModelDb.Event<ThingsSpikedDoor>().Id),
            "ThingsDepths.AllEvents still includes shared Depths/Hive event ThingsSpikedDoor.");
        assert(depthsEventIds.Contains(ModelDb.Event<ThingsWallCrackLedger>().Id)
               && depthsEventIds.Contains(ModelDb.Event<ThingsShadowCloakroom>().Id)
               && depthsEventIds.Contains(ModelDb.Event<ThingsReturnsCounter>().Id),
            "ThingsDepths.AllEvents still includes authored Depths events.");

        // Verify third-party non-vanilla non-Things Act 2 events added to Hive.AllEvents are preserved in Depths.
        _syntheticThirdPartyEvent ??= new SyntheticThirdPartyAct2Event();
        Assembly probeAssembly = typeof(SyntheticThirdPartyAct2Event).Assembly;
        bool addedModMapEntry = false;
        var tempHarmony = new Harmony("DepthsEventRandomizationRegression.ThirdPartyAct2");
        try
        {
            if (AssemblyInfo.ModMap != null && !AssemblyInfo.ModMap.ContainsKey(probeAssembly))
            {
                AssemblyInfo.ModMap[probeAssembly] = new Mod
                {
                    path = "probe://SyntheticThirdPartyAct2",
                    state = ModLoadState.Loaded,
                    manifest = new ModManifest
                    {
                        id = "SyntheticThirdPartyAct2",
                        affectsGameplay = true,
                    },
                };
                addedModMapEntry = true;
            }

            MethodInfo hiveGetter = typeof(Hive).GetProperty(nameof(ActModel.AllEvents))!.GetGetMethod()!;
            MethodInfo postfix = typeof(DepthsEventRandomizationRegression)
                .GetMethod(nameof(AppendSyntheticThirdPartyEvent), BindingFlags.NonPublic | BindingFlags.Static)!;
            tempHarmony.Patch(hiveGetter, postfix: new HarmonyMethod(postfix) { priority = Priority.First });

            EventModel[] depthsWithThirdParty = depths.AllEvents.ToArray();
            assert(depthsWithThirdParty.Contains(_syntheticThirdPartyEvent),
                "ThingsDepths.AllEvents preserves third-party non-vanilla non-Things Act 2 events from Hive.AllEvents.");
            assert(depthsWithThirdParty.Any(e => e.Id == ModelDb.Event<ThingsPoliteMaw>().Id),
                "ThingsDepths.AllEvents retains Depths events alongside third-party Act 2 events.");
            foreach (EventModel hiveOnly in hiveExclusiveEvents)
            {
                assert(depthsWithThirdParty.All(e => e.Id != hiveOnly.Id),
                    $"ThingsDepths.AllEvents still excludes Hive-exclusive {hiveOnly.Id.Entry} with third-party Act 2 mod present.");
            }

            SetOnlyDepthsEventsEnabled();
            EventModel[] thirdPartyOnly = depths.AllEvents.ToArray();
            assert(thirdPartyOnly.Length == 1 && ReferenceEquals(thirdPartyOnly[0], _syntheticThirdPartyEvent),
                "Disabling all STS2_Things Depths events preserves third-party Act 2 events instead of dropping them.");
        }
        finally
        {
            tempHarmony.UnpatchAll(tempHarmony.Id);
            if (addedModMapEntry)
            {
                AssemblyInfo.ModMap?.Remove(probeAssembly);
            }
            ResetConfig();
        }
    }

    private static void VerifyDepthsEventPoolFallbackAndCursorStability(Action<bool, string> assert)
    {
        try
        {
            // 1) Exhausting enabled mod events (VisitedEventIds) must fall back to unvisited vanilla events instead of repeating.
            SetOnlyDepthsEventsEnabled(
                ThingsModConfig.EventPoliteMawEnabled,
                ThingsModConfig.EventBitingChestEnabled);

            RunState exhaustedRun = CreateRun("depths-exhaust-fallback");
            ThingsDepths exhaustedDepths = (ThingsDepths)exhaustedRun.Act;
            exhaustedDepths.GenerateRooms(exhaustedRun.Rng.UpFront, UnlockState.all, isMultiplayer: false);
            RoomSet exhaustedRooms = GetRoomSet(exhaustedDepths);
            assert(exhaustedRooms.events.Count == 2 && exhaustedRooms.events.All(e => !IsBaseGameEvent(e)),
                "GenerateRooms strips base-game events when solo-usable Depths mod events are enabled.");

            EventModel pull1 = exhaustedDepths.PullNextEvent(exhaustedRun);
            exhaustedDepths.MarkRoomVisited(RoomType.Event);
            EventModel pull2 = exhaustedDepths.PullNextEvent(exhaustedRun);
            exhaustedDepths.MarkRoomVisited(RoomType.Event);
            HashSet<ModelId> modExpected =
            [
                ModelDb.Event<ThingsPoliteMaw>().Id,
                ModelDb.Event<ThingsBitingChest>().Id,
            ];
            assert(modExpected.SetEquals([pull1.Id, pull2.Id]),
                "First two Depths pulls consume both enabled mod events without repetition.");

            int upFrontCounterBefore = exhaustedRun.Rng.UpFront.ToSerializable().counter;
            EventModel pull3 = exhaustedDepths.PullNextEvent(exhaustedRun);
            exhaustedDepths.MarkRoomVisited(RoomType.Event);
            int upFrontCounterAfter = exhaustedRun.Rng.UpFront.ToSerializable().counter;
            assert(upFrontCounterBefore == upFrontCounterAfter,
                "Depths fallback shuffle consumes zero draws from RunState.Rng.UpFront.");
            assert(IsBaseGameEvent(pull3) && !modExpected.Contains(pull3.Id),
                $"Once all enabled mod events are in VisitedEventIds, Depths falls back to an unvisited base-game event (got {pull3.Id.Entry}).");

            EventModel pull4 = exhaustedDepths.PullNextEvent(exhaustedRun);
            exhaustedDepths.MarkRoomVisited(RoomType.Event);
            assert(IsBaseGameEvent(pull4) && pull4.Id != pull3.Id,
                $"Subsequent fallback pull draws another distinct unvisited base-game event (got {pull4.Id.Entry}).");

            // 2) Fallback order is randomized across seeds (NOT fixed alphabetical BrainLeech-first)
            //    and deterministic for identical seeds across two peers.
            SetOnlyDepthsEventsEnabled(ThingsModConfig.EventPoliteMawEnabled);
            HashSet<ModelId> firstFallbacksAcrossSeeds = [];
            for (int s = 0; s < 24; s++)
            {
                RunState seedRun = CreateRun($"depths-fallback-shuffle-{s}");
                ThingsDepths seedDepths = (ThingsDepths)seedRun.Act;
                seedDepths.GenerateRooms(seedRun.Rng.UpFront, UnlockState.all, isMultiplayer: false);
                seedRun.AddVisitedEvent(ModelDb.Event<ThingsPoliteMaw>());
                EventModel firstFallback = seedDepths.PullNextEvent(seedRun);
                assert(IsBaseGameEvent(firstFallback),
                    $"Seed {s} fallback must pull a base-game event when sole mod event is visited.");
                firstFallbacksAcrossSeeds.Add(firstFallback.Id);
            }
            assert(firstFallbacksAcrossSeeds.Count >= 4,
                $"Fallback shuffle must vary across seeds instead of fixed alphabetical order (distinct first events: {firstFallbacksAcrossSeeds.Count}).");

            ModelId[] PullSequenceForPeer(string sharedSeed)
            {
                RunState peerRun = CreateRun(sharedSeed, playerCount: 2);
                ThingsDepths peerDepths = (ThingsDepths)peerRun.Act;
                peerDepths.GenerateRooms(peerRun.Rng.UpFront, UnlockState.all, isMultiplayer: true);
                ModelId[] pulls = new ModelId[5];
                for (int i = 0; i < pulls.Length; i++)
                {
                    pulls[i] = peerDepths.PullNextEvent(peerRun).Id;
                    peerDepths.MarkRoomVisited(RoomType.Event);
                }
                return pulls;
            }

            ModelId[] peerA = PullSequenceForPeer("depths-coop-deterministic-fallback");
            ModelId[] peerB = PullSequenceForPeer("depths-coop-deterministic-fallback");
            assert(peerA.SequenceEqual(peerB),
                "Two peers with the same run seed must draw the exact same Depths mod + fallback event sequence.");

            // 3) Stripping legacy mixed base-game events mid-act must preserve cursor without skipping M1 -> M2.
            ResetConfig();
            RunState cursorRun = CreateRun("depths-cursor-stability");
            ThingsDepths cursorDepths = (ThingsDepths)cursorRun.Act;
            RoomSet cursorRooms = GetRoomSet(cursorDepths);
            EventModel[] sharedNative = ModelDb.AllSharedEvents.Take(3).ToArray();
            EventModel m0 = ModelDb.Event<ThingsPoliteMaw>();
            EventModel m1 = ModelDb.Event<ThingsBitingChest>();
            EventModel m2 = ModelDb.Event<ThingsWallCrackLedger>();
            cursorRooms.events.Clear();
            cursorRooms.events.AddRange([sharedNative[0], m0, sharedNative[1], m1, sharedNative[2], m2]);
            cursorRun.AddVisitedEvent(m0);
            cursorRooms.eventsVisited = 2; // pointing at sharedNative[1], right before m1

            EventModel nextAfterStrip = cursorDepths.PullNextEvent(cursorRun);
            assert(nextAfterStrip.Id == m1.Id,
                $"Stripping mixed native events must preserve cursor onto m1 ({m1.Id.Entry}), not jump to m2 (got {nextAfterStrip.Id.Entry}).");
            assert(cursorRooms.events.All(e => !IsBaseGameEvent(e)),
                "Stripping mixed native events leaves zero base-game events when usable mod events exist.");
            cursorDepths.MarkRoomVisited(RoomType.Event);
            EventModel followingAfterStrip = cursorDepths.PullNextEvent(cursorRun);
            assert(followingAfterStrip.Id == m2.Id,
                $"Cursor continues cleanly from m1 to m2 ({m2.Id.Entry}), got {followingAfterStrip.Id.Entry}.");

            // 4) Mid-act temporary IsAllowed dip -> fallback -> recovery -> exhaustion without repeating fallback.
            SetOnlyDepthsEventsEnabled(
                ThingsModConfig.EventShadowCloakroomEnabled,
                ThingsModConfig.EventWallCrackLedgerEnabled);
            RunState dipRun = CreateRun("depths-dip-recover");
            Player dipPlayer = dipRun.Players[0];
            ThingsDepths dipDepths = (ThingsDepths)dipRun.Act;
            dipDepths.GenerateRooms(dipRun.Rng.UpFront, UnlockState.all, isMultiplayer: false);

            dipPlayer.Gold = 0;
            dipPlayer.Creature.SetCurrentHpInternal(5m);
            EventModel dipFallback1 = dipDepths.PullNextEvent(dipRun);
            dipDepths.MarkRoomVisited(RoomType.Event);
            assert(IsBaseGameEvent(dipFallback1),
                $"When all Depths mod events are temporarily disallowed, PullNextEvent falls back to vanilla (got {dipFallback1.Id.Entry}).");

            dipPlayer.Gold = ThingsShadowCloakroom.DepositCost;
            dipPlayer.Creature.SetCurrentHpInternal(dipPlayer.Creature.MaxHp);
            EventModel recovered1 = dipDepths.PullNextEvent(dipRun);
            dipDepths.MarkRoomVisited(RoomType.Event);
            EventModel recovered2 = dipDepths.PullNextEvent(dipRun);
            dipDepths.MarkRoomVisited(RoomType.Event);
            HashSet<ModelId> dipModIds =
            [
                ModelDb.Event<ThingsShadowCloakroom>().Id,
                ModelDb.Event<ThingsWallCrackLedger>().Id,
            ];
            assert(dipModIds.SetEquals([recovered1.Id, recovered2.Id]),
                "Once mod events become allowed again, Depths prioritizes unvisited mod events over fallback events.");

            EventModel dipFallback2 = dipDepths.PullNextEvent(dipRun);
            assert(IsBaseGameEvent(dipFallback2) && dipFallback2.Id != dipFallback1.Id,
                $"Re-entering fallback after exhausting mod events skips already-visited {dipFallback1.Id.Entry} (got {dipFallback2.Id.Entry}).");
        }
        finally
        {
            ResetConfig();
        }
    }

    private static void VerifyRelaxedIsAllowedGates(Action<bool, string> assert)
    {
        // 1) ThingsWallCrackLedger: qualifies if CurrentHp > LoanDamage (8) OR owns tradable Uncommon/Rare relic.
        RunState ledgerRun = CreateRun("gate-ledger-relaxed");
        Player ledgerPlayer = ledgerRun.Players[0];
        assert(ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(ledgerRun),
            "ThingsWallCrackLedger allows full-HP player without Uncommon/Rare relic (SIGN_LOAN works).");
        ledgerPlayer.Creature.SetCurrentHpInternal(ThingsWallCrackLedger.LoanDamage);
        assert(!ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(ledgerRun),
            "ThingsWallCrackLedger rejects HP <= 8 when player has no pawnable relic (would force death).");
        RelicModel pawnRelic = ModelDb.AllRelics
            .First(r => r.IsTradable && r.Rarity == RelicRarity.Rare && r.IsAllowed(ledgerRun))
            .ToMutable();
        ledgerPlayer.AddRelicInternal(pawnRelic);
        assert(ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(ledgerRun),
            "ThingsWallCrackLedger allows low-HP player holding a tradable Rare/Uncommon relic (PAWN_RELIC heals to full).");

        // 2) ThingsReturnsCounter: any of RECEIPT, VIEWFINDER, or RUMMAGE qualifies.
        RunState returnsRun = CreateRun("gate-returns-relaxed");
        Player returnsPlayer = returnsRun.Players[0];
        returnsPlayer.Gold = 0;
        assert(ModelDb.Event<ThingsReturnsCounter>().IsAllowed(returnsRun),
            "ThingsReturnsCounter allows 0 Gold with full HP/MaxHP (RECEIPT/VIEWFINDER work).");
        returnsPlayer.Creature.SetMaxHpInternal(ThingsReturnsCounter.ReceiptMaxHpLoss);
        returnsPlayer.Creature.SetCurrentHpInternal(ThingsReturnsCounter.ReceiptMaxHpLoss);
        returnsPlayer.Gold = ThingsReturnsCounter.RummageCost - 1;
        assert(!ModelDb.Event<ThingsReturnsCounter>().IsAllowed(returnsRun),
            "ThingsReturnsCounter rejects when MaxHP <= 6, HP <= 10, and Gold < 40.");
        returnsPlayer.Gold = ThingsReturnsCounter.RummageCost;
        assert(ModelDb.Event<ThingsReturnsCounter>().IsAllowed(returnsRun),
            "ThingsReturnsCounter allows Gold >= 40 even when HP/MaxHP are too low for relic branches (RUMMAGE works).");

        RunState returnsOwnedRun = CreateRun("gate-returns-owned");
        Player returnsOwnedPlayer = returnsOwnedRun.Players[0];
        returnsOwnedPlayer.Gold = 0;
        returnsOwnedPlayer.AddRelicInternal(ModelDb.Relic<ThingsReturnReceipt>().ToMutable());
        returnsOwnedPlayer.AddRelicInternal(ModelDb.Relic<ThingsNarrowViewfinder>().ToMutable());
        assert(!ModelDb.Event<ThingsReturnsCounter>().IsAllowed(returnsOwnedRun),
            "ThingsReturnsCounter rejects player owning both relics with Gold < 40.");

        // 3) ThingsGildedPress: (MaxHp > SealMaxHpLoss (8) && RoyallyApproved target) || (Gold >= MoldGoldCost (75) && Clone target).
        RunState pressRun = CreateRun("gate-press-relaxed");
        Player pressPlayer = pressRun.Players[0];
        pressPlayer.Gold = 0;
        assert(ModelDb.Event<ThingsGildedPress>().IsAllowed(pressRun),
            $"ThingsGildedPress allows 0 Gold when MaxHP > {ThingsGildedPress.SealMaxHpLoss} with a RoyallyApproved target (SEAL works).");
        pressPlayer.Creature.SetMaxHpInternal(ThingsGildedPress.SealMaxHpLoss);
        pressPlayer.Creature.SetCurrentHpInternal(ThingsGildedPress.SealMaxHpLoss);
        pressPlayer.Gold = ThingsGildedPress.MoldGoldCost - 1;
        assert(!ModelDb.Event<ThingsGildedPress>().IsAllowed(pressRun),
            $"ThingsGildedPress rejects when MaxHP <= {ThingsGildedPress.SealMaxHpLoss} and Gold < {ThingsGildedPress.MoldGoldCost}.");
        pressPlayer.Gold = ThingsGildedPress.MoldGoldCost;
        assert(ModelDb.Event<ThingsGildedPress>().IsAllowed(pressRun),
            $"ThingsGildedPress allows Gold >= {ThingsGildedPress.MoldGoldCost} with a Clone target even when MaxHP <= {ThingsGildedPress.SealMaxHpLoss} (WAX_MOLD works).");

        // 4) ThingsSilveredGallery: (CurrentHp > 16 && cloneable) || (Gold >= 95 && upgraded removable && rare relic).
        RunState galleryRun = CreateRun("gate-gallery-relaxed");
        Player galleryPlayer = galleryRun.Players[0];
        galleryPlayer.Gold = 0;
        galleryPlayer.Deck.AddInternal(galleryRun.CreateCard<Feed>(galleryPlayer));
        galleryPlayer.Creature.SetCurrentHpInternal(ThingsSilveredGallery.DuplicateDamage + 1);
        assert(ModelDb.Event<ThingsSilveredGallery>().IsAllowed(galleryRun),
            "ThingsSilveredGallery allows 0 Gold when HP > 16 with cloneable Rare card (DUPLICATE works).");
        galleryPlayer.Creature.SetCurrentHpInternal(ThingsSilveredGallery.DuplicateDamage);
        assert(!ModelDb.Event<ThingsSilveredGallery>().IsAllowed(galleryRun),
            "ThingsSilveredGallery rejects 0 Gold with HP <= 16.");
        galleryPlayer.Gold = ThingsSilveredGallery.PawnGoldCost;
        assert(!ModelDb.Event<ThingsSilveredGallery>().IsAllowed(galleryRun),
            "ThingsSilveredGallery rejects Gold >= 95 with HP <= 16 when no upgraded removable card exists.");
        CardCmd.Upgrade(galleryPlayer.Deck.Cards.First(c => c.IsUpgradable && c.IsRemovable));
        assert(ModelDb.Event<ThingsSilveredGallery>().IsAllowed(galleryRun),
            "ThingsSilveredGallery allows Gold >= 95 + upgraded removable card even when HP <= 16 (PAWN works).");

        // 5) ThingsGrudgeVendingMachine: Gold >= CoinCost (55) || CurrentHp > Kick1Damage (6).
        RunState vendingRun = CreateRun("gate-vending-relaxed");
        Player vendingPlayer = vendingRun.Players[0];
        vendingPlayer.Gold = 0;
        vendingPlayer.Creature.SetCurrentHpInternal(ThingsGrudgeVendingMachine.Kick1Damage + 1);
        assert(ModelDb.Event<ThingsGrudgeVendingMachine>().IsAllowed(vendingRun),
            $"ThingsGrudgeVendingMachine allows 0 Gold with HP > {ThingsGrudgeVendingMachine.Kick1Damage} (kick escalation works).");
        vendingPlayer.Creature.SetCurrentHpInternal(ThingsGrudgeVendingMachine.Kick1Damage);
        vendingPlayer.Gold = ThingsGrudgeVendingMachine.CoinCost - 1;
        assert(!ModelDb.Event<ThingsGrudgeVendingMachine>().IsAllowed(vendingRun),
            $"ThingsGrudgeVendingMachine rejects Gold < {ThingsGrudgeVendingMachine.CoinCost} and HP <= {ThingsGrudgeVendingMachine.Kick1Damage}.");
        vendingPlayer.Gold = ThingsGrudgeVendingMachine.CoinCost;
        assert(ModelDb.Event<ThingsGrudgeVendingMachine>().IsAllowed(vendingRun),
            $"ThingsGrudgeVendingMachine allows Gold >= {ThingsGrudgeVendingMachine.CoinCost} even when HP <= {ThingsGrudgeVendingMachine.Kick1Damage} (INSERT_COIN works).");

        // 6) ThingsShadowCloakroom: no ticket && ((Gold >= DepositCost (45) && depositable) || (Gold >= SwapCost (70) && swappable)).
        RunState cloakRun = CreateRun("gate-cloakroom-relaxed");
        Player cloakPlayer = cloakRun.Players[0];
        cloakPlayer.Gold = ThingsShadowCloakroom.DepositCost;
        assert(ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(cloakRun),
            $"ThingsShadowCloakroom allows {ThingsShadowCloakroom.DepositCost} Gold with depositable card (DEPOSIT works).");
        cloakPlayer.Gold = ThingsShadowCloakroom.DepositCost - 1;
        assert(!ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(cloakRun),
            $"ThingsShadowCloakroom rejects Gold < {ThingsShadowCloakroom.DepositCost}.");
        foreach (CardModel card in cloakPlayer.Deck.Cards)
        {
            if (card.IsUpgradable)
            {
                CardCmd.Upgrade(card);
            }
        }
        cloakPlayer.Gold = ThingsShadowCloakroom.DepositCost;
        assert(!ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(cloakRun),
            $"ThingsShadowCloakroom rejects {ThingsShadowCloakroom.DepositCost} Gold when all cards are upgraded (no depositable card and Gold < {ThingsShadowCloakroom.SwapCost}).");
        cloakPlayer.Gold = ThingsShadowCloakroom.SwapCost;
        assert(ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(cloakRun),
            $"ThingsShadowCloakroom allows {ThingsShadowCloakroom.SwapCost} Gold when all cards are upgraded (SWAP_COAT works).");
        cloakPlayer.AddRelicInternal(ModelDb.Relic<ThingsShadowClaimTicket>().ToMutable());
        assert(!ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(cloakRun),
            "ThingsShadowCloakroom rejects player who already holds ThingsShadowClaimTicket.");
        // 7) Multiplayer Players.Any relaxation: one eligible teammate allows event even when partner is 1 HP / 0 Gold / dead.
        RunState coopAnyRun = CreateRun("gate-coop-any", playerCount: 2);
        coopAnyRun.Players[0].Gold = ThingsShadowCloakroom.DepositCost;
        coopAnyRun.Players[1].Gold = 0;
        coopAnyRun.Players[1].Creature.SetCurrentHpInternal(1m);
        assert(ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(coopAnyRun),
            "ThingsWallCrackLedger allows co-op party when one teammate has HP > 8 even if partner is at 1 HP.");
        assert(ModelDb.Event<ThingsShadowCloakroom>().IsAllowed(coopAnyRun),
            "ThingsShadowCloakroom allows co-op party when one teammate has >= 45 Gold even if partner has 0 Gold.");
        assert(ModelDb.Event<ThingsCrowdedWard>().IsAllowed(coopAnyRun),
            "ThingsCrowdedWard allows co-op party when one teammate is <= 75% HP even if partner is at full HP.");
        coopAnyRun.Players[1].Creature.SetCurrentHpInternal(0m);
        assert(ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(coopAnyRun),
            "ThingsWallCrackLedger does not block living teammates when a partner is dead.");
        coopAnyRun.Players[0].Creature.SetCurrentHpInternal(ThingsWallCrackLedger.LoanDamage);
        assert(!ModelDb.Event<ThingsWallCrackLedger>().IsAllowed(coopAnyRun),
            "ThingsWallCrackLedger rejects co-op party when NO living teammate qualifies.");
    }

    private static void VerifyThingsBackroomsAndUnlitFireGates(Action<bool, string> assert)
    {
        // 1) ThingsBackrooms: solo rejects lethal 1-HP / 5-MaxHP (and 2-HP / 5-MaxHP); co-op uses Players.Any.
        RunState lethalRun = CreateRun("gate-things-backrooms-lethal");
        Player lethalPlayer = lethalRun.Players[0];
        lethalPlayer.Creature.SetMaxHpInternal(ThingsBackrooms.RestMaxHpLoss);
        lethalPlayer.Creature.SetCurrentHpInternal(1m);
        assert(!ModelDb.Event<ThingsBackrooms>().IsAllowed(lethalRun),
            "ThingsBackrooms rejects lethal 1-HP / 5-MaxHP solo player.");
        lethalPlayer.Creature.SetCurrentHpInternal(ThingsBackrooms.FirstSearchDamage);
        assert(!ModelDb.Event<ThingsBackrooms>().IsAllowed(lethalRun),
            "ThingsBackrooms rejects lethal 2-HP / 5-MaxHP solo player.");
        lethalPlayer.Creature.SetCurrentHpInternal(ThingsBackrooms.FirstSearchDamage + 1);
        assert(ModelDb.Event<ThingsBackrooms>().IsAllowed(lethalRun),
            "ThingsBackrooms allows 3-HP / 5-MaxHP player (can survive first search).");
        lethalPlayer.Creature.SetMaxHpInternal(ThingsBackrooms.RestMaxHpLoss + 1);
        lethalPlayer.Creature.SetCurrentHpInternal(1m);
        assert(ModelDb.Event<ThingsBackrooms>().IsAllowed(lethalRun),
            "ThingsBackrooms allows 1-HP / 6-MaxHP player (REST is unlocked and non-lethal).");

        RunState coopLethalRun = CreateRun("gate-things-backrooms-coop", playerCount: 2);
        coopLethalRun.Players[1].Creature.SetMaxHpInternal(ThingsBackrooms.RestMaxHpLoss);
        coopLethalRun.Players[1].Creature.SetCurrentHpInternal(1m);
        assert(ModelDb.Event<ThingsBackrooms>().IsAllowed(coopLethalRun),
            "ThingsBackrooms allows co-op party when one teammate qualifies (1-HP/5-MaxHP partner gets safe LEAVE fallback).");
        coopLethalRun.Players[0].Creature.SetMaxHpInternal(ThingsBackrooms.RestMaxHpLoss);
        coopLethalRun.Players[0].Creature.SetCurrentHpInternal(1m);
        assert(!ModelDb.Event<ThingsBackrooms>().IsAllowed(coopLethalRun),
            "ThingsBackrooms rejects co-op party when ALL players are at lethal 1-HP / 5-MaxHP.");

        // 2) ThingsUnlitFire: uses Players.Any so one teammate holding ThingsBorrowedEmber or being dead does not block others.
        RunState unlitRun = CreateRun("gate-unlit-fire-coop", playerCount: 2);
        unlitRun.Act.GenerateRooms(unlitRun.Rng.UpFront, UnlockState.all, isMultiplayer: true);
        unlitRun.Map = unlitRun.Act.CreateMap(unlitRun, false);
        assert(ThingsBorrowedEmber.FindNextRestSites(unlitRun).Count > 0,
            "Generated Depths map exposes upcoming rest sites for ThingsUnlitFire check.");
        assert(ModelDb.Event<ThingsUnlitFire>().IsAllowed(unlitRun),
            "ThingsUnlitFire allows co-op party when all players are alive and lack ThingsBorrowedEmber.");
        unlitRun.Players[1].AddRelicInternal(ModelDb.Relic<ThingsBorrowedEmber>().ToMutable());
        assert(ModelDb.Event<ThingsUnlitFire>().IsAllowed(unlitRun),
            "ThingsUnlitFire allows co-op party when at least one living teammate lacks ThingsBorrowedEmber.");
        unlitRun.Players[0].AddRelicInternal(ModelDb.Relic<ThingsBorrowedEmber>().ToMutable());
        assert(!ModelDb.Event<ThingsUnlitFire>().IsAllowed(unlitRun),
            "ThingsUnlitFire rejects co-op party when ALL living teammates already hold ThingsBorrowedEmber.");

        RunState unlitDeadRun = CreateRun("gate-unlit-fire-dead", playerCount: 2);
        unlitDeadRun.Act.GenerateRooms(unlitDeadRun.Rng.UpFront, UnlockState.all, isMultiplayer: true);
        unlitDeadRun.Map = unlitDeadRun.Act.CreateMap(unlitDeadRun, false);
        unlitDeadRun.Players[1].Creature.SetCurrentHpInternal(0m);
        assert(ModelDb.Event<ThingsUnlitFire>().IsAllowed(unlitDeadRun),
            "ThingsUnlitFire allows co-op party when at least one living teammate lacks ThingsBorrowedEmber.");
        unlitDeadRun.Players[0].Creature.SetCurrentHpInternal(0m);
        assert(!ModelDb.Event<ThingsUnlitFire>().IsAllowed(unlitDeadRun),
            "ThingsUnlitFire rejects co-op party when ALL players are dead.");
    }
}
