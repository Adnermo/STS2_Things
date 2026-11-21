using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsOriginFogmogBossEncounter : ModBossEncounter
{
    protected override string IconName => "origin_fogmog";

    public override IEnumerable<string> ExtraAssetPaths => new[]
    {
        "res://music/origin_fogmog/origin_fogmog_boss_theme.wav",
        "res://images/ui/run_history/things_origin_fogmog_boss_encounter.png",
        "res://images/ui/run_history/things_origin_fogmog_boss_encounter_outline.png"
    };

    public override RoomType RoomType => RoomType.Boss;

    // 始源雾菇专属 Boss 战曲（Act 1 森林幻象暗黑室内交响无缝循环）
    public override string CustomBgm => ConfiguredBgm("res://music/origin_fogmog/origin_fogmog_boss_theme.wav");

    public override bool HasScene => true;

    protected override bool HasCustomBackground => true;

    public override IReadOnlyList<string> Slots => new[] { "fogmog", "illusion1", "illusion2" };

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<ThingsOriginFogmog>(),
        ModelDb.Monster<ThingsOriginEyeWithTeeth>()
    };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new (MonsterModel, string?)[1]
        {
            (ModelDb.Monster<ThingsOriginFogmog>().ToMutable(), "fogmog")
        };
    }
}
