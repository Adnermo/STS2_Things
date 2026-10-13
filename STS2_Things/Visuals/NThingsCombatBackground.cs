using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2_Things.Visuals;

/// <summary>
/// 原生 NCombatBackground 子类，仅为 Mod Godot 场景提供稳定的 C# ScriptPath。
/// 背景层的选择、预加载和挂载全部由 EncounterModel/BackgroundAssets 处理。
/// </summary>
[GlobalClass]
public partial class NThingsCombatBackground : NCombatBackground
{
    public override void _Ready()
    {
        base._Ready();
        GetNode<Control>("Layer_00").ZIndex = -2;
        // One-layer native templates (e.g. Origin Fogmog) have no Layer_01.
        if (GetNodeOrNull<Control>("Layer_01") is { } middleLayer)
            middleLayer.ZIndex = 1;
        if (GetNodeOrNull<Control>("Foreground") is { } foreground)
            foreground.ZIndex = 3;
    }
}
