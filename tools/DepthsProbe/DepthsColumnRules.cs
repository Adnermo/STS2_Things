using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private static async Task<Battle> ColumnBattle(int players=1,int ascension=0)
    {
        var b=await StrongBattle(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>(),players,ascension,"column-rules");
        foreach(var c in b.Enemies)await c.Monster!.AfterAddedToRoom();
        foreach(var p in b.Players){p.Creature.SetMaxHpInternal(10000);p.Creature.SetCurrentHpInternal(10000);}
        return b;
    }
    private static ThingsHumanFaceColumn[] Discs(Battle b)=>b.Enemies.Select(c=>c.Monster).OfType<ThingsHumanFaceColumn>().OrderBy(c=>c.Level).ToArray();
    private static async Task HitColumn(Battle b,params ThingsHumanFaceColumn[] targets)=>
        await CreatureCmd.Damage(Choice,targets.Select(c=>c.Creature).ToArray(),99999,ValueProp.Unpowered,b.Players[0].Creature);

    private async Task VerifyColumnRules()
    {
        await VerifyColumnAttackBudget();
        foreach(int count in new[]{1,4})
        foreach(int asc in new[]{0,20})
        {
            var b=await ColumnBattle(count,asc);var original=Discs(b);
            var bottom=original[0];var middle=original[1];var top=original[2];
            string middleMove=middle.NextMove.Id;string topMove=top.NextMove.Id;
            await HitColumn(b,bottom);
            var alive=Discs(b);
            Assert(alive.Length==3 && alive[0]==middle && alive[1]==top,"Original identities fall to the correct slots.");
            Assert(middle.IsDizzy && !top.IsDizzy && !alive[2].IsDizzy,"Only the ORIGINAL middle disc is stunned.");
            Assert(top.NextMove.Id==topMove && alive[2].NextMove.Id=="KNOCK_MOVE","Top and replacement keep real announced actions.");
            Assert(alive.All(c=>c.RemainingLayers==5 && c.Creature.GetPower<ThingsHumanFaceColumnPower>()?.Amount==5),"Layer counter remains shared and loses exactly one.");
            b.State.CurrentSide=CombatSide.Enemy;
            await middle.PerformMove();middle.RollMove(b.Players.Select(p=>p.Creature));
            Assert(!middle.IsDizzy && middle.NextMove.Id==middleMove,"One stun action restores the interrupted move.");
            var priorMiddle=alive[1];var priorTop=alive[2];
            await HitColumn(b,middle);
            alive=Discs(b);
            Assert(alive[0]==priorMiddle && priorMiddle.IsDizzy && !priorTop.IsDizzy && !alive[2].IsDizzy,
                "Repeated bottom breaks stun the middle identity of each collapse only.");
            DeactivateSyntheticCombat();

            foreach(bool reverse in new[]{false,true})
            {
                b=await ColumnBattle(count,asc);original=Discs(b);top=original[2];
                await HitColumn(b,reverse?[original[1],original[0]]:[original[0],original[1]]);
                alive=Discs(b);
                Assert(alive.Length==3 && alive[0]==top && alive.All(c=>!c.IsDizzy),
                    "Simultaneous bottom/middle deaths do not transfer stun to top or replacements.");
                Assert(alive.All(c=>c.RemainingLayers==4) && alive.Count(c=>c.IsReplacement)==2,
                    "Two confirmations deduct two layers and add exactly two replacements.");
                DeactivateSyntheticCombat();
            }
            // Breaking only a middle disc with moderate damage (redirected 25% does not kill bottom) causes no bottom-break stun.
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            middle.Creature.SetCurrentHpInternal(15);
            int bottomHpBeforeMiddleBreak=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,20,ValueProp.Unpowered,b.Players[0].Creature);
            alive=Discs(b);
            Assert(alive[0]==bottom && alive[1]==top && alive.All(c=>!c.IsDizzy),
                "Breaking only a middle disc causes no bottom-break stun.");
            Assert(bottomHpBeforeMiddleBreak-bottom.Creature.CurrentHp==5,
                "Middle break still redirects its negated 25% (5) onto the surviving bottom disc.");
            DeactivateSyntheticCombat();

            // Massive overkill on middle (99999) redirects 25000 to bottom and shatters both simultaneously without transferring stun to top.
            b=await ColumnBattle(count,asc);original=Discs(b);top=original[2];
            await HitColumn(b,original[1]);alive=Discs(b);
            Assert(alive.Length==3 && alive[0]==top && alive.All(c=>!c.IsDizzy) && alive.All(c=>c.RemainingLayers==4),
                "Overkilling middle redirects lethal damage to bottom and resolves both breaks cleanly without stunning top.");
            DeactivateSyntheticCombat();

            // Verify upper (50% DR) / middle (25% DR) negated damage redirection (single-target, odd split, block, unblockable, AoE, lethal AoE).
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            Assert(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>().RoomType==MegaCrit.Sts2.Core.Rooms.RoomType.Elite,
                "Human-Faced Column encounter is classified as an Elite.");
            Assert(top.MinInitialHp==(asc==0?34:40) && top.MaxInitialHp==(asc==0?40:46) && !top.CanChangeScale,"Both HP endpoints (34-40 A0 / 40-46 A20) and fixed-scale contract match.");
            int topHp=top.Creature.CurrentHp;
            int bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,10,ValueProp.Move,b.Players[0].Creature);
            Assert(topHp-top.Creature.CurrentHp==5,"Top 50% damage reduction retained (10 -> 5).");
            Assert(bottomHp-bottom.Creature.CurrentHp==5,"Top redirects 50% negated damage (5) to bottom.");

            int midHp=middle.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,12,ValueProp.Move,b.Players[0].Creature);
            Assert(midHp-middle.Creature.CurrentHp==9,"Middle 25% damage reduction retained (12 -> 9).");
            Assert(bottomHp-bottom.Creature.CurrentHp==3,"Middle redirects its negated 25% (3) to bottom.");

            // Odd damage (9) on middle: middle takes floor(9*0.75)=6, bottom receives the negated 3 (6 + 3 = 9, zero rounding loss).
            bottom.Creature.SetCurrentHpInternal(bottom.Creature.MaxHp);
            midHp=middle.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,9,ValueProp.Move,b.Players[0].Creature);
            Assert(midHp-middle.Creature.CurrentHp==6 && bottomHp-bottom.Creature.CurrentHp==3,
                "Odd damage (9) on middle splits cleanly as 6 to middle + 3 negated to bottom.");

            // Top Block absorbs Top's 50% share; redirected 50% respects Bottom Block unless Unblockable.
            await CreatureCmd.GainBlock(top.Creature,10,ValueProp.Move,null);
            await CreatureCmd.GainBlock(bottom.Creature,6,ValueProp.Move,null);
            int topBlockBefore=top.Creature.Block;
            int bottomBlockBefore=bottom.Creature.Block;
            int expectedHalf=bottomBlockBefore+4;
            topHp=top.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,expectedHalf*2,ValueProp.Move,b.Players[0].Creature);
            Assert(top.Creature.Block==Math.Max(0,topBlockBefore-expectedHalf) && top.Creature.CurrentHp==topHp,"Top Block absorbs Top's 50% share.");
            Assert(bottom.Creature.Block==0 && bottomHp-bottom.Creature.CurrentHp==4,
                "Redirected blockable 50% respects Bottom Block (breaks Bottom Block and deals 4 excess HP).");

            // Unblockable damage (e.g. Poison) on Top splits 50/50 and redirects as Unblockable onto Bottom.
            await CreatureCmd.GainBlock(bottom.Creature,10,ValueProp.Move,null);
            bottomBlockBefore=bottom.Creature.Block;
            topHp=top.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,8,ValueProp.Unblockable|ValueProp.Unpowered,null,null,null);
            Assert(topHp-top.Creature.CurrentHp==4,"Top takes 50% of unblockable damage (8 -> 4).");
            Assert(bottom.Creature.Block==bottomBlockBefore && bottomHp-bottom.Creature.CurrentHp==4,
                "Unblockable damage negated by Top (4) redirects as Unblockable onto Bottom.");

            // AoE (10 across [Top, Middle, Bottom]): Top=5 (+5 redirect), Middle=7 (+3 redirect), Bottom=10 + 5 + 3 = 18!
            DeactivateSyntheticCombat();
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            topHp=top.Creature.CurrentHp;midHp=middle.Creature.CurrentHp;bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,[top.Creature,middle.Creature,bottom.Creature],10,ValueProp.Move,b.Players[0].Creature);
            Assert(topHp-top.Creature.CurrentHp==5 && midHp-middle.Creature.CurrentHp==7 && bottomHp-bottom.Creature.CurrentHp==18,
                $"AoE 10 deals 5 to top, 7 to middle, and 18 (10+5+3) to bottom; got top={topHp-top.Creature.CurrentHp}, mid={midHp-middle.Creature.CurrentHp}, bot={bottomHp-bottom.Creature.CurrentHp}.");

            // Lethal AoE order-independence: [Top, Middle, Bottom] hit for 24 on 40 HP discs deals 12 to top (+12), 18 to middle (+6), 42 to bottom -> kills Bottom cleanly without Middle hurting itself.
            DeactivateSyntheticCombat();
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            foreach(var d in original){d.Creature.SetMaxHpInternal(40);d.Creature.SetCurrentHpInternal(40);}
            await CreatureCmd.Damage(Choice,[top.Creature,middle.Creature,bottom.Creature],24,ValueProp.Move,b.Players[0].Creature);
            alive=Discs(b);
            Assert(alive[0]==middle && middle.Creature.CurrentHp==22 && middle.IsDizzy && alive.All(c=>c.RemainingLayers==5),
                $"Lethal AoE breaks original bottom cleanly and leaves dropped middle at 22 HP stunned without self-damage (hp={middle.Creature.CurrentHp}).");
            DeactivateSyntheticCombat();

            // Regression (live report bd2f76d5): AoE breaks Bottom's Block while Middle/Top's redirect kills & removes Bottom
            // before outer CreatureCmd.Damage dispatches Hook.AfterBlockBroken(bottom.CombatState /* null */).
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            bottom.Creature.SetMaxHpInternal(20);bottom.Creature.SetCurrentHpInternal(20);
            middle.Creature.SetMaxHpInternal(40);middle.Creature.SetCurrentHpInternal(40);
            top.Creature.SetMaxHpInternal(40);top.Creature.SetCurrentHpInternal(40);
            await CreatureCmd.GainBlock(bottom.Creature,5,ValueProp.Unpowered,null);
            Assert(bottom.Creature.Block==5,$"Unpowered test block must stay exact across 1P/4P (block={bottom.Creature.Block}).");
            await CreatureCmd.Damage(Choice,[bottom.Creature,middle.Creature,top.Creature],20,ValueProp.Move,b.Players[0].Creature);
            alive=Discs(b);
            Assert(bottom.Creature.CombatState==null && !CombatManager.Instance.IsOverOrEnding && alive.Length==3 && alive[0]==middle && alive.All(c=>c.RemainingLayers==5),
                $"AoE Block-break + lethal redirect onto bottom must not throw NRE in Hook.AfterBlockBroken (combatStateNull={bottom.Creature.CombatState==null}, alive={alive.Length}, layers={alive.FirstOrDefault()?.RemainingLayers}).");
            DeactivateSyntheticCombat();

            // Regression: Simultaneous 3-disc lethal AoE wipe while Reserve > 0 must NOT prematurely end combat;
            // ShouldStopCombatFromEnding keeps combat alive until reserve replacements spawn.
            b=await ColumnBattle(count,asc);original=Discs(b);
            await HitColumn(b,original[0],original[1],original[2]);
            alive=Discs(b);
            Assert(!CombatManager.Instance.IsOverOrEnding && alive.Length==3 && alive.All(c=>c.IsReplacement && c.RemainingLayers==3),
                $"Simultaneous 3-disc wipe with reserve must spawn 3 replacements without ending combat (over={CombatManager.Instance.IsOverOrEnding}, alive={alive.Length}).");
            DeactivateSyntheticCombat();

            b=await ColumnBattle(count,asc);original=Discs(b);top=original[2];bottom=original[0];
            b.State.CurrentSide=CombatSide.Enemy;
            int hp=b.Players[0].Creature.CurrentHp;await top.PerformMove();
            Assert(hp-b.Players[0].Creature.CurrentHp==(asc==0?11:12),"Knock deals 11 on A0 and 12 on A20.");
            top.RollMove(b.Players.Select(p=>p.Creature));await top.PerformMove();
            Assert(b.Players.All(p=>p.Creature.GetPower<WeakPower>()?.Amount==1),"Rebuke applies one Weak to each player.");
            int block=bottom.Creature.Block;await bottom.PerformMove();
            int baseBlock=asc==0?10:14;
            Assert(count==1?bottom.Creature.Block-block==baseBlock:bottom.Creature.Block-block>=baseBlock,
                $"Seal reduced base Block and multiplayer uses native scaling. players={count}, gained={bottom.Creature.Block-block}");
            bottom.RollMove(b.Players.Select(p=>p.Creature));hp=b.Players[0].Creature.CurrentHp;
            await bottom.PerformMove();
            Assert(hp-b.Players[0].Creature.CurrentHp==(asc==0?8:10),"Rattle deals 4x2 on A0 and 5x2 on A20.");
            DeactivateSyntheticCombat();
        }
        GD.Print("PASS column rules: A0/A20, 1/4 players, 11/12 Knock, 8/10 Rattle, 34-40/40-46 HP, fixed scale, middle-only stun, AoE order and recovery.");
    }

    private static async Task VerifyColumnAttackBudget()
    {
        foreach (int players in new[] { 1, 4 })
        foreach (int asc in new[] { 0, 20 })
        {
            int knockPeak = asc == 0 ? 33 : 36;
            int rattlePeak = asc == 0 ? 24 : 30;
            int intactMin = asc == 0 ? 8 : 10;
            int intactMax = asc == 0 ? 19 : 22;
            var b = await ColumnBattle(players, asc);
            var totals = new List<int>();
            b.State.CurrentSide = CombatSide.Enemy;
            for (int turn = 0; turn < 8; turn++)
            {
                if (turn == 4)
                {
                    b.State.CurrentSide = CombatSide.Player;
                    await HitColumn(b, Discs(b)[0]);
                    // A synthetic damage call does not run the native enemy
                    // turn setup that announces newly summoned creatures.
                    foreach (var disc in Discs(b))
                        if (disc.NextMove.Id == "UNSET_MOVE") disc.RollMove(b.Players.Select(p => p.Creature));
                    b.State.CurrentSide = CombatSide.Enemy;
                }
                var before = b.Players.Select(p => p.Creature.CurrentHp).ToArray();
                foreach (var disc in Discs(b)) await disc.PerformMove();
                var damage = b.Players.Select((p, i) => before[i] - p.Creature.CurrentHp).ToArray();
                Assert(damage.All(value => value >= 0 && value <= knockPeak), $"Whole column stays within {knockPeak} damage per player, including collapse and replacement");
                if (turn < 4) Assert(damage.All(value => value >= intactMin && value <= intactMax), $"Intact column deals {intactMin} to {intactMax} damage per turn");
                totals.Add(damage.Max());
                foreach (var disc in Discs(b)) disc.RollMove(b.Players.Select(p => p.Creature));
            }
            foreach (var (id, expected) in new[] { ("KNOCK_MOVE", knockPeak), ("RATTLE_MOVE", rattlePeak) })
            {
                foreach (var disc in Discs(b)) disc.SetMoveImmediate(
                    (MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState)disc.MoveStateMachine!.States[id], forceTransition: true);
                var before = b.Players.Select(p => p.Creature.CurrentHp).ToArray();
                foreach (var disc in Discs(b)) await disc.PerformMove();
                Assert(b.Players.Select((p, i) => before[i] - p.Creature.CurrentHp).All(value => value == expected), "Three aligned column attacks deal the expected total");
            }
            GD.Print($"COLUMN TOTAL {players}p A{asc}: {string.Join(',', totals)}; aligned peak={knockPeak}");
            DeactivateSyntheticCombat();
        }
    }
}
