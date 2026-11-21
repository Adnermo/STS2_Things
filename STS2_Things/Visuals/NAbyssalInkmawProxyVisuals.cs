using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2_Things.Visuals;

/// <summary>
/// Proxy creature visual node for 渊墨巨蛸 (Abyssal Inkmaw) and its two flanking hero tentacles.
/// The actual 4-rig Spine visuals render seamlessly inside the subterranean lake/bridge depth sandwich
/// on <see cref="NAbyssalInkmawBossBackground"/>; this proxy elevates hitbox, HP bar, powers, and intents
/// cleanly to world ZIndex 0 above the stone bridge while staying underneath UI overlays and hand cards.
/// </summary>
[GlobalClass]
public partial class NAbyssalInkmawProxyVisuals : NCreatureVisuals
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
