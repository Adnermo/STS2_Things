using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2_Things.Audio;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Dual-pass background controller for the 活体巨岩 (Living Megalith) boss encounter.
/// Manages two lock-stepped SpineSprite rigs:
/// 1. CaveGodBody: Rendered behind the basalt platform foreground layer with all arm/fist slots masked transparent.
/// 2. CaveGodArms: Rendered in front of the platform foreground layer with all body/crystal/beard slots masked transparent.
/// This creates an authentic 3D sandwich occlusion depth where the boss's colossal body emerges from the lava behind
/// the circular platform, while its massive stone fists rest and strike on top of the front edge without occlusion.
/// </summary>
[GlobalClass]
public partial class NCaveGodBossBackground : Node2D
{
    private const int MainTrack = 0;
    private const int ReactionTrack = 1;
    private const int TremorTrack = 2;

    private ulong _lastHurtAnimTicks;
    private const ulong HurtAnimCooldownMs = 450;

    private ulong _lastGroanTicks;
    private const ulong GroanCooldownMs = 1800;
    private const float GroanChance = 0.25f;

    private static readonly string[] ArmForegroundSlotNames =
    [
        "arm1_2", "arm1_2_red", "arm1_2_back", "arm1_2_back_red", "arm1_3", "arm2_2", "arm2_2_red", "arm2_3"
    ];

    private static readonly string[] BodySlotNames =
    [
        "body_bottom", "body", "body_red",
        "arm1_1", "arm2_1", "neck", "neck_red",
        "head", "head_red", "eyes", "eyes_red", "beard3", "beard3_red",
        "beard2", "beard2_red", "beard1", "beard1_red", "crastalls_roof",
        "crastalls_roof_red", "horn1", "horn1_red", "horn2", "horn2_red",
        "head_skale", "head_skale_red", "horizon"
    ];

    private static readonly string[] BodyRedSlotNames =
    [
        "beard1_red", "beard2_red", "beard3_red",
        "body_red", "crastalls_roof_red", "eyes_red",
        "head_red", "head_skale_red", "horn1_red", "horn2_red", "neck_red"
    ];

    private static readonly string[] BodyBlueSlotNames =
    [
        "beard1", "beard2", "beard3",
        "body", "crastalls_roof", "eyes",
        "head", "head_skale", "horn1", "horn2", "neck"
    ];

    private static readonly string[] ArmRedSlotNames =
    [
        "arm1_2_red", "arm1_2_back_red", "arm2_2_red"
    ];

    private static readonly string[] ArmBlueSlotNames =
    [
        "arm1_2", "arm1_2_back", "arm2_2"
    ];

    private static readonly StringName MethodSetAttachment = "set_attachment";
    private static readonly StringName MethodGetA = "get_a";
    private static readonly StringName MethodGetB = "get_b";
    private static readonly StringName MethodGetC = "get_c";
    private static readonly StringName MethodGetD = "get_d";
    private static readonly StringName MethodGetWorldX = "get_world_x";
    private static readonly StringName MethodGetWorldY = "get_world_y";

    private static readonly Vector2[] HurtScatterOffsets =
    [
        new(0f, 0f),
        new(-28f, 16f),
        new(32f, -12f),
        new(-18f, -22f),
        new(22f, 24f),
        new(35f, 6f),
        new(-32f, -10f),
        new(14f, -18f)
    ];
    private int _hurtScatterIndex;

    private MegaSprite? _bodyController;
    private MegaSprite? _armsController;
    private Node2D? _bodyNode;
    private Node2D? _armsNode;

    internal Node2D? BodySpineNode => _bodyNode ?? GetNodeOrNull<Node2D>("%CaveGodBody") ?? GetNodeOrNull<Node2D>("CaveGodBody");
    internal MegaSprite? BodySpineController => _bodyController;
    internal Node2D? ArmsSpineNode => _armsNode ?? GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("CaveGodArms");
    internal MegaSprite? ArmsSpineController => _armsController;
    private Tween? _weakHurtTween;
    private Tween? _weakFlashTween;
    private TextureRect? _cachedRedBgNode;
    private Control? _cachedFarLayerNode;
    private bool _hasSyncedFarBackdrop;
    private Vector2 _lastFarViewportSize;
    private Transform2D _lastFarParentGlobal;
    private Tween? _bgTransitionTween;
    private Tween? _releaseTween;
    private bool _presentationEnded;

    private static readonly Vector2 FarBackdropNominalSize = new(2880f, 1440f);
    private const float FarBackdropOverscanMargin = 1.02f;

    // Z-order is owned by code, not the scene: native background scenes must rely on tree order
    // (scripts/verify_project.py rejects z_index in *_background.tscn).
    private const int BackgroundZIndex = -20;
    private const int ArmsZIndex = 2;
    private const int StolenCardZIndex = BackgroundZIndex + 1;
    private const int CapturedPlayerZIndex = BackgroundZIndex + 3;
    private const int FrontFistOverlayZIndex = BackgroundZIndex + 4;

    private Polygon2D? _frontFistOverlay;
    private FistBoneRig? _armsFistRig;
    private Vector2[]? _leftFistBuffer;
    private Vector2[]? _rightFistBuffer;
    private int _activeFistOverlaySide;

    private readonly List<GodotObject> _bodyMaskArmSlots = new();
    private readonly List<GodotObject> _bodyRedSlots = new();
    private readonly List<GodotObject> _bodyBlueSlots = new();

    private readonly List<GodotObject> _armsMaskBodySlots = new();
    private readonly List<GodotObject> _armsRedSlots = new();
    private readonly List<GodotObject> _armsBlueSlots = new();

    private Callable _bodyBeforeTransformsCallable;
    private Callable _armsBeforeTransformsCallable;
    private Callable _armsWorldTransformsCallable;
    private bool _bodyTransformsConnected;
    private bool _armsTransformsConnected;

    private readonly List<(NCreature node, Vector2 origPos, float origRot, Vector2 origScale)> _capturedPlayers = new();
    private bool _isGrabTracking;
    private bool _isRightGrab;

    /// <summary>
    /// True from the moment the unbroken fist starts hauling the captives down (ResumeSlamAnim)
    /// until the impact frame. While set, captured players are stretched along the fall axis so
    /// the plunge reads as being dragged through the air rather than gently lowered.
    /// </summary>
    private bool _isSlamDescending;
    private bool _isAngry;
    private bool _isCriticalTremor;
    private bool _isTransforming;
    private bool _isWeakIdle;
    private bool _isRetreating;
    private CancellationTokenSource? _cts;

    public bool IsAngry => _isAngry;
    public bool IsCriticalTremor => _isCriticalTremor;
    public bool IsWeakIdle => _isWeakIdle;
    public bool IsRetreating => _isRetreating;
    public bool HasCapturedPlayers => _capturedPlayers.Count > 0;

    /// <summary>
    /// Vertical world-space offset applied to the captive claw creature node during grab tracking,
    /// relative to the 'player_grab' bone origin. Lowering the node places the claw's health bar and
    /// nameplate at the lower edge of the giant stone fist instead of floating above the knuckles.
    /// The Bounds / CenterPos markers in things_cave_god_captive_claw.tscn are pre-offset by the
    /// same amount so the hitbox and hit-VFX anchor stay locked to the fist itself.
    /// </summary>
    private const float CaptiveClawNodeOffsetY = 64f;

    /// <summary>How long the crushed impact pose is held before the recovery bounce begins.</summary>
    private const float SlamImpactHoldDuration = 0.08f;

    /// <summary>Duration of the back-eased bounce that returns slammed players to their formation slots.</summary>
    private const float SlamRecoverDuration = 0.42f;

    private Marker2D? _stolenCardPosLeft;
    private Marker2D? _stolenCardPosRight;
    private Marker2D? _leftHandCreatureMarker;
    private Marker2D? _rightHandCreatureMarker;
    private int _leftStolenCardCount;
    private int _rightStolenCardCount;

    private NCreature? _cachedClawNode;

    private NCreature? GetCaptiveClawNode()
    {
        if (_cachedClawNode != null && GodotObject.IsInstanceValid(_cachedClawNode) && _cachedClawNode.IsInsideTree())
        {
            return _cachedClawNode;
        }

        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null) return null;

        _cachedClawNode = room.CreatureNodes.FirstOrDefault(c => c.Entity?.Monster is ThingsCaveGodCaptiveClaw && c.Entity.IsAlive);
        return _cachedClawNode;
    }

    public static void ElevateHandsUi(NCombatRoom? combatRoom)
    {
        if (combatRoom == null) return;
        foreach (NCreature enemyNode in combatRoom.CreatureNodes)
        {
            if (enemyNode != null && GodotObject.IsInstanceValid(enemyNode) && enemyNode.Entity?.IsEnemy == true)
            {
                if (enemyNode.Entity.Monster is ThingsCaveGodBody or ThingsCaveGodLeftHand or ThingsCaveGodRightHand or ThingsCaveGodCaptiveClaw)
                {
                    enemyNode.ZAsRelative = false;
                    enemyNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                }
            }
        }
    }

    private bool IsPresentationAlive() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() && !_presentationEnded;

    private static MegaAnimationState? TryGetValidAnimationState(MegaSprite? controller) =>
        controller != null && GodotObject.IsInstanceValid(controller.BoundObject)
            ? controller.TryGetAnimationState()
            : null;

    private static void SafeKillTween(ref Tween? tween)
    {
        if (tween != null && GodotObject.IsInstanceValid(tween))
            tween.Kill();
        tween = null;
    }

    public static void ResetHandsUi(NCombatRoom? combatRoom)
    {
        if (combatRoom == null || !GodotObject.IsInstanceValid(combatRoom)) return;
        foreach (NCreature enemyNode in combatRoom.CreatureNodes)
        {
            if (enemyNode != null && GodotObject.IsInstanceValid(enemyNode) && enemyNode.Entity?.IsEnemy == true)
            {
                if (enemyNode.Entity.Monster is ThingsCaveGodBody or ThingsCaveGodLeftHand or ThingsCaveGodRightHand or ThingsCaveGodCaptiveClaw)
                {
                    enemyNode.ZAsRelative = false;
                    enemyNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                }
            }
        }
    }

    public void ResetVisualState()
    {
        ResetHandsUi(NCombatRoom.Instance);
        HideFrontFistOverlay();

        if (_isRetreating)
        {
            return;
        }

        SafeKillTween(ref _weakHurtTween);
        SafeKillTween(ref _weakFlashTween);
        SafeKillTween(ref _bgTransitionTween);
        UpdateBackgroundPhaseImmediate(_isAngry);

        if (_bodyNode != null && GodotObject.IsInstanceValid(_bodyNode))
        {
            _bodyNode.Modulate = Colors.White;
            _bodyNode.Position = new Vector2(0f, 15f);
            _bodyNode.ZAsRelative = true;
            _bodyNode.ZIndex = 0;
        }
        if (_armsNode != null && GodotObject.IsInstanceValid(_armsNode))
        {
            _armsNode.Modulate = Colors.White;
            _armsNode.Position = new Vector2(0f, 15f);
            _armsNode.Scale = new Vector2(1.3f, 1.3f);
            _armsNode.ZAsRelative = true;
            _armsNode.ZIndex = ArmsZIndex;
        }

        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;

        ClearReactionTrackBoth(0.1f);

        Control? foreground = GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("Foreground");
        if (foreground != null && GodotObject.IsInstanceValid(foreground))
        {
            foreground.ZAsRelative = true;
            foreground.ZIndex = 0;
            if (foreground.GetChildCount() > 0 && foreground.GetChild(0) is CanvasItem fgChild && GodotObject.IsInstanceValid(fgChild))
            {
                fgChild.ZAsRelative = true;
                fgChild.ZIndex = 0;
            }
        }
        if (_armsNode != null && GodotObject.IsInstanceValid(_armsNode) &&
            foreground != null && GodotObject.IsInstanceValid(foreground))
        {
            // In normal combat, position _armsNode AFTER Foreground in tree order so hands strike in front of the platform
            if (_armsNode.GetIndex() < foreground.GetIndex())
            {
                Node? armsParent = _armsNode.GetParent();
                if (armsParent != null && GodotObject.IsInstanceValid(armsParent))
                    armsParent.MoveChild(_armsNode, foreground.GetIndex() + 1);
            }
        }
    }

    public void ResetArmScale()
    {
        if (_isRetreating)
        {
            return;
        }

        if (_armsNode != null)
        {
            _armsNode.Scale = new Vector2(1.3f, 1.3f);
        }

        Control? foreground = GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("Foreground");
        if (_armsNode != null && foreground != null)
        {
            if (_armsNode.GetIndex() < foreground.GetIndex())
            {
                _armsNode.GetParent()?.MoveChild(_armsNode, foreground.GetIndex() + 1);
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_presentationEnded) return;

        SyncFarBackdropToViewport();

        if (CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } room)
            UpdateHandAnchors(room.CreatureNodes);

        if (_isGrabTracking && _capturedPlayers.Count > 0)
        {
            Transform2D? boneTransform = _armsController?.GetGlobalBoneTransform("player_grab");
            Vector2 grabWorldPos = boneTransform?.Origin ?? new Vector2(983f, 400f);

            // Dynamic plunge stretch based on slam animation phase:
            // 0.00s ~ 0.52s: Upward windup into high sky (firm grip)
            // 0.52s ~ 0.78s: Explosive downward plunge (plunge stretch)
            if (_armsController != null)
            {
                using var mainTrackScope = TrackEntryScope(_armsController.TryGetAnimationState()?.GetCurrent(MainTrack), out MegaTrackEntry? mainTrack);
                if (mainTrack != null)
                {
                    string animName = mainTrack.GetAnimationName();
                    if (animName is "grab_slam" or "grab_slam_angry" or "grab_slam_right" or "grab_slam_right_angry")
                    {
                        float trackTime = mainTrack.GetTrackTime();
                        _isSlamDescending = trackTime >= 0.52f && trackTime < 0.78f;
                    }
                }
            }

            for (int i = 0; i < _capturedPlayers.Count; i++)
            {
                (NCreature node, _, _, Vector2 origScale) = _capturedPlayers[i];
                if (GodotObject.IsInstanceValid(node))
                {
                    Vector2 clusterOffset = GetClusterOffset(i, _capturedPlayers.Count);
                    node.GlobalPosition = grabWorldPos + clusterOffset;
                    node.Rotation = GetClusterTilt(i, _capturedPlayers.Count);

                    // Grip pose: captives are held slightly shrunk inside the fist pocket.
                    Vector2 gripScale = origScale * 0.85f;
                    if (_isSlamDescending)
                    {
                        // Plunge pose: narrow + tall stretch, and the cluster tilt is damped so the
                        // bodies hang straight as they are driven down into the stage.
                        gripScale = new Vector2(gripScale.X * 0.86f, gripScale.Y * 1.22f);
                        node.Rotation = GetClusterTilt(i, _capturedPlayers.Count) * 0.55f;
                    }
                    node.Scale = gripScale;
                }
            }

            // Lock the captive claw monster node onto the giant stone fist so its health bar,
            // nameplate and hitbox all track the palm as it lifts and holds the players.
            NCreature? clawNode = GetCaptiveClawNode();
            if (clawNode != null && GodotObject.IsInstanceValid(clawNode))
            {
                if (clawNode.ZIndex != NCaveGodHandCreatureVisuals.CreatureUiZIndex || clawNode.ZAsRelative)
                {
                    clawNode.ZAsRelative = false;
                    clawNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                }
                clawNode.GlobalPosition = new Vector2(grabWorldPos.X, grabWorldPos.Y + CaptiveClawNodeOffsetY);
            }

            // Ensure boss & hand creature UI nodes remain elevated above world layers
            if (NCombatRoom.Instance != null)
            {
                foreach (NCreature enemyNode in NCombatRoom.Instance.CreatureNodes)
                {
                    if (enemyNode != null && GodotObject.IsInstanceValid(enemyNode) && enemyNode.Entity?.IsEnemy == true)
                    {
                        if (enemyNode.Entity.Monster is ThingsCaveGodBody or ThingsCaveGodLeftHand or ThingsCaveGodRightHand)
                        {
                            if (enemyNode.ZIndex != NCaveGodHandCreatureVisuals.CreatureUiZIndex || enemyNode.ZAsRelative)
                            {
                                enemyNode.ZAsRelative = false;
                                enemyNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Keep targeting, impact effects and health bars attached to the enlarged Spine fists.</summary>
    public void UpdateHandAnchors(IEnumerable<NCreature> creatures)
    {
        if (_presentationEnded || _isRetreating || _armsController is null || _armsNode is null) return;
        foreach (NCreature node in creatures)
        {
            if (!GodotObject.IsInstanceValid(node) || node.Entity?.Monster is not ThingsCaveGodHand hand) continue;
            Transform2D? fist = _armsController.GetGlobalBoneTransform(hand.IsLeft ? "arm1_3" : "arm2_3");
            if (fist is not { } transform) continue;
            // Stay horizontal while the fist rotates. Use world scale so camera
            // zoom and the rig's 1.3x enlargement cannot displace the UI.
            Vector2 offset = new(hand.IsLeft ? -10f : 10f, 135f);
            node.GlobalPosition = transform.Origin + offset * _armsNode.GlobalScale.Abs();
        }
    }

    public override void _ExitTree()
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        _presentationEnded = true;
        _isCriticalTremor = false;
        DisconnectSpineSignals();
        _armsFistRig = null;
        _bodySnatchRig = null;
        _armsSnatchRig = null;
        _bodyContactRig = null;
        _armsContactRig = null;
        _leftHandCreatureMarker = null;
        _rightHandCreatureMarker = null;
        _cachedFarLayerNode = null;
        _hasSyncedFarBackdrop = false;
        HideFrontFistOverlay();
        ClearMainEntries();
        ClearAllStolenCards();
        RestoreCapturedPlayersInstantly();
        ResetVisualState();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void DisconnectSpineSignals()
    {
        DisconnectMainAnimationObservers();

        if (_bodyTransformsConnected && _bodyController != null && GodotObject.IsInstanceValid(_bodyController.BoundObject))
        {
            if (_bodyController.BoundObject.IsConnected("before_world_transforms_change", _bodyBeforeTransformsCallable))
                _bodyController.DisconnectBeforeWorldTransformsChange(_bodyBeforeTransformsCallable);
        }
        _bodyTransformsConnected = false;

        if (_armsTransformsConnected && _armsController != null && GodotObject.IsInstanceValid(_armsController.BoundObject))
        {
            if (_armsController.BoundObject.IsConnected("before_world_transforms_change", _armsBeforeTransformsCallable))
                _armsController.DisconnectBeforeWorldTransformsChange(_armsBeforeTransformsCallable);
            if (_armsController.BoundObject.IsConnected("world_transforms_changed", _armsWorldTransformsCallable))
                _armsController.DisconnectWorldTransformsChanged(_armsWorldTransformsCallable);
        }
        _armsTransformsConnected = false;
    }

    public override void _Ready()
    {
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        // Keep the whole world composition below native cards, menus and the
        // game-over backstop. Child arm tracks use relative Z inside this range.
        if (GetParent() is CanvasItem background)
        {
            background.ZAsRelative = false;
            background.ZIndex = BackgroundZIndex;
        }
        _bodyNode = GetNodeOrNull<Node2D>("%CaveGodBody") ?? GetNodeOrNull<Node2D>("CaveGodBody");
        _armsNode = GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("CaveGodArms");

        if (_bodyNode == null || _armsNode == null)
        {
            Log.Error($"[CaveGodBackground] Setup failed: bodyNode={(_bodyNode != null)}, armsNode={(_armsNode != null)}");
            return;
        }

        _frontFistOverlay = new Polygon2D
        {
            Name = "FrontFistOverlay",
            ZAsRelative = false,
            ZIndex = FrontFistOverlayZIndex,
            Visible = false,
            TextureFilter = TextureFilterEnum.Linear,
            TextureRepeat = TextureRepeatEnum.Disabled
        };
        AddChild(_frontFistOverlay);

        ResetVisualState();

        _stolenCardPosLeft = GetNodeOrNull<Marker2D>("%StolenCardPosLeft")
            ?? GetParent()?.GetNodeOrNull<Marker2D>("%StolenCardPosLeft")
            ?? GetParent()?.GetNodeOrNull<Marker2D>("StolenCardPosLeft");
        _stolenCardPosRight = GetNodeOrNull<Marker2D>("%StolenCardPosRight")
            ?? GetParent()?.GetNodeOrNull<Marker2D>("%StolenCardPosRight")
            ?? GetParent()?.GetNodeOrNull<Marker2D>("StolenCardPosRight");
        foreach (Marker2D? marker in new[] { _stolenCardPosLeft, _stolenCardPosRight })
        {
            if (marker == null) continue;
            marker.ZAsRelative = false;
            marker.ZIndex = StolenCardZIndex;
        }

        _bodyController = new MegaSprite(_bodyNode);
        _armsController = new MegaSprite(_armsNode);

        SetupBodySprite(_bodyController);
        SetupArmsSprite(_armsController);

        Callable.From(AlignStageAndPlayers).CallDeferred();
        Callable.From(SyncFarBackdropToViewport).CallDeferred();

        Log.Info("[CaveGodBackground] Dual-pass CaveGod Spine controllers initialized.");
    }

    private void SyncFarBackdropToViewport()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        if (GetParent() is not Control bgRoot || !GodotObject.IsInstanceValid(bgRoot))
            return;
        if (bgRoot.GetParent() is not CanvasItem bgParent || !GodotObject.IsInstanceValid(bgParent))
            return;
        if (bgParent.Name != "BgContainer" && NCombatRoom.Instance == null)
            return;

        if (_cachedFarLayerNode == null || !GodotObject.IsInstanceValid(_cachedFarLayerNode))
        {
            _cachedFarLayerNode = bgRoot.GetNodeOrNull<Control>("%Layer_00") ?? bgRoot.GetNodeOrNull<Control>("Layer_00");
            _hasSyncedFarBackdrop = false;
        }
        if (_cachedFarLayerNode == null || !GodotObject.IsInstanceValid(_cachedFarLayerNode))
            return;

        Vector2 viewportSize = bgRoot.GetViewportRect().Size;
        if (viewportSize.X < 1f || viewportSize.Y < 1f)
            return;

        Transform2D rootGlobal = bgRoot.GetGlobalTransform();
        if (_hasSyncedFarBackdrop && viewportSize == _lastFarViewportSize && rootGlobal == _lastFarParentGlobal)
            return;

        Vector2 rootScale = rootGlobal.Scale.Abs();
        if (rootScale.X <= 0.001f || rootScale.Y <= 0.001f)
            return;

        float targetGlobalScale = MathF.Max(
            viewportSize.X / FarBackdropNominalSize.X,
            viewportSize.Y / FarBackdropNominalSize.Y) * FarBackdropOverscanMargin;
        Vector2 targetCenterGlobal = viewportSize * 0.5f;

        _cachedFarLayerNode.PivotOffset = Vector2.Zero;
        _cachedFarLayerNode.Scale = new Vector2(targetGlobalScale / rootScale.X, targetGlobalScale / rootScale.Y);
        _cachedFarLayerNode.Position = rootGlobal.AffineInverse() * targetCenterGlobal;

        _lastFarViewportSize = viewportSize;
        _lastFarParentGlobal = rootGlobal;
        _hasSyncedFarBackdrop = true;
    }

    private void OnCombatEnded(CombatRoom room)
    {
        if (room.Encounter is not STS2_Things.Encounters.ThingsCaveGodBossEncounter) return;
        SetCriticalTremor(false);
        _presentationEnded = true;
        HideFrontFistOverlay();
        ClearMainEntries();
        _cts?.Cancel();
        RestoreCapturedPlayersInstantly();
        ClearAllStolenCards();
        SafeKillTween(ref _weakHurtTween);
        SafeKillTween(ref _weakFlashTween);
    }

    /// <summary>
    /// Aligns the player battle formation onto the central basalt cliff stage.
    /// Moves the entire native formation, including other players and their pets.
    /// </summary>
    public void AlignStageAndPlayers()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;
        Control? allyContainer = NCombatRoom.Instance?.GetNodeOrNull<Control>("%AllyContainer");
        if (allyContainer != null && GodotObject.IsInstanceValid(allyContainer) && !allyContainer.HasMeta("CaveGodStageAligned"))
        {
            allyContainer.SetMeta("CaveGodStageAligned", true);
            allyContainer.Position += Vector2.Down * PlayerStageOffsetY;
            Log.Info($"[CaveGodBackground] AllyContainer aligned to central stage (Y + {PlayerStageOffsetY}px -> {allyContainer.Position.Y}).");
        }
    }

    private void SetupBodySprite(MegaSprite sprite)
    {
        this.RunWhenSpineReady(sprite, state =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded ||
                !GodotObject.IsInstanceValid(sprite.BoundObject))
            {
                return;
            }

            ObserveMainAnimation(sprite, foreground: false);
            state.SetAnimation("idle_front", loop: true, MainTrack);
            if (_isCriticalTremor)
                ApplyCriticalTremorToSprite(sprite, true);
            MegaSkeleton? skel = sprite.GetSkeleton();
            if (skel != null)
            {
                foreach (string name in ArmForegroundSlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _bodyMaskArmSlots.Add(slotObj);
                    }
                }
                foreach (string name in BodyRedSlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _bodyRedSlots.Add(slotObj);
                    }
                }
                foreach (string name in BodyBlueSlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _bodyBlueSlots.Add(slotObj);
                    }
                }
            }

            _bodyBeforeTransformsCallable = Callable.From((Variant _) =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
                    return;
                ApplyAnimatedHandPose(sprite, foreground: false);
                // 1. Permanently mask ALL arm/hand/shoulder slots on background body pass
                // Under NO circumstances should _bodyNode EVER render any arm geometry!
                // Clearing slot attachment skips slot mesh generation completely in Spine (color is unused without attachment).
                for (int i = 0; i < _bodyMaskArmSlots.Count; i++)
                {
                    _bodyMaskArmSlots[i].Call(MethodSetAttachment, default(Variant));
                }

                // 2. Strict phase slot isolation (when not dynamically cross-fading during transformation)
                if (!_isTransforming)
                {
                    List<GodotObject> hiddenPhaseSlots = _isAngry ? _bodyBlueSlots : _bodyRedSlots;
                    for (int i = 0; i < hiddenPhaseSlots.Count; i++)
                    {
                        hiddenPhaseSlots[i].Call(MethodSetAttachment, default(Variant));
                    }
                }
            });
            sprite.ConnectBeforeWorldTransformsChange(_bodyBeforeTransformsCallable);
            _bodyTransformsConnected = true;
            Log.Info($"[CaveGodBackground] Body skeleton initialized: {_bodyMaskArmSlots.Count} arm slots masked transparent, {_bodyRedSlots.Count} red slots tracked.");
        });
    }

    private void SetupArmsSprite(MegaSprite sprite)
    {
        this.RunWhenSpineReady(sprite, state =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded ||
                !GodotObject.IsInstanceValid(sprite.BoundObject))
            {
                return;
            }

            ObserveMainAnimation(sprite, foreground: true);
            state.SetAnimation("idle_front", loop: true, MainTrack);
            if (_isCriticalTremor)
                ApplyCriticalTremorToSprite(sprite, true);
            MegaSkeleton? skel = sprite.GetSkeleton();
            if (skel != null)
            {
                foreach (string name in BodySlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _armsMaskBodySlots.Add(slotObj);
                    }
                }
                foreach (string name in ArmRedSlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _armsRedSlots.Add(slotObj);
                    }
                }
                foreach (string name in ArmBlueSlotNames)
                {
                    using Variant slotVar = skel.BoundObject.Call("find_slot", name);
                    if (slotVar.AsGodotObject() is GodotObject slotObj)
                    {
                        _armsBlueSlots.Add(slotObj);
                    }
                }
            }

            _armsBeforeTransformsCallable = Callable.From((Variant _) =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
                    return;
                ApplyAnimatedHandPose(sprite, foreground: true);
                // 1. Permanently mask ALL body/head/neck/horns/chest slots on foreground arms pass
                // Under NO circumstances should _armsNode EVER render any body geometry!
                for (int i = 0; i < _armsMaskBodySlots.Count; i++)
                {
                    _armsMaskBodySlots[i].Call(MethodSetAttachment, default(Variant));
                }

                // 2. Strict phase slot isolation (when not dynamically cross-fading during transformation)
                if (!_isTransforming)
                {
                    List<GodotObject> hiddenPhaseSlots = _isAngry ? _armsBlueSlots : _armsRedSlots;
                    for (int i = 0; i < hiddenPhaseSlots.Count; i++)
                    {
                        hiddenPhaseSlots[i].Call(MethodSetAttachment, default(Variant));
                    }
                }
                });
                _armsWorldTransformsCallable = Callable.From((Variant _) =>
                {
                    if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
                        return;
                    UpdateFrontFistOverlay();
                    UpdateStolenCardPositions();
                });
                sprite.ConnectBeforeWorldTransformsChange(_armsBeforeTransformsCallable);
                sprite.ConnectWorldTransformsChanged(_armsWorldTransformsCallable);
                _armsTransformsConnected = true;
                sprite.BoundObject.Call("update_skeleton", 0f);
                if (NCombatRoom.Instance is { } room && GodotObject.IsInstanceValid(room))
                    UpdateHandAnchors(room.CreatureNodes);
                Log.Info($"[CaveGodBackground] Arms skeleton initialized: {_armsMaskBodySlots.Count} body slots masked transparent, {_armsRedSlots.Count} red slots tracked, {_armsBlueSlots.Count} blue slots tracked.");
        });
    }

    private TextureRect? GetRedBgNode()
    {
        if (!GodotObject.IsInstanceValid(this))
            return null;

        if (_cachedRedBgNode != null)
        {
            if (GodotObject.IsInstanceValid(_cachedRedBgNode))
                return _cachedRedBgNode;
            _cachedRedBgNode = null;
        }

        if (!IsInsideTree())
            return null;

        Node? parent = GetParent();
        if (parent == null || !GodotObject.IsInstanceValid(parent))
            return null;

        Control? layer00 = parent.GetNodeOrNull<Control>("Layer_00");
        if (layer00 == null || !GodotObject.IsInstanceValid(layer00))
            return null;

        _cachedRedBgNode = layer00.GetNodeOrNull<TextureRect>("A/RedBg")
            ?? layer00.FindChild("RedBg", recursive: true, owned: false) as TextureRect;
        return _cachedRedBgNode != null && GodotObject.IsInstanceValid(_cachedRedBgNode)
            ? _cachedRedBgNode
            : null;
    }

    public void UpdateBackgroundPhaseImmediate(bool angry)
    {
        SafeKillTween(ref _bgTransitionTween);
        if (!GodotObject.IsInstanceValid(this))
            return;

        TextureRect? redBg = GetRedBgNode();
        if (redBg != null && GodotObject.IsInstanceValid(redBg))
        {
            redBg.Modulate = new Color(1f, 1f, 1f, angry ? 1f : 0f);
        }
    }

    public void StartBackgroundCrossfade(float durationSeconds)
    {
        if (!IsPresentationAlive())
            return;

        TextureRect? redBg = GetRedBgNode();
        if (redBg == null || !GodotObject.IsInstanceValid(redBg))
            return;

        SafeKillTween(ref _bgTransitionTween);
        _bgTransitionTween = CreateTween();
        _bgTransitionTween.TweenProperty(redBg, "modulate:a", 1.0f, durationSeconds)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        Log.Info($"[CaveGodBackground] Started background crossfade to RED ({durationSeconds}s).");
    }

    public void SetAngry(bool angry)
    {
        if (_isAngry == angry)
            return;

        _isAngry = angry;
        if (!angry && _isCriticalTremor)
            SetCriticalTremor(false);
        string targetIdle = _isAngry ? "idle_front_angry" : "idle_front";

        SetTrackAnimationBoth(targetIdle, loop: true, MainTrack);
        UpdateBackgroundPhaseImmediate(angry);
        Log.Info($"[CaveGodBackground] CaveGod transitioned to {(angry ? "ANGRY" : "NORMAL")} idle ({targetIdle}).");
    }

    public void SetCriticalTremor(bool active)
    {
        if (_presentationEnded || _isRetreating || !_isAngry)
            active = false;
        if (_isCriticalTremor == active)
            return;

        _isCriticalTremor = active;
        ApplyCriticalTremorToSprite(_bodyController, active);
        ApplyCriticalTremorToSprite(_armsController, active);
    }

    private void ApplyCriticalTremorToSprite(MegaSprite? sprite, bool active)
    {
        MegaAnimationState? state = TryGetValidAnimationState(sprite);
        if (state == null) return;
        if (active && HasBossAnimation(sprite, "critical_tremor"))
        {
            state.SetAnimation("critical_tremor", true, TremorTrack);
            using var scope = TrackEntryScope(state.GetCurrent(TremorTrack), out MegaTrackEntry? track);
            if (track != null)
            {
                track.BoundObject.Call("set_mix_blend", SpineMixBlendAdd);
                track.BoundObject.Call("set_alpha", 1f);
                track.SetMixDuration(0.25f);
            }
        }
        else
        {
            using (state.BoundObject.Call("set_empty_animation", TremorTrack, 0.2f))
            {
            }
        }
    }

    /// <summary>
    /// Plays the authentic Phase 2 transformation sequence using the Spine cutscene animation 'main_angry':
    /// 1. Drops any captured players safely and resets visual states;
    /// 2. Plays 'main_angry' (11.2s native, accelerated 1.5x to ~7.46s) in perfect lock-step across dual rigs;
    /// 3. Phase 1 (0.0s - 3.07s): Granite body channels subterranean magma with low rumble;
    /// 4. Phase 2 (3.07s): Molten red veins ignite across eyes and horns; background begins seamless 2.0s crossfade from blue to red;
    /// 5. Phase 3 (3.55s): Enraged roar upward, magma eruption with rock shatter particle;
    /// 6. Phase 4 (4.67s): Colossal body slam and ground shockwave;
    /// 7. Phase 5 (7.46s): Smooth settling into idle_front_angry with fully active red slots and red magma background.
    /// </summary>
    public async Task PlayPhaseTransitionAnim()
    {
        if (!IsPresentationAlive())
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        if (_cts == null || !IsPresentationAlive())
            return;

        ClearReactionTrackBoth(0.1f);

        if (HasCapturedPlayers)
        {
            await DropPlayersToGround(isSlam: false);
            if (!IsPresentationAlive() || token.IsCancellationRequested)
                return;
        }

        ResetVisualState();
        if (!IsPresentationAlive() || token.IsCancellationRequested)
            return;

        const float AnimSpeed = 1.5f;

        try
        {
            _isTransforming = true;
            _isAngry = false; // Spine 'main_angry' starts from normal blue palette and cross-fades internally
            UpdateBackgroundPhaseImmediate(false);

            SetTimeScaleBoth(AnimSpeed);
            SetTrackAnimationBoth("main_angry", loop: false, MainTrack);
            Log.Info($"[CaveGodBackground] Started authentic transformation animation 'main_angry' (speed={AnimSpeed}x, duration={11.2f / AnimSpeed:F2}s).");

            // Segment 1 (0.0s -> 3.07s real time): Boss shifts and channels magma core
            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Long);
            await Cmd.Wait(4.60f / AnimSpeed, token);
            if (!IsPresentationAlive())
                return;

            // Segment 2 (t = 4.60s Spine = 3.07s real): Eyes ignite with blazing molten glow
            // Start background crossfade from icy blue to fiery red over 2.0s
            StartBackgroundCrossfade(durationSeconds: 2.0f);
            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
            await Cmd.Wait((5.33f - 4.60f) / AnimSpeed, token);
            if (!IsPresentationAlive())
                return;

            // Segment 3 (t = 5.33s Spine = 3.55s real): Colossal roar upwards! Magma veins burst forth!
            NativeSfxPlayer.Play("res://sfx/cave_god/anger_roar.mp3", volumeDb: 2f);
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Long);
            Control? vfxContainer = NCombatRoom.Instance?.CombatVfxContainer;
            if (vfxContainer != null && GodotObject.IsInstanceValid(vfxContainer))
            {
                VfxCmd.PlayVfx(new Vector2(983f, 450f), VfxCmd.rockShatterPath, vfxContainer);
            }
            await Cmd.Wait((7.00f - 5.33f) / AnimSpeed, token);
            if (!IsPresentationAlive())
                return;

            // Segment 4 (t = 7.00s Spine = 4.67s real): Brutal body slam / shockwave thrashes across basalt floor
            NativeSfxPlayer.Play("res://sfx/cave_god/rock_punch.mp3", volumeDb: 2f);
            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
            await Cmd.Wait((11.20f - 7.00f) / AnimSpeed, token);
            if (!IsPresentationAlive())
                return;

            // Transformation sequence concluded: seamlessly settle into idle_front_angry
            _isTransforming = false;
            _isAngry = true;
            UpdateBackgroundPhaseImmediate(true);
            SetTimeScaleBoth(1.0f);
            SetTrackAnimationBoth("idle_front_angry", loop: true, MainTrack);
            Log.Info("[CaveGodBackground] Transformation sequence completed successfully into idle_front_angry.");
        }
        catch (OperationCanceledException)
        {
            _isTransforming = false;
            if (!IsPresentationAlive())
                return;

            _isAngry = true;
            UpdateBackgroundPhaseImmediate(true);
            SetTimeScaleBoth(1.0f);
            SetTrackAnimationBoth("idle_front_angry", loop: true, MainTrack);
            Log.Info("[CaveGodBackground] Phase transition animation was cancelled.");
        }
    }

    private void SetTimeScaleBoth(float scale)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;

        TryGetValidAnimationState(_bodyController)?.SetTimeScale(scale);
        TryGetValidAnimationState(_armsController)?.SetTimeScale(scale);
    }

    /// <summary>
    /// Starts attack animation without blocking. The monster move logic orchestrates
    /// precise impact frames (e.g. 1.88s for central_slam, 0.64s/1.34s/2.32s for jabs)
    /// and recovery phases directly.
    /// </summary>
    public void StartAttackAnim(string animBase, bool isFlipped = false)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        if (!animBase.StartsWith("grab_player") && (HasCapturedPlayers || _isGrabTracking))
        {
            RestoreCapturedPlayersInstantly();
        }

        if (_armsNode != null)
        {
            _armsNode.Scale = new Vector2(1.3f, 1.3f);
        }

        if (animBase.StartsWith("grab_player", StringComparison.Ordinal) ||
            animBase.StartsWith("grab_slam", StringComparison.Ordinal))
        {
            _isRightGrab = isFlipped || animBase.Contains("right", StringComparison.Ordinal);
        }

        if (isFlipped)
        {
            animBase = animBase switch
            {
                "alternating_jabs" => "alternating_jabs_right",
                "alternating_jabs_angry" => "alternating_jabs_right_angry",
                "card_snatch" => "card_snatch_right",
                "card_snatch_angry" => "card_snatch_right_angry",
                "front_sweep" => "front_sweep_right",
                "front_sweep_angry" => "front_sweep_right_angry",
                "grab_player" => "grab_player_right",
                "grab_player_angry" => "grab_player_right_angry",
                "grab_slam" => "grab_slam_right",
                "grab_slam_angry" => "grab_slam_right_angry",
                "leftpunch" => "rightpunch",
                "leftpunch_angry" => "rightpunch_angry",
                "weak_attack" => "weak_attack_right",
                "weak_enter" => "weak_enter_right",
                "weak_idle" => "weak_idle_right",
                "weak_recover" => "weak_recover_right",
                _ => animBase
            };
        }

        string anim = animBase;
        if (_isAngry)
        {
            if (!anim.EndsWith("_angry"))
            {
                string angryCandidate = animBase + "_angry";
                if (_bodyController != null && HasBossAnimation(_bodyController, angryCandidate))
                {
                    anim = angryCandidate;
                }
            }
        }
        else
        {
            if (anim.EndsWith("_angry"))
            {
                anim = anim.Substring(0, anim.Length - "_angry".Length);
            }
        }

        string idleAnim = _isAngry ? "idle_front_angry" : "idle_front";

        PrepareContactAim(anim);
        SetTrackAnimationBoth(anim, loop: false, MainTrack);
        if (!animBase.StartsWith("grab_player"))
        {
            AddTrackAnimationBoth(idleAnim, delay: 0f, loop: true, MainTrack);
        }
        Log.Info($"[CaveGodBackground] Started attack animation: {anim} (isAngry={_isAngry}, isFlipped={isFlipped})");
    }

    public async Task PlayAttackAnim(string animBase, float duration, bool isFlipped = false)
    {
        if (!IsPresentationAlive())
            return;

        StartAttackAnim(animBase, isFlipped);
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        if (_cts == null || !IsPresentationAlive())
            return;

        if (duration > 0f)
        {
            await Cmd.Wait(duration, token);
            if (!IsPresentationAlive())
                return;
        }
    }

    /// <summary>Wait deterministically for a combat attack beat, then commit the authored contact pose locally.</summary>
    public async Task WaitForAttackFrame(string animBase, float frameTime, float fallbackDelay, bool subtractPreviousDamageWait = false)
    {
        if (!IsPresentationAlive())
            return;

        bool liveSpineClock = IsInsideTree() &&
                              IsProcessing() &&
                              !NonInteractiveMode.IsActive &&
                              !CombatManager.Instance.IsEnding &&
                              SaveManager.Instance?.PrefsSave?.FastMode != FastModeType.Instant;

        if (liveSpineClock)
        {
            float currentTrackTime = -1f;
            MegaAnimationState? initialState = TryGetValidAnimationState(_armsController)
                ?? TryGetValidAnimationState(_bodyController);
            using (TrackEntryScope(initialState?.GetCurrent(MainTrack), out MegaTrackEntry? liveEntry))
            {
                if (liveEntry != null)
                {
                    string currentAnim = liveEntry.GetAnimationName();
                    if (currentAnim.StartsWith(animBase, StringComparison.Ordinal) ||
                        (animBase == "leftpunch" && currentAnim.StartsWith("rightpunch", StringComparison.Ordinal)))
                    {
                        currentTrackTime = liveEntry.GetTrackTime();
                    }
                }
            }

            if (currentTrackTime >= 0f)
            {
                float remaining = frameTime - currentTrackTime;
                if (remaining > 0.012f)
                {
                    await Cmd.Wait(remaining - 0.008f);
                    if (!IsPresentationAlive())
                        return;
                }
                int guardFrames = 0;
                while (IsPresentationAlive() && IsProcessing() && !CombatManager.Instance.IsEnding && guardFrames++ < 24)
                {
                    MegaAnimationState? checkState = TryGetValidAnimationState(_armsController)
                        ?? TryGetValidAnimationState(_bodyController);
                    using var checkScope = TrackEntryScope(checkState?.GetCurrent(MainTrack), out MegaTrackEntry? checkEntry);
                    if (checkEntry == null) break;
                    string currentAnim = checkEntry.GetAnimationName();
                    bool matches = currentAnim.StartsWith(animBase, StringComparison.Ordinal) ||
                                   (animBase == "leftpunch" && currentAnim.StartsWith("rightpunch", StringComparison.Ordinal));
                    if (!matches || checkEntry.GetTrackTime() >= frameTime - 0.004f)
                        break;
                    SceneTree? tree = GetTree();
                    if (tree == null)
                        return;
                    await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                    if (!IsPresentationAlive())
                        return;
                }
            }
            else if (subtractPreviousDamageWait)
            {
                await Cmd.CustomScaledWait(Math.Max(0f, fallbackDelay - 0.1f), Math.Max(0f, fallbackDelay - 0.2f));
                if (!IsPresentationAlive())
                    return;
            }
            else
            {
                await Cmd.Wait(Math.Max(0f, fallbackDelay));
                if (!IsPresentationAlive())
                    return;
            }
        }
        else if (subtractPreviousDamageWait)
        {
            await Cmd.CustomScaledWait(Math.Max(0f, fallbackDelay - 0.1f), Math.Max(0f, fallbackDelay - 0.2f));
            if (!IsPresentationAlive())
                return;
        }
        else
        {
            await Cmd.Wait(Math.Max(0f, fallbackDelay));
            if (!IsPresentationAlive())
                return;
        }

        if (!IsPresentationAlive())
            return;

        // Instant/skip-wait modes may resolve without advancing the rendered
        // timeline. Show the contact pose before spawning that hit's effects.
        foreach (var (sprite, controller) in new[] { (_bodyNode, _bodyController), (_armsNode, _armsController) })
        {
            if (sprite == null || !GodotObject.IsInstanceValid(sprite))
                continue;

            using var scope = TrackEntryScope(TryGetValidAnimationState(controller)?.GetCurrent(MainTrack), out MegaTrackEntry? entry);
            if (entry != null)
            {
                string currentAnim = entry.GetAnimationName();
                bool matchesAnim = currentAnim.StartsWith(animBase, StringComparison.Ordinal) ||
                                   (animBase == "leftpunch" && currentAnim.StartsWith("rightpunch", StringComparison.Ordinal));
                if (matchesAnim && entry.GetTrackTime() < frameTime)
                {
                    // A seek without elapsed time also has to finish the incoming
                    // blend; otherwise instant mode keeps the previous idle pose.
                    entry.SetMixDuration(0f);
                    entry.SetTrackTime(frameTime);
                    sprite.Call("update_skeleton", 0f);
                }
            }
        }
    }

    public async Task PlayWeakEnterAnim(bool isLeftArmBroken)
    {
        if (!IsPresentationAlive())
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        if (_cts == null || !IsPresentationAlive())
            return;

        ClearReactionTrackBoth(0.1f);

        if (HasCapturedPlayers)
        {
            await DropPlayersToGround(isSlam: false);
            if (!IsPresentationAlive() || token.IsCancellationRequested)
                return;
        }

        string enterAnim = isLeftArmBroken ? "weak_enter" : "weak_enter_right";
        string idleAnim = isLeftArmBroken ? "weak_idle" : "weak_idle_right";
        if (_isAngry)
        {
            enterAnim += "_angry";
            idleAnim += "_angry";
        }

        SetTrackAnimationBoth(enterAnim, loop: false, MainTrack);
        AddTrackAnimationBoth(idleAnim, delay: 0f, loop: true, MainTrack);
        _isWeakIdle = true;

        SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");
        NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);

        try
        {
            // 0.85s: ground thud impact
            await Cmd.Wait(0.85f, token);
            if (!IsPresentationAlive())
                return;

            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
            NativeSfxPlayer.Play("res://sfx/cave_god/toss_impact.mp3", volumeDb: 2f);

            // Remaining enter settle time: 2.20s - 0.85s = 1.35s
            await Cmd.Wait(1.35f, token);
            if (!IsPresentationAlive())
                return;

            Log.Info($"[CaveGodBackground] Weak enter animation completed ({enterAnim} -> {idleAnim}).");
        }
        catch (OperationCanceledException)
        {
            if (!IsPresentationAlive())
                return;
            Log.Info("[CaveGodBackground] Weak enter animation cancelled.");
        }
    }

    public void StartWeakAttackAnim(bool isLeftArmBroken)
    {
        if (!IsPresentationAlive())
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        string attackAnim = isLeftArmBroken ? "weak_attack" : "weak_attack_right";
        string idleAnim = isLeftArmBroken ? "weak_idle" : "weak_idle_right";
        if (_isAngry)
        {
            attackAnim += "_angry";
            idleAnim += "_angry";
        }

        PrepareContactAim(attackAnim);
        SetTrackAnimationBoth(attackAnim, loop: false, MainTrack);
        AddTrackAnimationBoth(idleAnim, delay: 0f, loop: true, MainTrack);
        Log.Info($"[CaveGodBackground] Started weak attack animation: {attackAnim}");
    }

    public async Task PlayWeakRecoverAnim(bool isLeftArmBroken)
    {
        if (!IsPresentationAlive())
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        if (_cts == null || !IsPresentationAlive())
            return;

        string recoverAnim = isLeftArmBroken ? "weak_recover" : "weak_recover_right";
        string idleAnim = _isAngry ? "idle_front_angry" : "idle_front";
        if (_isAngry)
        {
            recoverAnim += "_angry";
        }

        ResetVisualState();

        SetTrackAnimationBoth(recoverAnim, loop: false, MainTrack);
        AddTrackAnimationBoth(idleAnim, delay: 0f, loop: true, MainTrack);
        _isWeakIdle = false;

        try
        {
            // 0.45s: push ground
            await Cmd.Wait(0.45f, token);
            if (!IsPresentationAlive())
                return;
            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");

            // 1.20s: apex rise (0.75s later)
            await Cmd.Wait(0.75f, token);
            if (!IsPresentationAlive())
                return;
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);

            // 2.60s total duration: remaining 1.40s
            await Cmd.Wait(1.40f, token);
            if (!IsPresentationAlive())
                return;
            Log.Info($"[CaveGodBackground] Weak recover animation completed ({recoverAnim} -> {idleAnim}).");
        }
        catch (OperationCanceledException)
        {
            if (!IsPresentationAlive())
                return;
            Log.Info("[CaveGodBackground] Weak recover animation cancelled.");
        }
    }

    public void PlayHurtAnim(bool forceGroan = false, Vector2? hitPos = null, bool allowGroan = true)
    {
        if (_isRetreating)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (!forceGroan && now - _lastHurtAnimTicks < HurtAnimCooldownMs)
        {
            return;
        }
        _lastHurtAnimTicks = now;

        if (_isGrabTracking)
        {
            // During grab tracking, DO NOT play hit_recoil on Track 1!
            // hit_recoil overwrites arm bones and the player_grab bone, causing violent twitching/spasms
            // while the giant stone hand is holding the player in mid-air.
            // Instead, play stone flash, rock debris VFX, and subtle shake!
            PlayGrabHurtFeedback(hitPos, forceGroan, allowGroan);
            return;
        }

        if (!_isWeakIdle)
        {
            string hurtAnim = _isAngry ? "hit_recoil_angry" : "hit_recoil";
            SetHurtTrackAnimationBoth(hurtAnim);
            if (allowGroan)
                PlayHurtGroan(forceGroan);
        }
        else
        {
            PlayWeakHurtAnim(hitPos, forceGroan, allowGroan);
        }
    }

    public void PlayGrabHurtFeedback(Vector2? hitPos = null, bool forceGroan = false, bool allowGroan = true)
    {
        // 1. Rock Shatter Particle VFX at hit position
        Control? vfxContainer = NCombatRoom.Instance?.CombatVfxContainer;
        if (vfxContainer != null)
        {
            Vector2 offset = HurtScatterOffsets[_hurtScatterIndex % HurtScatterOffsets.Length];
            _hurtScatterIndex++;
            Vector2 defaultPos = new Vector2(960f, 400f);
            Transform2D? boneTransform = _armsController?.GetGlobalBoneTransform("player_grab");
            if (boneTransform.HasValue)
            {
                defaultPos = boneTransform.Value.Origin;
            }
            Vector2 spawnPos = (hitPos ?? defaultPos) + offset;
            VfxCmd.PlayVfx(spawnPos, VfxCmd.rockShatterPath, vfxContainer);
        }

        // 2. Heavy Stone Flash on Arms Modulate (white flash in 0.12s)
        if (_armsNode != null)
        {
            Color flashColor = _isAngry ? new Color(1.6f, 0.9f, 0.8f, 1f) : new Color(1.4f, 1.4f, 1.5f, 1f);
            _armsNode.Modulate = flashColor;
            _weakFlashTween?.Kill();
            _weakFlashTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            _weakFlashTween.TweenProperty(_armsNode, "modulate", Colors.White, 0.12f);
        }

        // 3. Shake & optional groan (native TakeDamageSfxType => DamageSfxType.Stone already plays impact SFX)
        NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
        if (allowGroan)
            PlayHurtGroan(forceGroan);
    }

    /// <summary>
    /// Plays the authentic Living Megalith prone hurt feedback:
    /// 1. Rock shatter particle VFX (vfx_rock_shatter) bursting with stone debris & dust;
    /// 2. Heavy body stone flash (magma red/gold if angry, pale granite white if normal);
    /// 3. Grounded thud impulse tween (Y-axis downward compression 5px + monotonic settle in 0.14s, zero upward bounce);
    /// 4. Optional low guttural hurt groan.
    /// Does NOT mix upright hit_recoil animation, completely preventing prone posture twisting.
    /// </summary>
    public void PlayWeakHurtAnim(Vector2? hitPos = null, bool forceGroan = false, bool allowGroan = true)
    {
        if (_isRetreating)
        {
            return;
        }

        // 1. Rock Shatter Particle VFX
        Control? vfxContainer = NCombatRoom.Instance?.CombatVfxContainer;
        if (vfxContainer != null)
        {
            Vector2 offset = HurtScatterOffsets[_hurtScatterIndex % HurtScatterOffsets.Length];
            _hurtScatterIndex++;
            Vector2 defaultPos = new Vector2(983f, 620f);
            Vector2 spawnPos = (hitPos ?? defaultPos) + offset;
            VfxCmd.PlayVfx(spawnPos, VfxCmd.rockShatterPath, vfxContainer);
        }

        // 2. Heavy Stone Flash (Modulate Tween)
        if (_bodyNode != null && _armsNode != null)
        {
            Color flashColor = _isAngry ? new Color(1.6f, 0.9f, 0.8f, 1f) : new Color(1.4f, 1.4f, 1.5f, 1f);
            _bodyNode.Modulate = flashColor;
            _armsNode.Modulate = flashColor;

            _weakFlashTween?.Kill();
            _weakFlashTween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            _weakFlashTween.TweenProperty(_bodyNode, "modulate", Colors.White, 0.14f);
            _weakFlashTween.TweenProperty(_armsNode, "modulate", Colors.White, 0.14f);
        }

        // 3. Ground Thud Impulse (heavy downward crush & monotonic settle on stone floor — zero elastic upward bounce)
        if (_bodyNode != null && _armsNode != null)
        {
            _weakHurtTween?.Kill();
            _weakHurtTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);

            // 0.04s: Heavy impact crushes body down 5px
            _weakHurtTween.TweenProperty(_bodyNode, "position:y", 20f, 0.04f);
            _weakHurtTween.Parallel().TweenProperty(_armsNode, "position:y", 20f, 0.04f);

            // 0.10s: Settles monotonically back to resting baseline (15f)
            _weakHurtTween.Chain().TweenProperty(_bodyNode, "position:y", 15f, 0.10f);
            _weakHurtTween.Parallel().TweenProperty(_armsNode, "position:y", 15f, 0.10f);
        }

        // 4. Screen Shake & optional groan (native TakeDamageSfxType => DamageSfxType.Stone already plays impact SFX)
        NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
        if (allowGroan)
            PlayHurtGroan(forceGroan);
    }

    /// <summary>Spine MixBlend.add: hit_recoil stores pure flinch deltas from the rest pose.</summary>
    private const int SpineMixBlendAdd = 3;

    private void SetHurtTrackAnimationBoth(string animName)
    {
        foreach (MegaSprite? sprite in new[] { _bodyController, _armsController })
        {
            MegaAnimationState? state = TryGetValidAnimationState(sprite);
            if (state == null || !HasBossAnimation(sprite, animName)) continue;
            state.SetAnimation(animName, false, ReactionTrack);
            using (TrackEntryScope(state.GetCurrent(ReactionTrack), out MegaTrackEntry? track))
            {
                if (track != null)
                {
                    // Layered on whatever the main track plays (idle or an attack), so the flinch
                    // never pulls a swing back toward the rest pose and ends without a pop.
                    track.BoundObject.Call("set_mix_blend", SpineMixBlendAdd);
                    track.BoundObject.Call("set_alpha", 1f);
                    track.SetMixDuration(0.15f);
                }
            }
            using (state.BoundObject.Call("add_empty_animation", ReactionTrack, 0.12f, 0f))
            {
            }
        }
    }

    public void PlayHurtGroan(bool force = false)
    {
        ulong now = Time.GetTicksMsec();
        if (!force && (now - _lastGroanTicks < GroanCooldownMs || !NativeSfxPlayer.RollChance(GroanChance)))
        {
            return;
        }

        _lastGroanTicks = now;
        NativeSfxPlayer.Play("res://sfx/cave_god/cave_god_hurt", volumeDb: 2f);
    }

    public void PlayBodyDeathAnim()
    {
        SetCriticalTremor(false);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        if (HasCapturedPlayers)
        {
            RestoreCapturedPlayersInstantly();
        }
        HideFrontFistOverlay();

        _isRetreating = true;
        _weakHurtTween?.Kill();
        _weakHurtTween = null;
        _weakFlashTween?.Kill();
        _weakFlashTween = null;

        ZAsRelative = true;
        ZIndex = 0;

        // Reset all custom ZIndex overrides so native Tree Ordering takes absolute precedence
        if (_armsNode != null)
        {
            _armsNode.ZAsRelative = true;
            _armsNode.ZIndex = 0;
        }
        if (_bodyNode != null)
        {
            _bodyNode.ZAsRelative = true;
            _bodyNode.ZIndex = 0;
        }

        Control? foreground = GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("%Foreground")
            ?? GetParent()?.GetNodeOrNull<Control>("Foreground");
        if (foreground != null)
        {
            foreground.ZAsRelative = true;
            foreground.ZIndex = 0;
            if (foreground.GetChildCount() > 0 && foreground.GetChild(0) is CanvasItem fgChild)
            {
                fgChild.ZAsRelative = true;
                fgChild.ZIndex = 0;
            }

            // Move Foreground platform to the very end of the background scene tree!
            // In Godot 2D native tree ordering, the last child in the parent renders in front of all previous siblings.
            // By moving Foreground to the last index, the basalt platform is guaranteed to render in front of BOTH
            // CaveGodBody (index 1) and CaveGodArms (index 2), seamlessly occluding all retreating geometry!
            Node? parent = foreground.GetParent() ?? _armsNode?.GetParent();
            if (parent != null)
            {
                if (_armsNode != null && _armsNode.GetParent() == parent)
                {
                    parent.MoveChild(_armsNode, 1);
                }
                parent.MoveChild(foreground, parent.GetChildCount() - 1);
                Log.Info($"[CaveGodBackground] Moved Foreground platform to tail index {foreground.GetIndex()} (arms at index {_armsNode?.GetIndex()}) to strictly cover retreating body and arms.");
            }
        }

        // Clear reaction track so hurt anim cannot glitch retreat anim
        AddEmptyReactionAnimation();

        const float Speed = 1.0f; // 7.20s authentic native speed retreat sink into magma floor
        string hideAnim = _isAngry ? "hide_angry" : "hide";

        if (HasBossAnimation(_bodyController, hideAnim))
        {
            SetTimeScaleBoth(Speed);
            SetTrackAnimationBoth(hideAnim, loop: false, MainTrack);
        }
        else
        {
            string recoilAnim = _isAngry ? "hit_recoil_angry" : "hit_recoil";
            SetTrackAnimationBoth(recoilAnim, loop: false, MainTrack);
        }

        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
        NativeSfxPlayer.Play("res://sfx/cave_god/rock_death.mp3", volumeDb: 2f, allowCombatEnding: true);

        Log.Info($"[CaveGodBackground] Playing authentic retreat death animation '{hideAnim}' (speed={Speed}x, duration={7.2f / Speed:F2}s, ZIndex=-1 behind floor).");
    }

    private void SetTrackAnimationBoth(string animName, bool loop, int track)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;

        float mixDuration = loop ? 0.30f : 0.12f;
        MegaAnimationState? bodyState = TryGetValidAnimationState(_bodyController);
        MegaAnimationState? armsState = TryGetValidAnimationState(_armsController);

        if (bodyState != null && HasBossAnimation(_bodyController, animName))
        {
            bodyState.SetAnimation(animName, loop, track);
            using (TrackEntryScope(bodyState.GetCurrent(track), out MegaTrackEntry? entry))
            {
                entry?.SetMixDuration(mixDuration);
            }
        }
        if (armsState != null && HasBossAnimation(_armsController, animName))
        {
            armsState.SetAnimation(animName, loop, track);
            using (TrackEntryScope(armsState.GetCurrent(track), out MegaTrackEntry? entry))
            {
                entry?.SetMixDuration(mixDuration);
            }
        }
    }

    private void AddTrackAnimationBoth(string animName, float delay, bool loop, int track)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;

        MegaAnimationState? bodyState = TryGetValidAnimationState(_bodyController);
        MegaAnimationState? armsState = TryGetValidAnimationState(_armsController);

        if (bodyState != null && HasBossAnimation(_bodyController, animName))
        {
            bodyState.AddAnimation(animName, delay, loop, track);
        }
        if (armsState != null && HasBossAnimation(_armsController, animName))
        {
            armsState.AddAnimation(animName, delay, loop, track);
        }
    }

    private void ClearReactionTrackBoth(float mixDuration = 0.1f)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        foreach (MegaSprite? sprite in new[] { _bodyController, _armsController })
        {
            MegaAnimationState? state = TryGetValidAnimationState(sprite);
            if (state == null) continue;
            using (state.BoundObject.Call("set_empty_animation", ReactionTrack, mixDuration))
            {
            }
        }
    }

    private void AddEmptyReactionAnimation() => ClearReactionTrackBoth(0.1f);

    /// <summary>
    /// Starts real-time bone tracking for grabbed players.
    /// Uses a 2.5D sandwich occlusion stack:
    /// - CaveGodArms remains at relative ZIndex 2 (global -18, behind captives so forearm &amp; back-of-hand stay behind);
    /// - Captured players render at global ZIndex -17 (emerging out the top of the clenched fist pocket);
    /// - FrontFistOverlay renders the Spine-weighted curled fingers &amp; thumb at global ZIndex -16 in front of captives.
    /// </summary>
    public void StartGrabTracking(IReadOnlyList<Creature> targets, bool isFlipped = false)
    {
        if (_presentationEnded) return;
        _isRightGrab = isFlipped;
        _capturedPlayers.Clear();
        NCombatRoom? combatRoom = NCombatRoom.Instance;
        if (combatRoom == null) return;

        Node2D? armsNode = GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("%CaveGodArms");
        if (armsNode != null)
        {
            armsNode.ZAsRelative = true;
            armsNode.ZIndex = ArmsZIndex;
            armsNode.Scale = new Vector2(1.3f, 1.3f);
        }

        foreach (Creature target in targets)
        {
            NCreature? node = combatRoom.GetCreatureNode(target);
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                _capturedPlayers.Add((node, node.Position, node.Rotation, node.Scale));
                node.ZAsRelative = false;
                node.ZIndex = CapturedPlayerZIndex;
                if (node.Entity.IsAlive) node.SetAnimationTrigger("Hit");
            }
        }

        ElevateHandsUi(combatRoom);

        _isGrabTracking = true;
        UpdateFrontFistOverlay();
        Log.Info($"[CaveGodBackground] Started real-time 2.5D sandwich grab tracking for {_capturedPlayers.Count} players (isFlipped={isFlipped}).");
    }

    /// <summary>
    /// Starts real-time bone tracking for grabbed players and awaits the upward lift duration.
    /// </summary>
    public async Task LiftPlayersToAir(IReadOnlyList<Creature> targets, float duration = 1.10f, bool isFlipped = false)
    {
        StartGrabTracking(targets, isFlipped);
        if (duration > 0f)
        {
            await Cmd.Wait(duration);
        }
    }

    /// <summary>
    /// Releases captured players back onto the stage floor.
    /// When <paramref name="isSlam"/> is true the giant fist has just driven them into the basalt,
    /// so this plays the full slam reaction: hurt pose, vertical squash, dust + debris burst,
    /// screen shake, a short hit-stop, then a back-eased bounce back into formation slots.
    /// When false (claw shattered) players are safely eased down from mid-air with cubic ease.
    /// </summary>
    public async Task DropPlayersToGround(bool isSlam)
    {
        _isGrabTracking = false;
        _isSlamDescending = false;
        HideFrontFistOverlay();

        if (_capturedPlayers.Count == 0) return;
        if (!IsPresentationAlive())
        {
            RestoreCapturedPlayersInstantly();
            return;
        }

        NCombatRoom? combatRoom = NCombatRoom.Instance;
        if (combatRoom == null || !GodotObject.IsInstanceValid(combatRoom))
        {
            RestoreCapturedPlayersInstantly();
            return;
        }

        if (isSlam)
        {
            // ---------------------------------------------------------------
            // Impact frame: the fist has just crushed the captives into the stage.
            // Everything below is driven off a single clock so every player in a
            // multiplayer cluster lands on exactly the same frame.
            // ---------------------------------------------------------------
            Control? impactVfx = NCombatRoom.Instance?.CombatVfxContainer;
            List<NCreature> validCaptives = [];

            foreach ((NCreature node, Vector2 origPos, float origRot, Vector2 origScale) in _capturedPlayers)
            {
                if (!GodotObject.IsInstanceValid(node))
                {
                    continue;
                }

                // Crumple instead of gliding: the captive is thrown into its hurt reaction.
                if (node.Entity.IsAlive) node.SetAnimationTrigger("Hit");

                // Vertical squash reads as the body being flattened into the stone floor.
                node.Scale = new Vector2(origScale.X * 1.26f, origScale.Y * 0.60f);
                node.Rotation = origRot;
                validCaptives.Add(node);
            }

            if (impactVfx != null && GodotObject.IsInstanceValid(impactVfx) && validCaptives.Count > 0)
            {
                foreach (NCreature burstTarget in SelectRepresentativeSlamImpactNodes(validCaptives))
                {
                    // Dust kicked off the basalt at floor level, plus a spray of shattered rock.
                    Vector2 groundPos = burstTarget.GlobalPosition + new Vector2(0f, 26f);
                    VfxCmd.PlayVfx(groundPos, VfxCmd.sandyImpactPath, impactVfx);
                    VfxCmd.PlayVfx(groundPos, VfxCmd.rockShatterPath, impactVfx);
                }
            }

            SfxCmd.Play("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone");
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Long);

            // Hit-stop: hold the crushed pose so the impact registers before recovery starts.
            await Cmd.Wait(SlamImpactHoldDuration);
            if (!IsPresentationAlive() || _capturedPlayers.Count == 0 || !GodotObject.IsInstanceValid(combatRoom))
                return;

            // ---------------------------------------------------------------
            // Recovery: a back-ease overshoots slightly, so the captives peel off the
            // floor and bounce back into formation instead of sliding there.
            // ---------------------------------------------------------------
            Tween recoverTween = combatRoom.CreateTween().SetParallel()
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            SafeKillTween(ref _releaseTween);
            _releaseTween = recoverTween;
            foreach ((NCreature node, Vector2 origPos, float origRot, Vector2 origScale) in _capturedPlayers)
            {
                if (!GodotObject.IsInstanceValid(node))
                {
                    continue;
                }
                recoverTween.TweenProperty(node, "position", origPos, SlamRecoverDuration);
                recoverTween.TweenProperty(node, "rotation", origRot, SlamRecoverDuration);
                recoverTween.TweenProperty(node, "scale", origScale, SlamRecoverDuration);
            }

            await Cmd.Wait(SlamRecoverDuration);
            if (!IsPresentationAlive())
                return;
            RestoreCapturedPlayersInstantly();
            return;
        }

        // Safe release upon shattered claw: smooth cubic descent back to original stage floor
        // AND smoothly lower the giant stone arm from the sky back to resting ground idle!
        string idleAnim = _isAngry ? "idle_front_angry" : "idle_front";
        MegaAnimationState? bodyState = TryGetValidAnimationState(_bodyController);
        MegaAnimationState? armsState = TryGetValidAnimationState(_armsController);
        if (bodyState != null)
        {
            bodyState.SetAnimation(idleAnim, true, MainTrack);
            using var trackScope = TrackEntryScope(bodyState.GetCurrent(MainTrack), out MegaTrackEntry? track);
            track?.SetMixDuration(0.5f);
        }
        if (armsState != null)
        {
            armsState.SetAnimation(idleAnim, true, MainTrack);
            using var trackScope = TrackEntryScope(armsState.GetCurrent(MainTrack), out MegaTrackEntry? track);
            track?.SetMixDuration(0.5f);
        }

        float duration = 0.50f;
        Tween tween = combatRoom.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        SafeKillTween(ref _releaseTween);
        _releaseTween = tween;
        foreach ((NCreature node, Vector2 origPos, float origRot, Vector2 origScale) in _capturedPlayers)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                tween.TweenProperty(node, "position", origPos, duration);
                tween.TweenProperty(node, "rotation", origRot, duration);
                tween.TweenProperty(node, "scale", origScale, duration);
            }
        }

        await Cmd.Wait(duration);
        if (!IsPresentationAlive())
            return;
        RestoreCapturedPlayersInstantly();
    }

    /// <summary>
    /// Instantly restores all captured players to their original positions and state.
    /// Guaranteed to be called on room exit, death, or error to prevent permanent coordinate drift.
    /// Resets CaveGodArms and CaveGodBody ZIndex back to default layer and ensures arms return to idle.
    /// </summary>
    public void RestoreCapturedPlayersInstantly()
    {
        SafeKillTween(ref _releaseTween);
        _isGrabTracking = false;
        _isSlamDescending = false;
        _isRightGrab = false;
        HideFrontFistOverlay();
        ResetHandsUi(NCombatRoom.Instance);
        bool selfValid = GodotObject.IsInstanceValid(this);
        bool inTree = selfValid && IsInsideTree();
        if (!_isRetreating && selfValid)
        {
            Node2D? armsNode = _armsNode != null && GodotObject.IsInstanceValid(_armsNode)
                ? _armsNode
                : (inTree ? GetNodeOrNull<Node2D>("%CaveGodArms") ?? GetParent()?.GetNodeOrNull<Node2D>("%CaveGodArms") : null);
            if (armsNode != null && GodotObject.IsInstanceValid(armsNode))
            {
                armsNode.ZAsRelative = true;
                armsNode.ZIndex = 2;
                armsNode.Scale = new Vector2(1.3f, 1.3f);
            }
            if (_bodyNode != null && GodotObject.IsInstanceValid(_bodyNode))
            {
                _bodyNode.ZAsRelative = true;
                _bodyNode.ZIndex = 0;
            }
        }

        string idleAnim = _isAngry ? "idle_front_angry" : "idle_front";
        // On room exit the Spine children have already left the tree. Restore
        // player transforms, but do not create track wrappers from a torn-down rig.
        MegaAnimationState? bodyState = (!inTree || _presentationEnded) ? null : TryGetValidAnimationState(_bodyController);
        MegaAnimationState? armsState = (!inTree || _presentationEnded) ? null : TryGetValidAnimationState(_armsController);
        if (bodyState != null)
        {
            using var currentTrackScope = TrackEntryScope(bodyState.GetCurrent(MainTrack), out MegaTrackEntry? currentTrack);
            if (currentTrack != null)
            {
                // grab_player, grab_hold and grab_slam (left/right, normal/angry) all carry captives.
                if (currentTrack.GetAnimationName().StartsWith("grab_", StringComparison.Ordinal))
                {
                    bodyState.SetAnimation(idleAnim, true, MainTrack);
                    using var trackScope = TrackEntryScope(bodyState.GetCurrent(MainTrack), out MegaTrackEntry? track);
                    track?.SetMixDuration(0.3f);
                }
            }
        }
        if (armsState != null)
        {
            using var currentTrackScope = TrackEntryScope(armsState.GetCurrent(MainTrack), out MegaTrackEntry? currentTrack);
            if (currentTrack != null)
            {
                if (currentTrack.GetAnimationName().StartsWith("grab_", StringComparison.Ordinal))
                {
                    armsState.SetAnimation(idleAnim, true, MainTrack);
                    using var trackScope = TrackEntryScope(armsState.GetCurrent(MainTrack), out MegaTrackEntry? track);
                    track?.SetMixDuration(0.3f);
                }
            }
        }

        foreach ((NCreature node, Vector2 origPos, float origRot, Vector2 origScale) in _capturedPlayers)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.ZAsRelative = true;
                node.ZIndex = 0;
                node.Position = origPos;
                node.Rotation = origRot;
                node.Scale = origScale;
                // Preserve the native death pose and keep end-of-combat UI hidden.
                if (!_presentationEnded && node.Entity.IsAlive && CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsEnding)
                {
                    node.AnimEnableUi();
                    node.SetAnimationTrigger("Idle");
                }
            }
        }
        _capturedPlayers.Clear();
    }

    private const int MaxSlamImpactVfxCount = 6;
    private const float ExtendedClusterMinRadiusX = 34f;
    private const float ExtendedClusterMaxRadiusX = 54f;
    private const float ExtendedClusterMinRadiusY = 24f;
    private const float ExtendedClusterMaxRadiusY = 36f;

    private static IReadOnlyList<NCreature> SelectRepresentativeSlamImpactNodes(List<NCreature> validCaptives)
    {
        if (validCaptives.Count <= MaxSlamImpactVfxCount)
            return validCaptives;

        var selected = new List<NCreature>(MaxSlamImpactVfxCount);
        int localIndex = validCaptives.FindIndex(c => c.Entity != null && LocalContext.IsMe(c.Entity));
        if (localIndex >= 0)
            selected.Add(validCaptives[localIndex]);

        int remaining = MaxSlamImpactVfxCount - selected.Count;
        if (remaining <= 0)
            return selected;

        var others = new List<NCreature>(validCaptives.Count);
        for (int i = 0; i < validCaptives.Count; i++)
        {
            if (i != localIndex)
                others.Add(validCaptives[i]);
        }

        if (others.Count <= remaining)
        {
            selected.AddRange(others);
            return selected;
        }

        for (int k = 0; k < remaining; k++)
        {
            int pick = remaining == 1
                ? others.Count / 2
                : (int)MathF.Round(k * (others.Count - 1) / (float)(remaining - 1));
            selected.Add(others[Math.Clamp(pick, 0, others.Count - 1)]);
        }

        return selected;
    }

    private static Vector2 GetClusterOffset(int index, int total)
    {
        if (total <= 1) return Vector2.Zero;
        if (total == 2)
        {
            return index == 0 ? new Vector2(-16f, -6f) : new Vector2(16f, 6f);
        }
        if (total == 3)
        {
            return index switch
            {
                0 => new Vector2(0f, -20f),
                1 => new Vector2(-20f, 10f),
                _ => new Vector2(20f, 10f)
            };
        }
        if (total == 4)
        {
            return index switch
            {
                0 => new Vector2(-18f, -18f),
                1 => new Vector2(18f, -18f),
                2 => new Vector2(-22f, 10f),
                _ => new Vector2(22f, 10f)
            };
        }

        // >4 players: pack all captives in a compact Vogel golden-angle oval cluster inside the
        // stone fist pocket (|X| <= 54, |Y| <= 36) so players 5..32 never stack onto one point.
        const float goldenAngle = 2.39996323f;
        float t = Mathf.Clamp((total - MultiplayerCompat.VanillaMaxPlayers) / 20f, 0f, 1f);
        float radiusX = Mathf.Lerp(ExtendedClusterMinRadiusX, ExtendedClusterMaxRadiusX, t);
        float radiusY = Mathf.Lerp(ExtendedClusterMinRadiusY, ExtendedClusterMaxRadiusY, t);
        float radial = MathF.Sqrt((index + 0.5f) / Math.Max(1, total));
        float angle = index * goldenAngle;
        return new Vector2(
            MathF.Cos(angle) * radial * radiusX,
            MathF.Sin(angle) * radial * radiusY);
    }

    private static float GetClusterTilt(int index, int total)
    {
        if (total <= 1) return 0f;
        if (total == 2) return index == 0 ? 0.15f : -0.15f;
        if (total == 3) return index == 0 ? 0f : (index == 1 ? 0.18f : -0.18f);
        if (total == 4) return (index % 2 == 0) ? 0.12f : -0.12f;
        return (index % 2 == 0 ? 1f : -1f) * (0.08f + (index % 3) * 0.04f);
    }

    /// <summary>
    /// Keeps the captives up beside the face while they choose cards and attack the claw: the raised
    /// fist breathes, squeezes and shifts in the grab_hold loop instead of freezing one grab frame.
    /// grab_player reaches the same hold pose from 1.90s, so the blend in is seamless.
    /// </summary>
    public void HoldGrabAnim()
    {
        string hold = (_isRightGrab ? "grab_hold_right" : "grab_hold") + (_isAngry ? "_angry" : "");
        foreach (MegaSprite? sprite in new[] { _bodyController, _armsController })
        {
            MegaAnimationState? state = sprite?.TryGetAnimationState();
            if (state == null || !HasBossAnimation(sprite, hold)) continue;
            state.SetAnimation(hold, true, MainTrack);
            using var scope = TrackEntryScope(state.GetCurrent(MainTrack), out MegaTrackEntry? entry);
            entry?.SetMixDuration(0.35f);
        }
        Log.Info($"[CaveGodBackground] Holding captives in '{hold}'.");
    }

    /// <summary>
    /// Plays the dedicated slam animation (grab_slam / grab_slam_angry) when captives are not freed in time.
    /// The fist hauls the captives up beside the head, then plunges them into the platform at t = 0.75s.
    /// grab_slam starts from the grab_hold rest pose; a short blend absorbs the loop's breathing phase.
    /// </summary>
    public void ResumeSlamAnim()
    {
        _isSlamDescending = false;
        SetTimeScaleBoth(1.0f);
        string slamAnim = _isAngry
            ? (_isRightGrab ? "grab_slam_right_angry" : "grab_slam_angry")
            : (_isRightGrab ? "grab_slam_right" : "grab_slam");
        PrepareContactAim(slamAnim);
        SetTrackAnimationBoth(slamAnim, loop: false, MainTrack);

        using (TrackEntryScope(_bodyController?.TryGetAnimationState()?.GetCurrent(MainTrack), out MegaTrackEntry? entryBody)) entryBody?.SetMixDuration(0.15f);
        using (TrackEntryScope(_armsController?.TryGetAnimationState()?.GetCurrent(MainTrack), out MegaTrackEntry? entryArms)) entryArms?.SetMixDuration(0.15f);

        string idleAnim = _isAngry ? "idle_front_angry" : "idle_front";
        AddTrackAnimationBoth(idleAnim, delay: 0f, loop: true, MainTrack);
        Log.Info($"[CaveGodBackground] Triggered dedicated slam animation '{slamAnim}' (0.15s blend from the hold loop).");
    }

    public Marker2D? GetStolenCardPos(bool isLeft)
    {
        if (isLeft)
        {
            return _stolenCardPosLeft ??= GetNodeOrNull<Marker2D>("%StolenCardPosLeft")
                ?? GetParent()?.GetNodeOrNull<Marker2D>("%StolenCardPosLeft")
                ?? GetParent()?.GetNodeOrNull<Marker2D>("StolenCardPosLeft");
        }
        else
        {
            return _stolenCardPosRight ??= GetNodeOrNull<Marker2D>("%StolenCardPosRight")
                ?? GetParent()?.GetNodeOrNull<Marker2D>("%StolenCardPosRight")
                ?? GetParent()?.GetNodeOrNull<Marker2D>("StolenCardPosRight");
        }
    }

    private const int MaxVisibleStolenCardsPerHand = 8;
    private const float MaxStolenCardFanHalfWidth = 96f;
    private const float MaxStolenCardFanArcY = 22f;
    private const float MaxStolenCardFanTilt = 0.32f;

    public void AttachStolenCard(bool isLeft, CardModel card)
    {
        if (_presentationEnded) return;
        if (isLeft)
            _leftStolenCardCount++;
        else
            _rightStolenCardCount++;

        Marker2D? target = GetStolenCardPos(isLeft);
        if (target == null)
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room != null)
            {
                NCreature? creature = room.CreatureNodes.FirstOrDefault(c =>
                    isLeft ? c.Entity?.Monster is ThingsCaveGodLeftHand : c.Entity?.Monster is ThingsCaveGodRightHand);
                target = creature?.GetSpecialNode<Marker2D>("%StolenCardPos");
            }
        }

        if (target != null && GodotObject.IsInstanceValid(target))
        {
            List<NCard> existingCards = target.GetChildren().OfType<NCard>()
                .Where(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion())
                .ToList();
            NCard? evicted = null;
            bool shouldAttachPreview = true;

            if (existingCards.Count >= MaxVisibleStolenCardsPerHand)
            {
                bool isLocalCard = card.Owner != null && LocalContext.IsMe(card.Owner);
                if (isLocalCard)
                {
                    evicted = existingCards.FirstOrDefault(c => c.Model?.Owner == null || !LocalContext.IsMe(c.Model.Owner));
                    if (evicted != null)
                        CardPreviewVisualFactory.ReturnPreviewSafely(evicted);
                    else
                        shouldAttachPreview = false;
                }
                else
                {
                    shouldAttachPreview = false;
                }
            }

            if (shouldAttachPreview)
            {
                NCard? nCard = CardPreviewVisualFactory.TryAttachPreview(target, card);
                if (nCard != null)
                {
                    var heldCards = target.GetChildren().OfType<NCard>()
                        .Where(c => c != evicted && GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion())
                        .ToArray();
                    if (heldCards.Length <= MultiplayerCompat.VanillaMaxPlayers)
                    {
                        for (int i = 0; i < heldCards.Length; i++)
                        {
                            float spread = i - (heldCards.Length - 1) * 0.5f;
                            heldCards[i].Position = new Vector2(spread * 22f, Math.Abs(spread) * 5f);
                            heldCards[i].Rotation = spread * 0.07f;
                        }
                    }
                    else
                    {
                        float maxHalfSpread = MathF.Max(1f, (heldCards.Length - 1) * 0.5f);
                        float stepX = MathF.Min(22f, MaxStolenCardFanHalfWidth / maxHalfSpread);
                        float stepY = MathF.Min(5f, MaxStolenCardFanArcY / maxHalfSpread);
                        float stepRot = MathF.Min(0.07f, MaxStolenCardFanTilt / maxHalfSpread);
                        for (int i = 0; i < heldCards.Length; i++)
                        {
                            float spread = i - (heldCards.Length - 1) * 0.5f;
                            heldCards[i].Position = new Vector2(spread * stepX, Math.Abs(spread) * stepY);
                            heldCards[i].Rotation = spread * stepRot;
                        }
                    }
                    Log.Info($"[CaveGodBackground] Attached stolen card visual '{card.Id}' to {(isLeft ? "Left" : "Right")} hand.");
                }
            }

            target.Visible = true;
            UpdateStolenCardPositions();
        }
        else
        {
            Log.Warn($"[CaveGodBackground] Could not find StolenCardPos marker for {(isLeft ? "Left" : "Right")} hand.");
        }
    }

    public void ClearStolenCard(bool isLeft)
    {
        if (isLeft)
            _leftStolenCardCount = 0;
        else
            _rightStolenCardCount = 0;

        bool exiting = !GodotObject.IsInstanceValid(this) || !IsInsideTree();
        Marker2D? target = GetStolenCardPos(isLeft);
        if (target != null && GodotObject.IsInstanceValid(target))
        {
            foreach (Node child in target.GetChildren())
            {
                CardPreviewVisualFactory.ReturnPreviewSafely(child, parentExitingTree: exiting);
            }
            target.Visible = false;
        }

        NCombatRoom? room = NCombatRoom.Instance;
        if (room != null && GodotObject.IsInstanceValid(room))
        {
            NCreature? creature = room.CreatureNodes.FirstOrDefault(c =>
                c != null && GodotObject.IsInstanceValid(c) &&
                (isLeft ? c.Entity?.Monster is ThingsCaveGodLeftHand : c.Entity?.Monster is ThingsCaveGodRightHand));
            Marker2D? creatureMarker = creature?.GetSpecialNode<Marker2D>("%StolenCardPos");
            if (creatureMarker != null && GodotObject.IsInstanceValid(creatureMarker) && creatureMarker != target)
            {
                foreach (Node child in creatureMarker.GetChildren())
                {
                    CardPreviewVisualFactory.ReturnPreviewSafely(child, parentExitingTree: exiting);
                }
                creatureMarker.Visible = false;
            }
        }
    }

    public void ClearAllStolenCards()
    {
        ClearStolenCard(true);
        ClearStolenCard(false);
    }

    public const float StolenCardScale = 0.50f;

    private Marker2D? GetHandCreatureStolenCardMarker(bool isLeft)
    {
        ref Marker2D? cached = ref (isLeft ? ref _leftHandCreatureMarker : ref _rightHandCreatureMarker);
        if (cached != null && GodotObject.IsInstanceValid(cached) && cached.IsInsideTree())
            return cached;

        cached = null;
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
            return null;

        foreach (NCreature node in room.CreatureNodes)
        {
            if (node == null || !GodotObject.IsInstanceValid(node))
                continue;
            bool match = isLeft
                ? node.Entity?.Monster is ThingsCaveGodLeftHand
                : node.Entity?.Monster is ThingsCaveGodRightHand;
            if (!match)
                continue;

            Marker2D? marker = node.GetSpecialNode<Marker2D>("%StolenCardPos");
            if (marker != null && GodotObject.IsInstanceValid(marker) && marker.IsInsideTree())
            {
                cached = marker;
                return cached;
            }
            break;
        }

        return null;
    }

    private static void ApplyStolenCardMarkerTransform(Marker2D? marker, Vector2 pos, float rot)
    {
        if (marker == null || !GodotObject.IsInstanceValid(marker) || !marker.IsInsideTree())
            return;

        marker.GlobalPosition = pos;
        marker.GlobalRotation = rot;
        marker.Scale = new Vector2(StolenCardScale, StolenCardScale);
        marker.ZIndex = StolenCardZIndex;
        marker.ZAsRelative = false;
    }

    private void UpdateStolenCardPositions()
    {
        if (_leftStolenCardCount == 0 && _rightStolenCardCount == 0)
            return;

        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded ||
            _armsController == null || !GodotObject.IsInstanceValid(_armsController.BoundObject))
        {
            return;
        }

        // 1. Left hand: bone "arm1_3" (with fallback to "fist1")
        if (_leftStolenCardCount > 0)
        {
            Transform2D? leftHand = _armsController.GetGlobalBoneTransform("arm1_3")
                ?? _armsController.GetGlobalBoneTransform("fist1");
            if (leftHand.HasValue)
            {
                // Carry the card inside the knuckles; use the full bone transform so
                // reaching, wrist rotation, and camera zoom never leave it floating behind.
                Vector2 pos = leftHand.Value * new Vector2(160f, -5f);
                float rot = leftHand.Value.X.Angle() + MathF.PI / 2 - 0.12f;
                ApplyStolenCardMarkerTransform(GetStolenCardPos(true), pos, rot);
                ApplyStolenCardMarkerTransform(GetHandCreatureStolenCardMarker(true), pos, rot);
            }
        }

        // 2. Right hand: bone "arm2_3" (with fallback to "fist2")
        if (_rightStolenCardCount > 0)
        {
            Transform2D? rightHand = _armsController.GetGlobalBoneTransform("arm2_3")
                ?? _armsController.GetGlobalBoneTransform("fist2");
            if (rightHand.HasValue)
            {
                // The right wrist has its own native bind pose; use its transform directly.
                Vector2 pos = rightHand.Value * new Vector2(160f, 5f);
                float rot = rightHand.Value.X.Angle() + MathF.PI / 2 + 0.12f;
                ApplyStolenCardMarkerTransform(GetStolenCardPos(false), pos, rot);
                ApplyStolenCardMarkerTransform(GetHandCreatureStolenCardMarker(false), pos, rot);
            }
        }
    }

    private readonly record struct BoneWorldMatrix(float A, float B, float C, float D, float WorldX, float WorldY);
    private sealed record FistBoneRig(
        GodotObject Arm1_2,
        GodotObject Arm1_3,
        GodotObject Fist1,
        GodotObject Arm2_2,
        GodotObject Arm2_3,
        GodotObject Fist2);
    private sealed record FistMesh(Vector2[] Uvs, Godot.Collections.Array Polygons, CaveGodFistOverlayData.VertexWeight[][] Weights);

    private static FistMesh? _leftFistMesh;
    private static FistMesh? _rightFistMesh;
    private static Texture2D? _cachedFistAtlasTexture;

    private void HideFrontFistOverlay()
    {
        if (_frontFistOverlay != null && GodotObject.IsInstanceValid(_frontFistOverlay))
        {
            _frontFistOverlay.Visible = false;
        }
    }

    private void UpdateFrontFistOverlay()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (_frontFistOverlay == null || !GodotObject.IsInstanceValid(_frontFistOverlay))
            return;

        if (_presentationEnded || _isRetreating || !_isGrabTracking || _capturedPlayers.Count == 0 ||
            _armsNode == null || _armsController == null)
        {
            HideFrontFistOverlay();
            return;
        }

        string clip = _armsMainAnimation;
        float trackTime = 0f;
        if (_armsMainEntry != null && !string.IsNullOrEmpty(clip))
        {
            trackTime = _armsMainEntry.GetTrackTime();
        }
        else
        {
            using var scope = TrackEntryScope(_armsController.TryGetAnimationState()?.GetCurrent(MainTrack), out MegaTrackEntry? entry);
            if (entry != null)
            {
                clip = entry.GetAnimationName();
                trackTime = entry.GetTrackTime();
            }
        }

        if (!clip.StartsWith("grab_", StringComparison.Ordinal) ||
            (clip.StartsWith("grab_player", StringComparison.Ordinal) && (trackTime < 1.08f || trackTime >= 3.60f)) ||
            (clip.StartsWith("grab_slam", StringComparison.Ordinal) && trackTime >= 0.92f))
        {
            HideFrontFistOverlay();
            return;
        }

        Texture2D? atlasTex = EnsureFistAtlasTexture();
        FistBoneRig? rig = EnsureArmsFistRig();
        if (atlasTex == null || rig == null)
        {
            HideFrontFistOverlay();
            return;
        }

        bool right = _isRightGrab;
        FistMesh mesh = GetBakedFistMesh(right);
        BoneWorldMatrix m0 = ReadBoneWorldMatrix(right ? rig.Arm2_2 : rig.Arm1_2);
        BoneWorldMatrix m1 = ReadBoneWorldMatrix(right ? rig.Arm2_3 : rig.Arm1_3);
        BoneWorldMatrix m2 = ReadBoneWorldMatrix(right ? rig.Fist2 : rig.Fist1);

        int vertexCount = mesh.Weights.Length;
        Vector2[] buffer;
        if (right)
        {
            if (_rightFistBuffer == null || _rightFistBuffer.Length != vertexCount)
                _rightFistBuffer = new Vector2[vertexCount];
            buffer = _rightFistBuffer;
        }
        else
        {
            if (_leftFistBuffer == null || _leftFistBuffer.Length != vertexCount)
                _leftFistBuffer = new Vector2[vertexCount];
            buffer = _leftFistBuffer;
        }

        Transform2D armsToLocal = GetGlobalTransform().AffineInverse() * _armsNode.GetGlobalTransform();
        for (int v = 0; v < vertexCount; v++)
        {
            float wx = 0f;
            float wy = 0f;
            CaveGodFistOverlayData.VertexWeight[] vw = mesh.Weights[v];
            for (int k = 0; k < vw.Length; k++)
            {
                CaveGodFistOverlayData.VertexWeight w = vw[k];
                BoneWorldMatrix bm = w.LocalBone switch
                {
                    0 => m0,
                    1 => m1,
                    _ => m2
                };
                wx += (w.Vx * bm.A + w.Vy * bm.B + bm.WorldX) * w.Weight;
                wy += (w.Vx * bm.C + w.Vy * bm.D + bm.WorldY) * w.Weight;
            }
            buffer[v] = armsToLocal * new Vector2(wx, wy);
        }

        int desiredSide = right ? 2 : 1;
        if (_activeFistOverlaySide != desiredSide || _frontFistOverlay.Texture != atlasTex)
        {
            _activeFistOverlaySide = desiredSide;
            _frontFistOverlay.Texture = atlasTex;
            _frontFistOverlay.UV = mesh.Uvs;
            _frontFistOverlay.Polygons = mesh.Polygons;
        }

        _frontFistOverlay.Polygon = buffer;
        _frontFistOverlay.Modulate = _armsNode.Modulate;
        _frontFistOverlay.ZAsRelative = false;
        _frontFistOverlay.ZIndex = FrontFistOverlayZIndex;
        _frontFistOverlay.Visible = true;
    }

    private static BoneWorldMatrix ReadBoneWorldMatrix(GodotObject bone) => new(
        bone.Call(MethodGetA).AsSingle(),
        bone.Call(MethodGetB).AsSingle(),
        bone.Call(MethodGetC).AsSingle(),
        bone.Call(MethodGetD).AsSingle(),
        bone.Call(MethodGetWorldX).AsSingle(),
        bone.Call(MethodGetWorldY).AsSingle());

    private FistBoneRig? EnsureArmsFistRig()
    {
        if (_armsFistRig != null)
            return _armsFistRig;

        GodotObject? skel = _armsController?.GetSkeleton()?.BoundObject;
        if (skel == null)
            return null;

        GodotObject? arm1_2 = skel.Call("find_bone", "arm1_2").AsGodotObject();
        GodotObject? arm1_3 = skel.Call("find_bone", "arm1_3").AsGodotObject();
        GodotObject? fist1 = skel.Call("find_bone", "fist1").AsGodotObject();
        GodotObject? arm2_2 = skel.Call("find_bone", "arm2_2").AsGodotObject();
        GodotObject? arm2_3 = skel.Call("find_bone", "arm2_3").AsGodotObject();
        GodotObject? fist2 = skel.Call("find_bone", "fist2").AsGodotObject();
        if (arm1_2 == null || arm1_3 == null || fist1 == null ||
            arm2_2 == null || arm2_3 == null || fist2 == null)
        {
            return null;
        }

        _armsFistRig = new FistBoneRig(arm1_2, arm1_3, fist1, arm2_2, arm2_3, fist2);
        return _armsFistRig;
    }

    private static Texture2D? EnsureFistAtlasTexture()
    {
        if (_cachedFistAtlasTexture != null && GodotObject.IsInstanceValid(_cachedFistAtlasTexture))
            return _cachedFistAtlasTexture;

        _cachedFistAtlasTexture = GD.Load<Texture2D>(CaveGodFistOverlayData.AtlasTexturePath);
        if (_cachedFistAtlasTexture == null || !GodotObject.IsInstanceValid(_cachedFistAtlasTexture))
        {
            _cachedFistAtlasTexture = null;
            Log.Error("[CaveGodBackground] Could not load cave_god.png texture for front fist overlay.");
            return null;
        }

        return _cachedFistAtlasTexture;
    }

    private static FistMesh GetBakedFistMesh(bool right)
    {
        if (right)
        {
            return _rightFistMesh ??= BuildBakedFistMesh(
                CaveGodFistOverlayData.RightAtlasUvs,
                CaveGodFistOverlayData.RightTriangles,
                CaveGodFistOverlayData.RightWeights);
        }

        return _leftFistMesh ??= BuildBakedFistMesh(
            CaveGodFistOverlayData.LeftAtlasUvs,
            CaveGodFistOverlayData.LeftTriangles,
            CaveGodFistOverlayData.LeftWeights);
    }

    private static FistMesh BuildBakedFistMesh(
        Vector2[] atlasUvs,
        int[] triangles,
        CaveGodFistOverlayData.VertexWeight[][] weights)
    {
        var polygons = new Godot.Collections.Array();
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            polygons.Add(Variant.From(new int[] { triangles[t], triangles[t + 1], triangles[t + 2] }));
        }

        return new FistMesh(atlasUvs, polygons, weights);
    }
}
