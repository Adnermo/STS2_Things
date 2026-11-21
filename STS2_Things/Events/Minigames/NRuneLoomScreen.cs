using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Overgrowth (Act 1A) 4x4 Overgrown Rune Loom overlay screen styled after vanilla CrystalSphere:
/// full-bleed hand-painted Overgrowth moss-stone altar backdrop, animated amber-sap shader cavity,
/// hand-painted carved limestone rune tablets connected by glowing liquid amber-sap channels,
/// dramatic floating outlined bottom-left HUD counter, and hand-painted divine_button controls.
/// </summary>
public partial class NRuneLoomScreen : Control, IOverlayScreen
{
    private const string BackdropPath = "res://images/events/minigames/things_rune_loom_bg.png";
    private const string StoneTilePath = "res://images/events/minigames/things_rune_tile_stone.png";
    private const string SapShaderPath = "res://shaders/things_rune_sap_bed.gdshader";

    private sealed partial class PathChannelCanvas : Control
    {
        public RuneLoomMinigame? Minigame { get; set; }
        public Dictionary<(int X, int Y), Control> TileControls { get; } = new();

        public override void _Draw()
        {
            if (Minigame == null || Minigame.Path.Count < 2)
                return;

            for (int i = 0; i < Minigame.Path.Count - 1; i++)
            {
                RuneLoomTile aTile = Minigame.Path[i];
                RuneLoomTile bTile = Minigame.Path[i + 1];
                if (!TileControls.TryGetValue((aTile.X, aTile.Y), out Control? ctrlA) ||
                    !TileControls.TryGetValue((bTile.X, bTile.Y), out Control? ctrlB))
                    continue;
                Vector2 a = ctrlA.Position + ctrlA.Size * 0.5f;
                Vector2 b = ctrlB.Position + ctrlB.Size * 0.5f;
                DrawLine(a, b, new Color(0.95f, 0.70f, 0.22f, 0.58f), 22f, true);
                DrawLine(a, b, new Color(1.00f, 0.88f, 0.48f, 0.98f), 8f, true);
            }
        }
    }

    private sealed partial class PathBadgeCanvas : Control
    {
        public RuneLoomMinigame? Minigame { get; set; }
        public Dictionary<(int X, int Y), Control> TileControls { get; } = new();
        public Font? BadgeFont { get; set; }

        public override void _Draw()
        {
            if (Minigame == null || Minigame.Path.Count == 0)
                return;

            for (int i = 0; i < Minigame.Path.Count - 1; i++)
            {
                RuneLoomTile aTile = Minigame.Path[i];
                RuneLoomTile bTile = Minigame.Path[i + 1];
                if (!TileControls.TryGetValue((aTile.X, aTile.Y), out Control? ctrlA) ||
                    !TileControls.TryGetValue((bTile.X, bTile.Y), out Control? ctrlB))
                    continue;
                Vector2 ca = ctrlA.Position + ctrlA.Size * 0.5f;
                Vector2 cb = ctrlB.Position + ctrlB.Size * 0.5f;
                Vector2 dir = (cb - ca).Normalized();
                Vector2 edgeA = ca + dir * (ctrlA.Size.X * 0.5f - 2f);
                Vector2 edgeB = cb - dir * (ctrlB.Size.X * 0.5f - 2f);
                DrawLine(edgeA, edgeB, new Color(0.98f, 0.78f, 0.28f, 0.96f), 11f, true);
                DrawLine(edgeA, edgeB, new Color(1.00f, 0.94f, 0.68f, 1.00f), 4.5f, true);
            }

            for (int i = 0; i < Minigame.Path.Count; i++)
            {
                RuneLoomTile tile = Minigame.Path[i];
                if (!TileControls.TryGetValue((tile.X, tile.Y), out Control? ctrl))
                    continue;
                Vector2 badgeCenter = ctrl.Position + new Vector2(18f, 18f);
                DrawCircle(badgeCenter, 15.5f, new Color("#111d13"));
                DrawCircle(badgeCenter, 12.5f, new Color("#f2c858"));
                if (BadgeFont != null)
                {
                    DrawString(
                        BadgeFont,
                        badgeCenter + new Vector2(-12f, 6.5f),
                        (i + 1).ToString(),
                        HorizontalAlignment.Center,
                        24f,
                        18,
                        new Color("#142116"));
                }
            }
        }
    }

    private sealed partial class RuneTileNode : Control
    {
        public required RuneLoomTile Tile { get; init; }
        public required TextureRect StoneArt { get; init; }
        public required NinePatchRect GlowOutline { get; init; }
        public required TextureRect Icon { get; init; }
        public required Label TitleLabel { get; init; }
        public required Label ValueLabel { get; init; }

        public bool IsHovered { get; set; }
        private Tween? _scaleTween;

        public void AnimateScale(float target)
        {
            if (!IsInsideTree())
                return;
            _scaleTween?.Kill();
            _scaleTween = CreateTween();
            _scaleTween.TweenProperty(this, "scale", new Vector2(target, target), 0.10f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
        }
    }

    private RuneLoomMinigame _minigame = null!;
    private readonly List<RuneTileNode> _tileNodes = new(RuneLoomMinigame.GridWidth * RuneLoomMinigame.GridHeight);
    private PathChannelCanvas _channelCanvas = null!;
    private PathBadgeCanvas _badgeCanvas = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private RichTextLabel _gainsBodyRtl = null!;
    private RichTextLabel _costsBodyRtl = null!;
    private RichTextLabel _feedbackRtl = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _undoButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _resetButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _confirmButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _cancelButton = null!;

    private Texture2D? _stoneTex;
    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _numberFont;

    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _confirmButton.Disabled ? _cancelButton : _confirmButton;

    public static NRuneLoomScreen Create(RuneLoomMinigame minigame)
    {
        var screen = new NRuneLoomScreen
        {
            Name = "STS2ThingsRuneLoomScreen",
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.None,
        };
        screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        screen._minigame = minigame;
        screen.BuildUi();
        return screen;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public void AfterOverlayOpened()
    {
        if (_minigame != null)
            _minigame.StateChanged += RefreshAll;
        RefreshAll();
    }

    public void AfterOverlayClosed()
    {
        if (_minigame != null)
            _minigame.StateChanged -= RefreshAll;
        this.QueueFreeSafely();
    }

    public void AfterOverlayShown()
    {
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void AfterOverlayHidden()
    {
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (_minigame != null)
        {
            _minigame.StateChanged -= RefreshAll;
            _minigame.ForceEnd();
        }
    }

    private void BuildUi()
    {
        _titleFont = ThingsMinigameUiTheme.GetTitleFont();
        _bodyFont = ThingsMinigameUiTheme.GetBodyFont();
        _numberFont = ThingsMinigameUiTheme.GetNumberFont();
        _stoneTex = ThingsMinigameUiTheme.TryLoadTexture(StoneTilePath);

        // 1. Deep Overgrowth nocturnal forest outer fill
        var outerFill = new ColorRect
        {
            Color = new Color("#040d07"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 Crystal-Sphere artwork stage with pixel-locked carved altar cavity
        var stage = new Control
        {
            Name = "LoomStage",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(stage);
        stage.AnchorLeft = 0.5f;
        stage.AnchorTop = 0.5f;
        stage.AnchorRight = 0.5f;
        stage.AnchorBottom = 0.5f;
        stage.OffsetLeft = -960f;
        stage.OffsetTop = -540f;
        stage.OffsetRight = 960f;
        stage.OffsetBottom = 540f;

        var bg = new TextureRect
        {
            Name = "LoomBackdrop",
            Texture = ThingsMinigameUiTheme.TryLoadTexture(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 3. Embedded 630x630 carved stone altar cavity (exact alignment: 500..1130, 215..845)
        var playfield = new Control
        {
            Name = "LoomPlayfield",
            Position = new Vector2(500f, 215f),
            Size = new Vector2(630f, 630f),
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(playfield);

        // Animated painterly Overgrowth moss & breathing golden-amber sap shader over the painted emerald cavity
        Shader? sapShader = ThingsMinigameUiTheme.TryLoadShader(SapShaderPath);
        if (sapShader != null)
        {
            var sapFx = new ColorRect
            {
                Name = "SapBedFx",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.36f),
                Material = new ShaderMaterial { Shader = sapShader },
                MouseFilter = MouseFilterEnum.Ignore,
            };
            playfield.AddChild(sapFx);
            sapFx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        // 4x4 Grid layer: 4 * 140 + 3 * 14 = 602x602 centered cleanly with 14px inset inside 630x630 pit
        var boardLayer = new Control
        {
            Name = "LoomBoardLayer",
            Position = new Vector2(14f, 14f),
            Size = new Vector2(602f, 602f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        playfield.AddChild(boardLayer);

        _channelCanvas = new PathChannelCanvas
        {
            Minigame = _minigame,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        boardLayer.AddChild(_channelCanvas);
        _channelCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var grid = new GridContainer
        {
            Name = "LoomGrid",
            Columns = RuneLoomMinigame.GridWidth,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        boardLayer.AddChild(grid);
        grid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _badgeCanvas = new PathBadgeCanvas
        {
            Minigame = _minigame,
            BadgeFont = _numberFont,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 10,
        };
        boardLayer.AddChild(_badgeCanvas);
        _badgeCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        for (int y = 0; y < RuneLoomMinigame.GridHeight; y++)
        {
            for (int x = 0; x < RuneLoomMinigame.GridWidth; x++)
            {
                RuneLoomTile tile = _minigame.GetTile(x, y);
                int tx = x;
                int ty = y;

                var shadow = new TextureRect
                {
                    Texture = _stoneTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    Modulate = new Color(0f, 0f, 0f, 0.48f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };

                var stoneArt = new TextureRect
                {
                    Texture = _stoneTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                };

                var glowOutline = ThingsMinigameUiTheme.CreateChamferOutline(new Color(1.0f, 0.80f, 0.24f, 0.95f), visible: false);

                var box = new VBoxContainer
                {
                    Alignment = BoxContainer.AlignmentMode.Center,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                box.AddThemeConstantOverride("separation", 3);

                var icon = new TextureRect
                {
                    Texture = ResolveTileTexture(tile),
                    CustomMinimumSize = new Vector2(44f, 44f),
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                icon.Visible = icon.Texture != null;
                box.AddChild(icon);

                var titleLbl = ThingsMinigameUiTheme.MakeLabel(
                    GetTileTitle(tile),
                    16,
                    new Color("#f8fbf5"),
                    _bodyFont,
                    HorizontalAlignment.Center,
                    wrap: true,
                    outlineSize: 5,
                    outlineColor: new Color("#051008"));
                box.AddChild(titleLbl);

                var valLbl = ThingsMinigameUiTheme.MakeLabel(
                    GetTileEffectShort(tile),
                    15,
                    tile.IsCost ? new Color("#ffa29c") : (tile.Kind == RuneTileKind.Moss ? new Color("#a4c2aa") : new Color("#ffe48c")),
                    _bodyFont,
                    HorizontalAlignment.Center,
                    wrap: false,
                    outlineSize: 5,
                    outlineColor: new Color("#051008"));
                box.AddChild(valLbl);

                var node = new RuneTileNode
                {
                    Tile = tile,
                    StoneArt = stoneArt,
                    GlowOutline = glowOutline,
                    Icon = icon,
                    TitleLabel = titleLbl,
                    ValueLabel = valLbl,
                    CustomMinimumSize = new Vector2(140f, 140f),
                    PivotOffset = new Vector2(70f, 70f),
                    MouseFilter = MouseFilterEnum.Stop,
                    FocusMode = FocusModeEnum.None,
                    MouseDefaultCursorShape = CursorShape.PointingHand,
                };

                node.AddChild(shadow);
                shadow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                shadow.OffsetLeft = 4f;
                shadow.OffsetTop = 5f;
                shadow.OffsetRight = 4f;
                shadow.OffsetBottom = 5f;

                node.AddChild(stoneArt);
                stoneArt.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

                node.AddChild(glowOutline);
                glowOutline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                glowOutline.OffsetLeft = -5f;
                glowOutline.OffsetTop = -5f;
                glowOutline.OffsetRight = 5f;
                glowOutline.OffsetBottom = 5f;

                node.AddChild(box);
                box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                box.OffsetLeft = 8f;
                box.OffsetRight = -8f;
                box.OffsetTop = 10f;
                box.OffsetBottom = -8f;

                node.GuiInput += ev =>
                {
                    if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        _minigame.ClickTile(tx, ty, out _);
                        AcceptEvent();
                    }
                };
                node.MouseEntered += () =>
                {
                    node.IsHovered = true;
                    if (_minigame.CanAddTile(tx, ty, out _) || _minigame.GetPathIndex(tile) >= 0)
                        node.AnimateScale(1.04f);
                    RefreshTileVisual(node);
                };
                node.MouseExited += () =>
                {
                    node.IsHovered = false;
                    node.AnimateScale(1.0f);
                    RefreshTileVisual(node);
                };

                grid.AddChild(node);
                _channelCanvas.TileControls[(x, y)] = node;
                _badgeCanvas.TileControls[(x, y)] = node;
                _tileNodes.Add(node);
            }
        }

        // 4. Floating Crystal-Sphere HUD Layer
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Top-Left Floating Bold Gold Title
        var titleRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#f4d678]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.title")}[/color]",
            36,
            new Color("#f4d678"),
            new Color("#06130a"),
            _titleFont,
            outlineSize: 10,
            shadowX: 4,
            shadowY: 3,
            align: HorizontalAlignment.Left,
            wrap: false);
        hud.AddChild(titleRtl);
        titleRtl.Position = new Vector2(52f, 22f);
        titleRtl.CustomMinimumSize = new Vector2(500f, 52f);

        // Subtle floating caption directly below the stone altar
        var altarHintRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#a7d1b0]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.boardHint")}[/color]",
            17,
            new Color("#a7d1b0"),
            new Color("#041008"),
            _bodyFont,
            outlineSize: 6,
            shadowX: 2,
            shadowY: 2,
            align: HorizontalAlignment.Center,
            wrap: true);
        stage.AddChild(altarHintRtl);
        altarHintRtl.Position = new Vector2(465f, 992f);
        altarHintRtl.Size = new Vector2(700f, 64f);

        // Bottom-Left Dramatic Crystal-Sphere DivinationsLeft-style Floating Counter
        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            26,
            new Color("#eef6ef"),
            new Color("#051209"),
            _titleFont,
            outlineSize: 10,
            shadowX: 4,
            shadowY: 3,
            align: HorizontalAlignment.Left,
            wrap: true);
        hud.AddChild(_bottomStatusRtl);
        _bottomStatusRtl.AnchorLeft = 0f;
        _bottomStatusRtl.AnchorRight = 0f;
        _bottomStatusRtl.AnchorTop = 1f;
        _bottomStatusRtl.AnchorBottom = 1f;
        _bottomStatusRtl.OffsetLeft = 48f;
        _bottomStatusRtl.OffsetRight = 460f;
        _bottomStatusRtl.OffsetTop = -150f;
        _bottomStatusRtl.OffsetBottom = -18f;

        // Right Column (Single dark translucent octagonal plaque + hand-painted divine_button controls)
        var rightAnchor = new Control
        {
            Name = "RightUi",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hud.AddChild(rightAnchor);
        rightAnchor.AnchorLeft = 1f;
        rightAnchor.AnchorRight = 1f;
        rightAnchor.AnchorTop = 0f;
        rightAnchor.AnchorBottom = 1f;
        rightAnchor.OffsetLeft = -556f;
        rightAnchor.OffsetRight = -52f;
        rightAnchor.OffsetTop = 44f;
        rightAnchor.OffsetBottom = -44f;

        var rightCenter = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rightCenter.AddThemeConstantOverride("separation", 16);
        rightAnchor.AddChild(rightCenter);
        rightCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Single Octagonal Crystal-Sphere Plaque (tiny_nine_patch.png, deep nocturnal Overgrowth emerald)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.11f, 0.06f, 0.88f),
            padH: 26,
            padV: 20);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 10);
        plaque.AddChild(plaqueBox);

        // Concise Rules Header
        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#d8eadc]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.rules", ("MaxSteps", RuneLoomMinigame.MaxPathLength))}[/color]",
            16,
            new Color("#d8eadc"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2));

        plaqueBox.AddChild(MakeDivider(new Color("#366c49", 0.55f)));

        // Gains Preview Section
        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeLabel(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.gainsHeader"),
            20,
            new Color("#98ebaa"),
            _titleFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5));

        _gainsBodyRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#edf7ef"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2);
        _gainsBodyRtl.CustomMinimumSize = new Vector2(0f, 68f);
        plaqueBox.AddChild(_gainsBodyRtl);

        plaqueBox.AddChild(MakeDivider(new Color("#6b3c3c", 0.52f)));

        // Costs Preview Section
        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeLabel(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.costsHeader"),
            20,
            new Color("#f89d96"),
            _titleFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5));

        _costsBodyRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#f5dcd9"),
            new Color("#0e0404"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2);
        _costsBodyRtl.CustomMinimumSize = new Vector2(0f, 44f);
        plaqueBox.AddChild(_costsBodyRtl);

        plaqueBox.AddChild(MakeDivider(new Color("#366c49", 0.55f)));

        // Live Feedback Whisper
        _feedbackRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            16,
            new Color("#f4e1ad"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2);
        _feedbackRtl.CustomMinimumSize = new Vector2(0f, 42f);
        plaqueBox.AddChild(_feedbackRtl);

        // Undo / Reset Row (hand-painted divine_button tablets)
        var editRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        editRow.AddThemeConstantOverride("separation", 14);
        rightCenter.AddChild(editRow);

        _undoButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnUndo"),
            new Vector2(245f, 58f),
            baseTint: new Color("#648c70"),
            activeTint: new Color("#7cb38c"),
            outlineColor: new Color(0.55f, 0.94f, 0.66f, 0.90f),
            font: _bodyFont,
            fontSize: 18,
            textColor: new Color("#eef7ef"));
        _undoButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _undoButton.Pressed += () => _minigame.UndoLast();
        editRow.AddChild(_undoButton);

        _resetButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnReset"),
            new Vector2(245f, 58f),
            baseTint: new Color("#648c70"),
            activeTint: new Color("#7cb38c"),
            outlineColor: new Color(0.55f, 0.94f, 0.66f, 0.90f),
            font: _bodyFont,
            fontSize: 18,
            textColor: new Color("#eef7ef"));
        _resetButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _resetButton.Pressed += () => _minigame.ResetPath();
        editRow.AddChild(_resetButton);

        // Primary Confirm / Cancel Row
        var actionRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        actionRow.AddThemeConstantOverride("separation", 14);
        rightCenter.AddChild(actionRow);

        _confirmButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnConfirm"),
            new Vector2(320f, 76f),
            baseTint: new Color("#5ea372"),
            activeTint: new Color("#70c486"),
            outlineColor: new Color(1.0f, 0.80f, 0.24f, 1.0f),
            font: _titleFont,
            fontSize: 23,
            textColor: new Color("#fff8d9"));
        _confirmButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirmButton.Pressed += () => _minigame.ConfirmPath();
        actionRow.AddChild(_confirmButton);

        _cancelButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnCancel"),
            new Vector2(170f, 76f),
            baseTint: new Color("#8a7068"),
            activeTint: new Color("#a8897f"),
            outlineColor: new Color(0.95f, 0.72f, 0.62f, 0.85f),
            font: _bodyFont,
            fontSize: 19,
            textColor: new Color("#f7eae3"));
        _cancelButton.Pressed += () => _minigame.Cancel();
        actionRow.AddChild(_cancelButton);
    }

    private static ColorRect MakeDivider(Color color) => new()
    {
        Color = color,
        CustomMinimumSize = new Vector2(0f, 2f),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private void RefreshAll()
    {
        string stepsLine = ThingsMinigameUiTheme.Tr(
            "THINGS_MOSS_RUNE_LOOM.ui.stepsLabel",
            ("Used", $"[color=#ffd100]{_minigame.Path.Count}[/color]"),
            ("Max", RuneLoomMinigame.MaxPathLength));
        string cardLine = ThingsMinigameUiTheme.Tr(
            "THINGS_MOSS_RUNE_LOOM.ui.cardLabel",
            ("Card", $"[color=#f8df8c]{_minigame.SelectedCard.Title}[/color]"));
        string statsLine = ThingsMinigameUiTheme.Tr(
            "THINGS_MOSS_RUNE_LOOM.ui.playerStats",
            ("Hp", _minigame.Owner.Creature.CurrentHp),
            ("MaxHp", _minigame.Owner.Creature.MaxHp),
            ("Gold", _minigame.Owner.Gold));
        _bottomStatusRtl.Text = $"{stepsLine}\n[font_size=20]{cardLine}[/font_size]\n[font_size=17][color=#b4dfc0]{statsLine}[/color][/font_size]";

        ThingsRuneLoomOutcome preview = _minigame.BuildOutcome();

        var gains = new List<string>();
        if (preview.UpgradeCard)
            gains.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryUpgrade", ("Card", _minigame.SelectedCard.Title)));
        if (preview.EnchantmentKind != RuneLoomEnchantmentKind.None)
        {
            EnchantmentModel? ench = RuneLoomMinigame.GetCanonicalEnchantment(preview.EnchantmentKind);
            string enchName = ench != null ? ench.Title.GetFormattedText() : preview.EnchantmentKind.ToString();
            string label = ench != null && ench.ShowAmount
                ? ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryEnchantAmount", ("Name", enchName), ("Amount", preview.EnchantmentAmount))
                : ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryEnchant", ("Name", enchName));
            gains.Add(label);
        }
        if (preview.HealAmount > 0)
            gains.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryHeal", ("Amount", preview.HealAmount)));
        if (preview.GoldGain > 0)
            gains.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryGold", ("Amount", preview.GoldGain)));
        if (preview.MaxHpGain > 0)
            gains.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryMaxHp", ("Amount", preview.MaxHpGain)));

        _gainsBodyRtl.Text = gains.Count > 0
            ? $"[color=#eef9f0]{string.Join("\n", gains)}[/color]"
            : $"[color=#8cad94]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryNoGains")}[/color]";

        var costs = new List<string>();
        if (preview.DamageAmount > 0)
            costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryDamage", ("Amount", preview.DamageAmount)));
        if (preview.GoldLoss > 0)
            costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryGoldLoss", ("Amount", preview.GoldLoss)));
        if (preview.MaxHpLoss > 0)
            costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryMaxHpLoss", ("Amount", preview.MaxHpLoss)));
        if (preview.CurseCount > 0)
            costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryCurse", ("Curse", ModelDb.Card<Clumsy>().Title)));

        string costBody = costs.Count > 0
            ? $"[color=#ffccc8]{string.Join("\n", costs)}[/color]"
            : $"[color=#92a896]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryNoCosts")}[/color]";
        if (_minigame.IsLethalWarning)
            costBody += $"\n[color=#ff726b]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.lethalWarning")}[/color]";
        _costsBodyRtl.Text = costBody;

        _feedbackRtl.Text = $"[color=#f4e2b0]{ThingsMinigameUiTheme.Tr(_minigame.LastFeedbackKey)}[/color]";

        bool hasPath = _minigame.Path.Count > 0;
        bool canConfirm = _minigame.CanConfirm();
        _undoButton.Disabled = !hasPath;
        _resetButton.Disabled = !hasPath;
        _confirmButton.Disabled = !canConfirm;
        _confirmButton.Active = canConfirm;

        RefreshTiles();
        _channelCanvas.QueueRedraw();
        _badgeCanvas.QueueRedraw();
    }

    private void RefreshTiles()
    {
        foreach (RuneTileNode node in _tileNodes)
            RefreshTileVisual(node);
    }

    private void RefreshTileVisual(RuneTileNode node)
    {
        RuneLoomTile tile = node.Tile;
        int idx = _minigame.GetPathIndex(tile);
        bool inPath = idx >= 0;
        bool canClick = _minigame.CanAddTile(tile.X, tile.Y, out _);

        Color baseTint = tile.Kind switch
        {
            RuneTileKind.Upgrade => new Color("#7c7238"),
            RuneTileKind.Enchantment => new Color("#33767d"),
            RuneTileKind.Moss => new Color("#46604d"),
            RuneTileKind.ThornCut or RuneTileKind.SapDrain or RuneTileKind.BlightVine or RuneTileKind.ClingingTwig => new Color("#82373b"),
            _ => new Color("#37734b"),
        };

        if (inPath)
        {
            node.StoneArt.SelfModulate = baseTint.Lightened(node.IsHovered ? 0.28f : 0.20f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(1.0f, 0.80f, 0.22f, 0.98f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else if (canClick && _minigame.Path.Count > 0)
        {
            node.StoneArt.SelfModulate = baseTint.Lightened(node.IsHovered ? 0.22f : 0.10f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(0.45f, 0.98f, 0.58f, node.IsHovered ? 0.98f : 0.72f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else if (canClick)
        {
            node.StoneArt.SelfModulate = node.IsHovered ? baseTint.Lightened(0.18f) : baseTint;
            node.GlowOutline.Visible = node.IsHovered;
            node.GlowOutline.Modulate = new Color(0.95f, 0.88f, 0.52f, 0.78f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else
        {
            node.StoneArt.SelfModulate = baseTint.Darkened(0.24f);
            node.GlowOutline.Visible = false;
            node.Modulate = new Color(0.80f, 0.84f, 0.81f, 0.94f);
            node.MouseDefaultCursorShape = CursorShape.Arrow;
        }
    }

    private static Texture2D? ResolveTileTexture(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.Upgrade => ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/hammer_time_power.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/rest_site/option_smith.png"),
        RuneTileKind.Enchantment => ResolveEnchantmentTexture(tile.EnchantmentKind),
        RuneTileKind.SapHeal => ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/regen_power.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png"),
        RuneTileKind.AmberGold => ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/reward_screen/reward_icon_money.png"),
        RuneTileKind.Heartwood => ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png"),
        RuneTileKind.ThornCut => ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/thorns_power.png"),
        RuneTileKind.SapDrain => ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png"),
        RuneTileKind.BlightVine => ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/poison_power.png"),
        RuneTileKind.ClingingTwig => ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_curse.png"),
        _ => null,
    };

    private static Texture2D? ResolveEnchantmentTexture(RuneLoomEnchantmentKind kind)
    {
        EnchantmentModel? ench = RuneLoomMinigame.GetCanonicalEnchantment(kind);
        return ench != null ? ThingsMinigameUiTheme.TryLoadTexture(ench.IconPath) : null;
    }

    private static string GetTileTitle(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.Upgrade => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileUpgrade"),
        RuneTileKind.Enchantment => RuneLoomMinigame.GetCanonicalEnchantment(tile.EnchantmentKind)?.Title.GetFormattedText()
            ?? tile.EnchantmentKind.ToString(),
        RuneTileKind.SapHeal => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileSapHeal"),
        RuneTileKind.AmberGold => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileAmberGold"),
        RuneTileKind.Heartwood => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileHeartwood"),
        RuneTileKind.ThornCut => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileThornCut"),
        RuneTileKind.SapDrain => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileSapDrain"),
        RuneTileKind.BlightVine => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileBlightVine"),
        RuneTileKind.ClingingTwig => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileClingingTwig"),
        _ => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.tileMoss"),
    };

    private static string GetTileEffectShort(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.Upgrade => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valUpgrade"),
        RuneTileKind.Enchantment => FormatEnchantmentShort(tile.EnchantmentKind, tile.Value),
        RuneTileKind.SapHeal => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valHeal", ("Amount", tile.Value)),
        RuneTileKind.AmberGold => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valGold", ("Amount", tile.Value)),
        RuneTileKind.Heartwood => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMaxHp", ("Amount", tile.Value)),
        RuneTileKind.ThornCut => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valDamage", ("Amount", tile.Value)),
        RuneTileKind.SapDrain => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valGoldLoss", ("Amount", tile.Value)),
        RuneTileKind.BlightVine => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMaxHpLoss", ("Amount", tile.Value)),
        RuneTileKind.ClingingTwig => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valCurse", ("Curse", ModelDb.Card<Clumsy>().Title)),
        _ => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMoss"),
    };

    private static string FormatEnchantmentShort(RuneLoomEnchantmentKind kind, int amount) => kind switch
    {
        RuneLoomEnchantmentKind.Sharp => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valSharp", ("Amount", amount)),
        RuneLoomEnchantmentKind.Vigorous => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valVigorous", ("Amount", amount)),
        RuneLoomEnchantmentKind.Nimble => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valNimble", ("Amount", amount)),
        RuneLoomEnchantmentKind.Adroit => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valAdroit", ("Amount", amount)),
        RuneLoomEnchantmentKind.Steady => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valSteady"),
        RuneLoomEnchantmentKind.Swift => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valSwift", ("Amount", amount)),
        RuneLoomEnchantmentKind.Clone => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valClone"),
        RuneLoomEnchantmentKind.Sown => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valSown", ("Amount", amount)),
        _ => string.Empty,
    };
}
