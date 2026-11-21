using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class DepthsProbeNode
{
    private async Task VerifySoulRoesDeathBurst()
    {
        foreach (int ascension in new[] { 0, 20 })
        foreach (int players in new[] { 1, 4 })
        {
            var battle = await StrongBattle(
                ModelDb.Encounter<ThingsSoulRoesEncounter>(), players, ascension,
                $"soul-roes-death-{ascension}-{players}");
            var parent = battle.Monster<ThingsSoulRoes>();
            Assert(parent.MinInitialHp == (ascension == 0 ? 80 : 90)
                && parent.MaxInitialHp == (ascension == 0 ? 88 : 96),
                "Parent HP bounds must be twice the old values.");
            await parent.AfterAddedToRoom();
            var burst = parent.Creature.GetPower<ThingsSoulRoesPower>();
            Assert(burst != null && burst.Amount == 6,
                "Parent retains the six-roes death burst.");

            // Exercise the native death-power callback and creature-add/power-apply
            // path. Direct invocation avoids contaminating the player's game save.
            await burst!.AfterDeath(Choice, parent.Creature,
#if STS2_V113_1
                source: null,
#endif
                wasRemovalPrevented: false,
                deathAnimLength: 0f);
            var children = battle.State.Enemies
                .Where(enemy => enemy.IsAlive && enemy.Monster is ThingsSoulRoe)
                .ToArray();
            Assert(children.Length == 6, "Death burst must create all six roes.");
            Assert(children.Select(child => child.SlotName).Distinct().Count() == 6,
                "Roes must occupy separate native spawn slots.");
            Assert(children.All(child => child.GetPower<IntangiblePower>()?.Amount == 1),
                "Every death-spawned roe must immediately have exactly 1 Intangible.");
            Assert(children.All(child => ((ThingsSoulRoe)child.Monster!).StartStunned),
                "Death-spawned roes must retain their original opening stun.");
            Assert(children.Select(child => ((ThingsSoulRoe)child.Monster!).StartMovePhase)
                .SequenceEqual(new[] { 0, 1, 2, 0, 1, 2 }),
                "Death-spawned roes retain their offset action phases.");
            DeactivateSyntheticCombat();
        }
        GD.Print("SOUL_ROES_DEATH_PASS HP A0/A20, 1p/4p, six spawns, Intangible=1, slots, stun, phases.");
    }
}
