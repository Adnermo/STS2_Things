using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Depths multiplayer-exclusive Bronze Tripod Scramble overlay screen styled after vanilla
/// CrystalSphere + Treasure Room multiplayer hands:
/// colossal overhead hand-painted bronze cauldron filled with 24-30 gleaming Mega Crit treasures,
/// full-screen vanilla Treasure Room character arms (ArmPaperTexture -> ArmRockTexture) anchored
/// outside the screen edges with authentic NHandImage shoulder-tilt physics, glowing palm-capture
/// circle, dramatic 3-2-1 countdown, and live multiplayer standings plaque.
/// </summary>
public partial class NBronzeCauldronScreen : Control, IOverlayScreen
{
    private const string BackdropPath = "res://images/events/minigames/things_bronze_cauldron_bg.png";

    private static readonly Color[] SeatColors =
    [
        new Color("#ffd458"), // Slot 0: Warm Gold
        new Color("#6ee89a"), // Slot 1: Emerald
        new Color("#6cd8ff"), // Slot 2: Sky Cyan
        new Color("#ff8a7a"), // Slot 3: Coral
    ];

    /// <summary>
    /// Draws the 24-30 gleaming hand-painted treasures and grab shockwave ripples inside the colossal
    /// 720x720 bronze cauldron mouth cavity (stage position 400..1120, 180..900).
    /// </summary>
    private sealed partial class CauldronArenaCanvas : Control
    {
        public BronzeCauldronMinigame? Minigame { get; set; }
        public Texture2D? RareRelicIcon { get; set; }
        public Texture2D? CommonRelicIcon { get; set; }
        public Texture2D? GoldSingleIcon { get; set; }
        public Texture2D? GoldStackIcon { get; set; }
        public Texture2D? PotionEmeraldIcon { get; set; }
        public Texture2D? PotionAmberIcon { get; set; }
        public Texture2D? CardIcon { get; set; }
        public Texture2D? UpgradeIcon { get; set; }
        public Texture2D? MaxHpIcon { get; set; }
        public Font? BodyFont { get; set; }

        public override void _Draw()
        {
            if (Minigame == null)
                return;

            // 0. Subtle rotating clockwise golden swirl cues inside the bronze basin so players read the spin direction at a glance
            Vector2 center = new(BronzeCauldronMinigame.Center, BronzeCauldronMinigame.Center);
            float spin = Minigame.OrbitAngle;
            float[] swirlRadii = [118f, 206f, 286f];
            for (int rIdx = 0; rIdx < swirlRadii.Length; rIdx++)
            {
                float r = swirlRadii[rIdx];
                for (int k = 0; k < 3; k++)
                {
                    float startA = spin + rIdx * 0.72f + k * (MathF.Tau / 3f);
                    float spanA = 0.78f;
                    DrawArc(center, r, startA, startA + spanA, 16, new Color(1.0f, 0.82f, 0.34f, 0.14f), 2.5f, true);
                    // Clockwise arrowhead at leading end (startA + spanA)
                    float tipA = startA + spanA;
                    Vector2 tip = center + new Vector2(MathF.Cos(tipA), MathF.Sin(tipA)) * r;
                    Vector2 tangent = new(-MathF.Sin(tipA), MathF.Cos(tipA)); // clockwise unit tangent (+angle)
                    Vector2 radial = new(MathF.Cos(tipA), MathF.Sin(tipA));
                    Vector2[] arrow =
                    [
                        tip + tangent * 6f,
                        tip - tangent * 5f + radial * 5f,
                        tip - tangent * 5f - radial * 5f,
                    ];
                    DrawColoredPolygon(arrow, new Color(1.0f, 0.86f, 0.42f, 0.24f));
                }
            }

            // Identify items currently covered by the local player's open palm for instant tactile feedback
            CauldronHandState? localHand = Minigame.LocalHand;
            HashSet<int> coveredByLocal = [];
            if (localHand != null && Minigame.Phase == CauldronPhase.Scramble && localHand.GrabsRemaining > 0)
            {
                foreach (CauldronTreasureItem cov in Minigame.GetItemsCoveredByPalm(localHand.X, localHand.Y))
                    coveredByLocal.Add(cov.Index);
            }

            // 1. Draw the carpeting layer of 3-10G gold coins first, then featured treasures & Cardboard Relic Boxes cleanly on top!
            foreach (CauldronTreasureItem item in Minigame.Items)
            {
                if (item.Kind != CauldronTreasureKind.Gold)
                    continue;
                if (item.IsClaimed && item.ClaimAnimTimer <= 0.01f)
                    continue;
                DrawTreasureItem(item, coveredByLocal.Contains(item.Index));
            }
            foreach (CauldronTreasureItem item in Minigame.Items)
            {
                if (item.Kind == CauldronTreasureKind.Gold)
                    continue;
                if (item.IsClaimed && item.ClaimAnimTimer <= 0.01f)
                    continue;
                DrawTreasureItem(item, coveredByLocal.Contains(item.Index));
            }

            // 2. Draw grab burst ripples (gold hit ring or crimson false-start/miss ring)
            foreach (CauldronHandState hand in Minigame.Hands)
            {
                if (hand.LastBurstTimer <= 0.01f)
                    continue;
                float t = 1f - Math.Clamp(hand.LastBurstTimer / 0.55f, 0f, 1f);
                float r = BronzeCauldronMinigame.PalmCaptureRadius * (0.72f + t * 0.65f);
                Color ringCol = hand.LastBurstHit
                    ? new Color(1.0f, 0.85f, 0.30f, (1f - t) * 0.90f)
                    : new Color(1.0f, 0.38f, 0.35f, (1f - t) * 0.85f);
                DrawArc(new Vector2(hand.LastBurstX, hand.LastBurstY), r, 0f, MathF.Tau, 36, ringCol, 5.5f * (1f - t * 0.5f), true);
            }
        }

        private Texture2D? ResolveItemTexture(CauldronTreasureItem item) => item.Kind switch
        {
            CauldronTreasureKind.RareRelic => RareRelicIcon ?? CommonRelicIcon,
            CauldronTreasureKind.UncommonRelic or CauldronTreasureKind.CommonRelic => CommonRelicIcon,
            CauldronTreasureKind.UpgradeCard => UpgradeIcon,
            CauldronTreasureKind.MaxHp => MaxHpIcon,
            CauldronTreasureKind.Potion => (item.VisualVariant == 0 ? PotionEmeraldIcon : PotionAmberIcon) ?? PotionEmeraldIcon,
            CauldronTreasureKind.Card => CardIcon,
            _ => (item.VisualVariant == 0 ? GoldSingleIcon : GoldStackIcon) ?? GoldSingleIcon,
        };

        private void DrawTreasureItem(CauldronTreasureItem item, bool hoveredByLocal)
        {
            Vector2 pos = new(item.X, item.Y);
            float scale = item.IsClaimed ? (1.0f + (1.0f - item.ClaimAnimTimer) * 0.48f) : (hoveredByLocal ? 1.12f : 1.0f);
            float alpha = item.IsClaimed ? item.ClaimAnimTimer : 1.0f;

            float baseDim = item.Kind switch
            {
                CauldronTreasureKind.RareRelic => 76f,
                CauldronTreasureKind.CommonRelic or CauldronTreasureKind.UncommonRelic => 68f,
                CauldronTreasureKind.UpgradeCard => 58f,
                CauldronTreasureKind.MaxHp => 52f,
                CauldronTreasureKind.Potion => 54f,
                CauldronTreasureKind.Card => 56f,
                CauldronTreasureKind.Gold => item.VisualVariant == 1 ? 45f : 38f,
                _ => 46f,
            };
            float dim = baseDim * scale;

            // Grounded drop shadow inside the dark hammered bronze basin
            if (!item.IsClaimed)
            {
                DrawSetTransform(pos + new Vector2(1.5f, dim * 0.30f), 0f, new Vector2(1.0f, 0.42f));
                DrawCircle(Vector2.Zero, dim * 0.40f, new Color(0.03f, 0.02f, 0.01f, 0.48f));
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            // Radiant aura ONLY on Rare Cardboard Boxes or items currently covered by your open palm!
            if (hoveredByLocal || item.Kind == CauldronTreasureKind.RareRelic)
            {
                Color halo = item.Kind == CauldronTreasureKind.RareRelic
                    ? new Color(1.0f, 0.84f, 0.20f, (hoveredByLocal ? 0.56f : 0.36f) * alpha)
                    : new Color(1.0f, 0.88f, 0.38f, 0.32f * alpha);
                float hr = dim * 0.52f;
                DrawCircle(pos, hr, halo);
                if (hoveredByLocal || item.Kind == CauldronTreasureKind.RareRelic)
                {
                    Color rim = item.Kind == CauldronTreasureKind.RareRelic ? new Color("#ffe45e") : new Color("#fff3ba");
                    DrawArc(pos, hr + 2f, 0f, MathF.Tau, 24, new Color(rim.R, rim.G, rim.B, alpha), hoveredByLocal ? 2.6f : 2.0f, true);
                }
            }

            Texture2D? icon = ResolveItemTexture(item);
            if (icon != null)
            {
                Vector2 texSz = icon.GetSize();
                float aspect = texSz.Y > 0f ? texSz.X / texSz.Y : 1f;
                float drawW = aspect >= 1f ? dim : dim * aspect;
                float drawH = aspect >= 1f ? dim / aspect : dim;
                DrawSetTransform(pos, item.TiltAngle, Vector2.One);
                if (item.Kind == CauldronTreasureKind.MaxHp)
                {
                    DrawCircle(Vector2.Zero, dim * 0.44f, new Color(0.05f, 0.03f, 0.04f, 0.56f * alpha));
                }
                DrawTextureRect(icon, new Rect2(-drawW * 0.5f, -drawH * 0.5f, drawW, drawH), false, new Color(1f, 1f, 1f, alpha));
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            // Crisp minimalist tag ONLY on Cardboard Relic Boxes, +MaxHP hearts, or hovered items (NEVER spam 48 coin labels!)
            bool showTag = hoveredByLocal
                || item.Kind == CauldronTreasureKind.RareRelic
                || item.Kind == CauldronTreasureKind.CommonRelic
                || item.Kind == CauldronTreasureKind.MaxHp;
            if (showTag && BodyFont != null && !item.IsClaimed)
            {
                (string tag, Color col) = item.Kind switch
                {
                    CauldronTreasureKind.RareRelic => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagRareRelic"), new Color("#ffe668")),
                    CauldronTreasureKind.UncommonRelic => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagUncommonRelic"), new Color("#84ecff")),
                    CauldronTreasureKind.CommonRelic => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagCommonRelic"), new Color("#f0dc9c")),
                    CauldronTreasureKind.UpgradeCard => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagUpgrade"), new Color("#ffc070")),
                    CauldronTreasureKind.MaxHp => ($"+{item.Value}HP", new Color("#ff98a8")),
                    CauldronTreasureKind.Potion => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagPotion"), new Color("#8cf0c8")),
                    CauldronTreasureKind.Card => (ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.tagCard"), new Color("#a4ceff")),
                    _ => ($"+{item.Value}G", new Color("#ffe482")),
                };
                float ly = pos.Y + dim * 0.44f + 9f;
                DrawStringOutline(BodyFont, new Vector2(pos.X - 44f, ly), tag, HorizontalAlignment.Center, 88f, 13, 5, new Color("#030a0f"));
                DrawString(BodyFont, new Vector2(pos.X - 44f, ly), tag, HorizontalAlignment.Center, 88f, 13, col);
            }
        }
    }

    /// <summary>
    /// Full-screen 1920x1080 overlay canvas rendering every player's authentic vanilla Treasure Room
    /// arm (366x1024px matching hand_image.tscn's 383x1072 geometry!) extending seamlessly from
    /// OUTSIDE the screen edges with vanilla NHandImage shoulder-pivot tilt, plus the 3-2-1 countdown.
    /// </summary>
    private sealed partial class CauldronHandsCanvas : Control
    {
        // Matches vanilla scenes/ui/hand_image.tscn (383x1072, GrabMarker at (175/383=0.457, 222/1072=0.207))
        private const float VanillaArmWidth = 366f;
        private const float VanillaArmHeight = 1024f;
        private const float PalmAnchorX = 0.457f;
        private const float PalmAnchorY = 0.207f;

        public BronzeCauldronMinigame? Minigame { get; set; }
        public Dictionary<ulong, (Texture2D? Paper, Texture2D? Rock)> HandTextures { get; } = [];
        public Font? TitleFont { get; set; }
        public Font? BodyFont { get; set; }

        public override void _Draw()
        {
            if (Minigame == null)
                return;

            CauldronHandState? localHand = Minigame.LocalHand;

            // 1. Remote teammates first (slightly translucent like vanilla NHandImage), local player crisp on top!
            foreach (CauldronHandState hand in Minigame.Hands)
            {
                if (hand.Player.NetId != Minigame.LocalPlayerNetId)
                    DrawOffscreenPlayerArm(hand, isLocal: false);
            }
            if (localHand != null)
                DrawOffscreenPlayerArm(localHand, isLocal: true);

            // 2. Dramatic 3 - 2 - 1 Countdown Overlay right across the center of the giant cauldron!
            if (Minigame.Phase == CauldronPhase.Countdown && TitleFont != null)
            {
                Vector2 center = new(
                    BronzeCauldronMinigame.StageOriginX + BronzeCauldronMinigame.Center,
                    BronzeCauldronMinigame.StageOriginY + BronzeCauldronMinigame.Center);
                int sec = Math.Clamp((int)MathF.Ceiling(Minigame.CountdownRemaining), 1, 3);
                float frac = Minigame.CountdownRemaining - MathF.Floor(Minigame.CountdownRemaining);
                int fontSize = (int)(134 + frac * 38);
                string numText = sec.ToString();

                DrawCircle(center, 108f, new Color(0.02f, 0.05f, 0.09f, 0.72f));
                DrawArc(center, 108f, 0f, MathF.Tau, 44, new Color("#ffd458"), 6f, true);
                DrawStringOutline(TitleFont, center + new Vector2(-120f, fontSize * 0.34f), numText, HorizontalAlignment.Center, 240f, fontSize, 18, new Color("#050d12"));
                DrawString(TitleFont, center + new Vector2(-120f, fontSize * 0.34f), numText, HorizontalAlignment.Center, 240f, fontSize, new Color("#ffe37d"));
            }
        }

        private void DrawOffscreenPlayerArm(CauldronHandState hand, bool isLocal)
        {
            // Convert board-local coordinates into full 1920x1080 stage coordinates!
            Vector2 screenPos = new(
                BronzeCauldronMinigame.StageOriginX + hand.X,
                BronzeCauldronMinigame.StageOriginY + hand.Y);
            Vector2 cauldronCenter = new(
                BronzeCauldronMinigame.StageOriginX + BronzeCauldronMinigame.Center,
                BronzeCauldronMinigame.StageOriginY + BronzeCauldronMinigame.Center);

            Color seatCol = SeatColors[hand.SlotIndex % SeatColors.Length];
            int slot = hand.SlotIndex % 4;

            // 1. Palm capture coverage ring ("手心覆盖区域") — muted during 3-2-1 countdown, crisp gold once left-click grab opens
            bool isLive = Minigame?.Phase == CauldronPhase.Scramble;
            Color ringCol = new(seatCol.R, seatCol.G, seatCol.B, isLocal ? (isLive ? 0.88f : 0.42f) : 0.46f);
            Color fillCol = new(seatCol.R, seatCol.G, seatCol.B, isLocal ? (isLive ? 0.14f : 0.05f) : 0.07f);

            DrawCircle(screenPos, BronzeCauldronMinigame.PalmCaptureRadius, fillCol);
            DrawArc(screenPos, BronzeCauldronMinigame.PalmCaptureRadius, 0f, MathF.Tau, 40, ringCol, isLocal ? 3.5f : 2.2f, true);

            // 2. Authentic vanilla Treasure Room character arm anchored OUTSIDE the screen border!
            //    Includes vanilla NHandImage._Process shoulder-pivot tilt so arms feel alive & grounded off-screen.
            HandTextures.TryGetValue(hand.Player.NetId, out var texPair);
            Texture2D? tex = hand.IsClenched ? (texPair.Rock ?? texPair.Paper) : (texPair.Paper ?? texPair.Rock);
            if (tex != null)
            {
                float baseRot = slot switch
                {
                    0 => 0f,             // Bottom screen edge -> reaching up
                    1 => MathF.PI,       // Top screen edge    -> reaching down
                    2 => MathF.PI * 0.5f,// Left screen edge   -> reaching right
                    _ => -MathF.PI * 0.5f,// Right screen edge -> reaching left
                };

                // Vanilla NHandImage._Process tilt formula around cauldron center!
                float shoulderTilt = slot switch
                {
                    0 => Math.Clamp((screenPos.X - cauldronCenter.X) / 1800f, -0.30f, 0.30f),
                    1 => Math.Clamp(-(screenPos.X - cauldronCenter.X) / 1800f, -0.30f, 0.30f),
                    2 => Math.Clamp((screenPos.Y - cauldronCenter.Y) / 1100f, -0.30f, 0.30f),
                    _ => Math.Clamp(-(screenPos.Y - cauldronCenter.Y) / 1100f, -0.30f, 0.30f),
                };

                float slamScale = hand.IsClenched ? 0.94f : 1.0f;
                float armW = VanillaArmWidth * slamScale;
                float armH = VanillaArmHeight * slamScale;
                // Slightly translucent when hovering open so treasures beneath remain crisp; bright opaque when clenching!
                Color mod = isLocal
                    ? new Color(1f, 1f, 1f, hand.IsClenched ? 0.96f : 0.84f)
                    : new Color(0.86f, 0.88f, 0.92f, hand.IsClenched ? 0.82f : 0.62f);

                DrawSetTransform(screenPos, baseRot + shoulderTilt, Vector2.One);
                DrawTextureRect(
                    tex,
                    new Rect2(-armW * PalmAnchorX, -armH * PalmAnchorY, armW, armH),
                    false,
                    mod);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            // 3. Crisp player seat badge kept cleanly inside the 1920x1080 viewport even when hand rests near screen edge
            if (BodyFont != null)
            {
                string badge = $"P{hand.SlotIndex + 1} · {hand.Player.Character.Title.GetFormattedText()} ({hand.GrabsRemaining})";
                float labelY = slot == 1
                    ? Math.Clamp(screenPos.Y + BronzeCauldronMinigame.PalmCaptureRadius + 22f, 28f, 1054f)
                    : Math.Clamp(screenPos.Y - BronzeCauldronMinigame.PalmCaptureRadius - 10f, 28f, 1054f);
                float labelX = Math.Clamp(screenPos.X - 96f, 16f, 1180f);
                DrawStringOutline(BodyFont, new Vector2(labelX, labelY), badge, HorizontalAlignment.Center, 192f, 15, 6, new Color("#030a0f"));
                DrawString(BodyFont, new Vector2(labelX, labelY), badge, HorizontalAlignment.Center, 192f, 15, seatCol);
            }
        }
    }

    private BronzeCauldronMinigame _minigame = null!;
    private CauldronArenaCanvas _arenaCanvas = null!;
    private CauldronHandsCanvas _handsCanvas = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private RichTextLabel _standingsRtl = null!;
    private RichTextLabel _feedbackRtl = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _finishButton = null!;

    private Font? _titleFont;
    private Font? _bodyFont;
    private float _handSyncTimer;

    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _finishButton;

    public static NBronzeCauldronScreen Create(BronzeCauldronMinigame minigame)
    {
        var screen = new NBronzeCauldronScreen
        {
            Name = "STS2ThingsBronzeCauldronScreen",
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
        float dt = (float)delta;
        _minigame.Advance(dt);

        // Broadcast local hand hover position to teammates at ~20Hz
        _handSyncTimer += dt;
        if (_handSyncTimer >= 0.05f && !_minigame.IsComplete)
        {
            _handSyncTimer = 0f;
            CauldronHandState? local = _minigame.LocalHand;
            if (local != null)
                ThingsEventMinigameSync.SendCauldronHandUpdate(local.X, local.Y, local.IsClenched);
        }

        _arenaCanvas.QueueRedraw();
        _handsCanvas.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (!Visible || _minigame == null)
            return;
        // Grabbing is driven exclusively by left-click; Space/Enter only dismisses once finished.
        if (ev is InputEventKey key && key.Pressed && !key.Echo &&
            (key.Keycode == Key.Space || key.Keycode == Key.Enter) &&
            _minigame.IsComplete)
        {
            _minigame.ConfirmUiExit();
            GetViewport().SetInputAsHandled();
        }
    }

    public void AfterOverlayOpened()
    {
        if (_minigame != null)
            _minigame.StateChanged += RefreshStatus;
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

        var outerFill = new ColorRect
        {
            Color = new Color("#050912"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var stage = new Control
        {
            Name = "CauldronStage",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
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
            Name = "CauldronBackdrop",
            Texture = ThingsMinigameUiTheme.TryLoadTexture(BackdropPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Full-stage hover/click surface so players smoothly guide off-screen arms across the entire cauldron & rim!
        var stageInput = new Control
        {
            Name = "CauldronStageInput",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Cross,
        };
        stage.AddChild(stageInput);
        stageInput.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stageInput.GuiInput += ev =>
        {
            if (ev is InputEventMouseMotion mm && !_minigame.IsComplete)
            {
                _minigame.UpdateLocalHandPosition(
                    mm.Position.X - BronzeCauldronMinigame.StageOriginX,
                    mm.Position.Y - BronzeCauldronMinigame.StageOriginY);
            }
            else if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && !_minigame.IsComplete)
            {
                _minigame.UpdateLocalHandPosition(
                    mb.Position.X - BronzeCauldronMinigame.StageOriginX,
                    mb.Position.Y - BronzeCauldronMinigame.StageOriginY);
                // Ignore left-clicks completely before the opening 3-2-1 countdown finishes!
                if (_minigame.Phase == CauldronPhase.Scramble)
                    TriggerLocalGrab();
                AcceptEvent();
            }
        };

        // Colossal 720x720 Bronze Tripod mouth cavity aligned at (400, 180) -> center (760, 540)
        var playfield = new Control
        {
            Name = "CauldronPlayfield",
            Position = new Vector2(BronzeCauldronMinigame.StageOriginX, BronzeCauldronMinigame.StageOriginY),
            Size = new Vector2(BronzeCauldronMinigame.BoardSize, BronzeCauldronMinigame.BoardSize),
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stage.AddChild(playfield);

        _arenaCanvas = new CauldronArenaCanvas
        {
            Name = "CauldronArenaCanvas",
            Minigame = _minigame,
            RareRelicIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/minigames/things_loot_relic_rare_box.png")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png"),
            CommonRelicIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png"),
            GoldSingleIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png"),
            GoldStackIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_big_gold.png")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_gold.png"),
            PotionEmeraldIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_rare_potion.png"),
            PotionAmberIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_rare_potion.png"),
            CardIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png"),
            UpgradeIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/hammer_time_power.png"),
            MaxHpIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png"),
            BodyFont = _bodyFont,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        playfield.AddChild(_arenaCanvas);
        _arenaCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Full-screen 1920x1080 vanilla off-screen hands layer (reaches smoothly over rim & floor from screen edges!)
        _handsCanvas = new CauldronHandsCanvas
        {
            Name = "HandsLayer",
            Minigame = _minigame,
            TitleFont = _titleFont,
            BodyFont = _bodyFont,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        // Load each player's authentic vanilla treasure-room Paper & Rock hands with safe Ironclad fallback
        Texture2D? fallbackPaper = ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/hands/multiplayer_hand_ironclad_paper.png");
        Texture2D? fallbackRock = ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/hands/multiplayer_hand_ironclad_rock.png");
        foreach (CauldronHandState hand in _minigame.Hands)
        {
            string slug = hand.Player.Character.Id.Entry.ToLowerInvariant();
            Texture2D? paper = ThingsMinigameUiTheme.TryLoadTexture($"res://images/ui/hands/multiplayer_hand_{slug}_paper.png") ?? fallbackPaper;
            Texture2D? rock = ThingsMinigameUiTheme.TryLoadTexture($"res://images/ui/hands/multiplayer_hand_{slug}_rock.png") ?? fallbackRock;
            _handsCanvas.HandTextures[hand.Player.NetId] = (paper, rock);
        }

        stage.AddChild(_handsCanvas);
        _handsCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Floating Crystal-Sphere HUD Layer (above hands so standings & Claim button always stay clickable/readable!)
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var titleRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#f4d678]{ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.title")}[/color]",
            36,
            new Color("#f4d678"),
            new Color("#060b14"),
            _titleFont,
            outlineSize: 10,
            shadowX: 4,
            shadowY: 3,
            align: HorizontalAlignment.Left,
            wrap: false);
        hud.AddChild(titleRtl);
        titleRtl.Position = new Vector2(44f, 20f);
        titleRtl.CustomMinimumSize = new Vector2(520f, 52f);

        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            26,
            new Color("#eef4f8"),
            new Color("#050a12"),
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
        _bottomStatusRtl.OffsetLeft = 40f;
        _bottomStatusRtl.OffsetRight = 400f;
        _bottomStatusRtl.OffsetTop = -126f;
        _bottomStatusRtl.OffsetBottom = -18f;

        var hintRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#b0c8dc]{ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.controlsHint")}[/color]",
            16,
            new Color("#b0c8dc"),
            new Color("#040a12"),
            _bodyFont,
            outlineSize: 6,
            shadowX: 2,
            shadowY: 2,
            align: HorizontalAlignment.Center,
            wrap: true);
        hud.AddChild(hintRtl);
        hintRtl.AnchorLeft = 0.5f;
        hintRtl.AnchorRight = 0.5f;
        hintRtl.AnchorTop = 1f;
        hintRtl.AnchorBottom = 1f;
        hintRtl.OffsetLeft = -560f;
        hintRtl.OffsetRight = 160f;
        hintRtl.OffsetTop = -44f;
        hintRtl.OffsetBottom = -8f;

        // Right Column cleanly right of the colossal bronze cauldron's right handle (x = 1364..1872)
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
        rightAnchor.OffsetRight = -48f;
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

        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.03f, 0.08f, 0.14f, 0.88f),
            padH: 26,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 12);
        plaque.AddChild(plaqueBox);

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeLabel(
            ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.standingsTitle"),
            22,
            new Color("#f4d678"),
            _titleFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5));

        _standingsRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#edf4fa"),
            new Color("#040910"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2);
        _standingsRtl.CustomMinimumSize = new Vector2(0f, 92f);
        plaqueBox.AddChild(_standingsRtl);

        plaqueBox.AddChild(MakeDivider(new Color("#365975", 0.55f)));

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#d4e4f0]{ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.rules", ("Grabs", BronzeCauldronMinigame.GrabsPerPlayer))}[/color]",
            16,
            new Color("#d4e4f0"),
            new Color("#040910"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2));

        plaqueBox.AddChild(MakeDivider(new Color("#365975", 0.55f)));

        _feedbackRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#f5e2ad"),
            new Color("#040910"),
            _bodyFont,
            outlineSize: 5,
            shadowX: 2,
            shadowY: 2);
        _feedbackRtl.CustomMinimumSize = new Vector2(0f, 52f);
        plaqueBox.AddChild(_feedbackRtl);

        _finishButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.btnClaim"),
            new Vector2(508f, 76f),
            baseTint: new Color("#556e80"),
            activeTint: new Color("#6bbd94"),
            outlineColor: new Color(1.0f, 0.80f, 0.24f, 1.0f),
            font: _titleFont,
            fontSize: 22,
            textColor: new Color("#fff8d9"));
        _finishButton.Pressed += () => _minigame.ConfirmUiExit();
        rightCenter.AddChild(_finishButton);
    }

    private void TriggerLocalGrab()
    {
        if (_minigame.TryLocalGrab(out float gx, out float gy, out bool sendToHost) && sendToHost)
            ThingsEventMinigameSync.SendCauldronGrabRequest(gx, gy);
    }

    private static ColorRect MakeDivider(Color color) => new()
    {
        Color = color,
        CustomMinimumSize = new Vector2(0f, 2f),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private void RefreshStatus()
    {
        CauldronHandState? local = _minigame.LocalHand;
        int myGrabs = local?.GrabsRemaining ?? 0;
        string grabsLine = ThingsMinigameUiTheme.Tr(
            "THINGS_BRONZE_CAULDRON.ui.grabsLabel",
            ("Remaining", $"[color=#ffd100]{myGrabs}[/color]"),
            ("Max", BronzeCauldronMinigame.GrabsPerPlayer));

        string timerLine = _minigame.Phase switch
        {
            CauldronPhase.Countdown => ThingsMinigameUiTheme.Tr(
                "THINGS_BRONZE_CAULDRON.ui.timerCountdown",
                ("Seconds", $"[color=#ff9e78]{Math.Max(1, (int)MathF.Ceiling(_minigame.CountdownRemaining))}[/color]")),
            CauldronPhase.Scramble => ThingsMinigameUiTheme.Tr(
                "THINGS_BRONZE_CAULDRON.ui.timerScramble",
                ("Seconds", $"[color=#96e6a8]{Math.Max(0, (int)MathF.Ceiling(_minigame.ScrambleRemaining))}[/color]")),
            _ => ThingsMinigameUiTheme.Tr("THINGS_BRONZE_CAULDRON.ui.timerDone"),
        };
        _bottomStatusRtl.Text = $"{grabsLine}\n[font_size=20]{timerLine}[/font_size]";

        var standingLines = new List<string>();
        foreach (CauldronHandState hand in _minigame.Hands)
        {
            ThingsBronzeCauldronPlayerOutcome po = _minigame.BuildPlayerOutcome(hand.Player.NetId);
            Color sc = SeatColors[hand.SlotIndex % SeatColors.Length];
            string hex = "#" + sc.ToHtml(false);
            int totalRelics = po.RareRelicCount + po.UncommonRelicCount + po.CommonRelicCount;
            standingLines.Add(ThingsMinigameUiTheme.Tr(
                "THINGS_BRONZE_CAULDRON.ui.standingRow",
                ("Color", hex),
                ("Slot", hand.SlotIndex + 1),
                ("Name", hand.Player.Character.Title.GetFormattedText()),
                ("Items", po.ItemsGrabbed),
                ("Relics", totalRelics),
                ("Gold", po.GoldAmount),
                ("Left", hand.GrabsRemaining)));
        }
        _standingsRtl.Text = string.Join("\n", standingLines);

        string fb = ThingsMinigameUiTheme.Tr(
            _minigame.LastFeedbackKey,
            ("Actor", _minigame.LastFeedbackActor),
            ("Value", _minigame.LastFeedbackValue));
        string fbColor = _minigame.LastFeedbackKey.Contains("FalseStart") || _minigame.LastFeedbackKey.Contains("Miss")
            ? "#ff9b94"
            : (_minigame.IsComplete ? "#9de8a9" : "#f5e2ad");
        _feedbackRtl.Text = $"[color={fbColor}]{fb}[/color]";

        _finishButton.Disabled = !_minigame.IsComplete;
        _finishButton.Active = _minigame.IsComplete;
        _arenaCanvas.QueueRedraw();
        _handsCanvas.QueueRedraw();
    }
}
