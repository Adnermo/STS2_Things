using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Enchantments;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Monsters;
using STS2_Things.Powers;
using STS2_Things.Relics;

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
            Assert(parent.MinInitialHp == (ascension == 0 ? 66 : 74)
                && parent.MaxInitialHp == (ascension == 0 ? 72 : 80),
                $"Parent HP bounds must match reduced values (got {parent.MinInitialHp}..{parent.MaxInitialHp}).");
            await parent.AfterAddedToRoom();
            var burst = parent.Creature.GetPower<ThingsSoulRoesPower>();
            Assert(burst != null && burst.Amount == 6,
                "Parent retains the six-roes death burst.");

            // T1 BECKON_STR_MOVE adds 2 Ethereal + Exhaust Beckon cards per player.
            Assert(parent.NextMove.Id == "BECKON_STR_MOVE", "Parent opens with BECKON_STR_MOVE.");
            await parent.PerformMove();
            foreach (var p in battle.Players)
            {
                var parentBeckons = PileType.Discard.GetPile(p).Cards.Where(c => c is Beckon).ToArray();
                Assert(parentBeckons.Length == 2,
                    $"Parent BECKON_STR_MOVE must add 2 Beckons to player discard (got {parentBeckons.Length}).");
                Assert(parentBeckons.All(c =>
                        c.Keywords.Contains(CardKeyword.Ethereal) && c.Keywords.Contains(CardKeyword.Exhaust)),
                    "Every Beckon added by ThingsSoulRoes must have both Ethereal and Exhaust.");
            }

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
            Assert(children.All(child =>
                    child.Monster!.MinInitialHp == (ascension == 0 ? 3 : 5)
                    && child.Monster!.MaxInitialHp == (ascension == 0 ? 5 : 6)),
                "Child ThingsSoulRoe HP bounds must match reduced values (A0: 3..5, A20: 5..6).");
            Assert(children.Select(child => child.SlotName).Distinct().Count() == 6,
                "Roes must occupy separate native spawn slots.");
            Assert(children.All(child => child.GetPower<IntangiblePower>()?.Amount == 1),
                "Every death-spawned roe must immediately have exactly 1 Intangible.");
            Assert(children.All(child => ((ThingsSoulRoe)child.Monster!).StartStunned),
                "Death-spawned roes must retain their original opening stun.");
            Assert(children.Select(child => ((ThingsSoulRoe)child.Monster!).StartMovePhase)
                .SequenceEqual(new[] { 0, 1, 2, 0, 1, 2 }),
                "Death-spawned roes retain their offset action phases.");

            // Child phase-1 roe transitions from opening stun to BECKON_MOVE and adds 1 Ethereal + Exhaust Beckon per player.
            var beckonChild = (ThingsSoulRoe)children.First(c => ((ThingsSoulRoe)c.Monster!).StartMovePhase == 1).Monster!;
            beckonChild.MoveStateMachine!.OnMovePerformed(beckonChild.NextMove);
            beckonChild.RollMove(battle.Players.Select(p => p.Creature).ToArray());
            Assert(beckonChild.NextMove.Id == "BECKON_MOVE", "Phase-1 child rolls BECKON_MOVE after stun.");
            await beckonChild.PerformMove();
            foreach (var p in battle.Players)
            {
                var allBeckons = PileType.Discard.GetPile(p).Cards.Where(c => c is Beckon).ToArray();
                Assert(allBeckons.Length == 3,
                    $"Child BECKON_MOVE must add 1 more Beckon (expected 3 total, got {allBeckons.Length}).");
                Assert(allBeckons.All(c =>
                        c.Keywords.Contains(CardKeyword.Ethereal) && c.Keywords.Contains(CardKeyword.Exhaust)),
                    "Every Beckon added by ThingsSoulRoe must have both Ethereal and Exhaust.");
            }

            DeactivateSyntheticCombat();
        }

        // Verify ThingsBackrooms (跌倒) EXIT page options expose Almond Water relic & Disperse enchantment previews.
        {
            var run = CreateRun("backrooms-exit-preview", 1, 0);
            var backrooms = (ThingsBackrooms)ModelDb.Event<ThingsBackrooms>().ToMutable();
            var begin = typeof(EventModel).GetMethods().Single(m => m.Name == "BeginEvent");
            object?[] beginArgs = begin.GetParameters().Select(p =>
                p.ParameterType == typeof(MegaCrit.Sts2.Core.Entities.Players.Player) ? (object)run.Players[0] :
                p.ParameterType == typeof(bool) ? false : null).ToArray();
            await (Task)begin.Invoke(backrooms, beginArgs)!;
            var showExit = typeof(ThingsBackrooms).GetMethod("ShowExitPage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(showExit != null, "ThingsBackrooms.ShowExitPage must exist.");
            showExit!.Invoke(backrooms, null);

            var almondOpt = backrooms.CurrentOptions.Single(o => o.TextKey.EndsWith(".TAKE_ALMOND_WATER", StringComparison.Ordinal));
            var expectedAlmondTips = HoverTipFactory.FromRelic<ThingsAlmondWater>().ToArray();
            var actualAlmondTips = almondOpt.HoverTips.ToArray();
            Assert(actualAlmondTips.Length == expectedAlmondTips.Length && actualAlmondTips.Length >= 2,
                $"TAKE_ALMOND_WATER option must carry Almond Water + Regen HoverTips (got {actualAlmondTips.Length}).");
            Assert(actualAlmondTips.Any(t => t.Id == ModelDb.Relic<ThingsAlmondWater>().Id.ToString()
                    || (t is HoverTip ht && ht.Title == ModelDb.Relic<ThingsAlmondWater>().Title.GetFormattedText())),
                "TAKE_ALMOND_WATER HoverTips must include ThingsAlmondWater relic preview.");

            var disperseOpt = backrooms.CurrentOptions.Single(o => o.TextKey.EndsWith(".ENCHANT_DISSOLVE", StringComparison.Ordinal));
            Assert(disperseOpt.HoverTips.Any(),
                "ENCHANT_DISSOLVE option must carry ThingsDisperse enchantment HoverTip.");
        }

        GD.Print("SOUL_ROES_DEATH_PASS HP A0/A20, 1p/4p, Ethereal+Exhaust Beckons, six spawns, Intangible=1, slots, stun, phases, Backrooms previews.");
    }
}
