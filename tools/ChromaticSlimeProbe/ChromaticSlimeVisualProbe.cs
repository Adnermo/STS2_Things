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
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

public partial class ChromaticSlimeProbeNode
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

    private async Task RenderVisualProbe()
    {
        string output = Path.Combine(
            System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT")
                ?? Path.Combine(_root, "build/chromatic_slime"),
            "visuals");
        Directory.CreateDirectory(output);

        var extension = GDExtensionManager.LoadExtension(
            Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded,
            "Spine extension loaded.");

        var ui = new Harmony("ChromaticSlimeProbe.VisualHost");
        ui.Patch(
            AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(ChromaticSlimeProbeNode), nameof(SkipUiGuard)));
        ui.Patch(
            AccessTools.Method(typeof(Sts2VersionSeam), nameof(Sts2VersionSeam.GetCreatureNode)),
            prefix: new HarmonyMethod(typeof(ChromaticSlimeProbeNode), nameof(ResolveProbeCreatureNode)));

        ScriptManagerBridge.LookupScriptsInAssembly(typeof(NCard).Assembly);
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsChromaticSlimeLarge).Assembly);

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
            GD.Print($"[VisualProbe] Loading visuals: {visualsPath}");
            var visuals = GD.Load<PackedScene>(visualsPath).Instantiate<NCreatureVisuals>();
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, visuals);
            node.Position = position;
            VisualProbeNodes[entity] = node;
            targetView.AddChild(node);
            return node;
        }

        // 1) Stage 1: Large Chromatic Slime + cave_skylight + Chromatic Fission HUD icon + intent
        {
            var s = await Scenario();
            var bg = NCombatBackground.Create(new BackgroundAssets(s.Encounter.Id.Entry.ToLowerInvariant(), new Rng()));
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = s.Encounter.CreateScene();
            view.AddChild(slots);

            AddCreature(view, s.Player.Creature, new Vector2(440, 746));
            var largeNode = AddCreature(view, s.Large.Creature, slots.GetNode<Node2D>("large").Position);
            for (int f = 0; f < 14; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            await largeNode.UpdateIntent([s.Player.Creature]);
            largeNode.IntentContainer.Modulate = Colors.White;
            largeNode.TrackBlockStatus(s.Large.Creature);
            for (int f = 0; f < 8; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var power = ModelDb.Power<ThingsChromaticFissionPower>();
            Assert(power.BigIcon.GetSize() == new Vector2(256, 256), "Power BigIcon is 256x256.");
            var inst = s.Large.Creature.GetPower<ThingsChromaticFissionPower>()!;
            var hud = largeNode.FindChildren("*", "", true, false).OfType<NPower>()
                .Single(n => n.Model == inst);
            Assert(hud != null, "NPower HUD renders ThingsChromaticFissionPower.");

            await Capture("chromatic_slime_stage1_large");
            await Clear();
            DeactivateSyntheticCombat();
        }

        // 2) Stage 2: Showcase all 3 Medium skins (Crimson, Amber w/ Block, Azure) with realistic inherited HP + half-HP fission badge
        {
            var s = await Scenario();
            var bg = NCombatBackground.Create(new BackgroundAssets(s.Encounter.Id.Entry.ToLowerInvariant(), new Rng()));
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = s.Encounter.CreateScene();
            view.AddChild(slots);
            AddCreature(view, s.Player.Creature, new Vector2(360, 746));

            var crimson = (ThingsChromaticSlimeMediumCrimson)ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>().ToMutable();
            var amber = (ThingsChromaticSlimeMediumAmber)ModelDb.Monster<ThingsChromaticSlimeMediumAmber>().ToMutable();
            var azure = (ThingsChromaticSlimeMediumAzure)ModelDb.Monster<ThingsChromaticSlimeMediumAzure>().ToMutable();
            (MonsterModel, string)[] lineup = [(crimson, "med_1"), (amber, "large"), (azure, "med_2")];
            foreach (var (m, slot) in lineup)
            {
                var c = s.Room.CombatState.CreateCreature(m, CombatSide.Enemy, slot);
                c.SetMaxHpInternal(36);
                c.SetCurrentHpInternal(36);
                s.Room.CombatState.AddCreature(c);
                m.SetUpForCombat();
                m.RollMove([s.Player.Creature]);
                await PowerCmd.Apply<ThingsChromaticFissionPower>(Choice, c, 18m, c, null);
                if (m is ThingsChromaticSlimeMediumAmber)
                    await CreatureCmd.GainBlock(c, 8m, ValueProp.Move, null);
                var n = AddCreature(view, c, slots.GetNode<Node2D>(slot).Position);
                await n.UpdateIntent([s.Player.Creature]);
                n.IntentContainer.Modulate = Colors.White;
                n.TrackBlockStatus(c);
            }
            for (int f = 0; f < 16; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            await Capture("chromatic_slime_stage2_mediums");
            await Clear();
            DeactivateSyntheticCombat();
        }

        // 3) Stage 3: Showcase all 4 Small slots + dual-color Split VFX (Crimson left + Azure right)
        {
            var s = await Scenario();
            var bg = NCombatBackground.Create(new BackgroundAssets(s.Encounter.Id.Entry.ToLowerInvariant(), new Rng()));
            bg.Position = new Vector2(983, 540);
            view.AddChild(bg);
            var slots = s.Encounter.CreateScene();
            view.AddChild(slots);
            AddCreature(view, s.Player.Creature, new Vector2(340, 746));

            var spawnedSmalls = new List<(Creature Child, string StrainKey)>();
            (MonsterModel, string, string)[] lineup =
            [
                (ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>().ToMutable(), "small_1", "crimson"),
                (ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>().ToMutable(), "small_2", "crimson"),
                (ModelDb.Monster<ThingsChromaticSlimeSmallAzure>().ToMutable(), "small_3", "azure"),
                (ModelDb.Monster<ThingsChromaticSlimeSmallAzure>().ToMutable(), "small_4", "azure"),
            ];
            foreach (var (m, slot, key) in lineup)
            {
                var c = s.Room.CombatState.CreateCreature(m, CombatSide.Enemy, slot);
                c.SetMaxHpInternal(16);
                c.SetCurrentHpInternal(16);
                s.Room.CombatState.AddCreature(c);
                m.SetUpForCombat();
                m.RollMove([s.Player.Creature]);
                var n = AddCreature(view, c, slots.GetNode<Node2D>(slot).Position);
                await n.UpdateIntent([s.Player.Creature]);
                n.IntentContainer.Modulate = Colors.White;
                n.TrackBlockStatus(c);
                spawnedSmalls.Add((c, key));
            }
            for (int f = 0; f < 12; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            NChromaticSlimeSplitVfx.PlaySplitArcs(slots.GetNode<Node2D>("large").Position + new Vector2(0, -120), spawnedSmalls);
            for (int f = 0; f < 8; f++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture("chromatic_slime_stage3_smalls_vfx");

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
        GD.Print("PASS Chromatic Slime visual scenes across Large, Medium Trio, and Small Quartet + VFX.");
    }
}
