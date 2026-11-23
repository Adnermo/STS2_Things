using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsLanternSpongeEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.LanternFish, DepthsEncounterTags.Sponge];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;
    public override IReadOnlyList<string> Slots => ["fish_1", "sponge", "fish_2"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ModelDb.Monster<ThingsLanternFish>(), ModelDb.Monster<ThingsWaterSponge>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var fish1 = (ThingsLanternFish)ModelDb.Monster<ThingsLanternFish>().ToMutable();
        fish1.SetOpeningPhase(0);
        var sponge = ModelDb.Monster<ThingsWaterSponge>().ToMutable();
        var fish2 = (ThingsLanternFish)ModelDb.Monster<ThingsLanternFish>().ToMutable();
        fish2.SetOpeningPhase(1);
        return
        [
            (fish1, "fish_1"),
            (sponge, "sponge"),
            (fish2, "fish_2"),
        ];
    }
}
