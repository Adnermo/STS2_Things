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
/// Dedicated Bestiary presenter for 人面塔 (The Visage Tower) and all four of its stone relief faces
/// (<see cref="ThingsHumanFaceTowerWrath"/>, <see cref="ThingsHumanFaceTowerLament"/>,
/// <see cref="ThingsHumanFaceTowerBastion"/>, <see cref="ThingsHumanFaceTowerMockery"/>).
/// Mounts <c>human_face_tower_skel_data.tres</c> inside a centered clipped stage window so the active
/// concave stone face is showcased prominently without spilling over the left MoveList or right Sidebar,
/// snaps directly to <c>hold_slot_{i}</c> + face idle + Bastion seal track, and drives every preview
/// through <see cref="BestiaryMonsterMove.FromAction"/> independently of <c>CombatManager.Instance.IsOverOrEnding</c>.
/// </summary>
public sealed partial class NBestiaryLayoutHumanFaceTower : NBestiaryLayout
{
    private const string SkeletonDataPath = "res://STS2_Things/animations/monsters/human_face_tower/human_face_tower_skel_data.tres";

    private const int TurntableTrack = NHumanFaceTowerBossBackground.TurntableTrack;
    private const int BastionSealTrack = NHumanFaceTowerBossBackground.BastionSealTrack;
    private const int FaceActionTrack = NHumanFaceTowerBossBackground.FaceActionTrack;
    private const int ShatterTrackBase = NHumanFaceTowerBossBackground.ShatterTrackBase;

    // Centered Bestiary safe showcase window on 1920x1080: x = [490, 1470] (w=980), y = [190, 850] (h=660).
    // Clips the far concave cylinder wings while showcasing the active sculpted relief face (414x336 px at 0.88x)
    // and flanking curved stone wall panels cleanly clear of MonsterName, MoveContainer, Sidebar, and DialogueLine.
    private const float StageOffsetLeft = -470f;
    private const float StageOffsetRight = 510f;
    private const float StageOffsetTop = -350f;
    private const float StageOffsetBottom = 310f;

    private static readonly Vector2 TowerLocalPos = new(490f, 390f);
    private const float TowerScale = 0.88f;

    private Control? _stageClip;
    private Node2D? _spineNode;
    private MegaSprite? _spineSprite;
    private NCreature? _creature;

    private bool _spineReady;
    private int _activeSlotIndex;
    private bool _bastionBolted = true;
    private string _currentIdleAnim = "wrath_idle";
    private Action<MegaAnimationState>? _pendingSetupPose;

    public Node2D? PrimarySpineNode => _spineNode;
    public MegaSprite? PrimarySpineController => _spineSprite;

    public static NBestiaryLayoutHumanFaceTower Create() => new();

    public override void _Ready()
    {
        EnsureInitialized();
    }

    public override void _ExitTree()
    {
        _pendingSetupPose = null;
    }

    public override void Cleanup()
    {
        _creature?.QueueFreeSafely();
        _creature = null;
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

        MonsterModel monsterModel = (entry.monsterModel ?? ModelDb.Monster<ThingsHumanFaceTowerWrath>()).ToMutable();
        monsterModel.Rng = Rng.Chaotic;
        monsterModel.RunRng = new RunRngSet(Rng.Chaotic);
        monsterModel.SetUpForCombat();

        ThingsHumanFaceTowerFace face = monsterModel as ThingsHumanFaceTowerFace
            ?? (ThingsHumanFaceTowerFace)ModelDb.Monster<ThingsHumanFaceTowerWrath>().ToMutable();
        _activeSlotIndex = Math.Clamp(face.FaceSlotIndex, 0, 3);
        _bastionBolted = true;
        _currentIdleAnim = face.IdleAnimationName;

        Creature entity = new Creature(monsterModel, CombatSide.Enemy, null)
        {
            CombatState = NullCombatState.Instance
        };
        _creature = NCreature.Create(entity);
        if (_creature != null && _stageClip != null)
        {
            _stageClip.AddChildSafely(_creature);
            _creature.SetupForBestiary();
            _creature.Position = TowerLocalPos;
        }

        Action<MegaAnimationState> snapToFace = state =>
        {
            SnapToActiveFaceImmediate(state);
            _spineSprite?.BoundObject.Call("update_skeleton", 0f);
        };

        if (_spineReady && _spineSprite?.TryGetAnimationState() is { } readyState)
        {
            snapToFace(readyState);
        }
        else
        {
            _pendingSetupPose = snapToFace;
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

        return BuildMoves(face);
    }

    private void EnsureInitialized()
    {
        if (_stageClip != null && GodotObject.IsInstanceValid(_stageClip))
            return;

        Name = "BestiaryLayoutHumanFaceTower";
        SetAnchorsPreset(LayoutPreset.FullRect);
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Ignore;

        _stageClip = new Control
        {
            Name = "HumanFaceTowerBestiaryStage",
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
            Log.Error("[BestiaryLayoutHumanFaceTower] SpineSprite class is not registered.");
            return;
        }

        Resource? skelData = LoadSkeletonResource(SkeletonDataPath);
        if (skelData == null)
        {
            Log.Error($"[BestiaryLayoutHumanFaceTower] Failed to load '{SkeletonDataPath}'.");
            return;
        }

        _spineNode = ClassDB.Instantiate("SpineSprite").As<Node2D>();
        if (_spineNode == null)
        {
            Log.Error("[BestiaryLayoutHumanFaceTower] Failed to instantiate SpineSprite.");
            return;
        }

        _spineNode.Name = "HumanFaceTowerBestiarySpine";
        _spineNode.Visible = true;
        _spineNode.Position = TowerLocalPos;
        _spineNode.Scale = new Vector2(TowerScale, TowerScale);
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
        if (_pendingSetupPose != null)
        {
            Action<MegaAnimationState> pending = _pendingSetupPose;
            _pendingSetupPose = null;
            pending(state);
        }
        else
        {
            SnapToActiveFaceImmediate(state);
            _spineSprite.BoundObject.Call("update_skeleton", 0f);
        }
    }

    private void SnapToActiveFaceImmediate(MegaAnimationState state)
    {
        state.SetTimeScale(1f);
        ClearAllShatterTracks(state);
        SetTrackIfExists(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0f);
        SyncBastionSealTrack(state);
        SetTrackIfExists(state, _currentIdleAnim, loop: true, FaceActionTrack, mix: 0f);
    }

    private List<BestiaryMonsterMove> BuildMoves(ThingsHumanFaceTowerFace face)
    {
        return face switch
        {
            ThingsHumanFaceTowerLament => BuildFaceMoves(
                face,
                ("WEATHERED_WAIL", "lament_cast_wail"),
                ("BLOOD_RAIN", "lament_attack_rain"),
                isBastion: false),
            ThingsHumanFaceTowerBastion => BuildFaceMoves(
                face,
                ("BULWARK_PRESS", "bastion_fortify"),
                ("COLOSSUS_DROP", "bastion_attack_colossus"),
                isBastion: true),
            ThingsHumanFaceTowerMockery => BuildFaceMoves(
                face,
                ("SNEER_STAB", "mockery_attack_twin"),
                ("WHIRLING_FANG", "mockery_attack_fang"),
                isBastion: false),
            _ => BuildFaceMoves(
                face,
                ("HEAVY_MALLET", "wrath_attack_heavy"),
                ("DOUBLE_JAW", "wrath_attack_double"),
                isBastion: false),
        };
    }

    private List<BestiaryMonsterMove> BuildFaceMoves(
        ThingsHumanFaceTowerFace face,
        (string MoveId, string Clip) move1,
        (string MoveId, string Clip) move2,
        bool isBastion)
    {
        int slot = Math.Clamp(face.FaceSlotIndex, 0, 3);
        int nextSlot = (slot + 1) % 4;
        string stoneAttackSfx = face.StoneAttackSfx;
        string stoneRoarSfx = face.StoneRoarSfx;
        string deathSfx = face.DeathSfx;

        return
        [
            BestiaryMonsterMove.FromAction(
                MoveName(face, move1.MoveId),
                isBastion ? () => PlayBastionBulwarkMove(stoneAttackSfx) : () => PlayFaceActionMove(move1.Clip, stoneAttackSfx)),
            BestiaryMonsterMove.FromAction(
                MoveName(face, move2.MoveId),
                () => PlayFaceActionMove(move2.Clip, stoneAttackSfx)),
            BestiaryMonsterMove.FromAction(
                MoveName(face, "CYCLE_CLIMAX"),
                () => PlayFaceActionMove($"climax_cycle_roar_{slot}", stoneRoarSfx)),
            BestiaryMonsterMove.FromAction(
                MoveName(face, "COMBO_ROTATE"),
                () => PlayComboRotateMove(face.StarterAttackAnimName, slot, nextSlot, stoneAttackSfx)),
            BestiaryMonsterMove.FromAction(
                MoveName(face, "EMERGENCY_STUN"),
                isBastion ? () => PlayBastionUnboltMove(stoneRoarSfx) : () => PlayFaceActionMove($"face_stun_{slot}", stoneRoarSfx)),
            BestiaryMonsterMove.FromAction(
                new LocString("bestiary", "ACTION_NAME.hurt"),
                () => PlayFaceActionMove($"face_hurt_{slot}", null)),
            BestiaryMonsterMove.FromAction(
                new LocString("bestiary", "ACTION_NAME.die"),
                () => PlayFaceShatterMove(slot, deathSfx)),
        ];
    }

    private Task PlayFaceActionMove(string clip, string? sfx)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        ClearAllShatterTracks(state);
        SetTrackIfExists(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0.08f);
        SyncBastionSealTrack(state);

        if (_spineSprite.HasAnimation(clip))
        {
            float mix = clip == "bastion_rebolt_attack" ? 0f : 0.08f;
            SetTrackImmediate(state, clip, loop: false, FaceActionTrack, mix);
            if (_spineSprite.HasAnimation(_currentIdleAnim))
                state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
        }

        PlayFmodSfx(sfx);
        return Task.CompletedTask;
    }

    private Task PlayBastionBulwarkMove(string sfx)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        bool wasUnbolted = !_bastionBolted;
        _bastionBolted = true;
        _currentIdleAnim = "bastion_idle_bolted";

        ClearAllShatterTracks(state);
        SetTrackIfExists(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0.08f);
        SyncBastionSealTrack(state);

        string anim = wasUnbolted ? "bastion_rebolt_attack" : "bastion_fortify";
        if (_spineSprite.HasAnimation(anim))
        {
            SetTrackImmediate(state, anim, loop: false, FaceActionTrack, mix: wasUnbolted ? 0f : 0.08f);
            if (_spineSprite.HasAnimation(_currentIdleAnim))
                state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
        }

        PlayFmodSfx(sfx);
        return Task.CompletedTask;
    }

    private Task PlayBastionUnboltMove(string sfx)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        _bastionBolted = false;
        _currentIdleAnim = "bastion_idle_unbolted";

        ClearAllShatterTracks(state);
        SetTrackIfExists(state, $"hold_slot_{_activeSlotIndex}", loop: true, TurntableTrack, mix: 0.08f);
        SyncBastionSealTrack(state);

        if (_spineSprite.HasAnimation("bastion_unbolt"))
        {
            SetTrackImmediate(state, "bastion_unbolt", loop: false, FaceActionTrack, mix: 0f);
            if (_spineSprite.HasAnimation(_currentIdleAnim))
                state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
        }

        PlayFmodSfx(sfx);
        return Task.CompletedTask;
    }

    private Task PlayComboRotateMove(string starterClip, int fromSlot, int nextSlot, string sfx)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        ClearAllShatterTracks(state);
        SyncBastionSealTrack(state);

        if (_spineSprite.HasAnimation(starterClip))
        {
            SetTrackImmediate(state, starterClip, loop: false, FaceActionTrack, mix: 0.08f);
            if (_spineSprite.HasAnimation(_currentIdleAnim))
                state.AddAnimation(_currentIdleAnim, 0f, loop: true, FaceActionTrack);
        }

        string turnOut = $"combo_turn_{fromSlot}_to_{nextSlot}";
        string turnBack = $"combo_turn_{nextSlot}_to_{fromSlot}";
        string holdCurrent = $"hold_slot_{fromSlot}";
        if (_spineSprite.HasAnimation(turnOut))
        {
            SetTrackImmediate(state, turnOut, loop: false, TurntableTrack, mix: 0.05f);
            if (_spineSprite.HasAnimation(turnBack))
                state.AddAnimation(turnBack, 0.25f, loop: false, TurntableTrack);
            if (_spineSprite.HasAnimation(holdCurrent))
                state.AddAnimation(holdCurrent, 0f, loop: true, TurntableTrack);
        }

        PlayFmodSfx(sfx);
        return Task.CompletedTask;
    }

    private Task PlayFaceShatterMove(int slot, string sfx)
    {
        if (_spineSprite?.TryGetAnimationState() is not { } state)
            return Task.CompletedTask;

        SetTrackIfExists(state, $"hold_slot_{slot}", loop: true, TurntableTrack, mix: 0.05f);

        int shatterTrack = ShatterTrackBase + slot;
        string shatterClip = $"face_shatter_{slot}";
        string shatteredState = $"state_shattered_{slot}";
        if (_spineSprite.HasAnimation(shatterClip))
        {
            SetTrackImmediate(state, shatterClip, loop: false, shatterTrack, mix: 0.02f);
            if (_spineSprite.HasAnimation(shatteredState))
                state.AddAnimation(shatteredState, 0f, loop: true, shatterTrack);
        }

        PlayFmodSfx(sfx);
        return Task.CompletedTask;
    }

    private void SyncBastionSealTrack(MegaAnimationState state)
    {
        string sealAnim = _bastionBolted ? "state_bastion_bolted" : "state_bastion_unbolted";
        SetTrackIfExists(state, sealAnim, loop: true, BastionSealTrack, mix: 0f);
    }

    private static void ClearAllShatterTracks(MegaAnimationState state)
    {
        for (int i = 0; i < 4; i++)
        {
            using (state.BoundObject.Call("set_empty_animation", ShatterTrackBase + i, 0f))
            {
            }
        }
    }

    private void SetTrackIfExists(MegaAnimationState state, string animName, bool loop, int trackId, float mix)
    {
        if (_spineSprite == null || !_spineSprite.HasAnimation(animName))
            return;
        SetTrackImmediate(state, animName, loop, trackId, mix);
    }

    private static void SetTrackImmediate(MegaAnimationState state, string animName, bool loop, int trackId, float mix)
    {
        state.SetAnimation(animName, loop, trackId);
        using (TrackEntryScope(state.GetCurrent(trackId), out MegaTrackEntry? entry))
        {
            entry?.SetMixDuration(mix);
        }
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
            Log.Warn($"[BestiaryLayoutHumanFaceTower] Cache load fallback for '{path}': {ex.Message}");
            return ResourceLoader.Load<Resource>(path);
        }
    }
}
