using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

/// <summary>The three-fish regular encounter in Depths; preserves the original test encounter ID.</summary>
public sealed class ThingsLanternFishEncounter : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.LanternFish];
    public override bool HasScene => true;
    public override IReadOnlyList<string> Slots => ["first", "second", "third"];
    protected override bool HasCustomBackground => true;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<ThingsLanternFish>()];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var fish = new List<(MonsterModel, string?)>();
        int index = 0;
        foreach (int phase in new[] { 0, 1, 3 })
        {
            var monster = (ThingsLanternFish)ModelDb.Monster<ThingsLanternFish>().ToMutable();
            monster.SetOpeningPhase(phase);
            fish.Add((monster, Slots[index++]));
        }
        return fish;
    }
}
