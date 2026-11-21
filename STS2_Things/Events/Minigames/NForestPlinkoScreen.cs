using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Overgrowth (Act 1A) Briar Plinko overlay screen modeled after vanilla CrystalSphere:
/// full-bleed hand-painted hollow trunk backdrop, animated amber-sap shader, 7 wooden drop notches,
/// 45 briar pegs &amp; 2 glow-shroom bumpers (with hover tips + floating +5G pops instead of static labels),
/// 7 iconic carved root bowls (clean icons + minimal numeric values + native HoverTips),
/// single bold bottom-left counter, narrow right drop control + clean plaque, and native ProceedButton.
/// </summary>
public partial class NForestPlinkoScreen : Control, IOverlayScreen
{
    private const string BackdropPath = "res://images/events/minigames/things_forest_plinko_bg.png";
    private const string SeedTexPath = "res://images/events/minigames/things_plinko_seed.png";
    private const string PegTexPath = "res://images/events/minigames/things_plinko_peg.png";
    private const string ShroomTexPath = "res://images/events/minigames/things_plinko_shroom.png";
    private const string BowlTexPath = "res://images/events/minigames/things_plinko_bowl.png";
    private const string SapShaderPath = "res://shaders/things_rune_sap_bed.gdshader";

    private sealed partial class PlinkoBoardCanvas : Control
    {
        public ForestPlinkoMinigame? Minigame { get; set; }
        public Texture2D? SeedTex { get; set; }
        public Texture2D? PegTex { get; set; }
        public Texture2D? ShroomTex { get; set; }
        public Texture2D? BowlTex { get; set; }
        public Texture2D? RelicCommonIcon { get; set; }
        public Texture2D? RelicRareIcon { get; set; }
        public Texture2D? PotionIcon { get; set; }
        public Texture2D? GoldIcon { get; set; }
        public Texture2D? HealIcon { get; set; }
        public Texture2D? CardIcon { get; set; }
        public Texture2D? MaxHpIcon { get; set; }
        public Texture2D? UpgradeIcon { get; set; }
        public Font? NumberFont { get; set; }

        public override void _Draw()
        {
            if (Minigame == null)
                return;

            float w = Size.X;
            float h = Size.Y;

            // 1. Top carved release rail with 7 selectable wooden drop notches
            DrawRect(new Rect2(14f, 10f, w - 28f, 14f), new Color("#1d2b1d"));
            DrawRect(new Rect2(14f, 11f, w - 28f, 4f), new Color("#56734e"));
            for (int i = 0; i < ForestPlinkoMinigame.DropSlotCount; i++)
            {
                float sx = ForestPlinkoMinigame.GetDropSlotX(i);
                bool selected = i == Minigame.SelectedSlot && !Minigame.IsComplete;
                Color notchFill = selected ? new Color("#f6cf5e") : new Color("#3a523b");
                Vector2[] tri =
                [
                    new Vector2(sx - 11f, 20f),
                    new Vector2(sx + 11f, 20f),
                    new Vector2(sx, 33f),
                ];
                DrawColoredPolygon(tri, notchFill);
                if (selected && Minigame.CanDrop())
                {
                    DrawCircle(new Vector2(sx, ForestPlinkoMinigame.DropOriginY), 21f, new Color(1.0f, 0.80f, 0.25f, 0.28f));
                    if (SeedTex != null)
                    {
                        DrawTextureRect(
                            SeedTex,
                            new Rect2(sx - 16f, ForestPlinkoMinigame.DropOriginY - 16f, 32f, 32f),
                            false,
                            Colors.White);
                    }
                    else
                    {
                        DrawCircle(new Vector2(sx, ForestPlinkoMinigame.DropOriginY), 13f, new Color("#ffd458"));
                    }
                    for (float dy = 56f; dy < 96f; dy += 11f)
                        DrawCircle(new Vector2(sx, dy), 2.3f, new Color(1.0f, 0.88f, 0.48f, 0.50f));
                }
            }

            // 2. 45 Briar Pegs + 2 Glow-Shroom Bonus Bumpers (clean icon-only, no stamped text!)
            foreach (PlinkoPeg peg in Minigame.Pegs)
            {
                Vector2 pos = new(peg.X, peg.Y);
                if (peg.IsBumper)
                {
                    float glowR = peg.Radius + 11f + peg.Flash * 10f;
                    DrawCircle(pos, glowR, new Color(0.24f, 0.90f, 0.78f, 0.22f + peg.Flash * 0.42f));
                    if (ShroomTex != null)
                    {
                        float dim = 46f + peg.Flash * 6f;
                        DrawTextureRect(ShroomTex, new Rect2(pos.X - dim * 0.5f, pos.Y - dim * 0.5f, dim, dim), false, Colors.White);
                    }
                    else
                    {
                        DrawCircle(pos, peg.Radius, new Color("#7ef29a"));
                    }
                }
                else
                {
                    if (peg.Flash > 0.01f)
                        DrawCircle(pos, peg.Radius + 6f + peg.Flash * 6f, new Color(1.0f, 0.84f, 0.34f, peg.Flash * 0.50f));
                    if (PegTex != null)
                    {
                        float dim = 26f + peg.Flash * 4f;
                        Color tint = peg.Flash > 0.01f ? Colors.White.Lerp(new Color("#fff0a8"), peg.Flash) : Colors.White;
                        DrawTextureRect(PegTex, new Rect2(pos.X - dim * 0.5f, pos.Y - dim * 0.5f, dim, dim), false, tint);
                    }
                    else
                    {
                        DrawCircle(pos, peg.Radius, new Color("#9fb895"));
                    }
                }
            }

            // 3. Bottom 7 Hand-Painted Carved Mossy Ironwood Root Bowls (clean icons + minimal numbers only!)
            float binY = ForestPlinkoMinigame.BinTopY;
            float binH = h - binY - 4f;
            DrawRect(new Rect2(6f, binY + 20f, w - 12f, binH - 16f), new Color(0.02f, 0.05f, 0.03f, 0.28f));
            for (int i = 0; i < Minigame.Bins.Count; i++)
            {
                PlinkoBin bin = Minigame.Bins[i];
                float bx = i * ForestPlinkoMinigame.BinWidth;
                float bw = ForestPlinkoMinigame.BinWidth;
                float cx = bx + bw * 0.5f;

                Color glowTint = bin.Kind switch
                {
                    PlinkoBinKind.RareRelic => new Color(1.0f, 0.84f, 0.22f, 0.34f + bin.Flash * 0.42f),
                    PlinkoBinKind.CommonRelic or PlinkoBinKind.UpgradeCard or PlinkoBinKind.GoldBig => new Color(1.0f, 0.84f, 0.34f, 0.20f + bin.Flash * 0.42f),
                    PlinkoBinKind.Potion or PlinkoBinKind.CardReward => new Color(0.36f, 0.86f, 0.90f, 0.16f + bin.Flash * 0.42f),
                    _ => new Color(0.45f, 0.90f, 0.52f, 0.15f + bin.Flash * 0.42f),
                };
                DrawCircle(new Vector2(cx, binY + 42f), 34f + bin.Flash * 8f, glowTint);

                if (BowlTex != null)
                {
                    Color bowlMod = bin.Kind switch
                    {
                        PlinkoBinKind.RareRelic => new Color("#fff2b4"),
                        PlinkoBinKind.CommonRelic or PlinkoBinKind.UpgradeCard or PlinkoBinKind.GoldBig => new Color("#ffefc6"),
                        _ => Colors.White,
                    };
                    if (bin.Flash > 0.01f)
                        bowlMod = bowlMod.Lerp(new Color("#fff5b8"), bin.Flash * 0.8f);
                    DrawTextureRect(BowlTex, new Rect2(bx + 2f, binY + 18f, bw - 4f, binH - 18f), false, bowlMod);
                }
                else
                {
                    DrawRect(new Rect2(bx + 3f, binY, bw - 6f, binH), new Color("#2c3820", 0.82f));
                }

                bool hasNumLip = bin.Kind is PlinkoBinKind.Gold or PlinkoBinKind.GoldBig or PlinkoBinKind.Heal or PlinkoBinKind.MaxHp;

                Texture2D? icon = bin.Kind switch
                {
                    PlinkoBinKind.RareRelic => RelicRareIcon ?? RelicCommonIcon,
                    PlinkoBinKind.CommonRelic => RelicCommonIcon,
                    PlinkoBinKind.Potion => PotionIcon,
                    PlinkoBinKind.Gold or PlinkoBinKind.GoldBig => GoldIcon,
                    PlinkoBinKind.Heal => HealIcon,
                    PlinkoBinKind.CardReward => CardIcon,
                    PlinkoBinKind.MaxHp => MaxHpIcon,
                    PlinkoBinKind.UpgradeCard => UpgradeIcon,
                    _ => GoldIcon,
                };
                if (icon != null)
                {
                    float maxDim = (bin.Kind == PlinkoBinKind.RareRelic ? 54f : (hasNumLip ? 46f : 52f)) + bin.Flash * 4f;
                    Vector2 texSz = icon.GetSize();
                    float aspect = texSz.Y > 0f ? texSz.X / texSz.Y : 1f;
                    float drawW = aspect >= 1f ? maxDim : maxDim * aspect;
                    float drawH = aspect >= 1f ? maxDim / aspect : maxDim;
                    Vector2 iconCenter = new(cx, hasNumLip ? binY + 28f : binY + 34f);
                    if (bin.Kind == PlinkoBinKind.MaxHp)
                        DrawCircle(iconCenter + new Vector2(0f, 1f), maxDim * 0.46f, new Color(0.03f, 0.08f, 0.05f, 0.62f));
                    DrawTextureRect(icon, new Rect2(iconCenter.X - drawW * 0.5f, iconCenter.Y - drawH * 0.5f, drawW, drawH), false, Colors.White);
                }

                // Minimal crisp numeral ONLY on numeric bowls (+20G / +5 / +3) — ZERO cramped Chinese word labels!
                if (hasNumLip && NumberFont != null)
                {
                    string numText = bin.Kind switch
                    {
                        PlinkoBinKind.Gold or PlinkoBinKind.GoldBig => $"+{bin.Amount}G",
                        _ => $"+{bin.Amount}",
                    };
                    Color numCol = bin.Kind switch
                    {
                        PlinkoBinKind.Heal => new Color("#9cf2ad"),
                        PlinkoBinKind.MaxHp => new Color("#ff9eb0"),
                        _ => new Color("#ffe57d"),
                    };
                    DrawStringOutline(NumberFont, new Vector2(bx + 1f, binY + 79f), numText, HorizontalAlignment.Center, bw - 2f, 18, 6, new Color("#030c06"));
                    DrawString(NumberFont, new Vector2(bx + 1f, binY + 79f), numText, HorizontalAlignment.Center, bw - 2f, 18, numCol);
                }
            }

            // Wooden briar pin pegs marking the bowl dividers cleanly
            for (int d = 1; d < ForestPlinkoMinigame.BinCount; d++)
            {
                float dx = d * ForestPlinkoMinigame.BinWidth;
                if (PegTex != null)
                    DrawTextureRect(PegTex, new Rect2(dx - 8f, binY - 10f, 16f, 16f), false, Colors.White);
                else
                    DrawCircle(new Vector2(dx, binY - 2f), 5f, new Color("#b8cc9e"));
            }

            // 4. Active falling golden amber seed + glowing golden sap trail
            PlinkoSeedBall ball = Minigame.ActiveBall;
            if (ball.Active || ball.Trail.Count > 1)
            {
                int count = ball.Trail.Count;
                for (int i = 1; i < count; i++)
                {
                    float frac = (float)i / count;
                    Vector2 p0 = new(ball.Trail[i - 1].X, ball.Trail[i - 1].Y);
                    Vector2 p1 = new(ball.Trail[i].X, ball.Trail[i].Y);
                    DrawLine(p0, p1, new Color(1.0f, 0.80f, 0.25f, frac * (ball.Active ? 0.70f : 0.32f)), 4f + frac * 7f, true);
                }
            }

            if (ball.Active)
            {
                Vector2 bpos = new(ball.X, ball.Y);
                DrawCircle(bpos, ForestPlinkoMinigame.BallRadius + 10f, new Color(1.0f, 0.80f, 0.24f, 0.36f));
                if (SeedTex != null)
                    DrawTextureRect(SeedTex, new Rect2(bpos.X - 16f, bpos.Y - 16f, 32f, 32f), false, Colors.White);
                else
                    DrawCircle(bpos, ForestPlinkoMinigame.BallRadius, new Color("#ffd458"));
            }
        }
    }

    private ForestPlinkoMinigame _minigame = null!;
    private PlinkoBoardCanvas _boardCanvas = null!;
    private ThingsMinigameUiTheme.FloatingPopLayer _popLayer = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private ThingsMinigameUiTheme.CompactLootStrip _lootStrip = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _dropButton = null!;
    private ThingsMinigameUiTheme.NativeProceedButton _finishButton = null!;

    private Texture2D? _relicCommonIcon;
    private Texture2D? _relicRareIcon;
    private Texture2D? _potionIcon;
    private Texture2D? _goldIcon;
    private Texture2D? _healIcon;
    private Texture2D? _cardIcon;
    private Texture2D? _maxHpIcon;
    private Texture2D? _upgradeIcon;
    private Texture2D? _shroomTex;

    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _numberFont;
    private bool _hasOpened;
    private string _lastFeedbackKey = string.Empty;
    private int _lastFeedbackValue;

    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _minigame.IsComplete ? _finishButton : _dropButton;

    public static NForestPlinkoScreen Create(ForestPlinkoMinigame minigame)
    {
        var screen = new NForestPlinkoScreen
        {
            Name = "STS2ThingsForestPlinkoScreen",
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

    public override void _Process(double delta)
    {
        if (!Visible || _minigame == null)
            return;
        _minigame.Advance((float)delta);
        _boardCanvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (!Visible || _minigame == null)
            return;
        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Left || key.Keycode == Key.A)
            {
                _minigame.SelectSlot(_minigame.SelectedSlot - 1);
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Right || key.Keycode == Key.D)
            {
                _minigame.SelectSlot(_minigame.SelectedSlot + 1);
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Space || key.Keycode == Key.Enter)
            {
                if (_minigame.CanDrop())
                {
                    _minigame.TryDropSeed();
                    GetViewport().SetInputAsHandled();
                }
                else if (_minigame.IsComplete)
                {
                    _minigame.ConfirmExit();
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }

    public void AfterOverlayOpened()
    {
        if (_minigame != null)
        {
            _lastFeedbackKey = _minigame.LastFeedbackKey;
            _lastFeedbackValue = _minigame.LastFeedbackValue;
            _minigame.StateChanged += RefreshStatus;
        }
        _hasOpened = true;
        RefreshStatus();
    }

    public void AfterOverlayClosed()
    {
        if (_minigame != null)
            _minigame.StateChanged -= RefreshStatus;
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
            _minigame.StateChanged -= RefreshStatus;
            _minigame.ForceEnd();
        }
    }

    private void BuildUi()
    {
        _titleFont = ThingsMinigameUiTheme.GetTitleFont();
        _bodyFont = ThingsMinigameUiTheme.GetBodyFont();
        _numberFont = ThingsMinigameUiTheme.GetNumberFont();

        _shroomTex = ThingsMinigameUiTheme.TryLoadTexture(ShroomTexPath);
        _relicCommonIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png");
        _relicRareIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/minigames/things_loot_relic_rare_box.png")
            ?? _relicCommonIcon;
        _potionIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_rare_potion.png");
        _goldIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_big_gold.png");
        _healIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/regen_power.png");
        _cardIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png");
        _maxHpIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png");
        _upgradeIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/hammer_time_power.png");

        // 1. Deep nocturnal Overgrowth outer fill
        var outerFill = new ColorRect
        {
            Color = new Color("#040d07"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 artwork stage
        var stage = new Control
        {
            Name = "PlinkoStage",
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
            Name = "PlinkoBackdrop",
            Texture = ThingsMinigameUiTheme.TryLoadTexture(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 3. Embedded 630x630 hollow Overgrowth trunk Plinko cavity (uniformly fitted inside 563..1129, 255..821)
        float plinkoScale = 566f / 630f;
        var playfield = new Control
        {
            Name = "PlinkoPlayfield",
            Position = new Vector2(563f, 255f),
            Size = new Vector2(630f, 630f),
            Scale = new Vector2(plinkoScale, plinkoScale),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        stage.AddChild(playfield);

        playfield.GuiInput += ev =>
        {
            if (ev is InputEventMouseMotion mm && !_minigame.IsComplete)
            {
                int nearest = FindNearestSlot(mm.Position.X);
                _minigame.SelectSlot(nearest);
            }
            else if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && _minigame.CanDrop())
            {
                int nearest = FindNearestSlot(mb.Position.X);
                _minigame.TryDropSeed(nearest);
                AcceptEvent();
            }
        };

        Shader? sapShader = ThingsMinigameUiTheme.TryLoadShader(SapShaderPath);
        if (sapShader != null)
        {
            var sapFx = new ColorRect
            {
                Name = "SapBedFx",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.10f),
                Material = new ShaderMaterial { Shader = sapShader },
                MouseFilter = MouseFilterEnum.Ignore,
            };
            playfield.AddChild(sapFx);
            sapFx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        _boardCanvas = new PlinkoBoardCanvas
        {
            Name = "PlinkoBoardCanvas",
            Minigame = _minigame,
            SeedTex = ThingsMinigameUiTheme.TryLoadTexture(SeedTexPath),
            PegTex = ThingsMinigameUiTheme.TryLoadTexture(PegTexPath),
            ShroomTex = _shroomTex,
            BowlTex = ThingsMinigameUiTheme.TryLoadTexture(BowlTexPath),
            RelicCommonIcon = _relicCommonIcon,
            RelicRareIcon = _relicRareIcon,
            PotionIcon = _potionIcon,
            GoldIcon = _goldIcon,
            HealIcon = _healIcon,
            CardIcon = _cardIcon,
            MaxHpIcon = _maxHpIcon,
            UpgradeIcon = _upgradeIcon,
            NumberFont = _numberFont,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        playfield.AddChild(_boardCanvas);
        _boardCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Pass-through hover hitboxes on the 7 root bowls & 2 glowing mushrooms for native STS2 HoverTips!
        AttachBoardHoverHitboxes(playfield);

        _popLayer = ThingsMinigameUiTheme.FloatingPopLayer.Create(_titleFont);
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

        // Bottom-Left: Single bold vanilla DivinationsLeft counter (HP already in top bar!)
        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            30,
            ThingsMinigameUiTheme.VanillaCream,
            new Color("#051209"),
            _titleFont,
            outlineSize: 10,
            shadowX: 4,
            shadowY: 3,
            align: HorizontalAlignment.Left,
            wrap: false);
        hud.AddChild(_bottomStatusRtl);
        _bottomStatusRtl.AnchorLeft = 0f;
        _bottomStatusRtl.AnchorRight = 0f;
        _bottomStatusRtl.AnchorTop = 1f;
        _bottomStatusRtl.AnchorBottom = 1f;
        _bottomStatusRtl.OffsetLeft = 56f;
        _bottomStatusRtl.OffsetRight = 460f;
        _bottomStatusRtl.OffsetTop = -92f;
        _bottomStatusRtl.OffsetBottom = -36f;

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
        rightCenter.AddThemeConstantOverride("separation", 18);
        rightAnchor.AddChild(rightCenter);
        rightCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Primary Drop Seed Button
        _dropButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.btnDrop"),
            new Vector2(372f, 86f),
            baseTint: new Color("#59996b"),
            activeTint: new Color("#6eb883"),
            outlineColor: ThingsMinigameUiTheme.VanillaWarmOutline,
            font: _titleFont,
            fontSize: 23,
            textColor: new Color("#fff6d4"));
        _dropButton.Pressed += () => _minigame.TryDropSeed();
        rightCenter.AddChild(_dropButton);

        // Single clean narrow Overgrowth plaque (ZERO dividers, ZERO bullet wall, ZERO log!)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.11f, 0.06f, 0.86f),
            padH: 24,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 14);
        plaque.AddChild(plaqueBox);

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeVanillaPlaqueBody(
            ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.rules", ("BumperGold", ForestPlinkoMinigame.BumperBonusGold)),
            _bodyFont,
            fontSize: 18));

        _lootStrip = ThingsMinigameUiTheme.CompactLootStrip.Create(_numberFont);
        plaqueBox.AddChild(_lootStrip);

        // Authentic bottom-right vanilla STS2 ProceedButton
        _finishButton = ThingsMinigameUiTheme.NativeProceedButton.Create(string.Empty, _titleFont, fontSize: 28);
        _finishButton.AnchorBottomRight(rightInset: 54f, bottomInset: 48f);
        _finishButton.Pressed += () => _minigame.ConfirmExit();
        hud.AddChild(_finishButton);
    }

    private void AttachBoardHoverHitboxes(Control playfield)
    {
        float binY = ForestPlinkoMinigame.BinTopY;
        float binH = 630f - binY;
        for (int i = 0; i < _minigame.Bins.Count; i++)
        {
            int idx = i;
            var hit = new Control
            {
                Position = new Vector2(i * ForestPlinkoMinigame.BinWidth, binY),
                Size = new Vector2(ForestPlinkoMinigame.BinWidth, binH),
                MouseFilter = MouseFilterEnum.Pass,
                MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            hit.MouseEntered += () => ShowBinHoverTip(hit, _minigame.Bins[idx]);
            hit.MouseExited += () => ThingsMinigameUiTheme.HideHoverTip(hit);
            playfield.AddChild(hit);
        }

        foreach (PlinkoPeg peg in _minigame.Pegs)
        {
            if (!peg.IsBumper)
                continue;
            var hit = new Control
            {
                Position = new Vector2(peg.X - 24f, peg.Y - 24f),
                Size = new Vector2(48f, 48f),
                MouseFilter = MouseFilterEnum.Pass,
                MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            hit.MouseEntered += () => ThingsMinigameUiTheme.ShowLocalizedHoverTip(
                hit,
                "THINGS_FOREST_PLINKO.ui.haulTitle",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusHitBumper", ("Value", ForestPlinkoMinigame.BumperBonusGold)),
                _shroomTex,
                HoverTipAlignment.Right);
            hit.MouseExited += () => ThingsMinigameUiTheme.HideHoverTip(hit);
            playfield.AddChild(hit);
        }
    }

    private void ShowBinHoverTip(Control owner, PlinkoBin bin)
    {
        (string titleKey, string desc, Texture2D? icon) = bin.Kind switch
        {
            PlinkoBinKind.RareRelic => ("THINGS_FOREST_PLINKO.ui.binRareRelic",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonRareRelic", ("Value", ForestPlinkoMinigame.BinGoldBigAmount)),
                _relicRareIcon),
            PlinkoBinKind.CommonRelic => ("THINGS_FOREST_PLINKO.ui.binRelic",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonRelic", ("Value", ForestPlinkoMinigame.BinGoldBigAmount)),
                _relicCommonIcon),
            PlinkoBinKind.Potion => ("THINGS_FOREST_PLINKO.ui.binPotion",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonPotion"),
                _potionIcon),
            PlinkoBinKind.Gold or PlinkoBinKind.GoldBig => ("THINGS_FOREST_PLINKO.ui.haulTitle",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonGold", ("Value", bin.Amount)),
                _goldIcon),
            PlinkoBinKind.Heal => ("THINGS_FOREST_PLINKO.ui.haulTitle",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonHeal", ("Value", bin.Amount)),
                _healIcon),
            PlinkoBinKind.CardReward => ("THINGS_FOREST_PLINKO.ui.binCard",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonCard"),
                _cardIcon),
            PlinkoBinKind.MaxHp => ("THINGS_FOREST_PLINKO.ui.haulTitle",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonMaxHp", ("Value", bin.Amount)),
                _maxHpIcon),
            PlinkoBinKind.UpgradeCard => ("THINGS_FOREST_PLINKO.ui.binUpgrade",
                ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.statusWonUpgrade", ("Value", ForestPlinkoMinigame.BinGoldBigAmount)),
                _upgradeIcon),
            _ => ("THINGS_FOREST_PLINKO.ui.haulTitle", string.Empty, _goldIcon),
        };
        ThingsMinigameUiTheme.ShowLocalizedHoverTip(owner, titleKey, desc, icon, HoverTipAlignment.Right);
    }

    private static int FindNearestSlot(float localX)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < ForestPlinkoMinigame.DropSlotCount; i++)
        {
            float d = MathF.Abs(localX - ForestPlinkoMinigame.GetDropSlotX(i));
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private void RefreshStatus()
    {
        _bottomStatusRtl.Text = ThingsMinigameUiTheme.Tr(
            "THINGS_FOREST_PLINKO.ui.dropsLabel",
            ("Remaining", $"[color=#ffd100]{_minigame.DropsRemaining}[/color]"),
            ("Max", _minigame.MaxDrops));

        _lootStrip.SetEntries(
            (_relicRareIcon, _minigame.CollectedRareRelics > 0 ? $"×{_minigame.CollectedRareRelics}" : string.Empty, new Color("#ffe568")),
            (_relicCommonIcon, _minigame.CollectedCommonRelics > 0 ? $"×{_minigame.CollectedCommonRelics}" : string.Empty, new Color("#ffe89c")),
            (_upgradeIcon, _minigame.CollectedUpgrades > 0 ? $"×{_minigame.CollectedUpgrades}" : string.Empty, new Color("#ffc070")),
            (_goldIcon, _minigame.CollectedGold > 0 ? $"+{_minigame.CollectedGold}G" : string.Empty, new Color("#ffe37d")),
            (_healIcon, _minigame.CollectedHeal > 0 ? $"+{_minigame.CollectedHeal}" : string.Empty, new Color("#9cf2ad")),
            (_maxHpIcon, _minigame.CollectedMaxHp > 0 ? $"+{_minigame.CollectedMaxHp}" : string.Empty, new Color("#ff9eb0")),
            (_potionIcon, _minigame.CollectedPotions > 0 ? $"×{_minigame.CollectedPotions}" : string.Empty, new Color("#9cf0cc")),
            (_cardIcon, _minigame.CollectedCards > 0 ? $"×{_minigame.CollectedCards}" : string.Empty, new Color("#a4d2ff")));

        if (_hasOpened && (_minigame.LastFeedbackKey != _lastFeedbackKey || _minigame.LastFeedbackValue != _lastFeedbackValue))
        {
            _lastFeedbackKey = _minigame.LastFeedbackKey;
            _lastFeedbackValue = _minigame.LastFeedbackValue;
            PopPlinkoFeedback(_lastFeedbackKey, _lastFeedbackValue);
        }

        bool canDrop = _minigame.CanDrop();
        _dropButton.Disabled = !canDrop;
        _dropButton.Active = canDrop;

        bool hasLoot = _minigame.BuildOutcome().HasAnyLoot;
        if (_minigame.IsComplete)
        {
            _finishButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_FOREST_PLINKO.ui.btnClaim" : "THINGS_FOREST_PLINKO.ui.btnLeaveEmpty");
            _finishButton.Active = true;
            _finishButton.Disabled = false;
        }
        else
        {
            _finishButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_FOREST_PLINKO.ui.btnFinishEarly" : "THINGS_FOREST_PLINKO.ui.btnLeaveEmpty");
            _finishButton.Active = false;
            _finishButton.Disabled = _minigame.ActiveBall.Active;
        }

        _boardCanvas.QueueRedraw();
    }

    private void PopPlinkoFeedback(string key, int val)
    {
        if (string.IsNullOrEmpty(key) || key.EndsWith("Ready") || key.EndsWith("Dropping"))
            return;

        if (key.EndsWith("HitBumper"))
        {
            Vector2 pos = new(Math.Clamp(_minigame.ActiveBall.X, 90f, 540f), Math.Clamp(_minigame.ActiveBall.Y - 22f, 90f, 480f));
            _popLayer.Pop(pos, $"+{val}G", new Color("#ffe37d"), 24);
            return;
        }

        // Bowl win pop right above the landing bowl!
        float bx = Math.Clamp(_minigame.ActiveBall.X, 110f, 520f);
        string text = key switch
        {
            "THINGS_FOREST_PLINKO.ui.statusWonGold" => $"+{val}G",
            "THINGS_FOREST_PLINKO.ui.statusWonHeal" => $"+{val} HP",
            "THINGS_FOREST_PLINKO.ui.statusWonMaxHp" => $"+{val} MaxHP",
            "THINGS_FOREST_PLINKO.ui.statusWonRareRelic" => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binRareRelic"),
            "THINGS_FOREST_PLINKO.ui.statusWonRelic" => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binRelic"),
            "THINGS_FOREST_PLINKO.ui.statusWonPotion" => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binPotion"),
            "THINGS_FOREST_PLINKO.ui.statusWonCard" => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binCard"),
            "THINGS_FOREST_PLINKO.ui.statusWonUpgrade" => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binUpgrade"),
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(text))
            _popLayer.Pop(new Vector2(bx, ForestPlinkoMinigame.BinTopY - 12f), text, new Color("#ffe682"), 24);
    }
}
