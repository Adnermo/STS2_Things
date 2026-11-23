using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using STS2_Things.Monsters;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Dedicated Bestiary presenter for 渊墨巨蛸 (Abyssal Inkmaw) and all five of its Spine-driven
/// tentacle entries (<see cref="ThingsAbyssalInkmaw"/>, <see cref="ThingsAbyssalCrushingTentacle"/>,
/// <see cref="ThingsAbyssalInkTentacle"/>, <see cref="ThingsAbyssalSpikeTentacle"/>,
/// <see cref="ThingsAbyssalShellTentacle"/>, <see cref="ThingsAbyssalGlandTentacle"/>).
/// Mounts only the selected entry's Spine 4.2 rig(s) centered cleanly on the Bestiary stage
/// (no room backdrop, no ZIndex = -20) with pure animation/SFX previews via
/// <see cref="BestiaryMonsterMove.FromAction"/> so <c>NullCombatState</c> never triggers summon/damage side effects.
/// </summary>
public sealed partial class NBestiaryLayoutAbyssalInkmaw : NBestiaryLayout
{
    private const string BodySkelPath = "res://STS2_Things/animations/monsters/abyssal_inkmaw/abyssal_inkmaw_skel_data.tres";
    private const string BeamSkelPath = "res://STS2_Things/animations/monsters/abyssal_ink_beam/abyssal_ink_beam_skel_data.tres";
    private const string CrushingSkelPath = "res://STS2_Things/animations/monsters/abyssal_crushing_tentacle/abyssal_crushing_tentacle_skel_data.tres";
    private const string InkTentacleSkelPath = "res://STS2_Things/animations/monsters/abyssal_ink_tentacle/abyssal_ink_tentacle_skel_data.tres";
    private const string SmallTentacleSkelPath = "res://STS2_Things/animations/monsters/abyssal_small_tentacle/abyssal_small_tentacle_skel_data.tres";

    private const int ActionTrack = NAbyssalInkmawBossBackground.ActionTrack;
    private const int BaseModeTrack = NAbyssalInkmawBossBackground.BaseModeTrack;
    private const int VariantSkinTrack = NAbyssalInkmawBossBackground.VariantSkinTrack;

    // Positions relative to centered _stageAnchor ((960, 540) on 1920x1080).
    // Safe stage sits between left MoveContainer (~490) and right Sidebar (1520), below MonsterName (152).
    private static readonly Vector2 BodyLocalPos = new(65f, 75f);
    private const float BodyScale = 0.48f;

    private static readonly Vector2 CrushingLocalPos = new(-35f, 165f);
    private static readonly Vector2 InkTentacleLocalPos = new(65f, 165f);
    private const float MajorTentacleScale = 0.88f;

    private static readonly Vector2 SmallTentacleLocalPos = new(-15f, 135f);
    private const float SmallTentacleScale = 1.25f;

    private const int RigNone = 0;
    private const int RigBody = 1;
    private const int RigCrushingTentacle = 2;
    private const int RigInkTentacle = 3;
    private const int RigSmallTentacle = 4;

    private Control? _stageAnchor;
    private NCreature? _creature;
    private MonsterModel? _activeModel;

    private int _activeRigKind;
    private Node2D? _primaryNode;
    private MegaSprite? _primarySprite;
    private bool _primaryReady;

    private Node2D? _beamNode;
    private MegaSprite? _beamSprite;

    private string _currentIdleAnim = "idle_loop";
    private string? _activeSmallSkinAnim;
    private int _actionGeneration;
    private Action<MegaAnimationState>? _pendingPrimarySetup;

    public Node2D? PrimarySpineNode => _primaryNode;
    public MegaSprite? PrimarySpineController => _primarySprite;
    public Node2D? BeamSpineNode => _beamNode;

    public static NBestiaryLayoutAbyssalInkmaw Create() => new();

    public override void _Ready()
    {
        EnsureStageInitialized();
    }

    public override void _ExitTree()
    {
        _actionGeneration++;
        _pendingPrimarySetup = null;
    }

    public override void Cleanup()
    {
        _actionGeneration++;
        HideBeamImmediate();
        _creature?.QueueFreeSafely();
        _creature = null;
        _activeModel = null;
    }

    public override IEnumerable<NCreature> GetCreatures()
    {
        if (_creature == null || !GodotObject.IsInstanceValid(_creature))
            return Array.Empty<NCreature>();
        return [_creature];
    }

    public override List<BestiaryMonsterMove> Setup(BestiaryEntry entry, Tween tween)
    {
        EnsureStageInitialized();
        Cleanup();

        MonsterModel monsterModel = (entry.monsterModel ?? ModelDb.Monster<ThingsAbyssalInkmaw>()).ToMutable();
        monsterModel.Rng = Rng.Chaotic;
        monsterModel.RunRng = new RunRngSet(Rng.Chaotic);
        monsterModel.SetUpForCombat();
        _activeModel = monsterModel;

        Creature entity = new Creature(monsterModel, CombatSide.Enemy, null)
        {
            CombatState = NullCombatState.Instance
        };
        _creature = NCreature.Create(entity);
        if (_creature != null && _stageAnchor != null)
        {
            _stageAnchor.AddChildSafely(_creature);
            _creature.SetupForBestiary();
            _creature.Position = Vector2.Zero;
        }

        _currentIdleAnim = "idle_loop";
        _activeSmallSkinAnim = monsterModel is ThingsAbyssalSmallTentacleBase small
            ? NormalizeSmallSkin(small.VariantSkinName)
            : null;

        int requiredKind = ResolveRigKind(monsterModel);
        EnsureRigForKind(requiredKind);
        ApplyRigFraming(requiredKind);

        Action<MegaAnimationState> applyPose = state =>
        {
            ApplyIdlePose(state, requiredKind, _activeSmallSkinAnim);
            _primarySprite?.BoundObject.Call("update_skeleton", 0f);
        };

        if (_primaryReady && _primarySprite?.TryGetAnimationState() is { } readyState)
        {
            applyPose(readyState);
        }
        else
        {
            _pendingPrimarySetup = applyPose;
        }

        if (_stageAnchor != null)
        {
            _stageAnchor.Modulate = new Color(1f, 1f, 1f, 0f);
            if (tween != null && GodotObject.IsInstanceValid(tween))
            {
                tween.TweenProperty(_stageAnchor, "modulate", Colors.White, 0.25);
            }
            else
            {
                _stageAnchor.Modulate = Colors.White;
            }
        }

        return BuildMoves(monsterModel);
    }

    private void EnsureStageInitialized()
    {
        if (_stageAnchor != null && GodotObject.IsInstanceValid(_stageAnchor))
            return;

        Name = "BestiaryLayoutAbyssalInkmaw";
        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;

        _stageAnchor = new Control
        {
            Name = "AbyssalInkmawBestiaryStage",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = 0f,
            OffsetRight = 0f,
            OffsetTop = 0f,
            OffsetBottom = 0f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_stageAnchor);
    }

    private static int ResolveRigKind(MonsterModel model) => model switch
    {
        ThingsAbyssalCrushingTentacle => RigCrushingTentacle,
        ThingsAbyssalInkTentacle => RigInkTentacle,
        ThingsAbyssalSmallTentacleBase => RigSmallTentacle,
        _ => RigBody,
    };

    private void EnsureRigForKind(int kind)
    {
        if (_activeRigKind == kind && _primaryNode != null && GodotObject.IsInstanceValid(_primaryNode))
        {
            _primaryNode.Visible = true;
            return;
        }

        ReleaseCurrentRigs();
        _activeRigKind = kind;

        if (_stageAnchor == null || !ClassDB.ClassExists("SpineSprite"))
            return;

        string skelPath = kind switch
        {
            RigCrushingTentacle => CrushingSkelPath,
            RigInkTentacle => InkTentacleSkelPath,
            RigSmallTentacle => SmallTentacleSkelPath,
            _ => BodySkelPath,
        };

        string nodeName = kind switch
        {
            RigCrushingTentacle => "AbyssalCrushingTentacleSpine",
            RigInkTentacle => "AbyssalInkTentacleSpine",
            RigSmallTentacle => "AbyssalSmallTentacleSpine",
            _ => "AbyssalInkmawBodySpine",
        };

        (_primaryNode, _primarySprite) = CreateSpineChild(_stageAnchor, skelPath, nodeName, visible: true);
        if (_primarySprite != null)
        {
            MegaSprite captured = _primarySprite;
            this.RunWhenSpineReady(captured, state =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !ReferenceEquals(_primarySprite, captured))
                    return;

                _primaryReady = true;
                if (_pendingPrimarySetup != null)
                {
                    Action<MegaAnimationState> pending = _pendingPrimarySetup;
                    _pendingPrimarySetup = null;
                    pending(state);
                }
                else
                {
                    ApplyIdlePose(state, _activeRigKind, _activeSmallSkinAnim);
                    captured.BoundObject.Call("update_skeleton", 0f);
                }
            });
        }

        if (kind == RigBody)
        {
            (_beamNode, _beamSprite) = CreateSpineChild(_stageAnchor, BeamSkelPath, "AbyssalInkBeamSpine", visible: false);
            if (_beamSprite != null)
            {
                MegaSprite capturedBeam = _beamSprite;
                this.RunWhenSpineReady(capturedBeam, beamState =>
                {
                    if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !ReferenceEquals(_beamSprite, capturedBeam))
                        return;

                    SetTrackImmediate(beamState, "idle_hidden", loop: true, ActionTrack, mix: 0f);
                    capturedBeam.BoundObject.Call("update_skeleton", 0f);
                    if (_beamNode != null && GodotObject.IsInstanceValid(_beamNode))
                        _beamNode.Visible = false;
                });
            }
        }
    }

    private void ReleaseCurrentRigs()
    {
        _primaryReady = false;
        _pendingPrimarySetup = null;

        if (_beamNode != null && GodotObject.IsInstanceValid(_beamNode))
        {
            _beamNode.GetParent()?.RemoveChild(_beamNode);
            _beamNode.QueueFreeSafely();
        }
        _beamNode = null;
        _beamSprite = null;

        if (_primaryNode != null && GodotObject.IsInstanceValid(_primaryNode))
        {
            _primaryNode.GetParent()?.RemoveChild(_primaryNode);
            _primaryNode.QueueFreeSafely();
        }
        _primaryNode = null;
        _primarySprite = null;
        _activeRigKind = RigNone;
    }

    private void ApplyRigFraming(int kind)
    {
        if (_primaryNode == null || !GodotObject.IsInstanceValid(_primaryNode))
            return;

        switch (kind)
        {
            case RigCrushingTentacle:
                _primaryNode.Position = CrushingLocalPos;
                _primaryNode.Scale = new Vector2(MajorTentacleScale, MajorTentacleScale);
                break;
            case RigInkTentacle:
                _primaryNode.Position = InkTentacleLocalPos;
                _primaryNode.Scale = new Vector2(MajorTentacleScale, MajorTentacleScale);
                break;
            case RigSmallTentacle:
                _primaryNode.Position = SmallTentacleLocalPos;
                _primaryNode.Scale = new Vector2(SmallTentacleScale, SmallTentacleScale);
                break;
            default:
                _primaryNode.Position = BodyLocalPos;
                _primaryNode.Scale = new Vector2(BodyScale, BodyScale);
                if (_beamNode != null && GodotObject.IsInstanceValid(_beamNode))
                {
                    _beamNode.Position = BodyLocalPos;
                    _beamNode.Scale = new Vector2(BodyScale, BodyScale);
                }
                break;
        }
    }

    private void ApplyIdlePose(MegaAnimationState state, int kind, string? smallSkinAnim)
    {
        state.SetTimeScale(1f);
        SetTrackImmediate(state, _currentIdleAnim, loop: true, ActionTrack, mix: 0f);
        if (kind is RigCrushingTentacle or RigInkTentacle or RigSmallTentacle)
        {
            SetTrackImmediate(state, "base_floor", loop: true, BaseModeTrack, mix: 0f);
        }
        if (kind == RigSmallTentacle && !string.IsNullOrEmpty(smallSkinAnim))
        {
            SetTrackImmediate(state, smallSkinAnim, loop: true, VariantSkinTrack, mix: 0f);
        }
    }

    private List<BestiaryMonsterMove> BuildMoves(MonsterModel model)
    {
        return model switch
        {
            ThingsAbyssalCrushingTentacle crushing => BuildCrushingMoves(crushing),
            ThingsAbyssalInkTentacle inkTentacle => BuildInkTentacleMoves(inkTentacle),
            ThingsAbyssalSpikeTentacle spike => BuildSmallMoves(
                spike,
                ThingsAbyssalSpikeTentacle.SpikeSlamId, "slam_attack",
                ThingsAbyssalSpikeTentacle.DoubleSlapId, "double_slap"),
            ThingsAbyssalShellTentacle shell => BuildSmallMoves(
                shell,
                ThingsAbyssalShellTentacle.ShellGuardId, "guard_brace",
                ThingsAbyssalShellTentacle.ShellSlapId, "slam_attack"),
            ThingsAbyssalGlandTentacle gland => BuildSmallMoves(
                gland,
                ThingsAbyssalGlandTentacle.GlandSurgeId, "buff_pulse",
                ThingsAbyssalGlandTentacle.GlandSlapId, "slam_attack"),
            _ => BuildBodyMoves(model),
        };
    }

    private List<BestiaryMonsterMove> BuildBodyMoves(MonsterModel model)
    {
        // Strictly 9 moves (NBestiary logs an error beyond 9 hotkeys):
        // covers every authored clip on abyssal_inkmaw + abyssal_ink_beam + hurt + die.
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.TidalCrashId), () => PlayBodyOneShot("tidal_crash")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.SummonTentacleId), () => PlayBodyOneShot("summon_tentacle")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.AbyssalPressureId), () => PlayBodyOneShot("charge_pulse")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.InkChargeId), () => PlayBodyOneShot("charge_pulse", returnLoop: "charge_loop")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.InkSprayId), PlayBodyInkSprayMove),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.PostSprayStunId), () => PlayBodyLoop("stun_loop")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkmaw.Phase2SurgeId), PlayBodyPhase2SurgeMove),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayBodyOneShot("hurt")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayBodyDieMove),
        ];
    }

    private List<BestiaryMonsterMove> BuildCrushingMoves(ThingsAbyssalCrushingTentacle model)
    {
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalCrushingTentacle.HeavySlamId), () => PlayTentacleOneShot("heavy_slam", baseBurst: true)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalCrushingTentacle.GuardCoilId), () => PlayTentacleOneShot("guard_coil", baseBurst: false)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalCrushingTentacle.ChargeBuffId), () => PlayTentacleOneShot("charge_buff", baseBurst: false)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalCrushingTentacle.PostSprayStunId), () => PlayTentacleLoop("stun_loop")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayTentacleOneShot("hurt", baseBurst: false)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayTentacleDieMove),
        ];
    }

    private List<BestiaryMonsterMove> BuildInkTentacleMoves(ThingsAbyssalInkTentacle model)
    {
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkTentacle.InkBolsterId), () => PlayTentacleOneShot("ink_bolster", baseBurst: false)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkTentacle.WhipLashId), () => PlayTentacleOneShot("whip_lash", baseBurst: true)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkTentacle.ChargeBuffId), () => PlayTentacleOneShot("charge_buff", baseBurst: false)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalInkTentacle.PostSprayStunId), () => PlayTentacleLoop("stun_loop")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayTentacleOneShot("hurt", baseBurst: false)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayTentacleDieMove),
        ];
    }

    private List<BestiaryMonsterMove> BuildSmallMoves(
        ThingsAbyssalSmallTentacleBase model,
        string firstMoveId,
        string firstClip,
        string secondMoveId,
        string secondClip)
    {
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, firstMoveId), () => PlayTentacleOneShot(firstClip, baseBurst: true)),
            BestiaryMonsterMove.FromAction(MoveName(model, secondMoveId), () => PlayTentacleOneShot(secondClip, baseBurst: true)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalSmallTentacleBase.ChargeBuffId), () => PlayTentacleOneShot("buff_pulse", baseBurst: false)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsAbyssalSmallTentacleBase.PostSprayStunId), () => PlayTentacleLoop("stun_loop")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayTentacleOneShot("hurt", baseBurst: false)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayTentacleDieMove),
        ];
    }

    private Task PlayBodyOneShot(string clip, string? returnLoop = null)
    {
        _actionGeneration++;
        HideBeamImmediate();
        if (_primarySprite?.TryGetAnimationState() is not { } state || !_primarySprite.HasAnimation(clip))
            return Task.CompletedTask;

        string idle = returnLoop ?? _currentIdleAnim;
        SetTrackImmediate(state, clip, loop: false, ActionTrack, mix: 0.10f);
        if (_primarySprite.HasAnimation(idle))
            state.AddAnimation(idle, 0f, loop: true, ActionTrack);
        return Task.CompletedTask;
    }

    private Task PlayBodyLoop(string loopClip)
    {
        _actionGeneration++;
        HideBeamImmediate();
        if (_primarySprite?.TryGetAnimationState() is not { } state || !_primarySprite.HasAnimation(loopClip))
            return Task.CompletedTask;

        SetTrackImmediate(state, loopClip, loop: true, ActionTrack, mix: 0.15f);
        return Task.CompletedTask;
    }

    private Task PlayBodyPhase2SurgeMove()
    {
        _actionGeneration++;
        HideBeamImmediate();
        _currentIdleAnim = "phase2_idle_loop";
        if (_primarySprite?.TryGetAnimationState() is not { } state || !_primarySprite.HasAnimation("phase2_surge"))
            return Task.CompletedTask;

        SetTrackImmediate(state, "phase2_surge", loop: false, ActionTrack, mix: 0.12f);
        if (_primarySprite.HasAnimation("phase2_idle_loop"))
            state.AddAnimation("phase2_idle_loop", 0f, loop: true, ActionTrack);
        return Task.CompletedTask;
    }

    private Task PlayBodyInkSprayMove()
    {
        int gen = ++_actionGeneration;
        if (_primarySprite?.TryGetAnimationState() is { } bodyState && _primarySprite.HasAnimation("ink_spray"))
        {
            SetTrackImmediate(bodyState, "ink_spray", loop: false, ActionTrack, mix: 0.08f);
            if (_primarySprite.HasAnimation(_currentIdleAnim))
                bodyState.AddAnimation(_currentIdleAnim, 0f, loop: true, ActionTrack);
        }

        if (_beamNode != null && GodotObject.IsInstanceValid(_beamNode)
            && _beamSprite?.TryGetAnimationState() is { } beamState && _beamSprite.HasAnimation("ink_spray"))
        {
            _beamNode.Visible = true;
            SetTrackImmediate(beamState, "ink_spray", loop: false, ActionTrack, mix: 0f);
            if (_beamSprite.HasAnimation("idle_hidden"))
                beamState.AddAnimation("idle_hidden", 0f, loop: true, ActionTrack);
            _beamSprite.BoundObject.Call("update_skeleton", 0f);

            float duration = beamState.GetCurrentAnimationDuration(ActionTrack) is { } d && d > 0.1f ? d : 2.05f;
            SceneTree? tree = GetTree();
            if (tree != null)
            {
                tree.CreateTimer(duration + 0.12f).Timeout += () =>
                {
                    if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _actionGeneration != gen)
                        return;
                    HideBeamImmediate();
                };
            }
        }

        return Task.CompletedTask;
    }

    private Task PlayBodyDieMove()
    {
        _actionGeneration++;
        HideBeamImmediate();
        if (_primarySprite?.TryGetAnimationState() is { } state && _primarySprite.HasAnimation("die"))
        {
            SetTrackImmediate(state, "die", loop: false, ActionTrack, mix: 0.08f);
        }
        PlayFmodSfx(_activeModel?.DeathSfx);
        return Task.CompletedTask;
    }

    private Task PlayTentacleOneShot(string clip, bool baseBurst)
    {
        _actionGeneration++;
        if (_primarySprite?.TryGetAnimationState() is not { } state || !_primarySprite.HasAnimation(clip))
            return Task.CompletedTask;

        SetTrackImmediate(state, clip, loop: false, ActionTrack, mix: 0.08f);
        if (_primarySprite.HasAnimation("idle_loop"))
            state.AddAnimation("idle_loop", 0f, loop: true, ActionTrack);

        if (baseBurst && _primarySprite.HasAnimation("base_floor_burst"))
        {
            SetTrackImmediate(state, "base_floor_burst", loop: false, BaseModeTrack, mix: 0.05f);
            if (_primarySprite.HasAnimation("base_floor"))
                state.AddAnimation("base_floor", 0f, loop: true, BaseModeTrack);
        }
        else if (_primarySprite.HasAnimation("base_floor"))
        {
            SetTrackImmediate(state, "base_floor", loop: true, BaseModeTrack, mix: 0.05f);
        }

        return Task.CompletedTask;
    }

    private Task PlayTentacleLoop(string loopClip)
    {
        _actionGeneration++;
        if (_primarySprite?.TryGetAnimationState() is not { } state || !_primarySprite.HasAnimation(loopClip))
            return Task.CompletedTask;

        SetTrackImmediate(state, loopClip, loop: true, ActionTrack, mix: 0.15f);
        if (_primarySprite.HasAnimation("base_floor"))
            SetTrackImmediate(state, "base_floor", loop: true, BaseModeTrack, mix: 0.05f);
        return Task.CompletedTask;
    }

    private Task PlayTentacleDieMove()
    {
        _actionGeneration++;
        if (_primarySprite?.TryGetAnimationState() is { } state && _primarySprite.HasAnimation("retreat_die"))
        {
            SetTrackImmediate(state, "retreat_die", loop: false, ActionTrack, mix: 0.06f);
            if (_primarySprite.HasAnimation("base_floor_burst"))
                SetTrackImmediate(state, "base_floor_burst", loop: false, BaseModeTrack, mix: 0.05f);
        }
        PlayFmodSfx(_activeModel?.DeathSfx);
        return Task.CompletedTask;
    }

    private void HideBeamImmediate()
    {
        if (_beamNode != null && GodotObject.IsInstanceValid(_beamNode))
            _beamNode.Visible = false;

        if (_beamSprite?.TryGetAnimationState() is { } beamState && _beamSprite.HasAnimation("idle_hidden"))
        {
            SetTrackImmediate(beamState, "idle_hidden", loop: true, ActionTrack, mix: 0f);
            _beamSprite.BoundObject.Call("update_skeleton", 0f);
        }
    }

    private static (Node2D? Node, MegaSprite? Sprite) CreateSpineChild(Node parent, string skelPath, string name, bool visible)
    {
        Resource? skelData = LoadSkeletonResource(skelPath);
        if (skelData == null)
        {
            Log.Error($"[BestiaryLayoutAbyssalInkmaw] Failed to load '{skelPath}'.");
            return (null, null);
        }

        Node2D? node = ClassDB.Instantiate("SpineSprite").As<Node2D>();
        if (node == null)
        {
            Log.Error($"[BestiaryLayoutAbyssalInkmaw] Failed to instantiate SpineSprite '{name}'.");
            return (null, null);
        }

        node.Name = name;
        node.Visible = visible;
        parent.AddChild(node);

        MegaSprite sprite = new MegaSprite(node);
        sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skelData));
        return (node, sprite);
    }

    private static void SetTrackImmediate(MegaAnimationState state, string animName, bool loop, int trackId, float mix)
    {
        state.SetAnimation(animName, loop, trackId);
        using (TrackEntryScope(state.GetCurrent(trackId), out MegaTrackEntry? entry))
        {
            entry?.SetMixDuration(mix);
        }
    }

    private static string NormalizeSmallSkin(string skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName))
            return "skin_spike";
        return skinName.StartsWith("skin_", StringComparison.Ordinal) ? skinName : $"skin_{skinName}";
    }

    private static void PlayFmodSfx(string? sfx)
    {
        if (TestMode.IsOn || string.IsNullOrEmpty(sfx) || NAudioManager.Instance == null)
            return;
        NAudioManager.Instance.PlayOneShot(sfx);
    }

    private static LocString MoveName(MonsterModel model, string moveId) =>
        new("monsters", $"{model.Id.Entry}.moves.{moveId}.title");

    private static Resource? LoadSkeletonResource(string path)
    {
        try
        {
            return PreloadManager.Cache.GetAsset<Resource>(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"[BestiaryLayoutAbyssalInkmaw] Cache load fallback for '{path}': {ex.Message}");
            return ResourceLoader.Load<Resource>(path);
        }
    }
}
