using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Overgrowth (Act 1A) 4x4 Overgrown Rune Loom overlay screen modeled after vanilla CrystalSphere:
/// full-bleed hand-painted moss-stone altar backdrop, animated amber-sap cavity, clean iconic carved
/// limestone tablets (no SaaS text cards — pure large glyphs + minimal numbers + native STS2 HoverTips),
/// glowing amber sap links, narrow right preview plaque, and bottom-right native ProceedButton.
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
                DrawLine(a, b, new Color(0.95f, 0.70f, 0.22f, 0.52f), 20f, true);
                DrawLine(a, b, new Color(1.00f, 0.88f, 0.48f, 0.92f), 7f, true);
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
                DrawLine(edgeA, edgeB, new Color(0.96f, 0.76f, 0.26f, 0.90f), 10f, true);
                DrawLine(edgeA, edgeB, new Color(1.00f, 0.94f, 0.68f, 0.96f), 4f, true);
            }

            for (int i = 0; i < Minigame.Path.Count; i++)
            {
                RuneLoomTile tile = Minigame.Path[i];
                if (!TileControls.TryGetValue((tile.X, tile.Y), out Control? ctrl))
                    continue;
                Vector2 badgeCenter = ctrl.Position + new Vector2(18f, 18f);
                DrawCircle(badgeCenter, 15f, new Color("#111d13"));
                DrawCircle(badgeCenter, 12f, new Color("#e8be4e"));
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
        public required Label BadgeLabel { get; init; }

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
    private ThingsMinigameUiTheme.FloatingPopLayer _popLayer = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private RichTextLabel _rulesBodyRtl = null!;
    private VBoxContainer _previewSection = null!;
    private RichTextLabel _gainsBodyRtl = null!;
    private VBoxContainer _costsSection = null!;
    private RichTextLabel _costsBodyRtl = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _undoButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _resetButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _cancelButton = null!;
    private ThingsMinigameUiTheme.NativeProceedButton _confirmButton = null!;

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

        // 2. Centered 1920x1080 artwork stage with pixel-locked carved altar cavity
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

        // 3. Embedded 630x630 carved stone altar cavity (500..1130, 215..845)
        var playfield = new Control
        {
            Name = "LoomPlayfield",
            Position = new Vector2(500f, 215f),
            Size = new Vector2(630f, 630f),
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(playfield);

        Shader? sapShader = ThingsMinigameUiTheme.TryLoadShader(SapShaderPath);
        if (sapShader != null)
        {
            var sapFx = new ColorRect
            {
                Name = "SapBedFx",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.34f),
                Material = new ShaderMaterial { Shader = sapShader },
                MouseFilter = MouseFilterEnum.Ignore,
            };
            playfield.AddChild(sapFx);
            sapFx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        // 4x4 Grid layer: 4 * 140 + 3 * 14 = 602x602 centered with 14px inset inside 630x630 pit
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
                    Modulate = new Color(0f, 0f, 0f, 0.45f),
                    MouseFilter = MouseFilterEnum.Ignore,
                };

                var stoneArt = new TextureRect
                {
                    Texture = _stoneTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                };

                var glowOutline = ThingsMinigameUiTheme.CreateChamferOutline(new Color(0.95f, 0.76f, 0.24f, 0.72f), visible: false);

                Texture2D? tileTex = ResolveTileTexture(tile);
                string badgeText = GetTileNumericBadge(tile);
                bool hasBadge = !string.IsNullOrEmpty(badgeText);

                // Large centered iconic glyph — NO SaaS card title/subtitle stacks!
                var icon = new TextureRect
                {
                    Texture = tileTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Modulate = tile.Kind == RuneTileKind.SapDrain ? new Color(1.0f, 0.72f, 0.72f, 1f) : Colors.White,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Visible = tileTex != null,
                };

                var badgeLbl = ThingsMinigameUiTheme.MakeLabel(
                    badgeText,
                    23,
                    tile.IsCost ? new Color("#ffa29b") : new Color("#ffe88c"),
                    _numberFont,
                    HorizontalAlignment.Right,
                    wrap: false,
                    outlineSize: 7,
                    outlineColor: new Color("#051008"));
                badgeLbl.Visible = hasBadge;

                var node = new RuneTileNode
                {
                    Tile = tile,
                    StoneArt = stoneArt,
                    GlowOutline = glowOutline,
                    Icon = icon,
                    BadgeLabel = badgeLbl,
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

                node.AddChild(icon);
                icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                icon.OffsetLeft = 36f;
                icon.OffsetRight = -36f;
                icon.OffsetTop = hasBadge ? 28f : 35f;
                icon.OffsetBottom = hasBadge ? -42f : -35f;

                node.AddChild(badgeLbl);
                badgeLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
                badgeLbl.OffsetLeft = 10f;
                badgeLbl.OffsetRight = -12f;
                badgeLbl.OffsetTop = -36f;
                badgeLbl.OffsetBottom = -8f;

                node.GuiInput += ev =>
                {
                    if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        if (!_minigame.ClickTile(tx, ty, out RuneClickRejection rejection) && rejection != RuneClickRejection.None)
                        {
                            Vector2 popPos = new Vector2(14f, 14f) + node.Position + node.Size * 0.5f;
                            _popLayer.Pop(popPos, ThingsMinigameUiTheme.Tr(_minigame.LastFeedbackKey), new Color("#ff9d96"), 20);
                        }
                        AcceptEvent();
                    }
                };
                node.MouseEntered += () =>
                {
                    node.IsHovered = true;
                    if (_minigame.CanAddTile(tx, ty, out _) || _minigame.GetPathIndex(tile) >= 0)
                        node.AnimateScale(1.04f);
                    RefreshTileVisual(node);
                    ShowTileHoverTip(node);
                };
                node.MouseExited += () =>
                {
                    node.IsHovered = false;
                    node.AnimateScale(1.0f);
                    RefreshTileVisual(node);
                    ThingsMinigameUiTheme.HideHoverTip(node);
                };

                grid.AddChild(node);
                _channelCanvas.TileControls[(x, y)] = node;
                _badgeCanvas.TileControls[(x, y)] = node;
                _tileNodes.Add(node);
            }
        }

        _popLayer = ThingsMinigameUiTheme.FloatingPopLayer.Create(_bodyFont);
        _popLayer.ZIndex = 20;
        playfield.AddChild(_popLayer);

        // 4. Minimal vanilla CrystalSphere-style HUD Layer (no top-left title, no bottom helper bar)
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Bottom-Left: Single clean steps + card name line (HP & Gold already sit in top bar!)
        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            28,
            ThingsMinigameUiTheme.VanillaCream,
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
        _bottomStatusRtl.OffsetLeft = 56f;
        _bottomStatusRtl.OffsetRight = 460f;
        _bottomStatusRtl.OffsetTop = -114f;
        _bottomStatusRtl.OffsetBottom = -34f;

        // Right Column: Narrow vanilla CrystalSphere 372px width (x = 1496..1868)
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
        rightAnchor.OffsetLeft = -424f;
        rightAnchor.OffsetRight = -52f;
        rightAnchor.OffsetTop = 64f;
        rightAnchor.OffsetBottom = -196f;

        var rightCenter = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rightCenter.AddThemeConstantOverride("separation", 14);
        rightAnchor.AddChild(rightCenter);
        rightCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Single clean translucent Overgrowth plaque (NO horizontal dividers, NO console log!)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.11f, 0.06f, 0.86f),
            padH: 24,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 14);
        plaque.AddChild(plaqueBox);

        _rulesBodyRtl = ThingsMinigameUiTheme.MakeVanillaPlaqueBody(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.rules", ("MaxSteps", RuneLoomMinigame.MaxPathLength)),
            _bodyFont,
            fontSize: 18);
        plaqueBox.AddChild(_rulesBodyRtl);

        _previewSection = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _previewSection.AddThemeConstantOverride("separation", 8);
        plaqueBox.AddChild(_previewSection);

        _previewSection.AddChild(ThingsMinigameUiTheme.MakeVanillaPlaqueHeader(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.gainsHeader"),
            _titleFont,
            fontSize: 20,
            color: new Color("#98ebaa"),
            align: HorizontalAlignment.Left));

        _gainsBodyRtl = ThingsMinigameUiTheme.MakeVanillaPlaqueBody(string.Empty, _bodyFont, fontSize: 18);
        _previewSection.AddChild(_gainsBodyRtl);

        _costsSection = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _costsSection.AddThemeConstantOverride("separation", 6);
        _previewSection.AddChild(_costsSection);

        _costsSection.AddChild(ThingsMinigameUiTheme.MakeVanillaPlaqueHeader(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.costsHeader"),
            _titleFont,
            fontSize: 20,
            color: new Color("#f89d96"),
            align: HorizontalAlignment.Left));

        _costsBodyRtl = ThingsMinigameUiTheme.MakeVanillaPlaqueBody(string.Empty, _bodyFont, fontSize: 18);
        _costsSection.AddChild(_costsBodyRtl);

        // Compact Undo / Reset row
        var editRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        editRow.AddThemeConstantOverride("separation", 12);
        rightCenter.AddChild(editRow);

        _undoButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnUndo"),
            new Vector2(180f, 56f),
            baseTint: new Color("#61876b"),
            activeTint: new Color("#77a884"),
            outlineColor: new Color(0.65f, 0.88f, 0.65f, 0.72f),
            font: _bodyFont,
            fontSize: 19,
            textColor: ThingsMinigameUiTheme.VanillaCream);
        _undoButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _undoButton.Pressed += () => _minigame.UndoLast();
        editRow.AddChild(_undoButton);

        _resetButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnReset"),
            new Vector2(180f, 56f),
            baseTint: new Color("#61876b"),
            activeTint: new Color("#77a884"),
            outlineColor: new Color(0.65f, 0.88f, 0.65f, 0.72f),
            font: _bodyFont,
            fontSize: 19,
            textColor: ThingsMinigameUiTheme.VanillaCream);
        _resetButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _resetButton.Pressed += () => _minigame.ResetPath();
        editRow.AddChild(_resetButton);

        _cancelButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnCancel"),
            new Vector2(372f, 56f),
            baseTint: new Color("#7a6862"),
            activeTint: new Color("#968079"),
            outlineColor: new Color(0.90f, 0.70f, 0.62f, 0.70f),
            font: _bodyFont,
            fontSize: 19,
            textColor: new Color("#f2e6df"));
        _cancelButton.Pressed += () => _minigame.Cancel();
        rightCenter.AddChild(_cancelButton);

        // Authentic bottom-right vanilla STS2 ProceedButton for Confirm!
        _confirmButton = ThingsMinigameUiTheme.NativeProceedButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.btnConfirm"),
            _titleFont,
            fontSize: 28);
        _confirmButton.AnchorBottomRight(rightInset: 54f, bottomInset: 48f);
        _confirmButton.Pressed += () => _minigame.ConfirmPath();
        hud.AddChild(_confirmButton);
    }

    private void ShowTileHoverTip(RuneTileNode node)
    {
        RuneLoomTile tile = node.Tile;
        if (tile.Kind == RuneTileKind.Enchantment)
        {
            EnchantmentModel? ench = RuneLoomMinigame.GetCanonicalEnchantment(tile.EnchantmentKind);
            if (ench != null)
            {
                ThingsMinigameUiTheme.ShowEnchantmentHoverTip(node, ench, tile.Value, HoverTipAlignment.Right);
                return;
            }
        }

        string titleKey = GetTileTitleKey(tile);
        string desc = GetTileEffectShort(tile);
        ThingsMinigameUiTheme.ShowLocalizedHoverTip(node, titleKey, desc, node.Icon.Texture, HoverTipAlignment.Right);
    }

    private void RefreshAll()
    {
        string stepsLine = ThingsMinigameUiTheme.Tr(
            "THINGS_MOSS_RUNE_LOOM.ui.stepsLabel",
            ("Used", $"[color=#ffd100]{_minigame.Path.Count}[/color]"),
            ("Max", RuneLoomMinigame.MaxPathLength));
        string cardLine = ThingsMinigameUiTheme.Tr(
            "THINGS_MOSS_RUNE_LOOM.ui.cardLabel",
            ("Card", $"[color=#f8df8c]{_minigame.SelectedCard.Title}[/color]"));
        _bottomStatusRtl.Text = $"{stepsLine}\n[font_size=20]{cardLine}[/font_size]";

        bool hasPath = _minigame.Path.Count > 0;
        _rulesBodyRtl.Visible = !hasPath;
        _previewSection.Visible = hasPath;

        if (hasPath)
        {
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
                ? string.Join("\n", gains)
                : $"[color=#a0b8a4]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryNoGains")}[/color]";

            var costs = new List<string>();
            if (preview.DamageAmount > 0)
                costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryDamage", ("Amount", preview.DamageAmount)));
            if (preview.GoldLoss > 0)
                costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryGoldLoss", ("Amount", preview.GoldLoss)));
            if (preview.MaxHpLoss > 0)
                costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryMaxHpLoss", ("Amount", preview.MaxHpLoss)));
            if (preview.CurseCount > 0)
                costs.Add(ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.summaryCurse", ("Curse", ModelDb.Card<Clumsy>().Title)));

            bool showCosts = costs.Count > 0 || _minigame.IsLethalWarning;
            _costsSection.Visible = showCosts;
            if (showCosts)
            {
                string costBody = $"[color=#ffccc8]{string.Join("\n", costs)}[/color]";
                if (_minigame.IsLethalWarning)
                    costBody += $"\n[color=#ff726b]{ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.lethalWarning")}[/color]";
                _costsBodyRtl.Text = costBody;
            }
        }

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
            node.StoneArt.SelfModulate = baseTint.Lightened(node.IsHovered ? 0.24f : 0.16f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(0.96f, 0.76f, 0.22f, 0.74f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else if (canClick && _minigame.Path.Count > 0)
        {
            node.StoneArt.SelfModulate = baseTint.Lightened(node.IsHovered ? 0.20f : 0.08f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(0.56f, 0.88f, 0.56f, node.IsHovered ? 0.68f : 0.36f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else if (canClick)
        {
            node.StoneArt.SelfModulate = node.IsHovered ? baseTint.Lightened(0.16f) : baseTint;
            node.GlowOutline.Visible = node.IsHovered;
            node.GlowOutline.Modulate = new Color(0.95f, 0.86f, 0.52f, 0.52f);
            node.Modulate = Colors.White;
            node.MouseDefaultCursorShape = CursorShape.PointingHand;
        }
        else
        {
            node.StoneArt.SelfModulate = baseTint.Darkened(0.24f);
            node.GlowOutline.Visible = false;
            node.Modulate = new Color(0.78f, 0.82f, 0.79f, 0.92f);
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
        RuneTileKind.Heartwood => ThingsMinigameUiTheme.TryLoadTexture("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png"),
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

    private static string GetTileNumericBadge(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.SapHeal => $"+{tile.Value}",
        RuneTileKind.AmberGold => $"+{tile.Value}",
        RuneTileKind.Heartwood => $"+{tile.Value}",
        RuneTileKind.ThornCut => $"-{tile.Value}",
        RuneTileKind.SapDrain => $"-{tile.Value}",
        RuneTileKind.BlightVine => $"-{tile.Value}",
        RuneTileKind.Enchantment => FormatEnchantmentNumericBadge(tile.EnchantmentKind, tile.Value),
        _ => string.Empty,
    };

    private static string FormatEnchantmentNumericBadge(RuneLoomEnchantmentKind kind, int value)
    {
        EnchantmentModel? ench = RuneLoomMinigame.GetCanonicalEnchantment(kind);
        return ench != null && ench.ShowAmount ? $"+{value}" : string.Empty;
    }

    private static string GetTileTitleKey(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.Upgrade => "THINGS_MOSS_RUNE_LOOM.ui.tileUpgrade",
        RuneTileKind.SapHeal => "THINGS_MOSS_RUNE_LOOM.ui.tileSapHeal",
        RuneTileKind.AmberGold => "THINGS_MOSS_RUNE_LOOM.ui.tileAmberGold",
        RuneTileKind.Heartwood => "THINGS_MOSS_RUNE_LOOM.ui.tileHeartwood",
        RuneTileKind.ThornCut => "THINGS_MOSS_RUNE_LOOM.ui.tileThornCut",
        RuneTileKind.SapDrain => "THINGS_MOSS_RUNE_LOOM.ui.tileSapDrain",
        RuneTileKind.BlightVine => "THINGS_MOSS_RUNE_LOOM.ui.tileBlightVine",
        RuneTileKind.ClingingTwig => "THINGS_MOSS_RUNE_LOOM.ui.tileClingingTwig",
        _ => "THINGS_MOSS_RUNE_LOOM.ui.tileMoss",
    };

    private static string GetTileEffectShort(RuneLoomTile tile) => tile.Kind switch
    {
        RuneTileKind.Upgrade => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valUpgrade"),
        RuneTileKind.SapHeal => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valHeal", ("Amount", tile.Value)),
        RuneTileKind.AmberGold => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valGold", ("Amount", tile.Value)),
        RuneTileKind.Heartwood => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMaxHp", ("Amount", tile.Value)),
        RuneTileKind.ThornCut => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valDamage", ("Amount", tile.Value)),
        RuneTileKind.SapDrain => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valGoldLoss", ("Amount", tile.Value)),
        RuneTileKind.BlightVine => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMaxHpLoss", ("Amount", tile.Value)),
        RuneTileKind.ClingingTwig => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valCurse", ("Curse", ModelDb.Card<Clumsy>().Title)),
        _ => ThingsMinigameUiTheme.Tr("THINGS_MOSS_RUNE_LOOM.ui.valMoss"),
    };
}
