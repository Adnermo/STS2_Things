using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Platform;
#if STS2_V113_1
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Blitz;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Dread;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Audio;
using STS2_Things.Enchantments;
using STS2_Things.Encounters;
using STS2_Things.Map;
using STS2_Things.Modifiers;
using STS2_Things.Monsters;
using STS2_Things.Platform.Blitz;
using STS2_Things.Platform.Dread;
using STS2_Things.Powers;
#endif

public partial class DepthsProbeNode
{
    private readonly record struct RunManagerContextSnapshot(
        object? State,
        object? AscensionManager,
        object? NetService);

    private static RunManagerContextSnapshot CaptureRunManagerContext()
    {
        return new RunManagerContextSnapshot(
            typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(RunManager.Instance),
            typeof(RunManager).GetProperty("AscensionManager")!.GetValue(RunManager.Instance),
            typeof(RunManager).GetProperty("NetService")!.GetValue(RunManager.Instance));
    }

    private static void RestoreRunManagerContext(RunManagerContextSnapshot snapshot)
    {
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, snapshot.State);
        typeof(RunManager).GetProperty("AscensionManager")!
            .SetValue(RunManager.Instance, snapshot.AscensionManager);
        typeof(RunManager).GetProperty("NetService")!
            .SetValue(RunManager.Instance, snapshot.NetService);
    }

    private async Task VerifyBlitzAndDreadModes()
    {
        RunManagerContextSnapshot previousRunManager = CaptureRunManagerContext();
        DeactivateSyntheticCombat();
        ResetSettings();
        try
        {
#if !STS2_V113_1
            RunState stdA0 = CreateRun("modes-v111-std-a0", players: 1, ascension: 0);
            RunState stdA10 = CreateRun("modes-v111-std-a10", players: 1, ascension: 10);
            var customParty = new List<Player> { Player.CreateForNewRun<Ironclad>(UnlockState.all, 1) };
            RunState customRun = NativeRun(
                customParty,
                [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<ThingsDepths>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
                GameMode.Custom,
                0,
                "modes-v111-custom");

            typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(RunManager.Instance, stdA10);
            typeof(RunManager).GetProperty("AscensionManager")!
                .SetValue(RunManager.Instance, NativeAscension(10));
            typeof(RunManager).GetProperty("NetService")!
                .SetValue(RunManager.Instance, new NetSingleplayerGameService());

            Assert(!Sts2VersionSeam.IsDread(), "v111.1 Sts2VersionSeam.IsDread() must return false.");
            Assert(!Sts2VersionSeam.IsBlitz(stdA0) && !Sts2VersionSeam.IsBlitz(null),
                "v111.1 Sts2VersionSeam.IsBlitz(...) must return false.");
            Assert(!Sts2VersionSeam.IsCustomGame(stdA0) && Sts2VersionSeam.IsCustomGame(customRun),
                "v111.1 Sts2VersionSeam.IsCustomGame must distinguish Standard vs Custom runs.");
            Assert(!Sts2VersionSeam.HasDoubleBossAscension(null) &&
                   !Sts2VersionSeam.HasDoubleBossAscension(stdA0) &&
                   Sts2VersionSeam.HasDoubleBossAscension(stdA10),
                "v111.1 Sts2VersionSeam.HasDoubleBossAscension must track Ascension >= 10.");
            Assert(Sts2VersionSeam.GetActiveDominionId(stdA0) == null,
                "v111.1 Sts2VersionSeam.GetActiveDominionId must return null.");
            Assert(Sts2VersionSeam.GetRunModelIds(stdA0).Length == 0,
                "v111.1 Sts2VersionSeam.GetRunModelIds must return an empty array.");

            await Task.CompletedTask;
            GD.Print("PASS v111.1 modes seam: IsDread=false, IsBlitz=false, IsCustomGame, HasDoubleBossAscension(A0/A10), GetActiveDominionId=null, GetRunModelIds=[].");
#else
            await VerifyDreadModeSuite();
            await VerifyBlitzModeSuite();
            GD.Print("PASS v113.1 Dread & Blitz modes suite: all 8 boss Dominions (registration + live combat), Dread HP scaling/restore, Blitz rooms/map/Crossroads/music/shop/TriviaRelic packet round-trip, and BugSnapshot capture.");
#endif
        }
        finally
        {
            DeactivateSyntheticCombat();
            RestoreRunManagerContext(previousRunManager);
            ResetSettings();
        }
    }

#if STS2_V113_1
    private static RunState CreateModeRun(
        GameMode mode,
        int ascension,
        string seed,
        bool isCustomGame = false,
        int players = 1)
    {
        List<Player> party = Enumerable.Range(1, players)
            .Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i))
            .ToList();
        ActModel[] acts =
        [
            ModelDb.Act<Overgrowth>().ToMutable(),
            ModelDb.Act<ThingsDepths>().ToMutable(),
            ModelDb.Act<Glory>().ToMutable(),
        ];
        return RunState.CreateForNewRun(
            party,
            acts,
            Array.Empty<ModifierModel>(),
            mode,
            isCustomGame,
            ascension,
            seed);
    }

    private static void BindRunManager(RunState run)
    {
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!
            .SetValue(RunManager.Instance, new AscensionManager(run.GameMode, run.AscensionLevel));
        typeof(RunManager).GetProperty("NetService")!
            .SetValue(RunManager.Instance, new NetSingleplayerGameService());
    }

    private static async Task<Battle> EnterDreadDominionBattle(
        DominionModel canonicalDominion,
        EncounterModel encounterCanonical,
        string seed)
    {
        DeactivateSyntheticCombat();
        CombatManager.Instance.History.Clear();
        RunState run = CreateModeRun(GameMode.Dread, 10, seed);
        run.CurrentActIndex = 1; // ThingsDepths
        BindRunManager(run);
        run.SetDominion(canonicalDominion);

        Assert(run.Dominion != null &&
               run.Dominion.IsMutable &&
               run.Dominion.GetType() == canonicalDominion.GetType(),
            $"RunState.SetDominion must clone {canonicalDominion.GetType().Name} into a mutable instance.");
        Assert(Sts2VersionSeam.GetActiveDominionId(run) == canonicalDominion.Id.ToString(),
            $"Sts2VersionSeam.GetActiveDominionId must resolve {canonicalDominion.Id}.");

        EncounterModel encounter = encounterCanonical.ToMutable();
        MapPointType pointType = encounter.RoomType == RoomType.Elite ? MapPointType.Elite : MapPointType.Monster;
        run.AppendToMapPointHistory(pointType, encounter.RoomType, encounter.Id);
        CombatRoom room = new(encounter, run);
        Assert(ReferenceEquals(room.CombatState.Dominion, run.Dominion),
            $"CombatState must inherit active Dominion {canonicalDominion.Id.Entry}.");
        run.PushRoom(room);

        foreach (Player player in run.Players)
        {
            player.ResetCombatState();
            room.CombatState.AddPlayer(player);
            player.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
        }

        encounter.GenerateMonstersWithSlots(run);
        foreach ((MonsterModel monster, string? slot) in encounter.MonstersWithSlots)
        {
            Creature creature = room.CombatState.CreateCreature(monster, CombatSide.Enemy, slot);
            room.CombatState.AddCreature(creature);
            monster.SetUpForCombat();
            monster.RollMove(run.Players.Select(p => p.Creature));
        }

        room.CombatState.CurrentSide = CombatSide.Player;
        room.CombatState.RoundNumber = 1;
        ActivateSyntheticCombat(room.CombatState);
        await Hook.AfterRoomEntered(run, room);
        await Hook.BeforeCombatStart(run, room.CombatState);
        return new Battle(run, room, encounter);
    }

    private async Task VerifyDreadModeSuite()
    {
        // 1. Dread run creation at Ascension 10 with ThingsDepths in Act 2.
        RunState dreadRun = CreateModeRun(GameMode.Dread, 10, "modes-dread-a10");
        dreadRun.CurrentActIndex = 1;
        Assert(dreadRun.Act is ThingsDepths, "Dread run Act 2 must be ThingsDepths.");
        Assert(dreadRun.RunModels.OfType<DreadRunModel>().Any(),
            "Dread run must include DreadRunModel in RunState.RunModels.");
        BindRunManager(dreadRun);
        Assert(Sts2VersionSeam.IsDread(),
            "Sts2VersionSeam.IsDread() must return true while RunManager.Instance.State is a Dread run.");
        Assert(!Sts2VersionSeam.IsBlitz(dreadRun) && !Sts2VersionSeam.IsCustomGame(dreadRun),
            "Dread run must report IsBlitz=false and IsCustomGame=false.");
        Assert(Sts2VersionSeam.HasDoubleBossAscension(dreadRun),
            "Dread A10 run must report HasDoubleBossAscension=true.");

        // 2. Verify all 8 mod boss encounters map to their exact Things*Dominion.
        (EncounterModel Boss, Type ExpectedDominion)[] bossDominionContracts =
        [
            (ModelDb.Encounter<ThingsOriginFogmogBossEncounter>(), typeof(ThingsOriginFogmogDominion)),
            (ModelDb.Encounter<ThingsScaleBeetleBossEncounter>(), typeof(ThingsScaleBeetleDominion)),
            (ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>(), typeof(ThingsBowlbugProgenitorDominion)),
            (ModelDb.Encounter<ThingsCaveGodBossEncounter>(), typeof(ThingsCaveGodDominion)),
            (ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), typeof(ThingsHumanFaceTowerDominion)),
            (ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(), typeof(ThingsAbyssalInkmawDominion)),
            (ModelDb.Encounter<ThingsGravetideSlugBossEncounter>(), typeof(ThingsGravetideSlugDominion)),
            (ModelDb.Encounter<ThingsTheLegacyBossEncounter>(), typeof(ThingsTheLegacyDominion)),
        ];
        foreach ((EncounterModel boss, Type expectedDominion) in bossDominionContracts)
        {
            DominionModel? dominion = boss.GetDominion();
            Assert(dominion != null && dominion.GetType() == expectedDominion,
                $"Boss {boss.Id.Entry} GetDominion() returned {dominion?.GetType().Name ?? "<null>"}, expected {expectedDominion.Name}.");
            DominionModel resolvedDominion = dominion!;
            Assert(resolvedDominion.IsCanonical && ReferenceEquals(ModelDb.GetById<DominionModel>(resolvedDominion.Id), resolvedDominion),
                $"Boss {boss.Id.Entry} GetDominion() must return canonical ModelDb instance.");
        }

        // 3. Live synthetic combat verification for all 7 Dominions.
        // 3a. ThingsOriginFogmogDominion: +2 Regen (Normal) / +3 Regen (Elite).
        {
            Battle normalBattle = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsOriginFogmogDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-fogmog-normal");
            Assert(normalBattle.Enemies.Length == 2 &&
                   normalBattle.Enemies.All(e => e.GetPower<RegenPower>()?.Amount == 2),
                "ThingsOriginFogmogDominion must grant 2 Regen to all enemies in Normal rooms.");
            DeactivateSyntheticCombat();

            Battle eliteBattle = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsOriginFogmogDominion>(),
                ModelDb.Encounter<ThingsRadioJellyfishElite>(),
                "dread-fogmog-elite");
            Assert(eliteBattle.Enemies.Length == 1 &&
                   eliteBattle.Enemies.All(e => e.GetPower<RegenPower>()?.Amount == 3),
                "ThingsOriginFogmogDominion must grant 3 Regen to all enemies in Elite rooms.");
            DeactivateSyntheticCombat();
        }

        // 3b. ThingsScaleBeetleDominion: enemies +10 ThingsScaleUpPower, player +10 ThingsScaleDownPower.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsScaleBeetleDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-scale-beetle");
            Assert(b.Enemies.Length == 2 &&
                   b.Enemies.All(e => e.GetPower<ThingsScaleUpPower>()?.Amount == 10),
                "ThingsScaleBeetleDominion must grant 10 ThingsScaleUpPower to all enemies.");
            Assert(b.Players[0].Creature.GetPower<ThingsScaleDownPower>()?.Amount == 10,
                "ThingsScaleBeetleDominion must apply 10 ThingsScaleDownPower to the player.");
            DeactivateSyntheticCombat();
        }

        // 3c. ThingsBowlbugProgenitorDominion: 1-enemy room +2 Strength at start; multi-enemy room +2 Strength on enemy death.
        {
            Battle soloBattle = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsBowlbugProgenitorDominion>(),
                ModelDb.Encounter<ThingsCaveMawWeak>(),
                "dread-bowlbug-solo");
            Assert(soloBattle.Enemies.Length == 1 &&
                   soloBattle.Enemies[0].GetPower<StrengthPower>()?.Amount == 2,
                "ThingsBowlbugProgenitorDominion must grant +2 Strength at start in 1-enemy rooms.");
            DeactivateSyntheticCombat();

            Battle multiBattle = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsBowlbugProgenitorDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-bowlbug-multi");
            Assert(multiBattle.Enemies.Length == 2 &&
                   multiBattle.Enemies.All(e => (e.GetPower<StrengthPower>()?.Amount ?? 0) == 0),
                "ThingsBowlbugProgenitorDominion must not grant opening Strength in multi-enemy rooms.");
            Creature victim = multiBattle.Enemies[0];
            Creature survivor = multiBattle.Enemies[1];
            await CreatureCmd.Damage(Choice, victim, 99999, ValueProp.Unpowered, multiBattle.Players[0].Creature);
            Assert(!victim.IsAlive && survivor.IsAlive &&
                   survivor.GetPower<StrengthPower>()?.Amount == 2,
                "ThingsBowlbugProgenitorDominion must grant +2 Strength to surviving enemies when an enemy dies.");
            DeactivateSyntheticCombat();
        }

        // 3d. ThingsCaveGodDominion: shuffles 2 Debris into player Draw Pile at combat start.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsCaveGodDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-cave-god");
            int debrisInDraw = PileType.Draw.GetPile(b.Players[0]).Cards.Count(c => c is Debris);
            Assert(debrisInDraw == 2,
                $"ThingsCaveGodDominion must shuffle 2 Debris into Draw Pile at combat start, got {debrisInDraw}.");
            DeactivateSyntheticCombat();
        }

        // 3e. ThingsHumanFaceTowerDominion: 3 consecutive Attacks grant +6 Block to living enemies; Attack->Skill->Attack does not.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsHumanFaceTowerDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-human-face-tower");
            Player player = b.Players[0];
            foreach (Creature enemy in b.Enemies)
            {
                enemy.SetMaxHpInternal(500);
                enemy.SetCurrentHpInternal(500);
                ClearCreatureBlock(enemy);
            }

            // Alternating Attack -> Skill -> Attack must NOT trigger +6 Block.
            CardModel altA1 = b.State.CreateCard<StrikeIronclad>(player);
            CardModel altS1 = b.State.CreateCard<DefendIronclad>(player);
            CardModel altA2 = b.State.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardsToCombat([altA1, altS1, altA2], PileType.Hand, player);
            await CardCmd.AutoPlay(Choice, altA1, b.Enemies[0], skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, altS1, null, skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, altA2, b.Enemies[0], skipCardPileVisuals: true);
            Assert(b.Enemies.All(e => e.Block == 0),
                "ThingsHumanFaceTowerDominion must not grant Block on alternating Attack -> Skill -> Attack.");

            // New player turn resets streak from 1 to 0, then 3 consecutive Attacks trigger +6 Block on the 3rd Attack.
            await Hook.AfterSideTurnStart(b.State, CombatSide.Player, [player.Creature]);
            CardModel atk1 = b.State.CreateCard<StrikeIronclad>(player);
            CardModel atk2 = b.State.CreateCard<StrikeIronclad>(player);
            CardModel atk3 = b.State.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardsToCombat([atk1, atk2, atk3], PileType.Hand, player);
            await CardCmd.AutoPlay(Choice, atk1, b.Enemies[0], skipCardPileVisuals: true);
            Assert(b.Enemies.All(e => e.Block == 0), "1st consecutive Attack must not grant Block.");
            await CardCmd.AutoPlay(Choice, atk2, b.Enemies[0], skipCardPileVisuals: true);
            Assert(b.Enemies.All(e => e.Block == 0),
                "2nd consecutive Attack after turn reset must not grant Block prematurely.");
            await CardCmd.AutoPlay(Choice, atk3, b.Enemies[0], skipCardPileVisuals: true);
            Assert(b.Enemies.Length == 2 && b.Enemies.All(e => e.Block == 6),
                "3rd consecutive Attack in a turn must grant +6 Block to all living enemies.");
            DeactivateSyntheticCombat();
        }

        // 3e2. ThingsAbyssalInkmawDominion: player gains 1 Weak + 1 Frail and 1 Slimed shuffled into Draw Pile.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsAbyssalInkmawDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-abyssal-inkmaw");
            int slimedInDraw = PileType.Draw.GetPile(b.Players[0]).Cards.Count(c => c is Slimed);
            Assert(b.Players[0].Creature.GetPower<WeakPower>()?.Amount == 1,
                "ThingsAbyssalInkmawDominion must apply 1 Weak to the player at combat start.");
            Assert(b.Players[0].Creature.GetPower<FrailPower>()?.Amount == 1,
                "ThingsAbyssalInkmawDominion must apply 1 Frail to the player at combat start.");
            Assert(slimedInDraw == 1,
                $"ThingsAbyssalInkmawDominion must shuffle 1 Slimed into Draw Pile, got {slimedInDraw}.");
            DeactivateSyntheticCombat();
        }

        // 3f. ThingsGravetideSlugDominion: enemies +3 Regen AND 2 Slimed shuffled into Draw Pile.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsGravetideSlugDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-gravetide-slug");
            int slimedInDraw = PileType.Draw.GetPile(b.Players[0]).Cards.Count(c => c is Slimed);
            Assert(b.Enemies.Length == 2 &&
                   b.Enemies.All(e => e.GetPower<RegenPower>()?.Amount == 3),
                "ThingsGravetideSlugDominion must grant 3 Regen to all enemies.");
            Assert(slimedInDraw == 2,
                $"ThingsGravetideSlugDominion must shuffle 2 Slimed into Draw Pile, got {slimedInDraw}.");
            DeactivateSyntheticCombat();
        }

        // 3g. ThingsTheLegacyDominion: playing a card deals 1 blockable unpowered damage to player.
        {
            Battle b = await EnterDreadDominionBattle(
                ModelDb.Dominion<ThingsTheLegacyDominion>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                "dread-the-legacy");
            Player player = b.Players[0];
            foreach (Creature enemy in b.Enemies)
            {
                enemy.SetMaxHpInternal(500);
                enemy.SetCurrentHpInternal(500);
            }
            player.Creature.SetMaxHpInternal(100);
            player.Creature.SetCurrentHpInternal(80);
            ClearCreatureBlock(player.Creature);

            // Without Block -> -1 HP.
            CardModel strikeUnblocked = b.State.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardToCombat(strikeUnblocked, PileType.Hand, player);
            await CardCmd.AutoPlay(Choice, strikeUnblocked, b.Enemies[0], skipCardPileVisuals: true);
            Assert(player.Creature.CurrentHp == 79 && player.Creature.Block == 0,
                $"ThingsTheLegacyDominion unblocked card play must deal 1 HP damage (got HP={player.Creature.CurrentHp}, Block={player.Creature.Block}).");

            // With Block -> absorbed by Block (-1 Block, 0 HP loss).
            await CreatureCmd.GainBlock(player.Creature, 5, ValueProp.Unpowered, null);
            CardModel strikeBlocked = b.State.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardToCombat(strikeBlocked, PileType.Hand, player);
            await CardCmd.AutoPlay(Choice, strikeBlocked, b.Enemies[0], skipCardPileVisuals: true);
            Assert(player.Creature.CurrentHp == 79 && player.Creature.Block == 4,
                $"ThingsTheLegacyDominion blocked card play must be absorbed by Block (got HP={player.Creature.CurrentHp}, Block={player.Creature.Block}).");
            DeactivateSyntheticCombat();
        }

        // 4. Dread boss HP scaling: MinInitialHp increases in Dread vs Standard A10 for all 8 bosses,
        //    and returns to exact Standard A10 values once RunManager.Instance.State is restored.
        MonsterModel[] dreadBossMonsters =
        [
            ModelDb.Monster<ThingsOriginFogmog>(),
            ModelDb.Monster<ThingsScaleBeetle>(),
            ModelDb.Monster<ThingsBowlbugProgenitor>(),
            ModelDb.Monster<ThingsCaveGodBody>(),
            ModelDb.Monster<ThingsCaveGodLeftHand>(),
            ModelDb.Monster<ThingsCaveGodRightHand>(),
            ModelDb.Monster<ThingsHumanFaceTowerWrath>(),
            ModelDb.Monster<ThingsHumanFaceTowerLament>(),
            ModelDb.Monster<ThingsHumanFaceTowerBastion>(),
            ModelDb.Monster<ThingsHumanFaceTowerMockery>(),
            ModelDb.Monster<ThingsAbyssalInkmaw>(),
            ModelDb.Monster<ThingsAbyssalCrushingTentacle>(),
            ModelDb.Monster<ThingsAbyssalInkTentacle>(),
            ModelDb.Monster<ThingsGravetideSlug>(),
            ModelDb.Monster<ThingsTheLegacy>(),
        ];

        RunState stdA10Run = CreateModeRun(GameMode.Standard, 10, "modes-std-a10-hp");
        RunState dreadA10Run = CreateModeRun(GameMode.Dread, 10, "modes-dread-a10-hp");

        BindRunManager(stdA10Run);
        Assert(!Sts2VersionSeam.IsDread(), "Standard A10 run must report IsDread=false.");
        Dictionary<MonsterModel, int> stdA10Hp = dreadBossMonsters.ToDictionary(m => m, m => m.MinInitialHp);

        BindRunManager(dreadA10Run);
        Assert(Sts2VersionSeam.IsDread(), "Dread A10 run must report IsDread=true.");
        foreach (MonsterModel monster in dreadBossMonsters)
        {
            int dreadHp = monster.MinInitialHp;
            Assert(dreadHp > stdA10Hp[monster],
                $"Dread MinInitialHp for {monster.Id.Entry} must exceed Standard A10 ({dreadHp} vs {stdA10Hp[monster]}).");
        }

        BindRunManager(stdA10Run);
        Assert(!Sts2VersionSeam.IsDread(), "Restored Standard A10 run must report IsDread=false.");
        foreach (MonsterModel monster in dreadBossMonsters)
        {
            Assert(monster.MinInitialHp == stdA10Hp[monster],
                $"Restored Standard A10 MinInitialHp for {monster.Id.Entry} must return to {stdA10Hp[monster]}, got {monster.MinInitialHp}.");
        }

        // Verify BugSnapshot.Capture on Dread run with active Dominion.
        dreadRun.SetDominion(ModelDb.Dominion<ThingsCaveGodDominion>());
        BindRunManager(dreadRun);
        JsonObject dreadSnapshot = InvokeBugSnapshotCapture(dreadRun);
        Assert(dreadSnapshot["game_mode"]?.GetValue<string>() == "Dread",
            "BugSnapshot.Capture must record game_mode=Dread.");
        Assert(dreadSnapshot.ContainsKey("is_custom_game") && dreadSnapshot["is_custom_game"]?.GetValue<bool>() == false,
            "BugSnapshot.Capture must record is_custom_game=false on standard Dread run.");
        Assert(dreadSnapshot.ContainsKey("dominion_id") &&
               dreadSnapshot["dominion_id"]?.GetValue<string>() == ModelDb.Dominion<ThingsCaveGodDominion>().Id.ToString(),
            $"BugSnapshot.Capture must record active dominion_id ({dreadSnapshot["dominion_id"]}).");
        Assert(dreadSnapshot.ContainsKey("run_models") &&
               dreadSnapshot["run_models"] is JsonArray dreadModels &&
               dreadModels.Select(n => n?.GetValue<string>()).Contains(ModelDb.RunModel<DreadRunModel>().Id.ToString()),
            "BugSnapshot.Capture must populate run_models containing DreadRunModel.");

        GD.Print("PASS Dread mode suite: A10 run state, 7 boss Dominions (mapping + live combat hooks), Dread HP scaling/restore, and BugSnapshot capture.");
    }

    private async Task VerifyBlitzModeSuite()
    {
        // Sanity-check that Standard runs still attach a Crossroads ledger so the Blitz absence check is non-vacuous.
        RunState standardControl = CreateModeRun(GameMode.Standard, 0, "modes-standard-crossroads-control");
        Assert(Crossroads.Ledger(standardControl) != null,
            "Standard run control must attach a Crossroads ledger.");

        FieldInfo ledgersField = typeof(Crossroads).GetField("Ledgers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(Crossroads).FullName, "Ledgers");
        var ledgersTable = (ConditionalWeakTable<RunState, ThingsCrossroads>)ledgersField.GetValue(null)!;

        // 1. Blitz run (Ascension 0 and 5) with ThingsDepths in Act 2: rooms, map, Crossroads, and BugSnapshot.
        RunState? blitzA0ForMusic = null;
        foreach (int asc in new[] { 0, 5 })
        {
            RunState blitzRun = CreateModeRun(GameMode.Blitz, asc, $"modes-blitz-a{asc}");
            blitzRun.CurrentActIndex = 1; // ThingsDepths
            BindRunManager(blitzRun);

            Assert(Sts2VersionSeam.IsBlitz(blitzRun) && !Sts2VersionSeam.IsDread() && !Sts2VersionSeam.IsCustomGame(blitzRun),
                $"Blitz A{asc} run seam flags mismatch.");
            Assert(!Sts2VersionSeam.HasDoubleBossAscension(blitzRun),
                $"Blitz A{asc} must never report HasDoubleBossAscension=true.");
            Assert(blitzRun.RunModels.OfType<BlitzRunModel>().Any(),
                $"Blitz A{asc} must include BlitzRunModel in RunModels.");

            foreach (ActModel act in blitzRun.Acts)
                act.GenerateRooms(blitzRun);

            ActModel depthsAct = blitzRun.Act;
            Assert(depthsAct is ThingsDepths, $"Blitz A{asc} Act 2 must be ThingsDepths.");
            var actSave = depthsAct.ToSave();
            var normalIds = actSave.SerializableRooms.NormalEncounterIds;
            Assert(normalIds.Count == depthsAct.GetNumberOfRooms(isMultiplayer: false),
                $"Blitz A{asc} Depths normal encounter count mismatch: {normalIds.Count}.");
            Assert(ModelDb.GetById<EncounterModel>(normalIds[0]).IsWeak &&
                   depthsAct.AllWeakEncounters.Any(e => e.Id == normalIds[0]),
                $"Blitz A{asc} Depths must generate 1 opening weak encounter.");
            Assert(normalIds.Skip(1).All(id =>
                    !ModelDb.GetById<EncounterModel>(id).IsWeak &&
                    depthsAct.AllRegularEncounters.Any(e => e.Id == id)),
                $"Blitz A{asc} Depths remaining encounters must all be regular encounters.");
            Assert(actSave.SerializableRooms.EliteEncounterIds.Count == 15 && actSave.SerializableRooms.AncientId != null,
                $"Blitz A{asc} Depths elite and ancient room sets must be complete.");

            BlitzActMap blitzMap = new(blitzRun);
            blitzRun.Map = blitzMap;
            Assert(blitzMap.GetRowCount() == 7,
                $"BlitzActMap must have GetRowCount()==7, got {blitzMap.GetRowCount()}.");
            List<MapPoint> linearPoints = blitzMap.GetMapPoints();
            MapPointType[] expectedLinear =
            [
                MapPointType.Ancient,
                MapPointType.Monster,
                MapPointType.Monster,
                MapPointType.Shop,
                MapPointType.Elite,
                MapPointType.Treasure,
                MapPointType.RestSite,
                MapPointType.Boss,
            ];
            Assert(linearPoints.Count == 8 &&
                   linearPoints.Select(p => p.PointType).SequenceEqual(expectedLinear),
                $"BlitzActMap A{asc} must have linear 8-floor layout (got {linearPoints.Count} points).");

            // Crossroads must skip attaching and never inject ThingsCrossroads into Blitz saves.
            Assert(!ledgersTable.TryGetValue(blitzRun, out _) && Crossroads.Ledger(blitzRun) == null,
                $"Crossroads.Ledgers must not attach a ledger to Blitz A{asc} run.");

            SerializableRun blitzSave = RunManager.Instance.ToSave(null);
            Assert(!blitzSave.Modifiers.Any(m => m.Id?.Entry == "THINGS_CROSSROADS"),
                $"Blitz A{asc} SerializableRun.Modifiers must not contain ThingsCrossroads.");

            var runWriter = new PacketWriter { WarnOnGrow = false };
            blitzSave.Serialize(runWriter);
            var runReader = new PacketReader();
            runReader.Reset(runWriter.Buffer);
            var roundTrippedSave = new SerializableRun();
            roundTrippedSave.Deserialize(runReader);
            Assert(!roundTrippedSave.Modifiers.Any(m => m.Id?.Entry == "THINGS_CROSSROADS"),
                $"Packet round-tripped Blitz A{asc} save must not inject ThingsCrossroads.");
            RunState restoredBlitzRun = RunState.FromSerializable(roundTrippedSave);
            Assert(!ledgersTable.TryGetValue(restoredBlitzRun, out _) && Crossroads.Ledger(restoredBlitzRun) == null,
                $"Restored Blitz A{asc} run must not attach a Crossroads ledger.");

            if (asc == 0)
            {
                blitzA0ForMusic = blitzRun;

                JsonObject blitzSnapshot = InvokeBugSnapshotCapture(blitzRun);
                Assert(blitzSnapshot["game_mode"]?.GetValue<string>() == "Blitz",
                    "BugSnapshot.Capture must record game_mode=Blitz.");
                Assert(blitzSnapshot.ContainsKey("is_custom_game") && blitzSnapshot["is_custom_game"]?.GetValue<bool>() == false,
                    "BugSnapshot.Capture must record is_custom_game=false on standard Blitz run.");
                Assert(blitzSnapshot.ContainsKey("dominion_id") && blitzSnapshot["dominion_id"] == null,
                    "BugSnapshot.Capture must record dominion_id=null on Blitz run.");
                Assert(blitzSnapshot.ContainsKey("run_models") &&
                       blitzSnapshot["run_models"] is JsonArray blitzModels &&
                       blitzModels.Select(n => n?.GetValue<string>()).Contains(ModelDb.RunModel<BlitzRunModel>().Id.ToString()),
                    "BugSnapshot.Capture must populate run_models containing BlitzRunModel.");
            }
        }

        // Also verify BugSnapshot.Capture records is_custom_game=true on a Custom Game run.
        RunState customSnapshotRun = CreateModeRun(GameMode.Standard, 0, "modes-custom-flag-snap", isCustomGame: true);
        BindRunManager(customSnapshotRun);
        Assert(Sts2VersionSeam.IsCustomGame(customSnapshotRun),
            "Sts2VersionSeam.IsCustomGame must return true when RunState.IsCustomGame is true.");
        JsonObject customSnapshot = InvokeBugSnapshotCapture(customSnapshotRun);
        Assert(customSnapshot.ContainsKey("is_custom_game") && customSnapshot["is_custom_game"]?.GetValue<bool>() == true,
            "BugSnapshot.Capture must record is_custom_game=true on custom runs.");

        // 2. Verify DepthsActMusicController evolves suite by Floor 4 in Blitz (differs from Floor 1).
        await VerifyBlitzDepthsMusicEvolution(blitzA0ForMusic!);

        // 3. Verify Blitz Shop enchantments (idempotency) + ThingsBlitzTriviaRelicEntry organic roll, localization, and history packet round-trip.
        VerifyBlitzShopIntegration();

        GD.Print("PASS Blitz mode suite: A0/A5 rooms (1 weak + 13 regular), 8-floor BlitzActMap, Crossroads exclusion, Floor-4 Depths music evolution, Blitz shop enchantments & Trivia Relic entry, and BugSnapshot capture.");
    }

    private async Task VerifyBlitzDepthsMusicEvolution(RunState blitzRun)
    {
        for (ulong s = 0; s < 16; s++)
        {
            ulong seed = s * 0x123456789ABCDEFUL + s;
            int f1 = DepthsActMusicController.ResolveTargetSuiteIndex(seed, 1, 4);
            int f3 = DepthsActMusicController.ResolveTargetSuiteIndex(seed, 3, 4);
            int f4 = DepthsActMusicController.ResolveTargetSuiteIndex(seed, 4, 4);
            Assert(f1 is 0 or 1 && f3 == f1 && f4 == 1 - f1,
                $"Blitz ResolveTargetSuiteIndex(threshold=4) must keep suite on Floors 1..3 and flip at Floor 4 (f1={f1}, f3={f3}, f4={f4}).");
        }

        var proxyScript = new GDScript
        {
            SourceCode = """
                extends Node
                var _musicEv = null
                var _currentTrack: String = "event:/music/act2_a1_v2"
                func _ready() -> void:
                    _musicEv = self
                func stop_music() -> void:
                    _musicEv = null
                func update_music(track: String) -> void:
                    _currentTrack = track
                    _musicEv = self
                func update_global_parameter(param: String, val: Variant) -> void:
                    pass
                func stop_custom_music() -> void:
                    pass
                func unload_act_banks() -> void:
                    pass
                """,
        };
        Assert(proxyScript.Reload() == Error.Ok, "Compiled GDScript FMOD Proxy double for Blitz music probe.");

        var proxy = new Node { Name = "Proxy" };
        proxy.SetScript(proxyScript);
        var musicController = new NRunMusicController { Name = "ProbeBlitzMusicController" };
        musicController.AddChild(proxy);
        AddChild(musicController);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        MethodInfo syncMain = typeof(DepthsActMusicController).GetMethod("SyncOnMainThread", MusicPrivateStatic)
            ?? throw new MissingMethodException(nameof(DepthsActMusicController), "SyncOnMainThread");
        MethodInfo ensureInst = typeof(DepthsActMusicController).GetMethod("EnsureInstance", MusicPrivateStatic)!;
        MethodInfo stopAll = typeof(DepthsActMusicController).GetMethod("StopAllImmediate", MusicPrivateInstance)!;

        BindRunManager(blitzRun);
        blitzRun.CurrentActIndex = 1; // ThingsDepths

        try
        {
            while (blitzRun.CurrentRoom != null)
                blitzRun.PopCurrentRoom();

            // Floor 1: ExploreInit -> initial suite.
            blitzRun.ActFloor = 1;
            syncMain.Invoke(null, [musicController]);
            int floor1Suite = DepthsActMusicController.ActiveExploreSuite;
            Assert(DepthsActMusicController.CurrentStage == DepthsActMusicController.Stage.ExploreInit &&
                   floor1Suite is 0 or 1,
                $"Blitz Floor 1 Depths music must enter ExploreInit with suite 0 or 1 (stage={DepthsActMusicController.CurrentStage}, suite={floor1Suite}).");

            var inst = (DepthsActMusicController?)ensureInst.Invoke(null, null);
            Assert(inst != null, "DepthsActMusicController instance exists for Blitz run.");

            // Floor 3: still initial suite.
            stopAll.Invoke(inst, null);
            blitzRun.ActFloor = 3;
            blitzRun.PushRoom(new TreasureRoom(1));
            syncMain.Invoke(null, [musicController]);
            Assert(DepthsActMusicController.ActiveExploreSuite == floor1Suite,
                $"Blitz Floor 3 Depths music must remain on initial suite {floor1Suite}, got {DepthsActMusicController.ActiveExploreSuite}.");

            // Floor 4: evolves to companion suite!
            stopAll.Invoke(inst, null);
            blitzRun.PopCurrentRoom();
            blitzRun.ActFloor = 4;
            blitzRun.PushRoom(new TreasureRoom(1));
            syncMain.Invoke(null, [musicController]);
            int floor4Suite = DepthsActMusicController.ActiveExploreSuite;
            Assert(floor4Suite == 1 - floor1Suite && floor4Suite != floor1Suite,
                $"Blitz Floor 4 Depths music must evolve to companion suite {1 - floor1Suite}, got {floor4Suite}.");
        }
        finally
        {
            while (blitzRun.CurrentRoom != null)
                blitzRun.PopCurrentRoom();
            var inst = (DepthsActMusicController?)ensureInst.Invoke(null, null);
            if (inst != null)
            {
                stopAll.Invoke(inst, null);
                inst.Free();
            }
            musicController.Free();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void VerifyBlitzShopIntegration()
    {
        ModelId[] expectedModEnchantIds =
        [
            ModelDb.Enchantment<ThingsDisperse>().Id,
            ModelDb.Enchantment<ThingsOverclocked>().Id,
            ModelDb.Enchantment<ThingsWeighted>().Id,
        ];

        int? expectedEnchantOptionCount = null;
        int triviaHits = 0;
        ThingsBlitzTriviaRelicEntry? sampleTrivia = null;

        for (int seed = 0; seed < 32; seed++)
        {
            RunState blitzRun = CreateModeRun(GameMode.Blitz, 0, $"blitz-shop-{seed}");
            BindRunManager(blitzRun);
            MerchantInventory inventory = MerchantInventory.CreateForBlitzMerchant(blitzRun.Players[0]);

            Assert(inventory.IsBlitzShop && inventory.BlitzEntries.Count == 6 && inventory.BlitzChoicesRemaining == 3,
                $"Blitz shop seed {seed} must generate 6 Blitz entries with 3 choices.");

            List<(EnchantmentModel, int)> shopEnchants = BlitzRunModel.shopEnchantmentOptions;
            foreach (ModelId modEnchantId in expectedModEnchantIds)
            {
                Assert(shopEnchants.Count(e => e.Item1.Id == modEnchantId) == 1,
                    $"BlitzRunModel.shopEnchantmentOptions must contain exactly 1 entry for {modEnchantId} (seed {seed}).");
            }

            if (expectedEnchantOptionCount == null)
            {
                expectedEnchantOptionCount = shopEnchants.Count;
                // Calling CreateForBlitzMerchant again immediately must stay idempotent.
                _ = MerchantInventory.CreateForBlitzMerchant(blitzRun.Players[0]);
                Assert(shopEnchants.Count == expectedEnchantOptionCount.Value,
                    "Repeated MerchantInventory.CreateForBlitzMerchant calls must not duplicate shopEnchantmentOptions.");
            }
            else
            {
                Assert(shopEnchants.Count == expectedEnchantOptionCount.Value,
                    $"shopEnchantmentOptions count drifted from {expectedEnchantOptionCount.Value} to {shopEnchants.Count} on seed {seed}.");
            }

            if (seed == 0)
            {
                Type[] seed0EntryTypes = inventory.BlitzEntries.Select(e => e.GetType()).ToArray();
                RunState replayRun = CreateModeRun(GameMode.Blitz, 0, "blitz-shop-0");
                BindRunManager(replayRun);
                MerchantInventory replayInv = MerchantInventory.CreateForBlitzMerchant(replayRun.Players[0]);
                Assert(replayInv.BlitzEntries.Select(e => e.GetType()).SequenceEqual(seed0EntryTypes),
                    "Blitz shop entries must roll deterministically for the same seed.");
            }

            ThingsBlitzTriviaRelicEntry? trivia = inventory.BlitzEntries.OfType<ThingsBlitzTriviaRelicEntry>().FirstOrDefault();
            if (trivia != null)
            {
                triviaHits++;
                sampleTrivia ??= trivia;
            }
        }

        Assert(triviaHits > 0 && triviaHits < 32 && sampleTrivia != null,
            $"ThingsBlitzTriviaRelicEntry must roll organically across 32 seeded Blitz shops (expected 1..31 hits, got {triviaHits}).");
        ThingsBlitzTriviaRelicEntry rolledTrivia = sampleTrivia!;
        Assert(rolledTrivia.MerchantEntryType == BlitzMerchantEntryType.Relic,
            $"ThingsBlitzTriviaRelicEntry.MerchantEntryType must be Relic, got {rolledTrivia.MerchantEntryType}.");

        // Verify MerchantBargain skips Blitz entries / GameMode.Blitz.
        Type bargainManagerType = typeof(ThingsDepths).Assembly.GetType(
            "STS2_Things.Features.MerchantBargain.MerchantBargainManager", throwOnError: true)!;
        MethodInfo tryIntercept = bargainManagerType.GetMethod(
            "TryIntercept", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        RunState bargainBlitzRun = CreateModeRun(GameMode.Blitz, 0, "blitz-bargain-skip");
        BindRunManager(bargainBlitzRun);
        MerchantInventory bargainInv = MerchantInventory.CreateForBlitzMerchant(bargainBlitzRun.Players[0]);
        object?[] interceptArgs = [bargainInv.BlitzEntries[0], bargainInv, false, null];
        bool intercepted = (bool)tryIntercept.Invoke(null, interceptArgs)!;
        Assert(!intercepted, "MerchantBargainManager.TryIntercept must skip Blitz merchant entries.");

        // Verify bilingual Title/Description & HoverTips on ThingsBlitzTriviaRelicEntry.
        string originalLang = SaveManager.Instance.SettingsSave.Language ?? "zhs";
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                SaveManager.Instance.SettingsSave.Language = lang;
                LocManager.Initialize();
                AssertLocClean(rolledTrivia.Title.GetFormattedText(), $"ThingsBlitzTriviaRelicEntry.Title ({lang})");
                AssertLocClean(rolledTrivia.Description.GetFormattedText(), $"ThingsBlitzTriviaRelicEntry.Desc ({lang})");
                foreach (HoverTip ht in rolledTrivia.HoverTips.OfType<HoverTip>())
                    AssertLocClean(ht.Description, $"ThingsBlitzTriviaRelicEntry.HoverTip ({lang})");
            }
        }
        finally
        {
            SaveManager.Instance.SettingsSave.Language = originalLang;
            LocManager.Initialize();
        }

        // Verify BlitzMerchantHistoryEntry packet round-trip.
        BlitzMerchantHistoryEntry historyEntry = rolledTrivia.GenerateHistoryEntry(wasPicked: true);
        Assert(historyEntry.Type == BlitzMerchantEntryType.Relic && historyEntry.wasPicked,
            "ThingsBlitzTriviaRelicEntry.GenerateHistoryEntry(true) must produce Relic + wasPicked=true.");
        var histWriter = new PacketWriter { WarnOnGrow = false };
        historyEntry.Serialize(histWriter);
        var histReader = new PacketReader();
        histReader.Reset(histWriter.Buffer);
        var restoredHistory = new BlitzMerchantHistoryEntry();
        restoredHistory.Deserialize(histReader);
        Assert(restoredHistory.Type == historyEntry.Type &&
               restoredHistory.wasPicked == historyEntry.wasPicked &&
               restoredHistory.modelId == historyEntry.modelId,
            "ThingsBlitzTriviaRelicEntry BlitzMerchantHistoryEntry must round-trip cleanly via PacketWriter/PacketReader.");
    }

    private static JsonObject InvokeBugSnapshotCapture(RunState run)
    {
        Type bugSnapshotType = typeof(ThingsDepths).Assembly.GetType(
            "STS2_Things.Diagnostics.BugSnapshot", throwOnError: true)!;
        MethodInfo captureMethod = bugSnapshotType.GetMethod(
            "Capture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var snapshot = (JsonObject?)captureMethod.Invoke(null, [run, null]);
        Assert(snapshot != null, "BugSnapshot.Capture must return a non-null JsonObject.");
        return snapshot!;
    }
#endif
}
