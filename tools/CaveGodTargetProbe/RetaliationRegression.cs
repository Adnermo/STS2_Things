using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.ValueProps;

public partial class CaveGodProbeNode
{
    private static async Task VerifyThornsRetaliation()
    {
        foreach (int count in new[] { 1, 2, 4 })
        {
            var s = Scenario(count);
            foreach (var player in s.State.Players)
                await PowerCmd.Apply<ThornsPower>(Choice, player.Creature, 2m, player.Creature, null, silent: true);
            int hp = s.Body.Creature.CurrentHp;
            await Act(s);
            Assert(s.Body.Creature.CurrentHp == hp - 6 * count,
                "Each of the three native body attacks must receive native Thorns retaliation from every attacked player.");
            Assert(!s.Body.Creature.IsHittable, "Retaliation must not make the buried core targetable.");
            hp = s.Body.Creature.CurrentHp;
            await Hit(s, s.Body.Creature, 5m);
            Assert(s.Body.Creature.CurrentHp == hp, "Direct out-of-attack damage must still respect the buried core's protection.");
            await Hit(s, s.Left.Creature);
            hp = s.Body.Creature.CurrentHp;
            await Hit(s, s.Body.Creature, 3m);
            Assert(s.Body.Creature.CurrentHp == hp - 3, "Direct damage must still hit an exposed core.");
            DeactivateSyntheticCombat();
        }
        GD.Print("CaveGod retaliation regression: PASS (native Thorns, 1/2/4 players, buried/exposed protection)");
    }
}
