using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using STS2_Things.Acts;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Powers;
using MegaCrit.Sts2.Core.HoverTips;

public partial class DepthsProbeNode
{
    // An unmitigated trajectory, not a victory-rate simulation. No player
    // cards are played and no enemy is killed. Actual draw/end-turn hooks still
    // apply, so leaving Parasitism alone intentionally includes its growth.
    private async Task SampleDepthsBalance()
    {
        var custom = ModelDb.Act<Depths>().AllWeakEncounters
            .Concat(ModelDb.Act<Depths>().AllRegularEncounters)
            .Concat(ModelDb.Act<Depths>().AllEliteEncounters.Where(e => e.GetType().Assembly == typeof(Depths).Assembly)).ToArray();
        EncounterModel[] reference = [
            ModelDb.Encounter<BowlbugsWeak>(), ModelDb.Encounter<ThievingHopperWeak>(), ModelDb.Encounter<TunnelerWeak>(),
            ModelDb.Encounter<BowlbugsNormal>(), ModelDb.Encounter<ChompersNormal>(), ModelDb.Encounter<HunterKillerNormal>(),
            ModelDb.Encounter<EntomancerElite>(), ModelDb.Encounter<InfestedPrismsElite>(),
        ];
        var records = new List<object>();
        foreach (var canonical in custom.Concat(reference))
        foreach (int ascension in new[] { 0, 20 })
        foreach (int players in new[] { 1, 4 })
        {
            var b = await StrongBattle(canonical, players, ascension, "depths-balance-" + canonical.Id.Entry);
            foreach (var creature in b.Enemies) await creature.Monster!.AfterAddedToRoom();
            foreach (var creature in b.Enemies)
            {
                if (creature.Monster is ReverseSalamander salamander && creature.GetPower<ReverseCurrentPower>() is { } reflux)
                {
                    string text = reflux.HoverTips.OfType<HoverTip>().First().Description;
                    Assert(text.Contains($"[blue]{salamander.GrowthAmount}[/blue]"),
                        "Native Reflux tooltip displays the actual A0/A20 Strength gift.");
                }
                if (creature.Monster is RadioJellyfish && creature.GetPower<RadioReceptionPower>() is { } reception)
                {
                    string text = reception.HoverTips.OfType<HoverTip>().First().Description;
                    Assert(text.Contains("[blue]1[/blue]"), "Native Reception tooltip displays one point per opening stack.");
                }
            }
            var roster = b.Enemies.Select(c => new { monster = c.Monster!.Id.Entry, hp = c.MaxHp,
                baseMin = c.Monster.MinInitialHp, baseMax = c.Monster.MaxInitialHp, block = c.Block }).ToArray();
            var turns = await UnmitigatedTurns(b);
            records.Add(new { encounter = canonical.Id.Entry, source = canonical.GetType().Assembly == typeof(Depths).Assembly ? "depths" : "native",
                kind = canonical.RoomType.ToString(), weak = canonical.IsWeak, ascension, players, roster, turns });
            DeactivateSyntheticCombat();
        }
        File.WriteAllText(Path.Combine(_output, "balance-trajectories.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        Godot.GD.Print($"PASS {records.Count} native balance trajectories: A0/A20, 1/4 players; eight unmitigated turns, not win rates.");
        await SampleAct1EliteReference();
    }

    // Act 1 elites are the yardstick for the event-only Shifty Shop merchant pair. The pair is
    // prepared exactly as the event does; its moves are random branches, so every encounter is
    // sampled over many seeds. Per-slot damage lets a kill order be replayed afterwards.
    private async Task SampleAct1EliteReference()
    {
        EncounterModel[] encounters = [
            ModelDb.Encounter<BygoneEffigyElite>(), ModelDb.Encounter<ByrdonisElite>(), ModelDb.Encounter<PhrogParasiteElite>(),
            ModelDb.Encounter<PhantasmalGardenersElite>(), ModelDb.Encounter<SkulkingColonyElite>(), ModelDb.Encounter<TerrorEelElite>(),
            ModelDb.Encounter<RobberyFakeMerchantEncounter>(),
        ];
        var prepare = typeof(RobberyFakeMerchantEncounter).GetMethod("PrepareForFight", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var records = new List<object>();
        foreach (var canonical in encounters)
        foreach (int ascension in new[] { 0, 20 })
        for (int seed = 0; seed < 32; seed++)
        {
            var b = await StrongBattle(canonical, 1, ascension, $"act1-elite-{canonical.Id.Entry}-{seed}");
            foreach (var creature in b.Enemies) await creature.Monster!.AfterAddedToRoom();
            if (b.Encounter is RobberyFakeMerchantEncounter) await (Task)prepare.Invoke(b.Encounter, [b.State])!;
            var roster = b.Enemies.Select(c => new { monster = c.Monster!.Id.Entry, slot = c.SlotName, hp = c.MaxHp,
                block = c.Block, strength = c.GetPower<StrengthPower>()?.Amount ?? 0 }).ToArray();
            var turns = await UnmitigatedTurns(b);
            records.Add(new { encounter = canonical.Id.Entry, ascension, seed, roster, turns });
            DeactivateSyntheticCombat();
        }
        File.WriteAllText(Path.Combine(_output, "act1-elite-trajectories.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        Godot.GD.Print($"PASS {records.Count} Act 1 elite reference trajectories: A0/A20, single player, 32 seeds each.");
    }

    private async Task<List<object>> UnmitigatedTurns(Battle b)
    {
        foreach (var player in b.Players) { player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000); }
        var turns = new List<object>();
        for (int turn = 1; turn <= 8 && b.Enemies.Length > 0; turn++)
        {
            b.State.RoundNumber = turn;
            b.State.CurrentSide = CombatSide.Player;
            foreach (var player in b.Players)
            {
                await player.Creature.AfterTurnStart(CombatSide.Player);
                await PlayerCmd.SetEnergy(3, player);
                await CardPileCmd.Draw(Choice, 5, player);
                await Hook.AfterPlayerTurnStart(b.State, Choice, player);
            }
            await SideEnding(b, CombatSide.Player, b.Players.Select(p => p.Creature).ToArray());
            foreach (var player in b.Players) await EndPlayer(b, player);
            await SideEnded(b, CombatSide.Player, b.Players.Select(p => p.Creature).ToArray());
            b.State.CurrentSide = CombatSide.Enemy;
            var participants = b.Enemies;
            int[] hp = b.Players.Select(p => p.Creature.CurrentHp).ToArray();
            var moves = new List<object>();
            foreach (var creature in participants)
            {
                if (!creature.IsAlive) continue;
                await creature.AfterTurnStart(CombatSide.Enemy);
                var monster = creature.Monster!;
                int intent = monster.NextMove.Intents.OfType<AttackIntent>().Sum(i => i.GetTotalDamage([b.Players[0].Creature], creature));
                Assert(intent >= 0, "Nonnegative native attack intent");
                string id = monster.NextMove.Id;
                int before = b.Players[0].Creature.CurrentHp;
                await monster.PerformMove();
                moves.Add(new { monster = monster.Id.Entry, slot = creature.SlotName, move = id, intent,
                    actualDamageToFirstPlayer = before - b.Players[0].Creature.CurrentHp,
                    strengthAfter = creature.GetPower<StrengthPower>()?.Amount ?? 0, blockAfter = creature.Block });
            }
            await SideEnding(b, CombatSide.Enemy, participants);
            await SideEnded(b, CombatSide.Enemy, participants);
            turns.Add(new { turn, damageByPlayer = b.Players.Select((p, i) => hp[i] - p.Creature.CurrentHp).ToArray(), moves });
            b.State.CurrentSide = CombatSide.Player;
            foreach (var creature in b.Enemies) creature.Monster!.RollMove(b.Players.Select(p => p.Creature));
        }
        Assert(b.Players.All(p => p.Creature.IsAlive), "Measurement fixture stays alive");
        return turns;
    }
}
