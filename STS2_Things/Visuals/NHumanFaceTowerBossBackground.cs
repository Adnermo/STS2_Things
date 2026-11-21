using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Monolithic 3D concave stone chamber &amp; Spine turntable controller for 人面塔 (The Visage Tower).
/// Drives Track 0 (turntable holds &amp; heavy/whip rotations), Track 1 (persistent Bastion clamp seal state),
/// Track 2 (facial attacks, wails, unbolts, rebolts, taunts, hurts, stuns), and Tracks 3..6 (persistent blind
/// rubble sockets after a face shatters), while dynamically fitting the 1920x1080 chamber across all viewport
/// aspect ratios (16:9, 16:10, 4:3, 21:9) and coaxially anchoring both the centered player floor formation and
/// the active stone face HUD.
/// </summary>
[GlobalClass]
public partial class NHumanFaceTowerBossBackground : Node2D
{
    public const int TurntableTrack = 0;
    public const int BastionSealTrack = 1;
    public const int FaceActionTrack = 2;
    public const int ShatterTrackBase = 3;
    public const float ChamberOverscanScale = 1.025f;
    public const float ChamberCenterLocalOffsetY = -58f;
    public const float FaceHudLocalOffsetY = 72f;
    public const float PlayerFloorLocalOffsetY = 266f;
    public const float MaxPlayerFloorBottomInset = 328f;
    public const float AllyVisualScale = 0.67f;

    private const float NativeAllyLocalY = 200f;
    private const int BackgroundZIndex = -20;
    private const ulong HurtAnimCooldownMs = 360;

    private static NHumanFaceTowerBossBackground? _activeInstance;

    private static readonly StringName MethodGetX = "get_x";
    private static readonly StringName MethodGetY = "get_y";

    private Node2D? _spineNode;
    private MegaSprite? _spineController;
    private GodotObject? _cameraShakeBone;
    private Vector2? _initialScenePos;
    private Control? _cachedAllyContainer;
    private bool _hasSyncedTransform;
    private Vector2 _lastViewportSize;
    private Transform2D _lastParentGlobal;
    private Vector2 _lastScenePos;
    private float _lastShakeX;
    private float _lastShakeY;
    private int _lastCreatureCount = -1;
    private int _lastAllyCount = -1;
    private bool _spineReady;
    private bool _presentationEnded;
    private ulong _lastHurtTicks;

    private int _activeSlotIndex;
    private bool _bastionBolted = true;
    private string _currentIdleAnim = "wrath_idle";
    private readonly HashSet<int> _shatteredSlots = [];
    private string? _pendingOneShotAnim;
    private float _pendingOneShotMix = 0.08f;
    private (int From, int To, bool ComboWhip)? _pendingRotation;

    public static NHumanFaceTowerBossBackground? Current =>
        _activeInstance != null && GodotObject.IsInstanceValid(_activeInstance) && _activeInstance.IsInsideTree()
            ? _activeInstance
            : null;

    public override void _Ready()
    {
        _activeInstance = this;
        CombatManager.Instance.CombatEnded += OnCombatEnded;

        if (GetParent() is CanvasItem bgRoot)
        {
            bgRoot.ZAsRelative = false;
            bgRoot.ZIndex = BackgroundZIndex;
        }

        _spineNode = GetNodeOrNull<Node2D>("%TowerWallSpine") ?? GetNodeOrNull<Node2D>("TowerWallSpine");
        if (_spineNode == null)
        {
            Log.Error("[HumanFaceTowerBackground] Missing TowerWallSpine node.");
            return;
        }

        SyncChamberTransform();
        _spineController = new MegaSprite(_spineNode);
        this.RunWhenSpineReady(_spineController, OnSpineReady);
        Callable.From(AlignStageAndPlayers).CallDeferred();
    }

    public override void _ExitTree()
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        _presentationEnded = true;
        _cameraShakeBone = null;
        _cachedAllyContainer = null;
        _hasSyncedTransform = false;
        if (_activeInstance == this)
            _activeInstance = null;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_presentationEnded)
            return;

        SyncChamberTransformInternal(force: false);
    }

    public void AlignStageAndPlayers()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;
        SyncChamberTransform();
    }

    public void SetActiveSlotImmediate(int slotIndex, string idleAnim, IEnumerable<int>? shatteredSlots = null)
    {
        _activeSlotIndex = Math.Clamp(slotIndex, 0, 3);
        _currentIdleAnim = idleAnim;
        _pendingRotation = null;
        if (shatteredSlots != null)
        {
            _shatteredSlots.Clear();
            foreach (int dead in shatteredSlots)
                _shatteredSlots.Add(Math.Clamp(dead, 0, 3));
        }

        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
            return;

        SetTrackImmediate(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0f);
        SyncBastionSealTrack(state);
        SetTrackImmediate(state, _currentIdleAnim, loop: true, FaceActionTrack, mix: 0f);
        foreach (int dead in _shatteredSlots)
            SetTrackImmediate(state, $"state_shattered_{dead}", loop: true, ShatterTrackBase + dead, mix: 0f);

        _spineController.BoundObject.Call("update_skeleton", 0f);
        SyncChamberTransform();
    }

    public void SetBastionBolted(bool bolted)
    {
        _bastionBolted = bolted;
        if (!_spineReady || _shatteredSlots.Contains(2) || _spineController?.TryGetAnimationState() is not { } state)
            return;

        SyncBastionSealTrack(state);
    }

    public float PlayRotation(int fromSlot, int toSlot, bool comboWhip, string nextIdleAnim)
    {
        int clampedFrom = Math.Clamp(fromSlot, 0, 3);
        int clampedTo = Math.Clamp(toSlot, 0, 3);
        _activeSlotIndex = clampedTo;
        _currentIdleAnim = nextIdleAnim;

        int steps = ((clampedTo - clampedFrom) % 4 + 4) % 4;
        if (steps == 0)
        {
            SetFaceIdle(nextIdleAnim, 0.12f);
            return 0.1f;
        }

        float fallbackDuration = comboWhip
            ? steps switch { 1 => 0.78f, 2 => 0.92f, _ => 1.06f }
            : steps switch { 1 => 2.20f, 2 => 2.54f, _ => 2.88f };

        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
        {
            _pendingRotation = (clampedFrom, clampedTo, comboWhip);
            return fallbackDuration;
        }

        string turnClip = comboWhip
            ? $"combo_turn_{clampedFrom}_to_{clampedTo}"
            : $"turn_{clampedFrom}_to_{clampedTo}";

        SetTrackImmediate(state, turnClip, loop: false, TurntableTrack, mix: 0.05f);
        state.AddAnimation($"hold_slot_{clampedTo}", 0f, loop: true, TurntableTrack);
        SetTrackImmediate(state, nextIdleAnim, loop: true, FaceActionTrack, mix: comboWhip ? 0.12f : 0.22f);

        float? actualDuration = state.GetCurrentAnimationDuration(TurntableTrack);
        return actualDuration is > 0.05f ? actualDuration.Value : fallbackDuration;
    }

    public void PlayFaceOneShot(string animName, string? returnIdleAnim = null, float mixDuration = 0.08f)
    {
        if (!string.IsNullOrEmpty(returnIdleAnim))
            _currentIdleAnim = returnIdleAnim;

        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
        {
            _pendingOneShotAnim = animName;
            _pendingOneShotMix = mixDuration;
            return;
        }

        SetTrackImmediate(state, animName, loop: false, FaceActionTrack, mixDuration);
        state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
    }

    public void SetFaceIdle(string idleAnim, float mixDuration = 0.15f)
    {
        _currentIdleAnim = idleAnim;
        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
            return;

        SetTrackImmediate(state, idleAnim, loop: true, FaceActionTrack, mixDuration);
    }

    public void PlayFaceHurt(int slotIndex, string returnIdleAnim)
    {
        if (slotIndex != _activeSlotIndex || _shatteredSlots.Contains(slotIndex))
            return;

        ulong now = Time.GetTicksMsec();
        if (now - _lastHurtTicks < HurtAnimCooldownMs)
            return;
        _lastHurtTicks = now;

        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
            return;

        string? playing = state.GetCurrentAnimationName(FaceActionTrack);
        if (playing != null && !playing.Contains("idle", StringComparison.Ordinal) && !playing.StartsWith("face_hurt_", StringComparison.Ordinal))
            return;

        _currentIdleAnim = returnIdleAnim;
        SetTrackImmediate(state, $"face_hurt_{Math.Clamp(slotIndex, 0, 3)}", loop: false, FaceActionTrack, mix: 0.05f);
        state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
    }

    public float PlayFaceShatter(int slotIndex)
    {
        int clamped = Math.Clamp(slotIndex, 0, 3);
        _shatteredSlots.Add(clamped);

        if (!_spineReady || _spineController?.TryGetAnimationState() is not { } state)
            return 0.85f;

        int shatterTrack = ShatterTrackBase + clamped;
        SetTrackImmediate(state, $"face_shatter_{clamped}", loop: false, shatterTrack, mix: 0.02f);
        state.AddAnimation($"state_shattered_{clamped}", 0f, loop: true, shatterTrack);
        float? duration = state.GetCurrentAnimationDuration(shatterTrack);
        return duration is > 0.05f ? duration.Value : 0.85f;
    }

    private void OnSpineReady(MegaAnimationState state)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        _spineReady = true;
        MegaSkeleton? skel = _spineController?.GetSkeleton();
        if (skel != null)
        {
            using Variant boneVar = skel.BoundObject.Call("find_bone", "camera_shake");
            if (boneVar.VariantType == Variant.Type.Object)
            {
                _cameraShakeBone = boneVar.AsGodotObject();
            }
        }

        SetTrackImmediate(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0f);
        SyncBastionSealTrack(state);
        SetTrackImmediate(state, _currentIdleAnim, loop: true, FaceActionTrack, mix: 0f);
        foreach (int dead in _shatteredSlots)
            SetTrackImmediate(state, $"state_shattered_{dead}", loop: true, ShatterTrackBase + dead, mix: 0f);

        if (_pendingRotation is { } rot)
        {
            _pendingRotation = null;
            PlayRotation(rot.From, rot.To, rot.ComboWhip, _currentIdleAnim);
        }
        else if (_pendingOneShotAnim is { } oneShot)
        {
            _pendingOneShotAnim = null;
            PlayFaceOneShot(oneShot, _currentIdleAnim, _pendingOneShotMix);
        }

        _spineController?.BoundObject.Call("update_skeleton", 0f);
        SyncChamberTransform();
    }

    public void SyncChamberTransform() => SyncChamberTransformInternal(force: true);

    private Control? GetCachedAllyContainer(Control? sceneContainer)
    {
        if (_cachedAllyContainer != null && GodotObject.IsInstanceValid(_cachedAllyContainer) && _cachedAllyContainer.IsInsideTree())
            return _cachedAllyContainer;

        _cachedAllyContainer = NCombatRoom.Instance?.GetNodeOrNull<Control>("%AllyContainer")
            ?? sceneContainer?.GetNodeOrNull<Control>("AllyContainer");
        return _cachedAllyContainer;
    }

    private void SyncChamberTransformInternal(bool force)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (GetParent() is not Control bgControl || bgControl.GetParent() is not CanvasItem bgParent)
            return;

        Vector2 viewportSize = GetViewportRect().Size;
        if (viewportSize.X < 1f || viewportSize.Y < 1f)
            viewportSize = new Vector2(1920f, 1080f);

        NCombatRoom? room = NCombatRoom.Instance;
        Control? sceneContainer = room?.SceneContainer
            ?? bgParent.GetParent() as Control;

        Vector2 scenePos = sceneContainer?.Position ?? Vector2.Zero;
        Vector2 nativeShake = Vector2.Zero;
        if (sceneContainer != null)
        {
            _initialScenePos ??= scenePos;
            Vector2 deltaPos = scenePos - _initialScenePos.Value;
            if (deltaPos.LengthSquared() <= 4096f)
                nativeShake = deltaPos;
        }

        float shakeX = 0f;
        float shakeY = 0f;
        if (!_presentationEnded && _cameraShakeBone != null && GodotObject.IsInstanceValid(_cameraShakeBone))
        {
            shakeX = _cameraShakeBone.Call(MethodGetX).AsSingle();
            shakeY = _cameraShakeBone.Call(MethodGetY).AsSingle();
        }

        Transform2D parentGlobal = bgParent.GetGlobalTransform();
        Control? allyContainer = GetCachedAllyContainer(sceneContainer);
        int creatureCount = -1;
        if (room != null)
        {
            if (!room.CreatureNodes.TryGetNonEnumeratedCount(out creatureCount))
            {
                creatureCount = 0;
                foreach (var _ in room.CreatureNodes)
                    creatureCount++;
            }
        }
        int allyCount = allyContainer?.GetChildCount() ?? -1;

        if (!force &&
            _hasSyncedTransform &&
            viewportSize == _lastViewportSize &&
            parentGlobal == _lastParentGlobal &&
            scenePos == _lastScenePos &&
            shakeX == _lastShakeX &&
            shakeY == _lastShakeY &&
            creatureCount == _lastCreatureCount &&
            allyCount == _lastAllyCount)
        {
            return;
        }

        _hasSyncedTransform = true;
        _lastViewportSize = viewportSize;
        _lastParentGlobal = parentGlobal;
        _lastScenePos = scenePos;
        _lastShakeX = shakeX;
        _lastShakeY = shakeY;
        _lastCreatureCount = creatureCount;
        _lastAllyCount = allyCount;

        float targetGlobalScale = MathF.Max(viewportSize.X / 1920f, viewportSize.Y / 1080f) * ChamberOverscanScale;
        Vector2 baseChamberCenterGlobal = viewportSize * 0.5f
            + new Vector2(0f, ChamberCenterLocalOffsetY * targetGlobalScale)
            + nativeShake;
        Vector2 shakenChamberCenterGlobal = baseChamberCenterGlobal + new Vector2(shakeX, -shakeY) * targetGlobalScale;

        Vector2 parentScale = parentGlobal.Scale.Abs();
        if (parentScale.X > 0.001f && parentScale.Y > 0.001f)
        {
            bgControl.PivotOffset = Vector2.Zero;
            bgControl.Scale = new Vector2(targetGlobalScale / parentScale.X, targetGlobalScale / parentScale.Y);
            bgControl.Position = parentGlobal.AffineInverse() * shakenChamberCenterGlobal;
        }

        SyncStageAndCreatureAnchors(viewportSize, sceneContainer, allyContainer, baseChamberCenterGlobal, shakenChamberCenterGlobal, targetGlobalScale);
    }

    private static void SyncStageAndCreatureAnchors(
        Vector2 viewportSize,
        Control? sceneContainer,
        Control? allyContainer,
        Vector2 baseChamberCenterGlobal,
        Vector2 shakenChamberCenterGlobal,
        float targetGlobalScale)
    {
        if (allyContainer != null && GodotObject.IsInstanceValid(allyContainer))
        {
            ApplyMonumentalScaleToAllies(allyContainer);
            if (allyContainer.GetParent() is CanvasItem allyParent)
            {
                float maxFloorY = MathF.Max(baseChamberCenterGlobal.Y + 120f, viewportSize.Y - MaxPlayerFloorBottomInset);
                float floorGlobalY = MathF.Min(baseChamberCenterGlobal.Y + PlayerFloorLocalOffsetY * targetGlobalScale, maxFloorY);
                Vector2 desiredFloorGlobal = new Vector2(baseChamberCenterGlobal.X, floorGlobalY);
                Vector2 floorInParent = allyParent.GetGlobalTransform().AffineInverse() * desiredFloorGlobal;
                Vector2 desiredAllyPos = new Vector2(floorInParent.X, floorInParent.Y - NativeAllyLocalY * allyContainer.Scale.Y);
                if (allyContainer.Position.DistanceSquaredTo(desiredAllyPos) > 0.01f)
                    allyContainer.Position = desiredAllyPos;
            }
        }

        Vector2 desiredFaceHudGlobal = shakenChamberCenterGlobal + new Vector2(0f, FaceHudLocalOffsetY * targetGlobalScale);
        if (NCombatRoom.Instance is { } room)
        {
            foreach (NCreature node in room.CreatureNodes)
            {
                if (GodotObject.IsInstanceValid(node) && node.Entity?.Monster is ThingsHumanFaceTowerFace)
                    node.GlobalPosition = desiredFaceHudGlobal;
            }
        }
        else if (sceneContainer != null)
        {
            SyncContainerFaceNodes(sceneContainer, desiredFaceHudGlobal);
            if (sceneContainer.GetNodeOrNull("EnemyContainer") is Node enemyContainer)
                SyncContainerFaceNodes(enemyContainer, desiredFaceHudGlobal);
        }
    }

    private static void ApplyMonumentalScaleToAllies(Node allyContainer)
    {
        int count = allyContainer.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (allyContainer.GetChild(i) is not NCreature ally || ally.Visuals == null || !GodotObject.IsInstanceValid(ally))
                continue;

            if (MathF.Abs(ally.Visuals.DefaultScale - AllyVisualScale) <= 0.001f)
                continue;

            bool untouchedScale = (ally.Visuals.Scale - Vector2.One).LengthSquared() < 0.001f;
            if (untouchedScale)
                ally.SetScaleAndHue(AllyVisualScale, 0f);
            ally.SetDefaultScaleTo(AllyVisualScale, 0f);
        }
    }

    private static void SyncContainerFaceNodes(Node container, Vector2 desiredFaceHudGlobal)
    {
        int count = container.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (container.GetChild(i) is NCreature creature && creature.Entity?.Monster is ThingsHumanFaceTowerFace)
                creature.GlobalPosition = desiredFaceHudGlobal;
        }
    }

    private void SyncBastionSealTrack(MegaAnimationState state)
    {
        if (_shatteredSlots.Contains(2))
            return;

        SetTrackImmediate(
            state,
            _bastionBolted ? "state_bastion_bolted" : "state_bastion_unbolted",
            loop: true,
            BastionSealTrack,
            mix: 0f);
    }

    private static void SetTrackImmediate(MegaAnimationState state, string animName, bool loop, int trackId, float mix)
    {
        state.SetAnimation(animName, loop, trackId);
        using (TrackEntryScope(state.GetCurrent(trackId), out MegaTrackEntry? entry))
        {
            entry?.SetMixDuration(mix);
        }
    }

    private void OnCombatEnded(CombatRoom room)
    {
        if (room.Encounter is not ThingsHumanFaceTowerBossEncounter)
            return;
        _presentationEnded = true;
        _cameraShakeBone = null;
        if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            SyncChamberTransform();
    }
}
