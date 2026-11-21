using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Shared native STS2 visual components modeled directly after vanilla CrystalSphereScreen
/// and NProceedButton:
/// - Authentic hand-painted Proceed button (proceed_button.tres + proceed_button_outline.tres)
/// - Narrow Crystal-Sphere octagonal instructions plaque (tiny_nine_patch.png, cream body text, no web dividers)
/// - Restrained hand-painted tool buttons (divine_button.png + subtle warm-gold outline)
/// - Compact non-zero-only loot icon strip
/// - In-world floating feedback popups (replaces console log panels)
/// - Safe native NHoverTipSet wrapper for stone/bowl/cell tooltips
/// </summary>
internal static partial class ThingsMinigameUiTheme
{
    public const string DivineButtonPath = "res://images/events/crystal_sphere/divine_button.png";
    public const string DivineButtonOutlinePath = "res://images/events/crystal_sphere/divine_button_outline.png";
    public const string ProceedButtonPath = "res://images/atlases/ui_atlas.sprites/proceed_button.tres";
    public const string ProceedButtonOutlinePath = "res://images/atlases/compressed.sprites/proceed_button_outline.tres";
    public const string TinyNinePatchPath = "res://images/ui/tiny_nine_patch.png";

    public static readonly Color VanillaCream = new(1f, 0.9647f, 0.8863f, 1f);
    public static readonly Color VanillaGold = new(0.9961f, 0.8196f, 0f, 1f);
    public static readonly Color VanillaWarmOutline = new(0.92f, 0.66f, 0.14f, 0.82f);

    public static Font? GetTitleFont()
    {
        string lang = LocManager.Instance?.Language ?? "eng";
        string primary = lang switch
        {
            "zhs" => "res://fonts/zhs/SourceHanSerifSC-Bold.otf",
            "zht" => "res://fonts/zht/SourceHanSerifTC-Bold.otf",
            "jpn" => "res://fonts/jpn/NotoSansCJKjp-Bold.otf",
            "kor" => "res://fonts/kor/GyeonggiCheonnyeonBatangBold.ttf",
            "rus" => "res://fonts/rus/FiraSansExtraCondensed-Bold.ttf",
            "tha" => "res://fonts/tha/CSChatThaiUI.ttf",
            _ => "res://fonts/spectral_bold.ttf",
        };
        return TryLoadFont(primary) ?? TryLoadFont("res://fonts/spectral_bold.ttf");
    }

    public static Font? GetBodyFont()
    {
        string lang = LocManager.Instance?.Language ?? "eng";
        string primary = lang switch
        {
            "zhs" => "res://fonts/zhs/SourceHanSerifSC-Medium.otf",
            "zht" => "res://fonts/zht/SourceHanSerifTC-Medium.otf",
            "jpn" => "res://fonts/jpn/NotoSansCJKjp-Medium.otf",
            "kor" => "res://fonts/kor/GyeonggiCheonnyeonBatangBold.ttf",
            "rus" => "res://fonts/rus/FiraSansExtraCondensed-Regular.ttf",
            "tha" => "res://fonts/tha/CSChatThaiUI.ttf",
            _ => "res://fonts/kreon_regular.ttf",
        };
        return TryLoadFont(primary) ?? TryLoadFont("res://fonts/kreon_regular.ttf");
    }

    public static Font? GetNumberFont()
    {
        return TryLoadFont("res://fonts/kreon_bold.ttf")
            ?? TryLoadFont("res://fonts/spectral_bold.ttf")
            ?? GetTitleFont();
    }

    public static Font? TryLoadFont(string resPath)
    {
        if (!ResourceLoader.Exists(resPath))
            return null;
        return ResourceLoader.Load<Font>(resPath, null, ResourceLoader.CacheMode.Reuse);
    }

    public static Texture2D? TryLoadTexture(string resPath)
    {
        if (string.IsNullOrWhiteSpace(resPath) || !ResourceLoader.Exists(resPath))
            return null;
        return ResourceLoader.Load<Texture2D>(resPath, null, ResourceLoader.CacheMode.Reuse);
    }

    public static Shader? TryLoadShader(string resPath)
    {
        if (string.IsNullOrWhiteSpace(resPath) || !ResourceLoader.Exists(resPath))
            return null;
        return ResourceLoader.Load<Shader>(resPath, null, ResourceLoader.CacheMode.Reuse);
    }

    public static CanvasItemMaterial CreateAdditiveMaterial()
    {
        return new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public static string Tr(string key, params (string Name, object Value)[] vars)
    {
        var loc = new LocString("events", key);
        foreach (var (name, value) in vars)
        {
            switch (value)
            {
                case int i:
                    loc.Add(name, i);
                    break;
                case decimal d:
                    loc.Add(name, d);
                    break;
                case bool b:
                    loc.Add(name, b);
                    break;
                default:
                    loc.Add(name, value?.ToString() ?? string.Empty);
                    break;
            }
        }
        return loc.GetFormattedText();
    }

    public static void ShowHoverTip(Control owner, IHoverTip tip, HoverTipAlignment alignment = HoverTipAlignment.Right)
    {
        try
        {
            if (NGame.Instance == null || !owner.IsInsideTree())
                return;
            NHoverTipSet.Remove(owner);
            NHoverTipSet.CreateAndShow(owner, tip, alignment);
        }
        catch (Exception)
        {
            // Never disrupt minigame interaction if HoverTip layer is unavailable in headless probes.
        }
    }

    public static void ShowHoverTips(Control owner, IEnumerable<IHoverTip> tips, HoverTipAlignment alignment = HoverTipAlignment.Right)
    {
        try
        {
            if (NGame.Instance == null || !owner.IsInsideTree())
                return;
            NHoverTipSet.Remove(owner);
            NHoverTipSet.CreateAndShow(owner, tips, alignment);
        }
        catch (Exception)
        {
            // Safe headless fallback.
        }
    }

    public static void ShowLocalizedHoverTip(Control owner, string titleKey, string description, Texture2D? icon = null, HoverTipAlignment alignment = HoverTipAlignment.Right)
    {
        if (string.IsNullOrWhiteSpace(titleKey))
            return;
        ShowHoverTip(owner, new HoverTip(new LocString("events", titleKey), description, icon), alignment);
    }

    public static void ShowEnchantmentHoverTip(Control owner, EnchantmentModel enchantment, int amount = 1, HoverTipAlignment alignment = HoverTipAlignment.Right)
    {
        try
        {
            if (NGame.Instance == null || !owner.IsInsideTree())
                return;
            EnchantmentModel mutable = enchantment.ToMutable();
            mutable.Amount = amount;
            mutable.RecalculateValues();
            NHoverTipSet.Remove(owner);
            NHoverTipSet.CreateAndShow(owner, mutable.HoverTips, alignment);
        }
        catch (Exception)
        {
            // Safe headless fallback.
        }
    }

    public static void HideHoverTip(Control owner)
    {
        try
        {
            if (NGame.Instance == null)
                return;
            NHoverTipSet.Remove(owner);
        }
        catch (Exception)
        {
            // Safe headless fallback.
        }
    }

    public static NinePatchRect CreateChamferOutline(Color modulate, bool visible = false)
    {
        return new NinePatchRect
        {
            Texture = TryLoadTexture(DivineButtonOutlinePath),
            RegionRect = new Rect2(3f, 6f, 389f, 142f),
            PatchMarginLeft = 28,
            PatchMarginTop = 28,
            PatchMarginRight = 28,
            PatchMarginBottom = 28,
            Material = CreateAdditiveMaterial(),
            Modulate = modulate,
            Visible = visible,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>
    /// Creates an authentic narrow vanilla Crystal-Sphere octagonal plaque (tiny_nine_patch.png)
    /// with NO 1px web dividers and clean breathing padding.
    /// </summary>
    public static PanelContainer CreateCrystalPlaquePanel(Color tint, int padH = 26, int padV = 22)
    {
        var style = new StyleBoxTexture
        {
            Texture = TryLoadTexture(TinyNinePatchPath),
            RegionRect = new Rect2(0f, 0f, 48f, 48f),
            TextureMarginLeft = 14f,
            TextureMarginTop = 14f,
            TextureMarginRight = 13f,
            TextureMarginBottom = 14f,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
            ModulateColor = tint,
        };
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>
    /// Creates vanilla CrystalSphere-style gold plaque header (soft drop shadow, ZERO heavy black outline).
    /// </summary>
    public static Label MakeVanillaPlaqueHeader(
        string text,
        Font? font,
        int fontSize = 23,
        Color? color = null,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? VanillaGold);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.50f));
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        if (font != null)
            label.AddThemeFontOverride("font", font);
        return label;
    }

    /// <summary>
    /// Creates vanilla CrystalSphere-style clean cream instructions text (soft drop shadow, ZERO heavy black outline).
    /// </summary>
    public static RichTextLabel MakeVanillaPlaqueBody(
        string bbcode,
        Font? font,
        int fontSize = 19,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var rtl = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            ClipContents = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = bbcode,
        };
        rtl.AddThemeColorOverride("default_color", VanillaCream);
        rtl.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.52f));
        rtl.AddThemeConstantOverride("outline_size", 0);
        rtl.AddThemeConstantOverride("shadow_offset_x", 3);
        rtl.AddThemeConstantOverride("shadow_offset_y", 3);
        rtl.AddThemeFontSizeOverride("normal_font_size", fontSize);
        rtl.AddThemeFontSizeOverride("bold_font_size", fontSize);
        if (font != null)
        {
            rtl.AddThemeFontOverride("normal_font", font);
            rtl.AddThemeFontOverride("bold_font", font);
        }
        return rtl;
    }

    /// <summary>
    /// Creates a floating outlined RichTextLabel matching vanilla CrystalSphere's DivinationsLeft style.
    /// </summary>
    public static RichTextLabel MakeOutlinedRichText(
        string bbcode,
        int fontSize,
        Color textColor,
        Color outlineColor,
        Font? font,
        int outlineSize = 10,
        int shadowX = 5,
        int shadowY = 4,
        HorizontalAlignment align = HorizontalAlignment.Left,
        bool wrap = true)
    {
        var rtl = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            ClipContents = false,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = bbcode,
        };
        rtl.AddThemeColorOverride("default_color", textColor);
        rtl.AddThemeColorOverride("font_outline_color", outlineColor);
        rtl.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.54f));
        rtl.AddThemeConstantOverride("outline_size", outlineSize);
        rtl.AddThemeConstantOverride("shadow_offset_x", shadowX);
        rtl.AddThemeConstantOverride("shadow_offset_y", shadowY);
        rtl.AddThemeFontSizeOverride("normal_font_size", fontSize);
        rtl.AddThemeFontSizeOverride("bold_font_size", fontSize);
        if (font != null)
        {
            rtl.AddThemeFontOverride("normal_font", font);
            rtl.AddThemeFontOverride("bold_font", font);
        }
        return rtl;
    }

    public static Label MakeLabel(
        string text,
        int fontSize,
        Color color,
        Font? font,
        HorizontalAlignment align = HorizontalAlignment.Left,
        bool wrap = true,
        int outlineSize = 4,
        Color? outlineColor = null)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", outlineColor ?? new Color(0.02f, 0.04f, 0.05f, 0.90f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.65f));
        label.AddThemeConstantOverride("outline_size", outlineSize);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        if (font != null)
            label.AddThemeFontOverride("font", font);
        return label;
    }

    /// <summary>
    /// Compact non-zero-only loot display strip (shows real vanilla icons + counts ONLY for items earned > 0).
    /// Eliminates "Potions: 0 / Relics: 0" SaaS dashboard clutter.
    /// </summary>
    public sealed partial class CompactLootStrip : HFlowContainer
    {
        private Font? _numberFont;

        public static CompactLootStrip Create(Font? numberFont)
        {
            var strip = new CompactLootStrip
            {
                _numberFont = numberFont,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            strip.AddThemeConstantOverride("h_separation", 14);
            strip.AddThemeConstantOverride("v_separation", 8);
            return strip;
        }

        public void SetEntries(params (Texture2D? Icon, string Text, Color Color)[] entries)
        {
            foreach (Node child in GetChildren())
                child.QueueFree();

            int shown = 0;
            foreach (var (icon, text, color) in entries)
            {
                if (string.IsNullOrEmpty(text))
                    continue;
                shown++;
                var chip = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                chip.AddThemeConstantOverride("separation", 6);
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
                    chip.AddChild(tex);
                }
                Label lbl = MakeLabel(text, 20, color, _numberFont, HorizontalAlignment.Left, wrap: false, outlineSize: 6);
                chip.AddChild(lbl);
                AddChild(chip);
            }
            Visible = shown > 0;
        }
    }

    /// <summary>
    /// Lightweight in-world floating pop text layer (replaces right-panel console status logs).
    /// </summary>
    public sealed partial class FloatingPopLayer : Control
    {
        private Font? _font;

        public static FloatingPopLayer Create(Font? font)
        {
            var layer = new FloatingPopLayer
            {
                Name = "FloatingPopLayer",
                _font = font,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            return layer;
        }

        public void Pop(Vector2 pos, string text, Color color, int fontSize = 24)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsInsideTree())
                return;

            Label lbl = MakeLabel(
                text,
                fontSize,
                color,
                _font,
                HorizontalAlignment.Center,
                wrap: false,
                outlineSize: 7,
                outlineColor: new Color(0.02f, 0.04f, 0.06f, 0.94f));
            lbl.CustomMinimumSize = new Vector2(240f, 36f);
            lbl.Position = pos - new Vector2(120f, 18f);
            AddChild(lbl);

            Tween tw = lbl.CreateTween().SetParallel();
            tw.TweenProperty(lbl, "position:y", lbl.Position.Y - 42f, 1.05f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tw.TweenProperty(lbl, "modulate:a", 0f, 0.45f)
                .SetDelay(0.60f)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
            tw.Chain().TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(lbl))
                    lbl.QueueFree();
            }));
        }
    }

    /// <summary>
    /// Authentic bottom-right vanilla STS2 Proceed button backed directly by
    /// <c>proceed_button.tres</c> and <c>proceed_button_outline.tres</c> (matching proceed_button.tscn).
    /// Safe in both live gameplay and headless probes without requiring NGame.Instance.
    /// </summary>
    public sealed partial class NativeProceedButton : Control
    {
        private TextureRect _image = null!;
        private TextureRect _shadow = null!;
        private TextureRect _outline = null!;
        private Label _label = null!;
        private Font? _font;
        private int _maxFontSize = 28;
        private bool _isDisabled;
        private bool _isActive;
        private bool _isHovered;
        private Tween? _scaleTween;
        private Tween? _pulseTween;

        public event Action? Pressed;

        public string Text
        {
            get => _label.Text;
            set
            {
                _label.Text = value;
                FitLabelFontSize(value);
            }
        }

        public bool Disabled
        {
            get => _isDisabled;
            set
            {
                if (_isDisabled == value)
                    return;
                _isDisabled = value;
                MouseDefaultCursorShape = value ? CursorShape.Arrow : CursorShape.PointingHand;
                RefreshVisual();
            }
        }

        public bool Active
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                RefreshVisual();
            }
        }

        public static NativeProceedButton Create(string text, Font? font, int fontSize = 28)
        {
            var btn = new NativeProceedButton
            {
                Name = "ProceedButton",
                _font = font,
                _maxFontSize = fontSize,
                CustomMinimumSize = new Vector2(269f, 108f),
                PivotOffset = new Vector2(134.5f, 54f),
                MouseFilter = MouseFilterEnum.Stop,
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
            };

            Texture2D? mainTex = TryLoadTexture(ProceedButtonPath) ?? TryLoadTexture(DivineButtonPath);
            Texture2D? outlineTex = TryLoadTexture(ProceedButtonOutlinePath);

            btn._image = new TextureRect
            {
                Texture = mainTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn.AddChild(btn._image);
            btn._image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._image.OffsetLeft = -11f;
            btn._image.OffsetTop = -22f;
            btn._image.OffsetRight = 16f;
            btn._image.OffsetBottom = 25f;

            btn._shadow = new TextureRect
            {
                Texture = mainTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = new Color(0f, 0f, 0f, 0.28f),
                ShowBehindParent = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn._image.AddChild(btn._shadow);
            btn._shadow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._shadow.OffsetLeft = 10f;
            btn._shadow.OffsetTop = 10f;
            btn._shadow.OffsetRight = 10f;
            btn._shadow.OffsetBottom = 10f;

            btn._outline = new TextureRect
            {
                Texture = outlineTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Material = CreateAdditiveMaterial(),
                SelfModulate = new Color(1f, 0.80f, 0f, 0f),
                ShowBehindParent = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn._image.AddChild(btn._outline);
            btn._outline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            btn._label = MakeLabel(
                text,
                fontSize,
                VanillaCream,
                font,
                HorizontalAlignment.Center,
                wrap: false,
                outlineSize: 10,
                outlineColor: new Color(0.34f, 0.06f, 0.03f, 1f));
            btn._image.AddChild(btn._label);
            btn._label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._label.OffsetLeft = 48f;
            btn._label.OffsetRight = -30f;
            btn._label.OffsetTop = 10f;
            btn._label.OffsetBottom = -10f;
            btn.FitLabelFontSize(text);

            btn.MouseEntered += () =>
            {
                btn._isHovered = true;
                btn.RefreshVisual();
                if (!btn._isDisabled)
                    btn.AnimateScale(1.05f);
            };
            btn.MouseExited += () =>
            {
                btn._isHovered = false;
                btn.RefreshVisual();
                btn.AnimateScale(1.0f);
            };
            btn.Resized += () => btn.PivotOffset = btn.Size * 0.5f;

            btn.RefreshVisual();
            return btn;
        }

        public override void _Ready()
        {
            RefreshVisual();
        }

        private void FitLabelFontSize(string text)
        {
            if (_label == null)
                return;
            int chosen = _maxFontSize;
            if (_font != null && !string.IsNullOrEmpty(text))
            {
                while (chosen > 17 && _font.GetStringSize(text, HorizontalAlignment.Left, -1f, chosen).X > 186f)
                    chosen -= 2;
            }
            _label.AddThemeFontSizeOverride("font_size", chosen);
        }

        public void AnchorBottomRight(float rightInset = 54f, float bottomInset = 54f)
        {
            AnchorLeft = 1f;
            AnchorRight = 1f;
            AnchorTop = 1f;
            AnchorBottom = 1f;
            OffsetLeft = -(rightInset + 269f);
            OffsetRight = -rightInset;
            OffsetTop = -(bottomInset + 108f);
            OffsetBottom = -bottomInset;
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (_isDisabled)
                return;
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    AnimateScale(0.95f);
                    Pressed?.Invoke();
                    AcceptEvent();
                }
                else
                {
                    AnimateScale(_isHovered ? 1.05f : 1.0f);
                }
            }
        }

        private void AnimateScale(float target)
        {
            if (!IsInsideTree())
                return;
            _scaleTween?.Kill();
            _scaleTween = CreateTween();
            _scaleTween.TweenProperty(this, "scale", new Vector2(target, target), 0.12f)
                .SetTrans(Tween.TransitionType.Expo)
                .SetEase(Tween.EaseType.Out);
        }

        private void RefreshVisual()
        {
            _pulseTween?.Kill();
            if (_isDisabled)
            {
                _image.SelfModulate = new Color(0.55f, 0.55f, 0.55f, 0.85f);
                _outline.SelfModulate = new Color(1f, 0.80f, 0f, 0f);
                _label.Modulate = new Color(0.75f, 0.72f, 0.68f, 0.72f);
                return;
            }

            _image.SelfModulate = _isHovered ? new Color(1.12f, 1.08f, 1.04f, 1f) : Colors.White;
            _label.Modulate = Colors.White;

            if (_isHovered)
            {
                _outline.SelfModulate = new Color(1f, 0.80f, 0f, 0.92f);
            }
            else if (_isActive)
            {
                _outline.SelfModulate = new Color(1f, 0.80f, 0f, 0.55f);
                if (IsInsideTree())
                {
                    _pulseTween = CreateTween().SetLoops();
                    _pulseTween.TweenProperty(_outline, "self_modulate:a", 0.22f, 0.55f);
                    _pulseTween.TweenProperty(_outline, "self_modulate:a", 0.72f, 0.55f);
                }
            }
            else
            {
                _outline.SelfModulate = new Color(1f, 0.80f, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Hand-painted tool button styled after vanilla CrystalSphere's NDivinationButton
    /// (divine_button.png + restrained warm outline + shadowed gold text + optional icon).
    /// Reserved for genuine action/mode switches rather than stretched footer bars.
    /// </summary>
    public sealed partial class PaintedActionButton : Control
    {
        private NinePatchRect _bg = null!;
        private NinePatchRect _outline = null!;
        private TextureRect? _iconRect;
        private Label _label = null!;
        private Color _baseTint = Colors.White;
        private Color _activeTint = Colors.White;
        private Color _outlineColor = VanillaWarmOutline;
        private bool _isActive;
        private bool _isDisabled;
        private bool _isHovered;
        private Tween? _scaleTween;

        public event Action? Pressed;

        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }

        public bool Disabled
        {
            get => _isDisabled;
            set
            {
                _isDisabled = value;
                MouseDefaultCursorShape = value ? CursorShape.Arrow : CursorShape.PointingHand;
                RefreshVisual();
            }
        }

        public bool Active
        {
            get => _isActive;
            set
            {
                _isActive = value;
                RefreshVisual();
            }
        }

        public static PaintedActionButton Create(
            string text,
            Vector2 size,
            Color baseTint,
            Color activeTint,
            Color outlineColor,
            Font? font,
            int fontSize = 22,
            Texture2D? icon = null,
            Color? textColor = null)
        {
            var btn = new PaintedActionButton
            {
                CustomMinimumSize = size,
                PivotOffset = size * 0.5f,
                MouseFilter = MouseFilterEnum.Stop,
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                _baseTint = baseTint,
                _activeTint = activeTint,
                _outlineColor = outlineColor,
            };

            Texture2D? divineTex = TryLoadTexture(DivineButtonPath);
            var shadow = new NinePatchRect
            {
                Texture = divineTex,
                RegionRect = new Rect2(12f, 13f, 372f, 128f),
                PatchMarginLeft = 28,
                PatchMarginTop = 26,
                PatchMarginRight = 28,
                PatchMarginBottom = 26,
                Modulate = new Color(0f, 0f, 0f, 0.42f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn.AddChild(shadow);
            shadow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            shadow.OffsetLeft = 5f;
            shadow.OffsetTop = 6f;
            shadow.OffsetRight = 5f;
            shadow.OffsetBottom = 6f;

            btn._bg = new NinePatchRect
            {
                Texture = divineTex,
                RegionRect = new Rect2(12f, 13f, 372f, 128f),
                PatchMarginLeft = 28,
                PatchMarginTop = 26,
                PatchMarginRight = 28,
                PatchMarginBottom = 26,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            btn.AddChild(btn._bg);
            btn._bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            btn._outline = CreateChamferOutline(outlineColor, visible: false);
            btn.AddChild(btn._outline);
            btn._outline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._outline.OffsetLeft = -5f;
            btn._outline.OffsetTop = -5f;
            btn._outline.OffsetRight = 5f;
            btn._outline.OffsetBottom = 5f;

            float textLeft = 18f;
            if (icon != null)
            {
                float iconDim = Math.Min(48f, size.Y - 24f);
                btn._iconRect = new TextureRect
                {
                    Texture = icon,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                btn.AddChild(btn._iconRect);
                btn._iconRect.AnchorTop = 0.5f;
                btn._iconRect.AnchorBottom = 0.5f;
                btn._iconRect.OffsetLeft = 26f;
                btn._iconRect.OffsetRight = 26f + iconDim;
                btn._iconRect.OffsetTop = -iconDim * 0.5f;
                btn._iconRect.OffsetBottom = iconDim * 0.5f;
                textLeft = 26f + iconDim + 14f;
            }

            btn._label = MakeLabel(
                text,
                fontSize,
                textColor ?? new Color(0.937f, 0.784f, 0.318f, 1f),
                font,
                icon != null ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                wrap: false,
                outlineSize: 0);
            btn._label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.42f));
            btn._label.AddThemeConstantOverride("shadow_offset_x", 3);
            btn._label.AddThemeConstantOverride("shadow_offset_y", 2);
            btn.AddChild(btn._label);
            btn._label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._label.OffsetLeft = textLeft;
            btn._label.OffsetRight = -18f;

            btn.MouseEntered += () =>
            {
                btn._isHovered = true;
                btn.RefreshVisual();
                if (!btn._isDisabled)
                    btn.AnimateScale(1.035f);
            };
            btn.MouseExited += () =>
            {
                btn._isHovered = false;
                btn.RefreshVisual();
                btn.AnimateScale(1.0f);
            };
            btn.Resized += () => btn.PivotOffset = btn.Size * 0.5f;

            btn.RefreshVisual();
            return btn;
        }

        public void SetColors(Color baseTint, Color activeTint, Color outlineColor)
        {
            _baseTint = baseTint;
            _activeTint = activeTint;
            _outlineColor = outlineColor;
            RefreshVisual();
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (_isDisabled)
                return;
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    AnimateScale(0.97f);
                    Pressed?.Invoke();
                    AcceptEvent();
                }
                else
                {
                    AnimateScale(_isHovered ? 1.035f : 1.0f);
                }
            }
        }

        private void AnimateScale(float target)
        {
            if (!IsInsideTree())
                return;
            _scaleTween?.Kill();
            _scaleTween = CreateTween();
            _scaleTween.TweenProperty(this, "scale", new Vector2(target, target), 0.11f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
        }

        private void RefreshVisual()
        {
            if (_isDisabled)
            {
                _bg.SelfModulate = _baseTint.Darkened(0.45f);
                _outline.Visible = false;
                _label.Modulate = new Color(0.65f, 0.66f, 0.64f, 0.72f);
                if (_iconRect != null)
                    _iconRect.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.62f);
                return;
            }

            Color tint = _isActive ? _activeTint : _baseTint;
            if (_isHovered)
                tint = tint.Lightened(0.14f);
            _bg.SelfModulate = tint;

            _outline.Visible = _isActive || _isHovered;
            _outline.Modulate = _isActive
                ? new Color(_outlineColor.R, _outlineColor.G, _outlineColor.B, 0.76f)
                : new Color(_outlineColor.R, _outlineColor.G, _outlineColor.B, 0.45f);
            _label.Modulate = _isActive || _isHovered ? Colors.White : new Color(0.94f, 0.90f, 0.80f, 1f);
            if (_iconRect != null)
                _iconRect.Modulate = Colors.White;
        }
    }
}
