using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

/// <summary>
/// Depths Act 2 Regular Encounter: opens with one Large Prismatic Slime that interrupts into a half-HP
/// inherited-HP split (Large -&gt; 2 asymmetric Mediums -&gt; up to 4 same-color Smalls).
/// </summary>
public sealed class ThingsChromaticSlimeWeak : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool IsWeak => false;
    public override IEnumerable<EncounterTag> Tags => [DepthsEncounterTags.ChromaticSlime];
    public override bool HasScene => true;
    protected override bool HasCustomBackground => false;

    public override float GetCameraScaling() => 0.94f;

    public override IReadOnlyList<string> Slots =>
    [
        "small_1",
        "med_1",
        "small_2",
        "large",
        "small_3",
        "med_2",
        "small_4",
    ];

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
    [
        ModelDb.Monster<ThingsChromaticSlimeLarge>(),
        ModelDb.Monster<ThingsChromaticSlimeMediumCrimson>(),
        ModelDb.Monster<ThingsChromaticSlimeMediumAmber>(),
        ModelDb.Monster<ThingsChromaticSlimeMediumAzure>(),
        ModelDb.Monster<ThingsChromaticSlimeSmallCrimson>(),
        ModelDb.Monster<ThingsChromaticSlimeSmallAmber>(),
        ModelDb.Monster<ThingsChromaticSlimeSmallAzure>(),
    ];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
        [(ModelDb.Monster<ThingsChromaticSlimeLarge>().ToMutable(), "large")];
}
