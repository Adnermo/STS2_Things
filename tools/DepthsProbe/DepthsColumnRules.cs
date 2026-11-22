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
            // Breaking only a middle disc with moderate damage (redirected half does not kill bottom) causes no bottom-break stun.
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            middle.Creature.SetCurrentHpInternal(10);
            int bottomHpBeforeMiddleBreak=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,20,ValueProp.Unpowered,b.Players[0].Creature);
            alive=Discs(b);
            Assert(alive[0]==bottom && alive[1]==top && alive.All(c=>!c.IsDizzy),
                "Breaking only a middle disc causes no bottom-break stun.");
            Assert(bottomHpBeforeMiddleBreak-bottom.Creature.CurrentHp==10,
                "Middle break still redirects its negated 50% (10) onto the surviving bottom disc.");
            DeactivateSyntheticCombat();

            // Massive overkill on middle (99999) redirects 50000 to bottom and shatters both simultaneously without transferring stun to top.
            b=await ColumnBattle(count,asc);original=Discs(b);top=original[2];
            await HitColumn(b,original[1]);alive=Discs(b);
            Assert(alive.Length==3 && alive[0]==top && alive.All(c=>!c.IsDizzy) && alive.All(c=>c.RemainingLayers==4),
                "Overkilling middle redirects lethal damage to bottom and resolves both breaks cleanly without stunning top.");
            DeactivateSyntheticCombat();

            // Verify upper/middle negated damage redirection (single-target, odd split, block, unblockable, AoE, lethal AoE).
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            Assert(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>().RoomType==MegaCrit.Sts2.Core.Rooms.RoomType.Elite,
                "Human-Faced Column encounter is classified as an Elite.");
            Assert(top.MinInitialHp==(asc==0?34:40) && top.MaxInitialHp==(asc==0?40:46) && !top.CanChangeScale,"Both HP endpoints (34-40 A0 / 40-46 A20) and fixed-scale contract match.");
            int topHp=top.Creature.CurrentHp;
            int bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,10,ValueProp.Move,b.Players[0].Creature);
            Assert(top.Creature.CurrentHp==topHp,"Top immunity retained.");
            Assert(bottomHp-bottom.Creature.CurrentHp==10,"Top redirects 100% of negated damage (10) to bottom.");

            int midHp=middle.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,10,ValueProp.Move,b.Players[0].Creature);
            Assert(midHp-middle.Creature.CurrentHp==5,"Middle half damage retained.");
            Assert(bottomHp-bottom.Creature.CurrentHp==5,"Middle redirects its negated 50% (5) to bottom.");

            // Odd damage (9) on middle: middle takes 4, bottom receives the negated 5 (4 + 5 = 9, zero rounding loss).
            bottom.Creature.SetCurrentHpInternal(bottom.Creature.MaxHp);
            midHp=middle.Creature.CurrentHp;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,middle.Creature,9,ValueProp.Move,b.Players[0].Creature);
            Assert(midHp-middle.Creature.CurrentHp==4 && bottomHp-bottom.Creature.CurrentHp==5,
                "Odd damage (9) on middle splits cleanly as 4 to middle + 5 negated to bottom.");

            // Top Block is untouched by immune hits; redirected damage respects Bottom Block unless Unblockable.
            await CreatureCmd.GainBlock(top.Creature,10,ValueProp.Move,null);
            await CreatureCmd.GainBlock(bottom.Creature,6,ValueProp.Move,null);
            int topBlockBefore=top.Creature.Block;
            int bottomBlockBefore=bottom.Creature.Block;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,bottomBlockBefore+4,ValueProp.Move,b.Players[0].Creature);
            Assert(top.Creature.Block==topBlockBefore,"Top Block is untouched when Top negates damage.");
            Assert(bottom.Creature.Block==0 && bottomHp-bottom.Creature.CurrentHp==4,
                "Redirected blockable damage respects Bottom Block (breaks Bottom Block and deals 4 excess HP).");

            // Unblockable damage (e.g. Poison) on Top redirects as Unblockable onto Bottom.
            await CreatureCmd.GainBlock(bottom.Creature,10,ValueProp.Move,null);
            bottomBlockBefore=bottom.Creature.Block;
            bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,top.Creature,7,ValueProp.Unblockable|ValueProp.Unpowered,null,null,null);
            Assert(bottom.Creature.Block==bottomBlockBefore && bottomHp-bottom.Creature.CurrentHp==7,
                "Unblockable damage negated by Top redirects as Unblockable onto Bottom.");

            // AoE (10 across [Top, Middle, Bottom]): Top=0, Middle=5, Bottom=10 + 10 + 5 = 25!
            DeactivateSyntheticCombat();
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            topHp=top.Creature.CurrentHp;midHp=middle.Creature.CurrentHp;bottomHp=bottom.Creature.CurrentHp;
            await CreatureCmd.Damage(Choice,[top.Creature,middle.Creature,bottom.Creature],10,ValueProp.Move,b.Players[0].Creature);
            Assert(top.Creature.CurrentHp==topHp && midHp-middle.Creature.CurrentHp==5 && bottomHp-bottom.Creature.CurrentHp==25,
                $"AoE 10 deals 0 to top, 5 to middle, and 25 (10+10+5) to bottom; got top={topHp-top.Creature.CurrentHp}, mid={midHp-middle.Creature.CurrentHp}, bot={bottomHp-bottom.Creature.CurrentHp}.");

            // Lethal AoE order-independence: [Top, Middle, Bottom] hit for 20 on 40 HP discs kills Bottom without Middle hurting itself.
            DeactivateSyntheticCombat();
            b=await ColumnBattle(count,asc);original=Discs(b);bottom=original[0];middle=original[1];top=original[2];
            foreach(var d in original){d.Creature.SetMaxHpInternal(40);d.Creature.SetCurrentHpInternal(40);}
            await CreatureCmd.Damage(Choice,[top.Creature,middle.Creature,bottom.Creature],20,ValueProp.Move,b.Players[0].Creature);
            alive=Discs(b);
            Assert(alive[0]==middle && middle.Creature.CurrentHp==30 && middle.IsDizzy && alive.All(c=>c.RemainingLayers==5),
                $"Lethal AoE breaks original bottom cleanly and leaves dropped middle at 30 HP stunned without self-damage (hp={middle.Creature.CurrentHp}).");
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
