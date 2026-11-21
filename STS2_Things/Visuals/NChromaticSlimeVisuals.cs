using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace STS2_Things.Visuals;

/// <summary>
/// Spine-backed creature visuals for the Chromatic Slime family (<c>large</c>, <c>medium</c>, <c>small</c>),
/// applying multi-skin variant selection (<c>default</c>, <c>crimson</c>, <c>amber</c>, <c>azure</c>) cleanly on load.
/// </summary>
[GlobalClass]
public partial class NChromaticSlimeVisuals : NCreatureVisuals
{
    private static readonly string[] CoreAnimations =
    [
        "idle_loop",
        "attack",
        "cast",
        "hurt",
        "die",
        "power_up",
        "revive",
    ];

    /// <summary>Optional Spine skin name (<c>crimson</c>, <c>amber</c>, <c>azure</c>); <c>default</c> keeps the primary skeleton skin.</summary>
    [Export]
    public string SkinName { get; set; } = "default";

    public override void _Ready()
    {
        base._Ready();
        if (SpineBody == null)
        {
            GD.PushWarning($"{Name}: Chromatic Slime SpineSprite did not load its skeleton data.");
            return;
        }

        if (!string.IsNullOrEmpty(SkinName) && !string.Equals(SkinName, "default", StringComparison.Ordinal))
            ApplySkin(SkinName);

        foreach (string animation in CoreAnimations)
        {
            if (!SpineBody.HasAnimation(animation))
                GD.PushWarning($"{Name}: Spine skeleton is missing core animation '{animation}'.");
        }

        Log.Info($"[ThingsSpine] {Name}: ChromaticSlime visuals ready; skin={SkinName}");
    }

    /// <summary>Applies a named skin (<c>crimson</c>, <c>amber</c>, <c>azure</c>) onto the Spine skeleton safely.</summary>
    public void ApplySkin(string skinName)
    {
        if (SpineBody == null || string.IsNullOrWhiteSpace(skinName))
            return;

        MegaSkeleton? skeleton = SpineBody.GetSkeleton();
        if (skeleton == null)
        {
            GD.PushWarning($"{Name}: Spine skin '{skinName}' was not found on skeleton.");
            return;
        }

        MegaSkeletonDataResource data = skeleton.GetData();
        MegaSkin? skin = data.FindSkin(skinName);
        if (skin == null)
        {
            GD.PushWarning($"{Name}: Spine skin '{skinName}' was not found on skeleton.");
            return;
        }

        skeleton.SetSkin(skin);
        skeleton.SetSlotsToSetupPose();
    }

    /// <summary>
    /// Applies per-strain hitbox bounds and marker offsets onto a shared Chromatic Slime scene
    /// before <c>NCreature.UpdateBounds</c> runs, keeping every strain pixel-aligned without
    /// duplicating <c>.tscn</c> files over the same <c>SpineSkeletonDataResource</c>.
    /// </summary>
    public void ApplyVariantLayout(float boundsTop, Vector2 centerPos, Vector2 intentPos)
    {
        if (GetNodeOrNull<Control>("%Bounds") is { } bounds)
            bounds.OffsetTop = boundsTop;
        if (GetNodeOrNull<Marker2D>("%CenterPos") is { } center)
            center.Position = centerPos;
        if (GetNodeOrNull<Marker2D>("%IntentPos") is { } intent)
            intent.Position = intentPos;
    }
}
