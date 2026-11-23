using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsSilkMothTrio : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.SilkMoth];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["moth_left", "great_moth", "moth_right"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsSilkMoth>(), ModelDb.Monster<ThingsGreatSilkMoth>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var left = (ThingsSilkMoth)ModelDb.Monster<ThingsSilkMoth>().ToMutable();
        var center = (ThingsGreatSilkMoth)ModelDb.Monster<ThingsGreatSilkMoth>().ToMutable();
        var right = (ThingsSilkMoth)ModelDb.Monster<ThingsSilkMoth>().ToMutable();
        left.SetOpeningPhase(1);
        center.SetOpeningPhase(0);
        right.SetOpeningPhase(2);
        return [(left, "moth_left"), (center, "great_moth"), (right, "moth_right")];
    }
}
