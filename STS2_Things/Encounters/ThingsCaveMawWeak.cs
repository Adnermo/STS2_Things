using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsCaveMawWeak : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => true;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.CaveMaw];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => true;
    public override IReadOnlyList<string> Slots => ["maw"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsCaveMaw>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsCaveMaw>().ToMutable(), "maw")];
}
