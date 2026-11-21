using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsScaleBeetleBossEncounter : ModBossEncounter
{
    protected override string IconName => "scale_beetle";

    public override RoomType RoomType => RoomType.Boss;

    // 放缩巨甲虫专属 Boss 战曲（Act 1 节肢跃动与巨力反差无缝循环）
    public override string CustomBgm => ConfiguredBgm("res://music/scale_beetle/scale_beetle_boss_theme.wav");
    public override bool HasScene => true;
    protected override bool HasCustomBackground => true;

    public override IEnumerable<string> ExtraAssetPaths => new[]
    {
        "res://music/scale_beetle/scale_beetle_boss_theme.wav",
        "res://images/ui/run_history/things_scale_beetle_boss_encounter.png",
        "res://images/ui/run_history/things_scale_beetle_boss_encounter_outline.png"
    };

    public override IReadOnlyList<string> Slots => ["scale_beetle"];

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<ThingsScaleBeetle>()
    };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new (MonsterModel, string?)[]
        {
            (ModelDb.Monster<ThingsScaleBeetle>().ToMutable(), "scale_beetle")
        };
    }
}
