using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;

namespace STS2_Things.Visuals;

/// <summary>
/// Full-screen presentation-only ink-drench VFX overlay for 渊墨巨蛸's 【高压渊墨扫射】 and
/// Turn-5 whole-deck blackout ambience. Mounts inside the active <see cref="NCombatRoom"/>
/// with <c>MouseFilter.Ignore</c> so it never blocks input or affects combat determinism.
/// </summary>
public partial class NAbyssalInkSprayOverlay : CanvasLayer
{
    private const string NodeName = "AbyssalInkSprayOverlay";
    private const string ShaderPath = "res://shaders/vfx/abyssal_ink_screen_drench.gdshader";
    private const string SplatterTexturePath = "res://images/vfx/abyssal_ink_screen_splatter.png";

    private static Shader? _cachedShader;
    private static Texture2D? _cachedSplatter;
    private static NAbyssalInkSprayOverlay? _activeInstance;

    private TextureRect? _rect;
    private ShaderMaterial? _material;
    private Tween? _animTween;
    private bool _persistentBorderActive;

    public static void PlaySweepDrench()
    {
        if (GetOrCreate() is { } overlay)
        {
            overlay.TriggerSweepDrenchInternal();
        }
    }

    public static void SetPersistentInkBorder(bool active, bool washClean = false)
    {
        if (!active && (_activeInstance == null || !GodotObject.IsInstanceValid(_activeInstance)))
            return;

        if (GetOrCreate() is { } overlay)
        {
            overlay.SetPersistentBorderInternal(active, washClean);
        }
    }

    private static NAbyssalInkSprayOverlay? GetOrCreate()
    {
        if (_activeInstance != null && GodotObject.IsInstanceValid(_activeInstance) && _activeInstance.IsInsideTree())
            return _activeInstance;

        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
            return null;

        if (room.GetNodeOrNull<NAbyssalInkSprayOverlay>(NodeName) is { } existing &&
            GodotObject.IsInstanceValid(existing))
        {
            _activeInstance = existing;
            return existing;
        }

        var overlay = new NAbyssalInkSprayOverlay
        {
            Name = NodeName,
            Layer = 48,
        };
        room.AddChild(overlay);
        _activeInstance = overlay;
        return overlay;
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        CombatManager.Instance.CombatEnded += OnCombatEnded;
    }

    public override void _ExitTree()
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        KillTween();
        if (_activeInstance == this)
            _activeInstance = null;
        base._ExitTree();
    }

    public override void _Ready()
    {
        _cachedShader ??= GD.Load<Shader>(ShaderPath);
        _cachedSplatter ??= GD.Load<Texture2D>(SplatterTexturePath);

        _material = new ShaderMaterial
        {
            Shader = _cachedShader,
            ResourceLocalToScene = true,
        };
        ResetShaderUniforms();

        _rect = new TextureRect
        {
            Name = "InkScreenDrenchRect",
            Texture = _cachedSplatter,
            Material = _material,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    private void ResetShaderUniforms()
    {
        if (_material == null)
            return;

        _material.SetShaderParameter("sweep_progress", -0.20f);
        _material.SetShaderParameter("curtain_strength", 0.0f);
        _material.SetShaderParameter("border_strength", 0.0f);
        _material.SetShaderParameter("drip_offset", 0.0f);
        _material.SetShaderParameter("wash_progress", 0.0f);
    }

    private void TriggerSweepDrenchInternal()
    {
        if (_rect == null || _material == null)
            return;

        KillTween();
        _rect.Visible = true;
        _material.SetShaderParameter("wash_progress", 0.0f);
        _material.SetShaderParameter("sweep_progress", -0.18f);
        _material.SetShaderParameter("curtain_strength", 0.0f);
        _material.SetShaderParameter("border_strength", 0.0f);
        _material.SetShaderParameter("drip_offset", 0.0f);

        _animTween = CreateTween();
        // 0.00s - 0.52s: wait for Boss wind-up before hydro-ink cannon ignites (`ink_fire_start@0.56s`)
        _animTween.TweenInterval(0.52f);

        // 0.52s - 1.48s: sweeping left-to-right viscous ink wall + heavy hand-painted splatters slam screen!
        _animTween.TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("sweep_progress", v)), -0.18f, 1.28f, 0.96f)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);
        _animTween.Parallel().TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("curtain_strength", v)), 0.0f, 0.95f, 0.34f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _animTween.Parallel().TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("border_strength", v)), 0.0f, 0.98f, 0.38f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        // 1.48s - 2.65s: viscous gravity drip-down; curtain clears while subtle corner splatter settles!
        _animTween.TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("drip_offset", v)), 0.0f, 0.14f, 1.15f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        _animTween.Parallel().TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("curtain_strength", v)), 0.95f, 0.0f, 0.85f)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        _animTween.Parallel().TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("border_strength", v)), 0.98f, _persistentBorderActive ? 0.52f : 0.30f, 1.05f)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);

        _animTween.TweenCallback(Callable.From(() =>
        {
            if (!_persistentBorderActive && _rect != null && GodotObject.IsInstanceValid(_rect))
            {
                // Keep soft splatter until Player Turn 5 begins (which calls SetPersistentInkBorder(true)),
                // or fade gently if Boss died / combat ended.
                if (!CombatManager.Instance.IsInProgress)
                {
                    _rect.Visible = false;
                }
            }
        }));
    }

    private void SetPersistentBorderInternal(bool active, bool washClean)
    {
        _persistentBorderActive = active;
        if (_rect == null || _material == null)
            return;

        KillTween();

        if (active)
        {
            _rect.Visible = true;
            _material.SetShaderParameter("sweep_progress", 1.35f);
            _material.SetShaderParameter("curtain_strength", 0.0f);
            _material.SetShaderParameter("wash_progress", 0.0f);
            _material.SetShaderParameter("drip_offset", 0.04f);

            _animTween = CreateTween();
            _animTween.TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("border_strength", v)), 0.0f, 0.40f, 0.45f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            return;
        }

        if (!_rect.Visible)
            return;

        _animTween = CreateTween();
        if (washClean)
        {
            _material.SetShaderParameter("wash_progress", 0.0f);
            _animTween.TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("wash_progress", v)), 0.0f, 1.0f, 0.62f)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.InOut);
        }
        else
        {
            float currentBorder = _material.GetShaderParameter("border_strength").AsSingle();
            _animTween.TweenMethod(Callable.From<float>(v => _material?.SetShaderParameter("border_strength", v)), currentBorder, 0.0f, 0.40f)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }

        _animTween.TweenCallback(Callable.From(() =>
        {
            if (!_persistentBorderActive && _rect != null && GodotObject.IsInstanceValid(_rect))
            {
                _rect.Visible = false;
                ResetShaderUniforms();
            }
        }));
    }

    private void OnCombatEnded(CombatRoom _)
    {
        _persistentBorderActive = false;
        KillTween();
        if (_rect != null && GodotObject.IsInstanceValid(_rect))
        {
            _rect.Visible = false;
            ResetShaderUniforms();
        }
    }

    private void KillTween()
    {
        if (_animTween != null && GodotObject.IsInstanceValid(_animTween))
        {
            _animTween.Kill();
        }
        _animTween = null;
    }
}
