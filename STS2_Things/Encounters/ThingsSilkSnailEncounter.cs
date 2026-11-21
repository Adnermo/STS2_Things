using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsSilkSnailEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.SilkMoth, DepthsEncounterTags.Snail];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => true;
    public override IReadOnlyList<string> Slots => ["moth", "rock", "slime"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ModelDb.Monster<ThingsSilkMoth>(), ModelDb.Monster<ThingsRockSnail>(), ModelDb.Monster<ThingsSlimeSnail>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsSilkMoth>().ToMutable(), "moth"),
         (ModelDb.Monster<ThingsRockSnail>().ToMutable(), "rock"),
         (ModelDb.Monster<ThingsSlimeSnail>().ToMutable(), "slime")];
}
