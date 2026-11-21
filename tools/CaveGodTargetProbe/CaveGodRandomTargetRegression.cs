using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2_Things.Monsters;
using STS2_Things.Powers;

public partial class CaveGodProbeNode
{
    private static AttackCommand? _observedRandomAttack;
    private static void ObserveRandomAttack(AttackCommand __instance)
    {
        if (__instance.ModelSource is Stardust) _observedRandomAttack = __instance;
    }

    private static AttackCommand RandomAttack(Fixture fixture, CardModel card, decimal damagePerHit, int hits)
    {
        var command = DamageCmd.Attack(damagePerHit).WithHitCount(hits);
        MethodInfo fromCard = typeof(AttackCommand).GetMethods().Single(m => m.Name == "FromCard");
        fromCard.Invoke(command, fromCard.GetParameters().Length == 1 ? [card] : [card, null]);
        return command.TargetingRandomOpponents(fixture.State);
    }

    private static async Task CheckRandomAttack(Fixture fixture, CardModel card, bool bodyBulwarked, params Creature[] allowed)
    {
        // Heal body & allowed arms so 24 hits of 4 damage never trigger a mid-command state change here.
        fixture.Body.Creature.SetMaxHpInternal(5000);
        fixture.Body.Creature.SetCurrentHpInternal(5000);
        foreach (Creature target in allowed)
        {
            if (target != fixture.Body.Creature)
            {
                target.SetMaxHpInternal(5000);
                target.SetCurrentHpInternal(5000);
            }
        }

        AttackCommand command = RandomAttack(fixture, card, 4m, 24);
        await command.Execute(Choice);
        var hits = command.Results.SelectMany(r => r).ToArray();
        GD.Print("CAVEGOD_RANDOM_HITS " + string.Join(",", hits.Select(h => h.Receiver.Monster?.Id.Entry ?? "player")));
        Assert(hits.Length == 24, "Every announced random hit must resolve.");
        Assert(hits.All(h => allowed.Contains(h.Receiver)), "Random attack wasted a hit on a downed or grabbing arm.");
        foreach (var hit in hits)
        {
            int expected = hit.Receiver == fixture.Body.Creature && bodyBulwarked ? 1 : 4;
            Assert(hit.UnblockedDamage == expected,
                $"Random hit on {hit.Receiver.Monster?.Id.Entry} dealt {hit.UnblockedDamage}, expected {expected} (bulwarked={bodyBulwarked}).");
        }
    }

    private static async Task<AttackCommand> PlayNativeStardust(Fixture fixture)
    {
        CardModel card = fixture.State.CreateCard(ModelDb.Card<Stardust>(), fixture.State.Players[0]);
        card.Owner.PlayerCombatState!.Hand.AddInternal(card);
        card.LastStarsSpent = 6;
        var play = (CardPlay)Activator.CreateInstance(typeof(CardPlay))!;
        foreach (var (name, value) in new (string, object?)[] {
            ("Card", card), ("Player", card.Owner), ("Target", null), ("ResultPile", PileType.Discard),
            ("Resources", new ResourceInfo { EnergySpent = 0, EnergyValue = 0, StarsSpent = 6, StarValue = 6 }),
            ("IsAutoPlay", false), ("PlayIndex", 0), ("PlayCount", 1)
        })
        {
            PropertyInfo? property = typeof(CardPlay).GetProperty(name);
            if (name != "Player" && property == null) throw new MissingMemberException(typeof(CardPlay).FullName, name);
            property?.SetValue(play, value);
        }
        _observedRandomAttack = null;
        await (Task)typeof(Stardust).GetMethod("OnPlay", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(card, [Choice, play])!;
        return _observedRandomAttack!;
    }

    private static async Task VerifyCaveGodRandomTargets()
    {
        var harmony = new Harmony("CaveGod.RandomTargetProbe");
        Type? patch = ImplementationAssembly.GetType("STS2_Things.Hooks.CaveGodRandomTargetPatch");
        if (patch != null) harmony.CreateClassProcessor(patch).Patch();
        harmony.Patch(AccessTools.Method(typeof(AttackCommand), nameof(AttackCommand.Execute)),
            prefix: new HarmonyMethod(typeof(CaveGodProbeNode), nameof(ObserveRandomAttack)));
        try
        {
            foreach (int count in new[] { 1, 2, 4 })
            {
                var s = Scenario(count);
                CardModel card = s.State.CreateCard(ModelDb.Card<Stardust>(), s.State.Players[0]);
                // Default state: Body and both healthy arms are hittable; Body has Granite Bulwark (25% damage).
                Assert(s.Body.Creature.IsHittable && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
                    "Body must be hittable by default with Granite Bulwark active.");
                await CheckRandomAttack(s, card, bodyBulwarked: true, s.Left.Creature, s.Right.Creature, s.Body.Creature);

                // Ordinary AOE and explicit targeting retain their native candidate lists.
                MethodInfo candidates = AccessTools.Method(typeof(AttackCommand), "GetPossibleTargets");
                var aoe = DamageCmd.Attack(1m);
                MethodInfo fromCard = typeof(AttackCommand).GetMethods().Single(m => m.Name == "FromCard");
                fromCard.Invoke(aoe, fromCard.GetParameters().Length == 1 ? [card] : [card, null]);
                aoe.TargetingAllOpponents(s.State);
                var unchanged = (IReadOnlyList<Creature>)candidates.Invoke(aoe, null)!;
                Assert(unchanged.Contains(s.Body.Creature) && unchanged.Count == 3, "AOE target behavior must remain native.");
                var explicitTarget = DamageCmd.Attack(1m);
                fromCard.Invoke(explicitTarget, fromCard.GetParameters().Length == 1 ? [card] : [card, null]);
                explicitTarget.Targeting(s.Body.Creature);
                unchanged = (IReadOnlyList<Creature>)candidates.Invoke(explicitTarget, null)!;
                Assert(unchanged.SequenceEqual([s.Body.Creature]), "Explicit targeting must remain native.");

                // Breaking Left arm removes Granite Bulwark and excludes the downed arm from random hits.
                await Hit(s, s.Left.Creature, 100000m);
                Assert(s.Left.Creature.IsAlive && !s.Left.Creature.IsHittable, "Downed arm is alive but cannot be hit.");
                Assert(!s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(), "Breaking an arm removes Granite Bulwark.");
                await CheckRandomAttack(s, card, bodyBulwarked: false, s.Body.Creature, s.Right.Creature);

                // Breaking Right arm leaves only the exposed Body hittable at 100% damage.
                await Hit(s, s.Right.Creature, 100000m);
                Assert(s.Right.Creature.IsAlive && !s.Right.Creature.IsHittable, "Second downed arm cannot be hit.");
                await CheckRandomAttack(s, card, bodyBulwarked: false, s.Body.Creature);

                // Two exposure turns awaken the boss, restoring both arms and Granite Bulwark.
                for (int i = 0; i < 2; i++) await Act(s);
                Assert(!s.Body.IsWeakPhase && s.Body.Creature.HasPower<ThingsCaveGodAncientCorePower>(),
                    "Awakening after 2 exposure turns must restore both arms and Granite Bulwark.");
                await CheckRandomAttack(s, card, bodyBulwarked: true, s.Left.Creature, s.Right.Creature, s.Body.Creature);

                await CreatureCmd.Stun(s.Left.Creature);
                Assert(s.Left.Creature.IsHittable, "A normal stun must not exclude a healthy arm.");
                await CheckRandomAttack(s, card, bodyBulwarked: true, s.Left.Creature, s.Right.Creature, s.Body.Creature);
                DeactivateSyntheticCombat();

                // Execute actual native Stardust OnPlay (6 hits of 5 base damage: 5 on arms, 1 on bulwarked Body).
                s = Scenario(count);
                var starHits = (await PlayNativeStardust(s)).Results.SelectMany(r => r).ToArray();
                Assert(starHits.Length == 6, "Native Stardust must resolve all 6 hits.");
                int armHitCount = starHits.Count(h => h.Receiver != s.Body.Creature);
                int bodyHitCount = starHits.Count(h => h.Receiver == s.Body.Creature);
                Assert(starHits.Sum(h => h.UnblockedDamage) == armHitCount * 5 + bodyHitCount * 1,
                    "Native Stardust hits must deal 5 to healthy arms and 1 (25%) to bulwarked Body.");
                DeactivateSyntheticCombat();

                // Mid-command arm break: arms start at 2 HP, so the first arm hit breaks an arm,
                // excludes that arm from remaining hits, and removes Granite Bulwark mid-command.
                s = Scenario(count);
                s.Left.Creature.SetCurrentHpInternal(2);
                s.Right.Creature.SetCurrentHpInternal(2);
                starHits = (await PlayNativeStardust(s)).Results.SelectMany(r => r).ToArray();
                Assert(starHits.Length == 6, "Mid-command break must still resolve all 6 Stardust hits.");
                int firstArmHitIdx = Array.FindIndex(starHits, h => h.Receiver != s.Body.Creature);
                if (firstArmHitIdx >= 0)
                {
                    Creature brokenArm = starHits[firstArmHitIdx].Receiver;
                    Assert(starHits.Skip(firstArmHitIdx + 1).All(h => h.Receiver != brokenArm),
                        "Subsequent hits in the same command must not target an arm that just broke.");
                    Assert(starHits.Skip(firstArmHitIdx + 1).Where(h => h.Receiver == s.Body.Creature).All(h => h.UnblockedDamage == 5),
                        "Body hits after mid-command arm break must immediately deal 100% full damage (5).");
                }
                DeactivateSyntheticCombat();
            }
            GD.Print("CaveGod random target regression: PASS (native Stardust, 1/2/4-player models, bulwarked/exposed/recovering/stunned)");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
