using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private async Task VerifyFleetingEcho(bool render = true)
    {
        await VerifyFleetingEchoIndirectDamage();
        foreach (int count in new[] { 1, 4 })
        foreach (int asc in new[] { 0, 20 })
        {
            var b = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>(), count, asc);
            var echo = b.Monster<FleetingEcho>();
            var target = echo.Creature;
            Assert(b.Enemies.Length == 1 && b.Encounter.IsWeak, "One Echo in the weak pool.");
            Assert(target.GetPower<FleetingFadePower>()?.Amount == 5, "Timer is five for the whole party.");
            var source = b.Players[0].Creature;
            foreach (var p in b.Players) { p.Creature.SetMaxHpInternal(10000); p.Creature.SetCurrentHpInternal(10000); }
            for (int i = 0; i < 3; i++)
                await CreatureCmd.Damage(Choice, target, 4, ValueProp.Move, source);
            Assert(source.Block == 12, "Three four-damage hits produce twelve Block.");
            Assert(b.Players.Skip(1).All(p => p.Creature.Block == 0), "Other players receive no duplicated Block.");
            await CreatureCmd.GainBlock(target, 5, ValueProp.Unpowered, null);
            await CreatureCmd.Damage(Choice, target, 8, ValueProp.Move, source);
            Assert(source.Block == 15, "Only three HP lost beyond the five Block are converted.");
            await PowerCmd.Apply<DexterityPower>(Choice, source, 10, source, null);
            await CreatureCmd.Damage(Choice, target, 5, ValueProp.Move, source);
            Assert(source.Block == 20, "Conversion does not add ten Dexterity a second time.");
            await PowerCmd.Remove<DexterityPower>(source);
            if (count > 1)
            {
                var other = b.Players[1].Creature;
                await CreatureCmd.Damage(Choice, target, 7, ValueProp.Move, other);
                Assert(other.Block == 7 && source.Block == 20, "Each player gets their own damage converted.");
            }
            // Native PetOwner is the route used by Osty and other summons.
            var pet = b.State.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Osty>().ToMutable(), CombatSide.Player, null);
            pet.PetOwner = b.Players[0];b.State.AddCreature(pet);
            await CreatureCmd.Damage(Choice, target, 6, ValueProp.Move, pet);
            Assert(source.Block == 26 && pet.Block == 0, "Pet attacks protect their owning player.");
            target.SetCurrentHpInternal(3);
            // Native GainBlock intentionally stops once the final enemy dies.
            // Keep another enemy alive to verify lethal/overkill conversion
            // during an ongoing battle, without bypassing that native rule.
            var sentinel=b.State.CreateCreature(ModelDb.Monster<CrystalSnail>().ToMutable(),CombatSide.Enemy,"sentinel");
            b.State.AddCreature(sentinel);
            await CreatureCmd.Damage(Choice, target, 100, ValueProp.Move, source);
            Assert(source.Block == 29 && target.IsDead, $"Lethal hit converts three effective HP, excluding overkill. actual Block={source.Block}, HP={target.CurrentHp}, dead={target.IsDead}");
            DeactivateSyntheticCombat();

            b = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>(), count, asc, "echo-timer-"+count+"-"+asc);
            echo = b.Monster<FleetingEcho>();target = echo.Creature;
            foreach (var p in b.Players) { p.Creature.SetMaxHpInternal(10000); p.Creature.SetCurrentHpInternal(10000); }
            int[] expected = asc == 0 ? [22,26,30,34,38] : [26,30,34,38,42];
            for (int phase = 0; phase < 5; phase++)
            {
                int hp = b.Players[0].Creature.CurrentHp;
                b.State.CurrentSide = CombatSide.Enemy;
                await echo.PerformMove();
                Assert(hp - b.Players[0].Creature.CurrentHp == expected[phase], $"Expected Echo attack damage: players={count}, asc={asc}, phase={phase}, expected={expected[phase]}, actual={hp-b.Players[0].Creature.CurrentHp}, block={b.Players[0].Creature.Block}");
                if (phase < 4)
                {
                    Assert(target.IsAlive && target.GetPower<FleetingFadePower>()?.Amount == 4-phase, "Exactly one timer decrement per enemy action.");
                    echo.RollMove(b.Players.Select(p => p.Creature));
                }
            }
            Assert(target.IsDead && !b.State.Enemies.Contains(target), "Final attack resolves before native death removes the Echo.");
            DeactivateSyntheticCombat();
        }
        var loss = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>(),1,0,"echo-loss");
        var losingEcho=loss.Monster<FleetingEcho>();
        loss.Players[0].Creature.SetCurrentHpInternal(1);
        loss.State.CurrentSide=CombatSide.Enemy;
        await losingEcho.PerformMove();
        Assert(loss.Players[0].Creature.IsDead && losingEcho.Creature.IsAlive,
            "Player defeat does not also kill the surviving Echo.");
        Assert(losingEcho.Creature.GetPower<FleetingFadePower>()?.Amount==5,
            "Already ending combat does not continue the timer or create a second outcome.");
        DeactivateSyntheticCombat();
        GD.Print("PASS Fleeting Echo: 1/4 players, A0/A20, five attacks, owner-only Block, pets, lethal and overkill.");
        if (render) await RenderFleetingEcho();
    }

    private async Task VerifyFleetingEchoIndirectDamage()
    {
        var battle = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>(), 4, seed: "echo-indirect");
        var echo = battle.Monster<FleetingEcho>().Creature;
        var lightningOwner = battle.Players[0].Creature;
        var poisonOwner = battle.Players[1].Creature;
        var secondPoisonOwner = battle.Players[2].Creature;
        var bystander = battle.Players[3].Creature;
        var lightning = (LightningOrb)ModelDb.Orb<LightningOrb>().ToMutable();
        lightning.Owner = battle.Players[0];

        int hp = echo.CurrentHp;
        await lightning.Passive(Choice, echo);
        Assert(hp - echo.CurrentHp == 3, "Native Lightning passive inflicted 3 damage.");
        int passiveBlock = lightningOwner.Block;
        hp = echo.CurrentHp;
        await lightning.Evoke(Choice);
        Assert(hp - echo.CurrentHp == 8, "Native Lightning evoke inflicted 8 damage.");
        int evokeBlock = lightningOwner.Block - passiveBlock;

        await PowerCmd.Apply<PoisonPower>(Choice, echo, 5, poisonOwner, null);
        Assert(poisonOwner.Block == 0, "Applying Poison alone does not award Block.");
        await CreatureCmd.GainBlock(echo, 4, ValueProp.Unpowered, null);
        battle.State.CurrentSide = CombatSide.Enemy;
        hp = echo.CurrentHp;
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
        Assert(hp - echo.CurrentHp == 5 && echo.Block == 4 && echo.GetPowerAmount<PoisonPower>() == 4,
            "Native Poison bypasses Block, deals 5 HP damage, and decays once.");
        Assert(passiveBlock == 3 && evokeBlock == 8 && poisonOwner.Block == 5,
            $"Indirect damage Block mismatch: Lightning passive={passiveBlock}/3, evoke={evokeBlock}/8, Poison={poisonOwner.Block}/5.");
        Assert(lightningOwner.Block == 11 && secondPoisonOwner.Block == 0 && bystander.Block == 0,
            "Only the sources gain Block; Poison does not credit the Lightning owner or the party.");

        // The Block is ready for the enemy attack on the same turn.
        hp = poisonOwner.CurrentHp;
        await CreatureCmd.Damage(Choice, poisonOwner, 5, ValueProp.Move, echo);
        Assert(poisonOwner.CurrentHp == hp && poisonOwner.Block == 0, "Poison Block absorbs an enemy attack.");

        await PowerCmd.Apply<PoisonPower>(Choice, echo, 3, secondPoisonOwner, null);
        await CreatureCmd.Damage(Choice, echo, 2, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        Assert(poisonOwner.Block == 0 && secondPoisonOwner.Block == 0 && lightningOwner.Block == 11,
            "An identical but unattributed damage command does not count as Poison.");
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
        Assert(poisonOwner.Block == 4 && secondPoisonOwner.Block == 3 && bystander.Block == 0,
            "A shared Poison tick credits each applier for their own stacks only.");

        await PowerCmd.Apply<AccelerantPower>(Choice, poisonOwner, 1, poisonOwner, null);
        hp = echo.CurrentHp;
#if STS2_V107_1
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
#else
        await echo.GetPower<PoisonPower>()!.Trigger(); // Same native entry used by Outbreak.
#endif
        Assert(hp - echo.CurrentHp == 11 && echo.GetPowerAmount<PoisonPower>() == 4,
            "Accelerant still deals the original 6 + 5 damage and consumes two stacks.");
        Assert(poisonOwner.Block == 9 && secondPoisonOwner.Block == 9 && bystander.Block == 0,
            "After oldest-stack decay the two ticks credit 3+2 and 3+3 respectively.");

        await PowerCmd.Remove<PoisonPower>(echo);
        await PowerCmd.Remove<AccelerantPower>(poisonOwner);
        await PowerCmd.Apply<PoisonPower>(Choice, echo, 2, null, null);
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
        Assert(poisonOwner.Block == 9 && secondPoisonOwner.Block == 9 && bystander.Block == 0,
            "Fresh unattributed Poison does not inherit the old stack owners.");

        await PowerCmd.Apply<ThornsPower>(Choice, bystander, 4, bystander, null);
        // The prior Lightning/Poison test left 4 Block on the Echo. Remove it so
        // the Thorns test checks the actual unblocked retaliation.
        await CreatureCmd.Damage(Choice, echo, 4, ValueProp.Unpowered, lightningOwner);
        hp = bystander.CurrentHp;
        await CreatureCmd.Damage(Choice, bystander, 6, ValueProp.Move, echo);
        Assert(bystander.CurrentHp == hp - 2 && bystander.Block == 0 && lightningOwner.Block == 11,
            "Player Thorns gain Block before the triggering hit; blocked damage gives no Block.");

        await PowerCmd.Remove<PoisonPower>(echo);
        await PowerCmd.Apply<PoisonPower>(Choice, echo, 100, secondPoisonOwner, null);
        echo.SetCurrentHpInternal(2);
        var sentinel = battle.State.CreateCreature(ModelDb.Monster<CrystalSnail>().ToMutable(), CombatSide.Enemy, "sentinel");
        battle.State.AddCreature(sentinel);
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
        Assert(echo.IsDead && secondPoisonOwner.Block == 11,
            "Lethal Poison in an ongoing battle gives Block for only the remaining 2 HP.");
        DeactivateSyntheticCombat();

        battle = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>(), seed: "echo-poison-final");
        echo = battle.Monster<FleetingEcho>().Creature;
        poisonOwner = battle.Players[0].Creature;
        await PowerCmd.Apply<PoisonPower>(Choice, echo, 20, poisonOwner, null);
        echo.SetCurrentHpInternal(2);
        await Hook.AfterSideTurnStart(battle.State, CombatSide.Enemy, [echo]);
        Assert(echo.IsDead && poisonOwner.Block == 0, "Native Block gain still stops when the last enemy dies.");
        DeactivateSyntheticCombat();
        GD.Print("PASS Fleeting Echo indirect damage: Lightning passive/evoke, native Poison ticks, separate appliers, Thorns, overkill.");
    }

    private async Task RenderFleetingEcho()
    {
        Type? atlasType=typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        ResourceFormatLoader? atlas=atlasType==null?null:(ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
        if(atlas!=null)ResourceLoader.AddResourceFormatLoader(atlas,true);
        var b = await StrongBattle(ModelDb.Encounter<FleetingEchoWeak>());
        var entity = b.Monster<FleetingEcho>().Creature;
        var view = new SubViewport { Size = new Vector2I(900,650), Disable3D = true,
            TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(view);
        var creature = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
        typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(creature,entity);
        typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(creature,entity.Monster!.CreateVisuals());
        creature.Position = new Vector2(450,540);view.AddChild(creature);
        await creature.UpdateIntent([b.Players[0].Creature]);creature.IntentContainer.Modulate=Colors.White;
        await ToSignal(GetTree().CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
        foreach (var (clip,time) in new[] { ("idle_loop",1f),("attack",.52f),("sweep",.58f),
                     ("grasp",.62f),("pulse",.64f),("scatter",.70f),("hurt",.12f),("die",1.15f) })
        {
            var state = creature.Visuals.SpineBody!.GetAnimationState();
            state.SetAnimation(clip,false,0);
            var entry=state.GetCurrent(0);
            using var scope=(object?)entry as IDisposable;
            if(entry==null)throw new Exception("Missing clip: "+clip);
            entry.SetMixDuration(0);entry.SetTrackTime(time);
            creature.Visuals.SpineBody.BoundObject.Call("update_skeleton",0f);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var im=view.GetTexture().GetImage();
            Assert(im.SavePng(Path.Combine(_output,"echo-"+clip+".png"))==Error.Ok,"Native pose captured: "+clip);
        }
        if(System.Environment.GetEnvironmentVariable("THINGS_ECHO_CAPTURE_ANIMATION")=="1")
        {
            creature.IntentContainer.Visible=false;
            var state=creature.Visuals.SpineBody!.GetAnimationState();
            state.SetTimeScale(0);
            foreach(var (clip,duration) in new[]{("idle_loop",3f),("grasp",1.5f),("scatter",1.65f),("die",1.9f)})
            {
                string folder=Path.Combine(_output,"motion",clip);Directory.CreateDirectory(folder);
                state.SetAnimation(clip,false,0);
                int count=(int)Math.Ceiling(duration*20);
                for(int i=0;i<=count;i++)
                {
                    var entry=state.GetCurrent(0);using var scope=(object?)entry as IDisposable;
                    if(entry==null)throw new Exception("Missing animation capture entry");
                    entry.SetMixDuration(0);entry.SetTrackTime(Math.Min(duration,i/20f));
                    creature.Visuals.SpineBody.BoundObject.Call("update_skeleton",0f);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var frame=view.GetTexture().GetImage();
                    Assert(frame.SavePng(Path.Combine(folder,$"{i:D3}.png"))==Error.Ok,"Native animation frame "+clip+" "+i);
                }
            }
        }
        view.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(atlas!=null)ResourceLoader.RemoveResourceFormatLoader(atlas);
        DeactivateSyntheticCombat();
    }
}
