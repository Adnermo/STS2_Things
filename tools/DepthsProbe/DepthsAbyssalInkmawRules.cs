using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Godot.Bridge;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;
using STS2_Things.Visuals;

public partial class DepthsProbeNode
{
    private static async Task<Battle> InkmawBattle(int players = 1, int ascension = 0, string seed = "abyssal-inkmaw")
    {
        var b = await StrongBattle(ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(), players, ascension, seed);
        foreach (var c in b.State.Enemies)
            await c.Monster!.AfterAddedToRoom();
        foreach (var c in b.State.Enemies)
            await c.Monster!.BeforeCombatStart();
        await StartInkmawPlayerTurnAsync(b, rollMoves: true);
        foreach (var p in b.Players)
        {
            p.Creature.SetMaxHpInternal(10000);
            p.Creature.SetCurrentHpInternal(10000);
        }

        return b;
    }

    private static async Task StartInkmawPlayerTurnAsync(Battle b, bool rollMoves)
    {
        b.State.CurrentSide = CombatSide.Player;
        Creature[] playerParticipants = b.Players.Select(p => p.Creature).Where(c => c.IsAlive).ToArray();
        await Hook.BeforeSideTurnStart(b.State, CombatSide.Player, playerParticipants);
        if (rollMoves)
        {
            foreach (Creature c in b.State.Enemies.Where(c => c.IsAlive).ToList())
                c.Monster!.RollMove(b.Players.Select(p => p.Creature));
        }
        await Hook.AfterSideTurnStart(b.State, CombatSide.Player, playerParticipants);
    }

    private static async Task EndInkmawPlayerTurnAsync(Battle b)
    {
        Creature[] playerParticipants = b.Players.Select(p => p.Creature).Where(c => c.IsAlive).ToArray();
        await SideEnding(b, CombatSide.Player, playerParticipants);
        foreach (var p in b.Players)
            p.PlayerCombatState?.EndOfTurnCleanup();
        await SideEnded(b, CombatSide.Player, playerParticipants);
    }

    private static async Task RunInkmawFullRoundAsync(Battle b)
    {
        await EndInkmawPlayerTurnAsync(b);

        b.State.CurrentSide = CombatSide.Enemy;
        Creature[] enemyParticipants = b.State.Enemies.Where(c => c.IsAlive).ToArray();
        await Hook.BeforeSideTurnStart(b.State, CombatSide.Enemy, enemyParticipants);
        foreach (Creature c in enemyParticipants)
            await c.AfterTurnStart(CombatSide.Enemy);
        await Hook.AfterSideTurnStart(b.State, CombatSide.Enemy, enemyParticipants);

        foreach (Creature c in enemyParticipants)
        {
            if (c.IsAlive)
                await c.Monster!.PerformMove();
        }

        await SideEnding(b, CombatSide.Enemy, b.State.Enemies.Where(c => c.IsAlive).ToArray());
        foreach (var p in b.Players)
            p.PlayerCombatState?.EndOfTurnCleanup();
        await SideEnded(b, CombatSide.Enemy, b.State.Enemies.Where(c => c.IsAlive).ToArray());

        await StartInkmawPlayerTurnAsync(b, rollMoves: true);
    }

    private async Task VerifyAbyssalInkmawRules()
    {
        foreach (int players in new[] { 1, 4 })
        foreach (int asc in new[] { 0, 20 })
        {
            var b = await InkmawBattle(players, asc, $"inkmaw-rules-{players}-{asc}");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            var left = b.Monster<ThingsAbyssalCrushingTentacle>();
            var right = b.Monster<ThingsAbyssalInkTentacle>();
            var p0 = b.Players[0];

            if (players == 1)
            {
                Assert(boss.Creature.MaxHp == (asc == 0 ? 288 : 316), $"Boss HP mismatch: {boss.Creature.MaxHp}");
                Assert(left.Creature.MaxHp == (asc == 0 ? 52 : 58), $"Left tentacle HP mismatch: {left.Creature.MaxHp}");
                Assert(right.Creature.MaxHp == (asc == 0 ? 43 : 49), $"Right tentacle HP mismatch: {right.Creature.MaxHp}");
            }

            Assert(left.Creature.GetPower<MinionPower>() != null && right.Creature.GetPower<MinionPower>() != null,
                "Both hero tentacles start with MinionPower.");
            Assert(boss.Creature.GetPower<ThingsAbyssalCoverPower>()?.Amount == 25,
                "Boss starts with 25% Tentacle Cover DR while major tentacles are alive.");
            Assert(!left.IsFloorBreachMode && !right.IsFloorBreachMode,
                "Phase 1 tentacles start in water-emergence mode.");

            // Verify 25% powered-attack damage reduction while major tentacles live
            int hpBeforeCoverHit = boss.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice, boss.Creature, 40m, ValueProp.Move, p0.Creature);
            Assert(hpBeforeCoverHit - boss.Creature.CurrentHp == 30,
                $"Living major tentacles should reduce powered attack by 25% (40 -> 30), lost {hpBeforeCoverHit - boss.Creature.CurrentHp}.");

            // === TURN 1: Boss ABYSSAL_UNDERTOW, Left HEAVY_SLAM, Right INK_BOLSTER ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalUndertowId,
                $"Turn 1 Boss must intend ABYSSAL_UNDERTOW, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.HeavySlamId,
                $"Turn 1 Left must intend HEAVY_SLAM, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.InkBolsterId,
                $"Turn 1 Right must intend INK_BOLSTER, got {right.NextMove.Id}.");
            await RunInkmawFullRoundAsync(b);

            // === TURN 2: Boss SUMMON_TENTACLE, Left GUARD_COIL, Right WHIP_LASH ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.SummonTentacleId,
                $"Turn 2 Boss must intend SUMMON_TENTACLE, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.GuardCoilId,
                $"Turn 2 Left must intend GUARD_COIL, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.WhipLashId,
                $"Turn 2 Right must intend WHIP_LASH, got {right.NextMove.Id}.");
            await RunInkmawFullRoundAsync(b);

            // Verify Turn 2 spawned 1 small tentacle and Right WhipLash did NOT apply whole-deck InkBlackout!
            Assert(boss.LivingSmallTentacleCount == 1,
                $"Phase 1 SUMMON_TENTACLE must spawn 1 small tentacle, got {boss.LivingSmallTentacleCount}.");
            Assert(p0.Creature.GetPower<ThingsInkBlackoutPower>() == null,
                "Right tentacle WHIP_LASH must NOT hijack Boss's whole-deck Ink Blackout.");
            var small1 = boss.LivingSmallTentacles[0];
            if (players == 1)
            {
                Assert(small1.Creature.MaxHp == (asc == 0 ? 22 : 25),
                    $"Small tentacle HP mismatch: {small1.Creature.MaxHp}.");
            }
            Assert(boss.Creature.GetPower<ThingsAbyssalCoverPower>()?.Amount == 25,
                "Small tentacle must NOT increase Boss's Tentacle Cover DR beyond 25%.");

            // === TURN 3: Boss TIDAL_CRASH, Left HEAVY_SLAM, Right INK_BOLSTER, Small first move ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.TidalCrashId,
                $"Turn 3 Boss must intend TIDAL_CRASH, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.HeavySlamId,
                $"Turn 3 Left must intend HEAVY_SLAM, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.InkBolsterId,
                $"Turn 3 Right must intend INK_BOLSTER, got {right.NextMove.Id}.");
            await RunInkmawFullRoundAsync(b);

            // === TURN 4: Boss ABYSSAL_PRESSURE (+Block, +1 Strength, +1 Slimed to Discard), Left GUARD_COIL, Right WHIP_LASH ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalPressureId,
                $"Turn 4 Boss must intend ABYSSAL_PRESSURE, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.GuardCoilId,
                $"Turn 4 Left must intend GUARD_COIL, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.WhipLashId,
                $"Turn 4 Right must intend WHIP_LASH, got {right.NextMove.Id}.");
            int slimedBeforeT4 = PileType.Discard.GetPile(p0)!.Cards.Count(c => c is Slimed);
            await RunInkmawFullRoundAsync(b);
            int slimedAfterT4 = PileType.Discard.GetPile(p0)!.Cards.Count(c => c is Slimed);
            Assert(slimedAfterT4 == slimedBeforeT4 + ThingsAbyssalInkmaw.PressureSlimedCount,
                $"Turn 4 ABYSSAL_PRESSURE must add {ThingsAbyssalInkmaw.PressureSlimedCount} Slimed to Discard.");

            // === TURN 5 (PRE-SPRAY CHARGE-UP): Boss INK_CHARGE (Buff + Unknown), ALL living tentacles postpone & insert CHARGE_BUFF! ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.InkChargeId,
                $"Turn 5 Boss must intend INK_CHARGE after 4 prelude turns, got {boss.NextMove.Id}.");
            Assert(boss.NextMove.Intents.Count == 2 &&
                   boss.NextMove.Intents[0] is BuffIntent &&
                   boss.NextMove.Intents[1] is UnknownIntent,
                "Boss INK_CHARGE must display exact BuffIntent (teal arrow) + UnknownIntent (yellow ?).");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.ChargeBuffId,
                $"Turn 5 Left must postpone HEAVY_SLAM and intend CHARGE_BUFF, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.ChargeBuffId,
                $"Turn 5 Right must postpone INK_BOLSTER and intend CHARGE_BUFF, got {right.NextMove.Id}.");
            Assert(small1.NextMove.Id == ThingsAbyssalSmallTentacleBase.ChargeBuffId,
                $"Turn 5 Small Tentacle must postpone its next move and intend CHARGE_BUFF, got {small1.NextMove.Id}.");

            int p0HpBeforeCharge = p0.Creature.CurrentHp;
            int bossStrBeforeCharge = boss.Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            await RunInkmawFullRoundAsync(b);

            // Charge turn deals ZERO damage, Left applied Block+Blur, Right applied TempStrength (+Strength)!
            Assert(p0.Creature.CurrentHp == p0HpBeforeCharge,
                $"Charge turn must deal 0 damage (Boss does nothing, tentacles buff Boss), lost {p0HpBeforeCharge - p0.Creature.CurrentHp}.");
            Assert(boss.Creature.Block >= left.ChargeBossBlock,
                $"Left Crushing CHARGE_BUFF must grant >= {left.ChargeBossBlock} Block to Boss, got {boss.Creature.Block}.");
            Assert(boss.Creature.GetPower<BlurPower>()?.Amount >= 1,
                "Left Crushing CHARGE_BUFF must grant BlurPower to Boss.");
            Assert(boss.Creature.GetPower<ThingsAbyssalTempStrengthPower>()?.Amount == right.ChargeBossTempStrength,
                $"Right Ink CHARGE_BUFF must grant {right.ChargeBossTempStrength} ThingsAbyssalTempStrengthPower to Boss.");
            Assert((boss.Creature.GetPower<StrengthPower>()?.Amount ?? 0) == bossStrBeforeCharge + right.ChargeBossTempStrength,
                "ThingsAbyssalTempStrengthPower must increase Boss StrengthPower immediately for Turn 6 Ink Spray.");

            // === TURN 6 (FULL-SCREEN SWEEPING INK SPRAY): Boss INK_SPRAY, ALL tentacles resume EXACT postponed move! ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.InkSprayId,
                $"Turn 6 Boss must intend INK_SPRAY, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.HeavySlamId,
                $"Turn 6 Left must resume postponed HEAVY_SLAM, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.InkBolsterId,
                $"Turn 6 Right must resume postponed INK_BOLSTER, got {right.NextMove.Id}.");
            Assert(small1.NextMove.Id != ThingsAbyssalSmallTentacleBase.ChargeBuffId &&
                   small1.NextMove.Id != ThingsAbyssalSmallTentacleBase.PostSprayStunId,
                $"Turn 6 Small Tentacle must resume its postponed normal move, got {small1.NextMove.Id}.");

            // Prepare a diverse combat deck on Player 0 BEFORE Turn 4 Ink Spray resolves so Turn 5 activates whole-deck blackout!
            var strikeHand = b.State.CreateCard<StrikeIronclad>(p0);
            var defendDraw = b.State.CreateCard<DefendIronclad>(p0);
            var bashDiscard = b.State.CreateCard<Bash>(p0);
            var exhaustCard = b.State.CreateCard<StrikeIronclad>(p0);
            var unplayableCard = b.State.CreateCard<Wound>(p0);
            await CardPileCmd.AddGeneratedCardToCombat(strikeHand, PileType.Hand, null);
            await CardPileCmd.AddGeneratedCardToCombat(defendDraw, PileType.Draw, null);
            await CardPileCmd.AddGeneratedCardToCombat(bashDiscard, PileType.Discard, null);
            await CardPileCmd.AddGeneratedCardToCombat(exhaustCard, PileType.Exhaust, null);
            await CardPileCmd.AddGeneratedCardToCombat(unplayableCard, PileType.Hand, null);

            await RunInkmawFullRoundAsync(b);

            // Verify Turn 4 temporary buffs expired cleanly!
            Assert(boss.Creature.GetPower<ThingsAbyssalTempStrengthPower>() == null,
                "ThingsAbyssalTempStrengthPower must auto-remove at end of Turn 4 Ink Spray.");
            Assert(boss.Creature.GetPower<ThingsAbyssalTempThornsPower>() == null,
                "ThingsAbyssalTempThornsPower must auto-remove at end of Player Turn 4.");
            Assert(boss.Creature.GetPower<ThingsAbyssalTempWardPower>() == null,
                "ThingsAbyssalTempWardPower must auto-remove at end of Player Turn 4.");
            Assert(boss.Creature.GetPower<VigorPower>() == null,
                "Native VigorPower must auto-consume on Boss's Turn 4 Ink Spray attack.");

            // === TURN 5 (POST-SPRAY ALL-STUN + WHOLE-DECK BLACKOUT TURN!) ===
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.PostSprayStunId && boss.NextMove.Intents[0] is StunIntent,
                $"Turn 5 Boss must be Stunned (POST_SPRAY_STUN), got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.PostSprayStunId && left.NextMove.Intents[0] is StunIntent,
                $"Turn 5 Left must be Stunned, got {left.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.PostSprayStunId && right.NextMove.Intents[0] is StunIntent,
                $"Turn 5 Right must be Stunned, got {right.NextMove.Id}.");
            Assert(small1.NextMove.Id == ThingsAbyssalSmallTentacleBase.PostSprayStunId && small1.NextMove.Intents[0] is StunIntent,
                $"Turn 5 Small Tentacle must be Stunned, got {small1.NextMove.Id}.");

            // Verify Whole-Deck Ink Blackout active across ALL 4 piles (Hand, Draw, Discard, Exhaust) + 0-3 randomized costs!
            var p0Ink = p0.Creature.GetPower<ThingsInkBlackoutPower>();
            Assert(p0Ink != null && p0Ink.IsActiveThisTurn,
                "Turn 5 must activate ThingsInkBlackoutPower for the entire player turn.");
            foreach (CardModel card in new CardModel[] { strikeHand, defendDraw, bashDiscard, exhaustCard, unplayableCard })
            {
                Assert(AbyssalInkBlackout.IsInked(card) && AbyssalInkBlackout.IsVeiled(card),
                    $"Card {card.Id.Entry} in pile {card.Pile?.Type} must be inked on Turn 5.");
            }
            foreach (CardModel eligible in new CardModel[] { strikeHand, defendDraw, bashDiscard, exhaustCard })
            {
                int cost = eligible.EnergyCost.GetWithModifiers(CostModifiers.All);
                Assert(cost >= AbyssalInkBlackout.MinRandomCost && cost <= AbyssalInkBlackout.MaxRandomCostInclusive,
                    $"Inked card {eligible.Id.Entry} randomized cost must be in 0..3, got {cost}.");
            }
            Assert(AbyssalInkBlackout.GetRolledCost(unplayableCard) == null &&
                   unplayableCard.EnergyCost.GetWithModifiers(CostModifiers.All) < 0,
                "Unplayable card must stay unplayable and never consume RNG.");

            // Verify ZERO HoverTip keyword leak on veiled Bash (normally exposes Vulnerable keyword tip!) AND on LanternBlindness!
            List<IHoverTip> inkedTips = bashDiscard.HoverTips.ToList();
            Assert(inkedTips.Count == 1,
                $"Veiled Bash must expose ONLY 1 InkBlackout power HoverTip (no Vulnerable/keyword leaks!), got {inkedTips.Count}.");
            var lanternProbeCard = b.State.CreateCard<Bash>(p0);
            LanternBlindness.Mark(lanternProbeCard, 2);
            List<IHoverTip> lanternTips = lanternProbeCard.HoverTips.ToList();
            Assert(lanternTips.Count == 1,
                $"Lantern-blinded Bash must expose ONLY 1 LanternBlindness power HoverTip (side-keyword leak fixed!), got {lanternTips.Count}.");
            LanternBlindness.Forget(lanternProbeCard);

            // Verify reshuffle persistence: mark bashDiscard as played & simulate reshuffle -> draw -> STILL inked & cost in 0..3!
            AbyssalInkBlackout.MarkPlayedThisTurn(bashDiscard);
            await p0Ink!.AfterCardDrawn(Choice, bashDiscard, fromHandDraw: true);
            Assert(AbyssalInkBlackout.IsInked(bashDiscard) && AbyssalInkBlackout.IsVeiled(bashDiscard),
                "Card reshuffled from Discard and redrawn on Turn 5 must remain inked.");
            int rerolledCost = bashDiscard.EnergyCost.GetWithModifiers(CostModifiers.All);
            Assert(rerolledCost >= 0 && rerolledCost <= 3,
                $"Reshuffled redrawn card cost must stay in 0..3, got {rerolledCost}.");

            // Verify newly generated mid-turn card is IMMEDIATELY inked too!
            var generatedMidTurn = b.State.CreateCard<StrikeIronclad>(p0);
            await CardPileCmd.AddGeneratedCardToCombat(generatedMidTurn, PileType.Hand, null);
            await p0Ink.AfterCardEnteredCombat(generatedMidTurn);
            Assert(AbyssalInkBlackout.IsInked(generatedMidTurn) && AbyssalInkBlackout.IsVeiled(generatedMidTurn),
                "Mid-turn generated card during Turn 5 must immediately be inked.");

            // Verify surgical cost restoration: add an unrelated Bullet-Time-like SetThisTurn(0) on strikeHand,
            // kill small1 (washes 1 hand card cleanly, no Boss recoil), then kill Left Major Tentacle
            // (washes ENTIRE deck across ALL piles + removes InkBlackoutPower + preserves unrelated 0-cost modifier!).
            strikeHand.EnergyCost.SetThisTurn(0);
            ClearCreatureBlock(boss.Creature);
            int hpBeforeSmallKill = boss.Creature.CurrentHp;
            await CreatureCmd.Kill(small1.Creature, force: true);
            Assert(boss.Creature.CurrentHp == hpBeforeSmallKill,
                "Killing small tentacle must NOT deal recoil to Boss.");

            // Test all 3 Small Tentacle variants' distinct Charge-Up temporary Boss buffs directly!
            {
                var spike = (ThingsAbyssalSpikeTentacle)ModelDb.Monster<ThingsAbyssalSpikeTentacle>().ToMutable();
                spike.ConfigureSpawn(isOuterLeft: true, playEmergeOnAdd: false);
                var spikeC = await CreatureCmd.Add(spike, b.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot);
                spike.ArmTurnOverride(AbyssalTentacleTurnOverride.ChargeBuff);
                spike.SyncArmedOverrideImmediate();
                Assert(spike.NextMove.Id == ThingsAbyssalSmallTentacleBase.ChargeBuffId, "Spike armed CHARGE_BUFF.");
                await spike.PerformMove();
                Assert(boss.Creature.GetPower<ThingsAbyssalTempThornsPower>()?.Amount == spike.ChargeBossTempThorns &&
                       (boss.Creature.GetPower<ThornsPower>()?.Amount ?? 0) >= spike.ChargeBossTempThorns,
                    "Spike CHARGE_BUFF must grant TempThorns + native ThornsPower to Boss.");
                await CreatureCmd.Kill(spikeC, force: true);

                var shell = (ThingsAbyssalShellTentacle)ModelDb.Monster<ThingsAbyssalShellTentacle>().ToMutable();
                shell.ConfigureSpawn(isOuterLeft: true, playEmergeOnAdd: false);
                var shellC = await CreatureCmd.Add(shell, b.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot);
                shell.ArmTurnOverride(AbyssalTentacleTurnOverride.ChargeBuff);
                shell.SyncArmedOverrideImmediate();
                await shell.PerformMove();
                Assert(boss.Creature.GetPower<ThingsAbyssalTempWardPower>()?.Amount == shell.ChargeBossTempWardPercent,
                    "Shell CHARGE_BUFF must grant TempWard damage reduction to Boss.");
                await CreatureCmd.Kill(shellC, force: true);

                var gland = (ThingsAbyssalGlandTentacle)ModelDb.Monster<ThingsAbyssalGlandTentacle>().ToMutable();
                gland.ConfigureSpawn(isOuterLeft: false, playEmergeOnAdd: false);
                var glandC = await CreatureCmd.Add(gland, b.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterRightTentacleSlot);
                gland.ArmTurnOverride(AbyssalTentacleTurnOverride.ChargeBuff);
                gland.SyncArmedOverrideImmediate();
                await gland.PerformMove();
                Assert((boss.Creature.GetPower<VigorPower>()?.Amount ?? 0) >= gland.ChargeBossVigor,
                    "Gland CHARGE_BUFF must grant native VigorPower to Boss.");
                await CreatureCmd.Kill(glandC, force: true);
            }

            // Sever Left Major Tentacle on Turn 5 -> washes ALL cards across ALL piles + removes InkBlackoutPower!
            ClearCreatureBlock(boss.Creature);
            int hpBeforeSever = boss.Creature.CurrentHp;
            await CreatureCmd.Kill(left.Creature, force: true);
            Assert(hpBeforeSever - boss.Creature.CurrentHp == ThingsAbyssalCoverPower.SeverRecoilDamage,
                $"Severing major tentacle must deal {ThingsAbyssalCoverPower.SeverRecoilDamage} recoil.");
            Assert(boss.Creature.GetPower<ThingsAbyssalCoverPower>()?.Amount == 25,
                "With 1 major tentacle still alive, Tentacle Cover DR stays at 25%.");
            Assert(p0.Creature.GetPower<ThingsInkBlackoutPower>() == null,
                "Severing major tentacle must remove ThingsInkBlackoutPower.");
            foreach (CardModel card in new CardModel[] { strikeHand, defendDraw, bashDiscard, exhaustCard, generatedMidTurn })
            {
                Assert(!AbyssalInkBlackout.IsVeiled(card),
                    $"Severing major tentacle must wash {card.Id.Entry} in pile {card.Pile?.Type} clean.");
            }
            Assert(strikeHand.EnergyCost.GetWithModifiers(CostModifiers.All) == 0,
                "Surgical wash must preserve unrelated Bullet-Time-style 0-cost modifier on strikeHand.");
            Assert(bashDiscard.EnergyCost.GetWithModifiers(CostModifiers.All) == bashDiscard.EnergyCost.Canonical,
                "Surgical wash must restore bashDiscard's exact canonical energy cost.");

            // === PHASE 2 PRESERVES STUN TURN REWARD ===
            // Drop Boss to <= 50% HP while Stunned on Turn 5 -> must clamp at 50% AND stay Stunned on Turn 5!
            ClearCreatureBlock(boss.Creature);
            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, p0.Creature);
            Assert(boss.Creature.IsAlive && boss.Phase == 1 && boss.Creature.CurrentHp == boss.Phase2HpThreshold,
                $"Phase 1 lethal hit must clamp cleanly at 50% HP ({boss.Phase2HpThreshold}).");
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.PostSprayStunId,
                $"Hitting 50% HP during Turn 5 Stun must preserve Turn 5 Stun reward, got {boss.NextMove.Id}.");

            // Run Turn 5 (Stun turn — 0 damage!) -> Turn 6 Boss queues PHASE2_SURGE, Right resumes WHIP_LASH cleanly!
            int p0HpBeforeStun = p0.Creature.CurrentHp;
            await RunInkmawFullRoundAsync(b);
            Assert(p0.Creature.CurrentHp == p0HpBeforeStun,
                "Stun turn must deal 0 damage from Boss and all stunned tentacles.");
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.Phase2SurgeId,
                $"Turn 6 after deferred Phase 2 trigger must queue PHASE2_SURGE, got {boss.NextMove.Id}.");
            Assert(right.NextMove.Id == ThingsAbyssalInkTentacle.WhipLashId,
                $"Turn 6 Right tentacle must seamlessly resume WHIP_LASH after Stun, got {right.NextMove.Id}.");

            // Execute Phase 2 Surge with 1 missing major + 0 living smalls -> strict cap of 2 total spawns
            // revives Left in floor-breach mode (1) + spawns 1 small tentacle (1), NEVER 3 or 4 at once!
            await RunInkmawFullRoundAsync(b);
            Assert(boss.Phase == 2 && boss.Creature.GetPower<ThingsAbyssalSurgePower>() != null,
                "Boss must transition cleanly to Phase 2 with ThingsAbyssalSurgePower.");
            Assert(boss.LivingLeftTentacle != null && boss.LivingLeftTentacle.IsFloorBreachMode,
                "Phase 2 Surge must revive missing Left tentacle in floor-breach mode.");
            Assert(boss.LivingSmallTentacleCount == 1,
                $"Phase 2 Surge with 1 missing major must cap total spawns at 2 (1 major + 1 small), got {boss.LivingSmallTentacleCount} smalls.");

            // Kill Boss in Phase 2 -> ALL surviving major & small tentacles die cleanly!
            await CreatureCmd.Kill(boss.Creature, force: true);
            Assert(!boss.Creature.IsAlive && b.State.Enemies.All(e => !e.IsAlive),
                "Killing Abyssal Inkmaw in Phase 2 must kill all surviving tentacles.");

            DeactivateSyntheticCombat();
        }

        // Extra dedicated check: Phase 2 hitting 50% HP DURING Turn 5 INK_CHARGE immediately interrupts Charge
        // AND reverts charging tentacles back to their normal scheduled move!
        // Also verifies full-board Phase 2 loop STILL executes 4 full prelude turns before INK_CHARGE!
        {
            var b = await InkmawBattle(1, 0, "inkmaw-charge-interrupt");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            var left = b.Monster<ThingsAbyssalCrushingTentacle>();
            var right = b.Monster<ThingsAbyssalInkTentacle>();
            await RunInkmawFullRoundAsync(b); // T1 AbyssalUndertow
            await RunInkmawFullRoundAsync(b); // T2 SummonTentacle
            await RunInkmawFullRoundAsync(b); // T3 TidalCrash
            await RunInkmawFullRoundAsync(b); // T4 AbyssalPressure
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.InkChargeId &&
                   left.NextMove.Id == ThingsAbyssalCrushingTentacle.ChargeBuffId &&
                   right.NextMove.Id == ThingsAbyssalInkTentacle.ChargeBuffId,
                "Turn 5 must enter INK_CHARGE with charging tentacles.");

            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, b.Players[0].Creature);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.Phase2SurgeId,
                $"50% HP during INK_CHARGE must immediately interrupt into PHASE2_SURGE, got {boss.NextMove.Id}.");
            Assert(left.NextMove.Id == ThingsAbyssalCrushingTentacle.HeavySlamId &&
                   right.NextMove.Id == ThingsAbyssalInkTentacle.InkBolsterId,
                $"Interrupting INK_CHARGE must revert tentacles to postponed normal moves (got Left={left.NextMove.Id}, Right={right.NextMove.Id}).");

            // Execute Phase 2 Surge (now all 4 tentacles are alive!) -> verify next 4 prelude turns STILL run
            // (ABYSSAL_UNDERTOW -> ABYSSAL_PRESSURE fallback -> TIDAL_CRASH -> ABYSSAL_PRESSURE -> INK_CHARGE)!
            await RunInkmawFullRoundAsync(b); // Phase 2 Surge
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalUndertowId, $"Post-Surge step 0: {boss.NextMove.Id}");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalPressureId, $"Post-Surge full-board step 1 fallback: {boss.NextMove.Id}");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.TidalCrashId, $"Post-Surge step 2: {boss.NextMove.Id}");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalPressureId, $"Post-Surge step 3: {boss.NextMove.Id}");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.InkChargeId, $"Post-Surge step 4 must enter INK_CHARGE: {boss.NextMove.Id}");

            DeactivateSyntheticCombat();
        }

        // Extra dedicated check 2: NEVER summon > 2 tentacles in one turn (even on completely empty board!)
        // AND NEVER execute consecutive summon turns (T2 SUMMON_TENTACLE -> T3 hit 50% HP -> PHASE2_SURGE_NO_SUMMON)!
        {
            // (a) Empty-board Phase 2 Surge cap: wipe both starting major tentacles -> trigger Phase 2 -> must spawn EXACTLY 2 (2 majors + 0 smalls, NEVER 4!)
            var b = await InkmawBattle(1, 0, "inkmaw-summon-cap-empty");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            await CreatureCmd.Kill(boss.LivingLeftTentacle!.Creature, force: true);
            await CreatureCmd.Kill(boss.LivingRightTentacle!.Creature, force: true);
            Assert(boss.LivingTentacleCount == 0 && boss.LivingSmallTentacleCount == 0,
                "Expected 0 tentacles before empty-board Phase 2 Surge.");

            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, b.Players[0].Creature);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.Phase2SurgeId,
                $"Empty-board Phase 2 trigger must queue PHASE2_SURGE, got {boss.NextMove.Id}.");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.LivingTentacleCount == 2 && boss.LivingSmallTentacleCount == 0,
                $"Empty-board PHASE2_SURGE must cap total spawns at 2 (2 majors + 0 smalls), got majors={boss.LivingTentacleCount}, smalls={boss.LivingSmallTentacleCount}.");

            // Kill both majors again right after Phase 2 Surge -> T+1 is ABYSSAL_UNDERTOW, T+2 must respect summon cooldown (ABYSSAL_PRESSURE, NOT SUMMON_TENTACLE)!
            await RunInkmawFullRoundAsync(b); // T+1 ABYSSAL_UNDERTOW (cd: 2 -> 1)
            await CreatureCmd.Kill(boss.LivingLeftTentacle!.Creature, force: true);
            await CreatureCmd.Kill(boss.LivingRightTentacle!.Creature, force: true);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.AbyssalPressureId,
                $"Post-Surge step 1 with active summon cooldown must NOT re-summon even with empty board, got {boss.NextMove.Id}.");

            // Complete rest of cycle until next cycle's SUMMON_TENTACLE (cd == 0, empty board) -> still capped at EXACTLY 2 tentacles!
            await RunInkmawFullRoundAsync(b); // T+2 ABYSSAL_PRESSURE
            await RunInkmawFullRoundAsync(b); // T+3 TIDAL_CRASH
            await RunInkmawFullRoundAsync(b); // T+4 ABYSSAL_PRESSURE
            await RunInkmawFullRoundAsync(b); // T+5 INK_CHARGE
            await RunInkmawFullRoundAsync(b); // T+6 INK_SPRAY
            await RunInkmawFullRoundAsync(b); // T+7 POST_SPRAY_STUN
            await RunInkmawFullRoundAsync(b); // T+8 ABYSSAL_UNDERTOW
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.SummonTentacleId,
                $"Next cycle step 1 with cooldown expired must queue SUMMON_TENTACLE, got {boss.NextMove.Id}.");
            await RunInkmawFullRoundAsync(b); // T+9 SUMMON_TENTACLE on empty board
            Assert(boss.LivingTentacleCount == 2 && boss.LivingSmallTentacleCount == 0,
                $"Phase 2 SUMMON_TENTACLE on empty board must cap at 2 total (2 majors + 0 smalls), got majors={boss.LivingTentacleCount}, smalls={boss.LivingSmallTentacleCount}.");
            DeactivateSyntheticCombat();
        }

        {
            // (b) Zero consecutive summon turns: T2 SUMMON_TENTACLE -> T3 drop Boss to 50% HP -> must route to PHASE2_SURGE_NO_SUMMON!
            var b = await InkmawBattle(1, 0, "inkmaw-no-consecutive-summon");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            await RunInkmawFullRoundAsync(b); // T1 ABYSSAL_UNDERTOW
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.SummonTentacleId, "T2 must be SUMMON_TENTACLE.");
            await RunInkmawFullRoundAsync(b); // T2 SUMMON_TENTACLE (spawns 1 small, sets cooldown=2)
            int totalBeforeSurge = boss.LivingTentacleCount + boss.LivingSmallTentacleCount;
            Assert(totalBeforeSurge == 3, $"Expected 3 tentacles after T2 summon, got {totalBeforeSurge}.");

            // Drop Boss to 50% HP on T3 immediately after T2's summon!
            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, b.Players[0].Creature);
            Assert(boss.NextMove.Id == ThingsAbyssalInkmaw.Phase2SurgeNoSummonId,
                $"Hitting 50% HP right after T2 SUMMON_TENTACLE must use PHASE2_SURGE_NO_SUMMON (no back-to-back summon!), got {boss.NextMove.Id}.");
            Assert(boss.NextMove.Intents.All(i => i is not SummonIntent),
                "PHASE2_SURGE_NO_SUMMON must NOT display a SummonIntent.");
            await RunInkmawFullRoundAsync(b);
            Assert(boss.Phase == 2 && boss.LivingTentacleCount + boss.LivingSmallTentacleCount == totalBeforeSurge,
                "PHASE2_SURGE_NO_SUMMON must enter Phase 2 without summoning extra tentacles back-to-back.");
            DeactivateSyntheticCombat();
        }

        await VerifyAbyssalInkmawDoomTransitionsAsync();
        await VerifyAbyssalInkBlackoutVeilLifecycleAsync();

        GD.Print("PASS Abyssal Inkmaw 1p/4p A0/A20 rules (T1-T5 repeatable Charge->Spray->AllStun climax, 5 unique vanilla-pattern temp Boss buffs, lossless tentacle state-machine postponement, whole-deck blackout + reshuffle + 0-3 cost randomization + surgical wash + zero hover-tip leaks for InkBlackout & LanternBlindness, Phase 2 Stun-preserve & Charge-interrupt, Phase 1/2 Doom transitions with living tentacles).");
    }

    private async Task VerifyAbyssalInkmawDoomTransitionsAsync()
    {
        // Case 1: Phase 1 Boss doomed with living tentacles (lethal Doom >= Boss HP before Phase 2)
        // Must NOT throw KillWithoutCheckingWinCondition recursion InvalidOperationException!
        {
            var b = await InkmawBattle(1, 0, "inkmaw-doom-p1-living-tentacles");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            Assert(boss.Phase == 1 && boss.LivingTentacleCount == 2, "[InkmawDoom] Starts in Phase 1 with 2 living tentacles.");

            await PowerCmd.Apply<DoomPower>(Choice, boss.Creature, boss.Creature.CurrentHp + 50m, b.Players[0].Creature, null);
            await RunInkmawFullRoundAsync(b);

            Assert(boss.Creature.IsAlive && boss.Creature.CurrentHp == boss.Phase2HpThreshold,
                $"[InkmawDoom] Phase 1 Doom must clamp Boss alive at 50% threshold ({boss.Phase2HpThreshold}) and queue Phase 2, got alive={boss.Creature.IsAlive}, hp={boss.Creature.CurrentHp}.");
            Assert(boss.NextMove.Id is ThingsAbyssalInkmaw.Phase2SurgeId or ThingsAbyssalInkmaw.Phase2SurgeNoSummonId,
                $"[InkmawDoom] Phase 1 Doom must queue Phase 2 Surge, got {boss.NextMove.Id}.");

            // Next round: Boss executes Phase 2 Surge (clears Doom safely & enters Phase 2) — must NOT die or hang!
            await RunInkmawFullRoundAsync(b);
            Assert(boss.Phase == 2 && boss.Creature.IsAlive && boss.Creature.GetPower<DoomPower>() == null,
                $"[InkmawDoom] Phase 2 Surge must enter Phase 2 and safely clear Doom, got phase={boss.Phase}, alive={boss.Creature.IsAlive}, doom={boss.Creature.GetPower<DoomPower>()?.Amount}.");
            DeactivateSyntheticCombat();
        }

        // Case 2: Phase 1 Boss already clamped at 50% HP (_phase2Pending == true) and THEN doomed before Phase 2 Surge runs (e.g. End of Days / enemy turn end while stunned)
        {
            var b = await InkmawBattle(1, 0, "inkmaw-doom-p1-pending");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, b.Players[0].Creature);
            Assert(boss.Phase == 1 && boss.Creature.CurrentHp == boss.Phase2HpThreshold, "[InkmawDoom] Clamped at 50% in Phase 1.");

            await PowerCmd.Apply<DoomPower>(Choice, boss.Creature, boss.Creature.CurrentHp + 20m, b.Players[0].Creature, null);
            // Simulate direct DoomPower.DoomKill (same path as End of Days card or turn-end trigger while stunned!)
            await DoomPower.DoomKill([boss.Creature]);
            Assert(boss.Creature.IsAlive && boss.Creature.CurrentHp == boss.Phase2HpThreshold,
                $"[InkmawDoom] Repeated DoomKill while Phase 1 pending must keep Boss alive at threshold ({boss.Phase2HpThreshold}), got alive={boss.Creature.IsAlive}, hp={boss.Creature.CurrentHp}.");
            DeactivateSyntheticCombat();
        }

        // Case 4: Phase 1 simultaneous DoomKill of Boss + both living major tentacles (recoil hits pending Phase 1 boss)
        {
            var b = await InkmawBattle(1, 0, "inkmaw-doom-p1-simultaneous-all");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            var left = b.Monster<ThingsAbyssalCrushingTentacle>();
            var right = b.Monster<ThingsAbyssalInkTentacle>();

            await PowerCmd.Apply<DoomPower>(Choice, boss.Creature, boss.Creature.CurrentHp + 50m, b.Players[0].Creature, null);
            await PowerCmd.Apply<DoomPower>(Choice, left.Creature, left.Creature.CurrentHp + 20m, b.Players[0].Creature, null);
            await PowerCmd.Apply<DoomPower>(Choice, right.Creature, right.Creature.CurrentHp + 20m, b.Players[0].Creature, null);
            await DoomPower.DoomKill([boss.Creature, left.Creature, right.Creature]);

            Assert(boss.Creature.IsAlive && boss.Creature.CurrentHp == boss.Phase2HpThreshold,
                $"[InkmawDoom] Simultaneous DoomKill must keep Phase 1 Boss alive at threshold ({boss.Phase2HpThreshold}), got alive={boss.Creature.IsAlive}, hp={boss.Creature.CurrentHp}.");
            Assert(left.Creature.IsDead && right.Creature.IsDead,
                "[InkmawDoom] Doomed tentacles must die cleanly alongside Phase 1 Boss survival.");
            DeactivateSyntheticCombat();
        }

        // Case 3: Phase 2 Boss doomed while tentacles are alive -> Boss dies cleanly AND kills all surviving tentacles without recursion/hang!
        {
            var b = await InkmawBattle(1, 0, "inkmaw-doom-p2-living-tentacles");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            await CreatureCmd.Damage(Choice, boss.Creature, 9999m, ValueProp.Unblockable | ValueProp.Unpowered, b.Players[0].Creature);
            await RunInkmawFullRoundAsync(b); // Enters Phase 2 & summons/keeps tentacles
            Assert(boss.Phase == 2 && boss.LivingTentacleCount + boss.LivingSmallTentacleCount > 0,
                $"[InkmawDoom] Entered Phase 2 with living tentacles (majors={boss.LivingTentacleCount}, smalls={boss.LivingSmallTentacleCount}).");

            await PowerCmd.Apply<DoomPower>(Choice, boss.Creature, boss.Creature.CurrentHp + 50m, b.Players[0].Creature, null);
            await DoomPower.DoomKill([boss.Creature]);

            Assert(boss.Creature.IsDead, "[InkmawDoom] Phase 2 Boss must die cleanly to Doom.");
            Assert(b.State.Enemies.All(e => e.IsDead), "[InkmawDoom] All surviving tentacles must be killed when Phase 2 Boss dies to Doom.");
            DeactivateSyntheticCombat();
        }
    }

    /// <summary>
    /// Live NCard + NInkBlackoutCardVeil regression covering the exact 3-player co-op crash sequence:
    ///   1. 3-player Abyssal Inkmaw combat where 1 player dies, surviving players enter blackened turn with live NCard nodes.
    ///   2. Killing a small tentacle washes 1 hand card cleanly while ThingsInkBlackoutPower remains active (no re-veil).
    ///   3. Playing Whirlwind kills Crushing Tentacle -> washes all cards + removes ThingsInkBlackoutPower ->
    ///      NCard.UpdateVisuals / DetachAndFree / tween completion must NEVER trigger infinite recursion (StackOverflowException).
    ///   4. Turn-end cleanup, model swap, and pool return on veiled NCard nodes detach cleanly without recursion.
    /// </summary>
    private async Task VerifyAbyssalInkBlackoutVeilLifecycleAsync()
    {
        try { ScriptManagerBridge.LookupScriptsInAssembly(typeof(NCard).Assembly); } catch (ArgumentException) { }
        try { ScriptManagerBridge.LookupScriptsInAssembly(typeof(NInkBlackoutCardVeil).Assembly); } catch (ArgumentException) { }

        var host = new Node2D { Name = "InkBlackoutVeilRegressionHost" };
        AddChild(host);
        PackedScene cardScene = GD.Load<PackedScene>("res://scenes/cards/card.tscn");
        Assert(cardScene != null, "[InkVeilRepro] Loaded native card.tscn.");

        try
        {
            Battle b = await InkmawBattle(players: 3, ascension: 0, seed: "inkmaw-veil-repro-3p");
            var boss = b.Monster<ThingsAbyssalInkmaw>();
            var left = b.Monster<ThingsAbyssalCrushingTentacle>();
            var deadPlayer = b.Players[0];
            var ironclad = b.Players[1];
            var regent = b.Players[2];

            // Mirror the exact 3-player state: Player 0 is dead before surviving players receive Ink Blackout.
            // Use SetCurrentHpInternal(0) so synthetic headless v111.1 combat avoids uninitialized turn-ready state.
            deadPlayer.Creature.SetCurrentHpInternal(0);
            Assert(!deadPlayer.Creature.IsAlive, "[InkVeilRepro] Player 0 died.");

            // Spawn 1 small tentacle so we also verify small-tentacle single-card wash while power is active.
            await boss.SpawnSmallTentaclesAsync(1, Choice);
            Assert(boss.LivingSmallTentacleCount == 1, "[InkVeilRepro] Spawned 1 small tentacle.");
            var small = boss.LivingSmallTentacles[0];

            // Populate surviving players' hands with Whirlwind (X-cost) + Strike + Bash and mount real NCard nodes.
            CardModel whirlwind = b.State.CreateCard<Whirlwind>(ironclad);
            CardModel strike = b.State.CreateCard<StrikeIronclad>(ironclad);
            CardModel bash = b.State.CreateCard<Bash>(regent);
            await CardPileCmd.AddGeneratedCardToCombat(whirlwind, PileType.Hand, null);
            await CardPileCmd.AddGeneratedCardToCombat(strike, PileType.Hand, null);
            await CardPileCmd.AddGeneratedCardToCombat(bash, PileType.Hand, null);

            NCard MountCard(CardModel model)
            {
                NCard node = cardScene!.Instantiate<NCard>();
                node.Model = model;
                host.AddChild(node);
                node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                return node;
            }

            NCard whirlNode = MountCard(whirlwind);
            NCard strikeNode = MountCard(strike);
            NCard bashNode = MountCard(bash);

            // Apply Ink Blackout to surviving players and start their blackened turn.
            await PowerCmd.Apply<ThingsInkBlackoutPower>(Choice, ironclad.Creature, 1m, boss.Creature, null);
            await PowerCmd.Apply<ThingsInkBlackoutPower>(Choice, regent.Creature, 1m, boss.Creature, null);
            await StartInkmawPlayerTurnAsync(b, rollMoves: false);

            whirlNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            strikeNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);

            Assert(AbyssalInkBlackout.IsVeiled(whirlwind) && AbyssalInkBlackout.IsVeiled(strike) && AbyssalInkBlackout.IsVeiled(bash),
                "[InkVeilRepro] All surviving players' hand cards are veiled.");
            Assert(whirlNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") != null &&
                   strikeNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") != null &&
                   bashNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") != null,
                "[InkVeilRepro] All 3 live NCard nodes mounted NInkBlackoutCardVeil.");

            // Step 1: Sever Left Major Tentacle (same as Whirlwind killing Crushing Tentacle) ->
            // washes ALL remaining cards + removes ThingsInkBlackoutPower!
            left.Creature.SetCurrentHpInternal(1);
            await CreatureCmd.Kill(left.Creature, force: true);

            Assert(ironclad.Creature.GetPower<ThingsInkBlackoutPower>() == null &&
                   regent.Creature.GetPower<ThingsInkBlackoutPower>() == null,
                "[InkVeilRepro] Major tentacle sever removed ThingsInkBlackoutPower from all players.");
            Assert(!AbyssalInkBlackout.IsVeiled(whirlwind) && !AbyssalInkBlackout.IsVeiled(strike) && !AbyssalInkBlackout.IsVeiled(bash),
                "[InkVeilRepro] Major tentacle sever unveiled all cards.");

            // Trigger every NCard visual refresh hook that fires right after Whirlwind / card play completion!
            whirlNode.UpdateVisuals(PileType.Play, CardPreviewMode.Normal);
            strikeNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            whirlwind.InvokeEnergyCostChanged();
            strike.InvokeEnergyCostChanged();
            bash.InvokeEnergyCostChanged();

            // Force immediate detach paths (same as wave-wash tween callback, turn end, model swap, and pool return).
            NInkBlackoutCardVeil.DetachFromCard(whirlNode);
            NInkBlackoutCardVeil.DetachFromCard(strikeNode);
            NInkBlackoutCardVeil.DetachFromCard(bashNode);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Assert(whirlNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null &&
                   strikeNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null &&
                   bashNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null,
                "[InkVeilRepro] All NInkBlackoutCardVeil children detached cleanly after sever.");

            // Verify body children modulate alpha restored cleanly (no permanently invisible card!).
            foreach (NCard node in new[] { whirlNode, strikeNode, bashNode })
            {
                if (node.GetNodeOrNull<CanvasItem>("%Frame") is { } frame)
                {
                    Assert(frame.Modulate.A > 0.9f,
                        $"[InkVeilRepro] Card {node.Model?.Id.Entry} %Frame alpha must restore >0.9 after detach, got {frame.Modulate.A}.");
                }
            }

            // Step 2: Re-apply Ink Blackout and kill small tentacle -> washes 1 hand card per surviving player while ThingsInkBlackoutPower is STILL active.
            await PowerCmd.Apply<ThingsInkBlackoutPower>(Choice, ironclad.Creature, 1m, boss.Creature, null);
            await PowerCmd.Apply<ThingsInkBlackoutPower>(Choice, regent.Creature, 1m, boss.Creature, null);
            await StartInkmawPlayerTurnAsync(b, rollMoves: false);
            whirlNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            strikeNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);

            await CreatureCmd.Kill(small.Creature, force: true);
            bool anyIroncladWashed = !AbyssalInkBlackout.IsVeiled(whirlwind) || !AbyssalInkBlackout.IsVeiled(strike);
            Assert(anyIroncladWashed && !AbyssalInkBlackout.IsVeiled(bash),
                "[InkVeilRepro] Small tentacle death must make washed hand card genuinely unveiled even while ThingsInkBlackoutPower remains active.");

            // Refresh visuals on the washed card immediately (must NOT cancel wave wash or stack-overflow!).
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            Assert(!AbyssalInkBlackout.IsVeiled(bash), "[InkVeilRepro] Washed card stays unveiled after UpdateVisuals.");
            NInkBlackoutCardVeil.DetachFromCard(bashNode);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Assert(bashNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null,
                "[InkVeilRepro] Small-tentacle washed card detached cleanly.");

            // Step 3: Re-veil a card and verify real EndPlayerTurn (AfterSideTurnEnd + EndOfTurnCleanup),
            // OnCardModelChanged, and OnFreedToPool all detach cleanly without recursion.
            AbyssalInkBlackout.InkCard(strike, randomizeCost: true);
            strikeNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            Assert(strikeNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") != null,
                "[InkVeilRepro] Re-inked strike mounted veil.");
            await EndInkmawPlayerTurnAsync(b);
            strikeNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Assert(strikeNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null,
                "[InkVeilRepro] EndOfTurnCleanup detached veil cleanly without recursion.");

            AbyssalInkBlackout.InkCard(bash, randomizeCost: true);
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            bashNode.Model = b.State.CreateCard<DefendIronclad>(regent);
            bashNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Assert(bashNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null,
                "[InkVeilRepro] Model swap detached veil cleanly without recursion.");

            AbyssalInkBlackout.InkCard(whirlwind, randomizeCost: true);
            whirlNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            Assert(whirlNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") != null,
                "[InkVeilRepro] Re-inked whirlwind mounted veil before pool return.");
            whirlNode.OnFreedToPool();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Assert(whirlNode.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil") == null,
                "[InkVeilRepro] OnFreedToPool detached veil cleanly without recursion.");

            DeactivateSyntheticCombat();
            GD.Print("PASS Abyssal Inkmaw NInkBlackoutCardVeil live NCard detach/wash/3p regression.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(host))
            {
                if (host.GetParent() != null)
                    host.GetParent().RemoveChild(host);
                host.QueueFree();
            }
            DeactivateSyntheticCombat();
        }
    }
}
