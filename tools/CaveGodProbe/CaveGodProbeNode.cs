using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Monsters;
using STS2_Things.Encounters;
using STS2_Things.Powers;
using STS2_Things.Cards;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Visuals;

public partial class CaveGodProbeNode : Node
{
    private static readonly Assembly ImplementationAssembly = typeof(STS2_ThingsInit).Assembly;
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        try
        {
            TestMode.TurnOnInternal();
            string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK")
                ?? "D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck");
#if STS2_V111_1
            const string defaultTarget = "v111.1";
#else
            const string defaultTarget = "v113.1";
#endif
            string defaultPck = new[]
                {
                    Path.Combine(root, $"build/{defaultTarget}/STS2_Things.pck"),
                    Path.Combine(root, "build/v113.1/STS2_Things.pck"),
                    Path.Combine(root, "build/v111.1/STS2_Things.pck"),
                    Path.Combine(root, "build/unified/STS2_Things.pck"),
                }.FirstOrDefault(File.Exists)
                ?? Path.Combine(root, $"build/{defaultTarget}/STS2_Things.pck");
            ProjectSettings.LoadResourcePack(System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK")
                ?? defaultPck);
            string? overlay=System.Environment.GetEnvironmentVariable("THINGS_PROBE_OVERLAY_PCK");
            if(!string.IsNullOrWhiteSpace(overlay))Assert(ProjectSettings.LoadResourcePack(overlay),"Recovery overlay mounted.");
            try
            {
                SaveManager.Instance.InitSettingsDataForTest();
                SaveManager.Instance.SettingsSave.Language = "eng";
                LocManager.Initialize();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"LocManager.Initialize failed: {ex}");
            }
            STS2_Things.Platform.Sts2VersionSeam.InitializeBeforeModelDatabase();
            InitializeModelDb();
            EnsureScriptsLookedUp();
            if (OS.GetCmdlineUserArgs().Contains("--ui-art-review"))
            {
                await RenderCaveGodUiArt();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--crossroads-ui"))
            {
                await VerifyCrossroadUi();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--crossroads"))
            {
                await VerifyCrossroads();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--scale-beetle-actions"))
            {
                await VerifyBeetleActions();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--fogmog-preview"))
            {
                await RenderFogmogPreview();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--fogmog-actions"))
            {
                await VerifyFogmogActions();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--action-regression"))
            {
                await VerifyActionRegression();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--ui-regression"))
            {
                await VerifyUiRegression();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--visual"))
            {
                await VerifyRenderedResources();
                GetTree().Quit(0);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--render-official-rooms"))
            {
                await RenderOfficialRooms();
                GetTree().Quit(0);
                return;
            }
            await VerifyDefaultBodyAndBulwark();
            await VerifyDefeatCleanse();
            await VerifyCleanseAndRecovery();
            await VerifyCyclesAndIntents();
            await VerifyPhaseScaling();
            await VerifyCapture(escape: true, phase2: false);
            await VerifyCapture(escape: false, phase2: false);
            await VerifyCapture(escape: true, phase2: true);
            await VerifyCapture(escape: false, phase2: true);
            await VerifyCaptureInterrupted();
            await VerifyEnemyTurnBreak();
            await VerifyFinalDefeat();
            await VerifyPoisonAndThorns();
            await VerifyNativeStunAndForcedDeath();
            await VerifyTransitionPoisonAndGrowth();
            await VerifyRepeatedBreakGrowth();
            await VerifyStunnedArmGrowth();
            await VerifyWeakPhaseSurvivingArmFlow();
            await VerifyAgingPowerAndStoneArmor();
            await VerifyHalfHpEnragePaths();
            await VerifySweepCardTheft();
            await VerifyHandLayoutAndZIndex();
            GD.Print("CaveGod behavior probe: PASS");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
        finally { DeactivateSyntheticCombat(); }
    }
    private sealed record Fixture(CombatState State, ThingsCaveGodBody Body, ThingsCaveGodLeftHand Left, ThingsCaveGodRightHand Right);
    private static Fixture Scenario(int count = 1, int ascension = 0, bool mixedCharacters = false, bool? forceRightLead = false)
    {
        var players = Enumerable.Range(1, count).Select(i => !mixedCharacters ? Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i) : i switch
        {
            2 => Player.CreateForNewRun<Silent>(UnlockState.all, (ulong)i),
            3 => Player.CreateForNewRun<Regent>(UnlockState.all, (ulong)i),
            4 => Player.CreateForNewRun<Defect>(UnlockState.all, (ulong)i),
            _ => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i),
        }).ToArray();
        var run = RunState.CreateForTest(players, seed: "cavegod-regression", ascensionLevel: ascension);
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, ProbeRuntimeApi.Ascension(ascension));
        ProbeRuntimeApi.PrepareSelectors(run);
        var room = new CombatRoom(ModelDb.Encounter<ThingsCaveGodBossEncounter>().ToMutable(), run);
        run.PushRoom(room);
        foreach (var player in players)
        {
            player.ResetCombatState(); room.CombatState.AddPlayer(player);
            player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000);
        }
        var left = Add<ThingsCaveGodLeftHand>(room.CombatState, ThingsCaveGodBossEncounter.LeftHandSlot);
        var right = Add<ThingsCaveGodRightHand>(room.CombatState, ThingsCaveGodBossEncounter.RightHandSlot);
        var body = Add<ThingsCaveGodBody>(room.CombatState, ThingsCaveGodBossEncounter.BodySlot);
        ActivateSyntheticCombat(room.CombatState);
        left.EnsureAgingPower().GetAwaiter().GetResult();
        right.EnsureAgingPower().GetAwaiter().GetResult();
        body.EnsureBulwarkPower().GetAwaiter().GetResult();
        if (forceRightLead.HasValue)
            body.SetRightLeadsForTest(forceRightLead.Value);
        else
            body.RefreshHandIntents();
        return new Fixture(room.CombatState, body, left, right);
    }
    private static T Add<T>(CombatState state, string slot) where T : MonsterModel
    {
        var monster = (T)ModelDb.Monster<T>().ToMutable();
        var creature = state.CreateCreature(monster, CombatSide.Enemy, slot);
        state.AddCreature(creature);
        monster.SetUpForCombat();
        monster.RollMove(state.PlayerCreatures);
        return monster;
    }
    private static async Task VerifyDefaultBodyAndBulwark()
    {
        var s = Scenario();
        Assert(!s.Body.IsHealthBarVisible ? false : s.Body.ShouldAllowTargeting(s.Body.Creature) && s.Body.Creature.IsHittable,
            "Body health bar must be visible and directly targetable from Turn 1.");
        Assert(s.Body.BothArmsHealthy && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
            "Body must start with Granite Bulwark while both arms are healthy.");

        int hpBefore = s.Body.Creature.CurrentHp;
        await CreatureCmd.Damage(Choice, s.Body.Creature, 20m, ValueProp.Move, s.State.Players[0].Creature);
        Assert(s.Body.Creature.CurrentHp == hpBefore - 5,
            $"Bulwarked Body must take 25% powered damage (20 -> 5), lost {hpBefore - s.Body.Creature.CurrentHp}.");

        hpBefore = s.Body.Creature.CurrentHp;
        await Hit(s, s.Body.Creature, 12m);
        Assert(s.Body.Creature.CurrentHp == hpBefore - 3,
            $"Bulwarked Body must take 25% unpowered damage (12 -> 3), lost {hpBefore - s.Body.Creature.CurrentHp}.");

        await Hit(s, s.Left.Creature, 100000m);
        Assert(s.Body.IsWeakPhase && !s.Body.BothArmsHealthy && !s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
            "Breaking either arm must remove Granite Bulwark.");

        hpBefore = s.Body.Creature.CurrentHp;
        await CreatureCmd.Damage(Choice, s.Body.Creature, 20m, ValueProp.Move, s.State.Players[0].Creature);
        Assert(s.Body.Creature.CurrentHp == hpBefore - 20,
            "Exposed Body must take 100% full damage (20 -> 20).");

        await Act(s);
        Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id,
            "Exposure turn 2 must remain prone (WEAK_PRONE_2).");
        await Act(s);
        Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId,
            "Exposure turn 3 must prepare recovery (WEAK_UNKNOWN_3).");
        await Act(s);
        Assert(!s.Body.IsWeakPhase && s.Body.BothArmsHealthy && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
            "Awakening after 3 exposure turns must restore Granite Bulwark.");

        hpBefore = s.Body.Creature.CurrentHp;
        await CreatureCmd.Damage(Choice, s.Body.Creature, 20m, ValueProp.Move, s.State.Players[0].Creature);
        Assert(s.Body.Creature.CurrentHp == hpBefore - 5,
            "Re-bulwarked Body must take 25% damage again.");
        DeactivateSyntheticCombat();
        GD.Print("PASS default targetable Body HP bar and 25% Granite Bulwark lifecycle");
    }
    private static async Task VerifyDefeatCleanse()
    {
        var s = Scenario();
        var choice = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<PoisonPower>(choice, s.Left.Creature, 9m, s.State.Players[0].Creature, null);
        await CreatureCmd.Damage(choice, s.Left.Creature, 10000m, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable, s.State.Players[0].Creature);
        Assert(!s.Left.Creature.HasPower<PoisonPower>(), "Defeated arm retains Poison (regression).");
        Assert(s.Body.IsWeakPhase, "Breaking an arm must expose the core.");
        DeactivateSyntheticCombat();
        GD.Print("PASS defeated arm clears poison");
    }
    private static readonly ThrowingPlayerChoiceContext Choice = new();
    private static Task Hit(Fixture s, Creature target, decimal amount = 100000m) =>
        CreatureCmd.Damage(Choice, target, amount, ValueProp.Unpowered | ValueProp.Unblockable, s.State.Players[0].Creature);
    private static async Task Act(Fixture s)
    {
        s.State.CurrentSide = CombatSide.Enemy;
        await Hook.BeforeSideTurnStart(s.State, CombatSide.Enemy, s.State.Enemies.ToArray());
        foreach (var enemy in s.State.Enemies.ToList())
            if (enemy.IsAlive && enemy.Monster != null) await enemy.Monster.PerformMove();
        await Hook.AfterSideTurnEnd(s.State, CombatSide.Enemy, s.State.Enemies.ToArray());
        s.State.CurrentSide = CombatSide.Player;
        await Hook.BeforeSideTurnStart(s.State, CombatSide.Player, s.State.PlayerCreatures);
        foreach (var enemy in s.State.Enemies.ToList())
            if (enemy.IsAlive) enemy.Monster!.RollMove(s.State.PlayerCreatures);
        await Hook.AfterSideTurnStart(s.State, CombatSide.Player, s.State.PlayerCreatures);
    }
    private sealed class TrialQueueCardSelector(params int[] optionIndices) : ICardSelector
    {
        private readonly Queue<int> _indices = new(optionIndices);
        public int SelectionCount { get; private set; }

        public Task<IEnumerable<CardModel>> GetSelectedCards(
            IEnumerable<CardModel> options,
            int minSelect,
            int maxSelect)
        {
            CardModel[] cards = options.ToArray();
            Assert(cards.Length == 2 &&
                   cards[0] is ThingsCaveGodBrokenBladeTrial &&
                   cards[1] is ThingsCaveGodShatteredShieldTrial,
                "Phase 2 Trial choice screen must present [BrokenBladeTrial, ShatteredShieldTrial].");
            SelectionCount++;
            int idx = _indices.Count > 0 ? _indices.Dequeue() : 0;
            return Task.FromResult<IEnumerable<CardModel>>([cards[idx]]);
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => default;
    }

    private static async Task Phase2(Fixture s)
    {
        await Hit(s, s.Left.Creature);
        int dmg = s.Body.Creature.CurrentHp - s.Body.EnrageHpThreshold + 40;
        await Hit(s, s.Body.Creature, dmg);
        Assert(s.Body.IsEnrageLocked && s.Body.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers,
            "Half-HP hit must immediately lock InfiniteWithoutNumbers HP display.");
        Assert(s.Body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId &&
               s.Body.NextMove.Intents.All(i => i is UnknownIntent),
            "Half-HP prone core must immediately swap intent to EnrageRecoverId (UnknownIntent).");
        await Act(s);
        Assert(s.Body.Phase == 2 && !s.Body.IsEnrageLocked &&
               s.Body.Creature.HpDisplay == HpDisplay.Normal &&
               s.Body.Creature.CurrentHp == s.Body.EnrageHpThreshold &&
               s.Body.NextMove.Id == ThingsCaveGodBody.CrushId,
            "Same-turn EnrageRecover must enter Phase 2 CrushId and restore exact 50% HP with Normal HP bar.");
    }
    private static async Task VerifyCleanseAndRecovery()
    {
        foreach (bool breakLeft in new[] { true, false })
        {
            var s = Scenario();
            ThingsCaveGodHand broken = breakLeft ? s.Left : s.Right;
            ThingsCaveGodHand survivor = breakLeft ? s.Right : s.Left;
            await PowerCmd.Apply<StrengthPower>(Choice, broken.Creature, 4m, broken.Creature, null);
            await PowerCmd.Apply<WeakPower>(Choice, broken.Creature, 3m, s.State.Players[0].Creature, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, broken.Creature, 3m, s.State.Players[0].Creature, null);
            await PowerCmd.Apply<PoisonPower>(Choice, broken.Creature, 9m, s.State.Players[0].Creature, null);
            await Hit(s, broken.Creature);
            Assert(!broken.Creature.Powers.Any(p => p.TypeForCurrentAmount == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff), "Defeat leaves a debuff.");
            Assert(broken.Creature.GetPowerAmount<StrengthPower>() == 4, "Defeat must preserve positive strength.");
            Assert(!broken.ShouldAllowTargeting(broken.Creature) && s.Body.ShouldAllowTargeting(s.Body.Creature), "Exposure targeting mismatch.");
            await Act(s);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id, "Exposure turn 2 must remain prone (WEAK_PRONE_2).");
            Assert(survivor.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakAttack), "Surviving arm must advertise weak attack on exposure turn 2.");
            Assert(!survivor.NextMove.Intents.OfType<StunIntent>().Any(), "Surviving arm must NOT be stunned on exposure turn 2.");
            Assert(survivor.NextMove.Intents.OfType<AttackIntent>().Any(), "Surviving arm must have attack intent on exposure turn 2.");
            await Act(s);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId, "Exposure turn 3 must prepare recovery.");
            Assert(survivor.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakAttack), "Surviving arm must advertise weak attack on exposure turn 3.");
            Assert(!survivor.NextMove.Intents.OfType<StunIntent>().Any(), "Surviving arm must NOT be stunned on exposure turn 3.");
            Assert(survivor.NextMove.Intents.OfType<AttackIntent>().Any(), "Surviving arm must have attack intent on exposure turn 3.");
            await PowerCmd.Apply<StrengthPower>(Choice, survivor.Creature, -4m, s.State.Players[0].Creature, null);
            await PowerCmd.Apply<DoomPower>(Choice, survivor.Creature, 2m, s.State.Players[0].Creature, null);
            await Hit(s, survivor.Creature);
            Assert(!survivor.Creature.HasPower<StrengthPower>() && !survivor.Creature.HasPower<DoomPower>(), "Negative strength/Doom not cleansed.");
            Assert(survivor.NextMove.Intents.Any(i => i is StunIntent) && survivor.NextMove.Intents.Any(i => i is HealIntent), "Stun+heal intent missing.");
            Assert(!survivor.ShouldAllowTargeting(survivor.Creature), "Second downed arm must also become untargetable.");
            int before = s.State.Players[0].Creature.CurrentHp;
            await Act(s);
            Assert(s.State.Players[0].Creature.CurrentHp == before, "Downed second arm unexpectedly attacked during recovery.");
            Assert(!s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.GrabId, "Three exposure turns must awaken and resume the interrupted opening move.");
            Assert(s.Left.Creature.CurrentHp == s.Left.Creature.MaxHp && s.Right.Creature.CurrentHp == s.Right.Creature.MaxHp, "Recovery failed to heal arms.");
            Assert(s.Left.ShouldAllowTargeting(s.Left.Creature) && s.Right.ShouldAllowTargeting(s.Right.Creature), "Arms remain untargetable after recovery.");
            Assert(s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(), "Awakening must restore Granite Bulwark.");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS both arms: cleanse, retain buffs, negative Strength/Doom, second-arm knockdown cancels weak attack, three-window recovery");
    }
    private static async Task VerifyCyclesAndIntents()
    {
        foreach (int ascension in new[] { 0, 10 })
        {
            var s = Scenario(ascension: ascension);
            // P1 7-move cycle: Grab -> AirSlam -> Sweep -> RockSweep -> Jabs -> HeavyPunch -> Guard.
            // 5 flips per cycle -> Cycle 1 automatically inverts every single-hand move to the opposite arm!
            string[] sequence = [
                ThingsCaveGodBody.GrabId,
                ThingsCaveGodBody.AirSlamId,
                ThingsCaveGodBody.SweepId,
                ThingsCaveGodBody.RockSweepId,
                ThingsCaveGodBody.JabsId,
                ThingsCaveGodBody.HeavyPunchId,
                ThingsCaveGodBody.GuardId,
            ];
            for (int turn = 0; turn < 14; turn++)
            {
                int cycle = turn / 7;
                int slot = turn % 7;
                Assert(s.Body.NextMove.Id == sequence[slot], $"Normal P1 loop drift at turn {turn}: {s.Body.NextMove.Id} != {sequence[slot]}.");
                bool cycle0 = cycle % 2 == 0;
                switch (slot)
                {
                    case 0:
                    {
                        ThingsCaveGodHand expected = cycle0 ? s.Left : s.Right;
                        Assert(expected.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Grab),
                            $"Turn {turn}: expected {(expected.IsLeft ? "Left" : "Right")} to lead Grab.");
                        break;
                    }
                    case 1:
                    {
                        ThingsCaveGodHand expected = cycle0 ? s.Left : s.Right;
                        Assert(expected.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Slam),
                            $"Turn {turn}: expected {(expected.IsLeft ? "Left" : "Right")} to perform AirSlam.");
                        break;
                    }
                    case 2:
                    {
                        ThingsCaveGodHand expected = cycle0 ? s.Right : s.Left;
                        Assert(expected.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Sweep),
                            $"Turn {turn}: expected {(expected.IsLeft ? "Left" : "Right")} to perform Sweep.");
                        break;
                    }
                    case 3:
                    {
                        ThingsCaveGodHand expected = cycle0 ? s.Left : s.Right;
                        Assert(expected.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.CleaveSweep),
                            $"Turn {turn}: expected {(expected.IsLeft ? "Left" : "Right")} to perform RockSweep.");
                        break;
                    }
                    case 4:
                    {
                        Assert(s.Body.IsRightLeading == cycle0,
                            $"Turn {turn}: Jabs lead parity mismatch (expected rightLeads={cycle0}, got {s.Body.IsRightLeading}).");
                        break;
                    }
                    case 5:
                    {
                        ThingsCaveGodHand expected = cycle0 ? s.Left : s.Right;
                        Assert(expected.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.HeavyPunch),
                            $"Turn {turn}: expected {(expected.IsLeft ? "Left" : "Right")} to perform HeavyPunch.");
                        break;
                    }
                }

                var attackers = s.State.Enemies.Where(c => c.Monster!.NextMove.Intents.OfType<AttackIntent>().Any()).ToArray();
                Assert(attackers.Length == 1, $"Expected exactly one advertised attacker on P1 turn {turn}, found {attackers.Length}.");
                var attacker = attackers[0];
                AttackIntent p1Intent = attacker.Monster!.NextMove.Intents.OfType<AttackIntent>().Single();
                int expectedBase = slot switch
                {
                    0 => ascension == 0 ? 9 : 10,
                    1 => ascension == 0 ? 19 : 21,
                    2 => ascension == 0 ? 11 : 13,
                    3 => ascension == 0 ? 12 : 14,
                    4 => ascension == 0 ? 5 : 6,
                    5 => ascension == 0 ? 14 : 16,
                    6 => ascension == 0 ? 12 : 14,
                    _ => throw new InvalidOperationException(),
                };
                Assert(p1Intent.DamageCalc!() == expectedBase,
                    $"Turn {turn} ({sequence[slot]}): P1 base intent {p1Intent.DamageCalc!()} != {expectedBase}.");
                int expectedDmg = attacker.Monster!.NextMove.Intents.OfType<AttackIntent>().Sum(i => i.GetTotalDamage(s.State.PlayerCreatures, attacker));
                int hp = s.State.Players[0].Creature.CurrentHp;
                int leftBlockBefore = s.Left.Creature.Block;
                int rightBlockBefore = s.Right.Creature.Block;
                await Act(s);
                Assert(hp - s.State.Players[0].Creature.CurrentHp == expectedDmg,
                    $"Turn {turn} ({sequence[slot]}): actual damage {hp - s.State.Players[0].Creature.CurrentHp} != intent {expectedDmg}.");
                if (slot == 3)
                    Assert(s.State.Players[0].Creature.HasPower<FrailPower>(), "RockSweep must apply FrailPower.");
                if (slot == 6)
                {
                    int expectedGuardBlock = ascension == 0 ? 15 : 18;
                    Assert(s.Left.Creature.Block - leftBlockBefore == expectedGuardBlock &&
                           s.Right.Creature.Block - rightBlockBefore == expectedGuardBlock,
                        $"P1 Guard must grant +{expectedGuardBlock} block to each healthy arm.");
                }

                int strength = (turn + 1) / 7 * (ascension == 0 ? 2 : 3);
                foreach (var owner in new[] { s.Body.Creature, s.Left.Creature, s.Right.Creature })
                    Assert(owner.GetPowerAmount<StrengthPower>() == strength, $"Strength sync drift at turn {turn}.");
            }
            DeactivateSyntheticCombat();
        }

        // Verify Phase 2 enraged 2-segment Left+Right combos on HeavyPunch, RockSweep, and Sweep!
        {
            var p2 = Scenario();
            await Phase2(p2);
            AddTestCard<Inflame>(p2, p2.State.Players[0]);
            AddTestCard<DemonForm>(p2, p2.State.Players[0]);

            // 1) Crush (Body) -> HeavyPunch (Left + Right 2-segment!)
            Assert(p2.Body.NextMove.Id == ThingsCaveGodBody.CrushId, "Expected CrushId at P2 start.");
            Assert(p2.Body.NextMove.Intents.OfType<AttackIntent>().Single().DamageCalc!() == 8m, "P2 Crush base per-hit damage must remain 8 on A0.");
            await Act(p2);

            foreach (string expectedMove in new[]
            {
                ThingsCaveGodBody.HeavyPunchId,
                ThingsCaveGodBody.GrabId,
                ThingsCaveGodBody.AirSlamId,
                ThingsCaveGodBody.RockSweepId,
                ThingsCaveGodBody.SweepId,
                ThingsCaveGodBody.AngrySlamId,
            })
            {
                Assert(p2.Body.NextMove.Id == expectedMove, $"P2 cycle drift: expected {expectedMove}, got {p2.Body.NextMove.Id}.");
                bool isCombo = expectedMove is ThingsCaveGodBody.HeavyPunchId or ThingsCaveGodBody.RockSweepId or ThingsCaveGodBody.SweepId;
                var attackers = p2.State.Enemies.Where(c => c.Monster!.NextMove.Intents.OfType<AttackIntent>().Any()).ToArray();
                Assert(attackers.Length == (isCombo ? 2 : 1),
                    $"P2 move {expectedMove}: expected {(isCombo ? 2 : 1)} advertised attackers, got {attackers.Length}.");
                int expectedP2Base = expectedMove switch
                {
                    ThingsCaveGodBody.HeavyPunchId => 11,
                    ThingsCaveGodBody.GrabId => 6,
                    ThingsCaveGodBody.AirSlamId => 16,
                    ThingsCaveGodBody.RockSweepId => 9,
                    ThingsCaveGodBody.SweepId => 8,
                    ThingsCaveGodBody.AngrySlamId => 17,
                    _ => throw new InvalidOperationException(),
                };
                Assert(attackers.All(a => a.Monster!.NextMove.Intents.OfType<AttackIntent>().Single().DamageCalc!() == expectedP2Base),
                    $"P2 move {expectedMove}: expected dynamic base damage {expectedP2Base} (+0 Phase 2 bonus).");
                if (isCombo)
                {
                    Assert(attackers.Contains(p2.Left.Creature) && attackers.Contains(p2.Right.Creature),
                        $"P2 combo {expectedMove} must activate BOTH LeftHand and RightHand.");
                }
                int expectedTotal = attackers.Sum(a => a.Monster!.NextMove.Intents.OfType<AttackIntent>().Sum(i => i.GetTotalDamage(p2.State.PlayerCreatures, a)));
                int hpBefore = p2.State.Players[0].Creature.CurrentHp;
                await Act(p2);
                Assert(hpBefore - p2.State.Players[0].Creature.CurrentHp == expectedTotal,
                    $"P2 move {expectedMove}: actual damage {hpBefore - p2.State.Players[0].Creature.CurrentHp} != advertised {expectedTotal}.");
                if (expectedMove == ThingsCaveGodBody.SweepId)
                {
                    Assert(p2.Left.StolenCards.Count == 1 && p2.Right.StolenCards.Count == 1,
                        "P2 2-segment Sweep must steal 1 card into LeftHand and 1 card into RightHand.");
                }
            }
            Assert(p2.Body.NextMove.Id == ThingsCaveGodBody.CrushId, "P2 cycle must loop cleanly back to CrushId.");
            DeactivateSyntheticCombat();
        }

        // Verify natural RNG lead initialization & Right-leading Turn 1
        {
            var naturalRng = Scenario(forceRightLead: null);
            int grabbers = (naturalRng.Left.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Grab) ? 1 : 0)
                         + (naturalRng.Right.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Grab) ? 1 : 0);
            Assert(grabbers == 1, "Natural RNG initialization must assign exactly one arm to Turn 1 Grab.");
            DeactivateSyntheticCombat();

            var rightStart = Scenario(forceRightLead: true);
            Assert(rightStart.Right.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Grab) &&
                   rightStart.Left.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Rest),
                "forceRightLead=true must start Turn 1 Grab on RightHand.");
            DeactivateSyntheticCombat();
        }

        // Give body and arm different modifiers to verify body attacks never read arm Weak.
        var mismatch = Scenario();
        await Act(mismatch); // Turn 1 Grab
        await Hit(mismatch, mismatch.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw));
        await Act(mismatch); // Turn 2 Escape -> Sweep
        await Act(mismatch); // Turn 3 Sweep -> RockSweep
        await Act(mismatch); // Turn 4 RockSweep -> Jabs
        Assert(mismatch.Body.NextMove.Id == ThingsCaveGodBody.JabsId, $"Expected JabsId, got {mismatch.Body.NextMove.Id}.");
        await PowerCmd.Apply<WeakPower>(Choice, mismatch.Left.Creature, 4m, mismatch.State.Players[0].Creature, null);
        var intent = mismatch.Body.NextMove.Intents.OfType<AttackIntent>().Single();
        int advertised = intent.GetTotalDamage(mismatch.State.PlayerCreatures, mismatch.Body.Creature);
        int health = mismatch.State.Players[0].Creature.CurrentHp;
        await Act(mismatch);
        Assert(health - mismatch.State.Players[0].Creature.CurrentHp == advertised, "Body attacks must not use arm Weak modifiers.");
        DeactivateSyntheticCombat();
        GD.Print("PASS 14-turn P1 7-move loop with hand inversion, P2 Left+Right 2-segment combos, RNG lead init, exact intent damage, +2/+3 growth");
    }
    private static async Task VerifyPhaseScaling()
    {
        foreach (int count in new[] { 1, 2, 4 })
        foreach (int ascension in new[] { 0, 10 })
        {
            var s = Scenario(count, ascension);
            int Scale(int hp) => (int)Creature.ScaleHpForMultiplayer(hp, s.State.Encounter, count, s.State.RunState.CurrentActIndex);
            Assert(s.Body.Creature.MaxHp == Scale(ascension == 0 ? 226 : 244),
                $"Core MaxHp mismatch ({count}p A{ascension}): got {s.Body.Creature.MaxHp}.");
            Assert(s.Left.Creature.MaxHp == Scale(ascension == 0 ? 68 : 75) && s.Right.Creature.MaxHp == s.Left.Creature.MaxHp,
                $"Arm 1.5x MaxHp mismatch ({count}p A{ascension}): got {s.Left.Creature.MaxHp}.");

            await Act(s); // Turn 1 Grab spawns Phase 1 CaptiveClaw
            var clawP1 = s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive);
            Assert(clawP1.MaxHp == Scale(ascension == 0 ? 22 : 27),
                $"Phase 1 CaptiveClaw MaxHp mismatch ({count}p A{ascension}): got {clawP1.MaxHp}.");
            await Hit(s, clawP1);

            await Phase2(s);
            Assert(s.Body.Phase == 2 && s.Body.Creature.MaxHp == Scale(ascension == 0 ? 226 : 244),
                "Single honest Boss HP bar must keep its MaxHp across half-HP Phase 2 transition.");
            Assert(!s.Body.ShouldTremble,
                $"Body must NOT tremble at 50% HP ({s.Body.Creature.CurrentHp}/{s.Body.Creature.MaxHp}).");
            Assert(s.Left.Creature.MaxHp == Scale(ascension == 0 ? 68 : 75) && s.Right.Creature.MaxHp == s.Left.Creature.MaxHp,
                "Arms retain 68/75 scaled MaxHp in Phase 2.");

            await Act(s); // Crush -> HeavyPunch
            await Act(s); // HeavyPunch -> Grab
            await Act(s); // Phase 2 Grab spawns Phase 2 CaptiveClaw
            var clawP2 = s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive);
            Assert(clawP2.MaxHp == Scale(ascension == 0 ? 27 : 32),
                $"Phase 2 CaptiveClaw MaxHp mismatch ({count}p A{ascension}): got {clawP2.MaxHp}.");
            await Hit(s, clawP2);
            await Hit(s, s.Right.Creature); // Expose core so unpowered damage is 1:1

            int thresholdHp = s.Body.Creature.MaxHp / 5;
            await Hit(s, s.Body.Creature, s.Body.Creature.CurrentHp - (thresholdHp + 1));
            Assert(s.Body.Creature.CurrentHp == thresholdHp + 1 && !s.Body.ShouldTremble,
                $"Body must stay steady above 20% HP ({s.Body.Creature.CurrentHp}/{s.Body.Creature.MaxHp}).");
            await Hit(s, s.Body.Creature, 1m);
            Assert(s.Body.Creature.CurrentHp == thresholdHp && s.Body.ShouldTremble,
                $"Body must tremble at <= 20% HP ({s.Body.Creature.CurrentHp}/{s.Body.Creature.MaxHp}).");
            await CreatureCmd.Heal(s.Body.Creature, 5m);
            Assert(!s.Body.ShouldTremble,
                $"Body must stop trembling when healed above 20% HP ({s.Body.Creature.CurrentHp}/{s.Body.Creature.MaxHp}).");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS Core (226/244), Arm 1.5x (68/75), Claw P1 (22/27) & P2 (27/32) HP scaling, and <=20% HP critical tremor gate");
    }
    private static async Task StartCapture(Fixture s, bool phase2 = false)
    {
        Assert(s.Body.NextMove.Id == ThingsCaveGodBody.GrabId, $"Expected GrabId before StartCapture, got {s.Body.NextMove.Id}.");
        await Act(s);
        Assert(s.State.Enemies.Count(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive) == 1,
            "Capture must create exactly one living CaptiveClaw.");

        if (!phase2)
        {
            // ThrowingPlayerChoiceContext remains active across the entire Phase 1 capture turn -> proves ZERO modal popups!
            foreach (var player in s.State.Players)
                await Hook.AfterPlayerTurnStart(s.State, Choice, player);
            Assert(s.State.PlayerCreatures.All(c =>
                    c.HasPower<VulnerablePower>() &&
                    !c.Powers.Any(p => p.GetType().Namespace?.StartsWith("STS2_Things") == true)),
                "Phase 1 capture must apply VulnerablePower and zero custom Trial powers.");
        }
        else
        {
            Assert(s.State.PlayerCreatures.All(c => c.HasPower<ThingsCaveGodPendingTrialPower>()),
                "Phase 2 capture must schedule ThingsCaveGodPendingTrialPower before draw.");
            int[] indices = Enumerable.Range(0, s.State.Players.Count).Select(i => i % 2).ToArray();
            var selector = new TrialQueueCardSelector(indices);
            using (ProbeRuntimeApi.UseSelector(selector))
            {
                foreach (var player in s.State.Players)
                    await Hook.AfterPlayerTurnStart(s.State, Choice, player);
            }
            Assert(selector.SelectionCount == s.State.Players.Count,
                $"Expected {s.State.Players.Count} Phase 2 Trial choices, got {selector.SelectionCount}.");
            Assert(s.State.PlayerCreatures.All(c => !c.HasPower<ThingsCaveGodPendingTrialPower>()),
                "PendingTrialPower must be consumed during AfterPlayerTurnStartLate.");

            var p0 = s.State.Players[0];
            var brokenBlade = p0.Creature.GetPower<ThingsCaveGodBrokenBladePower>();
            Assert(brokenBlade != null, "Player 0 choosing option 0 must receive BrokenBladePower.");
            CardModel atk0 = s.State.CreateCard(ModelDb.Card<StrikeIronclad>(), p0);
            CardModel skl0 = s.State.CreateCard(ModelDb.Card<DefendIronclad>(), p0);
            Assert(!brokenBlade!.ShouldPlay(atk0, AutoPlayType.None) && brokenBlade.ShouldPlay(skl0, AutoPlayType.None),
                "BrokenBladePower must block Attack cards while allowing Skill cards.");

            if (s.State.Players.Count > 1)
            {
                var p1 = s.State.Players[1];
                var shatteredShield = p1.Creature.GetPower<ThingsCaveGodShatteredShieldPower>();
                Assert(shatteredShield != null, "Player 1 choosing option 1 must receive ShatteredShieldPower.");
                CardModel atk1 = s.State.CreateCard(ModelDb.Card<StrikeIronclad>(), p1);
                CardModel skl1 = s.State.CreateCard(ModelDb.Card<DefendIronclad>(), p1);
                Assert(!shatteredShield!.ShouldPlay(skl1, AutoPlayType.None) && shatteredShield.ShouldPlay(atk1, AutoPlayType.None),
                    "ShatteredShieldPower must block Skill cards while allowing Attack cards.");
            }
        }
    }
    private static async Task VerifyCapture(bool escape, bool phase2)
    {
        var s = Scenario(2);
        if (phase2)
        {
            await Phase2(s);
            await Act(s); // Crush -> HeavyPunch
            await Act(s); // HeavyPunch (2-segment L+R, keeps _rightLeads=false) -> Grab (Left leads Cycle 0!)
        }
        for (int cycle = 0; cycle < 2; cycle++)
        {
            await StartCapture(s, phase2);
            ThingsCaveGodHand grabber = s.Left.IsGrabbing ? s.Left : s.Right;
            bool expectedLeft = cycle == 0;
            Assert(grabber.IsLeft == expectedLeft,
                $"Lead grabber mismatch (phase2={phase2}, cycle={cycle}): got isLeft={grabber.IsLeft}.");
            Assert(!grabber.Creature.IsHittable && !grabber.ShouldAllowTargeting(grabber.Creature),
                "Grabbing arm must be untargetable while players are inside CaptiveClaw.");

            if (escape)
            {
                await Hit(s, s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw));
                Assert(grabber.NextMove.Intents.Any(i => i is StunIntent), "Breaking claw must stun grabber.");
                if (phase2)
                {
                    Assert(s.State.PlayerCreatures.All(c =>
                            !c.HasPower<ThingsCaveGodBrokenBladePower>() &&
                            !c.HasPower<ThingsCaveGodShatteredShieldPower>()),
                        "Breaking CaptiveClaw in Phase 2 must immediately lift all Trial card bans mid-turn.");
                }
            }
            else if (phase2)
            {
                await Hook.AfterSideTurnEnd(s.State, CombatSide.Player, s.State.PlayerCreatures);
                Assert(s.State.PlayerCreatures.All(c =>
                        !c.HasPower<ThingsCaveGodBrokenBladePower>() &&
                        !c.HasPower<ThingsCaveGodShatteredShieldPower>()),
                    "Phase 2 Trial card bans must expire cleanly at end of player turn.");
            }

            int hp = s.State.Players[0].Creature.CurrentHp;
            await Act(s);
            Assert(escape ? s.State.Players[0].Creature.CurrentHp == hp : s.State.Players[0].Creature.CurrentHp < hp,
                "Broken claw caused hidden hit or intact claw failed to slam.");
            Assert(!s.State.Enemies.Any(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive), "Claw survived resolution.");
            Assert(!s.Left.IsGrabbing && !s.Right.IsGrabbing, "Capture flag survived resolution.");

            if (phase2)
            {
                Assert(s.Body.NextMove.Id == ThingsCaveGodBody.RockSweepId, "P2 throw/escape must advance to ROCK_SWEEP.");
                await Act(s); // RockSweep -> Sweep
                await Act(s); // Sweep -> AngrySlam
                await Act(s); // AngrySlam -> Crush
                await Act(s); // Crush -> HeavyPunch
                await Act(s); // HeavyPunch -> Grab
            }
            else
            {
                Assert(s.Body.NextMove.Id == ThingsCaveGodBody.SweepId, "P1 throw/escape must advance to P1_REST_SWEEP.");
                await Act(s); // Sweep -> RockSweep
                await Act(s); // RockSweep -> Jabs
                await Act(s); // Jabs -> HeavyPunch
                await Act(s); // HeavyPunch -> Guard
                await Act(s); // Guard -> Grab
            }
        }
        DeactivateSyntheticCombat();
        GD.Print($"PASS capture (phase2={phase2}, escape={escape}): P1 zero-popup vs P2 Trial bans, mid-turn break lift, hand alternation");
    }
    private static async Task VerifyCaptureInterrupted()
    {
        var s = Scenario();
        await StartCapture(s); // Turn 1 Left grabs!
        await Hit(s, s.Right.Creature); // Break support arm during capture!
        Assert(s.Body.IsWeakPhase && !s.Left.IsGrabbing && s.Left.IsHealthBarVisible,
            "Breaking support arm must interrupt grab and restore surviving arm health bar visibility.");
        Assert(!s.State.Enemies.Any(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive), "Orphan claw after exposure.");
        await Act(s);
        await Act(s);
        await Act(s);
        Assert(!s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.SweepId,
            "Recovery after interrupted P1 capture must skip AirSlam and resume at SweepId.");
        DeactivateSyntheticCombat();
        GD.Print("PASS mid-capture support break cleans up and skips obsolete throw");
    }
    private static async Task VerifyEnemyTurnBreak()
    {
        var s = Scenario();
        s.State.CurrentSide = CombatSide.Enemy;
        await Hit(s, s.Left.Creature);
        await Act(s);
        Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed1Id, "Enemy-turn defeat must not consume the first player exposure window.");
        await Act(s);
        Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id, "Second player exposure window must stay prone (WEAK_PRONE_2).");
        await Act(s);
        Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId, "Third player exposure window must advertise recovery.");
        await Act(s);
        Assert(!s.Body.IsWeakPhase, "Deferred 3-turn exposure must end cleanly.");
        DeactivateSyntheticCombat();
        GD.Print("PASS enemy-turn defeat grants three complete player exposure windows");
    }
    private static async Task VerifyFinalDefeat()
    {
        var s = Scenario();
        await Phase2(s);
        await Act(s); // Crush -> HeavyPunch
        await Act(s); // HeavyPunch -> Grab
        await StartCapture(s, phase2: true);
        await Hit(s, s.Body.Creature);
        Assert(s.Body.IsDefeated && !s.Body.ShouldStopCombatFromEnding(), "Core defeat must end the encounter.");
        Assert(!s.State.Enemies.Any(c => c.IsAlive), "Living boss part remains after victory.");
        Assert(s.State.PlayerCreatures.All(c =>
                !c.HasPower<ThingsCaveGodBrokenBladePower>() &&
                !c.HasPower<ThingsCaveGodShatteredShieldPower>() &&
                !c.HasPower<ThingsCaveGodPendingTrialPower>()),
            "Boss defeat during P2 capture must clean up all Trial powers.");
        DeactivateSyntheticCombat();
        GD.Print("PASS final defeat kills parts and clears capture + Trial powers");
    }

    private static async Task VerifyPoisonAndThorns()
    {
        foreach (bool left in new[] { true, false })
        {
            var s = Scenario();
            var hand = left ? s.Left.Creature : s.Right.Creature;
            await PowerCmd.Apply<PoisonPower>(Choice, hand, 200m, s.State.Players[0].Creature, null);
            s.State.CurrentSide = CombatSide.Enemy;
            await hand.GetPower<PoisonPower>()!.Trigger();
            Assert(!hand.HasPower<PoisonPower>() && s.Body.IsWeakPhase, "Lethal poison tick fails to cleanse itself.");
            DeactivateSyntheticCombat();
        }
        var thorns = Scenario();
        await PowerCmd.Apply<ThornsPower>(Choice, thorns.State.Players[0].Creature, 200m, thorns.State.Players[0].Creature, null);
        await Act(thorns); // Turn 1 Grab hits Thorns -> Left arm breaks mid-Grab!
        Assert(thorns.Body.IsWeakPhase && thorns.Body.NextMove.Id == ThingsCaveGodBody.Exposed1Id,
            "Real thorns break during Grab did not defer exposure window.");
        Assert(!thorns.State.Enemies.Any(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive),
            "Thorns break during Grab must abort spawning CaptiveClaw.");
        DeactivateSyntheticCombat();
        GD.Print("PASS real lethal Poison hook and retaliatory Thorns defeat during Grab");
    }
    private static async Task VerifyNativeStunAndForcedDeath()
    {
        var s = Scenario();
        await StartCapture(s);
        await CreatureCmd.Stun(s.Left.Creature);
        Assert(s.Left.NextMove.Intents.Any(i => i is StunIntent), "Native stun was blocked by arm move flags.");
        int hp = s.State.Players[0].Creature.CurrentHp;
        await Act(s);
        Assert(s.State.Players[0].Creature.CurrentHp == hp && !s.Left.IsGrabbing, "Native stun leaves an active capture or unseen attack.");
        Assert(!s.State.Enemies.Any(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive), "Native stun leaves a claw.");
        DeactivateSyntheticCombat();

        var forced = Scenario();
        await CreatureCmd.Kill(forced.Body.Creature, force: true);
        Assert(forced.Body.IsDefeated && !forced.Body.ShouldStopCombatFromEnding() && forced.State.Enemies.All(c => c.IsDead), "Forced P1 removal leaves immortal parts.");
        DeactivateSyntheticCombat();

        var armForced = Scenario();
        await CreatureCmd.Kill(armForced.Left.Creature, force: true);
        Assert(armForced.State.Enemies.Contains(armForced.Left.Creature) && armForced.Left.IsDown && armForced.Body.IsWeakPhase,
            "Forced arm kill removed the arm or failed to expose the core.");
        await Act(armForced);
        await Act(armForced);
        await Act(armForced);
        Assert(!armForced.Body.IsWeakPhase && !armForced.Left.IsDown && armForced.Left.Creature.CurrentHp > 0 &&
            armForced.Left.Creature.HasPower<ThingsCaveGodAgingPower>(),
            "Forced arm kill failed to recover cleanly with its aging power intact.");
        DeactivateSyntheticCombat();
        GD.Print("PASS native stun cancels capture; forced death cannot softlock encounter");
    }

    private static async Task VerifyTransitionPoisonAndGrowth()
    {
        var s = Scenario();
        // Run 1 full P1 7-move cycle (Grab -> Escape -> Sweep -> RockSweep -> Jabs -> HeavyPunch -> Guard [+2 Strength])
        await Act(s);
        await Hit(s, s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw));
        await Act(s);
        await Act(s);
        await Act(s);
        await Act(s);
        await Act(s);
        await Act(s);
        Assert(s.Body.Creature.GetPowerAmount<StrengthPower>() == 2, "P1 Guard must grant +2 Strength.");

        await PowerCmd.Apply<PoisonPower>(Choice, s.Right.Creature, 999m, s.State.Players[0].Creature, null);
        await Hit(s, s.Left.Creature);
        await Hit(s, s.Body.Creature, s.Body.Creature.CurrentHp - s.Body.EnrageHpThreshold + 25);
        Assert(s.Body.IsEnrageLocked && s.Body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId,
            "Half-HP hit on exposed core must immediately lock EnrageRecoverId.");
        await s.Right.Creature.GetPower<PoisonPower>()!.Trigger();
        Assert(s.Right.IsDown && s.State.Enemies.Contains(s.Right.Creature), "Lethal poison on surviving arm disables rather than deletes it.");

        await Act(s); // ENRAGE_RECOVER -> Phase 2 (+2 Strength -> total 4 synced across Body + Arms!)
        Assert(s.Body.Phase == 2 && s.Body.Creature.CurrentHp == s.Body.EnrageHpThreshold, "Must transition to Phase 2 at 50% HP.");
        foreach (var c in new[] { s.Body.Creature, s.Left.Creature, s.Right.Creature })
            Assert(c.GetPowerAmount<StrengthPower>() == 4, $"Phase 2 awakening must sync Strength=4 across all parts, got {c.GetPowerAmount<StrengthPower>()} on {c.Monster?.Id.Entry}.");
        Assert(!s.Right.Creature.HasPower<PoisonPower>(), "Phase 2 recovery must cleanse arm poison.");
        Assert(s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(), "Phase 2 awakening must restore Granite Bulwark.");
        DeactivateSyntheticCombat();
        GD.Print("PASS poison during transition, synced Strength growth across phases, and Granite Bulwark restoration");
    }

    private static async Task VerifyRepeatedBreakGrowth()
    {
        foreach (int ascension in new[] { 0, 10 })
        {
            var s = Scenario(ascension: ascension);
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                await Hit(s, s.Left.Creature);
                await Act(s);
                Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id, "Exposure turn 2 must remain prone (WEAK_PRONE_2).");
                await Act(s);
                Assert(s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId &&
                       s.Body.NextMove.Intents.Any(i => i is HealIntent) &&
                       s.Body.NextMove.Intents.Any(i => i is BuffIntent),
                    "Recovery growth must be telegraphed.");
                await Act(s);
                foreach (var c in new[] { s.Body.Creature, s.Left.Creature, s.Right.Creature })
                    Assert(c.GetPowerAmount<StrengthPower>() == cycle * (ascension == 0 ? 2 : 3), "Repeated breaks freeze or duplicate growth.");
            }
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS repeated arm breaks grant consistent +2/+3 recovery growth (A0/A10)");
    }

    private static async Task VerifyStunnedArmGrowth()
    {
        var s = Scenario();
        await Act(s); // Grab
        await Hit(s, s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw));
        await Act(s); // Escape -> Sweep
        await Act(s); // Sweep -> RockSweep
        await Act(s); // RockSweep -> Jabs
        await Act(s); // Jabs -> HeavyPunch
        await Act(s); // HeavyPunch -> next is Guard (the growth turn)
        Assert(s.Body.NextMove.Id == ThingsCaveGodBody.GuardId, "Expected GuardId before stun growth check.");
        await CreatureCmd.Stun(s.Left.Creature);
        await Hook.AfterSideTurnStart(s.State, CombatSide.Player, s.State.PlayerCreatures);
        Assert(s.Left.IsStunned && s.Left.ShouldAllowTargeting(s.Left.Creature), "Native stun skips actions without making a healthy arm untargetable.");
        await Act(s);
        Assert(s.Left.Creature.GetPowerAmount<StrengthPower>() == 2, "Stunned arm silently misses encounter growth.");
        DeactivateSyntheticCombat();
        GD.Print("PASS healthy stunned arm receives cycle growth and remains targetable");
    }

    private static async Task VerifyWeakPhaseSurvivingArmFlow()
    {
        foreach (bool breakLeft in new[] { true, false })
        {
            var s = Scenario();
            ThingsCaveGodHand broken = breakLeft ? s.Left : s.Right;
            ThingsCaveGodHand survivor = breakLeft ? s.Right : s.Left;

            // Turn 1: Player breaks one arm -> Exposure Turn 1
            await Hit(s, broken.Creature);
            Assert(s.Body.IsWeakPhase, "Core must be exposed after arm break.");
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed1Id, "Body must enter WEAK_PRONE_1.");
            Assert(broken.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Down), "Broken arm must be Down.");
            Assert(survivor.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakStun), "Surviving arm must be WeakStun on turn 1.");
            Assert(survivor.NextMove.Intents.OfType<StunIntent>().Any(), "Surviving arm must have StunIntent on turn 1.");
            Assert(!survivor.NextMove.Intents.OfType<AttackIntent>().Any(), "Surviving arm must not attack on turn 1.");

            // Exposure Turn 2: Body remains prone (WEAK_PRONE_2), broken arm stays Down, surviving arm advertises WeakAttack
            await Act(s);
            Assert(s.Body.IsWeakPhase, "Core must remain exposed on turn 2.");
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id, "Body must advance to WEAK_PRONE_2.");
            Assert(broken.IsDown && broken.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Down),
                "Broken arm must remain IsDown on DOWN_STATE during Exposure Turn 2.");
            Assert(survivor.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakAttack), "Surviving arm must be WeakAttack on turn 2.");
            Assert(!survivor.NextMove.Intents.OfType<StunIntent>().Any(), "Surviving arm must NOT have StunIntent on turn 2.");
            Assert(survivor.NextMove.Intents.OfType<AttackIntent>().Any(), "Surviving arm must advertise attack on turn 2.");

            // Enemy Turn 2 executes: surviving arm attacks; Body advances to WEAK_UNKNOWN_3 (recovery omen)
            int playerHpBeforeTurn2 = s.State.Players[0].Creature.CurrentHp;
            await Act(s);
            int damageTakenTurn2 = playerHpBeforeTurn2 - s.State.Players[0].Creature.CurrentHp;
            Assert(damageTakenTurn2 > 0, "Surviving arm must have attacked on exposure turn 2.");
            Assert(s.Body.IsWeakPhase, "Core must remain exposed on turn 3.");
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId, "Body must advance to WEAK_UNKNOWN_3.");
            Assert(broken.IsDown && broken.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Recover),
                "Broken arm must remain IsDown on WEAK_RECOVER during Exposure Turn 3.");
            Assert(survivor.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakAttack), "Surviving arm must be WeakAttack on turn 3.");
            Assert(!survivor.NextMove.Intents.OfType<StunIntent>().Any(), "Surviving arm must NOT have StunIntent on turn 3.");
            Assert(survivor.NextMove.Intents.OfType<AttackIntent>().Any(), "Surviving arm must advertise attack on turn 3.");

            // Enemy Turn 3 executes: surviving arm attacks + Body awakens & restores both arms!
            int playerHpBefore = s.State.Players[0].Creature.CurrentHp;
            await Act(s);
            int damageTaken = playerHpBefore - s.State.Players[0].Creature.CurrentHp;
            Assert(damageTaken > 0, "Surviving arm must have attacked on exposure turn 3.");
            Assert(!s.Body.IsWeakPhase, "3-turn exposure must end after recovery executes.");
            Assert(s.Left.Creature.CurrentHp == s.Left.Creature.MaxHp && s.Right.Creature.CurrentHp == s.Right.Creature.MaxHp, "Both arms must be healed to full.");
            Assert(s.Left.ShouldAllowTargeting(s.Left.Creature) && s.Right.ShouldAllowTargeting(s.Right.Creature), "Arms must be targetable after recovery.");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS 3-turn weak phase surviving arm: turn 1 stun, turn 2 weak attack, turn 3 weak attack + recovery");
    }

    private static CardModel AddTestCard<T>(Fixture s, Player player) where T : CardModel
    {
        CardModel card = s.State.CreateCard(ModelDb.Card<T>(), player);
        card.DeckVersion = card;
        player.PlayerCombatState!.DrawPile!.AddInternal(card);
        return card;
    }

    private static async Task VerifySweepCardTheft()
    {
        // 1. Highest rarity first: Ancient over Rare, Uncommon, Common, Basic for Left Hand
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var basic = AddTestCard<Bash>(s, p);
            var common = AddTestCard<IronWave>(s, p);
            var uncommon = AddTestCard<Inflame>(s, p);
            var rare = AddTestCard<DemonForm>(s, p);
            var ancient = AddTestCard<Apotheosis>(s, p);

            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();

            Assert(s.Left.StolenCards.Count == 1, "Left arm must steal exactly 1 card.");
            Assert(s.Left.StolenCards[0] == ancient, "Theft rarity priority failed: Ancient must be stolen first.");
            Assert(ancient.Pile == null, "Stolen card must be removed from combat piles.");
            Assert(s.Right.StolenCards.Count == 0, "Right arm must not hold Left arm's stolen card.");

            // Downing Left Hand returns the card to player Hand
            await Hit(s, s.Left.Creature);
            Assert(s.Left.StolenCards.Count == 0, "Downed arm must release all stolen cards.");
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(ancient), "Released card must be placed back into player Hand.");
            DeactivateSyntheticCombat();
        }

        // 2. Rare over Uncommon, Common, Basic for Right Hand
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var basic = AddTestCard<Bash>(s, p);
            var common = AddTestCard<IronWave>(s, p);
            var uncommon = AddTestCard<Inflame>(s, p);
            var rare = AddTestCard<DemonForm>(s, p);

            s.Right.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Right.PerformMove();

            Assert(s.Right.StolenCards.Count == 1, "Right arm must steal exactly 1 card.");
            Assert(s.Right.StolenCards[0] == rare, "Theft rarity priority failed: Rare must be stolen over Uncommon/Common/Basic.");
            Assert(rare.Pile == null, "Stolen card must be removed from combat piles.");
            Assert(s.Left.StolenCards.Count == 0, "Left arm must not hold Right arm's stolen card.");

            // Downing Right Hand returns the card to player Hand
            await Hit(s, s.Right.Creature);
            Assert(s.Right.StolenCards.Count == 0, "Downed right arm must release stolen cards.");
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(rare), "Released rare card must be placed into player Hand.");
            DeactivateSyntheticCombat();
        }

        // 3. Uncommon over Common and Basic
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var basic = AddTestCard<Bash>(s, p);
            var common = AddTestCard<IronWave>(s, p);
            var uncommon = AddTestCard<Inflame>(s, p);

            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();

            Assert(s.Left.StolenCards.Count == 1 && s.Left.StolenCards[0] == uncommon, "Theft rarity priority failed: Uncommon must be stolen before Common/Basic.");
            await Hit(s, s.Left.Creature);
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(uncommon), "Uncommon card must return to Hand upon arm downed.");
            DeactivateSyntheticCombat();
        }

        // 4. Common over Basic, then Basic fallback
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var basic = AddTestCard<Bash>(s, p);
            var common = AddTestCard<IronWave>(s, p);

            s.Right.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Right.PerformMove();

            Assert(s.Right.StolenCards.Count == 1 && s.Right.StolenCards[0] == common, "Theft rarity priority failed: Common must be stolen before Basic.");
            await Hit(s, s.Right.Creature);
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(common), "Common card must return to Hand upon arm downed.");
            DeactivateSyntheticCombat();
        }

        // 5. Multiplayer 2-player theft and return
        {
            var s = Scenario(count: 2);
            var p0 = s.State.Players[0];
            var p1 = s.State.Players[1];
            var card0 = AddTestCard<Inflame>(s, p0);
            var card1 = AddTestCard<DemonForm>(s, p1);

            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();

            Assert(s.Left.StolenCards.Count == 2, "Left arm must steal 1 card per living player in multiplayer.");
            Assert(s.Left.StolenCards.Contains(card0) && s.Left.StolenCards.Contains(card1), "Left arm must hold cards from both players.");

            // Downing Left arm returns each card to its respective owner's hand
            await Hit(s, s.Left.Creature);
            Assert(s.Left.StolenCards.Count == 0, "Downed arm must release all cards.");
            Assert(p0.PlayerCombatState!.Hand!.Cards.Contains(card0), "Player 0's card must return to Player 0's Hand.");
            Assert(p1.PlayerCombatState!.Hand!.Cards.Contains(card1), "Player 1's card must return to Player 1's Hand.");
            DeactivateSyntheticCombat();
        }

        // 6. Boss defeat releases any held cards
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var card = AddTestCard<Inflame>(s, p);

            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();
            Assert(s.Left.StolenCards.Count == 1, "Left arm holds card before boss defeat.");

            // Defeat boss body directly
            await ProbeRuntimeApi.AfterDeath(s.Body, Choice, s.Body.Creature, false, 0f);
            Assert(s.Left.StolenCards.Count == 0, "Boss body defeat must release all held cards.");
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(card), "Card must be returned to Hand upon boss defeat.");
            DeactivateSyntheticCombat();
        }

        // 7. Hand full (10 cards) overflow routing to Discard upon arm downed
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var stolenCard = AddTestCard<Inflame>(s, p);

            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();
            Assert(s.Left.StolenCards.Count == 1, "Left arm must steal card.");

            // Fill hand to 10 cards
            for (int i = 0; i < CardPile.MaxCardsInHand; i++)
            {
                var dummy = s.State.CreateCard(ModelDb.Card<StrikeIronclad>(), p);
                dummy.DeckVersion = dummy;
                p.PlayerCombatState!.Hand!.AddInternal(dummy, 0);
            }
            Assert(p.PlayerCombatState!.Hand!.Cards.Count == CardPile.MaxCardsInHand, "Hand must have 10 cards.");

            // Downing Left Hand when hand is full routes to Discard pile
            await Hit(s, s.Left.Creature);
            Assert(s.Left.StolenCards.Count == 0, "Downed arm must release card even when hand is full.");
            Assert(p.PlayerCombatState!.DiscardPile!.Cards.Contains(stolenCard), "Stolen card must be routed to Discard pile when hand is full.");
            DeactivateSyntheticCombat();
        }

        // 8. Hand fallback theft (Draw and Discard are empty)
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var handCard = s.State.CreateCard(ModelDb.Card<Inflame>(), p);
            handCard.DeckVersion = handCard;
            p.PlayerCombatState!.Hand!.AddInternal(handCard, 0);

            s.Right.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Right.PerformMove();

            Assert(s.Right.StolenCards.Count == 1, "Right arm must steal card from Hand when Draw/Discard empty.");
            Assert(s.Right.StolenCards[0] == handCard, "Stolen card must match the Hand card.");
            Assert(!p.PlayerCombatState!.Hand!.Cards.Contains(handCard), "Card must be removed from Hand.");

            await Hit(s, s.Right.Creature);
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(handCard), "Card must return to Hand upon arm downed.");
            DeactivateSyntheticCombat();
        }

        // 9. Stolen card preserved into Phase 2 until arm is downed in Phase 2
        {
            var s = Scenario();
            var p = s.State.Players[0];
            var card = AddTestCard<Inflame>(s, p);

            s.Right.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Right.PerformMove();
            Assert(s.Right.StolenCards.Count == 1, "Right arm holds stolen card.");

            // Down Left arm to expose core
            await Hit(s, s.Left.Creature);
            Assert(s.Body.IsWeakPhase, "Core exposed.");

            // Half-HP hit on exposed core immediately locks ∞ + EnrageRecover without dropping Right arm's stolen card
            await Hit(s, s.Body.Creature, s.Body.Creature.CurrentHp - s.Body.EnrageHpThreshold + 20);
            Assert(s.Body.IsEnrageLocked && s.Body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId,
                "Half-HP exposed core must immediately prepare EnrageRecover.");

            // Right arm still holds the card during transition
            Assert(s.Right.StolenCards.Count == 1, "Right arm preserves held card into Phase 2.");

            // Complete same-turn phase transition
            await Act(s);
            Assert(s.Body.Phase == 2, "Transitioned to Phase 2.");
            Assert(s.Right.StolenCards.Count == 1, "Right arm continues holding card in Phase 2.");

            // Downing Right arm in Phase 2 releases the card to Hand
            await Hit(s, s.Right.Creature);
            Assert(s.Right.StolenCards.Count == 0, "Right arm must release card upon defeat in Phase 2.");
            Assert(p.PlayerCombatState!.Hand!.Cards.Contains(card), "Card must return to Hand in Phase 2.");
            DeactivateSyntheticCombat();
        }

        // 10. Empty deck safety (0 cards anywhere)
        {
            var s = Scenario();
            s.Left.Plan(ThingsCaveGodHand.Action.Sweep);
            await s.Left.PerformMove();
            Assert(s.Left.StolenCards.Count == 0, "Sweep with 0 cards must complete cleanly with 0 stolen cards.");
            DeactivateSyntheticCombat();
        }

        GD.Print("PASS sweep card theft: priority hierarchy, left/right separation, hand return on arm downed, multiplayer, full-hand overflow, hand fallback, Phase 2 retention & empty deck safety");
    }

    private async Task VerifyHandLayoutAndZIndex()
    {
        // 1. Encounter slot markers
        var encounterScene = GD.Load<PackedScene>("res://scenes/encounters/things_cave_god_boss_encounter.tscn");
        Assert(encounterScene != null, "Could not load cave_god_boss_encounter.tscn");
        var encounter = encounterScene!.Instantiate<Control>();
        var bodyMarker = encounter.GetNode<Marker2D>("body");
        var leftMarker = encounter.GetNode<Marker2D>("left_hand");
        var rightMarker = encounter.GetNode<Marker2D>("right_hand");
        Assert(bodyMarker.Position == new Vector2(971, -6), $"body expected (971, -6) but got {bodyMarker.Position}");
        Assert(leftMarker.Position == new Vector2(540, 490), $"left_hand expected (540, 490) but got {leftMarker.Position}");
        Assert(rightMarker.Position == new Vector2(1380, 490), $"right_hand expected (1380, 490) but got {rightMarker.Position}");
        encounter.QueueFree();

        // 2. Body and Left/Right Hand visual scenes and UI elevation
        foreach (string scenePath in new[]
        {
            "res://scenes/creature_visuals/things_cave_god.tscn",
            "res://scenes/creature_visuals/things_cave_god_left_hand.tscn",
            "res://scenes/creature_visuals/things_cave_god_right_hand.tscn",
        })
        {
            var visualScene = GD.Load<PackedScene>(scenePath);
            Assert(visualScene != null, $"Could not load {scenePath}");

            var dummyParent = new Control();
            var dummyHealthBar = new Control { Name = "HealthBar" };
            var dummyIntents = new Control { Name = "Intents" };
            dummyParent.AddChild(dummyHealthBar);
            dummyParent.AddChild(dummyIntents);

            var visuals = visualScene!.Instantiate<Node2D>();
            dummyParent.AddChild(visuals);
            AddChild(dummyParent);

            // Wait frame for Ready and deferred elevation
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(dummyParent.ZIndex == 0, $"Creature node ZIndex expected 0, got {dummyParent.ZIndex}");
            Assert(!dummyParent.ZAsRelative, "Creature node ZAsRelative must be false");
            Assert(dummyHealthBar.ZIndex == 0, $"HealthBar ZIndex expected 0, got {dummyHealthBar.ZIndex}");
            Assert(!dummyHealthBar.ZAsRelative, "HealthBar ZAsRelative must be false");
            Assert(dummyIntents.ZIndex == 0, $"Intents ZIndex expected 0, got {dummyIntents.ZIndex}");
            Assert(!dummyIntents.ZAsRelative, "Intents ZAsRelative must be false");

            var bounds = visuals.GetNode<Control>("Bounds");
            var centerPos = visuals.GetNode<Marker2D>("CenterPos");
            var intentPos = visuals.GetNode<Marker2D>("IntentPos");
            if (scenePath.EndsWith("things_cave_god.tscn"))
            {
                Assert(bounds.OffsetLeft == -405f && bounds.OffsetRight == 405f, $"Body Bounds X expected (-405, 405) but got ({bounds.OffsetLeft}, {bounds.OffsetRight})");
                Assert(bounds.OffsetTop == -114f && bounds.OffsetBottom == 606f, $"Body Bounds Y expected (-114, 606) but got ({bounds.OffsetTop}, {bounds.OffsetBottom})");
                Assert(centerPos.Position == new Vector2(0, 306), $"Body CenterPos expected (0, 306) but got {centerPos.Position}");
                Assert(intentPos.Position == new Vector2(0, 128), $"Body IntentPos expected (0, 128) but got {intentPos.Position}");
            }
            else
            {
                Assert(bounds.OffsetLeft == -130f && bounds.OffsetRight == 130f, $"Bounds X expected (-130, 130) but got ({bounds.OffsetLeft}, {bounds.OffsetRight})");
                Assert(bounds.OffsetTop == -180f && bounds.OffsetBottom == 10f, $"Bounds Y expected (-180, 10) but got ({bounds.OffsetTop}, {bounds.OffsetBottom})");
                Assert(centerPos.Position == new Vector2(0, -90), $"CenterPos expected (0, -90) but got {centerPos.Position}");
                Assert(intentPos.Position == new Vector2(0, -210), $"IntentPos expected (0, -210) but got {intentPos.Position}");
            }

            dummyParent.QueueFree();
        }

        // 3. Verify ElevateHandsUi and ResetHandsUi behavior on combat room creatures (both Body and Hands)
        var dummyRoom = new NCombatRoom();
        var bodyCreature = new Creature(ModelDb.Monster<ThingsCaveGodBody>().ToMutable(), CombatSide.Enemy, "body");
        var enemyCreature = new Creature(ModelDb.Monster<ThingsCaveGodLeftHand>().ToMutable(), CombatSide.Enemy, "left_hand");
        var bodyCreatureNode = new NCreature();
        var leftCreatureNode = new NCreature();
        typeof(NCreature).GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)!.SetValue(bodyCreatureNode, bodyCreature);
        typeof(NCreature).GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)!.SetValue(leftCreatureNode, enemyCreature);
        var nodesList = (List<NCreature>)typeof(NCombatRoom).GetField("_creatureNodes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dummyRoom)!;
        nodesList.Add(bodyCreatureNode);
        nodesList.Add(leftCreatureNode);

        NCaveGodBossBackground.ElevateHandsUi(dummyRoom);
        Assert(bodyCreatureNode.ZIndex == 0 && !bodyCreatureNode.ZAsRelative, "ElevateHandsUi must normalize Body UI node to Z=0 non-relative.");
        Assert(leftCreatureNode.ZIndex == 0, $"ElevateHandsUi expected ZIndex 0, got {leftCreatureNode.ZIndex}");
        Assert(!leftCreatureNode.ZAsRelative, "ElevateHandsUi expected ZAsRelative false");

        NCaveGodBossBackground.ResetHandsUi(dummyRoom);
        Assert(bodyCreatureNode.ZIndex == 0 && !bodyCreatureNode.ZAsRelative, "ResetHandsUi must preserve Body Z=0 non-relative.");
        Assert(leftCreatureNode.ZIndex == 0, $"ResetHandsUi expected ZIndex 0, got {leftCreatureNode.ZIndex}");
        Assert(!leftCreatureNode.ZAsRelative, "ResetHandsUi expected ZAsRelative false");

        bodyCreatureNode.Free();
        leftCreatureNode.Free();
        dummyRoom.Free();

        GD.Print("PASS body+hand layout and ZIndex: slot markers, bounds, center/intent offsets, native UI plane Z=0, and ResetHandsUi robustness");
    }

    private async Task VerifyRenderedResources()
    {
        TestMode.TurnOnInternal(); // Render normally while suppressing unavailable FMOD/game services.
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        // PCKs are already mounted in Run(); only load if not yet mounted.
        // Calling LoadResourcePack twice on the same PCK corrupts Godot's pack filesystem table.
        var extension = GDExtensionManager.LoadExtension(Path.Combine(root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, $"Spine load failed: {extension}");
        EnsureScriptsLookedUp();
        try
        {
            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "eng";
            LocManager.Initialize();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"LocManager.Initialize failed: {ex}");
        }
        try { NCard.InitPool(); } catch { }
        GetTree().Root.Size = new Vector2I(1920, 1080);
        var scene = GD.Load<PackedScene>("res://scenes/backgrounds/things_cave_god_boss_encounter/things_cave_god_boss_encounter_background.tscn");
        var bg = scene.Instantiate<Control>();
        bg.Position = new Vector2(983, 540);
        bg.Scale = Vector2.One * 0.75f; // Encounter's native camera scale.
        bg.GetNode("Layer_00").AddChild(GD.Load<PackedScene>("res://scenes/backgrounds/things_cave_god_boss_encounter/layers/things_cave_god_boss_encounter_bg_00_a.tscn").Instantiate());
        bg.GetNode("Foreground").AddChild(GD.Load<PackedScene>("res://scenes/backgrounds/things_cave_god_boss_encounter/layers/things_cave_god_boss_encounter_fg_a.tscn").Instantiate());
        AddChild(bg);
        var controller = bg.GetNode<STS2_Things.Visuals.NCaveGodBossBackground>("CaveGod");
        var body = bg.GetNode<Node2D>("CaveGod/CaveGodBody");
        var arms = bg.GetNode<Node2D>("CaveGodArms");
        for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string output = Path.Combine(root, "build/cavegod-refactor-20260919/visuals");
        Directory.CreateDirectory(output);
        async Task Capture(string name)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var frame = GetViewport().GetTexture().GetImage();
            Assert(frame.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "Could not save rendered frame.");
        }
        void Freeze(string animation, float time)
        {
            foreach (var node in new[] { body, arms })
            {
                var state = node.Call("get_animation_state").AsGodotObject();
                state.Call("clear_track", 0);
                var track = state.Call("set_animation", animation, false, 0).AsGodotObject();
                track.Call("set_track_time", time);
                track.Call("set_time_scale", 0f);
                track.Call("set_mix_duration", 0f);
            }
        }
        foreach (string animation in new[] { "idle_front", "idle_front_angry", "weak_idle", "weak_idle_angry", "grab_slam", "grab_slam_angry" })
        {
            var data = body.Get("skeleton_data_res").AsGodotObject();
            Assert(data.Call("find_animation", animation).AsGodotObject() != null, $"Missing packaged animation: {animation}");
        }
        controller.SetAngry(false);
        Freeze("idle_front", 0.5f); await Capture("01-blue-idle");
        var redBgNode = bg.GetNodeOrNull<TextureRect>("Layer_00/A/RedBg");
        if (redBgNode != null) redBgNode.Modulate = new Color(1f, 1f, 1f, 0.5f);
        await Capture("01b-phase-transition-half");
        controller.SetAngry(true);
        Freeze("idle_front_angry", 0.5f); await Capture("02-red-idle");

        var testCardLeft = (CardModel)ModelDb.Card<Inflame>().ToMutable();
        var testCardRight = (CardModel)ModelDb.Card<DemonForm>().ToMutable();
        controller.AttachStolenCard(true, testCardLeft);
        await Capture("idle-left-holding-card");
        controller.ClearStolenCard(true);

        controller.AttachStolenCard(false, testCardRight);
        await Capture("idle-right-holding-card");
        controller.ClearStolenCard(false);

        controller.AttachStolenCard(true, testCardLeft);
        controller.AttachStolenCard(false, testCardRight);
        await Capture("idle-both-holding-cards");
        controller.ClearAllStolenCards();
        controller.StartAttackAnim("front_sweep", isFlipped: false);
        Freeze("front_sweep_angry", 0.70f); await Capture("sweep-left-070");
        Freeze("front_sweep_angry", 1.00f); await Capture("sweep-left-100");
        Freeze("front_sweep_angry", 1.30f);
        testCardLeft = (CardModel)ModelDb.Card<Inflame>().ToMutable();
        controller.AttachStolenCard(true, testCardLeft);
        await Capture("sweep-left-130-holding-card");
        controller.ClearStolenCard(true);
        await Capture("sweep-left-130");

        // Verify that passing isFlipped: true routes cleanly to front_sweep_right_angry without negative scaling
        controller.StartAttackAnim("front_sweep", isFlipped: true);
        Assert(arms.Scale.X > 0, "Arm rig must never use negative X scale.");
        Freeze("front_sweep_right_angry", 0.70f); await Capture("sweep-right-070");
        Freeze("front_sweep_right_angry", 1.00f); await Capture("sweep-right-100");
        Freeze("front_sweep_right_angry", 1.30f); await Capture("sweep-right-130");

        controller.StartAttackAnim("front_sweep_right", isFlipped: false);
        Freeze("front_sweep_right_angry", 0.70f); await Capture("sweep-right-new-070");
        Freeze("front_sweep_right_angry", 1.00f); await Capture("sweep-right-new-100");
        Freeze("front_sweep_right_angry", 1.30f);
        testCardRight = (CardModel)ModelDb.Card<DemonForm>().ToMutable();
        controller.AttachStolenCard(false, testCardRight);
        await Capture("sweep-right-new-130-holding-card");
        controller.ClearStolenCard(false);
        await Capture("sweep-right-new-130");

        controller.StartAttackAnim("grab_player_right", isFlipped: false);
        controller.StartGrabTracking(Array.Empty<Creature>());
        Assert(arms.Scale.X > 0, "Arm rig must never use negative X scale during grab tracking.");
        controller.HoldGrabAnim();
        Freeze("grab_player_right_angry", 2.60f); await Capture("grab-right-new-hold");

        controller.ResumeSlamAnim();
        var animState = arms.Call("get_animation_state").AsGodotObject();
        var curTrack = animState.Call("get_current", 0).AsGodotObject();
        var anim = curTrack.Call("get_animation").AsGodotObject();
        string slamAnimName = anim.Call("get_name").AsString();
        Assert(slamAnimName == "grab_slam_right_angry", $"Expected grab_slam_right_angry after grab_player_right, but got {slamAnimName}");

        Freeze("grab_slam_right_angry", 0.45f); await Capture("slam-right-new-windup");
        Freeze("grab_slam_right_angry", 0.75f); await Capture("slam-right-new-impact");

        controller.ResetArmScale();
        Freeze("rightpunch", 1.20f); await Capture("rightpunch-120");
        Freeze("weak_attack_right_angry", 1.00f); await Capture("weak-attack-right-100");
        Freeze("weak_idle_right_angry", 0.50f); await Capture("weak-idle-right-050");
        Freeze("weak_enter_right_angry", 0.85f); await Capture("weak-enter-right-085");
        Freeze("weak_recover_right_angry", 1.00f); await Capture("weak-recover-right-100");

        controller.StartAttackAnim("grab_player", isFlipped: true);
        controller.StartGrabTracking(Array.Empty<Creature>(), isFlipped: true);
        Assert(arms.Scale.X > 0, "Arm rig must never use negative X scale during grab tracking.");
        controller.HoldGrabAnim();
        await Capture("grab-right-hold");
        controller.ResumeSlamAnim();
        Freeze("grab_slam_right_angry", 0.45f); await Capture("slam-right-windup");
        Freeze("grab_slam_right_angry", 0.75f); await Capture("slam-right-impact");

        controller.StartAttackAnim("grab_player", isFlipped: false);
        controller.HoldGrabAnim();
        await Capture("03-grab-hold");
        controller.ResumeSlamAnim();
        Freeze("grab_slam_angry", 0.45f); await Capture("04-slam-windup");
        Freeze("grab_slam_angry", 0.75f); await Capture("05-slam-impact");
        Assert(arms.GetIndex() > bg.GetNode("Foreground").GetIndex(), "Arms must draw in front of the platform during attacks.");
        controller.PlayBodyDeathAnim();
        Freeze("hide_angry", 3.0f);
        Assert(arms.GetIndex() < bg.GetNode("Foreground").GetIndex(), "Retreating arms must be behind the platform.");
        await Capture("06-death");
        GD.Print("CaveGod packaged background/render probe: PASS");
    }

    private static void InitializeModelDb()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("System.IO.Hashing");

        Type[] modTypes = ImplementationAssembly.GetTypes();
        Type[] modelTypes = AbstractModelSubtypes.All
            .Concat(modTypes.Where(type =>
                !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type)))
            .Distinct()
            .ToArray();
        typeof(ReflectionHelper).GetField(
                "_modTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, modTypes);
        RegisterSyntheticMod(ImplementationAssembly);

        PropertyInfo? managerState = typeof(ModManager).GetProperty(
            "State", BindingFlags.Public | BindingFlags.Static);
        if (managerState != null)
        {
            managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
        }

        Type? assemblyInfo = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Modding.AssemblyInfo");
        assemblyInfo?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
        typeof(ModelDb).GetMethod("ResetForTest", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);

        MethodInfo init = typeof(ModelDb).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [modelTypes]);

        Type serializationCache = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache",
            throwOnError: true)!;
        serializationCache.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);

        // The normal loader calls STS2_ThingsInit before ModelDb setup. The
        // isolated probe intentionally builds its own model database, so apply
        // the narrowly scoped Ravenous adapter here as well.
        // CaveGod uses native model hooks; unrelated registration patches are not needed here.
    }


    private static void RegisterSyntheticMod(Assembly implementationAssembly)
    {
        Assembly gameAssembly = typeof(ModManager).Assembly;
        Type modType = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Modding.Mod", throwOnError: true)!;
        Type manifestType = gameAssembly.GetType(
            "MegaCrit.Sts2.Core.Modding.ModManifest", throwOnError: true)!;
        object mod = Activator.CreateInstance(modType)!;
        object manifest = Activator.CreateInstance(manifestType)!;
        manifestType.GetField("id")!.SetValue(manifest, "STS2_Things");
        manifestType.GetField("name")?.SetValue(manifest, "STS2_Things Gravetide Probe");
        manifestType.GetField("affectsGameplay")?.SetValue(manifest, true);
        modType.GetField("path")!.SetValue(mod, "probe://STS2_Things");
        modType.GetField("manifest")!.SetValue(mod, manifest);
        FieldInfo stateField = modType.GetField("state")!;
        stateField.SetValue(mod, Enum.Parse(stateField.FieldType, "Loaded"));

        if (modType.GetField("assemblies")?.GetValue(mod) is IList assemblies)
        {
            assemblies.Add(implementationAssembly);
        }
        else
        {
            modType.GetField("assembly")!.SetValue(mod, implementationAssembly);
        }

        IList mods = (IList)typeof(ModManager)
            .GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        mods.Clear();
        mods.Add(mod);
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


    private static void EnsureRuntimeDependency(string assemblyName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
        {
            return;
        }

        string path = FindRuntimeDependencyPath(assemblyName)
            ?? throw new FileNotFoundException(
                $"Could not locate probe runtime dependency {assemblyName}.dll.");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }


    private static Assembly? ResolveRuntimeDependency(
        AssemblyLoadContext context,
        AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }
        string? path = FindRuntimeDependencyPath(assemblyName.Name);
        return path == null ? null : context.LoadFromAssemblyPath(path);
    }


    private static string? FindRuntimeDependencyPath(string assemblyName)
    {
        string configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? assemblyDirectory = Path.GetDirectoryName(
            typeof(CaveGodProbeNode).Assembly.Location);
        string[] candidates =
        [
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", configuration,
                $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug",
                $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release",
                $"{assemblyName}.dll"),
            Path.Combine(assemblyDirectory ?? string.Empty, $"{assemblyName}.dll")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }


    private static bool _scriptsLookedUp;
    private static void EnsureScriptsLookedUp()
    {
        if (_scriptsLookedUp) return;
        _scriptsLookedUp = true;
        try { Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly); } catch { }
        try { Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(ImplementationAssembly); } catch { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private async Task RenderOfficialRooms()
    {
        TestMode.TurnOnInternal();
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
        Assert(ProjectSettings.LoadResourcePack("D:/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck"), "Game PCK mount failed.");
        EnsureScriptsLookedUp();
        
        GetTree().Root.Size = new Vector2I(1920, 1080);
        string output = @"d:\Things\CaveGod_STS2_Style_ARCHIVE_20260808\official_rendered_rooms";
        Directory.CreateDirectory(output);

        async Task Capture(string filename)
        {
            for (int i = 0; i < 10; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var frame = GetViewport().GetTexture().GetImage();
            Assert(frame.SavePng(Path.Combine(output, filename)) == Error.Ok, $"Could not save {filename}");
            GD.Print($"Rendered and saved: {filename}");
        }

        // 1. Waterfall Giant Boss Room
        try
        {
            var sc = GD.Load<PackedScene>("res://scenes/backgrounds/waterfall_giant_boss/waterfall_giant_boss_background.tscn");
            if (sc != null)
            {
                var bg = sc.Instantiate<Control>();
                bg.Position = new Vector2(983, 540);
                bg.Scale = Vector2.One * 0.75f;
                var bgLayerSc = GD.Load<PackedScene>("res://scenes/backgrounds/waterfall_giant_boss/layers/waterfall_giant_boss_bg_00_a.tscn");
                if (bgLayerSc != null) bg.GetNodeOrNull("Layer_00")?.AddChild(bgLayerSc.Instantiate());
                var fgLayerSc = GD.Load<PackedScene>("res://scenes/backgrounds/waterfall_giant_boss/layers/waterfall_giant_boss_fg_a.tscn");
                if (fgLayerSc != null) bg.GetNodeOrNull("Foreground")?.AddChild(fgLayerSc.Instantiate());
                AddChild(bg);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Capture("01-waterfall_giant_boss_room.png");
                RemoveChild(bg);
                bg.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"Failed waterfall giant: {ex.Message}"); }

        // 2. Queen Boss Room
        try
        {
            var sc = GD.Load<PackedScene>("res://scenes/backgrounds/queen_boss/queen_boss_background.tscn");
            if (sc != null)
            {
                var bg = sc.Instantiate<Control>();
                bg.Position = new Vector2(983, 540);
                bg.Scale = Vector2.One * 0.75f;
                for (int l = 0; l <= 3; l++)
                {
                    var lSc = GD.Load<PackedScene>($"res://scenes/backgrounds/queen_boss/layers/queen_boss_bg_{l:D2}_a.tscn");
                    if (lSc != null) bg.GetNodeOrNull($"Layer_{l:D2}")?.AddChild(lSc.Instantiate());
                }
                var fgSc = GD.Load<PackedScene>("res://scenes/backgrounds/queen_boss/layers/queen_fg_a.tscn");
                if (fgSc != null) bg.GetNodeOrNull("Foreground")?.AddChild(fgSc.Instantiate());
                AddChild(bg);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Capture("02-queen_boss_room.png");
                RemoveChild(bg);
                bg.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"Failed queen: {ex.Message}"); }

        // 3. Ceremonial Beast Boss Room
        try
        {
            var sc = GD.Load<PackedScene>("res://scenes/backgrounds/ceremonial_beast_boss/ceremonial_beast_boss_background.tscn");
            if (sc != null)
            {
                var bg = sc.Instantiate<Control>();
                bg.Position = new Vector2(983, 540);
                bg.Scale = Vector2.One * 0.75f;
                for (int l = 0; l <= 4; l++)
                {
                    string suffix = l == 4 ? "c" : "a";
                    var lSc = GD.Load<PackedScene>($"res://scenes/backgrounds/ceremonial_beast_boss/layers/ceremonial_beast_boss_bg_{l:D2}_{suffix}.tscn");
                    if (lSc != null) bg.GetNodeOrNull($"Layer_{l:D2}")?.AddChild(lSc.Instantiate());
                }
                var fgSc = GD.Load<PackedScene>("res://scenes/backgrounds/ceremonial_beast_boss/layers/ceremonial_beast_boss_fg_a.tscn");
                if (fgSc != null) bg.GetNodeOrNull("Foreground")?.AddChild(fgSc.Instantiate());
                AddChild(bg);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Capture("03-ceremonial_beast_boss_room.png");
                RemoveChild(bg);
                bg.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"Failed ceremonial beast: {ex.Message}"); }

        // 4. Kaiser Crab Boss Room
        try
        {
            var sc = GD.Load<PackedScene>("res://scenes/backgrounds/kaiser_crab_boss/kaiser_crab_boss_background.tscn");
            if (sc != null)
            {
                var bg = sc.Instantiate<Control>();
                bg.Position = new Vector2(983, 540);
                bg.Scale = Vector2.One * 0.75f;
                for (int l = 0; l <= 3; l++)
                {
                    var lSc = GD.Load<PackedScene>($"res://scenes/backgrounds/kaiser_crab_boss/layers/kaiser_crab_boss_bg_{l:D2}_a.tscn");
                    if (lSc != null) bg.GetNodeOrNull($"Layer_{l:D2}")?.AddChild(lSc.Instantiate());
                }
                var fgSc = GD.Load<PackedScene>("res://scenes/backgrounds/kaiser_crab_boss/layers/kaiser_crab_boss_fg_a.tscn");
                if (fgSc != null) bg.GetNodeOrNull("Foreground")?.AddChild(fgSc.Instantiate());
                AddChild(bg);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Capture("04-kaiser_crab_boss_room.png");
                RemoveChild(bg);
                bg.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"Failed kaiser crab: {ex.Message}"); }

        // 5. The Insatiable Boss Room
        try
        {
            var sc = GD.Load<PackedScene>("res://scenes/backgrounds/the_insatiable_boss/the_insatiable_boss_background.tscn");
            if (sc != null)
            {
                var bg = sc.Instantiate<Control>();
                bg.Position = new Vector2(983, 540);
                bg.Scale = Vector2.One * 0.75f;
                for (int l = 0; l <= 4; l++)
                {
                    var lSc = GD.Load<PackedScene>($"res://scenes/backgrounds/the_insatiable_boss/layers/the_insatiable_boss_bg_{l:D2}_a.tscn");
                    if (lSc != null) bg.GetNodeOrNull($"Layer_{l:D2}")?.AddChild(lSc.Instantiate());
                }
                var fgSc = GD.Load<PackedScene>("res://scenes/backgrounds/the_insatiable_boss/layers/the_insatiable_boss_fg_a.tscn");
                if (fgSc != null) bg.GetNodeOrNull("Foreground")?.AddChild(fgSc.Instantiate());
                AddChild(bg);
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Capture("05-the_insatiable_boss_room.png");
                RemoveChild(bg);
                bg.QueueFree();
            }
        }
        catch (Exception ex) { GD.PrintErr($"Failed the insatiable: {ex.Message}"); }
    }

    private static async Task VerifyAgingPowerAndStoneArmor()
    {
        var s = Scenario(count: 2);
        var leftAging = s.Left.Creature.GetPower<ThingsCaveGodAgingPower>();
        var rightAging = s.Right.Creature.GetPower<ThingsCaveGodAgingPower>();
        Assert(leftAging != null && leftAging.StackType == MegaCrit.Sts2.Core.Entities.Powers.PowerStackType.Single, "Left arm must have Single-stack Aging power.");
        Assert(rightAging != null && rightAging.StackType == MegaCrit.Sts2.Core.Entities.Powers.PowerStackType.Single, "Right arm must have Single-stack Aging power.");

        var p1 = s.State.Players[0];
        var p2 = s.State.Players[1];
        int p1CardsBefore = p1.PlayerCombatState!.Hand.Cards.Count;
        int p2CardsBefore = p2.PlayerCombatState!.Hand.Cards.Count;

        static void AssertLatestStoneArmor(Player player, int expectedCount, string stage)
        {
            Assert(player.PlayerCombatState!.Hand.Cards.Count == expectedCount, $"{stage}: hand count mismatch for {player.Character.Id}.");
            var card = player.PlayerCombatState.Hand.Cards.Last();
            Assert(card.Id == ModelDb.Card<StoneArmor>().Id, $"{stage}: added card must be Stone Armor.");
            Assert(card.EnergyCost.GetResolved() == 0, $"{stage}: Stone Armor cost must be 0.");
        }

        // 1st knockdown: immediately grants 0-cost Stone Armor to every living player!
        await Hit(s, s.Left.Creature, 10000m);
        Assert(s.Left.IsDown && s.Body.IsWeakPhase, "Left arm must be downed and core exposed.");
        AssertLatestStoneArmor(p1, p1CardsBefore + 1, "First knockdown P1");
        AssertLatestStoneArmor(p2, p2CardsBefore + 1, "First knockdown P2");

        // 2nd knockdown (surviving arm on Exposure Turn 2): ALSO grants 0-cost Stone Armor!
        await Act(s);
        await Hit(s, s.Right.Creature, 10000m);
        AssertLatestStoneArmor(p1, p1CardsBefore + 2, "Second knockdown P1");
        AssertLatestStoneArmor(p2, p2CardsBefore + 2, "Second knockdown P2");

        // 3rd knockdown (after recovery on Exposure Turn 3): ALSO grants 0-cost Stone Armor!
        await Act(s);
        Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId, "Core must advertise recovery on Exposure Turn 3.");
        await Act(s);
        Assert(!s.Body.IsWeakPhase, "Core must recover after Exposure Turn 3.");
        await Hit(s, s.Left.Creature, 10000m);
        AssertLatestStoneArmor(p1, p1CardsBefore + 3, "Third knockdown P1");
        AssertLatestStoneArmor(p2, p2CardsBefore + 3, "Third knockdown P2");

        DeactivateSyntheticCombat();
        GD.Print("PASS Aging power grants 0-cost Stone Armor to every living player on EVERY arm knockdown");
    }

    private static async Task VerifyHalfHpEnragePaths()
    {
        // Path A: Upright massive overkill hit clamps strictly at 50% threshold, switches to ∞ + ? immediately,
        // locks 0 HP loss for the rest of the turn, and transforms on THAT SAME enemy turn back to exact 50% HP!
        foreach (int ascension in new[] { 0, 10 })
        {
            var s = Scenario(ascension: ascension);
            int expectedThreshold = ascension == 0 ? 113 : 122;
            Assert(s.Body.EnrageHpThreshold == expectedThreshold,
                $"Expected EnrageHpThreshold={expectedThreshold} at A{ascension}, got {s.Body.EnrageHpThreshold}.");

            await Hit(s, s.Right.Creature, 20m);
            await Hit(s, s.Body.Creature, 5000m);

            Assert(s.Body.Creature.IsAlive && !s.Body.IsDefeated, "5000-damage overkill in Phase 1 must never kill Body.");
            Assert(s.Body.IsEnrageLocked && s.Body.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers,
                "Half-HP hit must immediately switch Body to HpDisplay.InfiniteWithoutNumbers.");
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.EnrageId &&
                   s.Body.NextMove.Intents.All(i => i is UnknownIntent),
                $"Expected immediate EnrageId (?) at A{ascension}, got {s.Body.NextMove.Id}.");
            Assert(s.Left.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Rest) &&
                   s.Right.NextMove.Id == ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Rest),
                "Both arms must rest while Body prepares ENRAGE_ROAR.");

            // Further hits on the same turn must deal 0 HP loss and keep InfiniteWithoutNumbers!
            int lockedHpBefore = s.Body.Creature.CurrentHp;
            await Hit(s, s.Body.Creature, 5000m);
            Assert(s.Body.Creature.CurrentHp == lockedHpBefore && s.Body.IsEnrageLocked &&
                   s.Body.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers,
                "Subsequent hits during the ∞ lock turn must deal 0 HP loss.");

            // Same-turn enemy phase executes ENRAGE_ROAR -> Phase 2 Crush + restores exact 50% HP!
            await Act(s);
            int expectedStr = ascension == 0 ? 2 : 3;
            Assert(s.Body.Phase == 2 && !s.Body.IsEnrageLocked && !s.Body.ShouldTriggerEnrage,
                "Same-turn ENRAGE_ROAR must enter Phase 2 and unlock HP.");
            Assert(s.Body.Creature.HpDisplay == HpDisplay.Normal && s.Body.Creature.CurrentHp == expectedThreshold,
                $"After Phase 2 transition, Body HP must restore to exact 50% ({expectedThreshold}), got {s.Body.Creature.CurrentHp} ({s.Body.Creature.HpDisplay}).");
            Assert(s.Right.Creature.CurrentHp == s.Right.Creature.MaxHp, "ENRAGE_ROAR must heal chipped arms to full.");
            Assert(s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(), "ENRAGE_ROAR must ensure Granite Bulwark is active.");
            foreach (var c in new[] { s.Body.Creature, s.Left.Creature, s.Right.Creature })
                Assert(c.GetPowerAmount<StrengthPower>() == expectedStr, $"ENRAGE_ROAR must grant +{expectedStr} Strength.");
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.CrushId, "ENRAGE_ROAR must lead into DOUBLE_FIST_CRUSH.");
            DeactivateSyntheticCombat();
        }

        // Path B: Mid-grab half-HP hit immediately releases captive + locks ∞ + ?, and breaking an arm while locked upgrades to EnrageRecoverId
        {
            var s = Scenario();
            await StartCapture(s, phase2: false);
            Assert(s.Body.HasGrabbingHand, "Expected active grab before mid-grab half-HP hit.");

            await Hit(s, s.Body.Creature, 5000m);
            Assert(!s.Body.HasGrabbingHand && !s.State.Enemies.Any(c => c.Monster is ThingsCaveGodCaptiveClaw && c.IsAlive),
                "Half-HP lock must immediately release captive players.");
            Assert(s.Body.IsEnrageLocked && s.Body.NextMove.Id == ThingsCaveGodBody.EnrageId,
                "Mid-grab half-HP lock must set EnrageId.");

            await Hit(s, s.Left.Creature);
            Assert(s.Body.IsWeakPhase && s.Body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId &&
                   s.Body.NextMove.Intents.All(i => i is UnknownIntent),
                "Breaking an arm while enrage-locked must switch Body to EnrageRecoverId (?).");

            await Act(s);
            Assert(s.Body.Phase == 2 && !s.Body.IsWeakPhase && !s.Body.IsEnrageLocked &&
                   s.Body.Creature.HpDisplay == HpDisplay.Normal &&
                   s.Body.Creature.CurrentHp == 113 &&
                   s.Body.NextMove.Id == ThingsCaveGodBody.CrushId,
                "EnrageRecoverId must awaken on that same turn into Phase 2 at 113/226 HP.");
            DeactivateSyntheticCombat();
        }

        // Path C: Mid-exposure half-HP hit on Exposure Turn 3 immediately upgrades RecoverId -> EnrageRecoverId + ∞
        {
            var s = Scenario();
            await Hit(s, s.Left.Creature);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed1Id, "Exposure Turn 1 must start on WEAK_PRONE_1.");
            await Act(s);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.Exposed2Id, "Above-50% core on Exposure Turn 2 must stay prone (WEAK_PRONE_2).");
            await Act(s);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.RecoverId, "Above-50% core on Exposure Turn 3 must prepare WEAK_UNKNOWN_3.");

            int damageToCrossHalf = s.Body.Creature.CurrentHp - s.Body.EnrageHpThreshold;
            await Hit(s, s.Body.Creature, damageToCrossHalf);
            Assert(s.Body.IsEnrageLocked &&
                   s.Body.Creature.HpDisplay == HpDisplay.InfiniteWithoutNumbers &&
                   s.Body.NextMove.Id == ThingsCaveGodBody.EnrageRecoverId,
                "Crossing 50% HP while RecoverId is pending must immediately lock ∞ and swap intent to ENRAGE_RECOVER.");

            await Act(s);
            Assert(s.Body.Phase == 2 && !s.Body.IsWeakPhase && !s.Body.IsEnrageLocked &&
                   s.Body.Creature.HpDisplay == HpDisplay.Normal &&
                   s.Body.Creature.CurrentHp == 113 &&
                   s.Body.NextMove.Id == ThingsCaveGodBody.CrushId,
                "ENRAGE_RECOVER must awaken into Phase 2 DOUBLE_FIST_CRUSH at 113/226 HP.");
            Assert(s.Left.Creature.CurrentHp == s.Left.Creature.MaxHp && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
                "ENRAGE_RECOVER must restore downed arm and Granite Bulwark.");
            DeactivateSyntheticCombat();
        }

        // Path D: DoomKill on Phase 1 Body both before and while already enrage-locked must never recurse in KillWithoutCheckingWinCondition
        {
            var s = Scenario();
            await PowerCmd.Apply<DoomPower>(Choice, s.Body.Creature, s.Body.Creature.MaxHp + 50m, s.State.Players[0].Creature, null);
            await DoomPower.DoomKill([s.Body.Creature]);
            Assert(s.Body.Creature.IsAlive && s.Body.IsEnrageLocked && s.Body.Creature.CurrentHp == s.Body.Creature.MaxHp,
                "Phase 1 DoomKill must enter enrage lock alive at MaxHp without recursion.");

            // Trigger DoomKill a second time while already enrage-locked (previously returned early with CurrentHp == 0 -> recursion crash!)
            await DoomPower.DoomKill([s.Body.Creature]);
            Assert(s.Body.Creature.IsAlive && s.Body.IsEnrageLocked && s.Body.Creature.CurrentHp == s.Body.Creature.MaxHp,
                "Repeated DoomKill while already enrage-locked must keep Body alive at MaxHp without recursion.");

            await Act(s);
            Assert(s.Body.Phase == 2 && !s.Body.IsEnrageLocked && s.Body.Creature.CurrentHp == 113,
                "Enrage turn after Doom must awaken cleanly into Phase 2 at 113/226 HP.");
            DeactivateSyntheticCombat();
        }

        GD.Print("PASS half-HP Phase 2 transition: 50% clamp, WaterfallGiant ∞ bar + ?, 0-damage lock, mid-grab release, arm-break upgrade, Doom lock safety & 113/122 restoration");
    }
}
