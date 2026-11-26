using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2_Things.Visuals;

/// <summary>
/// Dynamically scales and centers custom STS2_Things combat backgrounds to cover the live viewport
/// with a tight 1.015x overscan safety margin across 16:9, 16:10, 21:9, and 4:3 displays, eliminating
/// excessive 1.25x-1.44x zoom-in/cropping while compensating BgContainer scale and clamping camera shake
/// inside the available overscan margin so edges never expose black bars.
/// </summary>
[GlobalClass]
public partial class NThingsBackgroundViewportFitter : Node
{
    public const float OverscanMargin = 1.015f;

    private Vector2 _nominalSize = new(2764.8f, 1296.0f);
    private Vector2? _initialScenePos;
    private Viewport? _connectedViewport;
    private Window? _connectedRootWindow;
    private Control? _bgControl;
    private CanvasItem? _bgParent;
    private Control? _sceneContainer;
    private bool _isLiveBgContainer;
    private bool _hasLastState;
    private Vector2 _lastViewportSize;
    private Transform2D _lastParentGlobal;
    private Vector2 _lastScenePos;
    private Vector2 _lastNominalSize;

    public void Configure(Vector2 nominalSize)
    {
        if (nominalSize.X > 1f && nominalSize.Y > 1f && nominalSize != _nominalSize)
        {
            _nominalSize = nominalSize;
            _hasLastState = false;
        }
    }

    public override void _Ready()
    {
        Viewport? viewport = GetViewport();
        if (viewport != null)
        {
            _connectedViewport = viewport;
            viewport.SizeChanged += OnViewportSizeChanged;
        }

        Window? rootWindow = GetTree()?.Root;
        if (rootWindow != null && !ReferenceEquals(rootWindow, viewport))
        {
            _connectedRootWindow = rootWindow;
            rootWindow.SizeChanged += OnViewportSizeChanged;
        }

        // Sync immediately to prevent a 1-frame oversized pop on combat entry, then re-sync after layout settles.
        SyncTransform();
        Callable.From(SyncTransform).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_connectedViewport != null && GodotObject.IsInstanceValid(_connectedViewport))
            _connectedViewport.SizeChanged -= OnViewportSizeChanged;
        if (_connectedRootWindow != null && GodotObject.IsInstanceValid(_connectedRootWindow))
            _connectedRootWindow.SizeChanged -= OnViewportSizeChanged;
        _connectedViewport = null;
        _connectedRootWindow = null;
        _initialScenePos = null;
        _bgControl = null;
        _bgParent = null;
        _sceneContainer = null;
        _isLiveBgContainer = false;
        _hasLastState = false;
    }

    public override void _Process(double delta)
    {
        SyncTransform();
    }

    private void OnViewportSizeChanged()
    {
        _hasLastState = false;
        SyncTransform();
    }

    public void SyncTransform()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;

        if (_bgControl == null || !GodotObject.IsInstanceValid(_bgControl))
        {
            _bgControl = GetParent() as Control;
            _bgParent = null;
            _sceneContainer = null;
            _initialScenePos = null;
            _isLiveBgContainer = false;
            _hasLastState = false;
        }
        if (_bgControl == null || !GodotObject.IsInstanceValid(_bgControl))
            return;

        if (_bgParent == null || !GodotObject.IsInstanceValid(_bgParent))
        {
            _bgParent = _bgControl.GetParent() as CanvasItem;
            _sceneContainer = null;
            _initialScenePos = null;
            _isLiveBgContainer = false;
            _hasLastState = false;
        }
        if (_bgParent == null || !GodotObject.IsInstanceValid(_bgParent))
            return;

        // Only engage inside live combat rooms / BgContainer; leave isolated headless SubViewport probes untouched.
        if (!_isLiveBgContainer)
        {
            bool isLive = _bgParent.Name == "BgContainer" || NCombatRoom.Instance != null;
            if (!isLive)
            {
                _bgParent = null;
                return;
            }
            _isLiveBgContainer = true;
        }

        Vector2 viewportSize = _bgControl.GetViewportRect().Size;
        if (viewportSize.X < 1f || viewportSize.Y < 1f)
            return;

        if (_sceneContainer == null || !GodotObject.IsInstanceValid(_sceneContainer))
        {
            _sceneContainer = NCombatRoom.Instance?.SceneContainer ?? _bgParent.GetParent() as Control;
            _initialScenePos = null;
            _hasLastState = false;
        }

        Vector2 scenePos = Vector2.Zero;
        bool hasSceneContainer = _sceneContainer != null && GodotObject.IsInstanceValid(_sceneContainer);
        if (hasSceneContainer)
            scenePos = _sceneContainer!.Position;

        Transform2D parentGlobal = _bgParent.GetGlobalTransform();
        if (_hasLastState &&
            viewportSize == _lastViewportSize &&
            parentGlobal == _lastParentGlobal &&
            scenePos == _lastScenePos &&
            _nominalSize == _lastNominalSize)
        {
            return;
        }

        float targetGlobalScale = MathF.Max(viewportSize.X / _nominalSize.X, viewportSize.Y / _nominalSize.Y) * OverscanMargin;

        Vector2 nativeShake = Vector2.Zero;
        if (hasSceneContainer)
        {
            _initialScenePos ??= scenePos;
            Vector2 deltaPos = scenePos - _initialScenePos.Value;
            if (deltaPos.LengthSquared() <= 4096f)
            {
                Vector2 spareHalfSpan = (_nominalSize * targetGlobalScale - viewportSize) * 0.5f;
                float maxShakeX = MathF.Max(0f, spareHalfSpan.X - 2f);
                float maxShakeY = MathF.Max(0f, spareHalfSpan.Y - 2f);
                nativeShake = new Vector2(
                    Mathf.Clamp(deltaPos.X, -maxShakeX, maxShakeX),
                    Mathf.Clamp(deltaPos.Y, -maxShakeY, maxShakeY));
            }
        }

        Vector2 targetCenterGlobal = viewportSize * 0.5f + nativeShake;
        Vector2 parentScale = parentGlobal.Scale.Abs();
        if (parentScale.X > 0.001f && parentScale.Y > 0.001f)
        {
            _bgControl.PivotOffset = Vector2.Zero;
            _bgControl.Scale = new Vector2(targetGlobalScale / parentScale.X, targetGlobalScale / parentScale.Y);
            _bgControl.Position = parentGlobal.AffineInverse() * targetCenterGlobal;

            _lastViewportSize = viewportSize;
            _lastParentGlobal = parentGlobal;
            _lastScenePos = scenePos;
            _lastNominalSize = _nominalSize;
            _hasLastState = true;
        }
    }
}
