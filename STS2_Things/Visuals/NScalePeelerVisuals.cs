using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2_Things.Visuals;

/// <summary>
/// Controls dynamic dorsal crystal armor plates (<c>plate_0..3</c>) on Scale-Peeler Wyrm
/// in proportion to its current Block, shedding shards when chipped away.
/// </summary>
[GlobalClass]
public partial class NScalePeelerVisuals : NThingsSpineCreatureVisuals
{
    private static readonly string[] PlateSlots = ["plate_0", "plate_1", "plate_2", "plate_3"];
    private static readonly int[] PlateThresholds = [1, 6, 12, 20];
    private static readonly StringName MethodSetColor = "set_color";

    private NCreature? _host;
    private readonly GodotObject?[] _plateSlots = new GodotObject?[4];
    private readonly float[] _plateAlpha = [1f, 1f, 1f, 1f];
    private readonly float[] _appliedPlateAlpha = [-1f, -1f, -1f, -1f];
    private float _appliedGlowBoost = -1f;
    private bool _plateColorsDirty = true;
    private bool _wasDead;
    private Callable _beforeTransformCallable;
    private bool _slotsBound;
    private bool _beforeTransformConnected;
    private int _lastPlateCount = -1;

    public override void _Ready()
    {
        base._Ready();
        TryResolveHost();
        if (SpineBody != null && !_beforeTransformConnected)
        {
            _beforeTransformConnected = true;
            _beforeTransformCallable = Callable.From((Variant _) =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                    return;
                // Spine's die/revive clips keyframe plate_0..3 rgba; all other clips leave slot color untouched.
                bool forceReapply = _host?.Entity?.IsDead == true;
                if (_plateColorsDirty || forceReapply)
                    ApplyPlateColors(force: forceReapply);
            });
            SpineBody.ConnectBeforeWorldTransformsChange(_beforeTransformCallable);
        }
    }

    public override void _ExitTree()
    {
        if (_beforeTransformConnected && SpineBody != null && GodotObject.IsInstanceValid(SpineBody.BoundObject))
        {
            if (SpineBody.BoundObject.IsConnected("before_world_transforms_change", _beforeTransformCallable))
                SpineBody.DisconnectBeforeWorldTransformsChange(_beforeTransformCallable);
        }
        _beforeTransformConnected = false;

        for (int i = 0; i < _plateSlots.Length; i++)
        {
            _plateSlots[i] = null;
            _appliedPlateAlpha[i] = -1f;
        }
        _appliedGlowBoost = -1f;
        _plateColorsDirty = true;
        _slotsBound = false;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        TryResolveHost();

        int targetCount = ResolveVisiblePlateCount();
        bool isDead = _host?.Entity?.IsDead == true;
        bool firstSync = _lastPlateCount < 0 && _host?.Entity != null;
        if (_host?.Entity is { IsAlive: true } entity && _lastPlateCount >= 0 && targetCount < _lastPlateCount)
        {
            NArmorSiphonVfx.SpawnShatterBurst(entity, (_lastPlateCount - targetCount) * 3);
        }
        if (_host?.Entity != null)
            _lastPlateCount = targetCount;

        float rate = (float)delta * 9.5f;
        bool changed = !_slotsBound || isDead != _wasDead;
        _wasDead = isDead;
        for (int i = 0; i < PlateSlots.Length; i++)
        {
            float target = i < targetCount ? 1f : 0f;
            float next = firstSync ? target : Mathf.MoveToward(_plateAlpha[i], target, rate);
            _plateAlpha[i] = next;
            if (!Mathf.IsEqualApprox(next, _appliedPlateAlpha[i]))
                changed = true;
        }

        float glowBoost = (_host?.Entity?.Block ?? 0) >= 20 ? 1.12f : 1f;
        if (!Mathf.IsEqualApprox(glowBoost, _appliedGlowBoost))
            changed = true;

        if (changed)
        {
            _plateColorsDirty = true;
            // Apply directly when dirty so UpdateMode_Manual poses keep exact Block plate state,
            // while clearing _plateColorsDirty avoids a duplicate apply in before_world_transforms_change.
            ApplyPlateColors(force: !_slotsBound || isDead);
        }
    }

    private int ResolveVisiblePlateCount()
    {
        // When rendered standalone without a Creature host (e.g. gallery/glossary preview), display full armor.
        if (_host?.Entity == null)
            return PlateSlots.Length;
        if (_host.Entity.IsDead)
            return 0;

        int block = _host.Entity.Block;
        int visible = 0;
        for (int i = 0; i < PlateThresholds.Length; i++)
        {
            if (block >= PlateThresholds[i])
                visible++;
        }
        return visible;
    }

    private void ApplyPlateColors(bool force)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
            SpineBody == null || !GodotObject.IsInstanceValid(SpineBody.BoundObject))
            return;

        if (!_slotsBound)
        {
            MegaSkeleton? skeleton = SpineBody.GetSkeleton();
            if (skeleton == null)
                return;

            _slotsBound = true;
            for (int i = 0; i < PlateSlots.Length; i++)
            {
                using Variant slotVar = skeleton.BoundObject.Call("find_slot", PlateSlots[i]);
                _plateSlots[i] = slotVar.VariantType == Variant.Type.Object ? slotVar.AsGodotObject() : null;
            }
            force = true;
        }

        int block = _host?.Entity?.Block ?? 0;
        float glowBoost = block >= 20 ? 1.12f : 1f;
        bool glowChanged = force || !Mathf.IsEqualApprox(glowBoost, _appliedGlowBoost);
        for (int i = 0; i < _plateSlots.Length; i++)
        {
            float alpha = _plateAlpha[i];
            if (!glowChanged && Mathf.IsEqualApprox(alpha, _appliedPlateAlpha[i]))
                continue;

            GodotObject? slot = _plateSlots[i];
            if (slot == null || !GodotObject.IsInstanceValid(slot))
                continue;

            using Variant _ = slot.Call(MethodSetColor,
                new Color(glowBoost, glowBoost, glowBoost, alpha));
            _appliedPlateAlpha[i] = alpha;
        }
        _appliedGlowBoost = glowBoost;
        _plateColorsDirty = false;
    }

    private void TryResolveHost()
    {
        if (_host != null)
            return;
        for (Node? parent = GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is NCreature creature)
            {
                _host = creature;
                break;
            }
        }
    }
}
