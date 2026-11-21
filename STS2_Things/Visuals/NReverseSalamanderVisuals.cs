using Godot;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2_Things.Visuals;

[GlobalClass]
public partial class NReverseSalamanderVisuals : NCreatureVisuals
{
    private const string FlowMaterialPath = "res://materials/monsters/reverse_current.tres";
    private static readonly StringName ParamVigor = "vigor";
    private static ShaderMaterial? _cachedFlowMaterial;

    private NCreature? _host;
    private ShaderMaterial? _flow;
    private float _vigor;
    private float _appliedVigor = -1f;

    public override void _Ready()
    {
        base._Ready();
        if (_cachedFlowMaterial == null || !GodotObject.IsInstanceValid(_cachedFlowMaterial))
            _cachedFlowMaterial = GD.Load<ShaderMaterial>(FlowMaterialPath);
        if (_cachedFlowMaterial != null && GodotObject.IsInstanceValid(_cachedFlowMaterial))
        {
            _flow = (ShaderMaterial)_cachedFlowMaterial.Duplicate();
            SpineBody?.SetNormalMaterial(_flow);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_host == null)
            for (Node? p = GetParent(); p != null; p = p.GetParent())
                if (p is NCreature creature) { _host = creature; break; }
        float strength = Math.Clamp((_host?.Entity.GetPower<StrengthPower>()?.Amount ?? 0) / 12f, 0, 1);
        _vigor = Mathf.Lerp(_vigor, strength, 1 - Mathf.Exp(-(float)delta * 5));
        if (Mathf.Abs(_vigor - strength) < 0.0005f) _vigor = strength;
        if (_flow != null && GodotObject.IsInstanceValid(_flow) && !Mathf.IsEqualApprox(_vigor, _appliedVigor))
        {
            _appliedVigor = _vigor;
            _flow.SetShaderParameter(ParamVigor, _vigor);
        }
    }
}
