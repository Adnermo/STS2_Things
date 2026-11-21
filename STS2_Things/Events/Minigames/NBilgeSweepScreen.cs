using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Underdocks (Act 1B) 6x6 Bilge Grate minesweeper overlay screen modeled directly after vanilla CrystalSphere:
/// full-bleed hand-painted canal grate backdrop, animated dark water caustic shader inside the iron pit,
/// hand-painted riveted hatch tiles that part to unveil loot &amp; mines directly over the water,
/// single bold bottom-left counter, narrow right-side tool buttons + clean instructions plaque,
/// and authentic bottom-right STS2 ProceedButton.
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
    private ThingsMinigameUiTheme.CompactLootStrip _lootStrip = null!;
    private ThingsMinigameUiTheme.FloatingPopLayer _popLayer = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _probeModeButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _flagModeButton = null!;
    private ThingsMinigameUiTheme.NativeProceedButton _primaryActionButton = null!;

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

        // 1. Deep nocturnal canal outer fill
        var outerFill = new ColorRect
        {
            Color = new Color("#040d11"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 artwork stage with pixel-locked pit playfield
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

        // 3. Embedded 630x630 iron bilge grate pit (500..1130, 215..845)
        var playfield = new Control
        {
            Name = "BilgePlayfield",
            Position = new Vector2(500f, 215f),
            Size = new Vector2(630f, 630f),
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(playfield);

        Shader? waterShader = ThingsMinigameUiTheme.TryLoadShader(WaterShaderPath);
        if (waterShader != null)
        {
            var waterFx = new ColorRect
            {
                Name = "WaterCaustics",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.40f),
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
                    Color = new Color(0.02f, 0.08f, 0.11f, 0.32f),
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

                var glowOutline = ThingsMinigameUiTheme.CreateChamferOutline(new Color(0.95f, 0.76f, 0.28f, 0.65f), visible: false);

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
                    outlineSize: 7,
                    outlineColor: new Color("#030c10"));

                var subLabel = ThingsMinigameUiTheme.MakeLabel(
                    string.Empty,
                    16,
                    new Color("#f8e08e"),
                    _numberFont,
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
                lootIcon.OffsetLeft = 16f;
                lootIcon.OffsetTop = 12f;
                lootIcon.OffsetRight = -16f;
                lootIcon.OffsetBottom = -22f;

                node.AddChild(numberLabel);
                numberLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

                node.AddChild(subLabel);
                subLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
                subLabel.OffsetTop = -24f;
                subLabel.OffsetBottom = -2f;

                node.GuiInput += ev => OnCellGuiInput(ev, node, cx, cy);
                node.MouseEntered += () =>
                {
                    node.IsHovered = true;
                    if (!node.Cell.IsRevealed && !_minigame.IsComplete)
                        node.AnimateScale(1.04f);
                    RefreshCellVisual(node);
                    ShowCellHoverTip(node);
                };
                node.MouseExited += () =>
                {
                    node.IsHovered = false;
                    node.AnimateScale(1.0f);
                    RefreshCellVisual(node);
                    ThingsMinigameUiTheme.HideHoverTip(node);
                };

                grid.AddChild(node);
                _cellNodes.Add(node);
            }
        }

        // In-world floating pop layer directly over the pit (replaces console log panel)
        _popLayer = ThingsMinigameUiTheme.FloatingPopLayer.Create(_titleFont);
        _popLayer.ZIndex = 20;
        playfield.AddChild(_popLayer);

        // 4. Minimal vanilla CrystalSphere-style HUD layer (no top-left title, no bottom helper bar)
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Bottom-Left: Single bold vanilla DivinationsLeft counter (only adds red warning if lethal HP)
        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            30,
            ThingsMinigameUiTheme.VanillaCream,
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
        _bottomStatusRtl.OffsetLeft = 56f;
        _bottomStatusRtl.OffsetRight = 460f;
        _bottomStatusRtl.OffsetTop = -112f;
        _bottomStatusRtl.OffsetBottom = -36f;

        // Right Column: Authentic vanilla CrystalSphere narrow 372px width (x = 1496..1868)
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
        rightCenter.AddThemeConstantOverride("separation", 16);
        rightAnchor.AddChild(rightCenter);
        rightCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Two vertically stacked native-proportioned divine_button tools (like Big/Small Divination!)
        _probeModeButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.modeProbe"),
            new Vector2(372f, 76f),
            baseTint: new Color("#729aa8"),
            activeTint: new Color("#91c5d6"),
            outlineColor: ThingsMinigameUiTheme.VanillaWarmOutline,
            font: _titleFont,
            fontSize: 22,
            icon: _probeTex,
            textColor: new Color("#f6e095"));
        _probeModeButton.Pressed += () => SetFlagMode(false);
        rightCenter.AddChild(_probeModeButton);

        _flagModeButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.modeFlag"),
            new Vector2(372f, 76f),
            baseTint: new Color("#77828c"),
            activeTint: new Color("#c27076"),
            outlineColor: new Color(0.95f, 0.46f, 0.42f, 0.80f),
            font: _titleFont,
            fontSize: 22,
            icon: _flagTex,
            textColor: new Color("#fbeae3"));
        _flagModeButton.Pressed += () => SetFlagMode(true);
        rightCenter.AddChild(_flagModeButton);

        // Single clean translucent narrow Crystal-Sphere plaque (ZERO dividers, ZERO 0-value table, ZERO log)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.10f, 0.14f, 0.84f),
            padH: 24,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 14);
        plaque.AddChild(plaqueBox);

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeVanillaPlaqueBody(
            $"{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.rules", ("MineDamage", BilgeSweepMinigame.MineBurstDamage))}\n\n" +
            $"[color=#f2da8a]{ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.legendGlint")}[/color]",
            _bodyFont,
            fontSize: 18));

        _lootStrip = ThingsMinigameUiTheme.CompactLootStrip.Create(_numberFont);
        plaqueBox.AddChild(_lootStrip);

        // Authentic bottom-right vanilla STS2 ProceedButton (replaces stretched 504px footer bar!)
        _primaryActionButton = ThingsMinigameUiTheme.NativeProceedButton.Create(string.Empty, _titleFont, fontSize: 28);
        _primaryActionButton.AnchorBottomRight(rightInset: 54f, bottomInset: 48f);
        _primaryActionButton.Pressed += OnPrimaryActionPressed;
        hud.AddChild(_primaryActionButton);
    }

    private void ShowCellHoverTip(BilgeCellNode node)
    {
        BilgeCell cell = node.Cell;
        if (!cell.IsRevealed)
            return;

        if (cell.IsMine)
        {
            ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                node,
                "THINGS_BILGE_MINES.ui.cellSac",
                ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.statusBurst", ("MineDamage", BilgeSweepMinigame.MineBurstDamage)),
                _mineIcon,
                HoverTipAlignment.Right);
            return;
        }

        switch (cell.LootKind)
        {
            case BilgeLootKind.GoldSmall:
            case BilgeLootKind.GoldMedium:
                ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                    node,
                    "THINGS_BILGE_MINES.ui.haulTitle",
                    ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.statusFoundGold", ("Value", cell.GoldValue)),
                    _goldIcon,
                    HoverTipAlignment.Right);
                break;
            case BilgeLootKind.Potion:
                ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                    node,
                    "THINGS_BILGE_MINES.ui.cellPotion",
                    ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.statusFoundPotion"),
                    _potionIcon,
                    HoverTipAlignment.Right);
                break;
            case BilgeLootKind.Card:
                ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                    node,
                    "THINGS_BILGE_MINES.ui.cellCard",
                    ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.statusFoundCard"),
                    _cardIcon,
                    HoverTipAlignment.Right);
                break;
            case BilgeLootKind.CommonRelic:
                ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                    node,
                    "THINGS_BILGE_MINES.ui.cellRelic",
                    ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.statusFoundRelic"),
                    _relicIcon,
                    HoverTipAlignment.Right);
                break;
        }
    }

    private void OnCellGuiInput(InputEvent ev, BilgeCellNode node, int x, int y)
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
            {
                _minigame.ToggleFlag(x, y);
            }
            else
            {
                bool wasRevealed = node.Cell.IsRevealed;
                _minigame.ProbeCell(x, y);
                if (!wasRevealed && node.Cell.IsRevealed)
                    PopCellFeedback(node);
            }
            AcceptEvent();
        }
    }

    private void PopCellFeedback(BilgeCellNode node)
    {
        Vector2 popPos = new Vector2(12f, 12f) + node.Position + node.Size * 0.5f;
        BilgeCell cell = node.Cell;
        if (cell.IsMine)
        {
            _popLayer.Pop(popPos, $"-{BilgeSweepMinigame.MineBurstDamage} HP", new Color("#ff8882"), 26);
        }
        else if (cell.LootKind == BilgeLootKind.GoldSmall || cell.LootKind == BilgeLootKind.GoldMedium)
        {
            _popLayer.Pop(popPos, $"+{cell.GoldValue}G", new Color("#ffe37d"), 25);
        }
        else if (cell.LootKind == BilgeLootKind.Potion)
        {
            _popLayer.Pop(popPos, ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellPotion"), new Color("#8cf0c8"), 24);
        }
        else if (cell.LootKind == BilgeLootKind.Card)
        {
            _popLayer.Pop(popPos, ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellCard"), new Color("#9cd4ff"), 24);
        }
        else if (cell.LootKind == BilgeLootKind.CommonRelic)
        {
            _popLayer.Pop(popPos, ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellRelic"), new Color("#ffe37d"), 26);
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
        bool lethal = !_minigame.IsComplete && curHp <= BilgeSweepMinigame.MineBurstDamage;

        string probesLine = ThingsMinigameUiTheme.Tr(
            "THINGS_BILGE_MINES.ui.probesLabel",
            ("Remaining", $"[color=#ffd100]{_minigame.ProbesRemaining}[/color]"),
            ("Max", _minigame.MaxProbes));
        if (lethal)
        {
            string warn = ThingsMinigameUiTheme.Tr(
                "THINGS_BILGE_MINES.ui.lethalWarning",
                ("MineDamage", BilgeSweepMinigame.MineBurstDamage));
            _bottomStatusRtl.Text = $"{probesLine}\n[font_size=17][color=#ff847d]{warn}[/color][/font_size]";
        }
        else
        {
            _bottomStatusRtl.Text = probesLine;
        }

        _lootStrip.SetEntries(
            (_goldIcon, _minigame.CollectedGold > 0 ? $"+{_minigame.CollectedGold}G" : string.Empty, new Color("#ffe37d")),
            (_potionIcon, _minigame.CollectedPotions > 0 ? $"×{_minigame.CollectedPotions}" : string.Empty, new Color("#9cf0cc")),
            (_cardIcon, _minigame.CollectedCards > 0 ? $"×{_minigame.CollectedCards}" : string.Empty, new Color("#a4d2ff")),
            (_relicIcon, _minigame.CollectedCommonRelics > 0 ? $"×{_minigame.CollectedCommonRelics}" : string.Empty, new Color("#ffe89c")));

        bool hasLoot = _minigame.BuildOutcome().HasAnyLoot;
        if (_minigame.IsComplete)
        {
            _primaryActionButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_BILGE_MINES.ui.btnClaim" : "THINGS_BILGE_MINES.ui.btnContinue");
            _primaryActionButton.Active = true;
        }
        else
        {
            _primaryActionButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_BILGE_MINES.ui.btnCashOut" : "THINGS_BILGE_MINES.ui.btnLeaveEmpty");
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
                node.GlowOutline.Modulate = new Color(0.95f, 0.44f, 0.40f, node.IsHovered ? 0.72f : 0.45f);
            }
            else if (cell.HasGlint)
            {
                node.CoverArt.Texture = _glintTex;
                node.CoverArt.SelfModulate = node.IsHovered ? new Color("#fff2cc") : Colors.White;
                if (node.IsHovered && !_minigame.IsComplete)
                {
                    node.GlowOutline.Visible = true;
                    node.GlowOutline.Modulate = new Color(0.96f, 0.78f, 0.28f, 0.68f);
                }
            }
            else
            {
                node.CoverArt.Texture = _coverTex;
                node.CoverArt.SelfModulate = node.IsHovered && !_minigame.IsComplete ? new Color("#cde8ef") : Colors.White;
                if (node.IsHovered && !_minigame.IsComplete)
                {
                    node.GlowOutline.Visible = true;
                    node.GlowOutline.Modulate = new Color(0.82f, 0.90f, 0.94f, 0.50f);
                }
            }
            return;
        }

        // Revealed cell: hatch lid vanishes to unveil dark swirling canal water below
        node.CoverArt.Visible = false;
        node.WaterWash.Visible = true;
        node.MouseDefaultCursorShape = CursorShape.Arrow;

        if (cell.IsMine)
        {
            node.WaterWash.Color = cell.WasTriggeredMine
                ? new Color(0.42f, 0.10f, 0.16f, 0.76f)
                : new Color(0.24f, 0.08f, 0.13f, 0.54f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = cell.WasTriggeredMine
                ? new Color(0.96f, 0.36f, 0.36f, 0.85f)
                : new Color(0.84f, 0.36f, 0.44f, 0.48f);
            node.LootIcon.OffsetLeft = 16f;
            node.LootIcon.OffsetTop = 8f;
            node.LootIcon.OffsetRight = -16f;
            node.LootIcon.OffsetBottom = -24f;
            node.LootIcon.Texture = _mineIcon;
            node.LootIcon.Visible = _mineIcon != null;
            node.SubLabel.Text = ThingsMinigameUiTheme.Tr("THINGS_BILGE_MINES.ui.cellSac");
            node.SubLabel.AddThemeColorOverride("font_color", new Color("#ff9ba2"));
            return;
        }

        if (cell.LootKind != BilgeLootKind.None)
        {
            node.WaterWash.Color = new Color(0.08f, 0.23f, 0.19f, 0.58f);
            node.GlowOutline.Visible = true;
            node.GlowOutline.Modulate = new Color(0.88f, 0.78f, 0.38f, 0.55f);
            node.LootIcon.Texture = cell.LootKind switch
            {
                BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium => _goldIcon,
                BilgeLootKind.Potion => _potionIcon,
                BilgeLootKind.Card => _cardIcon,
                BilgeLootKind.CommonRelic => _relicIcon,
                _ => null,
            };
            node.LootIcon.Visible = node.LootIcon.Texture != null;

            bool isGold = cell.LootKind is BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium;
            if (isGold)
            {
                node.LootIcon.OffsetLeft = 18f;
                node.LootIcon.OffsetTop = 8f;
                node.LootIcon.OffsetRight = -18f;
                node.LootIcon.OffsetBottom = -24f;
                node.SubLabel.Text = $"+{cell.GoldValue}G";
                node.SubLabel.AddThemeColorOverride("font_color", new Color("#fae28e"));
            }
            else
            {
                // Unmistakable vanilla Crystal Sphere icons stand cleanly on their own — no redundant text tags!
                node.LootIcon.OffsetLeft = 14f;
                node.LootIcon.OffsetTop = 14f;
                node.LootIcon.OffsetRight = -14f;
                node.LootIcon.OffsetBottom = -14f;
                node.SubLabel.Text = string.Empty;
            }

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
        node.WaterWash.Color = new Color(0.02f, 0.08f, 0.11f, 0.26f);
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
