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

public partial class CaveGodProbeNode
{
    private static AttackCommand? _observedRandomAttack;
    private static void ObserveRandomAttack(AttackCommand __instance)
    {
        if (__instance.ModelSource is Stardust) _observedRandomAttack = __instance;
    }

    private static AttackCommand RandomAttack(Fixture fixture, CardModel card, int hits)
    {
        var command = DamageCmd.Attack(1m).WithHitCount(hits);
        MethodInfo fromCard = typeof(AttackCommand).GetMethods().Single(m => m.Name == "FromCard");
        fromCard.Invoke(command, fromCard.GetParameters().Length == 1 ? [card] : [card, null]);
        return command.TargetingRandomOpponents(fixture.State);
    }

    private static async Task CheckRandomAttack(Fixture fixture, CardModel card, params Creature[] allowed)
    {
        AttackCommand command = RandomAttack(fixture, card, 24);
        await command.Execute(Choice);
        var hits = command.Results.SelectMany(r => r).ToArray();
        GD.Print("CAVEGOD_RANDOM_HITS " + string.Join(",", hits.Select(h => h.Receiver.Monster?.Id.Entry ?? "player")));
        Assert(hits.Length == 24, "Every announced random hit must resolve.");
        Assert(hits.All(h => allowed.Contains(h.Receiver)), "Random attack wasted a hit on a buried core or a protected arm.");
        Assert(hits.Sum(h => h.UnblockedDamage) == 24m, "Random attacks must retain their full damage against valid targets.");
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
            // Player was added to CardPlay in v111; all other fields exist in both targets.
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
        // Baseline packages have no patch; keep their real native behavior for the red run.
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
                await CheckRandomAttack(s, card, s.Left.Creature, s.Right.Creature);

                // Ordinary AOE and explicit targeting retain their original candidate lists.
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

                await Hit(s, s.Left.Creature);
                Assert(s.Left.Creature.IsAlive && !s.Left.Creature.IsHittable, "Downed arm is alive but cannot be hit.");
                await CheckRandomAttack(s, card, s.Body.Creature, s.Right.Creature);
                await Hit(s, s.Right.Creature);
                Assert(s.Right.Creature.IsAlive && !s.Right.Creature.IsHittable, "Recovering arm is alive but cannot be hit.");
                await CheckRandomAttack(s, card, s.Body.Creature);
                for (int i = 0; i < 3; i++) await Act(s);
                Assert(!s.Body.IsWeakPhase, "Native recovery must bury the core again.");
                await CheckRandomAttack(s, card, s.Left.Creature, s.Right.Creature);
                await CreatureCmd.Stun(s.Left.Creature);
                Assert(s.Left.Creature.IsHittable, "A normal stun must not exclude a healthy arm.");
                await CheckRandomAttack(s, card, s.Left.Creature, s.Right.Creature);
                DeactivateSyntheticCombat();

                // Execute the actual native Stardust OnPlay, including its star-X hit count.
                s = Scenario(count);
                var starHits = (await PlayNativeStardust(s)).Results.SelectMany(r => r).ToArray();
                Assert(starHits.Length == 6 && starHits.All(h => h.Receiver != s.Body.Creature), "Native Stardust must spend all six stars on hittable arms.");
                Assert(starHits.Sum(h => h.UnblockedDamage) == 30m, "Native Stardust damage must remain 5 per star.");
                DeactivateSyntheticCombat();

                // A hit breaks an arm inside this very same six-hit native command.
                s = Scenario(count);
                s.Left.Creature.SetCurrentHpInternal(2);
                s.Right.Creature.SetCurrentHpInternal(2);
                starHits = (await PlayNativeStardust(s)).Results.SelectMany(r => r).ToArray();
                Assert(starHits.Length == 6 && starHits[0].Receiver != s.Body.Creature, "The first hit must break an arm before core exposure.");
                Assert(starHits.Skip(1).All(h => h.Receiver != starHits[0].Receiver), "Later hits must not target the arm that just broke.");
                Assert(starHits.Count(h => h.Receiver == s.Body.Creature) >= 4, "Remaining hits must immediately include the newly exposed core.");
                DeactivateSyntheticCombat();
            }
            GD.Print("CaveGod random target regression: PASS (native Stardust, 1/2/4-player models, buried/exposed/recovering/stunned)");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
