using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsQuirkyHopperWeak : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;

    public override IEnumerable<EncounterTag> Tags => [EncounterTag.Thieves];

    public override bool IsWeak => true;

    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsQuirkyHopper>()];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return [(ModelDb.Monster<ThingsQuirkyHopper>().ToMutable(), null)];
    }
}
