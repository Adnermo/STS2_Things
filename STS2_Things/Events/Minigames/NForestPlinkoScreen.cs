using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Overgrowth (Act 1A) Briar Plinko overlay screen styled after vanilla CrystalSphere:
/// full-bleed hand-painted hollow Overgrowth trunk backdrop, animated amber-sap bed shader,
/// 7 selectable wooden drop notches, 45 briar pegs &amp; 2 glow-shroom bonus bumpers (+10G),
/// glowing golden amber seed trail, 7 asymmetric carved root-cup bins, floating outlined HUD,
/// and hand-painted divine_button controls.
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
        public Font? LabelFont { get; set; }

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
                    DrawCircle(new Vector2(sx, ForestPlinkoMinigame.DropOriginY), 21f, new Color(1.0f, 0.80f, 0.25f, 0.30f));
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
                    // Subtle dotted downward preview cue above row 0
                    for (float dy = 56f; dy < 96f; dy += 11f)
                        DrawCircle(new Vector2(sx, dy), 2.3f, new Color(1.0f, 0.88f, 0.48f, 0.55f));
                }
            }

            // 2. 45 Briar Pegs + 2 Glow-Shroom Bonus Bumpers
            foreach (PlinkoPeg peg in Minigame.Pegs)
            {
                Vector2 pos = new(peg.X, peg.Y);
                if (peg.IsBumper)
                {
                    float glowR = peg.Radius + 11f + peg.Flash * 10f;
                    DrawCircle(pos, glowR, new Color(0.24f, 0.90f, 0.78f, 0.24f + peg.Flash * 0.45f));
                    if (ShroomTex != null)
                    {
                        float dim = 44f + peg.Flash * 6f;
                        DrawTextureRect(ShroomTex, new Rect2(pos.X - dim * 0.5f, pos.Y - dim * 0.5f, dim, dim), false, Colors.White);
                    }
                    else
                    {
                        DrawCircle(pos, peg.Radius, new Color("#7ef29a"));
                    }
                    if (LabelFont != null)
                    {
                        string tag = $"+{ForestPlinkoMinigame.BumperBonusGold}G";
                        DrawStringOutline(LabelFont, pos + new Vector2(-24f, 30f), tag, HorizontalAlignment.Center, 48f, 14, 5, new Color("#030e06"));
                        DrawString(LabelFont, pos + new Vector2(-24f, 30f), tag, HorizontalAlignment.Center, 48f, 14, new Color("#ffe482"));
                    }
                }
                else
                {
                    if (peg.Flash > 0.01f)
                        DrawCircle(pos, peg.Radius + 6f + peg.Flash * 6f, new Color(1.0f, 0.84f, 0.34f, peg.Flash * 0.55f));
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

            // 3. Bottom 7 Hand-Painted Carved Mossy Ironwood Root Bowls
            float binY = ForestPlinkoMinigame.BinTopY;
            float binH = h - binY - 4f;
            DrawRect(new Rect2(6f, binY + 20f, w - 12f, binH - 16f), new Color(0.02f, 0.05f, 0.03f, 0.30f));
            for (int i = 0; i < Minigame.Bins.Count; i++)
            {
                PlinkoBin bin = Minigame.Bins[i];
                float bx = i * ForestPlinkoMinigame.BinWidth;
                float bw = ForestPlinkoMinigame.BinWidth;
                float cx = bx + bw * 0.5f;

                Color glowTint = bin.Kind switch
                {
                    PlinkoBinKind.RareRelic => new Color(1.0f, 0.84f, 0.22f, 0.42f + bin.Flash * 0.45f),
                    PlinkoBinKind.CommonRelic or PlinkoBinKind.UpgradeCard or PlinkoBinKind.GoldBig => new Color(1.0f, 0.84f, 0.34f, 0.26f + bin.Flash * 0.45f),
                    PlinkoBinKind.Potion or PlinkoBinKind.CardReward => new Color(0.36f, 0.86f, 0.90f, 0.22f + bin.Flash * 0.45f),
                    _ => new Color(0.45f, 0.90f, 0.52f, 0.20f + bin.Flash * 0.45f),
                };
                DrawCircle(new Vector2(cx, binY + 42f), 36f + bin.Flash * 8f, glowTint);

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

                // Official vanilla STS2 reward icon nestled cleanly in the open carved root bowl
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
                    float maxDim = (bin.Kind == PlinkoBinKind.RareRelic ? 50f : 46f) + bin.Flash * 4f;
                    Vector2 texSz = icon.GetSize();
                    float aspect = texSz.Y > 0f ? texSz.X / texSz.Y : 1f;
                    float drawW = aspect >= 1f ? maxDim : maxDim * aspect;
                    float drawH = aspect >= 1f ? maxDim / aspect : maxDim;
                    Vector2 iconCenter = new(cx, binY + 27f);
                    if (bin.Kind == PlinkoBinKind.MaxHp)
                    {
                        // Subtle dark cushion behind the borderless top-bar heart so crimson pops crisply
                        DrawCircle(iconCenter + new Vector2(0f, 1f), maxDim * 0.46f, new Color(0.03f, 0.08f, 0.05f, 0.62f));
                    }
                    DrawTextureRect(icon, new Rect2(iconCenter.X - drawW * 0.5f, iconCenter.Y - drawH * 0.5f, drawW, drawH), false, Colors.White);
                }

                // Crisp outlined label across the front lip of the root bowl
                if (LabelFont != null)
                {
                    string lbl = bin.Kind switch
                    {
                        PlinkoBinKind.RareRelic => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binRareRelic"),
                        PlinkoBinKind.CommonRelic => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binRelic"),
                        PlinkoBinKind.Potion => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binPotion"),
                        PlinkoBinKind.Gold or PlinkoBinKind.GoldBig => $"+{bin.Amount}G",
                        PlinkoBinKind.Heal => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binHeal", ("Amount", bin.Amount)),
                        PlinkoBinKind.CardReward => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binCard"),
                        PlinkoBinKind.MaxHp => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binMaxHp", ("Amount", bin.Amount)),
                        PlinkoBinKind.UpgradeCard => ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.binUpgrade"),
                        _ => string.Empty,
                    };
                    Color lblCol = bin.Kind == PlinkoBinKind.RareRelic ? new Color("#ffe568") : new Color("#fff2cc");
                    DrawStringOutline(LabelFont, new Vector2(bx + 1f, binY + 78f), lbl, HorizontalAlignment.Center, bw - 2f, 15, 6, new Color("#030c06"));
                    DrawString(LabelFont, new Vector2(bx + 1f, binY + 78f), lbl, HorizontalAlignment.Center, bw - 2f, 15, lblCol);
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
                    DrawLine(p0, p1, new Color(1.0f, 0.80f, 0.25f, frac * (ball.Active ? 0.72f : 0.35f)), 4f + frac * 7f, true);
                }
            }

            if (ball.Active)
            {
                Vector2 bpos = new(ball.X, ball.Y);
                DrawCircle(bpos, ForestPlinkoMinigame.BallRadius + 10f, new Color(1.0f, 0.80f, 0.24f, 0.38f));
                if (SeedTex != null)
                {
                    DrawTextureRect(SeedTex, new Rect2(bpos.X - 16f, bpos.Y - 16f, 32f, 32f), false, Colors.White);
                }
                else
                {
                    DrawCircle(bpos, ForestPlinkoMinigame.BallRadius, new Color("#ffd458"));
                }
            }
        }
    }

    private ForestPlinkoMinigame _minigame = null!;
    private PlinkoBoardCanvas _boardCanvas = null!;

    private RichTextLabel _bottomStatusRtl = null!;
    private RichTextLabel _haulSummaryRtl = null!;
    private RichTextLabel _feedbackRtl = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _dropButton = null!;
    private ThingsMinigameUiTheme.PaintedActionButton _finishButton = null!;

    private Font? _titleFont;
    private Font? _bodyFont;

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

        // 1. Deep nocturnal Overgrowth outer fill
        var outerFill = new ColorRect
        {
            Color = new Color("#040d07"),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(outerFill);
        outerFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 2. Centered 1920x1080 Crystal-Sphere artwork stage
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

        // 3. Embedded 630x630 hollow Overgrowth trunk Plinko cavity (uniformly fitted inside 563..1130, 255..821 recess)
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
            ShroomTex = ThingsMinigameUiTheme.TryLoadTexture(ShroomTexPath),
            BowlTex = ThingsMinigameUiTheme.TryLoadTexture(BowlTexPath),
            RelicCommonIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png"),
            RelicRareIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/minigames/things_loot_relic_rare_box.png")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_relic.png"),
            PotionIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_rare_potion.png"),
            GoldIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_big_gold.png"),
            HealIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/regen_power.png"),
            CardIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_uncommon_card_reward.png")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/events/crystal_sphere/crystal_sphere_common_card_reward.png"),
            MaxHpIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres")
                ?? ThingsMinigameUiTheme.TryLoadTexture("res://images/ui/emote/heart.png"),
            UpgradeIcon = ThingsMinigameUiTheme.TryLoadTexture("res://images/powers/hammer_time_power.png"),
            LabelFont = _bodyFont,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        playfield.AddChild(_boardCanvas);
        _boardCanvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Subtle caption below board
        var hintRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#a7d1b0]{ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.controlsHint")}[/color]",
            17,
            new Color("#a7d1b0"),
            new Color("#041008"),
            _bodyFont,
            outlineSize: 6,
            shadowX: 2,
            shadowY: 2,
            align: HorizontalAlignment.Center,
            wrap: true);
        stage.AddChild(hintRtl);
        hintRtl.Position = new Vector2(465f, 1006f);
        hintRtl.Size = new Vector2(700f, 54f);

        // 4. Floating Crystal-Sphere HUD Layer
        var hud = new Control
        {
            Name = "HudRoot",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var titleRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#f4d678]{ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.title")}[/color]",
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

        _bottomStatusRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            27,
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
        _bottomStatusRtl.OffsetTop = -132f;
        _bottomStatusRtl.OffsetBottom = -20f;

        // Right Column
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

        // Primary Drop Seed Button
        _dropButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.btnDrop"),
            new Vector2(504f, 82f),
            baseTint: new Color("#5ea372"),
            activeTint: new Color("#74c98b"),
            outlineColor: new Color(1.0f, 0.80f, 0.24f, 1.0f),
            font: _titleFont,
            fontSize: 24,
            textColor: new Color("#fff8d9"));
        _dropButton.Pressed += () => _minigame.TryDropSeed();
        rightCenter.AddChild(_dropButton);

        // Single Dark Overgrowth Octagonal Plaque
        PanelContainer plaque = ThingsMinigameUiTheme.CreateCrystalPlaquePanel(
            new Color(0.02f, 0.11f, 0.06f, 0.88f),
            padH: 26,
            padV: 22);
        rightCenter.AddChild(plaque);

        var plaqueBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plaqueBox.AddThemeConstantOverride("separation", 12);
        plaque.AddChild(plaqueBox);

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeLabel(
            ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.haulTitle"),
            22,
            new Color("#98ebaa"),
            _titleFont,
            HorizontalAlignment.Left,
            wrap: false,
            outlineSize: 5));

        _haulSummaryRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#eef8f0"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2);
        _haulSummaryRtl.CustomMinimumSize = new Vector2(0f, 88f);
        plaqueBox.AddChild(_haulSummaryRtl);

        plaqueBox.AddChild(MakeDivider(new Color("#366c49", 0.55f)));

        plaqueBox.AddChild(ThingsMinigameUiTheme.MakeOutlinedRichText(
            $"[color=#d8eadc]{ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.rules", ("BumperGold", ForestPlinkoMinigame.BumperBonusGold))}[/color]",
            16,
            new Color("#d8eadc"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 4,
            shadowX: 2,
            shadowY: 2));

        plaqueBox.AddChild(MakeDivider(new Color("#366c49", 0.55f)));

        _feedbackRtl = ThingsMinigameUiTheme.MakeOutlinedRichText(
            string.Empty,
            17,
            new Color("#f4e2b0"),
            new Color("#030d06"),
            _bodyFont,
            outlineSize: 5,
            shadowX: 2,
            shadowY: 2);
        _feedbackRtl.CustomMinimumSize = new Vector2(0f, 52f);
        plaqueBox.AddChild(_feedbackRtl);

        // Claim & Continue Button
        _finishButton = ThingsMinigameUiTheme.PaintedActionButton.Create(
            string.Empty,
            new Vector2(504f, 76f),
            baseTint: new Color("#5b7c65"),
            activeTint: new Color("#6bbd85"),
            outlineColor: new Color(1.0f, 0.80f, 0.24f, 1.0f),
            font: _titleFont,
            fontSize: 22,
            textColor: new Color("#fff8d9"));
        _finishButton.Pressed += () => _minigame.ConfirmExit();
        rightCenter.AddChild(_finishButton);
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

    private static ColorRect MakeDivider(Color color) => new()
    {
        Color = color,
        CustomMinimumSize = new Vector2(0f, 2f),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private void RefreshStatus()
    {
        string dropsLine = ThingsMinigameUiTheme.Tr(
            "THINGS_FOREST_PLINKO.ui.dropsLabel",
            ("Remaining", $"[color=#ffd100]{_minigame.DropsRemaining}[/color]"),
            ("Max", _minigame.MaxDrops));
        string hpLine = ThingsMinigameUiTheme.Tr(
            "THINGS_FOREST_PLINKO.ui.hpLabel",
            ("Current", $"[color=#96e6a8]{_minigame.Owner.Creature.CurrentHp}[/color]"),
            ("Max", _minigame.Owner.Creature.MaxHp));
        _bottomStatusRtl.Text = $"{dropsLine}\n[font_size=20]{hpLine}[/font_size]";

        var lines = new List<string>();
        if (_minigame.CollectedRareRelics > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryRareRelic", ("Count", _minigame.CollectedRareRelics)));
        if (_minigame.CollectedCommonRelics > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryRelic", ("Count", _minigame.CollectedCommonRelics)));
        if (_minigame.CollectedUpgrades > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryUpgrade", ("Count", _minigame.CollectedUpgrades)));
        if (_minigame.CollectedGold > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryGold", ("Amount", _minigame.CollectedGold)));
        if (_minigame.CollectedHeal > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryHeal", ("Amount", _minigame.CollectedHeal)));
        if (_minigame.CollectedMaxHp > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryMaxHp", ("Amount", _minigame.CollectedMaxHp)));
        if (_minigame.CollectedPotions > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryPotion", ("Count", _minigame.CollectedPotions)));
        if (_minigame.CollectedCards > 0)
            lines.Add(ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryCard", ("Count", _minigame.CollectedCards)));

        _haulSummaryRtl.Text = lines.Count > 0
            ? $"[color=#eef9f0]{string.Join("\n", lines)}[/color]"
            : $"[color=#8cad94]{ThingsMinigameUiTheme.Tr("THINGS_FOREST_PLINKO.ui.summaryNone")}[/color]";

        string fb = ThingsMinigameUiTheme.Tr(_minigame.LastFeedbackKey, ("Value", _minigame.LastFeedbackValue));
        string fbColor = _minigame.IsComplete ? "#9de8a9" : "#f5e1a8";
        _feedbackRtl.Text = $"[color={fbColor}]{fb}[/color]";

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
}
