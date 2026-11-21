using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Monsters;

namespace STS2_Things.Encounters;

public sealed class ThingsBowlbugProgenitorBossEncounter : ModBossEncounter
{
    protected override string IconName => "bowlbug_progenitor";

    public override RoomType RoomType => RoomType.Boss;

    // 盛碗虫族母专属 Boss 战曲（沿用原人面塔 Subterranean Siege 无缝循环母带）
    public override string CustomBgm => ConfiguredBgm("res://music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav");

    public override bool HasScene => true;

    protected override bool HasCustomBackground => true;

    public override float GetCameraScaling() => 0.85f;

    public override Vector2 GetCameraOffset() => new Vector2(-40f, -10f);

    public override IEnumerable<string> ExtraAssetPaths => new[]
    {
        "res://music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav",
        "res://images/ui/run_history/things_bowlbug_progenitor_boss_encounter.png",
        "res://images/ui/run_history/things_bowlbug_progenitor_boss_encounter_outline.png"
    };

    // Minions act left-to-right before the progenitor (cf. QueenBoss), so
    // previewed minion attack damage is never inflated by a same-turn +3 Strength buff.
    public override IReadOnlyList<string> Slots =>
        Enumerable.Range(1, 16).Select(i => $"bowlbug_{i}")
            .Concat(["bowlbug_progenitor"])
            .ToArray();

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
    {
        ModelDb.Monster<ThingsBowlbugProgenitor>(),
        ModelDb.Monster<BowlbugSilk>(),
        ModelDb.Monster<BowlbugRock>(),
        ModelDb.Monster<BowlbugNectar>(),
        ModelDb.Monster<BowlbugEgg>()
    };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        return new (MonsterModel, string?)[]
        {
            (ModelDb.Monster<ThingsBowlbugProgenitor>().ToMutable(),
                "bowlbug_progenitor")
        };
    }
}
