using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsSnailTrioWeak : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => true;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.Snail];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["crystal", "slime", "rock"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ModelDb.Monster<ThingsCrystalSnail>(), ModelDb.Monster<ThingsSlimeSnail>(), ModelDb.Monster<ThingsRockSnail>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsCrystalSnail>().ToMutable(), "crystal"),
         (ModelDb.Monster<ThingsSlimeSnail>().ToMutable(), "slime"),
         (ModelDb.Monster<ThingsRockSnail>().ToMutable(), "rock")];
}
