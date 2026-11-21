using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Hooks;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

public partial class DepthsProbeNode
{
    // Native creature HUDs and the native end-turn artwork, independently of a
    // live save. The button's shown position comes from the target game DLL.
    private async Task VerifyEncounterHudLayout()
    {
        var extension = GDExtensionManager.LoadExtension(Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, "Spine extension loaded.");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        var host = new Harmony("DepthsProbe.EncounterLayout");
        host.Patch(AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
        Type? atlasType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        ResourceFormatLoader? atlas = atlasType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
        if (atlas != null) ResourceLoader.AddResourceFormatLoader(atlas, true);
        var view = new SubViewport { Size = new Vector2I(1920, 1080), Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(view);
        GetTree().Root.Size = view.Size;
        string output = Path.Combine(_output, "layout");
        Directory.CreateDirectory(output);
        bool sourceScenes = OS.GetCmdlineUserArgs().Contains("--source-scenes")
            || System.Environment.GetEnvironmentVariable("THINGS_LAYOUT_SOURCE_SCENES") == "1";
        string? only = GetCmdOption("--encounter=")
            ?? System.Environment.GetEnvironmentVariable("THINGS_LAYOUT_ENCOUNTER");
        var rows = new List<object>();
        var failures = new List<string>();
        var encounters = ModelDb.AllEncounters
            .Where(e => e.GetType().Assembly == typeof(ThingsDepths).Assembly && e.HasScene)
            .Where(e => string.IsNullOrWhiteSpace(only)
                || string.Equals(e.Id.Entry, only, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.GetType().Name, only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id.Entry).ToArray();
        Assert(encounters.Length > 0, "At least one authored encounter selected.");
        foreach (var canonical in encounters)
        {
            var b = await StrongBattle(canonical);
            // Exercise authored summon locations as well as opening formations.
            MonsterModel? summon = canonical switch
            {
                ThingsBowlbugProgenitorBossEncounter => ModelDb.Monster<BowlbugRock>(),
                ThingsGravetideSlugBossEncounter => ModelDb.Monster<ThingsGravetideCorpseSlug>(),
                ThingsSoulRoesEncounter => ModelDb.Monster<ThingsSoulRoe>(),
                _ => null,
            };
            if (summon != null)
                foreach (string slot in canonical.Slots.Where(s => b.Enemies.All(e => e.SlotName != s)))
                {
                    var monster = summon.ToMutable();
                    var entity = b.State.CreateCreature(monster, CombatSide.Enemy, slot);
                    b.State.AddCreature(entity);
                    monster.SetUpForCombat();
                    monster.RollMove(b.Players.Select(p => p.Creature));
                }
            var world = new Control { Name = "CombatSceneContainer", Size = new Vector2(1920, 1080), PivotOffset = new Vector2(960, 540),
                Scale = Vector2.One * b.Encounter.GetCameraScaling(), Position = b.Encounter.GetCameraOffset() };
            // Legacy boss background scripts need their full room host. The HUD
            // fixture uses a native generic cave behind those actors instead.
            var background = canonical is ModBossEncounter and not (ThingsCaveGodBossEncounter or ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
                ? NCombatBackground.Create(ModelDb.Act<ThingsDepths>().GenerateBackgroundAssets(b.Run.Rng.UpFront))
                : b.Encounter.CreateBackground(b.Run.Act, b.Run.Rng.UpFront);
            Control? bgContainer = null;
            if (canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
            {
                view.AddChild(world);
                bgContainer = new Control { Name = "BgContainer", Position = world.Size * 0.5f + new Vector2(23, 0), Scale = Vector2.One * 0.9f };
                world.AddChild(bgContainer);
                bgContainer.AddChild(background);
            }
            else
            {
                background.Position = new Vector2(983, 540);
                view.AddChild(background);
                view.AddChild(world);
            }
            var slots = sourceScenes
                ? GD.Load<PackedScene>(Path.Combine(_root, "scenes/encounters", canonical.Id.Entry.ToLowerInvariant() + ".tscn")).Instantiate<Control>()
                : b.Encounter.CreateScene();
            world.AddChild(slots);
            if (canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
            {
                foreach (var entity in b.Enemies)
                    await entity.Monster!.AfterAddedToRoom();
                await PowerCmd.Apply<StrengthPower>(Choice, b.Players[0].Creature, 2, b.Players[0].Creature, null);
                await PowerCmd.Apply<VulnerablePower>(Choice, b.Players[0].Creature, 1, b.Enemies[0], null);
            }
            var creatures = new List<NCreature>();
            foreach (var entity in b.Enemies)
            {
                var creature = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(creature, entity);
                typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(creature, entity.Monster!.CreateVisuals());
                creature.Position = slots.GetNode<Node2D>(entity.SlotName!).Position;
                world.AddChild(creature);
                await creature.UpdateIntent(b.Players.Select(p => p.Creature));
                creature.IntentContainer.Modulate = Colors.White;
                if (entity.Monster is ThingsHumanFaceTowerFace { IsActiveFace: false })
                    creature.Visible = false;
                creatures.Add(creature);
            }
            var team = new Control { Name = "AllyContainer", Position = world.Size * 0.5f };
            if (canonical is ThingsCaveGodBossEncounter)
                team.Position += Vector2.Down * 120f;
            world.AddChild(team);
            var hero = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
            typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(hero, b.Players[0].Creature);
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(hero, b.Players[0].Character.CreateVisuals());
            team.AddChild(hero);
            NCombatRoom.PositionPlayersAndPets([hero], b.Encounter.GetCameraScaling(), b.Encounter.FullyCenterPlayers);

            // Keep the button's native dimensions, textures, offsets and pivots;
            // detach only its combat-input behavior from this render fixture.
            var originalButton = GD.Load<PackedScene>("res://scenes/combat/end_turn_button.tscn").Instantiate<Control>();
            var buttonVisuals = originalButton.GetNode<Control>("Visuals");
            originalButton.RemoveChild(buttonVisuals);
            Vector2 shownRatio = (Vector2)AccessTools.Field(typeof(NEndTurnButton), "_showPosRatio").GetValue(null)!;
            var button = new Control { Size = originalButton.Size, Position = shownRatio * new Vector2(1920, 1080) };
            originalButton.Free();
            view.AddChild(button);
            button.AddChild(buttonVisuals);
            buttonVisuals.GetNode<Label>("Label").Text = "结束回合";
            await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Rect2 blocker = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();
            foreach (var creature in creatures.Where(c => c.Visible))
            {
                var samples = new List<(string Kind, Control Node)>
                {
                    ("health", creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer")),
                    ("hp_text", creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer/HpLabel")),
                };
                if (creature.Entity.Block > 0)
                    samples.Add(("block", creature.GetNode<Control>("HealthBar/HealthBar/BlockContainer")));
                samples.AddRange(creature.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>()
                    .Where(n => n.Visible).Select(n => ("power", n)));
                foreach (var (kind, node) in samples)
                {
                    Rect2 rect = node.GetGlobalRect();
                    bool overlap = rect.Intersects(blocker.Grow(12));
                    rows.Add(new { encounter = canonical.Id.Entry, slot = creature.Entity.SlotName,
                        monster = creature.Entity.Monster!.Id.Entry, kind,
                        rect = new[] { rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y },
                        button = new[] { blocker.Position.X, blocker.Position.Y, blocker.Size.X, blocker.Size.Y }, overlap });
                    if (overlap) failures.Add($"{canonical.Id.Entry}/{creature.Entity.SlotName}: {kind} {rect}");
                }
            }
            using (var image = view.GetTexture().GetImage())
            {
                Assert(image.SavePng(Path.Combine(output, canonical.Id.Entry.ToLowerInvariant() + ".png")) == Error.Ok, "Layout capture saved.");
                if (canonical is ThingsHumanFaceTowerBossEncounter)
                    AssertTowerViewportAlignment(image, view.Size, hero, creatures.First(c => c.Visible), "1920x1080");
            }
            if (canonical is ThingsHumanFaceTowerBossEncounter)
            {
                foreach (var (w, h, bgScale, tag) in new[] { (1920, 1200, 0.9f, "1920x1200"), (1728, 1080, 1.08f, "1728x1080") })
                {
                    view.Size = new Vector2I(w, h);
                    GetTree().Root.Size = view.Size;
                    world.Size = new Vector2(w, h);
                    bgContainer!.Position = world.Size * 0.5f + new Vector2(23, 0);
                    bgContainer.Scale = Vector2.One * bgScale;
                    button.Position = shownRatio * new Vector2(w, h);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var altImage = view.GetTexture().GetImage();
                    Assert(altImage.SavePng(Path.Combine(output, canonical.Id.Entry.ToLowerInvariant() + "_" + tag + ".png")) == Error.Ok, $"Layout capture {tag} saved.");
                    AssertTowerViewportAlignment(altImage, view.Size, hero, creatures.First(c => c.Visible), tag);
                }
                view.Size = new Vector2I(1920, 1080);
                GetTree().Root.Size = view.Size;
            }
            if (canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
            {
                await VerifyMonumentalMultiplayerLayoutsAsync(
                    canonical, view, world, team, creatures, blocker, output, sourceScenes);
            }
            foreach (var child in view.GetChildren()) { view.RemoveChild(child); child.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            DeactivateSyntheticCombat();
        }
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new { sourceScenes,
            encounters = encounters.Length, rows, failures }, new JsonSerializerOptions { WriteIndented = true }));
        view.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        host.UnpatchAll(host.Id);
        if (atlas != null) ResourceLoader.RemoveResourceFormatLoader(atlas);
        foreach (string failure in failures) GD.Print("HUD_OVERLAP " + failure);
        Assert(failures.Count == 0, $"{failures.Count} creature HUD rectangles overlap the end-turn button; see {output}.");
    }

    private void AssertTowerViewportAlignment(Image image, Vector2I size, NCreature hero, NCreature activeFace, string tag)
    {
        float expectedCenterX = size.X * 0.5f;
        Assert(Mathf.Abs(hero.GlobalPosition.X - expectedCenterX) <= 2.5f,
            $"[HumanFaceTower {tag}] Hero X={hero.GlobalPosition.X:F1} not centered at {expectedCenterX:F1}.");
        Assert(Mathf.Abs(activeFace.GlobalPosition.X - expectedCenterX) <= 2.5f,
            $"[HumanFaceTower {tag}] Face HUD X={activeFace.GlobalPosition.X:F1} not centered at {expectedCenterX:F1}.");

        // Verify smaller player monumental scale.
        Assert(Mathf.Abs(hero.Visuals.DefaultScale - NHumanFaceTowerBossBackground.AllyVisualScale) <= 0.02f,
            $"[HumanFaceTower {tag}] Hero DefaultScale={hero.Visuals.DefaultScale:F3} expected {NHumanFaceTowerBossBackground.AllyVisualScale:F3}.");
        Rect2 heroHitbox = hero.Hitbox.GetGlobalRect();
        Assert(heroHitbox.Size.Y > 60f && heroHitbox.Size.Y < 145f,
            $"[HumanFaceTower {tag}] Hero global hitbox height={heroHitbox.Size.Y:F1}px not scaled down as expected.");

        // Verify zero collision between Boss HUD and Hero body/HP.
        Rect2 bossHp = activeFace.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
        Rect2 heroHp = hero.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
        Assert(!bossHp.Intersects(heroHp.Grow(32)),
            $"[HumanFaceTower {tag}] Boss HP {bossHp} too close to Hero HP {heroHp}.");
        Assert(heroHitbox.Position.Y > bossHp.End.Y + 12f,
            $"[HumanFaceTower {tag}] Hero top Y={heroHitbox.Position.Y:F1} overlaps Boss HP bottom Y={bossHp.End.Y:F1}.");

        // Verify zero occlusion by resting hand card fan (5-card center apex & 3-card max apex from HandPosHelper + NCard.defaultSize).
        float handContainerGlobalY = size.Y - 25f;
        float restingCardHalfHeightWithGlow = NCard.defaultSize.Y * HandPosHelper.GetScale(5).Y * 0.5f + 9f;
        float hand5TopY = handContainerGlobalY + HandPosHelper.GetPosition(5, 2).Y - restingCardHalfHeightWithGlow;
        float hand3TopY = handContainerGlobalY + HandPosHelper.GetPosition(3, 1).Y - restingCardHalfHeightWithGlow;
        var heroPowerControls = hero.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
        Assert(heroPowerControls.Length >= 2, $"[HumanFaceTower {tag}] Expected visible hero powers for HUD clearance check.");
        float heroPowersBottomY = heroPowerControls.Max(c => c.GetGlobalRect().End.Y);

        Assert(heroHp.End.Y <= hand5TopY - 40f,
            $"[HumanFaceTower {tag}] Hero HP bottom Y={heroHp.End.Y:F1} too close to 5-card hand top Y={hand5TopY:F1}.");
        Assert(heroPowersBottomY <= hand5TopY - 16f && heroPowersBottomY <= hand3TopY - 6f,
            $"[HumanFaceTower {tag}] Hero Power row bottom Y={heroPowersBottomY:F1} occluded by hand fan (hand5Top={hand5TopY:F1}, hand3Top={hand3TopY:F1}).");

        // Verify zero unrendered clear-color borders along all 4 viewport edges (including bottom corners & center).
        Color clear = ProjectSettings.GetSetting("rendering/environment/defaults/default_clear_color", new Color(0.092f, 0.122f, 0.117f, 1f)).AsColor();
        foreach (var (sx, sy, edgeName) in new[]
        {
            (6, size.Y / 2, "left"),
            (size.X - 7, size.Y / 2, "right"),
            (size.X / 2, 6, "top"),
            (size.X / 2, size.Y - 7, "bottom_center"),
            (size.X / 5, size.Y - 7, "bottom_left"),
            (size.X * 4 / 5, size.Y - 7, "bottom_right"),
        })
        {
            Color px = image.GetPixel(sx, sy);
            float dist = Mathf.Abs(px.R - clear.R) + Mathf.Abs(px.G - clear.G) + Mathf.Abs(px.B - clear.B);
            Assert(dist > 0.035f, $"[HumanFaceTower {tag}] Unrendered clear-color gap detected on {edgeName} edge at ({sx},{sy}): px={px}, clear={clear}.");
        }
    }

    private async Task VerifyMonumentalMultiplayerLayoutsAsync(
        EncounterModel canonical,
        SubViewport view,
        Control world,
        Control team,
        List<NCreature> enemyCreatures,
        Rect2 endTurnBlocker,
        string output,
        bool sourceScenes)
    {
        _ = world;
        _ = sourceScenes;
        bool isTower = canonical is ThingsHumanFaceTowerBossEncounter;
        string tagPrefix = isTower ? "HumanFaceTower" : "AbyssalInkmaw";
        CharacterModel[] roster =
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>()
        ];

        foreach (int partySize in new[] { 3, 4 })
        {
            foreach (var child in team.GetChildren()) { team.RemoveChild(child); child.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var mpBattle = await StrongBattle(canonical, players: partySize, seed: $"mp-layout-{canonical.Id.Entry}-{partySize}");
            var heroes = new List<NCreature>();
            for (int i = 0; i < partySize; i++)
            {
                var p = mpBattle.Players[i];
                await PowerCmd.Apply<StrengthPower>(Choice, p.Creature, 2, p.Creature, null);
                await PowerCmd.Apply<VulnerablePower>(Choice, p.Creature, 1, mpBattle.Enemies[0], null);

                var node = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, p.Creature);
                typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, roster[i % roster.Length].CreateVisuals());
                team.AddChild(node);
                heroes.Add(node);
            }

            NCombatRoom.PositionPlayersAndPets(heroes, mpBattle.Encounter.GetCameraScaling(), mpBattle.Encounter.FullyCenterPlayers);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            string tag = $"{partySize}p";
            using (var mpImage = view.GetTexture().GetImage())
            {
                Assert(mpImage.SavePng(Path.Combine(output, $"{canonical.Id.Entry.ToLowerInvariant()}_{tag}.png")) == Error.Ok,
                    $"[{tagPrefix} {tag}] Multiplayer layout capture saved.");
            }

            float expectedCenterX = view.Size.X * 0.5f;
            float avgX = heroes.Average(h => h.GlobalPosition.X);
            Assert(Mathf.Abs(avgX - expectedCenterX) <= 4f,
                $"[{tagPrefix} {tag}] Party average X={avgX:F1} not symmetrically centered at {expectedCenterX:F1}.");

            var orderedHeroes = heroes.OrderBy(h => h.GlobalPosition.X).ToArray();
            var hpRects = new Rect2[orderedHeroes.Length];

            NCreature bossCreature = isTower
                ? enemyCreatures.First(c => c.Visible)
                : enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkmaw);
            Rect2 bossHp = bossCreature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();

            float handContainerGlobalY = view.Size.Y - 25f;
            float restingCardHalfHeightWithGlow = NCard.defaultSize.Y * HandPosHelper.GetScale(5).Y * 0.5f + 9f;
            float hand5TopY = handContainerGlobalY + HandPosHelper.GetPosition(5, 2).Y - restingCardHalfHeightWithGlow;

            for (int i = 0; i < orderedHeroes.Length; i++)
            {
                NCreature h = orderedHeroes[i];
                Assert(h.Position.Y >= MonumentalBossPartyLayoutPatch.BaseFloorLocalY - 0.5f,
                    $"[{tagPrefix} {tag}] Hero #{i} local Y={h.Position.Y:F1} pushed up off the floor into background!");
                Assert(h.Visuals.Modulate.IsEqualApprox(Colors.White),
                    $"[{tagPrefix} {tag}] Hero #{i} unexpectedly dimmed ({h.Visuals.Modulate}).");
                Assert(Mathf.Abs(h.Visuals.DefaultScale - NAbyssalInkmawBossBackground.AllyVisualScale) <= 0.02f,
                    $"[{tagPrefix} {tag}] Hero #{i} DefaultScale={h.Visuals.DefaultScale:F3} not scaled to monumental 0.67.");

                Rect2 hHitbox = h.Hitbox.GetGlobalRect();
                Rect2 hHp = h.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
                hpRects[i] = hHp;

                Assert(hHitbox.Position.Y > bossHp.End.Y + 8f,
                    $"[{tagPrefix} {tag}] Hero #{i} top Y={hHitbox.Position.Y:F1} overlaps Boss HP bottom Y={bossHp.End.Y:F1}.");
                Assert(!hHp.Intersects(endTurnBlocker.Grow(12)),
                    $"[{tagPrefix} {tag}] Hero #{i} HP {hHp} overlaps End Turn button {endTurnBlocker}.");

                var heroPowerControls = h.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
                if (heroPowerControls.Length > 0)
                {
                    float powersBottomY = heroPowerControls.Max(c => c.GetGlobalRect().End.Y);
                    Assert(powersBottomY <= hand5TopY - 8f,
                        $"[{tagPrefix} {tag}] Hero #{i} Power row bottom Y={powersBottomY:F1} too close to 5-card hand top Y={hand5TopY:F1}.");
                }

                if (i > 0)
                {
                    Assert(hHp.Position.X >= hpRects[i - 1].End.X + 16f,
                        $"[{tagPrefix} {tag}] Hero #{i} HP {hHp} crowds Hero #{i - 1} HP {hpRects[i - 1]}.");
                }
            }

            if (!isTower)
            {
                Rect2 leftTentHp = enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalCrushingTentacle)
                    .GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
                Rect2 rightTentHp = enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkTentacle)
                    .GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
                Assert(hpRects[0].Position.X >= leftTentHp.End.X + 32f,
                    $"[{tagPrefix} {tag}] Leftmost Hero HP {hpRects[0]} crowds Left Tentacle HP {leftTentHp}.");
                Assert(hpRects[^1].End.X <= rightTentHp.Position.X - 32f,
                    $"[{tagPrefix} {tag}] Rightmost Hero HP {hpRects[^1]} crowds Right Tentacle HP {rightTentHp}.");

                if (partySize == 4)
                {
                    var spikeMonster = (ThingsAbyssalSpikeTentacle)ModelDb.Monster<ThingsAbyssalSpikeTentacle>().ToMutable();
                    spikeMonster.ConfigureSpawn(isOuterLeft: true, playEmergeOnAdd: false);
                    var shellMonster = (ThingsAbyssalShellTentacle)ModelDb.Monster<ThingsAbyssalShellTentacle>().ToMutable();
                    shellMonster.ConfigureSpawn(isOuterLeft: false, playEmergeOnAdd: false);

                    var spikeEntity = await CreatureCmd.Add(spikeMonster, mpBattle.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot);
                    var shellEntity = await CreatureCmd.Add(shellMonster, mpBattle.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterRightTentacleSlot);
                    await PowerCmd.Apply<MinionPower>(Choice, spikeEntity, 1m, spikeEntity, null);
                    await PowerCmd.Apply<MinionPower>(Choice, shellEntity, 1m, shellEntity, null);
                    spikeMonster.RollMove(mpBattle.Players.Select(p => p.Creature));
                    shellMonster.RollMove(mpBattle.Players.Select(p => p.Creature));

                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: true, alive: true, variantSkin: spikeMonster.VariantSkinName, playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: false, alive: true, variantSkin: shellMonster.VariantSkinName, playEmerge: false);

                    var smallNodes = new List<NCreature>();
                    foreach (var ent in new[] { spikeEntity, shellEntity })
                    {
                        var sc = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                        typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(sc, ent);
                        typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(sc, ent.Monster!.CreateVisuals());
                        world.AddChild(sc);
                        await sc.UpdateIntent(mpBattle.Players.Select(p => p.Creature));
                        sc.IntentContainer.Modulate = Colors.White;
                        smallNodes.Add(sc);
                    }

                    NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                    using (var full5Img = view.GetTexture().GetImage())
                    {
                        Assert(full5Img.SavePng(Path.Combine(output, $"{canonical.Id.Entry.ToLowerInvariant()}_4p_full5.png")) == Error.Ok,
                            $"[{tagPrefix} 4p_full5] 5-enemy + 4-player layout capture saved.");
                    }

                    await CaptureAbyssalInkVisualAuditsAsync(view, output, mpBattle, enemyCreatures, smallNodes);

                    Rect2 leftSmallHp = smallNodes[0].GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
                    Rect2 rightSmallHp = smallNodes[1].GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
                    Assert(leftSmallHp.Position.X >= 12f && rightSmallHp.End.X <= view.Size.X - 12f,
                        $"[{tagPrefix} 4p_full5] Small tentacle HP out of viewport: L={leftSmallHp}, R={rightSmallHp}.");
                    Assert(leftSmallHp.End.X <= leftTentHp.Position.X - 8f,
                        $"[{tagPrefix} 4p_full5] Left small tentacle HP {leftSmallHp} crowds Left major tentacle HP {leftTentHp}.");
                    Assert(rightSmallHp.Position.X >= rightTentHp.End.X + 8f,
                        $"[{tagPrefix} 4p_full5] Right small tentacle HP {rightSmallHp} crowds Right major tentacle HP {rightTentHp}.");
                    Assert(!rightSmallHp.Intersects(endTurnBlocker.Grow(12)),
                        $"[{tagPrefix} 4p_full5] Right small tentacle HP {rightSmallHp} overlaps End Turn button {endTurnBlocker}.");

                    foreach (var sc in smallNodes)
                    {
                        var scPowers = sc.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
                        foreach (var pc in scPowers)
                        {
                            Rect2 pr = pc.GetGlobalRect();
                            Assert(!pr.Intersects(endTurnBlocker.Grow(12)),
                                $"[{tagPrefix} 4p_full5] Small tentacle power {pr} overlaps End Turn button {endTurnBlocker}.");
                        }
                        world.RemoveChild(sc);
                        sc.QueueFree();
                    }
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: true, alive: false, variantSkin: "spike", playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: false, alive: false, variantSkin: "shell", playEmerge: false);
                }
            }
        }
    }

    private async Task CaptureAbyssalInkVisualAuditsAsync(
        SubViewport view,
        string output,
        Battle mpBattle,
        List<NCreature> majorEnemies,
        List<NCreature> smallEnemies)
    {
        var p0 = mpBattle.Players[0];
        var cardScene = GD.Load<PackedScene>("res://scenes/cards/card.tscn");

        // 1. Live GPU Zero-Leak Card Audit: 6 wildly different card archetypes top (unveiled) vs bottom (veiled)!
        var auditLayer = new Control
        {
            Name = "InkCardAuditBoard",
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            ZAsRelative = false,
            ZIndex = 90,
        };
        var backdrop = new ColorRect
        {
            Color = new Color(0.08f, 0.10f, 0.14f, 1f),
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
        };
        auditLayer.AddChild(backdrop);
        view.AddChild(auditLayer);

        CardModel MakeModel(Func<CardModel> create, bool upgrade = false)
        {
            CardModel m = create();
            if (upgrade && m.IsUpgradable)
                m.UpgradeInternal();
            return m;
        }

        CardModel[] topModels =
        [
            MakeModel(() => mpBattle.State.CreateCard<StrikeIronclad>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<DemonForm>(p0), upgrade: true),
            MakeModel(() => mpBattle.State.CreateCard<Shockwave>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Whirlwind>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Comet>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Wound>(p0)),
        ];
        CardModel[] botModels =
        [
            MakeModel(() => mpBattle.State.CreateCard<StrikeIronclad>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<DemonForm>(p0), upgrade: true),
            MakeModel(() => mpBattle.State.CreateCard<Shockwave>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Whirlwind>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Comet>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Wound>(p0)),
        ];

        var spawnedCards = new List<NCard>();
        for (int i = 0; i < topModels.Length; i++)
        {
            float cx = 195f + i * 305f;
            var topCard = cardScene.Instantiate<NCard>();
            topCard.Model = topModels[i];
            topCard.Position = new Vector2(cx, 275f);
            topCard.Scale = new Vector2(0.76f, 0.76f);
            auditLayer.AddChild(topCard);
            topCard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            spawnedCards.Add(topCard);

            await CardPileCmd.AddGeneratedCardToCombat(botModels[i], PileType.Hand, null);
            AbyssalInkBlackout.InkCard(botModels[i], randomizeCost: true);
            var botCard = cardScene.Instantiate<NCard>();
            botCard.Model = botModels[i];
            botCard.Position = new Vector2(cx, 785f);
            botCard.Scale = new Vector2(0.76f, 0.76f);
            auditLayer.AddChild(botCard);
            botCard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            botCard.CardHighlight?.AnimShow();
            NInkBlackoutCardVeil.Refresh(botCard);
            spawnedCards.Add(botCard);
        }

        for (int f = 0; f < 4; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        // Programmatic zero-leak check on all 6 veiled NCard instances!
        for (int i = 0; i < botModels.Length; i++)
        {
            NCard botCard = spawnedCards[i * 2 + 1];
            var veil = botCard.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil");
            Assert(veil != null, $"[InkCardAudit] Veiled card #{i} ({botModels[i].Id.Entry}) must mount NInkBlackoutCardVeil.");
            foreach (string leakPath in new[] { "%Frame", "%TitleBanner", "%StarIcon", "%Highlight", "%EnergyIcon", "%TypePlaque" })
            {
                if (botCard.GetNodeOrNull<CanvasItem>(leakPath) is { } leakNode)
                {
                    Assert(leakNode.Modulate.A <= 0.01f,
                        $"[InkCardAudit] Card #{i} ({botModels[i].Id.Entry}) leaked {leakPath} with alpha={leakNode.Modulate.A}.");
                }
            }
        }

        using (var cardImg = view.GetTexture().GetImage())
        {
            Assert(cardImg.SavePng(Path.Combine(output, "abyssal_ink_cards_zero_leak_audit.png")) == Error.Ok,
                "[InkCardAudit] Zero-leak veiled card board saved.");
        }

        view.RemoveChild(auditLayer);
        auditLayer.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 2. Live GPU Screen-Drench Spray Overlay Audit (mid-sweep over live 4P Boss arena)!
        var drenchShader = GD.Load<Shader>("res://shaders/vfx/abyssal_ink_screen_drench.gdshader");
        var splatterTex = GD.Load<Texture2D>("res://images/vfx/abyssal_ink_screen_splatter.png");
        Assert(drenchShader != null && splatterTex != null, "[InkSprayAudit] Loaded screen drench shader & splatter texture.");
        var drenchMat = new ShaderMaterial { Shader = drenchShader };
        drenchMat.SetShaderParameter("sweep_progress", 0.72f);
        drenchMat.SetShaderParameter("curtain_strength", 0.88f);
        drenchMat.SetShaderParameter("border_strength", 0.96f);
        drenchMat.SetShaderParameter("drip_offset", 0.05f);
        drenchMat.SetShaderParameter("wash_progress", 0.0f);

        var drenchRect = new TextureRect
        {
            Name = "AuditInkScreenDrench",
            Texture = splatterTex,
            Material = drenchMat,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            ZAsRelative = false,
            ZIndex = 80,
        };
        view.AddChild(drenchRect);

        for (int f = 0; f < 3; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        using (var drenchImg = view.GetTexture().GetImage())
        {
            Assert(drenchImg.SavePng(Path.Combine(output, "abyssal_ink_spray_screen_drench.png")) == Error.Ok,
                "[InkSprayAudit] Full-screen sweeping ink drench capture saved.");
        }

        // 3. Live GPU Turn-5 Stun + Persistent Subtle Ink Border + 5 Veiled Hand Cards Audit!
        NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Stunned);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: true, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: false, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: true, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: false, stunned: true);

        drenchMat.SetShaderParameter("sweep_progress", 1.35f);
        drenchMat.SetShaderParameter("curtain_strength", 0.0f);
        drenchMat.SetShaderParameter("border_strength", 0.40f);
        drenchMat.SetShaderParameter("drip_offset", 0.04f);

        var handHolder = new Node2D { Name = "AuditInkedHand", ZAsRelative = false, ZIndex = 70 };
        view.AddChild(handHolder);
        for (int i = 0; i < 5; i++)
        {
            var hc = cardScene.Instantiate<NCard>();
            hc.Model = botModels[i];
            hc.Position = new Vector2(610f + i * 175f, 940f + MathF.Abs(i - 2) * 14f);
            hc.RotationDegrees = (i - 2) * 4.5f;
            hc.Scale = new Vector2(0.56f, 0.56f);
            handHolder.AddChild(hc);
            hc.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            NInkBlackoutCardVeil.Refresh(hc);
        }

        for (int f = 0; f < 6; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        using (var stunImg = view.GetTexture().GetImage())
        {
            Assert(stunImg.SavePng(Path.Combine(output, "abyssal_ink_turn5_stun_blackout.png")) == Error.Ok,
                "[InkTurn5Audit] Turn 5 Stun + subtle ink screen border + veiled hand capture saved.");
        }

        // Clean up temporary audit nodes & reset stances
        view.RemoveChild(handHolder);
        handHolder.QueueFree();
        view.RemoveChild(drenchRect);
        drenchRect.QueueFree();
        foreach (CardModel m in botModels)
            AbyssalInkBlackout.Forget(m, restoreTrackedCost: true);
        NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Normal);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: true, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: false, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: true, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: false, stunned: false);
    }
}
