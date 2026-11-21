using System;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace STS2_Things.Events.Minigames;

/// <summary>
/// Shared Crystal-Sphere-inspired native STS2 visual components:
/// hand-painted beveled buttons (divine_button.png + additive outline),
/// octagonal nine-patch instruction plaques (tiny_nine_patch.png),
/// dramatic outlined floating RichText HUD counters, and native localized fonts.
/// </summary>
internal static partial class ThingsMinigameUiTheme
{
    public const string DivineButtonPath = "res://images/events/crystal_sphere/divine_button.png";
    public const string DivineButtonOutlinePath = "res://images/events/crystal_sphere/divine_button_outline.png";
    public const string TinyNinePatchPath = "res://images/ui/tiny_nine_patch.png";
    public const string AdditiveMatPath = "res://themes/canvas_item_material_additive_shared.tres";

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

    /// <summary>
    /// Creates a crisp non-distorting NinePatchRect glowing chamfered outline from vanilla divine_button_outline.png
    /// suitable for buttons OR square grid tiles of any aspect ratio.
    /// </summary>
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
    /// Creates an auto-sizing PanelContainer backed by vanilla Crystal-Sphere's octagonal tiny_nine_patch.png
    /// via StyleBoxTexture so wrapped zhs/eng copy never overflows or clips.
    /// </summary>
    public static PanelContainer CreateCrystalPlaquePanel(Color tint, int padH = 24, int padV = 20)
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
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>
    /// Creates a vanilla Crystal-Sphere-style octagonal translucent dark plaque using tiny_nine_patch.png.
    /// </summary>
    public static Control CreateCrystalPlaque(Color tint, Vector2 minSize, out NinePatchRect patch)
    {
        var host = new Control
        {
            CustomMinimumSize = minSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        patch = new NinePatchRect
        {
            Texture = TryLoadTexture(TinyNinePatchPath),
            RegionRect = new Rect2(0f, 0f, 48f, 48f),
            PatchMarginLeft = 14,
            PatchMarginTop = 14,
            PatchMarginRight = 13,
            PatchMarginBottom = 14,
            SelfModulate = tint,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        host.AddChild(patch);
        patch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return host;
    }

    /// <summary>
    /// Creates a floating outlined RichTextLabel matching vanilla CrystalSphere's DivinationsLeft / Instructions style.
    /// </summary>
    public static RichTextLabel MakeOutlinedRichText(
        string bbcode,
        int fontSize,
        Color textColor,
        Color outlineColor,
        Font? font,
        int outlineSize = 8,
        int shadowX = 4,
        int shadowY = 3,
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
        rtl.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.65f));
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
        label.AddThemeColorOverride("font_outline_color", outlineColor ?? new Color(0.02f, 0.04f, 0.05f, 0.92f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.72f));
        label.AddThemeConstantOverride("outline_size", outlineSize);
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        if (font != null)
            label.AddThemeFontOverride("font", font);
        return label;
    }

    /// <summary>
    /// Hand-painted beveled button styled directly after vanilla CrystalSphere's NDivinationButton
    /// (divine_button.png + additive golden divine_button_outline.png + shadowed gold text + optional icon).
    /// </summary>
    public sealed partial class PaintedActionButton : Control
    {
        private NinePatchRect _bg = null!;
        private NinePatchRect _outline = null!;
        private TextureRect? _iconRect;
        private Label _label = null!;
        private Color _baseTint = Colors.White;
        private Color _activeTint = Colors.White;
        private Color _outlineColor = new(1f, 0.72f, 0.14f, 1f);
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
                Modulate = new Color(0f, 0f, 0f, 0.48f),
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
            btn._outline.OffsetLeft = -6f;
            btn._outline.OffsetTop = -6f;
            btn._outline.OffsetRight = 6f;
            btn._outline.OffsetBottom = 6f;

            float textLeft = 18f;
            if (icon != null)
            {
                float iconDim = Math.Min(42f, size.Y - 22f);
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
                btn._iconRect.OffsetLeft = 18f;
                btn._iconRect.OffsetRight = 18f + iconDim;
                btn._iconRect.OffsetTop = -iconDim * 0.5f;
                btn._iconRect.OffsetBottom = iconDim * 0.5f;
                textLeft = 18f + iconDim + 10f;
            }

            btn._label = MakeLabel(
                text,
                fontSize,
                textColor ?? new Color("#f2cf5e"),
                font,
                icon != null ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                wrap: false,
                outlineSize: 5);
            btn.AddChild(btn._label);
            btn._label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn._label.OffsetLeft = textLeft;
            btn._label.OffsetRight = -16f;

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
                _bg.SelfModulate = _baseTint.Darkened(0.48f);
                _outline.Visible = false;
                _label.Modulate = new Color(0.60f, 0.64f, 0.62f, 0.78f);
                if (_iconRect != null)
                    _iconRect.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.65f);
                return;
            }

            Color tint = _isActive ? _activeTint : _baseTint;
            if (_isHovered)
                tint = tint.Lightened(0.16f);
            _bg.SelfModulate = tint;

            _outline.Visible = _isActive || _isHovered;
            _outline.Modulate = _isActive ? _outlineColor : new Color(_outlineColor.R, _outlineColor.G, _outlineColor.B, 0.62f);
            _label.Modulate = _isActive || _isHovered ? Colors.White : new Color(0.96f, 0.94f, 0.88f, 1f);
            if (_iconRect != null)
                _iconRect.Modulate = Colors.White;
        }
    }
}
