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
using STS2_Things.Audio;
using STS2_Things.Monsters;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Dedicated Bestiary presenter for 活体巨岩 (Living Megalith) and its three companion entries
/// (<see cref="ThingsCaveGodBody"/>, <see cref="ThingsCaveGodLeftHand"/>,
/// <see cref="ThingsCaveGodRightHand"/>, <see cref="ThingsCaveGodCaptiveClaw"/>).
/// Mounts a single un-occluded Spine 4.2 rig framed cleanly inside the Bestiary safe stage
/// (zero room backdrop, zero ZIndex = -20) with strict Phase 1/Phase 2 slot isolation and
/// side-effect-free <see cref="BestiaryMonsterMove.FromAction"/> move previews.
/// </summary>
public sealed partial class NBestiaryLayoutCaveGod : NBestiaryLayout
{
    private const string SkeletonDataPath = "res://animations/monsters/cave_god/cave_god_skeleton_data.tres";
    private const int MainTrack = 0;
    private const int ReactionTrack = 1;
    private const int SpineMixBlendAdd = 3;

    // Centered Bestiary safe showcase box on 1920x1080: x = [480, 1480] (w=1000), y = [140, 920] (h=780).
    // ClipContents cleanly grounds CaveGod's submerged body_bottom stalk and hide/die retreat sink
    // without spilling into the left Actions list, right Sidebar, top MonsterName, or bottom DialogueLine.
    private const float StageOffsetLeft = -480f;
    private const float StageOffsetRight = 520f;
    private const float StageOffsetTop = -400f;
    private const float StageOffsetBottom = 380f;

    private static readonly Vector2 BodyLocalPos = new(500f, 575f);
    private const float BodyScale = 0.64f;

    private static readonly Vector2 LeftHandLocalPos = new(535f, 575f);
    private static readonly Vector2 RightHandLocalPos = new(465f, 575f);
    private const float HandScale = 0.61f;

    private static readonly Vector2 CaptiveClawLocalPos = new(500f, 585f);
    private const float CaptiveClawScale = 0.64f;

    private static readonly StringName MethodSetAttachment = "set_attachment";

    private static readonly string[] RedSlotNames =
    [
        "beard1_red", "beard2_red", "beard3_red",
        "body_red", "crastalls_roof_red", "eyes_red",
        "head_red", "head_skale_red", "horn1_red", "horn2_red", "neck_red",
        "arm1_2_red", "arm1_2_back_red", "arm2_2_red"
    ];

    private static readonly string[] BlueSlotNames =
    [
        "beard1", "beard2", "beard3",
        "body", "crastalls_roof", "eyes",
        "head", "head_skale", "horn1", "horn2", "neck",
        "arm1_2", "arm1_2_back", "arm2_2"
    ];

    private Control? _stageClip;
    private Node2D? _spineNode;
    private MegaSprite? _spineSprite;
    private NCreature? _creature;
    private MonsterModel? _activeModel;

    private readonly List<GodotObject> _redSlots = [];
    private readonly List<GodotObject> _blueSlots = [];
    private GodotObject? _horizonSlot;
    private Callable _beforeTransformsCallable;
    private bool _beforeTransformsConnected;
    private bool _spineReady;

    private bool _isAngry;
    private bool _isTransforming;
    private string _baseIdleAnim = "idle_front";
    private int _actionGeneration;
    private Tween? _flashTween;
    private Action<MegaAnimationState>? _pendingSetupPose;

    public Node2D? PrimarySpineNode => _spineNode;
    public MegaSprite? PrimarySpineController => _spineSprite;

    public static NBestiaryLayoutCaveGod Create() => new();

    public override void _Ready()
    {
        EnsureInitialized();
    }

    public override void _ExitTree()
    {
        _actionGeneration++;
        KillFlashTween();
        DisconnectSpineSignals();
        _redSlots.Clear();
        _blueSlots.Clear();
        _horizonSlot = null;
        _pendingSetupPose = null;
    }

    public override void Cleanup()
    {
        _actionGeneration++;
        KillFlashTween();
        _isTransforming = false;
        _creature?.QueueFreeSafely();
        _creature = null;
        _activeModel = null;
        if (_spineNode != null && GodotObject.IsInstanceValid(_spineNode))
        {
            _spineNode.Modulate = Colors.White;
        }
        _spineSprite?.TryGetAnimationState()?.SetTimeScale(1f);
    }

    public override IEnumerable<NCreature> GetCreatures()
    {
        if (_creature == null || !GodotObject.IsInstanceValid(_creature))
            return Array.Empty<NCreature>();
        return [_creature];
    }

    public override List<BestiaryMonsterMove> Setup(BestiaryEntry entry, Tween tween)
    {
        EnsureInitialized();
        Cleanup();

        MonsterModel monsterModel = (entry.monsterModel ?? ModelDb.Monster<ThingsCaveGodBody>()).ToMutable();
        monsterModel.Rng = Rng.Chaotic;
        monsterModel.RunRng = new RunRngSet(Rng.Chaotic);
        monsterModel.SetUpForCombat();
        _activeModel = monsterModel;

        Creature entity = new Creature(monsterModel, CombatSide.Enemy, null)
        {
            CombatState = NullCombatState.Instance
        };
        _creature = NCreature.Create(entity);
        if (_creature != null && _stageClip != null)
        {
            _stageClip.AddChildSafely(_creature);
            _creature.SetupForBestiary();
            _creature.Position = BodyLocalPos;
        }

        _isAngry = false;
        _isTransforming = false;
        _baseIdleAnim = monsterModel is ThingsCaveGodCaptiveClaw ? "grab_hold" : "idle_front";

        ApplyFraming(monsterModel);

        Action<MegaAnimationState> applyEntryPose = state =>
        {
            state.SetTimeScale(1f);
            ClearReactionTrack(state, 0f);
            SetMainAnimation(state, _baseIdleAnim, loop: true, mixDuration: 0f);
            _spineSprite?.BoundObject.Call("update_skeleton", 0f);
        };

        if (_spineReady && _spineSprite?.TryGetAnimationState() is { } readyState)
        {
            applyEntryPose(readyState);
        }
        else
        {
            _pendingSetupPose = applyEntryPose;
        }

        if (_stageClip != null)
        {
            _stageClip.Modulate = new Color(1f, 1f, 1f, 0f);
            if (tween != null && GodotObject.IsInstanceValid(tween))
            {
                tween.TweenProperty(_stageClip, "modulate", Colors.White, 0.25);
            }
            else
            {
                _stageClip.Modulate = Colors.White;
            }
        }

        return BuildMoves(monsterModel);
    }

    private void EnsureInitialized()
    {
        if (_stageClip != null && GodotObject.IsInstanceValid(_stageClip))
            return;

        Name = "BestiaryLayoutCaveGod";
        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;

        _stageClip = new Control
        {
            Name = "CaveGodBestiaryStage",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = StageOffsetLeft,
            OffsetRight = StageOffsetRight,
            OffsetTop = StageOffsetTop,
            OffsetBottom = StageOffsetBottom,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_stageClip);

        if (!ClassDB.ClassExists("SpineSprite"))
        {
            Log.Error("[BestiaryLayoutCaveGod] SpineSprite class is not registered.");
            return;
        }

        Resource? skelData = LoadSkeletonResource(SkeletonDataPath);
        if (skelData == null)
        {
            Log.Error($"[BestiaryLayoutCaveGod] Failed to load '{SkeletonDataPath}'.");
            return;
        }

        _spineNode = ClassDB.Instantiate("SpineSprite").As<Node2D>();
        if (_spineNode == null)
        {
            Log.Error("[BestiaryLayoutCaveGod] Failed to instantiate SpineSprite.");
            return;
        }

        _spineNode.Name = "CaveGodBestiarySpine";
        _spineNode.Visible = true;
        _spineNode.Position = BodyLocalPos;
        _spineNode.Scale = new Vector2(BodyScale, BodyScale);
        _stageClip.AddChild(_spineNode);

        _spineSprite = new MegaSprite(_spineNode);
        _spineSprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skelData));
        this.RunWhenSpineReady(_spineSprite, OnSpineReady);
    }

    private void OnSpineReady(MegaAnimationState state)
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _spineSprite == null || !GodotObject.IsInstanceValid(_spineSprite.BoundObject))
            return;

        _spineReady = true;
        CachePhaseSlots(_spineSprite);
        ConnectSlotIsolationHook(_spineSprite);

        if (_pendingSetupPose != null)
        {
            Action<MegaAnimationState> pending = _pendingSetupPose;
            _pendingSetupPose = null;
            pending(state);
        }
        else
        {
            SetMainAnimation(state, _baseIdleAnim, loop: true, mixDuration: 0f);
            _spineSprite.BoundObject.Call("update_skeleton", 0f);
        }
    }

    private void CachePhaseSlots(MegaSprite sprite)
    {
        _redSlots.Clear();
        _blueSlots.Clear();
        _horizonSlot = null;

        MegaSkeleton? skel = sprite.GetSkeleton();
        if (skel == null)
            return;

        foreach (string slotName in RedSlotNames)
        {
            using Variant slotVar = skel.BoundObject.Call("find_slot", slotName);
            if (slotVar.AsGodotObject() is GodotObject slotObj)
                _redSlots.Add(slotObj);
        }

        foreach (string slotName in BlueSlotNames)
        {
            using Variant slotVar = skel.BoundObject.Call("find_slot", slotName);
            if (slotVar.AsGodotObject() is GodotObject slotObj)
                _blueSlots.Add(slotObj);
        }

        using Variant horizonVar = skel.BoundObject.Call("find_slot", "horizon");
        if (horizonVar.AsGodotObject() is GodotObject horizonObj)
            _horizonSlot = horizonObj;
    }

    private void ConnectSlotIsolationHook(MegaSprite sprite)
    {
        if (_beforeTransformsConnected)
            return;

        _beforeTransformsCallable = Callable.From((Variant _) =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                return;

            if (_horizonSlot != null && GodotObject.IsInstanceValid(_horizonSlot))
                _horizonSlot.Call(MethodSetAttachment, default(Variant));

            if (_isTransforming)
                return;

            List<GodotObject> hidden = _isAngry ? _blueSlots : _redSlots;
            for (int i = 0; i < hidden.Count; i++)
            {
                if (GodotObject.IsInstanceValid(hidden[i]))
                    hidden[i].Call(MethodSetAttachment, default(Variant));
            }
        });

        sprite.ConnectBeforeWorldTransformsChange(_beforeTransformsCallable);
        _beforeTransformsConnected = true;
    }

    private void DisconnectSpineSignals()
    {
        if (_beforeTransformsConnected && _spineSprite != null && GodotObject.IsInstanceValid(_spineSprite.BoundObject))
        {
            if (_spineSprite.BoundObject.IsConnected("before_world_transforms_change", _beforeTransformsCallable))
                _spineSprite.DisconnectBeforeWorldTransformsChange(_beforeTransformsCallable);
        }
        _beforeTransformsConnected = false;
    }

    private void ApplyFraming(MonsterModel model)
    {
        if (_spineNode == null || !GodotObject.IsInstanceValid(_spineNode))
            return;

        switch (model)
        {
            case ThingsCaveGodLeftHand:
                _spineNode.Position = LeftHandLocalPos;
                _spineNode.Scale = new Vector2(HandScale, HandScale);
                break;
            case ThingsCaveGodRightHand:
                _spineNode.Position = RightHandLocalPos;
                _spineNode.Scale = new Vector2(HandScale, HandScale);
                break;
            case ThingsCaveGodCaptiveClaw:
                _spineNode.Position = CaptiveClawLocalPos;
                _spineNode.Scale = new Vector2(CaptiveClawScale, CaptiveClawScale);
                break;
            default:
                _spineNode.Position = BodyLocalPos;
                _spineNode.Scale = new Vector2(BodyScale, BodyScale);
                break;
        }
    }

    private List<BestiaryMonsterMove> BuildMoves(MonsterModel model)
    {
        return model switch
        {
            ThingsCaveGodLeftHand left => BuildHandMoves(left, isLeft: true),
            ThingsCaveGodRightHand right => BuildHandMoves(right, isLeft: false),
            ThingsCaveGodCaptiveClaw claw => BuildCaptiveClawMoves(claw),
            _ => BuildBodyMoves(model),
        };
    }

    private List<BestiaryMonsterMove> BuildBodyMoves(MonsterModel model)
    {
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.JabsId), () => PlayOneShotMove("alternating_jabs", "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.GuardId), () => PlayOneShotMove("double_fist_crush", "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.CrushId), () => PlayOneShotMove("double_fist_crush", "res://sfx/cave_god/toss_impact.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.AngrySlamId), () => PlayOneShotMove("central_slam", "res://sfx/cave_god/toss_impact.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.Exposed1Id), () => PlayWeakProneMove(isLeftBroken: true)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodBody.EnrageId), PlayEnrageTransformMove),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayHurtMove(isClaw: false)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayBodyDieMove),
        ];
    }

    private List<BestiaryMonsterMove> BuildHandMoves(ThingsCaveGodHand model, bool isLeft)
    {
        string snatchClip = isLeft ? "card_snatch" : "card_snatch_right";
        string sweepClip = isLeft ? "front_sweep" : "front_sweep_right";
        string punchClip = isLeft ? "leftpunch" : "rightpunch";
        string grabClip = isLeft ? "grab_player" : "grab_player_right";
        string grabHoldClip = isLeft ? "grab_hold" : "grab_hold_right";
        string slamClip = isLeft ? "grab_slam" : "grab_slam_right";
        // When the opposite arm is downed, this arm executes the remaining-arm weak_attack.
        string weakAttackClip = isLeft ? "weak_attack_right" : "weak_attack";

        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Sweep)), () => PlayOneShotMove(snatchClip, "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.CleaveSweep)), () => PlayOneShotMove(sweepClip, "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.HeavyPunch)), () => PlayOneShotMove(punchClip, "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Grab)), () => PlayOneShotMove(grabClip, "res://sfx/cave_god/toss_impact.mp3", returnLoop: grabHoldClip)),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.Slam)), () => PlayOneShotMove(slamClip, "res://sfx/cave_god/toss_impact.mp3")),
            BestiaryMonsterMove.FromAction(MoveName(model, ThingsCaveGodHand.MoveId(ThingsCaveGodHand.Action.WeakAttack)), () => PlayOneShotMove(weakAttackClip, "res://sfx/cave_god/rock_punch.mp3")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayHurtMove(isClaw: false)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), () => PlayWeakProneMove(isLeftBroken: isLeft)),
        ];
    }

    private List<BestiaryMonsterMove> BuildCaptiveClawMoves(ThingsCaveGodCaptiveClaw model)
    {
        return
        [
            BestiaryMonsterMove.FromAction(MoveName(model, "CAPTIVE_HOLD"), () => PlayOneShotMove("grab_player", "res://sfx/cave_god/toss_impact.mp3", returnLoop: "grab_hold")),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.hurt"), () => PlayHurtMove(isClaw: true)),
            BestiaryMonsterMove.FromAction(new LocString("bestiary", "ACTION_NAME.die"), PlayClawDieMove),
        ];
    }

    private Task PlayOneShotMove(string clipBase, string? customSfxPath, string? returnLoop = null)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        _actionGeneration++;
        _isTransforming = false;
        state.SetTimeScale(1f);
        ClearReactionTrack(state, 0.08f);

        string clip = ResolvePhaseClip(clipBase);
        string idleLoop = ResolvePhaseClip(returnLoop ?? _baseIdleAnim);
        SetMainAnimation(state, clip, loop: false, mixDuration: 0.12f);
        if (_spineSprite.HasAnimation(idleLoop))
            state.AddAnimation(idleLoop, 0f, loop: true, MainTrack);

        PlayCustomSfx(customSfxPath);
        return Task.CompletedTask;
    }

    private Task PlayWeakProneMove(bool isLeftBroken)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        _actionGeneration++;
        _isTransforming = false;
        state.SetTimeScale(1f);
        ClearReactionTrack(state, 0.08f);

        string enterClip = ResolvePhaseClip(isLeftBroken ? "weak_enter" : "weak_enter_right");
        string idleClip = ResolvePhaseClip(isLeftBroken ? "weak_idle" : "weak_idle_right");
        SetMainAnimation(state, enterClip, loop: false, mixDuration: 0.12f);
        if (_spineSprite.HasAnimation(idleClip))
            state.AddAnimation(idleClip, 0f, loop: true, MainTrack);

        PlayCustomSfx("res://sfx/cave_god/toss_impact.mp3");
        return Task.CompletedTask;
    }

    private Task PlayEnrageTransformMove()
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        int gen = ++_actionGeneration;
        ClearReactionTrack(state, 0.08f);

        if (!_spineSprite.HasAnimation("main_angry"))
        {
            _isAngry = !_isAngry;
            _isTransforming = false;
            state.SetTimeScale(1f);
            SetMainAnimation(state, ResolvePhaseClip("idle_front"), loop: true, mixDuration: 0.2f);
            return Task.CompletedTask;
        }

        _isAngry = false;
        _isTransforming = true;
        const float animSpeed = 1.5f;
        state.SetTimeScale(animSpeed);
        SetMainAnimation(state, "main_angry", loop: false, mixDuration: 0.12f);
        if (_spineSprite.HasAnimation("idle_front_angry"))
            state.AddAnimation("idle_front_angry", 0f, loop: true, MainTrack);

        PlayCustomSfx("res://sfx/cave_god/anger_roar.mp3");

        float duration = state.GetCurrentAnimationDuration(MainTrack) is { } d && d > 0.1f
            ? d / animSpeed
            : 11.2f / animSpeed;

        SceneTree? tree = GetTree();
        if (tree != null)
        {
            tree.CreateTimer(duration).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _actionGeneration != gen)
                    return;
                _isTransforming = false;
                _isAngry = true;
                _spineSprite?.TryGetAnimationState()?.SetTimeScale(1f);
            };
        }

        return Task.CompletedTask;
    }

    private Task PlayHurtMove(bool isClaw)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        PlayStoneFlash();
        if (!isClaw)
        {
            string hurtAnim = ResolvePhaseClip("hit_recoil");
            if (_spineSprite.HasAnimation(hurtAnim))
            {
                state.SetAnimation(hurtAnim, loop: false, ReactionTrack);
                using (TrackEntryScope(state.GetCurrent(ReactionTrack), out MegaTrackEntry? track))
                {
                    if (track != null)
                    {
                        track.BoundObject.Call("set_mix_blend", SpineMixBlendAdd);
                        track.BoundObject.Call("set_alpha", 1f);
                        track.SetMixDuration(0.12f);
                    }
                }
                using (state.BoundObject.Call("add_empty_animation", ReactionTrack, 0.12f, 0f))
                {
                }
            }
        }

        PlayCustomSfx("res://sfx/cave_god/cave_god_hurt");
        return Task.CompletedTask;
    }

    private Task PlayBodyDieMove()
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        _actionGeneration++;
        _isTransforming = false;
        state.SetTimeScale(1f);
        ClearReactionTrack(state, 0.08f);

        string hideAnim = ResolvePhaseClip("hide");
        if (_spineSprite.HasAnimation(hideAnim))
        {
            SetMainAnimation(state, hideAnim, loop: false, mixDuration: 0.12f);
        }

        PlayCustomSfx("res://sfx/cave_god/rock_death.mp3");
        return Task.CompletedTask;
    }

    private Task PlayClawDieMove()
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        _actionGeneration++;
        _isTransforming = false;
        state.SetTimeScale(1f);
        ClearReactionTrack(state, 0.08f);
        PlayStoneFlash();

        if (_spineSprite.HasAnimation("grab_slam"))
        {
            SetMainAnimation(state, "grab_slam", loop: false, mixDuration: 0.10f);
            if (_spineSprite.HasAnimation("idle_front"))
                state.AddAnimation("idle_front", 0f, loop: true, MainTrack);
        }

        PlayFmodSfx(_activeModel?.DeathSfx);
        return Task.CompletedTask;
    }

    private string ResolvePhaseClip(string baseClip)
    {
        if (_spineSprite == null)
            return baseClip;

        if (_isAngry && !baseClip.EndsWith("_angry", StringComparison.Ordinal))
        {
            string angry = baseClip + "_angry";
            if (_spineSprite.HasAnimation(angry))
                return angry;
        }
        else if (!_isAngry && baseClip.EndsWith("_angry", StringComparison.Ordinal))
        {
            string calm = baseClip[..^"_angry".Length];
            if (_spineSprite.HasAnimation(calm))
                return calm;
        }

        return baseClip;
    }

    private void SetMainAnimation(MegaAnimationState state, string animName, bool loop, float mixDuration)
    {
        if (_spineSprite == null || !_spineSprite.HasAnimation(animName))
            return;

        state.SetAnimation(animName, loop, MainTrack);
        using (TrackEntryScope(state.GetCurrent(MainTrack), out MegaTrackEntry? entry))
        {
            entry?.SetMixDuration(mixDuration);
        }
    }

    private static void ClearReactionTrack(MegaAnimationState state, float mixDuration)
    {
        using (state.BoundObject.Call("set_empty_animation", ReactionTrack, mixDuration))
        {
        }
    }

    private void PlayStoneFlash()
    {
        if (_spineNode == null || !GodotObject.IsInstanceValid(_spineNode) || !IsInsideTree())
            return;

        KillFlashTween();
        _spineNode.Modulate = _isAngry ? new Color(1.5f, 0.9f, 0.8f, 1f) : new Color(1.35f, 1.35f, 1.45f, 1f);
        _flashTween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _flashTween.TweenProperty(_spineNode, "modulate", Colors.White, 0.14);
    }

    private void KillFlashTween()
    {
        if (_flashTween != null && GodotObject.IsInstanceValid(_flashTween))
            _flashTween.Kill();
        _flashTween = null;
    }

    private static void PlayCustomSfx(string? path)
    {
        if (TestMode.IsOn || string.IsNullOrEmpty(path))
            return;
        NativeSfxPlayer.Play(path, volumeDb: 2f, allowCombatEnding: true);
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
            Log.Warn($"[BestiaryLayoutCaveGod] Cache load fallback for '{path}': {ex.Message}");
            return ResourceLoader.Load<Resource>(path);
        }
    }
}
