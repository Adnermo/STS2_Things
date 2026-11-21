using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;
using STS2_Things.Powers;

namespace STS2_Things.Encounters;

/// <summary>
/// 渊墨巨蛸 (Abyssal Inkmaw) — Act 2「深处」(ThingsDepths) 巨物型水域古桥 Boss 战。
/// 玩家立于古桥中央直面深湖中的半沉巨蛸，左右可击杀主触腕自湖面破浪伸出（二阶段可砸穿桥面石板），
/// 本体以冲出屏底的高压柱状墨炮横扫桥面并将手牌染黑成盲盒，斩断任意触腕触发反伤并即刻冲净手牌。
/// </summary>
public sealed class ThingsAbyssalInkmawBossEncounter : ModBossEncounter
{
    public const string OuterLeftTentacleSlot = "outer_left_tentacle";
    public const string LeftTentacleSlot = "left_tentacle";
    public const string BossSlot = "boss";
    public const string RightTentacleSlot = "right_tentacle";
    public const string OuterRightTentacleSlot = "outer_right_tentacle";

    protected override string IconName => "abyssal_inkmaw";

    public override RoomType RoomType => RoomType.Boss;

    // 渊墨巨蛸专属 Boss 战曲（Act 2「深处」地下暗湖巨物与狂潮无缝循环）
    public override string CustomBgm => ConfiguredBgm("res://music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav");

    public override bool HasScene => true;

    protected override bool HasCustomBackground => true;

    public override bool FullyCenterPlayers => true;

    public override float GetCameraScaling() => 0.72f;

    public override Vector2 GetCameraOffset() => Vector2.Zero;

    public override IReadOnlyList<string> Slots =>
    [
        OuterLeftTentacleSlot,
        LeftTentacleSlot,
        BossSlot,
        RightTentacleSlot,
        OuterRightTentacleSlot
    ];

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
    [
        ModelDb.Monster<ThingsAbyssalCrushingTentacle>(),
        ModelDb.Monster<ThingsAbyssalInkmaw>(),
        ModelDb.Monster<ThingsAbyssalInkTentacle>(),
        ModelDb.Monster<ThingsAbyssalSpikeTentacle>(),
        ModelDb.Monster<ThingsAbyssalShellTentacle>(),
        ModelDb.Monster<ThingsAbyssalGlandTentacle>()
    ];

    public override IEnumerable<string> ExtraAssetPaths =>
    [
        "res://music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav",
        "res://images/map/abyssal_inkmaw_boss_icon.png",
        "res://images/map/abyssal_inkmaw_boss_icon_outline.png",
        "res://images/ui/run_history/things_abyssal_inkmaw_boss_encounter.png",
        "res://images/ui/run_history/things_abyssal_inkmaw_boss_encounter_outline.png",
        "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_bg_water.png",
        "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_bridge.png",
        "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_fg_frame.png",
        "res://images/ui/cards/abyssal_ink_card_frame.png",
        "res://images/ui/cards/abyssal_ink_energy_orb.png",
        "res://images/vfx/abyssal_ink_screen_splatter.png",
        "res://shaders/things_abyssal_lake_water.gdshader",
        "res://shaders/cards/abyssal_ink_blackout.gdshader",
        "res://shaders/vfx/abyssal_ink_screen_drench.gdshader",
        "res://STS2_Things/animations/monsters/abyssal_inkmaw/abyssal_inkmaw_skel_data.tres",
        "res://STS2_Things/animations/monsters/abyssal_inkmaw/abyssal_inkmaw.png",
        "res://STS2_Things/animations/monsters/abyssal_ink_beam/abyssal_ink_beam_skel_data.tres",
        "res://STS2_Things/animations/monsters/abyssal_ink_beam/abyssal_ink_beam.png",
        "res://STS2_Things/animations/monsters/abyssal_crushing_tentacle/abyssal_crushing_tentacle_skel_data.tres",
        "res://STS2_Things/animations/monsters/abyssal_crushing_tentacle/abyssal_crushing_tentacle.png",
        "res://STS2_Things/animations/monsters/abyssal_ink_tentacle/abyssal_ink_tentacle_skel_data.tres",
        "res://STS2_Things/animations/monsters/abyssal_ink_tentacle/abyssal_ink_tentacle.png",
        "res://STS2_Things/animations/monsters/abyssal_small_tentacle/abyssal_small_tentacle_skel_data.tres",
        "res://STS2_Things/animations/monsters/abyssal_small_tentacle/abyssal_small_tentacle.png",
        ModelDb.Power<ThingsAbyssalCoverPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsInkBlackoutPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalSurgePower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempStrengthPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempThornsPower>().ResolvedBigIconPath,
        ModelDb.Power<ThingsAbyssalTempWardPower>().ResolvedBigIconPath,
        ..ModelDb.Monster<ThingsAbyssalCrushingTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalInkmaw>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalInkTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalSpikeTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalShellTentacle>().AssetPaths,
        ..ModelDb.Monster<ThingsAbyssalGlandTentacle>().AssetPaths
    ];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return
        [
            (ModelDb.Monster<ThingsAbyssalCrushingTentacle>().ToMutable(), LeftTentacleSlot),
            (ModelDb.Monster<ThingsAbyssalInkmaw>().ToMutable(), BossSlot),
            (ModelDb.Monster<ThingsAbyssalInkTentacle>().ToMutable(), RightTentacleSlot)
        ];
    }
}
