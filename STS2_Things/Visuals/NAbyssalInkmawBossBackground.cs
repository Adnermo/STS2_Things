using System;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Full-screen subterranean lake &amp; ancient stone bridge Spine stage controller for
/// 渊墨巨蛸 (Abyssal Inkmaw). Manages all 5 Spine 4.2.0 rigs in true depth order:
///   1. Wide subterranean cavern lake (Layer_00)
///   2. Colossal half-submerged Kraken body + 6 misty lake tentacles (BodySpine)
///   3. Ancient weathered dead-flat monolithic stone bridge deck (BridgePlate)
///   4. Water-emerging hero tentacles mounted above BridgePlate with meniscus-hugging emerge_clip so
///      their submerged trunks stay cleanly clipped in idle while over-curb slams land unbroken on deck
///      (Left/RightWaterTentacleSpine)
///   5. Phase 2 floor-breach hero tentacles &amp; outer bridge small tendril variants
///      (Left/RightFloorTentacleSpine, OuterLeft/OuterRightSmallTentacleSpine)
///   6. Foreground off-screen cylindrical hydro-ink cannon beam elevated to ZIndex 2 above the centered player (BeamOverlay)
/// </summary>
[GlobalClass]
public partial class NAbyssalInkmawBossBackground : Node2D
{
    public const int ActionTrack = 0;
    public const int BaseModeTrack = 1;
    public const int VariantSkinTrack = 2;

    public enum BossStance
    {
        Normal = 0,
        Charging = 1,
        Stunned = 2,
    }

    public const float ChamberOverscanScale = 1.022f;
    public const float PlateNominalWidth = 2304f;
    public const float PlateNominalHeight = 1080f;

    public const float PlayerFloorLocalOffsetY = 212f;
    public const float MaxPlayerFloorBottomInset = 322f;
    public const float AllyVisualScale = 0.67f;

    public static readonly Vector2 BodyRigPos = new(0f, -24f);
    public static readonly Vector2 BossHudLocalOffset = new(0f, -14f);
    public static readonly Vector2 LeftWaterTentaclePos = new(-470f, 32f);
    public static readonly Vector2 RightWaterTentaclePos = new(470f, 32f);
    public static readonly Vector2 LeftFloorTentaclePos = new(-475f, 152f);
    public static readonly Vector2 RightFloorTentaclePos = new(475f, 152f);
    public static readonly Vector2 OuterLeftSmallTentaclePos = new(-735f, 182f);
    public static readonly Vector2 OuterRightSmallTentaclePos = new(735f, 182f);

    public static readonly Vector2 LeftWaterHudLocalOffset = new(-470f, 82f);
    public static readonly Vector2 RightWaterHudLocalOffset = new(470f, 82f);
    public static readonly Vector2 LeftFloorHudLocalOffset = new(-475f, 166f);
    public static readonly Vector2 RightFloorHudLocalOffset = new(475f, 166f);
    public static readonly Vector2 OuterLeftSmallHudLocalOffset = new(-735f, 198f);
    public static readonly Vector2 OuterRightSmallHudLocalOffset = new(735f, 198f);

    private const float NativeAllyLocalY = 200f;
    private const int BackgroundZIndex = -20;
    private const int ForegroundBeamZIndex = 2;
    private const ulong HurtAnimCooldownMs = 340;

    private static NAbyssalInkmawBossBackground? _activeInstance;

    private Node2D? _bodyNode;
    private Node2D? _beamOverlay;
    private Node2D? _beamNode;
    private Node2D? _leftWaterNode;
    private Node2D? _rightWaterNode;
    private Node2D? _leftFloorNode;
    private Node2D? _rightFloorNode;
    private Node2D? _outerLeftSmallNode;
    private Node2D? _outerRightSmallNode;

    private MegaSprite? _bodySprite;
    private MegaSprite? _beamSprite;
    private MegaSprite? _leftWaterSprite;
    private MegaSprite? _rightWaterSprite;
    private MegaSprite? _leftFloorSprite;
    private MegaSprite? _rightFloorSprite;
    private MegaSprite? _outerLeftSmallSprite;
    private MegaSprite? _outerRightSmallSprite;

    private Callable _bodyEventCallable;
    private Callable _beamEventCallable;
    private Callable _leftTentacleEventCallable;
    private Callable _rightTentacleEventCallable;
    private Callable _smallTentacleEventCallable;
    private bool _bodyEventConnected;
    private bool _beamEventConnected;
    private bool _leftWaterEventConnected;
    private bool _leftFloorEventConnected;
    private bool _rightWaterEventConnected;
    private bool _rightFloorEventConnected;
    private bool _outerLeftSmallEventConnected;
    private bool _outerRightSmallEventConnected;

    private static readonly StringName MethodGetX = "get_x";
    private static readonly StringName MethodGetY = "get_y";

    private GodotObject? _cameraShakeBone;
    private Vector2? _initialScenePos;
    private Control? _cachedAllyContainer;
    private bool _hasSyncedTransform;
    private Vector2 _lastViewportSize;
    private Transform2D _lastParentGlobal;
    private Vector2 _lastScenePos;
    private float _lastShakeX;
    private float _lastShakeY;
    private bool _lastLeftFloorBreach;
    private bool _lastRightFloorBreach;
    private int _lastCreatureCount = -1;
    private int _lastAllyCount = -1;
    private bool _beamPlaying;
    private int _beamPlayGeneration;
    private bool _bodyReady;
    private bool _beamReady;
    private bool _leftWaterReady;
    private bool _rightWaterReady;
    private bool _leftFloorReady;
    private bool _rightFloorReady;
    private bool _outerLeftSmallReady;
    private bool _outerRightSmallReady;
    private bool _presentationEnded;

    private bool _isPhase2;
    private bool _bossDead;
    private BossStance _bossStance;
    private bool _leftAlive = true;
    private bool _rightAlive = true;
    private bool _leftStunned;
    private bool _rightStunned;
    private bool _leftFloorBreach;
    private bool _rightFloorBreach;
    private bool _outerLeftSmallAlive;
    private bool _outerRightSmallAlive;
    private bool _outerLeftSmallStunned;
    private bool _outerRightSmallStunned;
    private string _outerLeftSmallSkin = "skin_spike";
    private string _outerRightSmallSkin = "skin_shell";
    private bool _outerLeftSmallPendingEmerge;
    private bool _outerRightSmallPendingEmerge;
    private ulong _lastBossHurtTicks;
    private ulong _lastLeftHurtTicks;
    private ulong _lastRightHurtTicks;
    private ulong _lastOuterLeftSmallHurtTicks;
    private ulong _lastOuterRightSmallHurtTicks;

    public static NAbyssalInkmawBossBackground? Current =>
        _activeInstance != null && GodotObject.IsInstanceValid(_activeInstance) && _activeInstance.IsInsideTree()
            ? _activeInstance
            : null;

    public bool LeftIsFloorBreach => _leftFloorBreach;
    public bool RightIsFloorBreach => _rightFloorBreach;

    public override void _Ready()
    {
        _activeInstance = this;
        CombatManager.Instance.CombatEnded += OnCombatEnded;

        if (GetParent() is CanvasItem bgRoot)
        {
            bgRoot.ZAsRelative = false;
            bgRoot.ZIndex = BackgroundZIndex;
        }

        _bodyNode = GetNodeOrNull<Node2D>("%BodySpine") ?? GetNodeOrNull<Node2D>("BodySpine");
        _leftWaterNode = GetNodeOrNull<Node2D>("%LeftWaterTentacleSpine") ?? GetNodeOrNull<Node2D>("LeftWaterTentacleSpine");
        _rightWaterNode = GetNodeOrNull<Node2D>("%RightWaterTentacleSpine") ?? GetNodeOrNull<Node2D>("RightWaterTentacleSpine");
        _leftFloorNode = GetNodeOrNull<Node2D>("%LeftFloorTentacleSpine") ?? GetNodeOrNull<Node2D>("LeftFloorTentacleSpine");
        _rightFloorNode = GetNodeOrNull<Node2D>("%RightFloorTentacleSpine") ?? GetNodeOrNull<Node2D>("RightFloorTentacleSpine");
        _outerLeftSmallNode = GetNodeOrNull<Node2D>("%OuterLeftSmallTentacleSpine") ?? GetNodeOrNull<Node2D>("OuterLeftSmallTentacleSpine");
        _outerRightSmallNode = GetNodeOrNull<Node2D>("%OuterRightSmallTentacleSpine") ?? GetNodeOrNull<Node2D>("OuterRightSmallTentacleSpine");
        _beamOverlay = GetNodeOrNull<Node2D>("%BeamOverlay") ?? GetNodeOrNull<Node2D>("BeamOverlay");
        _beamNode = GetNodeOrNull<Node2D>("%BeamSpine") ?? _beamOverlay?.GetNodeOrNull<Node2D>("BeamSpine");

        if (_bodyNode != null) _bodyNode.Position = BodyRigPos;
        if (_beamOverlay != null)
        {
            _beamOverlay.Position = BodyRigPos;
            _beamOverlay.ZAsRelative = false;
            _beamOverlay.ZIndex = ForegroundBeamZIndex;
        }
        if (_beamNode != null)
        {
            _beamNode.Visible = false;
        }

        if (_leftWaterNode != null) _leftWaterNode.Position = LeftWaterTentaclePos;
        if (_rightWaterNode != null) _rightWaterNode.Position = RightWaterTentaclePos;
        if (_leftFloorNode != null)
        {
            _leftFloorNode.Position = LeftFloorTentaclePos;
            _leftFloorNode.Visible = false;
        }
        if (_rightFloorNode != null)
        {
            _rightFloorNode.Position = RightFloorTentaclePos;
            _rightFloorNode.Visible = false;
        }
        if (_outerLeftSmallNode != null)
        {
            _outerLeftSmallNode.Position = OuterLeftSmallTentaclePos;
            _outerLeftSmallNode.Visible = false;
        }
        if (_outerRightSmallNode != null)
        {
            _outerRightSmallNode.Position = OuterRightSmallTentaclePos;
            _outerRightSmallNode.Scale = new Vector2(-1f, 1f);
            _outerRightSmallNode.Visible = false;
        }

        _bodyEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnBodyAnimationEvent);
        _beamEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnBeamAnimationEvent);
        _leftTentacleEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnTentacleAnimationEvent);
        _rightTentacleEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnTentacleAnimationEvent);
        _smallTentacleEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnSmallTentacleAnimationEvent);

        SyncChamberTransform();

        if (_bodyNode != null)
        {
            _bodySprite = new MegaSprite(_bodyNode);
            this.RunWhenSpineReady(_bodySprite, OnBodySpineReady);
        }
        else
        {
            Log.Error("[AbyssalInkmawBackground] Missing BodySpine node.");
        }

        if (_beamNode != null)
        {
            _beamSprite = new MegaSprite(_beamNode);
            this.RunWhenSpineReady(_beamSprite, OnBeamSpineReady);
        }

        if (_leftWaterNode != null)
        {
            _leftWaterSprite = new MegaSprite(_leftWaterNode);
            this.RunWhenSpineReady(_leftWaterSprite, state => OnTentacleSpineReady(state, isLeft: true, isFloor: false));
        }

        if (_rightWaterNode != null)
        {
            _rightWaterSprite = new MegaSprite(_rightWaterNode);
            this.RunWhenSpineReady(_rightWaterSprite, state => OnTentacleSpineReady(state, isLeft: false, isFloor: false));
        }

        if (_leftFloorNode != null)
        {
            _leftFloorSprite = new MegaSprite(_leftFloorNode);
            this.RunWhenSpineReady(_leftFloorSprite, state => OnTentacleSpineReady(state, isLeft: true, isFloor: true));
        }

        if (_rightFloorNode != null)
        {
            _rightFloorSprite = new MegaSprite(_rightFloorNode);
            this.RunWhenSpineReady(_rightFloorSprite, state => OnTentacleSpineReady(state, isLeft: false, isFloor: true));
        }

        if (_outerLeftSmallNode != null)
        {
            _outerLeftSmallSprite = new MegaSprite(_outerLeftSmallNode);
            this.RunWhenSpineReady(_outerLeftSmallSprite, state => OnSmallTentacleSpineReady(state, isOuterLeft: true));
        }

        if (_outerRightSmallNode != null)
        {
            _outerRightSmallSprite = new MegaSprite(_outerRightSmallNode);
            this.RunWhenSpineReady(_outerRightSmallSprite, state => OnSmallTentacleSpineReady(state, isOuterLeft: false));
        }

        Callable.From(AlignStageAndPlayers).CallDeferred();
    }

    public override void _ExitTree()
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        DisconnectEvent(_bodyNode, _bodyEventCallable, ref _bodyEventConnected);
        DisconnectEvent(_beamNode, _beamEventCallable, ref _beamEventConnected);
        DisconnectEvent(_leftWaterNode, _leftTentacleEventCallable, ref _leftWaterEventConnected);
        DisconnectEvent(_leftFloorNode, _leftTentacleEventCallable, ref _leftFloorEventConnected);
        DisconnectEvent(_rightWaterNode, _rightTentacleEventCallable, ref _rightWaterEventConnected);
        DisconnectEvent(_rightFloorNode, _rightTentacleEventCallable, ref _rightFloorEventConnected);
        DisconnectEvent(_outerLeftSmallNode, _smallTentacleEventCallable, ref _outerLeftSmallEventConnected);
        DisconnectEvent(_outerRightSmallNode, _smallTentacleEventCallable, ref _outerRightSmallEventConnected);
        _presentationEnded = true;
        _cameraShakeBone = null;
        _cachedAllyContainer = null;
        _hasSyncedTransform = false;
        _beamPlayGeneration++;
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

    public void SetBossStance(BossStance stance)
    {
        if (_bossStance == stance)
            return;

        _bossStance = stance;
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            string? playing = bodyState.GetCurrentAnimationName(ActionTrack);
            if (playing == null || playing.EndsWith("_loop", StringComparison.Ordinal))
            {
                SetTrackImmediate(bodyState, CurrentBossIdleAnim, loop: true, ActionTrack, mix: 0.18f);
            }
            else
            {
                bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
            }
        }
    }

    public void PlayBossChargePulse()
    {
        _bossStance = BossStance.Charging;
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "charge_pulse", loop: false, ActionTrack, mix: 0.10f);
            bodyState.AddAnimation("charge_loop", 0f, loop: true, ActionTrack);
        }
    }

    public void PlayBossPressurePulse()
    {
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "charge_pulse", loop: false, ActionTrack, mix: 0.10f);
            bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
        }
    }

    public void PlayInkSpray()
    {
        _bossStance = BossStance.Normal;
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "ink_spray", loop: false, ActionTrack, mix: 0.08f);
            bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
        }

        if (_beamReady && _beamSprite?.TryGetAnimationState() is { } beamState)
        {
            SetBeamActive(true);
            SetTrackImmediate(beamState, "ink_spray", loop: false, ActionTrack, mix: 0f);
            beamState.AddAnimation("idle_hidden", 0f, loop: true, ActionTrack);
            _beamSprite.BoundObject.Call("update_skeleton", 0f);
            float duration = beamState.GetCurrentAnimationDuration(ActionTrack) is { } d && d > 0.1f ? d : 2.05f;
            int generation = ++_beamPlayGeneration;
            GetTree().CreateTimer(duration + 0.12f).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _beamPlayGeneration != generation)
                    return;
                SetBeamActive(false);
            };
        }
    }

    public void PlayTidalCrash()
    {
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "tidal_crash", loop: false, ActionTrack, mix: 0.10f);
            bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
        }
    }

    public void PlaySummonTentacle()
    {
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "summon_tentacle", loop: false, ActionTrack, mix: 0.10f);
            bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
        }
    }

    public void PlayPhase2Surge()
    {
        _isPhase2 = true;
        _bossStance = BossStance.Normal;
        if (_bossDead || _presentationEnded)
            return;

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "phase2_surge", loop: false, ActionTrack, mix: 0.12f);
            bodyState.AddAnimation("phase2_idle_loop", 0f, loop: true, ActionTrack);
        }
    }

    public void PlayBossHurt()
    {
        if (_bossDead || _presentationEnded)
            return;

        ulong now = Time.GetTicksMsec();
        if (now - _lastBossHurtTicks < HurtAnimCooldownMs)
            return;
        _lastBossHurtTicks = now;

        if (!_bodyReady || _bodySprite?.TryGetAnimationState() is not { } bodyState)
            return;

        string? playing = bodyState.GetCurrentAnimationName(ActionTrack);
        if (playing != null && !playing.EndsWith("_loop", StringComparison.Ordinal) && playing != "hurt")
            return;

        SetTrackImmediate(bodyState, "hurt", loop: false, ActionTrack, mix: 0.05f);
        bodyState.AddAnimation(CurrentBossIdleAnim, 0f, loop: true, ActionTrack);
    }

    public void PlayBossDie()
    {
        _bossDead = true;
        _beamPlayGeneration++;
        if (_beamReady && _beamSprite?.TryGetAnimationState() is { } beamState)
        {
            SetTrackImmediate(beamState, "idle_hidden", loop: true, ActionTrack, mix: 0.05f);
            _beamSprite.BoundObject.Call("update_skeleton", 0f);
        }
        SetBeamActive(false);

        if (_bodyReady && _bodySprite?.TryGetAnimationState() is { } bodyState)
        {
            SetTrackImmediate(bodyState, "die", loop: false, ActionTrack, mix: 0.08f);
        }
    }

    public void SyncTentaclePresence(bool isLeft, bool alive, bool floorBreach, bool playEmerge)
    {
        if (isLeft)
        {
            _leftAlive = alive;
            _leftFloorBreach = floorBreach;
        }
        else
        {
            _rightAlive = alive;
            _rightFloorBreach = floorBreach;
        }

        UpdateTentacleVisibility(isLeft);
        if (!alive)
            return;

        MegaSprite? sprite = GetActiveTentacleSprite(isLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        string steadyBase = floorBreach ? "base_floor" : "base_water";
        string burstBase = floorBreach ? "base_floor_burst" : "base_water_burst";

        if (playEmerge)
        {
            SetTrackImmediate(state, "emerge", loop: false, ActionTrack, mix: 0f);
            state.AddAnimation("idle_loop", 0f, loop: true, ActionTrack);
            SetTrackImmediate(state, burstBase, loop: false, BaseModeTrack, mix: 0f);
            state.AddAnimation(steadyBase, 0f, loop: true, BaseModeTrack);
        }
        else
        {
            SetTrackImmediate(state, "idle_loop", loop: true, ActionTrack, mix: 0f);
            SetTrackImmediate(state, steadyBase, loop: true, BaseModeTrack, mix: 0f);
        }

        sprite.BoundObject.Call("update_skeleton", 0f);
        SyncChamberTransform();
    }

    public void PlayTentacleAction(bool isLeft, string actionAnim, bool triggerBaseBurst)
    {
        if (_presentationEnded)
            return;

        bool alive = isLeft ? _leftAlive : _rightAlive;
        if (!alive)
            return;

        MegaSprite? sprite = GetActiveTentacleSprite(isLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        SetTrackImmediate(state, actionAnim, loop: false, ActionTrack, mix: 0.08f);
        state.AddAnimation("idle_loop", 0f, loop: true, ActionTrack);

        if (triggerBaseBurst)
        {
            bool floorBreach = isLeft ? _leftFloorBreach : _rightFloorBreach;
            string burstBase = floorBreach ? "base_floor_burst" : "base_water_burst";
            string steadyBase = floorBreach ? "base_floor" : "base_water";
            SetTrackImmediate(state, burstBase, loop: false, BaseModeTrack, mix: 0.05f);
            state.AddAnimation(steadyBase, 0f, loop: true, BaseModeTrack);
        }
    }

    public void PlayTentacleChargeBuff(bool isLeft)
    {
        PlayTentacleAction(isLeft, "charge_buff", triggerBaseBurst: false);
    }

    public void SetTentacleStunned(bool isLeft, bool stunned)
    {
        if (isLeft)
            _leftStunned = stunned;
        else
            _rightStunned = stunned;

        if (_presentationEnded)
            return;

        bool alive = isLeft ? _leftAlive : _rightAlive;
        if (!alive)
            return;

        MegaSprite? sprite = GetActiveTentacleSprite(isLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        string targetLoop = stunned ? "stun_loop" : "idle_loop";
        string? playing = state.GetCurrentAnimationName(ActionTrack);
        if (playing == null || playing.EndsWith("_loop", StringComparison.Ordinal))
        {
            if (playing != targetLoop)
                SetTrackImmediate(state, targetLoop, loop: true, ActionTrack, mix: 0.16f);
        }
        else
        {
            state.AddAnimation(targetLoop, 0f, loop: true, ActionTrack);
        }
    }

    public void PlayTentacleHurt(bool isLeft)
    {
        if (_presentationEnded)
            return;

        bool alive = isLeft ? _leftAlive : _rightAlive;
        if (!alive)
            return;

        ulong now = Time.GetTicksMsec();
        ref ulong lastTicks = ref (isLeft ? ref _lastLeftHurtTicks : ref _lastRightHurtTicks);
        if (now - lastTicks < HurtAnimCooldownMs)
            return;
        lastTicks = now;

        MegaSprite? sprite = GetActiveTentacleSprite(isLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        string? playing = state.GetCurrentAnimationName(ActionTrack);
        if (playing != null && !playing.EndsWith("_loop", StringComparison.Ordinal) && playing != "hurt")
            return;

        bool stunned = isLeft ? _leftStunned : _rightStunned;
        SetTrackImmediate(state, "hurt", loop: false, ActionTrack, mix: 0.05f);
        state.AddAnimation(stunned ? "stun_loop" : "idle_loop", 0f, loop: true, ActionTrack);
    }

    public void PlayTentacleRetreatDie(bool isLeft)
    {
        MegaSprite? sprite = GetActiveTentacleSprite(isLeft, out bool ready);
        bool floorBreach = isLeft ? _leftFloorBreach : _rightFloorBreach;

        if (isLeft)
            _leftAlive = false;
        else
            _rightAlive = false;

        if (ready && sprite?.TryGetAnimationState() is { } state)
        {
            SetTrackImmediate(state, "retreat_die", loop: false, ActionTrack, mix: 0.06f);
            string burstBase = floorBreach ? "base_floor_burst" : "base_water_burst";
            SetTrackImmediate(state, burstBase, loop: false, BaseModeTrack, mix: 0.05f);
            GetTree().CreateTimer(1.15).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                    return;
                bool stillDead = isLeft ? !_leftAlive : !_rightAlive;
                if (stillDead)
                    UpdateTentacleVisibility(isLeft);
            };
        }
        else
        {
            UpdateTentacleVisibility(isLeft);
        }
    }

    public void SyncSmallTentaclePresence(bool isOuterLeft, bool alive, string variantSkin, bool playEmerge)
    {
        string skinAnim = NormalizeSmallSkinAnim(variantSkin);
        if (isOuterLeft)
        {
            _outerLeftSmallAlive = alive;
            _outerLeftSmallSkin = skinAnim;
            _outerLeftSmallPendingEmerge = alive && playEmerge;
        }
        else
        {
            _outerRightSmallAlive = alive;
            _outerRightSmallSkin = skinAnim;
            _outerRightSmallPendingEmerge = alive && playEmerge;
        }

        UpdateSmallTentacleVisibility(isOuterLeft);
        if (!alive)
            return;

        MegaSprite? sprite = GetSmallTentacleSprite(isOuterLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        ApplySmallTentacleState(state, isOuterLeft, skinAnim, playEmerge);
        SyncChamberTransform();
    }

    public void PlaySmallTentacleAction(bool isOuterLeft, string actionAnim)
    {
        if (_presentationEnded)
            return;

        bool alive = isOuterLeft ? _outerLeftSmallAlive : _outerRightSmallAlive;
        if (!alive)
            return;

        MegaSprite? sprite = GetSmallTentacleSprite(isOuterLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        SetTrackImmediate(state, actionAnim, loop: false, ActionTrack, mix: 0.06f);
        state.AddAnimation("idle_loop", 0f, loop: true, ActionTrack);
        SetTrackImmediate(state, "base_floor_burst", loop: false, BaseModeTrack, mix: 0.04f);
        state.AddAnimation("base_floor", 0f, loop: true, BaseModeTrack);
    }

    public void SetSmallTentacleStunned(bool isOuterLeft, bool stunned)
    {
        if (isOuterLeft)
            _outerLeftSmallStunned = stunned;
        else
            _outerRightSmallStunned = stunned;

        if (_presentationEnded)
            return;

        bool alive = isOuterLeft ? _outerLeftSmallAlive : _outerRightSmallAlive;
        if (!alive)
            return;

        MegaSprite? sprite = GetSmallTentacleSprite(isOuterLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        string targetLoop = stunned ? "stun_loop" : "idle_loop";
        string? playing = state.GetCurrentAnimationName(ActionTrack);
        if (playing == null || playing.EndsWith("_loop", StringComparison.Ordinal))
        {
            if (playing != targetLoop)
                SetTrackImmediate(state, targetLoop, loop: true, ActionTrack, mix: 0.16f);
        }
        else
        {
            state.AddAnimation(targetLoop, 0f, loop: true, ActionTrack);
        }
    }

    public void PlaySmallTentacleHurt(bool isOuterLeft)
    {
        if (_presentationEnded)
            return;

        bool alive = isOuterLeft ? _outerLeftSmallAlive : _outerRightSmallAlive;
        if (!alive)
            return;

        ulong now = Time.GetTicksMsec();
        ref ulong lastTicks = ref (isOuterLeft ? ref _lastOuterLeftSmallHurtTicks : ref _lastOuterRightSmallHurtTicks);
        if (now - lastTicks < HurtAnimCooldownMs)
            return;
        lastTicks = now;

        MegaSprite? sprite = GetSmallTentacleSprite(isOuterLeft, out bool ready);
        if (!ready || sprite?.TryGetAnimationState() is not { } state)
            return;

        string? playing = state.GetCurrentAnimationName(ActionTrack);
        if (playing != null && !playing.EndsWith("_loop", StringComparison.Ordinal) && playing != "hurt")
            return;

        bool stunned = isOuterLeft ? _outerLeftSmallStunned : _outerRightSmallStunned;
        SetTrackImmediate(state, "hurt", loop: false, ActionTrack, mix: 0.05f);
        state.AddAnimation(stunned ? "stun_loop" : "idle_loop", 0f, loop: true, ActionTrack);
    }

    public void PlaySmallTentacleRetreatDie(bool isOuterLeft)
    {
        MegaSprite? sprite = GetSmallTentacleSprite(isOuterLeft, out bool ready);
        if (isOuterLeft)
        {
            _outerLeftSmallAlive = false;
            _outerLeftSmallPendingEmerge = false;
        }
        else
        {
            _outerRightSmallAlive = false;
            _outerRightSmallPendingEmerge = false;
        }

        if (ready && sprite?.TryGetAnimationState() is { } state)
        {
            SetTrackImmediate(state, "retreat_die", loop: false, ActionTrack, mix: 0.05f);
            SetTrackImmediate(state, "base_floor_burst", loop: false, BaseModeTrack, mix: 0.04f);
            GetTree().CreateTimer(0.95).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                    return;
                bool stillDead = isOuterLeft ? !_outerLeftSmallAlive : !_outerRightSmallAlive;
                if (stillDead)
                    UpdateSmallTentacleVisibility(isOuterLeft);
            };
        }
        else
        {
            UpdateSmallTentacleVisibility(isOuterLeft);
        }
    }

    private static string NormalizeSmallSkinAnim(string variantSkin)
    {
        if (string.IsNullOrWhiteSpace(variantSkin))
            return "skin_spike";
        return variantSkin.StartsWith("skin_", StringComparison.Ordinal)
            ? variantSkin
            : $"skin_{variantSkin}";
    }

    private MegaSprite? GetSmallTentacleSprite(bool isOuterLeft, out bool ready)
    {
        if (isOuterLeft)
        {
            ready = _outerLeftSmallReady;
            return _outerLeftSmallSprite;
        }

        ready = _outerRightSmallReady;
        return _outerRightSmallSprite;
    }

    private static void SetSpineNodeActive(Node2D? node, bool visible, bool ready)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
            return;
        node.Visible = visible;
        if (visible)
            node.ProcessMode = ProcessModeEnum.Inherit;
        else if (ready)
            node.ProcessMode = ProcessModeEnum.Disabled;
    }

    private void SetBeamActive(bool active)
    {
        _beamPlaying = active;
        SetSpineNodeActive(_beamNode, active, _beamReady);
    }

    private void UpdateSmallTentacleVisibility(bool isOuterLeft)
    {
        if (isOuterLeft)
        {
            SetSpineNodeActive(_outerLeftSmallNode, _outerLeftSmallAlive, _outerLeftSmallReady);
        }
        else
        {
            SetSpineNodeActive(_outerRightSmallNode, _outerRightSmallAlive, _outerRightSmallReady);
        }
    }

    private void ApplySmallTentacleState(MegaAnimationState state, bool isOuterLeft, string skinAnim, bool playEmerge)
    {
        if (isOuterLeft)
            _outerLeftSmallPendingEmerge = false;
        else
            _outerRightSmallPendingEmerge = false;

        SetTrackImmediate(state, skinAnim, loop: true, VariantSkinTrack, mix: 0f);
        if (playEmerge)
        {
            SetTrackImmediate(state, "emerge", loop: false, ActionTrack, mix: 0f);
            state.AddAnimation("idle_loop", 0f, loop: true, ActionTrack);
            SetTrackImmediate(state, "base_floor_burst", loop: false, BaseModeTrack, mix: 0f);
            state.AddAnimation("base_floor", 0f, loop: true, BaseModeTrack);
        }
        else
        {
            SetTrackImmediate(state, "idle_loop", loop: true, ActionTrack, mix: 0f);
            SetTrackImmediate(state, "base_floor", loop: true, BaseModeTrack, mix: 0f);
        }

        MegaSprite? sprite = isOuterLeft ? _outerLeftSmallSprite : _outerRightSmallSprite;
        sprite?.BoundObject.Call("update_skeleton", 0f);
    }

    private string CurrentBossIdleAnim => _bossStance switch
    {
        BossStance.Charging => "charge_loop",
        BossStance.Stunned => "stun_loop",
        _ => _isPhase2 ? "phase2_idle_loop" : "idle_loop",
    };

    private MegaSprite? GetActiveTentacleSprite(bool isLeft, out bool ready)
    {
        bool floorBreach = isLeft ? _leftFloorBreach : _rightFloorBreach;
        if (isLeft)
        {
            ready = floorBreach ? _leftFloorReady : _leftWaterReady;
            return floorBreach ? _leftFloorSprite : _leftWaterSprite;
        }

        ready = floorBreach ? _rightFloorReady : _rightWaterReady;
        return floorBreach ? _rightFloorSprite : _rightWaterSprite;
    }

    private void UpdateTentacleVisibility(bool isLeft)
    {
        if (isLeft)
        {
            SetSpineNodeActive(_leftWaterNode, _leftAlive && !_leftFloorBreach, _leftWaterReady);
            SetSpineNodeActive(_leftFloorNode, _leftAlive && _leftFloorBreach, _leftFloorReady);
        }
        else
        {
            SetSpineNodeActive(_rightWaterNode, _rightAlive && !_rightFloorBreach, _rightWaterReady);
            SetSpineNodeActive(_rightFloorNode, _rightAlive && _rightFloorBreach, _rightFloorReady);
        }
    }

    private void OnBodySpineReady(MegaAnimationState state)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        _bodyReady = true;
        ConnectEvent(_bodyNode, _bodyEventCallable, ref _bodyEventConnected);

        MegaSkeleton? skel = _bodySprite?.GetSkeleton();
        if (skel != null)
        {
            using Variant boneVar = skel.BoundObject.Call("find_bone", "camera_shake");
            if (boneVar.VariantType == Variant.Type.Object)
                _cameraShakeBone = boneVar.AsGodotObject();
        }

        SetTrackImmediate(state, _bossDead ? "die" : CurrentBossIdleAnim, loop: !_bossDead, ActionTrack, mix: 0f);
        _bodySprite?.BoundObject.Call("update_skeleton", 0f);
        SyncChamberTransform();
    }

    private void OnBeamSpineReady(MegaAnimationState state)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        _beamReady = true;
        ConnectEvent(_beamNode, _beamEventCallable, ref _beamEventConnected);
        SetTrackImmediate(state, "idle_hidden", loop: true, ActionTrack, mix: 0f);
        _beamSprite?.BoundObject.Call("update_skeleton", 0f);
        SetBeamActive(_beamPlaying);
    }

    private void OnTentacleSpineReady(MegaAnimationState state, bool isLeft, bool isFloor)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        if (isLeft && !isFloor)
        {
            _leftWaterReady = true;
            ConnectEvent(_leftWaterNode, _leftTentacleEventCallable, ref _leftWaterEventConnected);
        }
        else if (isLeft && isFloor)
        {
            _leftFloorReady = true;
            ConnectEvent(_leftFloorNode, _leftTentacleEventCallable, ref _leftFloorEventConnected);
        }
        else if (!isLeft && !isFloor)
        {
            _rightWaterReady = true;
            ConnectEvent(_rightWaterNode, _rightTentacleEventCallable, ref _rightWaterEventConnected);
        }
        else
        {
            _rightFloorReady = true;
            ConnectEvent(_rightFloorNode, _rightTentacleEventCallable, ref _rightFloorEventConnected);
        }

        string steadyBase = isFloor ? "base_floor" : "base_water";
        SetTrackImmediate(state, "idle_loop", loop: true, ActionTrack, mix: 0f);
        SetTrackImmediate(state, steadyBase, loop: true, BaseModeTrack, mix: 0f);
        UpdateTentacleVisibility(isLeft);
    }

    private void OnSmallTentacleSpineReady(MegaAnimationState state, bool isOuterLeft)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _presentationEnded)
            return;

        if (isOuterLeft)
        {
            _outerLeftSmallReady = true;
            ConnectEvent(_outerLeftSmallNode, _smallTentacleEventCallable, ref _outerLeftSmallEventConnected);
            ApplySmallTentacleState(state, isOuterLeft: true, _outerLeftSmallSkin, _outerLeftSmallPendingEmerge);
        }
        else
        {
            _outerRightSmallReady = true;
            ConnectEvent(_outerRightSmallNode, _smallTentacleEventCallable, ref _outerRightSmallEventConnected);
            ApplySmallTentacleState(state, isOuterLeft: false, _outerRightSmallSkin, _outerRightSmallPendingEmerge);
        }

        UpdateSmallTentacleVisibility(isOuterLeft);
    }

    private void OnBodyAnimationEvent(Variant _sprite, Variant _animState, Variant _trackEntry, Variant spineEvent)
    {
        string? name = ExtractEventName(spineEvent);
        if (name == "tidal_hit" || name == "surge_roar")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
        }
    }

    private void OnBeamAnimationEvent(Variant _sprite, Variant _animState, Variant _trackEntry, Variant spineEvent)
    {
        string? name = ExtractEventName(spineEvent);
        if (name == "ink_hit_player")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Long);
        }
    }

    private void OnTentacleAnimationEvent(Variant _sprite, Variant _animState, Variant _trackEntry, Variant spineEvent)
    {
        string? name = ExtractEventName(spineEvent);
        if (name == "attack_hit")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
        }
        else if (name == "hit_1" || name == "hit_2")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
        }
    }

    private void OnSmallTentacleAnimationEvent(Variant _sprite, Variant _animState, Variant _trackEntry, Variant spineEvent)
    {
        string? name = ExtractEventName(spineEvent);
        if (name == "small_hit")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Medium, ShakeDuration.Short);
        }
        else if (name == "hit_1" || name == "hit_2")
        {
            NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
        }
    }

    private static string? ExtractEventName(Variant spineEvent)
    {
        if (spineEvent.VariantType != Variant.Type.Object)
            return null;
        return new MegaEvent(spineEvent).GetData().GetEventName();
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
            _leftFloorBreach == _lastLeftFloorBreach &&
            _rightFloorBreach == _lastRightFloorBreach &&
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
        _lastLeftFloorBreach = _leftFloorBreach;
        _lastRightFloorBreach = _rightFloorBreach;
        _lastCreatureCount = creatureCount;
        _lastAllyCount = allyCount;

        float targetGlobalScale = MathF.Max(viewportSize.X / 1920f, viewportSize.Y / 1080f) * ChamberOverscanScale;
        Vector2 baseCenterGlobal = viewportSize * 0.5f + nativeShake;
        Vector2 shakenCenterGlobal = baseCenterGlobal + new Vector2(shakeX, -shakeY) * targetGlobalScale;

        Vector2 parentScale = parentGlobal.Scale.Abs();
        if (parentScale.X > 0.001f && parentScale.Y > 0.001f)
        {
            bgControl.PivotOffset = Vector2.Zero;
            bgControl.Scale = new Vector2(targetGlobalScale / parentScale.X, targetGlobalScale / parentScale.Y);
            bgControl.Position = parentGlobal.AffineInverse() * shakenCenterGlobal;
        }

        SyncStageAndCreatureAnchors(viewportSize, sceneContainer, allyContainer, baseCenterGlobal, shakenCenterGlobal, targetGlobalScale);
    }

    private void SyncStageAndCreatureAnchors(
        Vector2 viewportSize,
        Control? sceneContainer,
        Control? allyContainer,
        Vector2 baseCenterGlobal,
        Vector2 shakenCenterGlobal,
        float targetGlobalScale)
    {
        if (allyContainer != null && GodotObject.IsInstanceValid(allyContainer))
        {
            ApplyMonumentalScaleToAllies(allyContainer);
            if (allyContainer.GetParent() is CanvasItem allyParent)
            {
                float maxFloorY = MathF.Max(baseCenterGlobal.Y + 120f, viewportSize.Y - MaxPlayerFloorBottomInset);
                float floorGlobalY = MathF.Min(baseCenterGlobal.Y + PlayerFloorLocalOffsetY * targetGlobalScale, maxFloorY);
                Vector2 desiredFloorGlobal = new(baseCenterGlobal.X, floorGlobalY);
                Vector2 floorInParent = allyParent.GetGlobalTransform().AffineInverse() * desiredFloorGlobal;
                Vector2 desiredAllyPos = new(floorInParent.X, floorInParent.Y - NativeAllyLocalY * allyContainer.Scale.Y);
                if (allyContainer.Position.DistanceSquaredTo(desiredAllyPos) > 0.01f)
                    allyContainer.Position = desiredAllyPos;
            }
        }

        Vector2 bossGlobal = shakenCenterGlobal + BossHudLocalOffset * targetGlobalScale;
        Vector2 leftOffset = _leftFloorBreach ? LeftFloorHudLocalOffset : LeftWaterHudLocalOffset;
        Vector2 rightOffset = _rightFloorBreach ? RightFloorHudLocalOffset : RightWaterHudLocalOffset;
        Vector2 leftGlobal = shakenCenterGlobal + leftOffset * targetGlobalScale;
        Vector2 rightGlobal = shakenCenterGlobal + rightOffset * targetGlobalScale;
        Vector2 outerLeftSmallGlobal = shakenCenterGlobal + OuterLeftSmallHudLocalOffset * targetGlobalScale;
        Vector2 outerRightSmallGlobal = shakenCenterGlobal + OuterRightSmallHudLocalOffset * targetGlobalScale;

        if (NCombatRoom.Instance is { } room)
        {
            foreach (NCreature node in room.CreatureNodes)
            {
                PositionAbyssalProxy(node, bossGlobal, leftGlobal, rightGlobal, outerLeftSmallGlobal, outerRightSmallGlobal);
            }
        }
        else if (sceneContainer != null)
        {
            SyncContainerProxies(sceneContainer, bossGlobal, leftGlobal, rightGlobal, outerLeftSmallGlobal, outerRightSmallGlobal);
            if (sceneContainer.GetNodeOrNull("EnemyContainer") is Node enemyContainer)
                SyncContainerProxies(enemyContainer, bossGlobal, leftGlobal, rightGlobal, outerLeftSmallGlobal, outerRightSmallGlobal);
        }
    }

    private static void PositionAbyssalProxy(
        NCreature node,
        Vector2 bossGlobal,
        Vector2 leftGlobal,
        Vector2 rightGlobal,
        Vector2 outerLeftSmallGlobal,
        Vector2 outerRightSmallGlobal)
    {
        if (!GodotObject.IsInstanceValid(node) || node.Entity?.Monster == null)
            return;

        switch (node.Entity.Monster)
        {
            case ThingsAbyssalInkmaw:
                node.GlobalPosition = bossGlobal;
                break;
            case ThingsAbyssalCrushingTentacle:
                node.GlobalPosition = leftGlobal;
                break;
            case ThingsAbyssalInkTentacle:
                node.GlobalPosition = rightGlobal;
                break;
            case ThingsAbyssalSmallTentacleBase small:
                node.GlobalPosition = small.IsOuterLeft ? outerLeftSmallGlobal : outerRightSmallGlobal;
                break;
        }
    }

    private static void SyncContainerProxies(
        Node container,
        Vector2 bossGlobal,
        Vector2 leftGlobal,
        Vector2 rightGlobal,
        Vector2 outerLeftSmallGlobal,
        Vector2 outerRightSmallGlobal)
    {
        int count = container.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (container.GetChild(i) is NCreature creature)
                PositionAbyssalProxy(creature, bossGlobal, leftGlobal, rightGlobal, outerLeftSmallGlobal, outerRightSmallGlobal);
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

    private static void ConnectEvent(Node? spineNode, Callable cb, ref bool connected)
    {
        if (connected || spineNode == null || !GodotObject.IsInstanceValid(spineNode))
            return;
        if (!spineNode.IsConnected("animation_event", cb))
            spineNode.Connect("animation_event", cb);
        connected = true;
    }

    private static void DisconnectEvent(Node? spineNode, Callable cb, ref bool connected)
    {
        if (!connected)
            return;
        if (spineNode != null && GodotObject.IsInstanceValid(spineNode) && spineNode.IsConnected("animation_event", cb))
            spineNode.Disconnect("animation_event", cb);
        connected = false;
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
        if (room.Encounter is not ThingsAbyssalInkmawBossEncounter)
            return;

        _presentationEnded = true;
        _cameraShakeBone = null;
        _beamPlayGeneration++;
        SetBeamActive(false);
        if (GodotObject.IsInstanceValid(this) && IsInsideTree())
            SyncChamberTransform();
    }
}
