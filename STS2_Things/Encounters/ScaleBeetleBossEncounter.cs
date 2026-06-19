using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ScaleBeetleBossEncounter : ModBossEncounter
{
    protected override string IconName => "scale_beetle";

    public override RoomType RoomType => RoomType.Boss;

    // 复用Vantom的专属Boss音乐（有完整progress参数+升调自动化）
    public override string CustomBgm => "event:/music/act1_boss_vantom";
    public override bool HasScene => true;

    public override IEnumerable<string> ExtraAssetPaths => new[]
    {
        "res://images/monsters/scale_beetle.png",
        "res://images/backgrounds/scale_beetle_bg.png"
    };

    public override IReadOnlyList<string> Slots => new[]
    {
        MonsterRegistrar.SlotName<ScaleBeetle>()
    };

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<ScaleBeetle>()
    };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new[]
        {
            (ModelDb.Monster<ScaleBeetle>().ToMutable(),
                MonsterRegistrar.SlotName<ScaleBeetle>())
        };
    }
}