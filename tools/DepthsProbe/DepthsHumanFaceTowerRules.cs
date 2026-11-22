using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Intents;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private static void ClearCreatureBlock(Creature creature)
    {
        if (creature.Block > 0)
            creature.LoseBlockInternal(creature.Block);
    }

    private static async Task<Battle> TowerBattle(int players = 1, int ascension = 0, string seed = "human-face-tower")
    {
        var b = await StrongBattle(ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), players, ascension, seed);
        foreach (var c in b.State.Enemies)
            await c.Monster!.AfterAddedToRoom();
        foreach (var c in b.State.Enemies)
            await c.Monster!.BeforeCombatStart();
        foreach (var c in b.State.Enemies)
            c.Monster!.RollMove(b.Players.Select(p => p.Creature));
        foreach (var p in b.Players)
        {
            p.Creature.SetMaxHpInternal(10000);
            p.Creature.SetCurrentHpInternal(10000);
        }

        return b;
    }

    private static async Task RunTowerEnemyRoundAsync(Battle b)
    {
        b.State.CurrentSide = CombatSide.Enemy;
        Creature[] participants = b.State.Enemies.Where(c => c.IsAlive).ToArray();
        await Hook.AfterSideTurnStart(b.State, CombatSide.Enemy, participants);
        foreach (Creature c in participants)
        {
            await c.AfterTurnStart(CombatSide.Enemy);
        }

        foreach (Creature c in participants)
        {
            if (c.IsAlive)
                await c.Monster!.PerformMove();
        }

        await SideEnding(b, CombatSide.Enemy, b.State.Enemies.Where(c => c.IsAlive).ToArray());
        await SideEnded(b, CombatSide.Enemy, b.State.Enemies.Where(c => c.IsAlive).ToArray());
        b.State.CurrentSide = CombatSide.Player;
        foreach (Creature c in b.State.Enemies.Where(c => c.IsAlive))
            c.Monster!.RollMove(b.Players.Select(p => p.Creature));
    }

    private async Task VerifyHumanFaceTowerRules()
    {
        foreach (int players in new[] { 1, 4 })
        foreach (int asc in new[] { 0, 20 })
        {
            // 1) Full 8-turn 4-face rotation cycle + off-center immunity + random-target exclusion + Turn 8 Climax.
            var b = await TowerBattle(players, asc, $"tower-cycle-{players}-{asc}");
            var wrath = b.Monster<ThingsHumanFaceTowerWrath>();
            var lament = b.Monster<ThingsHumanFaceTowerLament>();
            var bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            var mockery = b.Monster<ThingsHumanFaceTowerMockery>();
            var ctrl = wrath.Controller!;

            Assert(ctrl.LivingCount == 4 && ctrl.ActiveFace == wrath, "Wrath starts centered.");
            if (players == 1)
            {
                int rawHpSum = wrath.Creature.MaxHp + lament.Creature.MaxHp + bastion.Creature.MaxHp + mockery.Creature.MaxHp;
                Assert(wrath.Creature.MaxHp == (asc == 0 ? 99 : 108) &&
                       lament.Creature.MaxHp == (asc == 0 ? 104 : 113) &&
                       bastion.Creature.MaxHp == (asc == 0 ? 112 : 121) &&
                       mockery.Creature.MaxHp == (asc == 0 ? 99 : 108) &&
                       rawHpSum == (asc == 0 ? 414 : 450),
                    $"Act 2 Boss raw HP sum should be 414(A0)/450(A20), got {rawHpSum}.");
            }
            Assert(wrath.IsActiveFace && !lament.IsActiveFace && !bastion.IsActiveFace && !mockery.IsActiveFace,
                "At most one stone face is active.");
            Assert(wrath.Creature.IsHittable && !lament.Creature.IsHittable && !bastion.Creature.IsHittable && !mockery.Creature.IsHittable,
                "Off-center faces are non-hittable.");

            // Off-center direct & AoE immunity + random target exclusion.
            int lamentHpBefore = lament.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, lament.Creature, 50, ValueProp.Move, b.Players[0].Creature);
            Assert(lament.Creature.CurrentHp == lamentHpBefore, "Off-center face takes 0 damage.");

            int wrathHpBefore = wrath.Creature.CurrentHp;
            for (int r = 0; r < 4; r++)
            {
                var boomerang = b.State.CreateCard<SwordBoomerang>(b.Players[0]);
                await CardPileCmd.AddGeneratedCardToCombat(boomerang, PileType.Hand, null);
                await CardCmd.AutoPlay(Choice, boomerang, null, skipCardPileVisuals: true);
            }
            Assert(wrath.Creature.CurrentHp == wrathHpBefore - 36 &&
                   lament.Creature.CurrentHp == lamentHpBefore &&
                   bastion.Creature.CurrentHp == bastion.Creature.MaxHp &&
                   mockery.Creature.CurrentHp == mockery.Creature.MaxHp,
                "Random player attacks hit only the centered active face.");

            // Wrath passive: blocked attack gives 0 Strength; unblocked attack gives +2 Strength.
            await CreatureCmd.GainBlock(b.Players[0].Creature, 999, ValueProp.Unpowered, null);
            foreach (var p in b.Players.Skip(1))
                await CreatureCmd.GainBlock(p.Creature, 999, ValueProp.Unpowered, null);
            Assert(wrath.NextMove.Id == "HEAVY_MALLET_MOVE", "Wrath opens with Heavy Mallet.");
            await RunTowerEnemyRoundAsync(b);
            Assert((wrath.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == 0,
                "Fully blocked Wrath attack grants no Strength.");
            Assert(ctrl.ActiveFace == wrath && ctrl.StayTurnIndex == 1 && wrath.NextMove.Id == "DOUBLE_JAW_MOVE",
                "Wrath stays for Turn 2 and rolls Double Jaw.");

            foreach (var p in b.Players)
                ClearCreatureBlock(p.Creature);
            await RunTowerEnemyRoundAsync(b);
            Assert(wrath.Creature.GetPower<StrengthPower>()?.Amount == 2,
                "Unblocked Wrath attack grants +2 Strength.");
            Assert(ctrl.ActiveFace == lament && ctrl.StayTurnIndex == 0 && lament.NextMove.Id == "WEATHERED_WAIL_MOVE",
                "After 2 turns, tower rotates 90° from Wrath to Lament.");

            // Round 3-4: Lament adds 2 ThingsLamentTear to Draw + 2 to Hand + Weak; holding Tears at Turn End grants permanent +1 Lamentation stack per card.
            int drawTearsBefore = PileType.Draw.GetPile(b.Players[0]).Cards.Count(c => c is ThingsLamentTear);
            int handTearsBefore = PileType.Hand.GetPile(b.Players[0]).Cards.Count(c => c is ThingsLamentTear);
            await RunTowerEnemyRoundAsync(b);
            int drawTearsAfter = PileType.Draw.GetPile(b.Players[0]).Cards.Count(c => c is ThingsLamentTear);
            int handTearsAfter = PileType.Hand.GetPile(b.Players[0]).Cards.Count(c => c is ThingsLamentTear);
            Assert(drawTearsAfter - drawTearsBefore == 2 &&
                   handTearsAfter - handTearsBefore == 2 &&
                   b.Players[0].Creature.HasPower<WeakPower>(),
                "Lament Weathered Wail adds 2 Lament Tears to Draw, 2 to Hand, and applies Weak.");
            Assert(ctrl.ActiveFace == lament && lament.NextMove.Id == "BLOOD_RAIN_MOVE", "Lament Turn 2 rolls Blood Rain.");

            b.State.CurrentSide = CombatSide.Player;
            var p0Tears = PileType.Hand.GetPile(b.Players[0]).Cards.OfType<ThingsLamentTear>().ToArray();
            Assert(p0Tears[0].EnergyCost.Canonical == 1 && p0Tears[0].Keywords.Contains(CardKeyword.Exhaust),
                "Lament Tear costs 1 Energy and has Exhaust.");
            await CardCmd.AutoPlay(Choice, p0Tears[0], null, skipCardPileVisuals: true);
            Assert(PileType.Exhaust.GetPile(b.Players[0]).Cards.Contains(p0Tears[0]) &&
                   !b.Players[0].Creature.HasPower<ThingsLamentSorrowPower>(),
                "Playing a Lament Tear exhausts it cleanly with 0 Lamentation applied.");

            var extraHandTear = b.State.CreateCard<ThingsLamentTear>(b.Players[0]);
            await CardPileCmd.AddGeneratedCardToCombat(extraHandTear, PileType.Hand, null);
            int p0HpBeforeTurnEnd = b.Players[0].Creature.CurrentHp;
            foreach (var tear in PileType.Hand.GetPile(b.Players[0]).Cards.OfType<ThingsLamentTear>().ToArray())
            {
                await tear.OnTurnEndInHandWrapper(Choice);
            }
            Assert(p0HpBeforeTurnEnd == b.Players[0].Creature.CurrentHp &&
                   b.Players[0].Creature.GetPower<ThingsLamentSorrowPower>()?.Amount == 2 &&
                   !lament.Creature.HasPower<StrengthPower>() &&
                   !lament.Creature.HasPower<PlatingPower>(),
                "Holding 2 Lament Tears at Turn End grants +2 permanent Lamentation on player without direct damage or buffing Lament Strength/Plating.");
            int bloodRainIntent = lament.NextMove.Intents.OfType<MultiAttackIntent>().Single()
                .GetTotalDamage([b.Players[0].Creature], lament.Creature);
            Assert(bloodRainIntent == (lament.BloodRainDamage + 2) * 3,
                $"Blood Rain intent previews Lamentation +2 per hit ({bloodRainIntent} vs {(lament.BloodRainDamage + 2) * 3}).");

            foreach (var p in b.Players) ClearCreatureBlock(p.Creature);
            int p0HpBeforeRain = b.Players[0].Creature.CurrentHp;
            int p1HpBeforeRain = players > 1 ? b.Players[1].Creature.CurrentHp : 0;
            await RunTowerEnemyRoundAsync(b);
            Assert(p0HpBeforeRain - b.Players[0].Creature.CurrentHp == (lament.BloodRainDamage + 2) * 3,
                $"Blood Rain 3 hits dealt {(lament.BloodRainDamage + 2) * 3} with 2 Lamentation stacks (got {p0HpBeforeRain - b.Players[0].Creature.CurrentHp}).");
            if (players > 1)
            {
                Assert(!b.Players[1].Creature.HasPower<ThingsLamentSorrowPower>() &&
                       p1HpBeforeRain - b.Players[1].Creature.CurrentHp == lament.BloodRainDamage * 3,
                    "In multiplayer, Lamentation only amplifies damage for players who held Lament Tears.");
            }
            Assert(b.Players[0].Creature.GetPower<ThingsLamentSorrowPower>()?.Amount == 2,
                "Lamentation persists after Lament Enemy Turn ends and rotation completes.");
            Assert(ctrl.ActiveFace == bastion && ctrl.StayTurnIndex == 0, "After Turn 4, tower rotates to Bastion.");

            // Off-center check: rotated-away Lament still lets held Lament Tear add Lamentation, and Bastion attacks do NOT trigger Lamentation.
            var offCenterTear = b.State.CreateCard<ThingsLamentTear>(b.Players[0]);
            await CardPileCmd.AddGeneratedCardToCombat(offCenterTear, PileType.Hand, null);
            await offCenterTear.OnTurnEndInHandWrapper(Choice);
            Assert(b.Players[0].Creature.GetPower<ThingsLamentSorrowPower>()?.Amount == 3,
                "While Lament is rotated off-center but still alive, holding a Lament Tear still grants +1 Lamentation.");

            // Round 5-6: Bastion enters bolted with EntrySealBlock, retains Block, unbolts on Block break!
            int expectedEntryBlock = players == 1 ? bastion.EntrySealBlock : (int)(bastion.EntrySealBlock * players * 1.2m);
            Assert(bastion.IsBolted && bastion.EntrySealBlock == (asc == 0 ? 14 : 18) && bastion.Creature.Block == expectedEntryBlock,
                $"Bastion enters center bolted with EntrySealBlock {expectedEntryBlock} ({players}p, A{asc}, got {bastion.Creature.Block}).");
            ClearCreatureBlock(b.Players[0].Creature);
            int hpBeforeBastionPress = b.Players[0].Creature.CurrentHp;
            await RunTowerEnemyRoundAsync(b); // Turn 5: Bulwark Press
            Assert(hpBeforeBastionPress - b.Players[0].Creature.CurrentHp == bastion.BulwarkPressDamage,
                $"Bastion attack does NOT gain Lamentation bonus damage (dealt {hpBeforeBastionPress - b.Players[0].Creature.CurrentHp}, expected {bastion.BulwarkPressDamage}).");
            Assert(ctrl.ActiveFace == bastion && bastion.NextMove.Id == "COLOSSUS_DROP_MOVE",
                "Bastion Turn 2 rolls Colossus Drop.");
            Assert(bastion.ColossusDropDamage == (asc == 0 ? 18 : 21), "Bolted Colossus Drop uses full damage 18(21).");
            await CreatureCmd.Damage(Choice, bastion.Creature, bastion.Creature.Block + 5, ValueProp.Move, b.Players[0].Creature);
            Assert(!bastion.IsBolted && bastion.Creature.GetPower<VulnerablePower>()?.Amount == 2 &&
                   bastion.ColossusDropDamage == (asc == 0 ? 10 : 12),
                "Breaking Bastion Block triggers Unbolted (+2 Vulnerable and reduced Colossus Drop damage 10(12)).");
            string unboltedTip = bastion.Creature.GetPower<ThingsBastionSealPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description;
            Assert(unboltedTip.Contains($"[blue]{bastion.EntrySealBlock}[/blue]") || unboltedTip.Contains($"[blue]{expectedEntryBlock}[/blue]"),
                $"Bastion unbolted tooltip should reflect {bastion.EntrySealBlock} (or scaled {expectedEntryBlock}) EntrySealBlock: {unboltedTip}");
            await RunTowerEnemyRoundAsync(b); // Turn 6 finishes -> rotates to Mockery!
            Assert(ctrl.ActiveFace == mockery && ctrl.StayTurnIndex == 0 && mockery.NextMove.Id == "SNEER_STAB_MOVE",
                "After Turn 6, tower rotates to Mockery.");

            // Round 7: Mockery card-type echo check (Attack -> Skill = 0; Skill -> Skill = +Block & +1 permanent Strength).
            b.State.CurrentSide = CombatSide.Player;
            var strike = b.State.CreateCard<StrikeIronclad>(b.Players[0]);
            var defend1 = b.State.CreateCard<DefendIronclad>(b.Players[0]);
            var defend2 = b.State.CreateCard<DefendIronclad>(b.Players[0]);
            await CardPileCmd.AddGeneratedCardsToCombat([strike, defend1, defend2], PileType.Hand, b.Players[0]);
            await CardCmd.AutoPlay(Choice, strike, mockery.Creature, skipCardPileVisuals: true);
            await CardCmd.AutoPlay(Choice, defend1, null, skipCardPileVisuals: true);
            Assert((mockery.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == 0 && mockery.Creature.Block == 0,
                "Alternating Attack -> Skill does not trigger Mockery.");
            await CardCmd.AutoPlay(Choice, defend2, null, skipCardPileVisuals: true);
            Assert(mockery.Creature.GetPower<StrengthPower>()?.Amount == 1 && mockery.Creature.Block >= mockery.TauntBlock,
                "Consecutive Skill -> Skill triggers Mockery Block + 1 permanent Strength.");
            await SideEnded(b, CombatSide.Player, b.Players.Select(p => p.Creature).ToArray());

            await RunTowerEnemyRoundAsync(b); // Turn 7 finishes -> Permanent Strength persists, Turn 8 rolls CYCLE_CLIMAX_MOVE!
            Assert(mockery.Creature.GetPower<StrengthPower>()?.Amount == 1,
                "Mockery retains permanent Strength after Enemy Turn ends.");
            Assert(ctrl.ActiveFace == mockery && ctrl.StayTurnIndex == 1 && mockery.NextMove.Id == "CYCLE_CLIMAX_MOVE",
                "Turn 8 (full 4-face cycle completion) rolls CYCLE_CLIMAX_MOVE!");

            Assert(b.Players[0].Creature.GetPower<VulnerablePower>()?.Amount == 1,
                "Mockery Turn 7 Sneer Stab applied 1 Vulnerable ahead of Turn 8.");
            int wrathStrBeforeClimax = wrath.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            int lamentStrBeforeClimax = lament.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            int mockeryStrBeforeClimax = mockery.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            int p0HpBeforeClimax = b.Players[0].Creature.CurrentHp;
            foreach (var p in b.Players) ClearCreatureBlock(p.Creature);
            await RunTowerEnemyRoundAsync(b); // Turn 8 executes Climax + rotates back to Wrath!
            int expectedClimaxGain = asc == 0 ? 2 : 3;
            int expectedVulnerableClimaxDamage = (int)((mockery.ClimaxDamage + mockeryStrBeforeClimax) * 1.5m);
            Assert(p0HpBeforeClimax - b.Players[0].Creature.CurrentHp == expectedVulnerableClimaxDamage,
                $"Full-cycle climax dealt {p0HpBeforeClimax - b.Players[0].Creature.CurrentHp}, expected {expectedVulnerableClimaxDamage}.");
            Assert(!b.Players[0].Creature.HasPower<VulnerablePower>(),
                "Turn 7 Vulnerable expires after Turn 8 Enemy Turn.");
            Assert((wrath.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == wrathStrBeforeClimax + expectedClimaxGain &&
                   lament.Creature.GetPower<StrengthPower>()?.Amount == lamentStrBeforeClimax + expectedClimaxGain &&
                   bastion.Creature.GetPower<StrengthPower>()?.Amount == expectedClimaxGain &&
                   mockery.Creature.GetPower<StrengthPower>()?.Amount == mockeryStrBeforeClimax + expectedClimaxGain,
                "Full-cycle climax buffs all 4 living faces (including rotated-away faces).");
            Assert(ctrl.ActiveFace == wrath && ctrl.CycleActiveTurns == 0 && ctrl.StayTurnIndex == 0,
                "After Turn 8 Climax, tower wraps back to Wrath for the next cycle.");
            DeactivateSyntheticCombat();

            // 2) Mid-turn shatter -> emergency whip-rotate + 1-turn Stun -> Post-break Rotational 2-Hit Combo -> Final Stand!
            b = await TowerBattle(players, asc, $"tower-combo-{players}-{asc}");
            wrath = b.Monster<ThingsHumanFaceTowerWrath>();
            lament = b.Monster<ThingsHumanFaceTowerLament>();
            bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            mockery = b.Monster<ThingsHumanFaceTowerMockery>();
            ctrl = wrath.Controller!;

            // Shatter Wrath on Player Turn -> Lament emergency-rotates in and is STUNNED!
            b.State.CurrentSide = CombatSide.Player;
            await CreatureCmd.Damage(Choice, wrath.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(!wrath.Creature.IsAlive && ctrl.LivingCount == 3 && ctrl.ActiveFace == lament,
                "Shattering Wrath immediately rotates Lament into center.");
            Assert(lament.NextMove.Id == "STUNNED", "Emergency-rotated Lament is stunned for the upcoming Enemy Turn.");

            int hpBeforeStunTurn = b.Players[0].Creature.CurrentHp;
            await RunTowerEnemyRoundAsync(b);
            Assert(b.Players[0].Creature.CurrentHp == hpBeforeStunTurn && ctrl.ActiveFace == lament &&
                   ctrl.StayTurnIndex == 0 && lament.NextMove.Id == "WEATHERED_WAIL_MOVE",
                "Stunned entry turn deals 0 damage and preserves Lament's full 2-turn stay starting next round.");

            await RunTowerEnemyRoundAsync(b); // Lament Turn 1 -> Turn 2 should now be COMBO_ROTATE_MOVE!
            Assert(ctrl.ActiveFace == lament && ctrl.StayTurnIndex == 1 && lament.NextMove.Id == "COMBO_ROTATE_MOVE",
                "Once 1 face is defeated, remaining faces roll COMBO_ROTATE_MOVE on Turn 2.");
            Assert(lament.NextMove.Intents.Count == 2 && lament.NextMove.Intents[1] is NextFaceComboAttackIntent,
                "COMBO_ROTATE_MOVE exposes dual attack intents including NextFaceComboAttackIntent.");

            // Clear player Block before combo damage check.
            foreach (var p in b.Players) ClearCreatureBlock(p.Creature);
            int hpBeforeCombo = b.Players[0].Creature.CurrentHp;
            await RunTowerEnemyRoundAsync(b); // Lament strikes -> whip-rotates to Bastion -> Bastion follows up!
            int expectedComboTotal = lament.ComboStarterDamage + bastion.ComboFollowUpDamage;
            Assert(hpBeforeCombo - b.Players[0].Creature.CurrentHp == expectedComboTotal,
                $"Rotational 2-hit combo dealt {hpBeforeCombo - b.Players[0].Creature.CurrentHp}, expected {expectedComboTotal}.");
            Assert(ctrl.ActiveFace == bastion && ctrl.StayTurnIndex == 0 && bastion.NextMove.Id == "BULWARK_PRESS_MOVE",
                "After combo follow-up, Bastion stays centered for its Turn 1 move.");

            // Shatter Bastion AND Lament -> only Mockery remains (Final Stand: LivingCount == 1, no rotation!).
            b.State.CurrentSide = CombatSide.Player;
            await CreatureCmd.Damage(Choice, bastion.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(ctrl.ActiveFace == mockery && mockery.NextMove.Id == "STUNNED", "Mockery emergency-rotates in stunned.");
            await RunTowerEnemyRoundAsync(b); // Mockery recovers from stun -> Turn 1
            await RunTowerEnemyRoundAsync(b); // Mockery Turn 1 -> Turn 2 should combo into Lament (2 alive)
            Assert(mockery.NextMove.Id == "COMBO_ROTATE_MOVE" && mockery.PeekComboFollowUpFace() == lament,
                "With 2 faces alive, Mockery skips shattered Wrath/Bastion (270°/180°) and combos into Lament.");

            // Kill Mockery on Player Turn -> Lament is the LAST surviving face (LivingCount == 1)!
            b.State.CurrentSide = CombatSide.Player;
            await CreatureCmd.Damage(Choice, mockery.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(ctrl.LivingCount == 1 && ctrl.ActiveFace == lament && ctrl.TurnsUntilRotation == 0,
                "Last surviving face reports TurnsUntilRotation == 0.");
            await RunTowerEnemyRoundAsync(b); // Recover from stun
            Assert(lament.NextMove.Id == "WEATHERED_WAIL_MOVE", "Final face rolls Turn 1.");
            await RunTowerEnemyRoundAsync(b);
            Assert(ctrl.ActiveFace == lament && lament.NextMove.Id == "BLOOD_RAIN_MOVE",
                "Final face rolls standard Turn 2 (no combo rotate when LivingCount == 1).");
            await RunTowerEnemyRoundAsync(b);
            Assert(ctrl.ActiveFace == lament && lament.NextMove.Id == "WEATHERED_WAIL_MOVE",
                "Final face stays locked in center and loops back to Turn 1.");

            // Kill final face -> encounter defeated!
            b.State.CurrentSide = CombatSide.Player;
            await CreatureCmd.Damage(Choice, lament.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(ctrl.LivingCount == 0 && b.Enemies.Length == 0, "All 4 faces shattered cleanly.");
            DeactivateSyntheticCombat();

            // 2b) Defeating Lament mid-combat strips all player Lamentation stacks and stops held Lament Tears from reapplying.
            b = await TowerBattle(1, asc, $"tower-sorrow-cleanup-{asc}");
            wrath = b.Monster<ThingsHumanFaceTowerWrath>();
            lament = b.Monster<ThingsHumanFaceTowerLament>();
            bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            ctrl = wrath.Controller!;

            b.State.CurrentSide = CombatSide.Player;
            await CreatureCmd.Damage(Choice, wrath.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            var cleanTear1 = b.State.CreateCard<ThingsLamentTear>(b.Players[0]);
            var cleanTear2 = b.State.CreateCard<ThingsLamentTear>(b.Players[0]);
            await CardPileCmd.AddGeneratedCardsToCombat([cleanTear1, cleanTear2], PileType.Hand, b.Players[0]);
            await cleanTear1.OnTurnEndInHandWrapper(Choice);
            Assert(b.Players[0].Creature.GetPower<ThingsLamentSorrowPower>()?.Amount == 1,
                "Held Lament Tear inflicts 1 Lamentation while Lament lives.");
            await CreatureCmd.Damage(Choice, lament.Creature, 99999, ValueProp.Unpowered, b.Players[0].Creature);
            Assert(!lament.Creature.IsAlive && ctrl.ActiveFace == bastion &&
                   !b.Players[0].Creature.HasPower<ThingsLamentSorrowPower>(),
                "Defeating Lament mid-combat immediately strips Lamentation from players while remaining faces fight on.");
            await cleanTear2.OnTurnEndInHandWrapper(Choice);
            Assert(!b.Players[0].Creature.HasPower<ThingsLamentSorrowPower>(),
                "Holding a leftover Lament Tear after Lament dies does not reapply Lamentation.");
            DeactivateSyntheticCombat();
        }

        // 3) Act 2 Boss 8-turn unmitigated attack budget, dedicated Boss BGM & native tooltip parity check.
        var towerEncounter = ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>();
        var towerTheme = ResourceLoader.Load<AudioStreamWav>("res://music/human_face_tower/human_face_tower_boss_theme.wav");
        Assert(towerEncounter.CustomBgm == "res://music/human_face_tower/human_face_tower_boss_theme.wav" &&
               towerTheme != null &&
               towerTheme.LoopMode == AudioStreamWav.LoopModeEnum.Forward &&
               towerTheme.LoopEnd > 5_500_000 &&
               towerTheme.MixRate == 48_000 &&
               towerTheme.Stereo,
            $"Human-Faced Tower dedicated Boss BGM mismatch: bgm={towerEncounter.CustomBgm}, loaded={towerTheme != null}, mode={towerTheme?.LoopMode}, end={towerTheme?.LoopEnd}, rate={towerTheme?.MixRate}, stereo={towerTheme?.Stereo}.");
        foreach (int asc in new[] { 0, 20 })
        {
            var b = await TowerBattle(1, asc, $"tower-act2-budget-{asc}");
            var lament = b.Monster<ThingsHumanFaceTowerLament>();
            var bastion = b.Monster<ThingsHumanFaceTowerBastion>();
            var mockery = b.Monster<ThingsHumanFaceTowerMockery>();
            string sighTip = lament.Creature.GetPower<ThingsLamentSighPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description;
            string mockeryTip = mockery.Creature.GetPower<ThingsMockeryEchoPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description;
            string sorrowDesc = ModelDb.Power<ThingsLamentSorrowPower>().Description.GetFormattedText();
            var sorrowSmartLoc = ModelDb.Power<ThingsLamentSorrowPower>().SmartDescription;
            sorrowSmartLoc.Add("Amount", 1m);
            string sorrowSmart = sorrowSmartLoc.GetFormattedText();
            string tearDesc = b.State.CreateCard<ThingsLamentTear>(b.Players[0]).GetDescriptionForPile(PileType.Hand);
            string canonicalSealDesc = ModelDb.Power<ThingsBastionSealPower>().Description.GetFormattedText();
            Assert(sighTip.Contains("[blue]1[/blue]") &&
                   (sighTip.Contains("哀泪") || sighTip.Contains("Lament Tear")) &&
                   (sighTip.Contains("悲叹") || sighTip.Contains("Lamentation")),
                $"Lament passive tooltip should explain Lament Tear and Lamentation: {sighTip}");
            Assert((sorrowDesc.Contains("悲叹面") || sorrowDesc.Contains("Lament Visage")) &&
                   sorrowSmart.Contains("[blue]1[/blue]") &&
                   (sorrowSmart.Contains("悲叹面") || sorrowSmart.Contains("Lament Visage")),
                $"Lamentation power description should reference Lament Visage and +1 damage in SmartDescription: desc={sorrowDesc}; smart={sorrowSmart}");
            Assert(tearDesc.Contains("1") &&
                   (tearDesc.Contains("悲叹") || tearDesc.Contains("Lamentation")),
                $"Lament Tear card description should show 1 Lamentation: {tearDesc}");
            Assert(mockeryTip.Contains($"[blue]{(asc == 0 ? 4 : 5)}[/blue]") &&
                   mockeryTip.Contains("[blue]1[/blue]") &&
                   !mockeryTip.Contains("临时") &&
                   !mockeryTip.Contains("temporary", StringComparison.OrdinalIgnoreCase),
                $"Mockery tooltip should reflect {(asc == 0 ? 4 : 5)} Block and 1 permanent Strength: {mockeryTip}");
            Assert(canonicalSealDesc.Contains("[blue]14[/blue]"),
                $"Canonical Bastion Seal description should reflect 14 EntrySealBlock: {canonicalSealDesc}");

            int hpStart = b.Players[0].Creature.CurrentHp;
            for (int t = 0; t < 8; t++)
                await RunTowerEnemyRoundAsync(b);
            int total8TurnDamage = hpStart - b.Players[0].Creature.CurrentHp;
            int expected8Turn = asc == 0 ? 101 : 118;
            Assert(total8TurnDamage == expected8Turn,
                $"Human-Faced Tower 8-turn unmitigated damage at A{asc} was {total8TurnDamage}, expected {expected8Turn}.");
            DeactivateSyntheticCombat();
        }

        GD.Print("PASS Human-Faced Tower rules: A0/A20, 1p/4p, Act 2 Boss 414/450 HP & 101/118 8-turn budget, 2-turn rotation, off-center immunity, all 4 passives, Turn-8 Climax, shatter stun, rotational 2-hit combo, and Final Stand.");
    }
}
