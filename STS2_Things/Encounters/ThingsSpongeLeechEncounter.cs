using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsSpongeLeechEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.Leech, DepthsEncounterTags.Sponge];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["sponge", "leech_1", "leech_2"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ModelDb.Monster<ThingsWaterSponge>(), ModelDb.Monster<ThingsSanguineLeech>()];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var first = (ThingsSanguineLeech)ModelDb.Monster<ThingsSanguineLeech>().ToMutable();
        var second = (ThingsSanguineLeech)ModelDb.Monster<ThingsSanguineLeech>().ToMutable();
        first.SetOpeningPhase(0);
        second.SetOpeningPhase(2);
        return [(ModelDb.Monster<ThingsWaterSponge>().ToMutable(), "sponge"), (first, "leech_1"), (second, "leech_2")];
    }
}
