using Godot;
#if STS2_V113_1
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Acts;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Powers;
#endif

public partial class DepthsProbeNode
{
    private async Task VerifyFoolArrowRegressions()
    {
#if !STS2_V113_1
        await Task.CompletedTask;
        GD.Print("PASS Fool arrow regression skipped on v111.1 (Fool does not exist in v111.1).");
#else
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

        // 1) Cave God Hand broken directly by FoolishShot (AttackCommand overload) + YahPower & Twine dislodge hooks.
        await RunCase("CaveGodHand.LethalFoolishShotDislodgesArrow", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "fool-cg-foolish");
            Player fool = b.Players[0];
            var body = b.Monster<ThingsCaveGodBody>();
            var left = b.Monster<ThingsCaveGodLeftHand>();
            await RelicCmd.Obtain<Twine>(fool);
            await PowerCmd.Apply<YahPower>(Choice, fool.Creature, 1m, fool.Creature, null);

            Assert(ArrowCmd.GetAvailableArrowCount(fool) == 1, "Fool starts with 1 available arrow.");
            await CreatureCmd.Damage(Choice, left.Creature, left.Creature.CurrentHp - 10, ValueProp.Unpowered, fool.Creature);
            Assert(!left.IsDown && left.Creature.CurrentHp == 10, "Left hand prepared at 10 HP.");

            int energyBefore = fool.PlayerCombatState!.Energy;
            int blockBefore = fool.Creature.Block;
            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, left.Creature, skipCardPileVisuals: true);

            Assert(left.IsDown && body.IsWeakPhase, "FoolishShot broke left hand and exposed Cave God core.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.GetAvailableArrowCount(fool) == 1,
                $"Arrow stuck after breaking CaveGodHand with FoolishShot (available={ArrowCmd.GetAvailableArrowCount(fool)}, status={fool.PlayerCombatState.Arrows[0].Status}).");
            Assert(!ArrowCmd.CreatureIsImpaledByArrow(left.Creature), "Downed CaveGodHand must not retain ImpaledPower.");
            Assert(fool.PlayerCombatState.Energy == energyBefore + 1, "YahPower must trigger when breaking CaveGodHand dislodges arrow.");
            Assert(fool.Creature.Block == blockBefore + 4, "Twine must grant Block when breaking CaveGodHand dislodges arrow.");
        });

        // 2) Cave God Hand broken by TrickShot (v0.114.0 draws 2 cards on shot) & RainShot (Func<Task> overload).
        await RunCase("CaveGodHand.LethalTrickShotDrawsAndRainShotDislodges", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "fool-cg-trickshot");
            Player fool = b.Players[0];
            var left = b.Monster<ThingsCaveGodLeftHand>();
            var right = b.Monster<ThingsCaveGodRightHand>();

            await CreatureCmd.Damage(Choice, left.Creature, left.Creature.CurrentHp - 8, ValueProp.Unpowered, fool.Creature);
            int handBefore = PileType.Hand.GetPile(fool).Cards.Count;
            var trickShot = b.State.CreateCard<TrickShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(trickShot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, trickShot, left.Creature, skipCardPileVisuals: true);

            Assert(left.IsDown, "TrickShot broke left hand.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool),
                $"TrickShot breaking CaveGodHand left arrow in status {fool.PlayerCombatState!.Arrows[0].Status}.");
            Assert(PileType.Hand.GetPile(fool).Cards.Count >= handBefore + 1,
                $"v0.114.0 TrickShot must draw cards after breaking CaveGodHand (before={handBefore}, after={PileType.Hand.GetPile(fool).Cards.Count}).");

            await CreatureCmd.Damage(Choice, right.Creature, right.Creature.CurrentHp - 8, ValueProp.Unpowered, fool.Creature);
            var rainShot = b.State.CreateCard<RainShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(rainShot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, rainShot, right.Creature, skipCardPileVisuals: true);

            Assert(right.IsDown, "RainShot broke right hand.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool),
                $"RainShot breaking CaveGodHand left arrow in status {fool.PlayerCombatState.Arrows[0].Status}.");
        });

        // 3) Cave God Hand impaled first, then broken by EpicRetrieval (1-hit lethal before 3-hit counter) OR DoomPower.
        await RunCase("CaveGodHand.ImpaledThenBrokenByAttackOrDoomDislodgesArrow", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "fool-cg-impaled-break");
            Player fool = b.Players[0];
            var left = b.Monster<ThingsCaveGodLeftHand>();
            var right = b.Monster<ThingsCaveGodRightHand>();

            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, left.Creature, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.CreatureIsImpaledByPlayersArrow(left.Creature, fool),
                "Non-lethal FoolishShot impales CaveGodHand.");

            await CreatureCmd.Damage(Choice, left.Creature, left.Creature.CurrentHp - 10, ValueProp.Unpowered, fool.Creature);
            int energyBefore = fool.PlayerCombatState!.Energy;
            var epicRetrieval = b.State.CreateCard<EpicRetrieval>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(epicRetrieval, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, epicRetrieval, left.Creature, skipCardPileVisuals: true);

            Assert(left.IsDown, "EpicRetrieval broke impaled CaveGodHand.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && !ArrowCmd.CreatureIsImpaledByArrow(left.Creature),
                "Breaking an already-impaled CaveGodHand must dislodge the arrow instead of trapping it on the downed hand.");
            Assert(fool.PlayerCombatState.Energy == energyBefore + 3,
                "EpicRetrieval breaking an impaled CaveGodHand must grant its +3 energy dislodge bonus.");

            // Now impale right hand and break via DoomPower at Enemy Turn end.
            var shot2 = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot2, right.Creature, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool), "Right hand impaled before Doom.");

            await PowerCmd.Apply<DoomPower>(Choice, right.Creature, right.Creature.CurrentHp, fool.Creature, null);
            await DoomPower.DoomKill(new[] { right.Creature });
            Assert(right.IsDown && ArrowCmd.PlayerHasAnyArrows(fool) && !ArrowCmd.CreatureIsImpaledByArrow(right.Creature),
                "Doom breaking an impaled CaveGodHand must dislodge the arrow.");
        });

        // 4) Cave God Hand impaled first, then executes Grab (IsGrabbing = true, hand becomes untargetable/unhittable).
        await RunCase("CaveGodHand.ImpaledThenGrabbingPlayerDislodgesArrow", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "fool-cg-grab");
            Player fool = b.Players[0];
            var left = b.Monster<ThingsCaveGodLeftHand>();
            var right = b.Monster<ThingsCaveGodRightHand>();
            ThingsCaveGodHand grabber = left.NextMove.Id.Contains("GRAB", StringComparison.OrdinalIgnoreCase) ? left : right;
            Assert(grabber.NextMove.Id.Contains("GRAB", StringComparison.OrdinalIgnoreCase),
                $"Expected lead hand to roll GRAB on turn 1, got {left.NextMove.Id}/{right.NextMove.Id}.");

            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, grabber.Creature, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.CreatureIsImpaledByPlayersArrow(grabber.Creature, fool),
                "Grabbing hand is impaled before enemy turn.");

            b.State.CurrentSide = CombatSide.Enemy;
            await grabber.PerformMove();
            Assert(grabber.IsGrabbing && !grabber.Creature.IsHittable, "Hand entered IsGrabbing state.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && !ArrowCmd.CreatureIsImpaledByArrow(grabber.Creature),
                $"CaveGodHand entering IsGrabbing (unhittable) must dislodge impaled arrows so Fool can shoot CaptiveClaw (status={fool.PlayerCombatState!.Arrows[0].Status}).");
        });

        // 5) Human Face Tower: non-lethal arrow into active face, then face rotates away (2-turn cycle & combo whip).
        await RunCase("HumanFaceTower.ImpaledFaceRotatingAwayDislodgesArrow", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), "fool-tower-rotate");
            Player fool = b.Players[0];
            var wrath = b.Monster<ThingsHumanFaceTowerWrath>();
            var lament = b.Monster<ThingsHumanFaceTowerLament>();
            var ctrl = wrath.Controller!;

            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, wrath.Creature, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.CreatureIsImpaledByPlayersArrow(wrath.Creature, fool),
                "Wrath face impaled while centered.");

            // Turn 1 of Wrath: stays centered, arrow must remain impaled!
            await RunTowerEnemyRoundAsync(b);
            Assert(ctrl.ActiveFace == wrath && !ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.CreatureIsImpaledByPlayersArrow(wrath.Creature, fool),
                "Arrow remains impaled while Wrath stays centered on its 2nd turn.");

            // Turn 2 of Wrath: rotates 90 degrees to Lament -> Wrath becomes unhittable/untargetable, arrow MUST dislodge!
            await RunTowerEnemyRoundAsync(b);
            Assert(ctrl.ActiveFace == lament && !wrath.Creature.IsHittable, "Tower rotated from Wrath to Lament.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && !ArrowCmd.CreatureIsImpaledByArrow(wrath.Creature),
                $"Arrow remained trapped on back-of-tower Wrath after rotation (available={ArrowCmd.GetAvailableArrowCount(fool)}, status={fool.PlayerCombatState!.Arrows[0].Status}).");

            // Also verify mid-move combo whip rotation dislodges immediately:
            var shot2 = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot2, lament.Creature, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool), "Lament impaled before combo whip rotation.");
            var bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            await ctrl.ForceRotateToForTestAsync(bastion, comboWhip: true);
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && !ArrowCmd.CreatureIsImpaledByArrow(lament.Creature),
                "Combo-whip rotation must also dislodge impaled arrows from the departing face.");
        });

        // 6) Human Face Tower: lethal TrickShot / FoolishShot / EpicRetrieval shatters active face + rendered VFX visibility.
        await RunCase("HumanFaceTower.LethalShotShattersFaceAndKeepsRetrieveVfxVisible", async () =>
        {
            try { Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly); } catch (ArgumentException) { }
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), "fool-tower-shatter");
            Player fool = b.Players[0];
            var wrath = b.Monster<ThingsHumanFaceTowerWrath>();
            var lament = b.Monster<ThingsHumanFaceTowerLament>();
            var bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            var ctrl = wrath.Controller!;

            await CreatureCmd.Damage(Choice, wrath.Creature, wrath.Creature.CurrentHp - 8, ValueProp.Unpowered, fool.Creature);
            int handBefore = PileType.Hand.GetPile(fool).Cards.Count;
            var trickShot = b.State.CreateCard<TrickShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(trickShot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, trickShot, wrath.Creature, skipCardPileVisuals: true);

            Assert(wrath.Creature.IsDead && ctrl.ActiveFace == lament, "TrickShot shattered Wrath and rotated to Lament.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool), "Arrow dislodged when shattering Wrath with TrickShot.");
            Assert(PileType.Hand.GetPile(fool).Cards.Count >= handBefore + 1, "v0.114.0 TrickShot drew cards after shattering Wrath.");

            // Impale Lament, then shatter Lament with EpicRetrieval -> must grant +3 energy!
            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, lament.Creature, skipCardPileVisuals: true);
            await CreatureCmd.Damage(Choice, lament.Creature, lament.Creature.CurrentHp - 10, ValueProp.Unpowered, fool.Creature);

            int energyBefore = fool.PlayerCombatState!.Energy;
            var epic = b.State.CreateCard<EpicRetrieval>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(epic, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, epic, lament.Creature, skipCardPileVisuals: true);
            Assert(lament.Creature.IsDead && ctrl.ActiveFace == bastion, "EpicRetrieval shattered Lament.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool) && fool.PlayerCombatState.Energy == energyBefore + 3,
                "EpicRetrieval shattering an impaled Tower face must dislodge the arrow and grant +3 energy.");

            // Mount NCombatRoom + NCreature for Bastion to verify SetTowerFaceNodeActive(false) does NOT hide active NImpaledArrowVfx!
            var roomHarmony = new Harmony($"DepthsProbe.FoolArrowRoom.{Guid.NewGuid():N}");
            roomHarmony.Patch(
                AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            roomHarmony.Patch(
                AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.UpdateCreatureNavigation)),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            var dummyRoom = new NCombatRoom();
            var bastionNode = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
            typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(bastionNode, bastion.Creature);
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(bastionNode, bastion.CreateVisuals());
            AddChild(bastionNode);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var nodesList = (List<NCreature>)typeof(NCombatRoom).GetField("_creatureNodes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dummyRoom)!;
            nodesList.Add(bastionNode);
            _foolArrowProbeRoom = dummyRoom;
            roomHarmony.Patch(
                AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(FoolArrowRoomGetter)));

            var syntheticArrowVfx = new Node2D { Name = "ImpaledArrowVfx" };
            bastionNode.Visuals.AddChild(syntheticArrowVfx);
            // Note: in things_human_face_tower_face.tscn, Visuals itself has visible=false by default!
            // Look! If Visuals has visible=false in things_human_face_tower_face.tscn, ANY NImpaledArrowVfx added to Visuals is ALSO hidden EVEN WHEN ACTIVE!!
            try
            {
                Sts2VersionSeam.SetTowerFaceNodeActive(bastion.Creature, true);
                Assert(syntheticArrowVfx.IsVisibleInTree(),
                    "Active HumanFaceTower face has Visuals.Visible == false, hiding NImpaledArrowVfx even while the face is centered!");

                Sts2VersionSeam.SetTowerFaceNodeActive(bastion.Creature, false);
                Assert(syntheticArrowVfx.IsVisibleInTree(),
                    "SetTowerFaceNodeActive(false) hid NCreature/Visuals while NImpaledArrowVfx was still animating its retrieve arc!");
            }
            finally
            {
                _foolArrowProbeRoom = null;
                roomHarmony.UnpatchAll(roomHarmony.Id);
                if (GodotObject.IsInstanceValid(bastionNode))
                    bastionNode.Free();
                if (GodotObject.IsInstanceValid(dummyRoom))
                    dummyRoom.Free();
            }
        });

        // 7) Gravetide Slug Corpse devoured while impaled via RemoveCreatureWithoutDeathOrEscape.
        await RunCase("GravetideSlugCorpse.DevouringImpaledCorpseDislodgesArrow", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsGravetideSlugBossEncounter>(), "fool-gravetide-corpse");
            Player fool = b.Players[0];
            var boss = b.Monster<ThingsGravetideSlug>();
            var corpseModel = (ThingsGravetideSlugCorpse)ModelDb.Monster<ThingsGravetideSlugCorpse>().ToMutable();
            Creature corpse = b.State.CreateCreature(corpseModel, CombatSide.Enemy, "minion_left");
            b.State.AddCreature(corpse);
            corpseModel.SetUpForCombat();
            corpse.SetMaxHpInternal(30);
            corpse.SetCurrentHpInternal(30);
            await corpseModel.AfterAddedToRoom();
            corpseModel.RollMove(b.Players.Select(p => p.Creature));

            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, corpse, skipCardPileVisuals: true);
            Assert(!ArrowCmd.PlayerHasAnyArrows(fool) && ArrowCmd.CreatureIsImpaledByPlayersArrow(corpse, fool),
                "Gravetide corpse impaled by non-lethal FoolishShot.");

            var digestion = boss.Creature.GetPower<ThingsGravetideDigestionPower>()!;
            b.State.CurrentSide = CombatSide.Enemy;
            await digestion.AfterSideTurnEnd(Choice, CombatSide.Enemy, b.Enemies);

            Assert(!b.State.ContainsCreature(corpse), "Gravetide boss devoured the corpse.");
            Assert(ArrowCmd.PlayerHasAnyArrows(fool),
                $"Devouring an impaled Gravetide corpse permanently ate Fool's arrow (status={fool.PlayerCombatState!.Arrows[0].Status}).");
        });

        // 8) Human Face Column sanity check + Quiver 2-arrow & QuietusPower (DestroyAfterImpaled) contracts.
        await RunCase("HumanFaceColumnAndMultiArrowQuietusContracts", async () =>
        {
            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>(), "fool-column");
            Player fool = b.Players[0];
            ThingsHumanFaceColumn bottom = b.State.Enemies
                .Select(c => c.Monster)
                .OfType<ThingsHumanFaceColumn>()
                .Single(c => c.Level == 0);

            await CreatureCmd.Damage(Choice, bottom.Creature, bottom.Creature.CurrentHp - 8, ValueProp.Unpowered, fool.Creature);
            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, bottom.Creature, skipCardPileVisuals: true);

            Assert(bottom.Creature.IsDead && ArrowCmd.PlayerHasAnyArrows(fool),
                "Shattering a HumanFaceColumn disc with FoolishShot returns the arrow immediately.");
            DeactivateSyntheticCombat();

            // Quiver 2-arrow: impale CaveGodHand with arrow #1, then break CaveGodHand with arrow #2 -> both arrows return!
            var cg = await CreateFoolBattle(ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "fool-quiver-qui");
            Player fool2 = cg.Players[0];
            ArrowCmd.GainArrowSlot(fool2);
            Assert(ArrowCmd.GetAvailableArrowCount(fool2) == 2, "Quiver grants 2 available arrows.");
            var left = cg.Monster<ThingsCaveGodLeftHand>();
            var right = cg.Monster<ThingsCaveGodRightHand>();

            var s1 = cg.State.CreateCard<FoolishShot>(fool2);
            await CardPileCmd.AddGeneratedCardToCombat(s1, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, s1, left.Creature, skipCardPileVisuals: true);
            Assert(ArrowCmd.GetAvailableArrowCount(fool2) == 1 && ArrowCmd.CreatureIsImpaledByPlayersArrow(left.Creature, fool2),
                "First arrow impaled in CaveGodLeftHand.");

            await CreatureCmd.Damage(Choice, left.Creature, left.Creature.CurrentHp - 8, ValueProp.Unpowered, fool2.Creature);
            var s2 = cg.State.CreateCard<FoolishShot>(fool2);
            await CardPileCmd.AddGeneratedCardToCombat(s2, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, s2, left.Creature, skipCardPileVisuals: true);
            Assert(left.IsDown && ArrowCmd.GetAvailableArrowCount(fool2) == 2 && !ArrowCmd.CreatureIsImpaledByArrow(left.Creature),
                $"Breaking an already-impaled CaveGodHand with a 2nd arrow must return both arrows (available={ArrowCmd.GetAvailableArrowCount(fool2)}).");

            // v0.114.0 reworked QuietusPower into a multiplicative arrow-damage buff and removed ArrowStatus.Destroyed.
            // Verify breaking CaveGodRightHand with QuietusPower active retrieves the arrow cleanly without MissingMethodException.
            await PowerCmd.Apply<QuietusPower>(Choice, fool2.Creature, 2m, fool2.Creature, null);
            await CreatureCmd.Damage(Choice, right.Creature, right.Creature.CurrentHp - 8, ValueProp.Unpowered, fool2.Creature);
            var s3 = cg.State.CreateCard<FoolishShot>(fool2);
            await CardPileCmd.AddGeneratedCardToCombat(s3, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, s3, right.Creature, skipCardPileVisuals: true);
            Assert(right.IsDown && ArrowCmd.GetAvailableArrowCount(fool2) == 2 && !ArrowCmd.CreatureIsImpaledByArrow(right.Creature),
                $"v0.114.0 breaking CaveGodRightHand with QuietusPower must return the arrow cleanly (available={ArrowCmd.GetAvailableArrowCount(fool2)}).");
        });

        // 9) v0.113.1 parity: NImpaledArrowVfx Spine bone anchor + BeforeAnimPlayedInCompendium on HumanFaceColumn.
        await RunCase("V1131SpineArrowAnchorAndCompendiumHook", async () =>
        {
            var extStatus = GDExtensionManager.LoadExtension(Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
            Assert(extStatus is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, "Spine extension loaded.");

            var b = await CreateFoolBattle(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>(), "fool-v1131-anchor");
            Player fool = b.Players[0];
            ThingsHumanFaceColumn col = b.State.Enemies
                .Select(c => c.Monster)
                .OfType<ThingsHumanFaceColumn>()
                .Single(c => c.Level == 0);

            var roomHarmony = new Harmony($"DepthsProbe.V1131ArrowAnchor.{Guid.NewGuid():N}");
            roomHarmony.Patch(
                AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            roomHarmony.Patch(
                AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation"),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            roomHarmony.Patch(
                AccessTools.Method(typeof(NPowerContainer), "Add"),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
            var dummyRoom = new NCombatRoom();
            var colNode = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
            typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(colNode, col.Creature);
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(colNode, col.CreateVisuals());
            AddChild(colNode);
            for (int i = 0; i < 3; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var shot = b.State.CreateCard<FoolishShot>(fool);
            await CardPileCmd.AddGeneratedCardToCombat(shot, PileType.Hand, null);
            await CardCmd.AutoPlay(Choice, shot, col.Creature, skipCardPileVisuals: true);
            ArrowSlot arrowSlot = fool.PlayerCombatState!.Arrows[0];

            var nodesList = (List<NCreature>)typeof(NCombatRoom).GetField("_creatureNodes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dummyRoom)!;
            nodesList.Add(colNode);
            _foolArrowProbeRoom = dummyRoom;
            roomHarmony.Patch(
                AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)),
                prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(FoolArrowRoomGetter)));

            try
            {
                // Verify BeforeAnimPlayedInCompendium("die") pauses Track 1 and ("idle_loop") restores state without throwing.
                col.BeforeAnimPlayedInCompendium("die");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                col.BeforeAnimPlayedInCompendium("idle_loop");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                // Instantiate live NImpaledArrowVfx and verify AfterImpaledArrowReady attaches NThingsArrowBoneAnchor.
                var vfx = GD.Load<PackedScene>("res://scenes/vfx/impaled_arrow_vfx.tscn").Instantiate<MegaCrit.Sts2.Core.Nodes.Vfx.NImpaledArrowVfx>();
                typeof(MegaCrit.Sts2.Core.Nodes.Vfx.NImpaledArrowVfx).GetField("_creature", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vfx, col.Creature);
                typeof(MegaCrit.Sts2.Core.Nodes.Vfx.NImpaledArrowVfx).GetField("_arrowSlot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vfx, arrowSlot);
                colNode.Visuals.AddChild(vfx);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                GodotObject? frontSlot = vfx.Get(MegaCrit.Sts2.Core.Nodes.Vfx.NImpaledArrowVfx.PropertyName._frontSlotNode).AsGodotObject();
                GodotObject? backSlot = vfx.Get(MegaCrit.Sts2.Core.Nodes.Vfx.NImpaledArrowVfx.PropertyName._backSlotNode).AsGodotObject();
                Assert(frontSlot is STS2_Things.Visuals.NThingsArrowBoneAnchor && backSlot is STS2_Things.Visuals.NThingsArrowBoneAnchor,
                    $"Expected NImpaledArrowVfx to attach NThingsArrowBoneAnchor on custom Spine monster, got front={frontSlot?.GetType().Name}, back={backSlot?.GetType().Name}.");

                // Trigger native arrow retrieval and verify RetrieveAndFree reparents cleanly with zero errors.
                await ArrowCmd.DislodgeArrow(col.Creature, fool, Choice, null);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Assert(ArrowCmd.PlayerHasAnyArrows(fool), "Arrow retrieved cleanly from NThingsArrowBoneAnchor.");
            }
            finally
            {
                _foolArrowProbeRoom = null;
                roomHarmony.UnpatchAll(roomHarmony.Id);
                if (GodotObject.IsInstanceValid(colNode))
                    colNode.Free();
                if (GodotObject.IsInstanceValid(dummyRoom))
                    dummyRoom.Free();
            }
        });

        Assert(failures.Count == 0, $"Fool arrow regression failures ({failures.Count}):\n" + string.Join("\n", failures));
#endif
    }

#if STS2_V113_1
    private static NCombatRoom? _foolArrowProbeRoom;

    private static bool FoolArrowRoomGetter(ref NCombatRoom? __result)
    {
        if (_foolArrowProbeRoom == null)
            return true;
        __result = _foolArrowProbeRoom;
        return false;
    }

    private static async Task<Battle> CreateFoolBattle(EncounterModel canonical, string seed)
    {
        DeactivateSyntheticCombat();
        var party = new List<Player> { Player.CreateForNewRun<Fool>(UnlockState.all, 1UL) };
        var run = NativeRun(
            party,
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<ThingsDepths>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            GameMode.Standard,
            0,
            seed);
        run.CurrentActIndex = 1;
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(RunManager.Instance, run);
        typeof(RunManager).GetProperty("AscensionManager")!.SetValue(RunManager.Instance, NativeAscension(0));
        typeof(RunManager).GetProperty("NetService")!.SetValue(RunManager.Instance, new NetSingleplayerGameService());

        var encounter = canonical.ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);
        foreach (Player player in run.Players)
        {
            player.ResetCombatState();
            room.CombatState.AddPlayer(player);
            player.PopulateCombatState(run.Rng.Shuffle, room.CombatState);
            player.PlayerCombatState!.Energy = 99;
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
        foreach (Creature enemy in room.CombatState.Enemies.ToList())
            await enemy.Monster!.AfterAddedToRoom();
        await Hook.BeforeCombatStart(run, room.CombatState);
        foreach (Creature enemy in room.CombatState.Enemies.Where(c => c.IsAlive).ToList())
            enemy.Monster!.RollMove(run.Players.Select(p => p.Creature));
        await Hook.AfterSideTurnStart(
            room.CombatState,
            CombatSide.Player,
            room.CombatState.Allies.Concat(room.CombatState.Enemies).Where(c => c.IsAlive).ToArray());
        return new Battle(run, room, encounter);
    }
#endif
}
