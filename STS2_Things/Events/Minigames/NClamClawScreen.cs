using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Underdocks (Act 1B) Sewer Clam Claw Machine overlay screen modeled after vanilla CrystalSphere:
/// full-bleed hand-painted canal hoist backdrop, 20 hand-painted Mega Crit treasure sprites piled naturally
/// under 2D gravity (zero floating text tags cluttering the pit!), animated Underdocks Sewer Clam,
/// single bold bottom-left counter, narrow right drop control + clean rules plaque, and native ProceedButton.
/// </summary>
public partial class NClamClawScreen : Control, IOverlayScreen
{
    private const string BackdropPath = "res://images/events/minigames/things_clam_claw_bg.png";
    private const string UpperShellPath = "res://images/events/minigames/things_clam_shell_upper.png";
    private const string LowerShellPath = "res://images/events/minigames/things_clam_shell_lower.png";
    private const string MouthBackPath = "res://images/events/minigames/things_clam_mouth_back.png";
    private const string HingePath = "res://images/events/minigames/things_clam_hinge.png";
    private const string WaterShaderPath = "res://shaders/things_bilge_water.gdshader";

    private sealed partial class ClamTankCanvas : Control
    {
        public ClamClawMinigame? Minigame { get; set; }
        public Texture2D? UpperShellTex { get; set; }
        public Texture2D? LowerShellTex { get; set; }
        public Texture2D? MouthBackTex { get; set; }
        public Texture2D? HingeTex { get; set; }
        public Texture2D? GoldSmallIcon { get; set; }
        public Texture2D? GoldPouchIcon { get; set; }
        public Texture2D? GoldIngotsIcon { get; set; }
        public Texture2D? PotionEmeraldIcon { get; set; }
        public Texture2D? PotionAmberIcon { get; set; }
        public Texture2D? CardIcon { get; set; }
        public Texture2D? RelicCommonIcon { get; set; }
        public Texture2D? RelicRareIcon { get; set; }
        public Texture2D? ScrapIcon { get; set; }

        public override void _Draw()
        {
            if (Minigame == null)
                return;

            float w = Size.X;
            float h = Size.Y;

            // 1. Soft submerged floor shadow bed under the gravity-stacked 20-prize claw heap
            DrawRect(new Rect2(14f, 558f, w - 28f, h - 564f), new Color(0.01f, 0.05f, 0.08f, 0.36f));

            // 2. Overhead rusted iron crane gantry rail
            DrawRect(new Rect2(16f, 12f, w - 32f, 15f), new Color("#19262b"));
            DrawRect(new Rect2(16f, 13f, w - 32f, 4f), new Color("#6c8a8c"));
            DrawRect(new Rect2(16f, 24f, w - 32f, 3f), new Color("#091115"));
            for (float rx = 38f; rx <= w - 38f; rx += 64f)
                DrawCircle(new Vector2(rx, 19.5f), 3.2f, new Color("#b59858"));

            // 3. Aiming plumb-line while sweeping
            ClamPrize? aligned = Minigame.Phase == ClamCranePhase.Aiming ? Minigame.PeekAlignedPrize() : null;
            if (Minigame.Phase == ClamCranePhase.Aiming && !Minigame.IsComplete)
            {
                float beamEndY = aligned != null ? aligned.Y - 28f : ClamClawMinigame.FloorCraneY;
                Color beamColor = aligned != null
                    ? new Color(0.98f, 0.85f, 0.38f, 0.46f)
                    : new Color(0.55f, 0.85f, 0.92f, 0.20f);
                for (float dy = Minigame.CraneY + 46f; dy < beamEndY; dy += 15f)
                {
                    float segEnd = MathF.Min(dy + 8f, beamEndY);
                    DrawLine(new Vector2(Minigame.CraneX, dy), new Vector2(Minigame.CraneX, segEnd), beamColor, 2.5f, true);
                }
            }

            // 4. Draw all prizes piled naturally at the bottom (ZERO permanent text labels stamped over the mound!)
            List<ClamPrize> visiblePrizes = Minigame.Prizes
                .Where(p => p.State == ClamPrizeState.InTank || p.State == ClamPrizeState.FallingSlip)
                .OrderBy(p => p.Y)
                .ThenBy(p => p.Id)
                .ToList();

            foreach (ClamPrize p in visiblePrizes)
            {
                if (p == aligned)
                    continue;
                DrawPrizeItem(p, new Vector2(p.X, p.Y), p.TiltAngle, scale: 1.0f, highlighted: false);
            }
            if (aligned != null)
            {
                DrawPrizeItem(aligned, new Vector2(aligned.X, aligned.Y), aligned.TiltAngle, scale: 1.08f, highlighted: true);
            }

            // 5. Hoist carriage & linked iron chain descending to the Sewer Clam hinge
            float cx = Minigame.CraneX;
            float cy = Minigame.CraneY;
            DrawRect(new Rect2(cx - 26f, 6f, 52f, 28f), new Color("#283a40"));
            DrawRect(new Rect2(cx - 22f, 9f, 44f, 8f), new Color("#84a3a4"));
            DrawCircle(new Vector2(cx - 14f, 20f), 5.5f, new Color("#d4a852"));
            DrawCircle(new Vector2(cx + 14f, 20f), 5.5f, new Color("#d4a852"));

            float chainTop = 30f;
            float chainBot = cy - 64f;
            int linkIdx = 0;
            for (float y = chainTop; y < chainBot; y += 13f)
            {
                float segH = MathF.Min(11f, chainBot - y);
                float halfW = (linkIdx++ % 2 == 0) ? 5.5f : 3.2f;
                DrawRect(new Rect2(cx - halfW - 1f, y, halfW * 2f + 2f, segH + 1f), new Color("#10181c"));
                DrawRect(new Rect2(cx - halfW, y + 1f, halfW * 2f, MathF.Max(2f, segH - 2f)), new Color("#887462"));
                DrawRect(new Rect2(cx - halfW + 1f, y + 1f, MathF.Max(1.5f, halfW), MathF.Max(1.5f, segH - 4f)), new Color("#c8b08e"));
            }

            // 6. Sculpted 4-part Sewer Clam Claw
            DrawSewerClamClaw(new Vector2(cx, cy));
        }

        private void DrawSewerClamClaw(Vector2 center)
        {
            if (Minigame == null)
                return;

            float open = Minigame.ShellOpenAmount;
            float sway = Minigame.ClamSwayAngle;
            Vector2 hinge = center + new Vector2(0f, -24f);

            DrawSetTransform(hinge, sway, Vector2.One);
            if (MouthBackTex != null)
            {
                float mouthW = 92f + open * 48f;
                float mouthH = 94f + open * 10f;
                DrawTextureRect(MouthBackTex, new Rect2(-mouthW * 0.5f, -2f, mouthW, mouthH), false, Colors.White);
            }
            else
            {
                DrawCircle(new Vector2(0f, 38f), 34f + open * 14f, new Color("#121029"));
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            if (Minigame.HeldPrize != null)
            {
                Vector2 prizeWorldPos = hinge + new Vector2(0f, 54f).Rotated(sway);
                DrawPrizeItem(Minigame.HeldPrize, prizeWorldPos, sway, scale: 0.90f, highlighted: false, drawShadow: false);
            }

            float jawAngle = 0.04f + open * 0.44f;
            if (UpperShellTex != null)
            {
                DrawSetTransform(hinge, sway + jawAngle, Vector2.One);
                DrawTextureRect(UpperShellTex, new Rect2(-98f, -6f, 116f, 118f), false, Colors.White);
            }

            if (LowerShellTex != null)
            {
                DrawSetTransform(hinge, sway - jawAngle, Vector2.One);
                DrawTextureRect(LowerShellTex, new Rect2(-18f, -6f, 116f, 118f), false, Colors.White);
            }

            DrawSetTransform(hinge, sway, Vector2.One);
            if (HingeTex != null)
            {
                DrawTextureRect(HingeTex, new Rect2(-26f, -46f, 52f, 62f), false, Colors.White);
            }
            else
            {
                DrawCircle(new Vector2(0f, 0f), 11f, new Color("#1a252a"));
                DrawCircle(new Vector2(0f, 0f), 7.5f, new Color("#a68452"));
            }

            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        private Texture2D? ResolvePrizeTexture(ClamPrize p) => p.Kind switch
        {
            ClamPrizeKind.RareRelic => RelicRareIcon ?? RelicCommonIcon,
            ClamPrizeKind.CommonRelic => RelicCommonIcon,
            ClamPrizeKind.GoldBig => (p.VisualVariant == 0 ? GoldPouchIcon : GoldIngotsIcon) ?? GoldPouchIcon ?? GoldSmallIcon,
            ClamPrizeKind.GoldSmall => GoldSmallIcon ?? GoldPouchIcon,
            ClamPrizeKind.Potion => (p.VisualVariant == 0 ? PotionEmeraldIcon : PotionAmberIcon) ?? PotionEmeraldIcon,
            ClamPrizeKind.Card => CardIcon,
            ClamPrizeKind.BarnacleScrap => ScrapIcon ?? GoldSmallIcon,
            _ => GoldSmallIcon,
        };

        private static float GetPrizeBaseDim(ClamPrizeKind kind) => kind switch
        {
            ClamPrizeKind.RareRelic => 78f,
            ClamPrizeKind.CommonRelic => 74f,
            ClamPrizeKind.GoldBig => 68f,
            ClamPrizeKind.Card => 66f,
            ClamPrizeKind.Potion => 66f,
            ClamPrizeKind.GoldSmall => 54f,
            ClamPrizeKind.BarnacleScrap => 62f,
            _ => 62f,
        };

        private void DrawPrizeItem(ClamPrize p, Vector2 pos, float tilt, float scale, bool highlighted, bool drawShadow = true)
        {
            float dim = GetPrizeBaseDim(p.Kind) * scale;

            if (drawShadow)
            {
                DrawSetTransform(pos + new Vector2(2f, dim * 0.34f), 0f, new Vector2(1.0f, 0.38f));
                DrawCircle(Vector2.Zero, dim * 0.44f, new Color(0.01f, 0.04f, 0.07f, 0.54f));
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            // Subtle warm aura ONLY on the Rare Cardboard Box or currently aimed prize
            bool isRareBox = p.Kind == ClamPrizeKind.RareRelic;
            if (highlighted || isRareBox)
            {
                float haloR = dim * 0.54f;
                Color glowCol = isRareBox
                    ? new Color(1.0f, 0.84f, 0.24f, highlighted ? 0.38f : 0.26f)
                    : new Color(1.0f, 0.86f, 0.36f, 0.28f);
                DrawCircle(pos, haloR, glowCol);
                DrawArc(pos, haloR + 1.5f, 0f, MathF.Tau, 28,
                    isRareBox ? new Color(1f, 0.88f, 0.42f, 0.72f) : new Color(1f, 0.90f, 0.52f, 0.65f),
                    highlighted ? 2.4f : 1.8f, true);
            }

            Texture2D? icon = ResolvePrizeTexture(p);
            if (icon != null)
            {
                DrawSetTransform(pos, tilt, Vector2.One);
                DrawTextureRect(icon, new Rect2(-dim * 0.5f, -dim * 0.5f, dim, dim), false, Colors.White);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
        }
    }

    private ClamClawMinigame _minigame = null!;
    private ClamTankCanvas _tankCanvas = null!;
    private ThingsMinigameUiTheme.FloatingPopLayer _popLayer = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private ThingsMinigameUiTheme.CompactLootStrip _lootStrip = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _dropButton = null!;
    private ThingsMinigameUiTheme.NativeProceedButton _finishButton = null!;

    private Texture2D? _goldSmallIcon;
    private Texture2D? _goldPouchIcon;
    private Texture2D? _goldIngotsIcon;
    private Texture2D? _potionEmeraldIcon;
    private Texture2D? _potionAmberIcon;
    private Texture2D? _cardIcon;
    private Texture2D? _relicCommonIcon;
    private Texture2D? _relicRareIcon;
    private Texture2D? _scrapIcon;

    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _numberFont;
    private bool _hasOpened;
    private string _lastFeedbackKey = string.Empty;
    private int _lastFeedbackValue;

    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _minigame.IsComplete ? _finishButton : _dropButton;

    public static NClamClawScreen Create(ClamClawMinigame minigame)
    {
        var screen = new NClamClawScreen
        {
            Name = "STS2ThingsClamClawScreen",
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
        _tankCanvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (!Visible || _minigame == null)
            return;
        if (ev is InputEventKey key && key.Pressed && !key.Echo &&
            (key.Keycode == Key.Space || key.Keycode == Key.Enter))
        {
            if (_minigame.CanDrop())
            {
                _minigame.TryDropClaw();
                GetViewport().SetInputAsHandled();
            }
            else if (_minigame.IsComplete)
            {
                _minigame.ConfirmExit();
                GetViewport().SetInputAsHandled();
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

        _goldSmallIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_big_gold.png");
        _goldPouchIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_big_gold.png")
            ?? _goldSmallIcon;
        _goldIngotsIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/relics/old_coin.png")
            ?? _goldPouchIcon;
        _potionEmeraldIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_rare_potion.png");
        _potionAmberIcon = _potionEmeraldIcon;
        _cardIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png")
            ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png");
        _relicCommonIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png");
        _relicRareIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/minigames/things_loot_relic_rare_box.png")
            ?? _relicCommonIcon;
        _scrapIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/minigames/things_loot_scrap_bauble.png")
            ?? _goldSmallIcon;

        // 1. Dark canal outer fill
        var outerFill = new ColorRect
        {
            Color = new Color("#040d11"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 artwork stage
        var stage = new Control
        {
            Name = "ClamStage",
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
            Name = "ClamBackdrop",
            Texture = ThingsMinigameUiTheme.TryLoadTexture(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 3. Embedded 630x630 flooded Underdocks crane pit (uniformly fitted inside 552..1098, 264..810)
        float clamScale = 546f / 630f;
        var playfield = new Control
        {
            Name = "ClamPlayfield",
            Position = new Vector2(552f, 264f),
            Size = new Vector2(630f, 630f),
            Scale = new Vector2(clamScale, clamScale),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        stage.AddChild(playfield);

        playfield.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && _minigame.CanDrop())
            {
                _minigame.TryDropClaw();
                AcceptEvent();
            }
        };

        Shader? waterShader = ThingsMinigameUiTheme.TryLoadShader(WaterShaderPath);
        if (waterShader != null)
        {
            var waterFx = new ColorRect
            {
                Name = "WaterCaustics",
                Color = Colors.White,
                Modulate = new Color(1f, 1f, 1f, 0.04f),
                Material = new ShaderMaterial { Shader = waterShader },
                MouseFilter = MouseFilterEnum.Ignore,
            };
            playfield.AddChild(waterFx);
            waterFx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        _tankCanvas = new ClamTankCanvas
        {
            Name = "ClamTankCanvas",
            Minigame = _minigame,
            UpperShellTex = ThingsMinigameUiTheme.TryLoadTexture(UpperShellPath),
            LowerShellTex = ThingsMinigameUiTheme.TryLoadTexture(LowerShellPath),
            MouthBackTex = ThingsMinigameUiTheme.TryLoadTexture(MouthBackPath),
            HingeTex = ThingsMinigameUiTheme.TryLoadTexture(HingePath),
            GoldSmallIcon = _goldSmallIcon,
            GoldPouchIcon = _goldPouchIcon,
            GoldIngotsIcon = _goldIngotsIcon,
            PotionEmeraldIcon = _potionEmeraldIcon,
            PotionAmberIcon = _potionAmberIcon,
            CardIcon = _cardIcon,
            RelicCommonIcon = _relicCommonIcon,
            RelicRareIcon = _relicRareIcon,
            ScrapIcon = _scrapIcon,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        playfield.AddChild(_tankCanvas);
        _tankCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

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

        // Bottom-Left: Single bold vanilla DivinationsLeft counter (Gold already in top bar!)
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

        // Chunky hand-painted Drop Clam tool button
        _dropButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_CLAM_CLAW.ui.btnDrop"),
            new Vector2(372f, 86f),
            baseTint: new Color("#729aa8"),
            activeTint: new Color("#91c5d6"),
            outlineColor: ThingsMinigameUiTheme.VanillaWarmOutline,
            font: _titleFont,
            fontSize: 23,
            textColor: new Color("#fff4cf"));
        _dropButton.Pressed += () => _minigame.TryDropClaw();
        rightCenter.AddChild(_dropButton);

        // Clean narrow Crystal-Sphere plaque (ZERO dividers, ZERO 0-value table, ZERO log)
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.10f, 0.14f, 0.84f),
            padH: 24,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 14);
        plaque.AddChild(plaqueBox);

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeVanillaPlaqueBody(
            ThingsMinigameUiTheme.Tr("THINGS_CLAM_CLAW.ui.rules"),
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

    private void RefreshStatus()
    {
        _bottomStatusRtl.Text = ThingsMinigameUiTheme.Tr(
            "THINGS_CLAM_CLAW.ui.grabsLabel",
            ("Remaining", $"[color=#ffd100]{_minigame.GrabsRemaining}[/color]"),
            ("Max", _minigame.MaxGrabs));

        int totalRelics = _minigame.CollectedCommonRelics + _minigame.CollectedRareRelics;
        _lootStrip.SetEntries(
            (_goldPouchIcon, _minigame.CollectedGold > 0 ? $"+{_minigame.CollectedGold}G" : string.Empty, new Color("#ffe37d")),
            (_potionEmeraldIcon, _minigame.CollectedPotions > 0 ? $"×{_minigame.CollectedPotions}" : string.Empty, new Color("#9cf0cc")),
            (_cardIcon, _minigame.CollectedCards > 0 ? $"×{_minigame.CollectedCards}" : string.Empty, new Color("#a4d2ff")),
            (_minigame.CollectedRareRelics > 0 ? _relicRareIcon : _relicCommonIcon,
                totalRelics > 0 ? $"×{totalRelics}" : string.Empty,
                new Color("#ffe89c")));

        // In-world floating pop at the clam on catch/slip/miss (replaces right-panel console log)
        if (_hasOpened && (_minigame.LastFeedbackKey != _lastFeedbackKey || _minigame.LastFeedbackValue != _lastFeedbackValue))
        {
            _lastFeedbackKey = _minigame.LastFeedbackKey;
            _lastFeedbackValue = _minigame.LastFeedbackValue;
            PopClamFeedback(_lastFeedbackKey, _lastFeedbackValue);
        }

        bool canDrop = _minigame.CanDrop();
        _dropButton.Disabled = !canDrop;
        _dropButton.Active = canDrop;

        bool hasLoot = _minigame.BuildOutcome().HasAnyLoot;
        if (_minigame.IsComplete)
        {
            _finishButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_CLAM_CLAW.ui.btnClaim" : "THINGS_CLAM_CLAW.ui.btnLeaveEmpty");
            _finishButton.Active = true;
            _finishButton.Disabled = false;
        }
        else
        {
            _finishButton.Text = ThingsMinigameUiTheme.Tr(
                hasLoot ? "THINGS_CLAM_CLAW.ui.btnFinishEarly" : "THINGS_CLAM_CLAW.ui.btnLeaveEmpty");
            _finishButton.Active = false;
            _finishButton.Disabled = _minigame.Phase != ClamCranePhase.Aiming;
        }

        _tankCanvas.QueueRedraw();
    }

    private void PopClamFeedback(string key, int val)
    {
        if (string.IsNullOrEmpty(key) || key.EndsWith("Ready") || key.EndsWith("Dropping") || key.EndsWith("Finished"))
            return;

        Vector2 pos = new(Math.Clamp(_minigame.CraneX, 110f, 520f), Math.Clamp(_minigame.CraneY - 30f, 110f, 440f));
        Color col = key.Contains("Slipped") || key.Contains("Miss")
            ? new Color("#ff9c94")
            : (key.Contains("Rare") ? new Color("#ffe568") : new Color("#f6e4a8"));
        string text = ThingsMinigameUiTheme.Tr(key, ("Value", val));
        _popLayer.Pop(pos, text, col, 22);
    }
}
