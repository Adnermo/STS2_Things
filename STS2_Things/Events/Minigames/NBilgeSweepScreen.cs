using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Underdocks (Act 1B) 6x6 Bilge Grate minesweeper overlay screen styled after vanilla CrystalSphere:
/// full-bleed hand-painted canal grate backdrop, animated dark water caustic shader inside the iron pit,
/// hand-painted riveted hatch tiles that part to unveil loot &amp; mines directly over the water,
/// dramatic floating outlined bottom-left HUD counter, and hand-painted beveled divine_button controls.
/// </summary>
public partial class NBilgeSweepScreen : Control, IOverlayScreen
{
    private const string BackdropPath = "res://images/events/minigames/things_bilge_sweep_bg.png";
    private const string TileCoverPath = "res://images/events/minigames/things_bilge_tile_cover.png";
    private const string TileGlintPath = "res://images/events/minigames/things_bilge_tile_glint.png";
    private const string FlagIconPath = "res://images/events/minigames/things_bilge_flag_icon.png";
    private const string ProbeIconPath = "res://images/events/minigames/things_bilge_probe_icon.png";
    private const string WaterShaderPath = "res://shaders/things_bilge_water.gdshader";

    private sealed partial class BilgeCellNode : Control
    {
        public required BilgeCell Cell { get; init; }
        public required ColorRect WaterWash { get; init; }
        public required TextureRect CoverArt { get; init; }
        public required NinePatchRect GlowOutline { get; init; }
        public required TextureRect FlagIcon { get; init; }
        public required TextureRect LootIcon { get; init; }
        public required Label NumberLabel { get; init; }
        public required Label SubLabel { get; init; }

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

    private BilgeSweepMinigame _minigame = null!;
    private bool _flagMode;
    private readonly List<BilgeCellNode> _cellNodes = new(BilgeSweepMinigame.GridWidth * BilgeSweepMinigame.GridHeight);

    private RichTextLabel _bottomStatusRtl = null!;
    private RichTextLabel _feedbackRtl = null!;
    private Label _haulGoldLabel = null!;
    private Label _haulPotionLabel = null!;
    private Label _haulCardLabel = null!;
    private Label _haulRelicLabel = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _probeModeButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _flagModeButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _primaryActionButton = null!;

    private Texture2D? _coverTex;
    private Texture2D? _glintTex;
    private Texture2D? _flagTex;
    private Texture2D? _probeTex;
    private Texture2D? _goldIcon;
    private Texture2D? _potionIcon;
    private Texture2D? _cardIcon;
    private Texture2D? _relicIcon;
    private Texture2D? _mineIcon;

    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _numberFont;

    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _primaryActionButton;

    public static NBilgeSweepScreen Create(BilgeSweepMinigame minigame)
    {
        var screen = new NBilgeSweepScreen
        {
            Name = "STS2ThingsBilgeSweepScreen",
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

        _coverTex = ThingsMinigameUiTheme.TryLoadTexture(TileCoverPath);
        _glintTex = ThingsMinigameUiTheme.TryLoadTexture(TileGlintPath) ?? _coverTex;
        _flagTex = ThingsMinigameUiTheme.TryLoadTexture(FlagIconPath);
        _probeTex = ThingsMinigameUiTheme.TryLoadTexture(ProbeIconPath);
        _goldIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/reward_screen/reward_icon_money.png");
        _potionIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_potion.png");
        _cardIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/reward_screen/reward_icon_card.png");
        _relicIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/reward_screen/reward_icon_shared_relic.png");
        _mineIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/poison_power.png");

        // 1. Deep nocturnal canal outer fill (handles ultrawide/tall aspect ratios cleanly)
        var outerFill = new ColorRect
        {
            Color = new Color("#040d11"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 Crystal-Sphere-style artwork stage with pixel-locked pit playfield
        var stage = new Control
        {
            Name = "BilgeStage",
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
            Name = "BilgeBackdrop",
            Texture = ThingsMinigameUiTheme.TryLoadTexture(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 3. Embedded 630x630 iron bilge grate pit (exact pixel alignment with painted iron rim: 500..1130, 215..845)
        var playfield = new Control
        {
            Name = "BilgePlayfield",
            Position = new Vector2(500f, 215f),
            Size = new Vector2(630f, 630f),
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(playfield);

        // Animated painterly water caustic sheen blended smoothly over the hand-painted dark vortex
        Shader? waterShader = ThingsMinigameUiTheme.TryLoadShader(WaterShaderPath);
        if (waterShader != null)
        {
            var waterFx = new ColorRect
            {
                Name = "WaterCaustics",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.42f),
                Material = new ShaderMaterial { Shader = waterShader },
                MouseFilter = MouseFilterEnum.Ignore,
            };
            playfield.AddChild(waterFx);
            waterFx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        // 6x6 Grid: 6 * 96 + 5 * 6 = 606x606 centered cleanly with 12px inset inside 630x630 pit
        var grid = new GridContainer
        {
            Name = "BilgeGrid",
            Columns = BilgeSweepMinigame.GridWidth,
            Position = new Vector2(12f, 12f),
            Size = new Vector2(606f, 606f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        playfield.AddChild(grid);

        for (int y = 0; y < BilgeSweepMinigame.GridHeight; y++)
        {
            for (int x = 0; x < BilgeSweepMinigame.GridWidth; x++)
            {
                BilgeCell cell = _minigame.GetCell(x, y);
                int cx = x;
                int cy = y;

                var waterWash = new ColorRect
                {
                    Color = new Color(0.02f, 0.08f, 0.11f, 0.35f),
                    MouseFilter = MouseFilterEnum.Ignore,
                    Visible = false,
                };

                var coverArt = new TextureRect
                {
                    Texture = cell.HasGlint ? _glintTex : _coverTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                };

                var glowOutline = ThingsMinigameUiTheme.CreateChamferOutline(new Color(1f, 0.80f, 0.28f, 0.92f), visible: false);

                var flagIcon = new TextureRect
                {
                    Texture = _flagTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Visible = false,
                };

                var lootIcon = new TextureRect
                {
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Visible = false,
                };

                var numberLabel = ThingsMinigameUiTheme.MakeLabel(
                    string.Empty,
                    38,
                    Colors.White,
                    _numberFont,
                    HorizontalAlignment.Center,
                    wrap: false,
                    outlineSize: 8,
                    outlineColor: new Color("#030c10"));

                var subLabel = ThingsMinigameUiTheme.MakeLabel(
                    string.Empty,
                    16,
                    new Color("#f8e08e"),
                    _bodyFont,
                    HorizontalAlignment.Center,
                    wrap: false,
                    outlineSize: 6,
                    outlineColor: new Color("#030c10"));

                var node = new BilgeCellNode
                {
                    Cell = cell,
                    WaterWash = waterWash,
                    CoverArt = coverArt,
                    GlowOutline = glowOutline,
                    FlagIcon = flagIcon,
                    LootIcon = lootIcon,
                    NumberLabel = numberLabel,
                    SubLabel = subLabel,
                    CustomMinimumSize = new Vector2(96f, 96f),
                    PivotOffset = new Vector2(48f, 48f),
                    MouseFilter = MouseFilterEnum.Stop,
                    FocusMode = FocusModeEnum.None,
                    MouseDefaultCursorShape = CursorShape.PointingHand,
                };

                node.AddChild(waterWash);
                waterWash.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

                node.AddChild(coverArt);
                coverArt.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

                node.AddChild(glowOutline);
                glowOutline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                glowOutline.OffsetLeft = -4f;
                glowOutline.OffsetTop = -4f;
                glowOutline.OffsetRight = 4f;
                glowOutline.OffsetBottom = 4f;

                node.AddChild(flagIcon);
                flagIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                flagIcon.OffsetLeft = 18f;
                flagIcon.OffsetTop = 16f;
                flagIcon.OffsetRight = -18f;
                flagIcon.OffsetBottom = -16f;

                node.AddChild(lootIcon);
                lootIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                lootIcon.OffsetLeft = 20f;
                lootIcon.OffsetTop = 10f;
                lootIcon.OffsetRight = -20f;
                lootIcon.OffsetBottom = -28f;

                node.AddChild(numberLabel);
                numberLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

                node.AddChild(subLabel);
                subLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
                subLabel.OffsetTop = -25f;
                subLabel.OffsetBottom = -3f;

                node.GuiInput += ev => OnCellGuiInput(ev, cx, cy);
                node.MouseEntered += () =>
                {
                    node.IsHovered = true;
                    if (!node.Cell.IsRevealed && !_minigame.IsComplete)
                        node.AnimateScale(1.045f);
                    RefreshCellVisual(node);
                };
                node.MouseExited += () =>
                {
                    node.IsHovered = false;
                    node.AnimateScale(1.0f);
                    RefreshCellVisual(node);
                };

                grid.AddChild(node);
                _cellNodes.Add(node);
            }
        }

        // 4. Floating Crystal-Sphere HUD layer (edges & right column)
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Top-Left Floating Gold Event Title (matching vanilla CrystalSphere clean negative-space framing)
        var titleRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#f4d678]{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.title")}[/color]",
            36,
            new Color("#f4d678"),
            new Color("#06131a"),
            _titleFont,
            outlineSize: 10,
            shadowX: 4,
            shadowY: 3,
            align: HorizontalAlignment.Left,
            wrap: false);
        hud.AddChild(titleRtl);
        titleRtl.Position = new Vector2(52f, 22f);
        titleRtl.CustomMinimumSize = new Vector2(460f, 52f);

        // Subtle floating caption directly beneath the iron grate pit (sits cleanly over the dark timber beam!)
        var grateHintRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#a7ced8]{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.controlsHint")}[/color]",
            17,
            new Color("#a7ced8"),
            new Color("#041016"),
            _bodyFont,
            outlineSize: 6,
            shadowX: 2,
            shadowY: 2,
            align: HorizontalAlignment.Center,
            wrap: true);
        stage.AddChild(grateHintRtl);
        grateHintRtl.Position = new Vector2(465f, 1006f);
        grateHintRtl.Size = new Vector2(700f, 54f);

        // Bottom-Left Dramatic Crystal-Sphere DivinationsLeft-style Floating Counter
        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            27,
            new Color("#eef4f6"),
            new Color("#051117"),
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
        _bottomStatusRtl.OffsetRight = 450f;
        _bottomStatusRtl.OffsetTop = -140f;
        _bottomStatusRtl.OffsetBottom = -20f;

        // Right Column (CrystalSphere RightUi: hand-painted tool buttons -> octagonal plaque -> hand-painted finish button)
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
        rightAnchor.OffsetTop = 52f;
        rightAnchor.OffsetBottom = -52f;

        var rightCenter = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        rightCenter.AddThemeConstantOverride("separation", 18);
        rightAnchor.AddChild(rightCenter);
        rightCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Tool Switcher Row (2 chunky hand-painted divine_button.png tablets echoing Big/Small Divination buttons!)
        var modeRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        modeRow.AddThemeConstantOverride("separation", 14);
        rightCenter.AddChild(modeRow);

        _probeModeButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.modeProbe"),
            new Vector2(245f, 76f),
            baseTint: new Color("#78a5b5"),
            activeTint: new Color("#9ad4e6"),
            outlineColor: new Color(1.0f, 0.78f, 0.22f, 1.0f),
            font: _titleFont,
            fontSize: 17,
            icon: _probeTex,
            textColor: new Color("#f8e49c"));
        _probeModeButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _probeModeButton.Pressed += () => SetFlagMode(false);
        modeRow.AddChild(_probeModeButton);

        _flagModeButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.modeFlag"),
            new Vector2(245f, 76f),
            baseTint: new Color("#7c8792"),
            activeTint: new Color("#d1767c"),
            outlineColor: new Color(1.0f, 0.48f, 0.42f, 1.0f),
            font: _titleFont,
            fontSize: 17,
            icon: _flagTex,
            textColor: new Color("#fff0e8"));
        _flagModeButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _flagModeButton.Pressed += () => SetFlagMode(true);
        modeRow.AddChild(_flagModeButton);

        // Single Octagonal Crystal-Sphere Plaque (tiny_nine_patch.png, dark nocturnal teal)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.10f, 0.14f, 0.86f),
            padH: 26,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 12);
        plaque.AddChild(plaqueBox);

        // Salvaged Contraband Section
        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeLabel(
            ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.haulTitle"),
            22,
            new Color("#f2d06b"),
            _titleFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5));

        var haulGrid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        haulGrid.AddThemeConstantOverride("h_separation", 16);
        haulGrid.AddThemeConstantOverride("v_separation", 10);
        plaqueBox.AddChild(haulGrid);

        _haulGoldLabel = AddHaulRow(haulGrid, _goldIcon);
        _haulPotionLabel = AddHaulRow(haulGrid, _potionIcon);
        _haulCardLabel = AddHaulRow(haulGrid, _cardIcon);
        _haulRelicLabel = AddHaulRow(haulGrid, _relicIcon);

        plaqueBox.AddChild(MakeDivider(new Color("#2f6372", 0.55f)));

        // Rules & Glint Deduction Guide
        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#d8eaef]{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.rules", ("MineDamage", BilgeSweepMinigame.MineBurstDamage))}[/color]\n\n" +
            $"[color=#f2da8a]{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.legendGlint")}[/color]",
            16,
            new Color("#d8eaef"),
            new Color("#030c10"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2));

        plaqueBox.AddChild(MakeDivider(new Color("#2f6372", 0.55f)));

        // Live Action Log Whisper
        _feedbackRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#f4e2b0"),
            new Color("#030c10"),
            _bodyFont,
            outlineSize: 5,
            shadowX: 2,
            shadowY: 2);
        _feedbackRtl.CustomMinimumSize = new Vector2(0f, 52f);
        plaqueBox.AddChild(_feedbackRtl);

        // Primary Hand-Painted Cash-Out / Claim Button (divine_button.png + golden additive glow)
        _primaryActionButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            string.Empty,
            new Vector2(504f, 80f),
            baseTint: new Color("#6e9ca8"),
            activeTint: new Color("#6cb595"),
            outlineColor: new Color(1.0f, 0.78f, 0.24f, 1.0f),
            font: _titleFont,
            fontSize: 23,
            textColor: new Color("#fff6d6"));
        _primaryActionButton.Pressed += OnPrimaryActionPressed;
        rightCenter.AddChild(_primaryActionButton);
    }

    private static ColorRect MakeDivider(Color color) => new()
    {
        Color = color,
        CustomMinimumSize = new Vector2(0f, 2f),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private Label AddHaulRow(GridContainer container, Texture2D? icon)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 9);

        if (icon != null)
        {
            var tex = new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(30f, 30f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(tex);
        }

        var label = ThingsMinigameUiTheme.MakeLabel(
            string.Empty,
            18,
            new Color("#eff6f8"),
            _bodyFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        container.AddChild(row);
        return label;
    }

    private void OnCellGuiInput(InputEvent ev, int x, int y)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed)
            return;
        if (_minigame.IsComplete)
            return;

        if (mb.ButtonIndex == MouseButton.Right)
        {
            _minigame.ToggleFlag(x, y);
            AcceptEvent();
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (_flagMode)
                _minigame.ToggleFlag(x, y);
            else
                _minigame.ProbeCell(x, y);
            AcceptEvent();
        }
    }

    private void SetFlagMode(bool flagMode)
    {
        _flagMode = flagMode;
        RefreshModeButtons();
    }

    private void OnPrimaryActionPressed()
    {
        if (_minigame.IsComplete)
            _minigame.ConfirmExit();
        else
            _minigame.CashOutAndFinish();
    }

    private void RefreshAll()
    {
        RefreshModeButtons();
        RefreshStatus();
        RefreshCells();
    }

    private void RefreshModeButtons()
    {
        bool disabled = _minigame.IsComplete;
        _probeModeButton.Disabled = disabled;
        _flagModeButton.Disabled = disabled;
        _probeModeButton.Active = !disabled && !_flagMode;
        _flagModeButton.Active = !disabled && _flagMode;
    }

    private void RefreshStatus()
    {
        int curHp = _minigame.Owner.Creature.CurrentHp;
        int maxHp = _minigame.Owner.Creature.MaxHp;
        bool lethal = !_minigame.IsComplete && curHp <= BilgeSweepMinigame.MineBurstDamage;

        string probesLine = ThingsMinigameUiTheme.Tr(
            "THINGS_BILGE_MINES.ui.probesLabel",
            ("Remaining", $"[color=#ffd100]{_minigame.ProbesRemaining}[/color]"),
            ("Max", _minigame.MaxProbes));
        string hpColor = lethal ? "#ff7a75" : "#96e6a8";
        string hpLine = ThingsMinigameUiTheme.Tr(
            "THINGS_BILGE_MINES.ui.hpLabel",
            ("Current", $"[color={hpColor}]{curHp}[/color]"),
            ("Max", maxHp));
        string bottomBbcode = $"{probesLine}\n[font_size=21]{hpLine}[/font_size]";
        if (lethal)
        {
            string warn = ThingsMinigameUiTheme.Tr(
                "THINGS_BILGE_MINES.ui.lethalWarning",
                ("MineDamage", BilgeSweepMinigame.MineBurstDamage));
            bottomBbcode += $"\n[font_size=16][color=#ff847d]{warn}[/color][/font_size]";
        }
        _bottomStatusRtl.Text = bottomBbcode;

        _haulGoldLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.haulGold", ("Amount", _minigame.CollectedGold));
        _haulPotionLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.haulPotion", ("Count", _minigame.CollectedPotions));
        _haulCardLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.haulCard", ("Count", _minigame.CollectedCards));
        _haulRelicLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.haulRelic", ("Count", _minigame.CollectedCommonRelics));

        string feedbackText = ThingsMinigameUiTheme.Tr(
            _minigame.LastFeedbackKey,
            ("Value", _minigame.LastFeedbackValue),
            ("MineDamage", BilgeSweepMinigame.MineBurstDamage));
        string fbColor = _minigame.HitMine
            ? "#ff9892"
            : (_minigame.IsComplete ? "#9de8a9" : "#f5e1a8");
        _feedbackRtl.Text = $"[color={fbColor}]{feedbackText}[/color]";

        bool hasLoot = _minigame.BuildOutcome().HasAnyLoot;
        if (_minigame.IsComplete)
        {
            _primaryActionButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_BILGE_MINES.ui.btnClaim" : "THINGS_BILGE_MINES.ui.btnContinue");
            _primaryActionButton.SetColors(
                baseTint: _minigame.HitMine ? new Color("#c2646c") : new Color("#6bbd92"),
                activeTint: _minigame.HitMine ? new Color("#db747c") : new Color("#80d4a8"),
                outlineColor: _minigame.HitMine ? new Color(1.0f, 0.48f, 0.45f, 1.0f) : new Color(1.0f, 0.84f, 0.30f, 1.0f));
            _primaryActionButton.Active = true;
        }
        else
        {
            _primaryActionButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_BILGE_MINES.ui.btnCashOut" : "THINGS_BILGE_MINES.ui.btnLeaveEmpty");
            _primaryActionButton.SetColors(
                baseTint: hasLoot ? new Color("#68a88d") : new Color("#6b8c98"),
                activeTint: hasLoot ? new Color("#7cc7a6") : new Color("#80a6b5"),
                outlineColor: new Color(1.0f, 0.78f, 0.24f, 1.0f));
            _primaryActionButton.Active = hasLoot;
        }
    }

    private void RefreshCells()
    {
        foreach (BilgeCellNode node in _cellNodes)
            RefreshCellVisual(node);
    }

    private void RefreshCellVisual(BilgeCellNode node)
    {
        BilgeCell cell = node.Cell;
        node.LootIcon.Visible = false;
        node.FlagIcon.Visible = false;
        node.NumberLabel.Text = string.Empty;
        node.SubLabel.Text = string.Empty;
        node.GlowOutline.Visible = false;

        if (!cell.IsRevealed)
        {
            node.WaterWash.Visible = false;
            node.CoverArt.Visible = true;
            node.MouseDefaultCursorShape = _minigame.IsComplete ? CursorShape.Arrow : CursorShape.PointingHand;

            if (cell.IsFlagged)
            {
                node.CoverArt.Texture = _coverTex;
                node.CoverArt.SelfModulate = node.IsHovered ? new Color("#d89096") : new Color("#ba787e");
                node.FlagIcon.Visible = _flagTex != null;
                node.GlowOutline.Visible = true;
                node.GlowOutline.Modulate = new Color(1.0f, 0.45f, 0.42f, node.IsHovered ? 0.95f : 0.60f);
            }
            else if (cell.HasGlint)
            {
                node.CoverArt.Texture = _glintTex;
                node.CoverArt.SelfModulate = node.IsHovered ? new Color("#fff2cc") : Colors.White;
                if (node.IsHovered && !_minigame.IsComplete)
                {
                    node.GlowOutline.Visible = true;
                    node.GlowOutline.Modulate = new Color(1.0f, 0.84f, 0.30f, 0.92f);
                }
            }
            else
            {
                node.CoverArt.Texture = _coverTex;
                node.CoverArt.SelfModulate = node.IsHovered && !_minigame.IsComplete ? new Color("#d0eef5") : Colors.White;
                if (node.IsHovered && !_minigame.IsComplete)
                {
                    node.GlowOutline.Visible = true;
                    node.GlowOutline.Modulate = new Color(0.85f, 0.92f, 0.96f, 0.72f);
                }
            }
            return;
        }

        // Revealed cell: iron hatch lid vanishes cleanly to unveil the dark swirling canal water below!
        node.CoverArt.Visible = false;
        node.WaterWash.Visible = true;
        node.MouseDefaultCursorShape = CursorShape.Arrow;

        if (cell.IsMine)
        {
            node.WaterWash.Color = cell.WasTriggeredMine
                ? new Color(0.42f, 0.10f, 0.16f, 0.78f)
                : new Color(0.24f, 0.08f, 0.13f, 0.58f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = cell.WasTriggeredMine
                ? new Color(1.0f, 0.38f, 0.38f, 1.0f)
                : new Color(0.88f, 0.36f, 0.44f, 0.65f);
            node.LootIcon.Texture = _mineIcon;
            node.LootIcon.Visible = _mineIcon != null;
            node.SubLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellSac");
            node.SubLabel.AddThemeColorOverride("font_color", new Color("#ff9ba2"));
            return;
        }

        if (cell.LootKind != BilgeLootKind.None)
        {
            node.WaterWash.Color = new Color(0.08f, 0.25f, 0.20f, 0.62f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(0.88f, 0.84f, 0.42f, 0.78f);
            node.LootIcon.Texture = cell.LootKind switch
            {
                BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium => _goldIcon,
                BilgeLootKind.Potion => _potionIcon,
                BilgeLootKind.Card => _cardIcon,
                BilgeLootKind.CommonRelic => _relicIcon,
                _ => null,
            };
            node.LootIcon.Visible = node.LootIcon.Texture != null;
            node.SubLabel.Text = cell.LootKind switch
            {
                BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium => $"+{cell.GoldValue}G",
                BilgeLootKind.Potion => ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellPotion"),
                BilgeLootKind.Card => ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellCard"),
                BilgeLootKind.CommonRelic => ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellRelic"),
                _ => string.Empty,
            };
            node.SubLabel.AddThemeColorOverride("font_color", new Color("#fae28e"));

            if (cell.AdjacentMines > 0)
            {
                node.NumberLabel.Text = cell.AdjacentMines.ToString();
                node.NumberLabel.HorizontalAlignment = HorizontalAlignment.Right;
                node.NumberLabel.VerticalAlignment = VerticalAlignment.Top;
                node.NumberLabel.OffsetLeft = 0f;
                node.NumberLabel.OffsetTop = 3f;
                node.NumberLabel.OffsetRight = -7f;
                node.NumberLabel.OffsetBottom = 0f;
                node.NumberLabel.AddThemeFontSizeOverride("font_size", 20);
                node.NumberLabel.AddThemeColorOverride("font_color", GetNumberColor(cell.AdjacentMines));
            }
            return;
        }

        // Revealed safe open canal water
        node.WaterWash.Color = new Color(0.02f, 0.08f, 0.11f, 0.28f);
        node.NumberLabel.HorizontalAlignment = HorizontalAlignment.Center;
        node.NumberLabel.VerticalAlignment = VerticalAlignment.Center;
        node.NumberLabel.OffsetLeft = 0f;
        node.NumberLabel.OffsetTop = 0f;
        node.NumberLabel.OffsetRight = 0f;
        node.NumberLabel.OffsetBottom = 0f;
        node.NumberLabel.AddThemeFontSizeOverride("font_size", 38);
        if (cell.AdjacentMines > 0)
        {
            node.NumberLabel.Text = cell.AdjacentMines.ToString();
            node.NumberLabel.AddThemeColorOverride("font_color", GetNumberColor(cell.AdjacentMines));
        }
        else
        {
            node.NumberLabel.Text = string.Empty;
        }
    }

    private static Color GetNumberColor(int adjacentMines) => adjacentMines switch
    {
        1 => new Color("#7ee0ff"),
        2 => new Color("#8ef2a4"),
        3 => new Color("#ffd966"),
        4 => new Color("#ff9d6e"),
        _ => new Color("#ff7684"),
    };
}
