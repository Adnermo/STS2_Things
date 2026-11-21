using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class CaveGodProbeNode
{
    private static async Task VerifyThornsRetaliation()
    {
        foreach (int count in new[] { 1, 2, 4 })
        {
            var s = Scenario(count);
            // Advance to JabsId: Turn 1 Grab -> break CaptiveClaw -> Turn 2 Escape -> Turn 3 Sweep -> Turn 4 RockSweep -> Turn 5 Jabs
            await Act(s);
            await Hit(s, s.State.Enemies.Single(c => c.Monster is ThingsCaveGodCaptiveClaw));
            await Act(s);
            await Act(s);
            await Act(s);
            Assert(s.Body.NextMove.Id == ThingsCaveGodBody.JabsId, $"Expected JabsId on turn 5, got {s.Body.NextMove.Id}.");

            foreach (var player in s.State.Players)
                await PowerCmd.Apply<ThornsPower>(Choice, player.Creature, 4m, player.Creature, null, silent: true);

            // Both arms are healthy -> Granite Bulwark reduces each 4-damage Thorns tick to 1 (25%).
            // 3 jabs * count players * 1 damage = 3 * count HP lost.
            int hp = s.Body.Creature.CurrentHp;
            await Act(s);
            Assert(s.Body.Creature.CurrentHp == hp - 3 * count,
                $"Expected {3 * count} bulwarked Thorns retaliation damage across 3 jabs, got {hp - s.Body.Creature.CurrentHp}.");
            Assert(s.Body.Creature.IsHittable && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
                "Body remains hittable with Granite Bulwark active while both arms are up.");

            // Direct out-of-attack damage also respects Granite Bulwark (8 * 0.25 = 2).
            hp = s.Body.Creature.CurrentHp;
            await Hit(s, s.Body.Creature, 8m);
            Assert(s.Body.Creature.CurrentHp == hp - 2, "Direct damage to bulwarked Body must be reduced to 25% (8 -> 2).");

            // Breaking an arm removes Granite Bulwark -> direct damage deals 100% (8 -> 8).
            await Hit(s, s.Left.Creature);
            Assert(!s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(), "Granite Bulwark must be removed when an arm breaks.");
            hp = s.Body.Creature.CurrentHp;
            await Hit(s, s.Body.Creature, 8m);
            Assert(s.Body.Creature.CurrentHp == hp - 8, "Direct damage to exposed Body must deal 100% full damage (8 -> 8).");
            DeactivateSyntheticCombat();
        }
        GD.Print("CaveGod retaliation regression: PASS (native Thorns, 1/2/4 players, 25% bulwarked vs 100% exposed)");
    }
}
