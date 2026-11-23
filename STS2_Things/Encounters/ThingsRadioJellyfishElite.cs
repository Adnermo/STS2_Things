using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsRadioJellyfishElite : EncounterModel
{
    public override RoomType RoomType => RoomType.Elite;
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["jellyfish"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsRadioJellyfish>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsRadioJellyfish>().ToMutable(), "jellyfish")];
}
