using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Visuals;

internal static class DepthsBestiaryBossRegression
{
    private static readonly Color BackdropColor = new(0.085f, 0.095f, 0.125f, 1f);

    public static async Task RunAsync(Node host, Action<bool, string> assert)
    {
        string root = System.Environment.GetEnvironmentVariable("THINGS_PROBE_ROOT")
            ?? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));

        if (!ClassDB.ClassExists("SpineSprite"))
        {
            var loadStatus = GDExtensionManager.LoadExtension(Path.Combine(root, "addons/spine/spine_godot_extension.gdextension"));
            assert(loadStatus is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded,
                $"Bestiary boss regression loaded Spine GDExtension (status={loadStatus}).");
        }

        string screenshotDir = System.Environment.GetEnvironmentVariable("THINGS_BESTIARY_SCREENSHOT_DIR")
            ?? Path.Combine(root, "build", "probe-runs", "bestiary-visuals");
        Directory.CreateDirectory(screenshotDir);

        bool canCapture = !string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase);

        var viewport = new SubViewport
        {
            Name = "BestiaryBossRegressionViewport",
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        host.AddChild(viewport);
        if (canCapture)
        {
            host.GetTree().Root.Size = viewport.Size;
        }

        var backdrop = new ColorRect
        {
            Name = "BestiaryBackdrop",
            Color = BackdropColor,
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        viewport.AddChild(backdrop);

        var layoutContainer = new Control
        {
            Name = "LayoutContainer",
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        viewport.AddChild(layoutContainer);

        // Subtle Bestiary UI safe-zone guides matching vanilla bestiary.tscn (rendered on top of LayoutContainer).
        var uiGuides = BuildUiReferenceGuides();
        viewport.AddChild(uiGuides);

        string hurtLabel = new LocString("bestiary", "ACTION_NAME.hurt").GetRawText();
        string dieLabel = new LocString("bestiary", "ACTION_NAME.die").GetRawText();

        try
        {
            var encounterSpecs = new (EncounterModel Encounter, MonsterModel[] Monsters, Type ExpectedLayoutType, Dictionary<Type, string> SnapshotNames)[]
            {
                (
                    ModelDb.Encounter<ThingsCaveGodBossEncounter>(),
                    [
                        ModelDb.Monster<ThingsCaveGodBody>(),
                        ModelDb.Monster<ThingsCaveGodLeftHand>(),
                        ModelDb.Monster<ThingsCaveGodRightHand>(),
                        ModelDb.Monster<ThingsCaveGodCaptiveClaw>(),
                    ],
                    typeof(NBestiaryLayoutCaveGod),
                    new Dictionary<Type, string>
                    {
                        [typeof(ThingsCaveGodBody)] = "cave_god_body",
                        [typeof(ThingsCaveGodLeftHand)] = "cave_god_left_hand",
                        [typeof(ThingsCaveGodCaptiveClaw)] = "cave_god_captive_claw",
                    }
                ),
                (
                    ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(),
                    [
                        ModelDb.Monster<ThingsAbyssalInkmaw>(),
                        ModelDb.Monster<ThingsAbyssalCrushingTentacle>(),
                        ModelDb.Monster<ThingsAbyssalInkTentacle>(),
                        ModelDb.Monster<ThingsAbyssalSpikeTentacle>(),
                        ModelDb.Monster<ThingsAbyssalShellTentacle>(),
                        ModelDb.Monster<ThingsAbyssalGlandTentacle>(),
                    ],
                    typeof(NBestiaryLayoutAbyssalInkmaw),
                    new Dictionary<Type, string>
                    {
                        [typeof(ThingsAbyssalInkmaw)] = "abyssal_inkmaw_body",
                        [typeof(ThingsAbyssalCrushingTentacle)] = "abyssal_crushing_tentacle",
                        [typeof(ThingsAbyssalInkTentacle)] = "abyssal_ink_tentacle",
                        [typeof(ThingsAbyssalSpikeTentacle)] = "abyssal_spike_tentacle",
                    }
                ),
                (
                    ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(),
                    [
                        ModelDb.Monster<ThingsHumanFaceTowerWrath>(),
                        ModelDb.Monster<ThingsHumanFaceTowerLament>(),
                        ModelDb.Monster<ThingsHumanFaceTowerBastion>(),
                        ModelDb.Monster<ThingsHumanFaceTowerMockery>(),
                    ],
                    typeof(NBestiaryLayoutHumanFaceTower),
                    new Dictionary<Type, string>
                    {
                        [typeof(ThingsHumanFaceTowerWrath)] = "human_face_tower_wrath",
                        [typeof(ThingsHumanFaceTowerBastion)] = "human_face_tower_bastion",
                    }
                ),
            };

            int verifiedEntries = 0;
            int snapshotsCaptured = 0;
            NBestiaryLayout? previousEncounterLayout = null;
            var defaultCheckEntry = BestiaryEntry.FromMonster(
                ModelDb.Monster<ThingsLanternFish>(),
                ModelDb.Encounter<ThingsLanternFishWeak>(),
                RoomType.Monster);

            foreach (var (encounter, monsters, expectedLayoutType, snapshotNames) in encounterSpecs)
            {
                BestiaryEntry firstEntry = BestiaryEntry.FromMonster(monsters[0], encounter, RoomType.Boss);
                NBestiaryLayout? layout = firstEntry.CreateLayoutNode(null!);
                assert(layout != null && layout.GetType() == expectedLayoutType,
                    $"BestiaryEntry.CreateLayoutNode for {encounter.Id.Entry} returned {layout?.GetType().Name ?? "null"}, expected {expectedLayoutType.Name}.");
                assert(!defaultCheckEntry.CanReuseLayout(layout),
                    $"Non-boss entry {defaultCheckEntry.monsterModel!.Id.Entry} must not reuse {expectedLayoutType.Name}.");
                if (previousEncounterLayout != null)
                {
                    assert(!firstEntry.CanReuseLayout(previousEncounterLayout),
                        $"{encounter.Id.Entry} must not reuse previous boss layout {previousEncounterLayout.GetType().Name}.");
                }

                layoutContainer.AddChild(layout!);
                await AwaitFramesAsync(host, 2);

                Tween? activeTween = null;
                foreach (MonsterModel monster in monsters)
                {
                    BestiaryEntry entry = BestiaryEntry.FromMonster(monster, encounter, RoomType.Boss);
                    assert(entry.CanReuseLayout(layout),
                        $"BestiaryEntry.CanReuseLayout must return true for {monster.Id.Entry} on {expectedLayoutType.Name}.");

                    // Also confirm fresh CreateLayoutNode routes every part to the custom layout.
                    NBestiaryLayout? probeCreated = entry.CreateLayoutNode(null!);
                    assert(probeCreated != null && probeCreated.GetType() == expectedLayoutType,
                        $"BestiaryEntry.CreateLayoutNode for {monster.Id.Entry} returned {probeCreated?.GetType().Name ?? "null"}, expected {expectedLayoutType.Name}.");
                    probeCreated?.Free();

                    if (activeTween != null && GodotObject.IsInstanceValid(activeTween))
                        activeTween.Kill();
                    layout!.Cleanup();
                    activeTween = layout.CreateTween().SetParallel();
                    List<BestiaryMonsterMove> moves = layout.Setup(entry, activeTween);
                    await AwaitSpineReadyAsync(host, layout);

                    assert(moves.Count > 0 && moves.Count <= 9,
                        $"{monster.Id.Entry} must expose 1..9 Bestiary moves (got {moves.Count}).");
                    assert(moves.All(m => !string.IsNullOrWhiteSpace(m.displayName)),
                        $"{monster.Id.Entry} has a Bestiary move with empty displayName.");
                    assert(moves.Any(m => string.Equals(m.displayName, hurtLabel, StringComparison.Ordinal)),
                        $"{monster.Id.Entry} Bestiary move list must include Hurt ('{hurtLabel}').");
                    assert(moves.Any(m => string.Equals(m.displayName, dieLabel, StringComparison.Ordinal)),
                        $"{monster.Id.Entry} Bestiary move list must include Die ('{dieLabel}').");

                    VerifySpineVisualHealth(layout, monster, assert);
                    VerifyEncounterSpecificPose(layout, monster, assert);

                    // Capture clean idle snapshot before running attack/die preview actions.
                    if (canCapture && snapshotNames.TryGetValue(monster.GetType(), out string? snapName))
                    {
                        await WaitForFullOpacityAndRenderAsync(host, layout);
                        await CaptureAndVerifySnapshotAsync(host, viewport, screenshotDir, snapName, assert);
                        snapshotsCaptured++;
                    }

                    // Execute EVERY move action cleanly (including Hurt, Die, Ink Spray, Enrage, Bastion Unbolt/Rebolt).
                    for (int i = 0; i < moves.Count; i++)
                    {
                        BestiaryMonsterMove move = moves[i];
                        assert(move.action != null,
                            $"{monster.Id.Entry} move #{i + 1} ('{move.displayName}') must use BestiaryMonsterMove.FromAction.");
                        await move.action!();
                        await AwaitFramesAsync(host, 2);

                        if (layout is NBestiaryLayoutAbyssalInkmaw inkLayout && monster is ThingsAbyssalInkmaw
                            && string.Equals(move.displayName, new LocString("monsters", "THINGS_ABYSSAL_INKMAW.moves.INK_SPRAY.title").GetRawText(), StringComparison.Ordinal))
                        {
                            assert(inkLayout.BeamSpineNode != null && inkLayout.BeamSpineNode.Visible,
                                "Abyssal Inkmaw Bestiary INK_SPRAY preview must show BeamSpineNode.");
                        }
                    }

                    verifiedEntries++;
                }

                // Verify switching back to first entry after playing Die/Enrage/Shatter restores clean idle pose.
                if (activeTween != null && GodotObject.IsInstanceValid(activeTween))
                    activeTween.Kill();
                layout!.Cleanup();
                activeTween = layout.CreateTween().SetParallel();
                List<BestiaryMonsterMove> resetMoves = layout.Setup(firstEntry, activeTween);
                await AwaitSpineReadyAsync(host, layout);
                assert(resetMoves.Count > 0 && resetMoves.Count <= 9,
                    $"Re-entering {monsters[0].Id.Entry} after cycling parts must restore clean move list.");
                VerifySpineVisualHealth(layout, monsters[0], assert);
                VerifyEncounterSpecificPose(layout, monsters[0], assert);

                if (activeTween != null && GodotObject.IsInstanceValid(activeTween))
                    activeTween.Kill();
                layout.Cleanup();
                layoutContainer.RemoveChild(layout);
                previousEncounterLayout?.QueueFree();
                previousEncounterLayout = layout;
                await AwaitFramesAsync(host, 2);
            }

            previousEncounterLayout?.QueueFree();
            previousEncounterLayout = null;
            await AwaitFramesAsync(host, 2);

            assert(verifiedEntries == 14, $"Expected all 14 monumental boss Bestiary entries verified, got {verifiedEntries}.");
            if (canCapture)
            {
                assert(snapshotsCaptured >= 9, $"Expected 9 Bestiary boss framing snapshots captured, got {snapshotsCaptured}.");
            }
            GD.Print($"PASS Bestiary boss visuals regression ({verifiedEntries} entries across 3 boss layouts, {snapshotsCaptured} PNG snapshots -> {screenshotDir}).");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(viewport))
            {
                host.RemoveChild(viewport);
                viewport.QueueFree();
            }
            await AwaitFramesAsync(host, 2);
        }
    }

    private static void VerifySpineVisualHealth(NBestiaryLayout layout, MonsterModel monster, Action<bool, string> assert)
    {
        (Node2D? spineNode, MegaSprite? spineController) = GetPrimarySpine(layout);
        assert(spineNode != null && GodotObject.IsInstanceValid(spineNode),
            $"{monster.Id.Entry} layout must instantiate a valid primary SpineSprite node.");
        assert(spineNode!.Visible,
            $"{monster.Id.Entry} primary SpineSprite must have Visible == true.");
        assert(spineNode.Scale.X > 0f && spineNode.Scale.Y > 0f,
            $"{monster.Id.Entry} primary SpineSprite must have positive scale (got {spineNode.Scale}).");

        Node? walk = spineNode;
        while (walk != null && walk != layout.GetParent())
        {
            if (walk is CanvasItem ci)
            {
                assert(ci.ZIndex != -20,
                    $"{monster.Id.Entry} node '{ci.Name}' must not inherit combat background ZIndex = -20 in Bestiary.");
            }
            walk = walk.GetParent();
        }

        assert(spineController != null && GodotObject.IsInstanceValid(spineController.BoundObject),
            $"{monster.Id.Entry} layout must expose a valid MegaSprite controller.");
        assert(spineController!.GetSkeleton()?.GetData() != null,
            $"{monster.Id.Entry} primary MegaSprite must have loaded skeleton data.");
        assert(spineController.IsAnimationStateReady(),
            $"{monster.Id.Entry} primary MegaSprite animation state must be ready.");
    }

    private static void VerifyEncounterSpecificPose(NBestiaryLayout layout, MonsterModel monster, Action<bool, string> assert)
    {
        switch (layout)
        {
            case NBestiaryLayoutCaveGod caveGod:
            {
                string? current = caveGod.PrimarySpineController?.TryGetAnimationState()?.GetCurrentAnimationName(0);
                string expected = monster is ThingsCaveGodCaptiveClaw ? "grab_hold" : "idle_front";
                assert(string.Equals(current, expected, StringComparison.Ordinal),
                    $"{monster.Id.Entry} expected track-0 animation '{expected}', got '{current ?? "null"}'.");
                break;
            }
            case NBestiaryLayoutAbyssalInkmaw inkmaw:
            {
                MegaAnimationState? state = inkmaw.PrimarySpineController?.TryGetAnimationState();
                string? track0 = state?.GetCurrentAnimationName(NAbyssalInkmawBossBackground.ActionTrack);
                assert(string.Equals(track0, "idle_loop", StringComparison.Ordinal),
                    $"{monster.Id.Entry} expected track-0 'idle_loop', got '{track0 ?? "null"}'.");
                if (monster is ThingsAbyssalInkmaw)
                {
                    assert(inkmaw.BeamSpineNode != null && !inkmaw.BeamSpineNode.Visible,
                        "Abyssal Inkmaw BeamSpineNode must start hidden in idle Bestiary pose.");
                }
                else if (monster is ThingsAbyssalSmallTentacleBase small)
                {
                    string? track2 = state?.GetCurrentAnimationName(NAbyssalInkmawBossBackground.VariantSkinTrack);
                    string expectedSkin = $"skin_{small.VariantSkinName}";
                    assert(string.Equals(track2, expectedSkin, StringComparison.Ordinal),
                        $"{monster.Id.Entry} expected track-2 variant skin '{expectedSkin}', got '{track2 ?? "null"}'.");
                }
                break;
            }
            case NBestiaryLayoutHumanFaceTower tower when monster is ThingsHumanFaceTowerFace face:
            {
                MegaAnimationState? state = tower.PrimarySpineController?.TryGetAnimationState();
                string? turntable = state?.GetCurrentAnimationName(NHumanFaceTowerBossBackground.TurntableTrack);
                string? faceAnim = state?.GetCurrentAnimationName(NHumanFaceTowerBossBackground.FaceActionTrack);
                string? sealAnim = state?.GetCurrentAnimationName(NHumanFaceTowerBossBackground.BastionSealTrack);
                assert(string.Equals(turntable, $"hold_slot_{face.FaceSlotIndex}", StringComparison.Ordinal),
                    $"{monster.Id.Entry} expected turntable 'hold_slot_{face.FaceSlotIndex}', got '{turntable ?? "null"}'.");
                assert(string.Equals(faceAnim, face.IdleAnimationName, StringComparison.Ordinal),
                    $"{monster.Id.Entry} expected face idle '{face.IdleAnimationName}', got '{faceAnim ?? "null"}'.");
                assert(string.Equals(sealAnim, "state_bastion_bolted", StringComparison.Ordinal),
                    $"{monster.Id.Entry} expected Bastion seal track 'state_bastion_bolted', got '{sealAnim ?? "null"}'.");
                break;
            }
        }
    }

    private static (Node2D? Node, MegaSprite? Sprite) GetPrimarySpine(NBestiaryLayout layout) => layout switch
    {
        NBestiaryLayoutCaveGod caveGod => (caveGod.PrimarySpineNode, caveGod.PrimarySpineController),
        NBestiaryLayoutAbyssalInkmaw inkmaw => (inkmaw.PrimarySpineNode, inkmaw.PrimarySpineController),
        NBestiaryLayoutHumanFaceTower tower => (tower.PrimarySpineNode, tower.PrimarySpineController),
        _ => (null, null),
    };

    private static async Task AwaitSpineReadyAsync(Node host, NBestiaryLayout layout)
    {
        for (int i = 0; i < 30; i++)
        {
            (_, MegaSprite? sprite) = GetPrimarySpine(layout);
            if (sprite != null && sprite.IsAnimationStateReady())
            {
                await AwaitFramesAsync(host, 2);
                return;
            }
            await AwaitFramesAsync(host, 1);
        }
    }

    private static async Task WaitForFullOpacityAndRenderAsync(Node host, NBestiaryLayout layout)
    {
        // Let the 0.25s Bestiary fade-in tween finish and Spine advance a few frames before capture.
        await host.ToSignal(host.GetTree().CreateTimer(0.32), SceneTreeTimer.SignalName.Timeout);
        foreach (Node child in layout.GetChildren())
        {
            if (child is CanvasItem ci)
                ci.Modulate = Colors.White;
        }
        await AwaitFramesAsync(host, 3);
    }

    private static async Task CaptureAndVerifySnapshotAsync(
        Node host,
        SubViewport viewport,
        string screenshotDir,
        string fileStem,
        Action<bool, string> assert)
    {
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image? img = viewport.GetTexture()?.GetImage();
        assert(img != null && img.GetWidth() == 1920 && img.GetHeight() == 1080,
            $"Snapshot '{fileStem}' must render a 1920x1080 image.");

        // Sample the central Bestiary stage [480..1480, 170..920] every 4px to confirm visible boss pixels rendered.
        int litPixels = 0;
        for (int y = 170; y < 920; y += 4)
        {
            for (int x = 480; x < 1480; x += 4)
            {
                Color px = img!.GetPixel(x, y);
                float diff = MathF.Abs(px.R - BackdropColor.R)
                    + MathF.Abs(px.G - BackdropColor.G)
                    + MathF.Abs(px.B - BackdropColor.B);
                if (diff > 0.08f)
                    litPixels++;
            }
        }
        assert(litPixels >= 400,
            $"Snapshot '{fileStem}' appears blank/invisible in Bestiary stage (sampled visible pixels={litPixels}).");

        string outPath = Path.Combine(screenshotDir, fileStem + ".png");
        Error err = img!.SavePng(outPath);
        assert(err == Error.Ok, $"Saved Bestiary boss snapshot '{outPath}' (err={err}).");
    }

    private static Control BuildUiReferenceGuides()
    {
        var overlay = new Control
        {
            Name = "BestiaryUiReferenceGuides",
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        void AddGuide(string name, Rect2 rect, Color borderColor)
        {
            var box = new ReferenceRect
            {
                Name = name,
                Position = rect.Position,
                Size = rect.Size,
                BorderColor = borderColor,
                BorderWidth = 2f,
                EditorOnly = false,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            overlay.AddChild(box);
        }

        // Exact vanilla bestiary.tscn layout rects on 1920x1080:
        AddGuide("TitleZone", new Rect2(460f, 88f, 1000f, 64f), new Color(0.95f, 0.80f, 0.35f, 0.45f));
        AddGuide("MovePanelZone", new Rect2(242f, 320f, 230f, 440f), new Color(0.45f, 0.85f, 0.95f, 0.45f));
        AddGuide("SidebarZone", new Rect2(1520f, 0f, 400f, 1080f), new Color(0.95f, 0.55f, 0.45f, 0.45f));
        AddGuide("DialogueZone", new Rect2(560f, 946f, 800f, 78f), new Color(0.65f, 0.90f, 0.55f, 0.45f));
        return overlay;
    }

    private static async Task AwaitFramesAsync(Node host, int count)
    {
        SceneTree tree = host.GetTree();
        for (int i = 0; i < count; i++)
        {
            await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}
