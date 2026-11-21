using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Monsters;

namespace STS2_Things.Visuals;

/// <summary>
/// Proxy creature visual node for the four stone faces of 人面塔 (The Visage Tower).
/// The actual 3D concave stone relief is rendered seamlessly on <see cref="NHumanFaceTowerBossBackground"/>;
/// this node anchors the centered face's hitbox, HP bar, powers, and intents at world ZIndex 0 while keeping
/// rotated-away faces hidden.
/// </summary>
[GlobalClass]
public partial class NHumanFaceTowerFaceVisuals : NCreatureVisuals
{
    public const int CreatureUiZIndex = 0;

    public override void _Ready()
    {
        base._Ready();
        SyncAndElevateUi();
        Callable.From(SyncAndElevateUi).CallDeferred();
    }

    public void SyncAndElevateUi()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (GetParent() is not CanvasItem parentItem || !GodotObject.IsInstanceValid(parentItem))
            return;

        parentItem.ZIndex = CreatureUiZIndex;
        parentItem.ZAsRelative = false;

        if (parentItem is NCreature creatureNode && creatureNode.Entity?.Monster is ThingsHumanFaceTowerFace face)
        {
            creatureNode.Visible = face.IsActiveFace;
        }

        Node? healthBar = parentItem.GetNodeOrNull("%HealthBar")
            ?? parentItem.GetNodeOrNull("HealthBar")
            ?? parentItem.GetNodeOrNull("StateDisplay")
            ?? parentItem.FindChild("HealthBar", recursive: true, owned: false)
            ?? parentItem.FindChild("StateDisplay", recursive: true, owned: false);
        if (healthBar is CanvasItem hpCanvas)
        {
            hpCanvas.ZIndex = CreatureUiZIndex;
            hpCanvas.ZAsRelative = false;
        }

        Node? intents = parentItem.GetNodeOrNull("%Intents")
            ?? parentItem.GetNodeOrNull("Intents")
            ?? parentItem.GetNodeOrNull("%IntentContainer")
            ?? parentItem.GetNodeOrNull("IntentContainer")
            ?? parentItem.FindChild("Intents", recursive: true, owned: false)
            ?? parentItem.FindChild("IntentContainer", recursive: true, owned: false);
        if (intents is CanvasItem intentCanvas)
        {
            intentCanvas.ZIndex = CreatureUiZIndex;
            intentCanvas.ZAsRelative = false;
        }
    }
}
