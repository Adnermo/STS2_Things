using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class TheLegacyBossEncounter : ModBossEncounter
{
    protected override string IconName => "the_legacy";

    public override RoomType RoomType => RoomType.Boss;

    // 复用Vantom的专属Boss音乐（暗黑风格，与Legacy契合）
    public override string CustomBgm => "event:/music/act1_boss_vantom";

    public override bool HasScene => true;

    public override IEnumerable<string> ExtraAssetPaths => new[]
    {
        "res://images/monsters/the_legacy.png",
        "res://images/backgrounds/the_legacy_bg.png"
    };

    public override IReadOnlyList<string> Slots => new[]
    {
        MonsterRegistrar.SlotName<TheLegacy>()
    };

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<TheLegacy>()
    };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new[]
        {
            (ModelDb.Monster<TheLegacy>().ToMutable(),
                MonsterRegistrar.SlotName<TheLegacy>())
        };
    }
}