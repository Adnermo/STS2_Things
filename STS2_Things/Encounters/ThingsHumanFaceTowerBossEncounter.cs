using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

/// <summary>
/// 人面塔 (The Visage Tower) — Act 2「深处」(ThingsDepths) exclusive cylindrical stone tower Boss encounter.
/// Features 4 independent stone relief faces rotating every 2 turns inside a monolithic 3D concave stone chamber,
/// 8-turn Full-Cycle Resonance while all 4 faces live, and Whip-Rotational 2-Hit Combos once any face shatters.
/// </summary>
public sealed class ThingsHumanFaceTowerBossEncounter : ModBossEncounter
{
    protected override string IconName => "human_face_tower";

    public override RoomType RoomType => RoomType.Boss;

    public override string CustomBgm => ConfiguredBgm("res://music/human_face_tower/human_face_tower_boss_theme.wav");

    public override bool HasScene => true;

    protected override bool HasCustomBackground => true;

    public override bool FullyCenterPlayers => true;

    public override float GetCameraScaling() => 0.72f;

    public override Vector2 GetCameraOffset() => Vector2.Zero;

    public override IReadOnlyList<string> Slots =>
    [
        HumanFaceTowerController.WrathSlot,
        HumanFaceTowerController.LamentSlot,
        HumanFaceTowerController.BastionSlot,
        HumanFaceTowerController.MockerySlot
    ];

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
    [
        ModelDb.Monster<ThingsHumanFaceTowerWrath>(),
        ModelDb.Monster<ThingsHumanFaceTowerLament>(),
        ModelDb.Monster<ThingsHumanFaceTowerBastion>(),
        ModelDb.Monster<ThingsHumanFaceTowerMockery>()
    ];

    public override IEnumerable<string> ExtraAssetPaths =>
    [
        "res://music/human_face_tower/human_face_tower_boss_theme.wav",
        "res://images/map/human_face_tower_boss_icon.png",
        "res://images/map/human_face_tower_boss_icon_outline.png",
        "res://images/ui/run_history/things_human_face_tower_boss_encounter.png",
        "res://images/ui/run_history/things_human_face_tower_boss_encounter_outline.png",
        "res://images/ui/run_history/human_face_tower_boss.png",
        "res://images/ui/run_history/human_face_tower_boss_outline.png",
        "res://images/rooms/human_face_tower/tower_bg_chamber.png",
        "res://images/rooms/human_face_tower/tower_fg_frame.png",
        "res://images/rooms/human_face_tower/tower_bottom_skirt.png",
        "res://images/rooms/human_face_tower/tower_light_shaft.png",
        "res://STS2_Things/animations/monsters/human_face_tower/human_face_tower_skel_data.tres",
        "res://STS2_Things/animations/monsters/human_face_tower/human_face_tower.png",
        ..ModelDb.Monster<ThingsHumanFaceTowerWrath>().AssetPaths,
        ..ModelDb.Monster<ThingsHumanFaceTowerLament>().AssetPaths,
        ..ModelDb.Monster<ThingsHumanFaceTowerBastion>().AssetPaths,
        ..ModelDb.Monster<ThingsHumanFaceTowerMockery>().AssetPaths
    ];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
        => HumanFaceTowerController.CreateInitial();
}
