using System.Reflection;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private static Action<NCreature>? _simulatedThirdPartyCreatureReady;

    [HarmonyPriority(Priority.Normal)]
    private static void SimulatedThirdPartyCreatureReadyPostfix(NCreature __instance)
    {
        _simulatedThirdPartyCreatureReady?.Invoke(__instance);
    }

    private async Task VerifyLeechSurrenderAndZIndexRegressions()
    {
        var failures = new List<string>();
        async Task RunCase(string name, Func<Task> test)
        {
            try
            {
                await test();
                GD.Print($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"  [FAIL] {name}: {ex.Message}");
                failures.Add($"{name}: {ex.Message}");
            }
            finally
            {
                DeactivateSyntheticCombat();
            }
        }

        // 1) Report 443e4f37: Wake -> ThingsSurrender -> PerformMove -> RollMove must roll BROOD_MOVE, never SLEEP_MOVE.
        await RunCase("LeechMother.WakeThenSurrenderTransitionsToBrood", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsLeechMotherEncounter>(), players: 1, ascension: 0, seed: "leech-wake-surrender");
            Player player = b.Players[0];
            player.PlayerCombatState!.Energy = 99;
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(900);
            var mother = b.Monster<ThingsLeechMother>();
            await mother.AfterAddedToRoom();

            Assert(!mother.IsAwake && mother.NextMove.Id == ThingsLeechMother.SleepMoveId,
                $"Expected sleeping opening state, got awake={mother.IsAwake}, move={mother.NextMove.Id}.");

            await mother.Wake();
            Assert(mother.IsAwake && mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"Wake must switch NextMove to BROOD_MOVE, got {mother.NextMove.Id}.");
            Assert(mother.MoveStateMachine!.StateLog.Last().Id == ThingsLeechMother.SleepMoveId,
                "Precondition: StateLog still points at stale SLEEP_MOVE after SetMoveImmediate.");

            var surrender = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender, mother.Creature, skipCardPileVisuals: true);

            Assert(mother.NextMove.Id == "DEBUFF_WEAK",
                $"ThingsSurrender should override NextMove to DEBUFF_WEAK, got {mother.NextMove.Id}.");
            Assert(mother.NextMove.FollowUpStateId == ThingsLeechMother.BroodMoveId,
                $"ThingsSurrender must target live NextMove (BROOD_MOVE), not stale StateLog (SLEEP_MOVE); got {mother.NextMove.FollowUpStateId}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove();
            Assert(player.Creature.GetPower<WeakPower>()?.Amount >= 1,
                "DEBUFF_WEAK must apply Weak to the player on Turn 1.");
            await SideEnded(b, CombatSide.Enemy, b.Enemies);

            b.State.CurrentSide = CombatSide.Player;
            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"Turn 2 RollMove after Wake -> ThingsSurrender must be BROOD_MOVE, got {mother.NextMove.Id}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            Assert(b.State.Enemies.Count(c => c.Monster is ThingsSanguineLeech) == 2,
                "BROOD_MOVE on Turn 2 must summon both Sanguine Leeches.");

            b.State.CurrentSide = CombatSide.Player;
            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == "SIP_MOVE",
                $"Turn 3 RollMove after BROOD_MOVE must be SIP_MOVE, got {mother.NextMove.Id}.");
        });

        // 2) Reverse order: ThingsSurrender BEFORE damaging/waking her -> attack wakes her -> Turn 1 performs DEBUFF_WEAK -> Turn 2 rolls BROOD_MOVE!
        await RunCase("LeechMother.SurrenderBeforeWakeTransitionsToBrood", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsLeechMotherEncounter>(), players: 1, ascension: 0, seed: "leech-surrender-wake");
            Player player = b.Players[0];
            player.PlayerCombatState!.Energy = 99;
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(900);
            var mother = b.Monster<ThingsLeechMother>();
            await mother.AfterAddedToRoom();

            var surrender = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender, mother.Creature, skipCardPileVisuals: true);

            Assert(!mother.IsAwake && mother.NextMove.Id == "DEBUFF_WEAK" && !mother.NextMove.CanTransitionAway,
                $"Playing ThingsSurrender while asleep must queue non-transitionable DEBUFF_WEAK, got awake={mother.IsAwake}, move={mother.NextMove.Id}, canTransition={mother.NextMove.CanTransitionAway}.");
            Assert(mother.NextMove.FollowUpStateId == ThingsLeechMother.SleepMoveId,
                "Precondition: DEBUFF_WEAK queued while asleep initially records SLEEP_MOVE as FollowUpStateId.");

            var strike = b.State.CreateCard<StrikeIronclad>(player);
            await DamageCmd.Attack(6).FromCard(strike, null).Targeting(mother.Creature).Execute(Choice);

            Assert(mother.IsAwake
                && !mother.Creature.HasPower<ThingsLeechMotherSlumberPower>()
                && !mother.Creature.HasPower<PlatingPower>(),
                "Attacking LeechMother after ThingsSurrender must still wake her and clear Slumber + Plating.");
            Assert(mother.NextMove.Id == "DEBUFF_WEAK",
                $"Non-transitionable DEBUFF_WEAK must still be performed on Turn 1, got {mother.NextMove.Id}.");
            Assert(mother.NextMove.FollowUpState != null && mother.NextMove.FollowUpState.Id != ThingsLeechMother.SleepMoveId,
                $"Wake() must redirect non-transitionable FollowUpState away from SLEEP_MOVE, got {mother.NextMove.FollowUpState?.Id ?? "<null>"}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove();
            Assert(player.Creature.GetPower<WeakPower>()?.Amount >= 1,
                "LeechMother must perform DEBUFF_WEAK on Turn 1.");
            await SideEnded(b, CombatSide.Enemy, b.Enemies);

            b.State.CurrentSide = CombatSide.Player;
            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"Turn 2 RollMove after ThingsSurrender -> Wake must be BROOD_MOVE (never SLEEP_MOVE), got {mother.NextMove.Id}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            b.State.CurrentSide = CombatSide.Player;
            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == "SIP_MOVE",
                $"Turn 3 RollMove after BROOD_MOVE must be SIP_MOVE, got {mother.NextMove.Id}.");
        });

        // 3) External forced SLEEP_MOVE after Wake() escapes SLEEP_MOVE immediately (zero infinite loop), both before and after brood summon.
        await RunCase("LeechMother.ExternalForcedSleepWhileAwakeEscapesImmediately", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsLeechMotherEncounter>(), players: 1, ascension: 0, seed: "leech-forced-sleep");
            var mother = b.Monster<ThingsLeechMother>();
            await mother.AfterAddedToRoom();

            await mother.Wake();
            await CreatureCmd.Stun(mother.Creature);
            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove(); // Marks first move performed without summoning brood.

            var sleepState = (MoveState)mother.MoveStateMachine!.States[ThingsLeechMother.SleepMoveId];
            mother.SetMoveImmediate(sleepState, forceTransition: true);
            Assert(mother.NextMove.Id == ThingsLeechMother.SleepMoveId,
                "Precondition: externally forced SLEEP_MOVE while awake (pre-brood).");

            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"RollMove must escape externally forced SLEEP_MOVE via AFTER_SLEEP_BRANCH to BROOD_MOVE, got {mother.NextMove.Id}.");

            // Also verify AfterSideTurnEnd & AfterSideTurnStart escape an externally forced SLEEP_MOVE directly.
            mother.SetMoveImmediate(sleepState, forceTransition: true);
            await mother.AfterSideTurnEnd(Choice, CombatSide.Enemy, b.Enemies);
            Assert(mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"AfterSideTurnEnd must immediately escape forced SLEEP_MOVE while awake (pre-brood), got {mother.NextMove.Id}.");

            mother.SetMoveImmediate(sleepState, forceTransition: true);
            await mother.AfterSideTurnStart(CombatSide.Enemy, b.Enemies, b.State);
            Assert(mother.NextMove.Id == ThingsLeechMother.BroodMoveId,
                $"AfterSideTurnStart must immediately escape forced SLEEP_MOVE while awake (pre-brood), got {mother.NextMove.Id}.");

            // Perform BROOD_MOVE so _hasSummonedBrood == true, then force SLEEP_MOVE again.
            b.State.CurrentSide = CombatSide.Enemy;
            await mother.PerformMove();
            Assert(b.State.Enemies.Count(c => c.Monster is ThingsSanguineLeech) == 2,
                "Precondition: brood summoned.");

            mother.SetMoveImmediate(sleepState, forceTransition: true);
            mother.RollMove(b.Players.Select(p => p.Creature));
            Assert(mother.NextMove.Id == "SIP_MOVE",
                $"Post-brood RollMove from externally forced SLEEP_MOVE must route to SIP_MOVE (never SLEEP_MOVE or second BROOD_MOVE), got {mother.NextMove.Id}.");

            mother.SetMoveImmediate(sleepState, forceTransition: true);
            await mother.AfterSideTurnEnd(Choice, CombatSide.Enemy, b.Enemies);
            Assert(mother.NextMove.Id == "SIP_MOVE",
                $"Post-brood AfterSideTurnEnd from externally forced SLEEP_MOVE must route to SIP_MOVE, got {mother.NextMove.Id}.");
        });

        // 4) ThingsSurrender live NextMove resolution after SetMoveImmediate (both registered move & transient move fallback).
        await RunCase("ThingsSurrender.ResolvesLiveNextMoveAfterSetMoveImmediate", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsSanguineLeechWeak>(), players: 1, ascension: 0, seed: "surrender-live-nextmove");
            Player player = b.Players[0];
            player.PlayerCombatState!.Energy = 99;
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(900);

            MonsterModel monster = b.Enemies[0].Monster!;
            string initialLoggedId = monster.MoveStateMachine!.StateLog.Last().Id;
            MoveState alternateMove = monster.MoveStateMachine.States.Values
                .OfType<MoveState>()
                .First(m => m.Id != initialLoggedId);

            // Part A: SetMoveImmediate to a different registered move leaves StateLog stale at initialLoggedId.
            monster.SetMoveImmediate(alternateMove, forceTransition: true);
            Assert(monster.NextMove.Id == alternateMove.Id && monster.MoveStateMachine.StateLog.Last().Id == initialLoggedId,
                "Precondition: NextMove diverged from stale StateLog after SetMoveImmediate.");

            var surrender1 = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender1, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender1, monster.Creature, skipCardPileVisuals: true);

            Assert(monster.NextMove.Id == "DEBUFF_WEAK" && monster.NextMove.FollowUpStateId == alternateMove.Id,
                $"ThingsSurrender must resume live NextMove ({alternateMove.Id}), not stale StateLog ({initialLoggedId}); got {monster.NextMove.FollowUpStateId}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await monster.PerformMove();
            b.State.CurrentSide = CombatSide.Player;
            monster.RollMove(b.Players.Select(p => p.Creature));
            Assert(monster.NextMove.Id == alternateMove.Id,
                $"RollMove after DEBUFF_WEAK must transition to live move {alternateMove.Id}, got {monster.NextMove.Id}.");

            // Part B: Unregistered transient MoveState (CanTransitionAway = true) falls back to liveMove.FollowUpState / FollowUpStateId.
            var transient = new MoveState("TRANSIENT_PROBE_MOVE", _ => Task.CompletedTask, new UnknownIntent())
            {
                FollowUpStateId = initialLoggedId,
                MustPerformOnceBeforeTransitioning = false
            };
            monster.SetMoveImmediate(transient, forceTransition: true);

            var surrender2 = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender2, monster.Creature, skipCardPileVisuals: true);

            Assert(monster.NextMove.Id == "DEBUFF_WEAK" && monster.NextMove.FollowUpStateId == initialLoggedId,
                $"ThingsSurrender on transient NextMove must resolve FollowUpStateId ({initialLoggedId}), got {monster.NextMove.FollowUpStateId}.");
        });

        // 4b) Sanguine Leech parasite-triggered REINFEST_MOVE + ThingsSurrender:
        //     Playing parasite switches Leech into REINFEST_MOVE next turn; ThingsSurrender MUST override REINFEST_MOVE to DEBUFF_WEAK,
        //     prevent infestation that turn, resume REINFEST_MOVE next turn, and then resume the saved normal cycle move.
        await RunCase("SanguineLeech.SurrenderOverridesReinfestMoveAndResumesCycle", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsSanguineLeechWeak>(), players: 1, ascension: 0, seed: "leech-reinfest-surrender");
            Player player = b.Players[0];
            player.PlayerCombatState!.Energy = 99;
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(900);
            ThingsSanguineLeech leech = (ThingsSanguineLeech)b.Enemies[0].Monster!;
            string[] cycle = ["SIP_MOVE", "INFEST_MOVE", "SIP_AGAIN_MOVE", "CURL_MOVE"];
            string openingMove = cycle[leech.OpeningPhase];
            string nextNormalMove = cycle[(leech.OpeningPhase + 1) % cycle.Length];
            Assert(leech.NextMove.Id == openingMove, $"Expected opening move {openingMove}, got {leech.NextMove.Id}.");

            // Turn 1: Play a parasite bound to leech[0].
            var parasite1 = b.State.CreateCard<ThingsLeechParasite>(player);
            parasite1.BindOwnerLeech(leech.Creature);
            await CardPileCmd.AddGeneratedCardToCombat(parasite1, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, parasite1, null, skipCardPileVisuals: true);
            Assert(leech.NextMove.Id == openingMove,
                $"Playing parasite on Turn 1 must keep Turn 1 move ({openingMove}), got {leech.NextMove.Id}.");

            // Enemy Turn 1 -> Turn 2 RollMove enters REINFEST_MOVE (interruptible!).
            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == ThingsSanguineLeech.ReinfestMoveId && leech.NextMove.CanTransitionAway,
                $"Turn 2 must roll interruptible REINFEST_MOVE, got id={leech.NextMove.Id}, canTransition={leech.NextMove.CanTransitionAway}.");
            Assert(leech.MoveStateMachine!.StateLog.Last().Id == ThingsSanguineLeech.ReinfestMoveId,
                $"Turn 2 StateLog must record REINFEST_MOVE, got {leech.MoveStateMachine.StateLog.Last().Id}.");

            // Turn 2: Play ThingsSurrender on the leech during its REINFEST_MOVE turn!
            int hpBefore = player.Creature.CurrentHp;
            int parasitesBefore = player.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Count();
            var surrender1 = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender1, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender1, leech.Creature, skipCardPileVisuals: true);

            Assert(player.Creature.CurrentHp == hpBefore + 3,
                $"ThingsSurrender must heal 3 HP, got {player.Creature.CurrentHp - hpBefore}.");
            Assert(leech.NextMove.Id == "DEBUFF_WEAK" && !leech.NextMove.CanTransitionAway,
                $"ThingsSurrender must override REINFEST_MOVE to DEBUFF_WEAK, got {leech.NextMove.Id} (canTransition={leech.NextMove.CanTransitionAway}).");
            Assert(leech.NextMove.FollowUpStateId == ThingsSanguineLeech.ReinfestMoveId,
                $"ThingsSurrender on REINFEST_MOVE must set FollowUpStateId=REINFEST_MOVE, got {leech.NextMove.FollowUpStateId}.");

            // Enemy Turn 2: Performs DEBUFF_WEAK (applies Weak, adds NO parasite).
            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            Assert(player.Creature.GetPower<WeakPower>()?.Amount >= 1,
                "DEBUFF_WEAK on Turn 2 must apply Weak.");
            Assert(player.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Count() == parasitesBefore,
                "Surrendered REINFEST_MOVE must NOT add a parasite on Turn 2.");

            // Turn 3: RollMove resumes REINFEST_MOVE; test a second ThingsSurrender + another parasite clear on the same turn!
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == ThingsSanguineLeech.ReinfestMoveId && leech.NextMove.CanTransitionAway,
                $"Turn 3 RollMove after DEBUFF_WEAK must resume interruptible REINFEST_MOVE, got {leech.NextMove.Id}.");

            var surrender2 = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender2, leech.Creature, skipCardPileVisuals: true);
            var parasite2 = b.State.CreateCard<ThingsLeechParasite>(player);
            parasite2.BindOwnerLeech(leech.Creature);
            await CardPileCmd.AddGeneratedCardToCombat(parasite2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, parasite2, null, skipCardPileVisuals: true);
            Assert(leech.NextMove.Id == "DEBUFF_WEAK",
                $"Playing another parasite after ThingsSurrender on Turn 3 must keep DEBUFF_WEAK, got {leech.NextMove.Id}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);

            // Turn 4: RollMove resumes REINFEST_MOVE and performs it.
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == ThingsSanguineLeech.ReinfestMoveId,
                $"Turn 4 RollMove must be REINFEST_MOVE, got {leech.NextMove.Id}.");
            int parasitesBeforeInfest = player.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Count();
            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            Assert(player.PlayerCombatState.Hand.Cards.OfType<ThingsLeechParasite>().Count() == parasitesBeforeInfest + 1,
                "Resumed REINFEST_MOVE on Turn 4 must generate a bound parasite.");

            // Turn 5: Must resume the saved normal cycle move (no infinite REINFEST_MOVE loop).
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == nextNormalMove,
                $"Turn 5 RollMove after resumed REINFEST_MOVE must return to saved normal move {nextNormalMove}, got {leech.NextMove.Id}.");
        });

        // 4c) Reverse order (ThingsSurrender BEFORE parasite on Turn 1) + native Stun (Whistle) on REINFEST_MOVE:
        //     Turn 1 performs DEBUFF_WEAK -> Turn 2 rolls REINFEST_MOVE -> native Stun overrides REINFEST_MOVE ->
        //     Turn 3 performs REINFEST_MOVE -> Turn 4 resumes Turn 1's postponed opening move.
        await RunCase("SanguineLeech.SurrenderBeforeParasiteAndNativeStunOnReinfest", async () =>
        {
            var b = await StrongBattle(ModelDb.Encounter<ThingsSanguineLeechWeak>(), players: 1, ascension: 0, seed: "leech-surrender-before-parasite");
            Player player = b.Players[0];
            player.PlayerCombatState!.Energy = 99;
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(900);
            ThingsSanguineLeech leech = (ThingsSanguineLeech)b.Enemies[0].Monster!;
            string openingMove = leech.NextMove.Id;

            // Turn 1: Play ThingsSurrender FIRST, then play bound parasite.
            var surrender = b.State.CreateCard<ThingsSurrender>(player);
            await CardPileCmd.AddGeneratedCardToCombat(surrender, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, surrender, leech.Creature, skipCardPileVisuals: true);
            Assert(leech.NextMove.Id == "DEBUFF_WEAK" && leech.NextMove.FollowUpStateId == openingMove,
                $"ThingsSurrender on Turn 1 must queue DEBUFF_WEAK -> {openingMove}, got {leech.NextMove.Id} -> {leech.NextMove.FollowUpStateId}.");

            var parasite = b.State.CreateCard<ThingsLeechParasite>(player);
            parasite.BindOwnerLeech(leech.Creature);
            await CardPileCmd.AddGeneratedCardToCombat(parasite, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, parasite, null, skipCardPileVisuals: true);
            Assert(leech.NextMove.Id == "DEBUFF_WEAK",
                $"Playing parasite after ThingsSurrender on Turn 1 must not clobber DEBUFF_WEAK, got {leech.NextMove.Id}.");

            // Enemy Turn 1 -> Turn 2 rolls REINFEST_MOVE.
            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == ThingsSanguineLeech.ReinfestMoveId && leech.NextMove.CanTransitionAway,
                $"Turn 2 after DEBUFF_WEAK + parasite must roll interruptible REINFEST_MOVE, got {leech.NextMove.Id}.");

            // Turn 2: Native Whistle-style Stun (nextMoveId = null) must also override REINFEST_MOVE and follow up with REINFEST_MOVE!
            await CreatureCmd.Stun(leech.Creature);
            Assert(leech.NextMove.Id == MonsterModel.stunnedMoveId && leech.NextMove.FollowUpStateId == ThingsSanguineLeech.ReinfestMoveId,
                $"Native Stun on REINFEST_MOVE must set STUNNED -> REINFEST_MOVE, got {leech.NextMove.Id} -> {leech.NextMove.FollowUpStateId}.");

            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);

            // Turn 3: Resumes and performs REINFEST_MOVE.
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == ThingsSanguineLeech.ReinfestMoveId,
                $"Turn 3 after Stun must resume REINFEST_MOVE, got {leech.NextMove.Id}.");
            b.State.CurrentSide = CombatSide.Enemy;
            await leech.PerformMove();
            await SideEnded(b, CombatSide.Enemy, b.Enemies);

            // Turn 4: Resumes Turn 1's postponed opening move!
            b.State.CurrentSide = CombatSide.Player;
            leech.RollMove(b.Players.Select(p => p.Creature));
            Assert(leech.NextMove.Id == openingMove,
                $"Turn 4 after Turn 1 Surrender -> Turn 2 Stun -> Turn 3 Reinfest must resume {openingMove}, got {leech.NextMove.Id}.");
        });

        // 5) Report 9cb59828: BossStateDisplayLayerPatch clamps mid-combat summon (ThingsOriginEyeWithTeeth) against third-party Z overrides & resets %Intents on combat end.
        await RunCase("BossStateDisplayLayerPatch.ClampsMidCombatSummonAndResetsOnCombatEnd", async () =>
        {
            var extStatus = GDExtensionManager.LoadExtension(Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
            Assert(extStatus is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, "Spine extension loaded.");
            try { ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly); } catch (ArgumentException) { }

            Type? atlasType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
            ResourceFormatLoader? atlas = atlasType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
            if (atlas != null) ResourceLoader.AddResourceFormatLoader(atlas, true);

            var testHarmony = new Harmony($"DepthsProbe.ZIndexClamping.{Guid.NewGuid():N}");
            testHarmony.Patch(
                AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            testHarmony.Patch(
                AccessTools.Method(typeof(NCreature), nameof(NCreature._Ready)),
                postfix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SimulatedThirdPartyCreatureReadyPostfix))
                {
                    priority = Priority.Normal
                });

            var sceneContainer = new Control { Name = "CombatSceneContainer", ZAsRelative = true, ZIndex = -10 };
            var enemyContainer = new Control { Name = "EnemyContainer", ZAsRelative = true, ZIndex = 0 };
            sceneContainer.AddChild(enemyContainer);
            AddChild(sceneContainer);

            try
            {
                Type patchType = typeof(STS2_ThingsInit).Assembly.GetType(
                    "STS2_Things.Hooks.BossStateDisplayLayerPatch",
                    throwOnError: true)!;
                System.Reflection.MethodInfo computeEffectiveZ = patchType.GetMethod(
                    "ComputeEffectiveZIndex",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
                System.Reflection.MethodInfo applyLayerPatch = patchType.GetMethod(
                    "Apply",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
                System.Reflection.MethodInfo onCombatEnded = AccessTools.Method(typeof(NCreature), "OnCombatEnded", [typeof(CombatRoom)])!;

                int EffectiveZ(CanvasItem item) => (int)computeEffectiveZ.Invoke(null, [item])!;

                Assert(EffectiveZ(enemyContainer) == -10,
                    $"Expected EnemyContainer effective Z=-10 under CombatSceneContainer, got {EffectiveZ(enemyContainer)}.");

                var b = await StrongBattle(ModelDb.Encounter<ThingsOriginFogmogBossEncounter>(), players: 1, ascension: 0, seed: "origin-eye-zindex");

                NCreature SpawnOriginEyeNode(string slot)
                {
                    var eyeModel = (ThingsOriginEyeWithTeeth)ModelDb.Monster<ThingsOriginEyeWithTeeth>().ToMutable();
                    Creature eyeEntity = b.State.CreateCreature(eyeModel, CombatSide.Enemy, slot);
                    b.State.AddCreature(eyeEntity);
                    eyeModel.SetUpForCombat();
                    eyeModel.RollMove(b.Players.Select(p => p.Creature));

                    var node = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                    typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, eyeEntity);
                    typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, eyeModel.CreateVisuals());
                    enemyContainer.AddChild(node);
                    return node;
                }

                // 5a) Standard mid-combat summon under SceneContainer (ZIndex = -10) elevates safely to +4 (effective -6 <= -1).
                _simulatedThirdPartyCreatureReady = null;
                NCreature normalEye = SpawnOriginEyeNode("illusion1");
                NCreatureStateDisplay normalHp = normalEye.GetNode<NCreatureStateDisplay>("%HealthBar");
                Control normalIntents = normalEye.IntentContainer;
                Assert(EffectiveZ(normalEye) == -10 && normalHp.ZIndex == 4 && normalIntents.ZIndex == 4,
                    $"Normal mid-combat summon should elevate HP/Intents to relative Z=4 (effective -6), got creatureZ={EffectiveZ(normalEye)}, hpZ={normalHp.ZIndex}, intentZ={normalIntents.ZIndex}.");

                // 5b) Third-party NCreature._Ready Postfix sets ZAsRelative = false, ZIndex = 0 (RegentFX / Report 9cb59828):
                //     Priority.Last BossStateDisplayCreatureReadyPatch must clamp BOTH %HealthBar and %Intents back to 0 synchronously!
                _simulatedThirdPartyCreatureReady = c =>
                {
                    c.ZAsRelative = false;
                    c.ZIndex = 0;
                };
                NCreature absZeroEye = SpawnOriginEyeNode("illusion2");
                NCreatureStateDisplay absZeroHp = absZeroEye.GetNode<NCreatureStateDisplay>("%HealthBar");
                Control absZeroIntents = absZeroEye.IntentContainer;
                Assert(EffectiveZ(absZeroEye) == 0 && absZeroHp.ZIndex == 0 && absZeroIntents.ZIndex == 0,
                    $"Third-party ZAsRelative=false, ZIndex=0 in _Ready must synchronously clamp HP/Intents back to 0, got creatureZ={EffectiveZ(absZeroEye)}, hpZ={absZeroHp.ZIndex}, intentZ={absZeroIntents.ZIndex}.");

                // 5c) Third-party NCreature._Ready Postfix sets ZAsRelative = true, ZIndex = 10 under SceneContainer (-10):
                //     Effective Z is 0 -> must also clamp BOTH %HealthBar and %Intents back to 0 synchronously!
                _simulatedThirdPartyCreatureReady = c =>
                {
                    c.ZAsRelative = true;
                    c.ZIndex = 10;
                };
                NCreature relTenEye = SpawnOriginEyeNode("illusion1");
                NCreatureStateDisplay relTenHp = relTenEye.GetNode<NCreatureStateDisplay>("%HealthBar");
                Control relTenIntents = relTenEye.IntentContainer;
                Assert(EffectiveZ(relTenEye) == 0 && relTenHp.ZIndex == 0 && relTenIntents.ZIndex == 0,
                    $"Third-party ZAsRelative=true, ZIndex=10 under -10 container must clamp HP/Intents back to 0, got creatureZ={EffectiveZ(relTenEye)}, hpZ={relTenHp.ZIndex}, intentZ={relTenIntents.ZIndex}.");

                // 5d) Late post-_Ready mutation (simulating third-party NCombatRoom.AddCreature postfix) is caught by CallDeferred().
                _simulatedThirdPartyCreatureReady = null;
                NCreature lateMutatedEye = SpawnOriginEyeNode("illusion2");
                NCreatureStateDisplay lateHp = lateMutatedEye.GetNode<NCreatureStateDisplay>("%HealthBar");
                Control lateIntents = lateMutatedEye.IntentContainer;
                Assert(lateHp.ZIndex == 4 && lateIntents.ZIndex == 4,
                    "Precondition: lateMutatedEye initially elevated to ZIndex=4 inside _Ready.");
                lateMutatedEye.ZAsRelative = false;
                lateMutatedEye.ZIndex = 0;

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                Assert(EffectiveZ(lateMutatedEye) == 0 && lateHp.ZIndex == 0 && lateIntents.ZIndex == 0,
                    $"Deferred check after _Ready must clamp late ZAsRelative=false mutation back to 0, got hpZ={lateHp.ZIndex}, intentZ={lateIntents.ZIndex}.");
                Assert(absZeroHp.ZIndex == 0 && absZeroIntents.ZIndex == 0 && relTenHp.ZIndex == 0 && relTenIntents.ZIndex == 0,
                    "Clamped nodes remain at ZIndex=0 after deferred pass.");

                // 5e) Direct Apply check: absolute negative Z (e.g., ZAsRelative=false, ZIndex=-5) still clamps to effective <= -1,
                //     and non-relative stateDisplay (ZAsRelative=false) is left untouched.
                absZeroEye.ZAsRelative = false;
                absZeroEye.ZIndex = -5;
                applyLayerPatch.Invoke(null, [absZeroEye]);
                Assert(absZeroHp.ZIndex == 4 && EffectiveZ(absZeroHp) == -1 && absZeroIntents.ZIndex == 4 && EffectiveZ(absZeroIntents) == -1,
                    $"ZAsRelative=false, ZIndex=-5 must cap relative HUD Z at 4 (effective -1), got hpZ={absZeroHp.ZIndex} (eff {EffectiveZ(absZeroHp)}), intentZ={absZeroIntents.ZIndex} (eff {EffectiveZ(absZeroIntents)}).");

                absZeroHp.ZAsRelative = false;
                absZeroHp.ZIndex = 2;
                absZeroEye.ZAsRelative = false;
                absZeroEye.ZIndex = 0;
                applyLayerPatch.Invoke(null, [absZeroEye]);
                Assert(!absZeroHp.ZAsRelative && absZeroHp.ZIndex == 2 && absZeroIntents.ZIndex == 0,
                    "Apply must leave non-relative stateDisplay untouched while clamping relative intentContainer to 0.");

                // 5f) BossStateDisplayCombatEndResetPatch resets BOTH %HealthBar and %Intents back to 0 on OnCombatEnded.
                Assert(normalHp.ZIndex == 4 && normalIntents.ZIndex == 4,
                    "Precondition: normalEye still has elevated %HealthBar and %Intents before combat end.");
                onCombatEnded.Invoke(normalEye, [b.Room]);
                Assert(normalHp.ZIndex == 0 && normalIntents.ZIndex == 0,
                    $"OnCombatEnded must reset BOTH %HealthBar and %Intents relative ZIndex to 0, got hpZ={normalHp.ZIndex}, intentZ={normalIntents.ZIndex}.");
            }
            finally
            {
                _simulatedThirdPartyCreatureReady = null;
                testHarmony.UnpatchAll(testHarmony.Id);
                if (GodotObject.IsInstanceValid(sceneContainer))
                    sceneContainer.Free();
                if (atlas != null)
                    ResourceLoader.RemoveResourceFormatLoader(atlas);
            }
        });

        Assert(failures.Count == 0, $"Leech/Surrender/ZIndex regression failures ({failures.Count}):\n" + string.Join("\n", failures));
        GD.Print($"PASS LeechMother/ThingsSurrender/BossStateDisplayLayerPatch regressions ({_checks} total assertions).");
    }
}
