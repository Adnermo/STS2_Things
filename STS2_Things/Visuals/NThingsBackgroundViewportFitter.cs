using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2_Things.Visuals;

/// <summary>
/// Dynamically scales and centers custom STS2_Things combat backgrounds to cover the live viewport
/// with a tight 1.04x overscan safety margin across 16:9, 16:10, 21:9, and 4:3 displays, eliminating
/// excessive 1.25x-1.44x zoom-in/cropping while compensating BgContainer scale and tracking camera shake.
/// </summary>
[GlobalClass]
public partial class NThingsBackgroundViewportFitter : Node
{
    public const float OverscanMargin = 1.04f;

    private Vector2 _nominalSize = new(2764.8f, 1296.0f);
    private Vector2? _initialScenePos;
    private Viewport? _connectedViewport;

    public void Configure(Vector2 nominalSize)
    {
        if (nominalSize.X > 1f && nominalSize.Y > 1f)
            _nominalSize = nominalSize;
    }

    public override void _Ready()
    {
        Viewport? viewport = GetViewport();
        if (viewport != null)
        {
            _connectedViewport = viewport;
            viewport.SizeChanged += SyncTransform;
        }

        Callable.From(SyncTransform).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_connectedViewport != null && GodotObject.IsInstanceValid(_connectedViewport))
            _connectedViewport.SizeChanged -= SyncTransform;
        _connectedViewport = null;
        _initialScenePos = null;
    }

    public override void _Process(double delta)
    {
        SyncTransform();
    }

    public void SyncTransform()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (GetParent() is not Control bgControl || !GodotObject.IsInstanceValid(bgControl))
            return;
        if (bgControl.GetParent() is not CanvasItem bgParent || !GodotObject.IsInstanceValid(bgParent))
            return;

        // Only engage inside live combat rooms / BgContainer; leave isolated headless SubViewport probes untouched.
        bool isLiveBgContainer = bgParent.Name == "BgContainer" || NCombatRoom.Instance != null;
        if (!isLiveBgContainer)
            return;

        Vector2 viewportSize = bgControl.GetViewportRect().Size;
        if (viewportSize.X < 1f || viewportSize.Y < 1f)
            return;

        float targetGlobalScale = MathF.Max(viewportSize.X / _nominalSize.X, viewportSize.Y / _nominalSize.Y) * OverscanMargin;

        Control? sceneContainer = NCombatRoom.Instance?.SceneContainer ?? bgParent.GetParent() as Control;
        Vector2 nativeShake = Vector2.Zero;
        if (sceneContainer != null && GodotObject.IsInstanceValid(sceneContainer))
        {
            _initialScenePos ??= sceneContainer.Position;
            Vector2 deltaPos = sceneContainer.Position - _initialScenePos.Value;
            if (deltaPos.LengthSquared() <= 4096f)
                nativeShake = deltaPos;
        }

        Vector2 targetCenterGlobal = viewportSize * 0.5f + nativeShake;
        Transform2D parentGlobal = bgParent.GetGlobalTransform();
        Vector2 parentScale = parentGlobal.Scale.Abs();
        if (parentScale.X > 0.001f && parentScale.Y > 0.001f)
        {
            bgControl.PivotOffset = Vector2.Zero;
            bgControl.Scale = new Vector2(targetGlobalScale / parentScale.X, targetGlobalScale / parentScale.Y);
            bgControl.Position = parentGlobal.AffineInverse() * targetCenterGlobal;
        }
    }
}
