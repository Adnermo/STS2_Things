using System.Reflection;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

public partial class ScalePeelerProbeNode
{
    private static readonly Dictionary<Creature, NCreature> VisualProbeNodes = [];

    private static bool SkipUiGuard() => false;

    private static bool ResolveProbeCreatureNode(Creature creature, ref NCreature? __result)
    {
        if (!VisualProbeNodes.TryGetValue(creature, out NCreature? node) || !GodotObject.IsInstanceValid(node))
            return true;
        __result = node;
        return false;
    }

    private async Task RenderProbe()
    {
        string output = Path.Combine(
            System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT")
                ?? Path.Combine(_root, "build/scale_peeler"),
            "visuals");
        Directory.CreateDirectory(output);

        var extension = GDExtensionManager.LoadExtension(
            Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded,
            "Spine extension loaded.");

        var ui = new Harmony("ScalePeelerProbe.VisualHost");
        ui.Patch(
            AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(ScalePeelerProbeNode), nameof(SkipUiGuard)));
        ui.Patch(
            AccessTools.Method(typeof(Sts2VersionSeam), nameof(Sts2VersionSeam.GetCreatureNode)),
            prefix: new HarmonyMethod(typeof(ScalePeelerProbeNode), nameof(ResolveProbeCreatureNode)));

        ScriptManagerBridge.LookupScriptsInAssembly(typeof(NCard).Assembly);
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsScalePeeler).Assembly);

        var loaderType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        var loader = loaderType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(loaderType)!;
        if (loader != null)
            ResourceLoader.AddResourceFormatLoader(loader, true);

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
        GetTree().Root.Size = new Vector2I(1920, 1080);

        var view = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(view);

        async Task Capture(string name)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RenderingServer.ForceDraw();
            using var image = view.GetTexture().GetImage();
            Assert(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "Captured " + name);
        }

        async Task Clear()
        {
            VisualProbeNodes.Clear();
            foreach (var child in view.GetChildren())
            {
                view.RemoveChild(child);
                child.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RenderingServer.ForceDraw();
        }

        static NCreature AddCreature(SubViewport targetView, Creature entity, Vector2 position)
        {
            var node = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
            typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, entity);
            string visualsPath = entity.Player != null
                ? (string)typeof(CharacterModel).GetProperty("VisualsPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(entity.Player.Character)!
                : (string)typeof(MonsterModel).GetProperty("VisualsPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(entity.Monster!)!;
            var visuals = GD.Load<PackedScene>(visualsPath).Instantiate<NCreatureVisuals>();
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, visuals);
            node.Position = position;
            VisualProbeNodes[entity] = node;
            targetView.AddChild(node);
            return node;
        }

        static float[] ReadPlateAlphas(NScalePeelerVisuals visuals)
        {
            var field = typeof(NScalePeelerVisuals).GetField("_plateAlpha", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return ((float[])field.GetValue(visuals)!).ToArray();
        }

        static void Pose(Node2D sprite, string animation, float time)
        {
            sprite.Call("get_skeleton").AsGodotObject().Call("set_to_setup_pose");
            var state = sprite.Call("get_animation_state").AsGodotObject();
            state.Call("clear_tracks");
            var track = state.Call("set_animation", animation, false, 0).AsGodotObject();
            track.Call("set_track_time", time);
            track.Call("set_mix_duration", 0f);
            sprite.Call("update_skeleton", 0f);
        }

        // 1) Weak encounter: dynamic Block plate visuals (0, 6, 24), HUD, Plated Slam intent, siphon & shatter VFX.
        {
            var s = await Scenario(weak: true);
            var bg = NCombatBackground.Create(new BackgroundAssets(s.Encounter.Id.Entry.ToLowerInvariant(), new Rng()));
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = s.Encounter.CreateScene();
            view.AddChild(slots);

            var ironcladNode = AddCreature(view, s.Player.Creature, new Vector2(440, 746));
            var peelerNode = AddCreature(view, s.Peeler.Creature, slots.GetNode<Node2D>(s.Peeler.Creature.SlotName!).Position);

            for (int f = 0; f < 12; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            await peelerNode.UpdateIntent([s.Player.Creature]);
            peelerNode.IntentContainer.Modulate = Colors.White;
            peelerNode.TrackBlockStatus(s.Peeler.Creature);
            for (int f = 0; f < 8; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // Check native ArmorSiphonPower HUD + icon size.
            var power = ModelDb.Power<ThingsArmorSiphonPower>();
            Assert(power.Icon.GetSize() == (loader == null ? new Vector2(64, 64) : new Vector2(256, 256)),
                "Native HUD icon size matches expected AtlasResourceLoader behavior.");
            Assert(power.BigIcon.GetSize() == new Vector2(256, 256), "Power BigIcon is 256x256.");
            var siphonPowerInstance = s.Peeler.Creature.GetPower<ThingsArmorSiphonPower>()!;
            var hud = peelerNode.FindChildren("*", "", true, false).OfType<NPower>()
                .Single(n => n.Model == siphonPowerInstance);
            Assert(hud != null, "NPower HUD renders ThingsArmorSiphonPower.");

            var visuals = (NScalePeelerVisuals)peelerNode.Visuals;
            float[] alphasAt6 = ReadPlateAlphas(visuals);
            Assert(alphasAt6[0] > 0.95f && alphasAt6[1] > 0.95f && alphasAt6[2] < 0.05f && alphasAt6[3] < 0.05f,
                $"Opening 6 Block shows exactly 2 dorsal plates: [{string.Join(", ", alphasAt6)}]");
            await Capture("scale_peeler_weak_6block");

            // Drop to 0 Block -> 0 plates visible.
            await Sts2VersionSeam.LoseBlock(Choice, s.Peeler.Creature, s.Peeler.Creature.Block, s.Peeler.Creature);
            for (int f = 0; f < 20; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float[] alphasAt0 = ReadPlateAlphas(visuals);
            Assert(alphasAt0.All(a => a < 0.05f),
                $"0 Block hides all dorsal plates: [{string.Join(", ", alphasAt0)}]");
            await Capture("scale_peeler_weak_0block");

            // Advance to PLATED_SLAM_MOVE and gain 24 Block -> 4 glowing plates + dynamic intent 31.
            s.Peeler.MoveStateMachine!.OnMovePerformed(s.Peeler.NextMove);
            s.Peeler.RollMove([s.Player.Creature]);
            Assert(s.Peeler.NextMove.Id == "PLATED_SLAM_MOVE", "Rolled PLATED_SLAM_MOVE for 24-Block showcase.");
            await CreatureCmd.GainBlock(s.Peeler.Creature, 24, ValueProp.Unpowered, null);
            await peelerNode.UpdateIntent([s.Player.Creature]);
            peelerNode.IntentContainer.Modulate = Colors.White;
            for (int f = 0; f < 20; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float[] alphasAt24 = ReadPlateAlphas(visuals);
            Assert(alphasAt24.All(a => a > 0.95f),
                $"24 Block reveals all 4 dorsal plates: [{string.Join(", ", alphasAt24)}]");
            await Capture("scale_peeler_weak_24block");

            // Play siphon VFX stream from blocked Ironclad into Wyrm MouthSocket during cast pose.
            await CreatureCmd.GainBlock(s.Player.Creature, 18, ValueProp.Move, null);
            ironcladNode.TrackBlockStatus(s.Player.Creature);
            peelerNode.SetAnimationTrigger("Cast");
            NArmorSiphonVfx.PlaySiphonStream(s.Peeler.Creature, [s.Player.Creature]);
            Assert(view.GetChildren().OfType<NArmorSiphonVfx>().Any(),
                "PlaySiphonStream spawns NArmorSiphonVfx into scene container.");
            await ToSignal(GetTree().CreateTimer(0.36), SceneTreeTimer.SignalName.Timeout);
            await Capture("scale_peeler_siphon_vfx");
            await ToSignal(GetTree().CreateTimer(0.42), SceneTreeTimer.SignalName.Timeout);

            // Drop Block 24 -> 10 to trigger shatter burst & shed top 2 plates.
            await Sts2VersionSeam.LoseBlock(Choice, s.Peeler.Creature, 14, s.Peeler.Creature);
            for (int f = 0; f < 5; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Assert(view.GetChildren().OfType<NArmorSiphonVfx>().Any(),
                "Plate drop spawns NArmorSiphonVfx crystal shatter burst.");
            await Capture("scale_peeler_shatter_recoil");

            // Check all 10 clips exist on packaged Spine skeleton data.
            var sprite = visuals.GetNode<Node2D>("Visuals");
            sprite.Call("set_update_mode", ClassDB.ClassGetIntegerConstant("SpineConstant", "UpdateMode_Manual"));
            var skelRes = sprite.Get("skeleton_data_res").AsGodotObject();
            foreach (string clip in new[] { "idle_loop", "attack", "slam", "cast", "flurry", "hurt", "die", "revive", "summon", "power_up" })
            {
                Assert(skelRes.Call("find_animation", clip).AsGodotObject() != null, $"Packaged Spine clip exists: {clip}");
            }
            Pose(sprite, "slam", 0.56f);
            await Capture("scale_peeler_pose_slam");

            if (OS.GetCmdlineUserArgs().Contains("--motion"))
                await RenderMotionClips(view, s, ironcladNode, peelerNode, output);

            await Clear();
            DeactivateSyntheticCombat();
        }

        // 2) Normal 3-enemy Depths encounter (peeler + rock + slime) on cave_quartz.
        {
            var strong = await Scenario(weak: false);
            var bg = NCombatBackground.Create(new BackgroundAssets(strong.Encounter.Id.Entry.ToLowerInvariant(), new Rng()));
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = strong.Encounter.CreateScene();
            view.AddChild(slots);

            AddCreature(view, strong.Player.Creature, new Vector2(360, 746));
            foreach (var (monster, slot) in strong.Encounter.MonstersWithSlots)
            {
                var node = AddCreature(view, monster.Creature, slots.GetNode<Node2D>(slot!).Position);
                await node.UpdateIntent([strong.Player.Creature]);
                node.IntentContainer.Modulate = Colors.White;
            }
            for (int f = 0; f < 14; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture("scale_peeler_strong_trio");

            await Clear();
            DeactivateSyntheticCombat();
        }

        view.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        RenderingServer.ForceDraw();
        ui.UnpatchAll(ui.Id);
        if (loader != null)
            ResourceLoader.RemoveResourceFormatLoader(loader);
        var cache = PreloadManager.Cache;
        cache.GetType().GetMethod("UnloadMissedCacheAssets")?.Invoke(cache, null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print("PASS packaged Scale-Peeler scenes, 0/6/24 Block dorsal plate visuals, siphon & shatter VFX, and trio layout.");
    }
}
