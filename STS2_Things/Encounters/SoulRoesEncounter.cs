using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

/// <summary>
///     SoulRoes 精英遭遇
///     槽位命名默认使用 MonsterRegistrar.SlotName / SubSlotName:
///     SoulRoes → "soul_roes"
///     SoulRoe  → "soul_roe_1" ~ "soul_roe_6"
///     .tscn 场景中的 Marker2D 节点名必须与此一致。
/// </summary>
public sealed class SoulRoesEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Elite;

    public override bool HasScene => true;

    public override IReadOnlyList<string> Slots => new[]
    {
        MonsterRegistrar.SlotName<SoulRoes>(),
        MonsterRegistrar.SubSlotName<SoulRoe>(0),
        MonsterRegistrar.SubSlotName<SoulRoe>(1),
        MonsterRegistrar.SubSlotName<SoulRoe>(2),
        MonsterRegistrar.SubSlotName<SoulRoe>(3),
        MonsterRegistrar.SubSlotName<SoulRoe>(4),
        MonsterRegistrar.SubSlotName<SoulRoe>(5)
    };

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<SoulRoes>(),
        ModelDb.Monster<SoulRoe>()
    };

    public static string GetSoulRoeSlotName(int index)
    {
        return MonsterRegistrar.SubSlotName<SoulRoe>(index);
    }

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new[]
        {
            (ModelDb.Monster<SoulRoes>().ToMutable(),
                MonsterRegistrar.SlotName<SoulRoes>())
        };
    }
}