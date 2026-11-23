using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Compatibility;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

internal static class DepthsExternalSimulationRegression
{
    private static readonly ThrowingPlayerChoiceContext Choice = new();

    private static readonly FieldInfo PowerInternalDataField =
        typeof(PowerModel).GetField("_internalData", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_internalData");

    private sealed record BattleFixture(RunState Run, CombatRoom Room, EncounterModel Encounter)
    {
        public CombatState State => Room.CombatState;
        public IReadOnlyList<Player> Players => Run.Players;
        public Creature[] Enemies => State.Enemies.Where(c => c.IsAlive).ToArray();
        public T Monster<T>() where T : MonsterModel => Enemies.Select(c => c.Monster).OfType<T>().Single();
    }

    public static async Task RunAsync(Action<bool, string> assert)
    {
        try
        {
            await VerifyCaveGodSimulationIsolationAsync(assert);
            await VerifyAbyssalInkmawSimulationIsolationAsync(assert);
            await VerifyHumanFaceColumnRedirectHardeningAsync(assert);
            GD.Print("PASS Depths external live-state simulation isolation & HumanFaceColumn redirect hardening regressions.");
        }
        finally
        {
            DeactivateSyntheticCombat();
        }
    }

    private static async Task VerifyCaveGodSimulationIsolationAsync(Action<bool, string> assert)
    {
        BattleFixture battle = await CreateBattleAsync(
            ModelDb.Encounter<ThingsCaveGodBossEncounter>(),
            players: 2,
            ascension: 0,
            seed: "ext-sim-cavegod");
        try
        {
            foreach (Creature enemy in battle.State.Enemies.ToList())
                await enemy.Monster!.AfterAddedToRoom();
            foreach (Creature enemy in battle.State.Enemies.ToList())
                await enemy.Monster!.BeforeCombatStart();

            ThingsCaveGodBody body = battle.Monster<ThingsCaveGodBody>();
            ThingsCaveGodLeftHand left = battle.Monster<ThingsCaveGodLeftHand>();
            Player p0 = battle.Players[0];

            int initialBodyHp = body.Creature.CurrentHp;
            int initialLeftHp = left.Creature.CurrentHp;
            int threshold = body.EnrageHpThreshold;
            string initialBodyMove = body.NextMove.Id;

            assert(!body.IsEnrageLocked && body.Phase == 1 && !left.IsDown,
                "CaveGod starts in Phase 1 unlocked with healthy arms.");

            using (ExternalCombatSimulationGuard.BeginSimulatedExternalPreviewForTest())
            {
                assert(ExternalCombatSimulationGuard.IsActive(body.Creature),
                    "ExternalCombatSimulationGuard reports active inside simulated preview scope.");

                await CreatureCmd.Damage(
                    Choice,
                    body.Creature,
                    5000m,
                    ValueProp.Unpowered | ValueProp.Unblockable,
                    p0.Creature);

                assert(body.Creature.CurrentHp == threshold,
                    $"Simulated preview still clamps CaveGodBody HP at EnrageHpThreshold ({threshold}), got {body.Creature.CurrentHp}.");
                assert(!body.IsEnrageLocked,
                    "Simulated preview crossing CaveGodBody threshold must NOT set _isEnrageLocked.");
                assert(body.Creature.HpDisplay == HpDisplay.Normal,
                    $"Simulated preview must NOT flip CaveGodBody HpDisplay to Infinite, got {body.Creature.HpDisplay}.");
                assert(body.NextMove.Id == initialBodyMove &&
                       body.NextMove.Id != ThingsCaveGodBody.EnrageId &&
                       body.NextMove.Id != ThingsCaveGodBody.EnrageRecoverId,
                    $"Simulated preview must NOT jump CaveGodBody NextMove to Enrage, got {body.NextMove.Id}.");

                await CreatureCmd.Damage(
                    Choice,
                    left.Creature,
                    5000m,
                    ValueProp.Unpowered | ValueProp.Unblockable,
                    p0.Creature);

                assert(!left.IsDown,
                    "Simulated lethal preview on CaveGodHand must NOT set IsDown.");
                assert(!body.IsWeakPhase,
                    "Simulated lethal preview on CaveGodHand must NOT expose CaveGodBody core.");
                assert(body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
                    "Simulated lethal preview on CaveGodHand must NOT strip Ancient Core Bulwark.");
            }

            assert(!ExternalCombatSimulationGuard.IsActive(body.Creature),
                "ExternalCombatSimulationGuard returns false after disposing simulated preview scope.");

            // Roll back vanilla Creature HP exactly as damage-preview does, then run real attacks.
            body.Creature.SetCurrentHpInternal(initialBodyHp);
            left.Creature.SetCurrentHpInternal(initialLeftHp);

            await CreatureCmd.Damage(
                Choice,
                body.Creature,
                5000m,
                ValueProp.Unpowered | ValueProp.Unblockable,
                p0.Creature);

            assert(body.IsEnrageLocked,
                "Real attack crossing CaveGodBody threshold must set _isEnrageLocked.");
            assert(body.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers,
                $"Real attack crossing CaveGodBody threshold must switch HpDisplay to InfiniteWithoutNumbers, got {body.Creature.HpDisplay}.");
            assert(body.NextMove.Id == ThingsCaveGodBody.EnrageId,
                $"Real attack crossing CaveGodBody threshold must lock EnrageId, got {body.NextMove.Id}.");

            await CreatureCmd.Damage(
                Choice,
                left.Creature,
                5000m,
                ValueProp.Unpowered | ValueProp.Unblockable,
                p0.Creature);

            assert(left.IsDown,
                "Real lethal attack on CaveGodHand must set IsDown.");
            assert(body.IsWeakPhase && body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId,
                $"Real arm break while enrage-locked must expose core and lock EnrageRecoverId, got weak={body.IsWeakPhase}, move={body.NextMove.Id}.");
        }
        finally
        {
            DeactivateSyntheticCombat();
        }
    }

    private static async Task VerifyAbyssalInkmawSimulationIsolationAsync(Action<bool, string> assert)
    {
        BattleFixture battle = await CreateBattleAsync(
            ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(),
            players: 2,
            ascension: 0,
            seed: "ext-sim-inkmaw");
        try
        {
            foreach (Creature enemy in battle.State.Enemies.ToList())
                await enemy.Monster!.AfterAddedToRoom();
            foreach (Creature enemy in battle.State.Enemies.ToList())
                await enemy.Monster!.BeforeCombatStart();

            ThingsAbyssalInkmaw boss = battle.Monster<ThingsAbyssalInkmaw>();
            Player p0 = battle.Players[0];

            int initialHp = boss.Creature.CurrentHp;
            int threshold = boss.Phase2HpThreshold;
            string initialMove = boss.NextMove.Id;

            assert(!boss.IsPhase2Locked && boss.Phase == 1,
                "AbyssalInkmaw starts in Phase 1 unlocked.");

            using (ExternalCombatSimulationGuard.BeginSimulatedExternalPreviewForTest())
            {
                await CreatureCmd.Damage(
                    Choice,
                    boss.Creature,
                    5000m,
                    ValueProp.Unpowered | ValueProp.Unblockable,
                    p0.Creature);

                assert(boss.Creature.CurrentHp == threshold,
                    $"Simulated preview still clamps AbyssalInkmaw HP at Phase2HpThreshold ({threshold}), got {boss.Creature.CurrentHp}.");
                assert(!boss.IsPhase2Locked,
                    "Simulated preview crossing AbyssalInkmaw threshold must NOT set _phase2Pending.");
                assert(boss.Creature.HpDisplay == HpDisplay.Normal,
                    $"Simulated preview must NOT flip AbyssalInkmaw HpDisplay to Infinite, got {boss.Creature.HpDisplay}.");
                assert(boss.NextMove.Id == initialMove &&
                       boss.NextMove.Id != ThingsAbyssalInkmaw.Phase2SurgeId &&
                       boss.NextMove.Id != ThingsAbyssalInkmaw.Phase2SurgeNoSummonId,
                    $"Simulated preview must NOT jump AbyssalInkmaw NextMove to Phase2Surge, got {boss.NextMove.Id}.");
            }

            // Roll back vanilla Creature HP and execute real attack.
            boss.Creature.SetCurrentHpInternal(initialHp);

            await CreatureCmd.Damage(
                Choice,
                boss.Creature,
                5000m,
                ValueProp.Unpowered | ValueProp.Unblockable,
                p0.Creature);

            assert(boss.IsPhase2Locked,
                "Real attack crossing AbyssalInkmaw threshold must set _phase2Pending.");
            assert(boss.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers,
                $"Real attack crossing AbyssalInkmaw threshold must switch HpDisplay to InfiniteWithoutNumbers, got {boss.Creature.HpDisplay}.");
            assert(boss.NextMove.Id is ThingsAbyssalInkmaw.Phase2SurgeId or ThingsAbyssalInkmaw.Phase2SurgeNoSummonId,
                $"Real attack crossing AbyssalInkmaw threshold must switch move to Phase2Surge, got {boss.NextMove.Id}.");
        }
        finally
        {
            DeactivateSyntheticCombat();
        }
    }

    private static async Task VerifyHumanFaceColumnRedirectHardeningAsync(Action<bool, string> assert)
    {
        BattleFixture battle = await CreateBattleAsync(
            ModelDb.Encounter<ThingsHumanFaceColumnEncounter>(),
            players: 1,
            ascension: 0,
            seed: "ext-sim-column");
        try
        {
            foreach (Creature enemy in battle.State.Enemies.ToList())
                await enemy.Monster!.AfterAddedToRoom();

            ThingsHumanFaceColumn[] discs = battle.Enemies
                .Select(c => c.Monster)
                .OfType<ThingsHumanFaceColumn>()
                .OrderBy(c => c.Level)
                .ToArray();
            ThingsHumanFaceColumn bottom = discs[0];
            ThingsHumanFaceColumn middle = discs[1];
            ThingsHumanFaceColumn top = discs[2];
            Player p0 = battle.Players[0];

            // Keep generous HP so multi-step assertions never collapse discs mid-check.
            foreach (ThingsHumanFaceColumn disc in discs)
            {
                disc.Creature.SetMaxHpInternal(200);
                disc.Creature.SetCurrentHpInternal(200);
            }

            ThingsHumanFaceColumnPower topPower = top.Creature.GetPower<ThingsHumanFaceColumnPower>()!;
            ThingsHumanFaceColumnPower middlePower = middle.Creature.GetPower<ThingsHumanFaceColumnPower>()!;

            // (a1) Standalone Hook.ModifyDamage(..., CardPreviewMode.None) outside CreatureCmd.Damage must NOT stage.
            decimal previewModified = Hook.ModifyDamage(
                battle.Run,
                battle.State,
                top.Creature,
                p0.Creature,
                20m,
                ValueProp.Move,
                null,
                null,
                ModifyDamageHookType.All,
                CardPreviewMode.None,
                out _);
            assert(previewModified == 10m,
                $"Standalone Hook.ModifyDamage on Top must still return reduced 10m, got {previewModified}.");
            AssertCleanRedirectState(topPower, assert, "standalone Hook.ModifyDamage outside CreatureCmd.Damage");

            // (a2) PoisonPower.CalculateTotalDamageNextTurn() calls Hook.ModifyDamage(..., CardPreviewMode.None) and must NOT stage.
            await PowerCmd.Apply<PoisonPower>(Choice, top.Creature, 8m, p0.Creature, null);
            int poisonPreview = top.Creature.GetPower<PoisonPower>()!.CalculateTotalDamageNextTurn();
            assert(poisonPreview == 4,
                $"PoisonPower.CalculateTotalDamageNextTurn on Top must preview 4 (50% of 8), got {poisonPreview}.");
            AssertCleanRedirectState(topPower, assert, "PoisonPower.CalculateTotalDamageNextTurn");
            await PowerCmd.Remove<PoisonPower>(top.Creature);

            // (a3) Simulated external preview executing CreatureCmd.Damage must NOT stage or enqueue redirects.
            int topHpBeforeSim = top.Creature.CurrentHp;
            int bottomHpBeforeSim = bottom.Creature.CurrentHp;
            using (ExternalCombatSimulationGuard.BeginSimulatedExternalPreviewForTest())
            {
                await CreatureCmd.Damage(Choice, top.Creature, 12m, ValueProp.Move, p0.Creature);
                assert(topHpBeforeSim - top.Creature.CurrentHp == 6,
                    $"Simulated preview on Top must still show 6 damage (50% of 12), got {topHpBeforeSim - top.Creature.CurrentHp}.");
                assert(bottom.Creature.CurrentHp == bottomHpBeforeSim,
                    $"Simulated preview on Top must NOT redirect damage onto Bottom, got delta {bottomHpBeforeSim - bottom.Creature.CurrentHp}.");
                AssertCleanRedirectState(topPower, assert, "simulated CreatureCmd.Damage preview");
            }
            top.Creature.SetCurrentHpInternal(topHpBeforeSim);

            // (b) Real CreatureCmd.Damage (single-target + multi-target AoE) redirects exact damage to Bottom.
            int topHp = top.Creature.CurrentHp;
            int bottomHp = bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, top.Creature, 10m, ValueProp.Move, p0.Creature);
            assert(topHp - top.Creature.CurrentHp == 5 && bottomHp - bottom.Creature.CurrentHp == 5,
                $"Real single-target 10 on Top must deal 5 to Top and redirect 5 to Bottom, got top={topHp - top.Creature.CurrentHp}, bottom={bottomHp - bottom.Creature.CurrentHp}.");
            AssertCleanRedirectState(topPower, assert, "post-live single-target damage");

            topHp = top.Creature.CurrentHp;
            int midHp = middle.Creature.CurrentHp;
            bottomHp = bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, [top.Creature, middle.Creature, bottom.Creature], 10m, ValueProp.Move, p0.Creature);
            assert(topHp - top.Creature.CurrentHp == 5 &&
                   midHp - middle.Creature.CurrentHp == 7 &&
                   bottomHp - bottom.Creature.CurrentHp == 18,
                $"Real AoE 10 across [Top, Middle, Bottom] must deal 5/7/18, got {topHp - top.Creature.CurrentHp}/{midHp - middle.Creature.CurrentHp}/{bottomHp - bottom.Creature.CurrentHp}.");
            AssertCleanRedirectState(topPower, assert, "post-live AoE top");
            AssertCleanRedirectState(middlePower, assert, "post-live AoE middle");

            // (c) Clobbered/zeroed staging before BeforeDamageReceived must still redirect exact damage via math fallback.
            var clobberHarmony = new Harmony("STS2_Things.Probe.ColumnZeroStagingTest");
            try
            {
                clobberHarmony.Patch(
                    AccessTools.Method(typeof(ThingsHumanFaceColumnPower), nameof(ThingsHumanFaceColumnPower.BeforeDamageReceived)),
                    prefix: new HarmonyMethod(typeof(DepthsExternalSimulationRegression), nameof(ZeroStagedDamageBeforeReceivedPrefix)));

                // Odd damage 9 on Top: Top takes floor(4.5)=4, fallback redirects floor(4.5/0.5)-4 = 5.
                topHp = top.Creature.CurrentHp;
                bottomHp = bottom.Creature.CurrentHp;
                await CreatureCmd.Damage(Choice, top.Creature, 9m, ValueProp.Move, p0.Creature);
                assert(topHp - top.Creature.CurrentHp == 4 && bottomHp - bottom.Creature.CurrentHp == 5,
                    $"Zeroed-staging fallback on Top (9) must deal 4 to Top + 5 to Bottom, got top={topHp - top.Creature.CurrentHp}, bottom={bottomHp - bottom.Creature.CurrentHp}.");

                // Odd damage 9 on Middle: Middle takes floor(6.75)=6, fallback redirects floor(6.75/0.75)-6 = 3.
                midHp = middle.Creature.CurrentHp;
                bottomHp = bottom.Creature.CurrentHp;
                await CreatureCmd.Damage(Choice, middle.Creature, 9m, ValueProp.Move, p0.Creature);
                assert(midHp - middle.Creature.CurrentHp == 6 && bottomHp - bottom.Creature.CurrentHp == 3,
                    $"Zeroed-staging fallback on Middle (9) must deal 6 to Middle + 3 to Bottom, got mid={midHp - middle.Creature.CurrentHp}, bottom={bottomHp - bottom.Creature.CurrentHp}.");

                // Unblockable+Unpowered 8 on Top: Top takes 4, fallback redirects 4 as Unblockable bypassing Bottom Block.
                await CreatureCmd.GainBlock(bottom.Creature, 10m, ValueProp.Unpowered, null);
                int bottomBlock = bottom.Creature.Block;
                topHp = top.Creature.CurrentHp;
                bottomHp = bottom.Creature.CurrentHp;
                await CreatureCmd.Damage(Choice, top.Creature, 8m, ValueProp.Unblockable | ValueProp.Unpowered, null, null, null);
                assert(topHp - top.Creature.CurrentHp == 4 &&
                       bottom.Creature.Block == bottomBlock &&
                       bottomHp - bottom.Creature.CurrentHp == 4,
                    $"Zeroed-staging fallback on Top Unblockable (8) must deal 4 to Top and 4 unblockable to Bottom, got top={topHp - top.Creature.CurrentHp}, bottomHpDelta={bottomHp - bottom.Creature.CurrentHp}, block={bottom.Creature.Block}.");

                // IntangiblePower on Top caps damage to 1 and must NOT trigger fallback redirect.
                await PowerCmd.Apply<IntangiblePower>(Choice, top.Creature, 1m, top.Creature, null);
                topHp = top.Creature.CurrentHp;
                bottomHp = bottom.Creature.CurrentHp;
                await CreatureCmd.Damage(Choice, top.Creature, 10m, ValueProp.Move, p0.Creature);
                assert(topHp - top.Creature.CurrentHp == 1 && bottomHp - bottom.Creature.CurrentHp == 0,
                    $"Intangible Top must take 1 and redirect 0 even with fallback enabled, got top={topHp - top.Creature.CurrentHp}, bottom={bottomHp - bottom.Creature.CurrentHp}.");
            }
            finally
            {
                clobberHarmony.UnpatchAll(clobberHarmony.Id);
            }
        }
        finally
        {
            DeactivateSyntheticCombat();
        }
    }

    private static void ZeroStagedDamageBeforeReceivedPrefix(ThingsHumanFaceColumnPower __instance)
    {
        object? data = PowerInternalDataField.GetValue(__instance);
        if (data == null)
            return;

        Type dataType = data.GetType();
        dataType.GetField("StagedNegatedDamage")!.SetValue(data, 0m);
        dataType.GetField("StagedProps")!.SetValue(data, null);
        dataType.GetField("StagedReductionSkipped")!.SetValue(data, false);
    }

    private static void AssertCleanRedirectState(
        ThingsHumanFaceColumnPower power,
        Action<bool, string> assert,
        string context)
    {
        object data = PowerInternalDataField.GetValue(power)
            ?? throw new InvalidOperationException("ThingsHumanFaceColumnPower internal RedirectData is null.");
        Type dataType = data.GetType();
        decimal staged = (decimal)dataType.GetField("StagedNegatedDamage")!.GetValue(data)!;
        bool bypassing = (bool)dataType.GetField("IsBypassingReduction")!.GetValue(data)!;
        ICollection pending = (ICollection)dataType.GetField("PendingRedirects")!.GetValue(data)!;

        assert(staged == 0m, $"StagedNegatedDamage must stay 0 after {context}, got {staged}.");
        assert(!bypassing, $"IsBypassingReduction must stay false after {context}.");
        assert(pending.Count == 0, $"PendingRedirects must stay empty after {context}, got {pending.Count}.");
    }

    private static async Task<BattleFixture> CreateBattleAsync(
        EncounterModel canonical,
        int players = 1,
        int ascension = 0,
        string? seed = null)
    {
        DeactivateSyntheticCombat();
        List<Player> party = Enumerable.Range(1, players)
            .Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i))
            .ToList();
        ActModel[] acts =
        [
            ModelDb.Act<Overgrowth>().ToMutable(),
            ModelDb.Act<ThingsDepths>().ToMutable(),
            ModelDb.Act<Glory>().ToMutable(),
        ];
        RunState run = ProbeRuntimeApi.CreateForNewRun(party, acts, [], GameMode.Standard, ascension, seed ?? "ext-sim-" + canonical.Id.Entry);
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, ProbeRuntimeApi.Ascension(ascension));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, new NetSingleplayerGameService());

        EncounterModel encounter = canonical.ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);
        foreach (Player player in run.Players)
        {
            player.ResetCombatState();
            room.CombatState.AddPlayer(player);
            player.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
            player.Creature.SetMaxHpInternal(10000);
            player.Creature.SetCurrentHpInternal(10000);
        }

        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            Creature creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(creature);
            monster.SetUpForCombat();
            monster.RollMove(run.Players.Select(p => p.Creature));
        }

        room.CombatState.CurrentSide = CombatSide.Player;
        ActivateSyntheticCombat(room.CombatState);
        await Hook.BeforeCombatStart(run, room.CombatState);
        return new BattleFixture(run, room, encounter);
    }

    private static void ActivateSyntheticCombat(CombatState state)
    {
        LocalContext.NetId = state.Players[0].NetId;
        CombatManager manager = CombatManager.Instance;
        FieldInfo? legacyStateField = typeof(CombatManager).GetField(
            "_state", BindingFlags.NonPublic | BindingFlags.Instance);
        if (legacyStateField != null)
        {
            legacyStateField.SetValue(manager, state);
            typeof(CombatManager).GetField("_pendingLoss", BindingFlags.NonPublic | BindingFlags.Instance)?
                .SetValue(manager, null);
            typeof(CombatManager).GetField(
                    "<IsInProgress>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(manager, true);
        }
        else
        {
            FieldInfo turnStateField = typeof(CombatManager).GetField(
                    "_turnState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(typeof(CombatManager).FullName, "_turnState");
            object turnState = Activator.CreateInstance(
                    turnStateField.FieldType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: [state],
                    culture: null)
                ?? throw new InvalidOperationException("Could not create the V113 combat turn state.");
            turnState.GetType().GetProperty("IsInProgress")!.SetValue(turnState, true);
            turnState.GetType().GetProperty("IsStarting")!.SetValue(turnState, false);
            turnStateField.SetValue(manager, turnState);
        }

        ProbeRuntimeApi.EnterCombat(state);
        manager.StateTracker.SetState(state);
    }

    private static void DeactivateSyntheticCombat()
    {
        LocalContext.NetId = null;
        CombatManager manager = CombatManager.Instance;
        FieldInfo? legacyStateField = typeof(CombatManager).GetField(
            "_state", BindingFlags.NonPublic | BindingFlags.Instance);
        if (legacyStateField != null)
        {
            typeof(CombatManager).GetField(
                    "<IsInProgress>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)?
                .SetValue(manager, false);
            legacyStateField.SetValue(manager, null);
            return;
        }

        FieldInfo turnStateField = typeof(CombatManager).GetField(
                "_turnState", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(CombatManager).FullName, "_turnState");
        object? turnState = turnStateField.GetValue(manager);
        turnState?.GetType().GetMethod("Cancel", BindingFlags.Public | BindingFlags.Instance)?
            .Invoke(turnState, null);
        turnStateField.SetValue(manager, null);
    }
}
