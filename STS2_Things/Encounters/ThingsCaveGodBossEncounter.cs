using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Cards;
using STS2_Things.Monsters;
using STS2_Things.Powers;

namespace STS2_Things.Encounters;

/// <summary>
/// 活体巨岩 (Living Megalith) boss encounter - Built against Kaiser Crab dual-monster architectural framework.
/// Uses 0.75x camera scaling, centered players, dual monster proxies (Left Hand & Right Hand),
/// and a full-screen dual-pass background Spine driver.
/// </summary>
public sealed class ThingsCaveGodBossEncounter : ModBossEncounter
{
    public const string LeftHandSlot = "left_hand";
    public const string RightHandSlot = "right_hand";
    public const string CaptiveHandSlot = "captive_hand";
    public const string BodySlot = "body";

    protected override string IconName => "cave_god";

    public override RoomType RoomType => RoomType.Boss;

    // 洞神专属 Boss 战曲（哥特管风琴与古神威压无缝循环）
    public override string CustomBgm => ConfiguredBgm("res://music/cave_god/cave_god_boss_theme.wav");

    public override bool HasScene => true;

    protected override bool HasCustomBackground => true;

    public override IEnumerable<string> ExtraAssetPaths =>
    [
        "res://music/cave_god/cave_god_boss_theme.wav",
        "res://sfx/cave_god/cave_god_hurt-01.wav",
        "res://sfx/cave_god/cave_god_hurt-02.wav",
        "res://sfx/cave_god/cave_god_hurt-03.wav",
        "res://sfx/cave_god/cave_god_hurt-04.wav",
        "res://sfx/cave_god/cave_god_hurt-05.wav",
        "res://sfx/cave_god/anger_roar.mp3",
        "res://sfx/cave_god/rock_death.mp3",
        "res://sfx/cave_god/rock_punch.mp3",
        "res://sfx/cave_god/toss_impact.mp3",
        "res://images/map/cave_god_boss_icon.png",
        "res://images/map/cave_god_boss_icon_outline.png",
        "res://images/ui/run_history/things_cave_god_boss_encounter.png",
        "res://images/ui/run_history/things_cave_god_boss_encounter_outline.png",
        "res://images/rooms/cave_god_boss/cavegod_bg_far_blue.png",
        "res://images/rooms/cave_god_boss/cavegod_bg_far.png",
        "res://images/rooms/cave_god_boss/cavegod_fg_platform.png",
        ModelDb.Power<ThingsCaveGodAncientCorePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsCaveGodMagmaSealPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsCaveGodAgingPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsCaveGodBrokenBladePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsCaveGodShatteredShieldPower>().ResolvedBigIconPath,
        ModelDb.Card<ThingsCaveGodBrokenBladeTrial>().PortraitPath,
        ModelDb.Card<ThingsCaveGodShatteredShieldTrial>().PortraitPath,
        ..ModelDb.Monster<ThingsCaveGodCaptiveClaw>().AssetPaths,
        ..ModelDb.Monster<ThingsCaveGodBody>().AssetPaths
    ];

    public override bool FullyCenterPlayers => true;

    public override float GetCameraScaling() => 0.75f;

    public override Vector2 GetCameraOffset() => Vector2.Down * 35f;

    public override IReadOnlyList<string> Slots => [LeftHandSlot, RightHandSlot, CaptiveHandSlot, BodySlot];

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
    [
        ModelDb.Monster<ThingsCaveGodBody>(),
        ModelDb.Monster<ThingsCaveGodLeftHand>(),
        ModelDb.Monster<ThingsCaveGodRightHand>(),
        ModelDb.Monster<ThingsCaveGodCaptiveClaw>()
    ];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return
        [
            (ModelDb.Monster<ThingsCaveGodLeftHand>().ToMutable(), LeftHandSlot),
            (ModelDb.Monster<ThingsCaveGodRightHand>().ToMutable(), RightHandSlot),
            (ModelDb.Monster<ThingsCaveGodBody>().ToMutable(), BodySlot)
        ];
    }
}
