using System.Reflection;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Encounters;
using STS2_Things.Monsters;

public partial class DepthsProbeNode
{
    private async Task VerifyColumnTracks()
    {
        var extension = GDExtensionManager.LoadExtension(Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, "Spine extension");
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly);
        var host = new Harmony("DepthsProbe.ColumnTracksHost");
        host.Patch(AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
        Type? atlasType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        ResourceFormatLoader? atlas = atlasType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
        if (atlas != null) ResourceLoader.AddResourceFormatLoader(atlas, true);
        try
        {
            var battle = await StrongBattle(ModelDb.Encounter<ThingsHumanFaceColumnEncounter>());
            var view = new SubViewport { Size = new Vector2I(1920, 1080), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            GetTree().Root.Size = view.Size;
            var bg = battle.Encounter.CreateBackground(battle.Run.Act, battle.Run.Rng.UpFront);
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = battle.Encounter.CreateScene();
            view.AddChild(slots);
            var creatures = new List<NCreature>();
            foreach (var entity in battle.Enemies)
            {
                int level = ((ThingsHumanFaceColumn)entity.Monster!).Level;
                var node = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, entity);
                typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, entity.Monster!.CreateVisuals());
                node.Position = slots.GetNode<Node2D>(entity.SlotName!).Position;
                view.AddChild(node);creatures.Add(node);
                // Reproduce NCombatRoom.RandomizeEnemyScalesAndHues() for duplicate monster types (worst-case 1.10x middle bulge).
                float simulatedScale = level switch { 0 => 1.00f, 1 => 1.10f, _ => 1.04f };
                node.SetScaleAndHue(simulatedScale, .015f + .015f * level);
                await node.UpdateIntent(battle.Players.Select(p => p.Creature));
                node.IntentContainer.Modulate = Colors.White;
            }
            for (int frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var node in creatures)
            {
                Assert(node.Entity.MaxHp is >= 38 and <= 44, "A0 column disc HP stays within [38, 44]");
                Assert(Mathf.IsEqualApprox(node.Visuals.DefaultScale, 1f) && node.Visuals.Scale.IsEqualApprox(Vector2.One),
                    "Every column disc normalizes back to exact 1.0x scale despite duplicate-monster randomization");
                Assert(node.Hitbox.Size.IsEqualApprox(new Vector2(420f, 194f)),
                    "Every column disc hitbox matches uniform 420x194 bounds");
                Assert(node.Visuals.SpineBody!.GetNormalMaterial() == null,
                    "Every column disc clears duplicate-monster HSV tint material");
                var current = node.Visuals.SpineBody!.GetAnimationState().GetCurrent(1);
                GD.Print($"COLUMN TRACK {((ThingsHumanFaceColumn)node.Entity.Monster!).Level}: hp={node.Entity.CurrentHp}/{node.Entity.MaxHp}, wrapper={current != null}, native={current?.BoundObject != null}, scale={node.Visuals.Scale}, hitbox={node.Hitbox.Size}");
                Assert(current?.BoundObject != null && GodotObject.IsInstanceValid(current.BoundObject), "Column must start its actual rotation track after entering the scene");
                Assert(current!.GetAnimationName() == (((ThingsHumanFaceColumn)node.Entity.Monster!).Level == 1 ? "rotate_ccw" : "rotate_cw"), "Alternating native rotation tracks");
                (current as object as IDisposable)?.Dispose();
            }
            var reserveDiscs = creatures.Single(n => ((ThingsHumanFaceColumn)n.Entity.Monster!).Level == 2).Visuals.GetChildren().OfType<Node2D>().Where(n => n.Name.ToString().StartsWith("ReserveDisc")).ToArray();
            Assert(reserveDiscs.Length == 3 && reserveDiscs.All(r => r.Scale.IsEqualApprox(new Vector2(0.41f, 1.04f))),
                "Three reserves really render above the visible column at uniform disc scale");
            for (int frame = 0; frame < 30; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var image = view.GetTexture().GetImage())
                Assert(image.SavePng(Path.Combine(_output, "column-uniform-scale-preview.png")) == Error.Ok, "Saved rendered uniform column preview");
            for (int frame = 0; frame < 90; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var entity in battle.Enemies)
            {
                for (int turn = 0; turn < 4; turn++)
                {
                    await entity.Monster!.PerformMove();
                    entity.Monster.RollMove(battle.Players.Select(p => p.Creature));
                }
            }
            view.QueueFree();await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            DeactivateSyntheticCombat();
        }
        finally
        {
            if (atlas != null) ResourceLoader.RemoveResourceFormatLoader(atlas);
            host.UnpatchAll(host.Id);
        }
    }
}
