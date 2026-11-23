using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsMycorrhizalTwinsElite : EncounterModel
{
    public override RoomType RoomType => RoomType.Elite;
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["vanguard", "bulwark"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsMycorrhizalVanguard>(),ModelDb.Monster<ThingsMycorrhizalBulwark>()];
    protected override IReadOnlyList<(MonsterModel,string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsMycorrhizalVanguard>().ToMutable(),"vanguard"),(ModelDb.Monster<ThingsMycorrhizalBulwark>().ToMutable(),"bulwark")];
}
