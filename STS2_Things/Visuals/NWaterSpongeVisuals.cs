using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Monsters;

namespace STS2_Things.Visuals;

/// <summary>The painted belly fills with water while the feet stay anchored.</summary>
[GlobalClass]
public partial class NWaterSpongeVisuals : NCreatureVisuals
{
    private const string WaterShaderPath = "res://shaders/monsters/sponge_water.gdshader";
    private static readonly StringName ParamWaterLevel = "water_level";
    private static Shader? _cachedWaterShader;

    [Export] public int PreviewWater { get; set; } = -1;
    private Node2D? _body;
    private Vector2 _baseScale;
    private NCreature? _creature;
    private ShaderMaterial? _waterMaterial;
    private float _visibleWater;
    private bool _visualsInitialized;
    private Sprite2D? _shadow;
    private Vector2 _shadowScale;

    public override void _Ready()
    {
        base._Ready();
        _body = GetNode<Node2D>("Visuals");
        _baseScale = _body.Scale;
        _shadow = GetNodeOrNull<Sprite2D>("ContactShadow");
        _shadowScale = _shadow?.Scale ?? Vector2.One;
        if (_cachedWaterShader == null || !GodotObject.IsInstanceValid(_cachedWaterShader))
            _cachedWaterShader = GD.Load<Shader>(WaterShaderPath);
        if (_cachedWaterShader != null && GodotObject.IsInstanceValid(_cachedWaterShader))
        {
            _waterMaterial = new ShaderMaterial
            {
                Shader = _cachedWaterShader,
            };
            // Use Spine's slot material, the same channel the native hue and potion
            // overlays use. Each creature owns its water level and hue independently.
            SpineBody?.SetNormalMaterial(_waterMaterial);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_body == null) return;
        if (_creature == null)
            for (Node? parent = GetParent(); parent != null; parent = parent.GetParent())
                if (parent is NCreature creature) { _creature = creature; break; }
        float water = Math.Clamp(_creature?.Entity?.Monster is ThingsWaterSponge sponge
            ? sponge.VisibleWater : Math.Max(0, PreviewWater), 0f, ThingsWaterSponge.WaterCapacity);
        float blend = 1 - Mathf.Exp(-(float)delta * 7);
        float nextWater = Mathf.Lerp(_visibleWater, water, blend);
        if (Mathf.Abs(nextWater - water) < 0.0005f) nextWater = water;
        Vector2 target = _baseScale * new Vector2(1 + .06f * water, 1 + .045f * water);
        if (!_body.Scale.IsEqualApprox(target))
        {
            Vector2 nextScale = _body.Scale.Lerp(target, blend);
            _body.Scale = nextScale.DistanceSquaredTo(target) < 0.000001f ? target : nextScale;
        }
        if (!_visualsInitialized || !Mathf.IsEqualApprox(nextWater, _visibleWater))
        {
            _visibleWater = nextWater;
            _visualsInitialized = true;
            if (_shadow != null) _shadow.Scale = _shadowScale * new Vector2(1 + .055f * _visibleWater, 1);
            // Update the retained material without stealing a temporary potion overlay.
            if (_waterMaterial != null && GodotObject.IsInstanceValid(_waterMaterial))
                _waterMaterial.SetShaderParameter(ParamWaterLevel, _visibleWater / ThingsWaterSponge.WaterCapacity);
        }
    }
}
