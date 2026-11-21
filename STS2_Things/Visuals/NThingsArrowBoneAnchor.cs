using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;

namespace STS2_Things.Visuals;

/// <summary>
/// Read-only <see cref="Node2D"/> follower that keeps impaled Fool arrows anchored to an animated
/// Spine bone via <see cref="MegaSprite.GetGlobalBoneTransform"/> without mutating the skeleton.
/// </summary>
[GlobalClass]
public partial class NThingsArrowBoneAnchor : Node2D
{
    private Node2D? _spineNode;
    private MegaSprite? _megaSprite;
    private string _boneName = string.Empty;
    private Vector2 _localOffset;
    private float _initialBoneAngle;

    public static NThingsArrowBoneAnchor? Create(
        Node2D spineNode,
        MegaSprite megaSprite,
        string boneName,
        Vector2 initialGlobalPos,
        bool showBehindParent)
    {
        if (!GodotObject.IsInstanceValid(spineNode) || !spineNode.IsInsideTree() ||
            !GodotObject.IsInstanceValid(megaSprite.BoundObject))
        {
            return null;
        }

        Transform2D? initialBoneOpt = megaSprite.GetGlobalBoneTransform(boneName);
        if (!initialBoneOpt.HasValue)
        {
            return null;
        }

        Transform2D initialBone = initialBoneOpt.Value;
        var anchor = new NThingsArrowBoneAnchor
        {
            Name = showBehindParent ? "ThingsArrowBoneBackAnchor" : "ThingsArrowBoneFrontAnchor",
            ShowBehindParent = showBehindParent,
            ProcessPriority = 50,
            _spineNode = spineNode,
            _megaSprite = megaSprite,
            _boneName = boneName,
            _localOffset = initialBone.AffineInverse() * initialGlobalPos,
            _initialBoneAngle = initialBone.X.Angle(),
        };

        spineNode.AddChildSafely(anchor);
        anchor.SyncNow();
        return anchor;
    }

    public void SyncNow()
    {
        if (_spineNode == null || !GodotObject.IsInstanceValid(_spineNode) || !_spineNode.IsInsideTree())
            return;
        if (_megaSprite == null || !GodotObject.IsInstanceValid(_megaSprite.BoundObject))
            return;

        Transform2D? curBone = _megaSprite.GetGlobalBoneTransform(_boneName);
        if (!curBone.HasValue)
            return;

        GlobalPosition = curBone.Value * _localOffset;
        GlobalRotation = curBone.Value.X.Angle() - _initialBoneAngle;
    }

    public override void _Process(double delta)
    {
        SyncNow();
    }

    public override void _ExitTree()
    {
        _megaSprite = null;
        _spineNode = null;
        base._ExitTree();
    }
}
