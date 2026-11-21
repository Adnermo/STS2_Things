using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Hooks;
using STS2_Things.Monsters;
using STS2_Things.Powers;
using STS2_Things.Visuals;

public partial class DepthsProbeNode
{
    // Full Vanilla STS2 UI Redlines (1920x1080 baseline, responsive to viewport W x H):
    // - TopBar (0..80) + Relic Row 1 (82..150) => TopSafeY = 150f (148f with 2px font-outline tolerance).
    // - Exhaust Pile (NExhaustPileButton) => bottom-right anchored at (W - 90, H - 290, 80, 80) = (1830, 790, 80, 80) at 1920x1080.
    // - End Turn Button => real NEndTurnButton image rect + Grow(12).
    // - Resting 5-Card Hand Fan => exact HandPosHelper(5, i) + NCard.defaultSize * GetScale(5) + 9px glow margin.
    private const float TopSafeY = 150f;
    private const float TopSafeMinY = 148f;
    private const float CaveGodBodyTopSafeMinY = 152f;
    private const float ExhaustPileRightInset = 90f;
    private const float ExhaustPileBottomOffset = 290f; // 1080 - 290 = 790f
    private const float ExhaustPileWidth = 80f;
    private const float ExhaustPileHeight = 80f;

    private static Rect2 GetExhaustPileRedline(Vector2I viewportSize)
        => new(viewportSize.X - ExhaustPileRightInset, viewportSize.Y - ExhaustPileBottomOffset, ExhaustPileWidth, ExhaustPileHeight);

    private static Rect2[] GetRestingHand5Rects(Vector2I viewportSize)
    {
        Vector2 cardScale = HandPosHelper.GetScale(5);
        Vector2 cardSize = new(
            NCard.defaultSize.X * cardScale.X,
            NCard.defaultSize.Y * cardScale.Y + 18f); // +9px top/bottom resting card glow margin
        Vector2 handAnchor = new(viewportSize.X * 0.5f, viewportSize.Y - 25f);
        var rects = new Rect2[5];
        for (int i = 0; i < 5; i++)
        {
            Vector2 center = handAnchor + HandPosHelper.GetPosition(5, i);
            rects[i] = new Rect2(center - cardSize * 0.5f, cardSize);
        }
        return rects;
    }

    private static Rect2 GetHpBarRect(NCreature creature)
        => creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();

    private static Control[] GetVisiblePowerControls(NCreature creature)
        => creature.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>().Where(n => n.Visible).ToArray();

    private static NIntent[] GetVisibleIntentControls(NCreature creature)
        => creature.IntentContainer.Visible && creature.IntentContainer.Modulate.A > 0.01f
            ? creature.IntentContainer.GetChildren().OfType<NIntent>().Where(n => n.Visible && n.Size.X > 0f && n.Size.Y > 0f).ToArray()
            : [];

    private static bool IntersectsAny(Rect2 rect, Rect2[] zones, out int hitIndex)
    {
        for (int i = 0; i < zones.Length; i++)
        {
            if (rect.Intersects(zones[i]))
            {
                hitIndex = i;
                return true;
            }
        }
        hitIndex = -1;
        return false;
    }

    private static float[] ToRectArray(Rect2 r)
        => [r.Position.X, r.Position.Y, r.Size.X, r.Size.Y];

    private static bool IsPrimaryBossCreature(Creature entity) =>
        entity.Monster is ThingsBowlbugProgenitor
            or ThingsGravetideSlug
            or ThingsOriginFogmog
            or ThingsScaleBeetle
            or ThingsTheLegacy
            or ThingsCaveGodBody
            or ThingsHumanFaceTowerFace { IsActiveFace: true }
            or ThingsAbyssalInkmaw;

    private static async Task ApplyRealisticEncounterPowersAsync(EncounterModel canonical, Battle b)
    {
        if (canonical is not (ModBossEncounter or ThingsSoulRoesEncounter))
            return;

        foreach (var entity in b.Enemies)
            await entity.Monster!.AfterAddedToRoom();

        if (canonical is ThingsBowlbugProgenitorBossEncounter)
        {
            foreach (var minion in b.Enemies.Where(e => e.Monster is not ThingsBowlbugProgenitor))
            {
                if (!minion.HasPower<MinionPower>())
                    await PowerCmd.Apply<MinionPower>(Choice, minion, 1m, minion, null);
            }
        }

        if (canonical is ThingsOriginFogmogBossEncounter)
        {
            foreach (var eye in b.Enemies.Where(e => e.Monster is ThingsOriginEyeWithTeeth))
                await PowerCmd.Apply<StrengthPower>(Choice, eye, 2m, eye, null);
        }

        if (canonical is ThingsCaveGodBossEncounter)
        {
            foreach (var hand in b.Enemies.Where(e => e.Monster is ThingsCaveGodHand))
            {
                await PowerCmd.Apply<StrengthPower>(Choice, hand, 2m, hand, null);
                await PowerCmd.Apply<VulnerablePower>(Choice, hand, 2m, b.Players[0].Creature, null);
            }
            await PowerCmd.Apply<StrengthPower>(Choice, b.Players[0].Creature, 2m, b.Players[0].Creature, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, b.Players[0].Creature, 1m, b.Enemies[0], null);
        }

        if (canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
        {
            await PowerCmd.Apply<StrengthPower>(Choice, b.Players[0].Creature, 2m, b.Players[0].Creature, null);
            await PowerCmd.Apply<VulnerablePower>(Choice, b.Players[0].Creature, 1m, b.Enemies[0], null);
        }

        if (canonical is ThingsAbyssalInkmawBossEncounter)
        {
            foreach (var tentacle in b.Enemies.Where(e => e.Monster is ThingsAbyssalCrushingTentacle or ThingsAbyssalInkTentacle))
            {
                await PowerCmd.Apply<StrengthPower>(Choice, tentacle, 2m, tentacle, null);
                await PowerCmd.Apply<VulnerablePower>(Choice, tentacle, 1m, b.Players[0].Creature, null);
            }
        }

        if (canonical is ModBossEncounter)
        {
            Creature player = b.Players[0].Creature;
            foreach (var boss in b.Enemies.Where(IsPrimaryBossCreature))
            {
                // Apply realistic combat buffs/debuffs on top of native powers so every boss renders 4..6 visible power icons.
                if (!boss.HasPower<StrengthPower>() && boss.Powers.Count(p => p.IsVisible) < 6)
                    await PowerCmd.Apply<StrengthPower>(Choice, boss, 3m, boss, null);
                if (!boss.HasPower<PlatingPower>() && boss.Powers.Count(p => p.IsVisible) < 6)
                    await PowerCmd.Apply<PlatingPower>(Choice, boss, 5m, boss, null);
                if (!boss.HasPower<VulnerablePower>() && boss.Powers.Count(p => p.IsVisible) < 6)
                    await PowerCmd.Apply<VulnerablePower>(Choice, boss, 2m, player, null);
                if (!boss.HasPower<WeakPower>() && boss.Powers.Count(p => p.IsVisible) < 6)
                    await PowerCmd.Apply<WeakPower>(Choice, boss, 2m, player, null);
                if (!boss.HasPower<FrailPower>() && boss.Powers.Count(p => p.IsVisible) < 5)
                    await PowerCmd.Apply<FrailPower>(Choice, boss, 2m, player, null);
            }
        }
    }

    private static void EvaluateCreatureUiRedlines(
        string scenarioTag,
        EncounterModel canonical,
        IEnumerable<NCreature> enemyCreatures,
        Vector2I viewportSize,
        Rect2 endTurnBlocker,
        List<object> rows,
        List<string> failures,
        bool strictInkmawTopSafe150 = false)
    {
        Rect2 endTurnZone = endTurnBlocker.Grow(12);
        Rect2 exhaustZone = GetExhaustPileRedline(viewportSize);
        Rect2[] hand5Zones = GetRestingHand5Rects(viewportSize);
        int failuresBefore = failures.Count;

        foreach (var creature in enemyCreatures.Where(c => c.Visible))
        {
            string slot = creature.Entity.SlotName ?? "";
            string monsterEntry = creature.Entity.Monster!.Id.Entry;
            float minIntentTopY = float.PositiveInfinity;

            // 1. Visible Intent icons vs Top Safe Ceiling (TopBar 0..80 + Relic Row 1 82..150).
            foreach (NIntent intent in GetVisibleIntentControls(creature))
            {
                Rect2 intentRect = intent.GetGlobalRect();
                if (intentRect.Position.Y < minIntentTopY)
                    minIntentTopY = intentRect.Position.Y;
                bool isInkmawTopStrict = strictInkmawTopSafe150
                    && creature.Entity.Monster is ThingsAbyssalInkmaw or ThingsAbyssalCrushingTentacle or ThingsAbyssalInkTentacle;
                float minAllowedTopY = isInkmawTopStrict ? TopSafeY : TopSafeMinY;
                bool topSafeFail = intentRect.Position.Y < minAllowedTopY;
                rows.Add(new
                {
                    scenario = scenarioTag,
                    encounter = canonical.Id.Entry,
                    slot,
                    monster = monsterEntry,
                    kind = "intent",
                    viewport = new[] { viewportSize.X, viewportSize.Y },
                    rect = ToRectArray(intentRect),
                    button = ToRectArray(endTurnBlocker),
                    exhaust = ToRectArray(exhaustZone),
                    overlap = false,
                    exhaustOverlap = false,
                    handOverlap = false,
                    handRedlineFail = false,
                    topSafeMinY = minAllowedTopY,
                    topSafeFail,
                });
                if (topSafeFail)
                    failures.Add($"[TopSafe] {scenarioTag}/{slot}: intent top Y={intentRect.Position.Y:F1} < {minAllowedTopY:F1} ({intentRect})");
            }

            // 2. HP bar, HP text, Block, and visible Power icons vs TopSafe / EndTurn / Exhaust / Resting 5-Card Hand Fan.
            Control[] powerControls = GetVisiblePowerControls(creature);
            float firstPowerRowTopY = powerControls.Length > 0
                ? powerControls.Min(p => p.GetGlobalRect().Position.Y)
                : 0f;

            Rect2 hpRect = creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
            var samples = new List<(string Kind, Control Node, bool IsRow1Power)>
            {
                ("health", creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer"), false),
                ("hp_text", creature.GetNode<Control>("HealthBar/HealthBar/HpBarContainer/HpLabel"), false),
            };
            if (creature.Entity.Block > 0)
                samples.Add(("block", creature.GetNode<Control>("HealthBar/HealthBar/BlockContainer"), false));
            foreach (Control powerNode in powerControls)
            {
                bool isRow1 = powerNode.GetGlobalRect().Position.Y <= firstPowerRowTopY + 12f;
                samples.Add(("power", powerNode, isRow1));
            }

            int creatureFailuresBefore = failures.Count;
            float maxPowerBottomY = powerControls.Length > 0
                ? powerControls.Max(p => p.GetGlobalRect().End.Y)
                : hpRect.End.Y;

            foreach (var (kind, node, isRow1Power) in samples)
            {
                Rect2 rect = node.GetGlobalRect();
                bool overlap = rect.Intersects(endTurnZone);
                bool exhaustOverlap = rect.Intersects(exhaustZone);
                bool handOverlap = IntersectsAny(rect, hand5Zones, out int handCardIndex);
                bool handRedlineFail = handOverlap && (kind != "power" || isRow1Power);
                bool topSafeFail = rect.Position.Y < TopSafeMinY;

                rows.Add(new
                {
                    scenario = scenarioTag,
                    encounter = canonical.Id.Entry,
                    slot,
                    monster = monsterEntry,
                    kind,
                    isRow1Power,
                    viewport = new[] { viewportSize.X, viewportSize.Y },
                    rect = ToRectArray(rect),
                    button = ToRectArray(endTurnBlocker),
                    exhaust = ToRectArray(exhaustZone),
                    overlap,
                    exhaustOverlap,
                    handOverlap,
                    handCardIndex,
                    handRedlineFail,
                    topSafeMinY = TopSafeMinY,
                    topSafeFail,
                });

                if (overlap)
                    failures.Add($"[EndTurn] {scenarioTag}/{slot}: {kind} {rect} overlaps EndTurn {endTurnBlocker}");
                if (exhaustOverlap)
                    failures.Add($"[Exhaust] {scenarioTag}/{slot}: {kind} {rect} overlaps Exhaust {exhaustZone}");
                if (handRedlineFail)
                    failures.Add($"[HandFan] {scenarioTag}/{slot}: {kind} {rect} overlaps resting 5-card hand card#{handCardIndex} {hand5Zones[handCardIndex]}");
                if (topSafeFail)
                    failures.Add($"[TopSafe] {scenarioTag}/{slot}: {kind} top Y={rect.Position.Y:F1} < {TopSafeMinY:F1} ({rect})");
            }

            if (canonical is ModBossEncounter or ThingsSoulRoesEncounter)
            {
                string status = failures.Count == creatureFailuresBefore ? "PASS" : "FAIL";
                string intentStr = float.IsPositiveInfinity(minIntentTopY) ? "none" : $"{minIntentTopY:F1}";
                GD.Print($"LAYOUT_CHECK [{status}] {scenarioTag}/{slot} ({viewportSize.X}x{viewportSize.Y}): intentTopY={intentStr}, hp=({hpRect.Position.X:F1},{hpRect.Position.Y:F1},{hpRect.Size.X:F1}x{hpRect.Size.Y:F1}), powers={powerControls.Length} maxBottomY={maxPowerBottomY:F1}");
            }
        }

        string scenarioStatus = failures.Count == failuresBefore ? "PASS" : $"FAIL({failures.Count - failuresBefore})";
        GD.Print($"LAYOUT_SCENARIO [{scenarioStatus}] {scenarioTag} ({viewportSize.X}x{viewportSize.Y}) endTurn=({endTurnBlocker.Position.X:F1},{endTurnBlocker.Position.Y:F1},{endTurnBlocker.Size.X:F1}x{endTurnBlocker.Size.Y:F1}) exhaust=({exhaustZone.Position.X:F1},{exhaustZone.Position.Y:F1},{exhaustZone.Size.X:F1}x{exhaustZone.Size.Y:F1}) hand5ApexY={hand5Zones[2].Position.Y:F1}");
    }

    private static void VerifySwarmBossSeparation(
        EncounterModel canonical,
        List<NCreature> creatures,
        int expectedMinionCount,
        List<object> rows,
        List<string> failures)
    {
        NCreature bossNode = creatures.First(c => c.Visible && IsPrimaryBossCreature(c.Entity));
        NCreature[] minions = creatures.Where(c => c != bossNode && c.Visible).ToArray();
        Assert(minions.Length == expectedMinionCount,
            $"[{canonical.Id.Entry}] Expected {expectedMinionCount} populated minions, got {minions.Length}.");

        Control bossStateDisplay = bossNode.GetNodeOrNull<Control>("%HealthBar") ?? bossNode.GetNode<Control>("HealthBar");
        bool validRelativeZ = bossStateDisplay.ZAsRelative && bossStateDisplay.ZIndex >= 4;
        if (!validRelativeZ)
        {
            failures.Add($"[BossHealthBarZIndex] {canonical.Id.Entry}: Boss %HealthBar ZAsRelative={bossStateDisplay.ZAsRelative}, ZIndex={bossStateDisplay.ZIndex} (expected relative ZIndex >= 4, above Foreground=3)");
        }

        Rect2 bossHp = GetHpBarRect(bossNode);
        Rect2[] bossPowers = GetVisiblePowerControls(bossNode).Select(p => p.GetGlobalRect()).ToArray();
        int overlapCount = 0;

        foreach (NCreature minion in minions)
        {
            string mSlot = minion.Entity.SlotName ?? "";
            Rect2 mHp = GetHpBarRect(minion);
            Rect2 mHitbox = minion.Hitbox.GetGlobalRect();
            Rect2[] mPowers = GetVisiblePowerControls(minion).Select(p => p.GetGlobalRect()).ToArray();

            if (bossHp.Intersects(mHp))
            {
                overlapCount++;
                failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss HP {bossHp} overlaps minion {mSlot} HP {mHp}");
            }
            if (bossHp.Intersects(mHitbox))
            {
                overlapCount++;
                failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss HP {bossHp} overlaps minion {mSlot} Hitbox {mHitbox}");
            }
            foreach (Rect2 mp in mPowers)
            {
                if (bossHp.Intersects(mp))
                {
                    overlapCount++;
                    failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss HP {bossHp} overlaps minion {mSlot} Power {mp}");
                }
            }

            foreach (Rect2 bp in bossPowers)
            {
                if (bp.Intersects(mHp))
                {
                    overlapCount++;
                    failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss Power {bp} overlaps minion {mSlot} HP {mHp}");
                }
                if (bp.Intersects(mHitbox))
                {
                    overlapCount++;
                    failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss Power {bp} overlaps minion {mSlot} Hitbox {mHitbox}");
                }
                foreach (Rect2 mp in mPowers)
                {
                    if (bp.Intersects(mp))
                    {
                        overlapCount++;
                        failures.Add($"[BossMinionOverlap] {canonical.Id.Entry}: Boss Power {bp} overlaps minion {mSlot} Power {mp}");
                    }
                }
            }
        }

        rows.Add(new
        {
            scenario = canonical.Id.Entry.ToLowerInvariant(),
            encounter = canonical.Id.Entry,
            kind = "swarm_boss_contract",
            minionCount = minions.Length,
            bossHp = ToRectArray(bossHp),
            bossPowerCount = bossPowers.Length,
            bossHealthBarZIndex = bossStateDisplay.ZIndex,
            bossHealthBarZAsRelative = bossStateDisplay.ZAsRelative,
            validRelativeZ,
            overlapCount,
        });
        GD.Print($"LAYOUT_CONTRACT [{(validRelativeZ && overlapCount == 0 ? "PASS" : "FAIL")}] {canonical.Id.Entry} swarm_boss: minions={minions.Length}, bossHp=({bossHp.Position.X:F1},{bossHp.Position.Y:F1},{bossHp.Size.X:F1}x{bossHp.Size.Y:F1}), bossPowers={bossPowers.Length}, healthBarZ={bossStateDisplay.ZIndex} (relative={bossStateDisplay.ZAsRelative}), overlaps={overlapCount}");
    }

    private static void VerifyOriginFogmogLayout(
        EncounterModel canonical,
        List<NCreature> creatures,
        List<object> rows,
        List<string> failures)
    {
        NCreature[] visible = creatures.Where(c => c.Visible).ToArray();
        Assert(visible.Length == 3, $"[OriginFogmog] Expected 3 visible entities (fogmog + illusion1 + illusion2), got {visible.Length}.");
        Assert(visible.Any(c => c.Entity.SlotName == "illusion1") && visible.Any(c => c.Entity.SlotName == "illusion2"),
            "[OriginFogmog] Both illusion1 and illusion2 slots must be populated.");

        int overlapCount = 0;
        for (int i = 0; i < visible.Length; i++)
        {
            Rect2 hpA = GetHpBarRect(visible[i]);
            Rect2[] powA = GetVisiblePowerControls(visible[i]).Select(p => p.GetGlobalRect()).ToArray();
            Rect2[] intentsA = GetVisibleIntentControls(visible[i]).Select(n => n.GetGlobalRect()).ToArray();

            for (int j = i + 1; j < visible.Length; j++)
            {
                Rect2 hpB = GetHpBarRect(visible[j]);
                Rect2[] powB = GetVisiblePowerControls(visible[j]).Select(p => p.GetGlobalRect()).ToArray();
                if (hpA.Intersects(hpB))
                {
                    overlapCount++;
                    failures.Add($"[OriginFogmog] {visible[i].Entity.SlotName} HP {hpA} overlaps {visible[j].Entity.SlotName} HP {hpB}");
                }
                foreach (Rect2 pa in powA)
                {
                    if (pa.Intersects(hpB))
                    {
                        overlapCount++;
                        failures.Add($"[OriginFogmog] {visible[i].Entity.SlotName} Power {pa} overlaps {visible[j].Entity.SlotName} HP {hpB}");
                    }
                    foreach (Rect2 pb in powB)
                    {
                        if (pa.Intersects(pb))
                        {
                            overlapCount++;
                            failures.Add($"[OriginFogmog] {visible[i].Entity.SlotName} Power {pa} overlaps {visible[j].Entity.SlotName} Power {pb}");
                        }
                    }
                }
                foreach (Rect2 pb in powB)
                {
                    if (pb.Intersects(hpA))
                    {
                        overlapCount++;
                        failures.Add($"[OriginFogmog] {visible[j].Entity.SlotName} Power {pb} overlaps {visible[i].Entity.SlotName} HP {hpA}");
                    }
                }
            }

            foreach (Rect2 intentRect in intentsA)
            {
                foreach (NCreature other in visible.Where(o => o != visible[i]))
                {
                    Rect2 otherHp = GetHpBarRect(other);
                    if (intentRect.Intersects(otherHp))
                    {
                        overlapCount++;
                        failures.Add($"[OriginFogmog] {visible[i].Entity.SlotName} Intent {intentRect} overlaps {other.Entity.SlotName} HP {otherHp}");
                    }
                }
            }
        }

        rows.Add(new
        {
            scenario = canonical.Id.Entry.ToLowerInvariant(),
            encounter = canonical.Id.Entry,
            kind = "origin_fogmog_3entity_contract",
            entityCount = visible.Length,
            slots = visible.Select(v => v.Entity.SlotName).ToArray(),
            overlapCount,
        });
        GD.Print($"LAYOUT_CONTRACT [{(overlapCount == 0 ? "PASS" : "FAIL")}] {canonical.Id.Entry} origin_fogmog_3entity: entities={visible.Length}, overlaps={overlapCount}");
    }

    private static void VerifyCaveGodLayout(
        EncounterModel canonical,
        Vector2I viewportSize,
        List<NCreature> creatures,
        NCreature hero,
        List<object> rows,
        List<string> failures)
    {
        NCreature bodyNode = creatures.First(c => c.Entity.Monster is ThingsCaveGodBody);
        Control bodyStateDisplay = bodyNode.GetNodeOrNull<Control>("%HealthBar") ?? bodyNode.GetNode<Control>("HealthBar");
        Rect2 bodyHp = GetHpBarRect(bodyNode);
        Rect2[] bodyPowers = GetVisiblePowerControls(bodyNode).Select(p => p.GetGlobalRect()).ToArray();

        if (bodyHp.Position.Y < CaveGodBodyTopSafeMinY)
            failures.Add($"[CaveGod] body HP bar top Y={bodyHp.Position.Y:F1} < {CaveGodBodyTopSafeMinY:F1} (Relic Row 1 overlap): {bodyHp}");
        foreach (Rect2 bp in bodyPowers)
        {
            if (bp.Position.Y < CaveGodBodyTopSafeMinY)
                failures.Add($"[CaveGod] body Power top Y={bp.Position.Y:F1} < {CaveGodBodyTopSafeMinY:F1} (Relic Row 1 overlap): {bp}");
        }

        Rect2[] hand5Zones = GetRestingHand5Rects(viewportSize);
        Rect2 heroHp = GetHpBarRect(hero);
        Rect2[] heroPowers = GetVisiblePowerControls(hero).Select(p => p.GetGlobalRect()).ToArray();
        Assert(heroPowers.Length >= 2, "[CaveGod] Expected visible hero powers for 5-card hand-fan clearance check.");

        if (IntersectsAny(heroHp, hand5Zones, out int hpCardIdx))
            failures.Add($"[CaveGod] Hero HP {heroHp} overlaps resting 5-card hand card#{hpCardIdx} {hand5Zones[hpCardIdx]}");
        foreach (Rect2 hp in heroPowers)
        {
            if (IntersectsAny(hp, hand5Zones, out int powCardIdx))
                failures.Add($"[CaveGod] Hero Power {hp} overlaps resting 5-card hand card#{powCardIdx} {hand5Zones[powCardIdx]}");
        }

        float hand5ApexTopY = hand5Zones[2].Position.Y;
        float heroPowersBottomY = heroPowers.Max(p => p.End.Y);
        float bodyPowersMinY = bodyPowers.Length > 0 ? bodyPowers.Min(p => p.Position.Y) : 0f;
        rows.Add(new
        {
            scenario = canonical.Id.Entry.ToLowerInvariant(),
            encounter = canonical.Id.Entry,
            kind = "cave_god_layout_contract",
            bodyStateDisplayY = bodyStateDisplay.GlobalPosition.Y,
            bodyHp = ToRectArray(bodyHp),
            bodyPowerCount = bodyPowers.Length,
            bodyPowersMinY,
            heroHp = ToRectArray(heroHp),
            heroPowersBottomY,
            hand5ApexTopY,
        });
        bool caveGodPass = bodyHp.Position.Y >= CaveGodBodyTopSafeMinY
            && bodyPowers.All(p => p.Position.Y >= CaveGodBodyTopSafeMinY)
            && !IntersectsAny(heroHp, hand5Zones, out _)
            && heroPowers.All(p => !IntersectsAny(p, hand5Zones, out _));
        GD.Print($"LAYOUT_CONTRACT [{(caveGodPass ? "PASS" : "FAIL")}] {canonical.Id.Entry} cave_god: bodyHpY={bodyHp.Position.Y:F1}, bodyPowersMinY={bodyPowersMinY:F1}, heroPowersBottomY={heroPowersBottomY:F1}, hand5ApexTopY={hand5ApexTopY:F1}");
    }

    private static void VerifySoulRoesLayout(
        EncounterModel canonical,
        List<NCreature> creatures,
        List<object> rows,
        List<string> failures)
    {
        NCreature bossNode = creatures.First(c => c.Entity.Monster is ThingsSoulRoes);
        NCreature[] minions = creatures.Where(c => c.Entity.Monster is ThingsSoulRoe && c.Visible).ToArray();
        Assert(minions.Length == ThingsSoulRoesEncounter.SoulRoeSlotCount,
            $"[SoulRoes] Expected all {ThingsSoulRoesEncounter.SoulRoeSlotCount} Soul Roe minions populated, got {minions.Length}.");

        NIntent[] bossIntents = GetVisibleIntentControls(bossNode);
        Assert(bossIntents.Length >= 1, "[SoulRoes] Expected visible intent on soul_roes.");

        int occlusionCount = 0;
        foreach (NIntent intent in bossIntents)
        {
            Rect2 bi = intent.GetGlobalRect();
            foreach (NCreature minion in minions)
            {
                string mSlot = minion.Entity.SlotName ?? "";
                Rect2 mHitbox = minion.Hitbox.GetGlobalRect();
                Rect2 mHp = GetHpBarRect(minion);
                if (bi.Intersects(mHitbox))
                {
                    occlusionCount++;
                    failures.Add($"[SoulRoesIntent] soul_roes Intent {bi} occluded by {mSlot} Hitbox {mHitbox}");
                }
                if (bi.Intersects(mHp))
                {
                    occlusionCount++;
                    failures.Add($"[SoulRoesIntent] soul_roes Intent {bi} occluded by {mSlot} HP {mHp}");
                }
                foreach (NIntent mi in GetVisibleIntentControls(minion))
                {
                    Rect2 mir = mi.GetGlobalRect();
                    if (bi.Intersects(mir))
                    {
                        occlusionCount++;
                        failures.Add($"[SoulRoesIntent] soul_roes Intent {bi} overlaps {mSlot} Intent {mir}");
                    }
                }
                foreach (Control mp in GetVisiblePowerControls(minion))
                {
                    Rect2 mpr = mp.GetGlobalRect();
                    if (bi.Intersects(mpr))
                    {
                        occlusionCount++;
                        failures.Add($"[SoulRoesIntent] soul_roes Intent {bi} overlaps {mSlot} Power {mpr}");
                    }
                }
            }
        }

        rows.Add(new
        {
            scenario = canonical.Id.Entry.ToLowerInvariant(),
            encounter = canonical.Id.Entry,
            kind = "soul_roes_intent_contract",
            minionCount = minions.Length,
            bossIntentRects = bossIntents.Select(i => ToRectArray(i.GetGlobalRect())).ToArray(),
            occlusionCount,
        });
        GD.Print($"LAYOUT_CONTRACT [{(occlusionCount == 0 ? "PASS" : "FAIL")}] {canonical.Id.Entry} soul_roes_intent: minions={minions.Length}, intents={bossIntents.Length}, occlusions={occlusionCount}");
    }

    private static float ResolveScaleUpVisualFactor(ThingsScaleUpPower power)
    {
        Type t = power.GetType();
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? prop = t.GetProperty("VisualScaleFactor", instanceFlags)
            ?? t.GetProperty("ScaleFactor", instanceFlags);
        float factor = prop != null ? Convert.ToSingle(prop.GetValue(power)) : 1f;
        FieldInfo? capField = t.GetField("MaxVisualScale", staticFlags)
            ?? t.GetField("VisualScaleCap", staticFlags)
            ?? t.GetField("MaxScaleFactor", staticFlags);
        if (capField != null && Convert.ToSingle(capField.GetValue(null)) is > 1f and var cap)
            factor = MathF.Min(factor, cap);
        return factor;
    }

    private async Task VerifyScaleBeetleScaled60Async(
        EncounterModel canonical,
        SubViewport view,
        List<NCreature> creatures,
        Rect2 endTurnBlocker,
        string output,
        List<object> rows,
        List<string> failures)
    {
        NCreature bossNode = creatures.First(c => c.Entity.Monster is ThingsScaleBeetle);
        Rect2 normalBounds = bossNode.Visuals.Bounds.GetGlobalRect();
        NIntent[] normalIntents = GetVisibleIntentControls(bossNode);
        float normalIntentTopY = normalIntents.Length > 0 ? normalIntents.Min(n => n.GetGlobalRect().Position.Y) : 0f;

        await PowerCmd.Apply<ThingsScaleUpPower>(Choice, bossNode.Entity, 60m, bossNode.Entity, null);
        ThingsScaleUpPower scalePower = bossNode.Entity.GetPower<ThingsScaleUpPower>()!;
        float scaleFactor = ResolveScaleUpVisualFactor(scalePower);
        float expectedVisualScale = scaleFactor * bossNode.Visuals.DefaultScale;
        if (Mathf.Abs(bossNode.Visuals.Scale.X - expectedVisualScale) > 0.01f)
            bossNode.ScaleTo(scaleFactor, 0.02);

        await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        string scenarioTag = canonical.Id.Entry.ToLowerInvariant() + "_scaled60";
        using (var scaledImage = view.GetTexture().GetImage())
        {
            Assert(scaledImage.SavePng(Path.Combine(output, scenarioTag + ".png")) == Error.Ok,
                "[ScaleBeetle scaled60] Layout capture saved.");
        }

        Rect2 scaledBounds = bossNode.Visuals.Bounds.GetGlobalRect();
        NIntent[] scaledIntents = GetVisibleIntentControls(bossNode);
        float carapaceTopEstimateY = scaledBounds.End.Y - scaledBounds.Size.Y * 0.74f;

        if (scaleFactor <= 1.10f || scaleFactor > 1.35f)
            failures.Add($"[ScaleBeetle] 60-stack visual scale factor={scaleFactor:F3} outside expected capped range (1.10..1.35]");
        if (Mathf.Abs(bossNode.Visuals.Scale.X - expectedVisualScale) > 0.03f)
            failures.Add($"[ScaleBeetle] Visuals.Scale.X={bossNode.Visuals.Scale.X:F3} did not settle to expected {expectedVisualScale:F3}");
        if (scaledBounds.End.X > view.Size.X)
            failures.Add($"[ScaleBeetle] Scaled visual right edge X={scaledBounds.End.X:F1} > viewport width {view.Size.X}");

        EvaluateCreatureUiRedlines(scenarioTag, canonical, creatures, view.Size, endTurnBlocker, rows, failures);

        foreach (NIntent intent in scaledIntents)
        {
            Rect2 ir = intent.GetGlobalRect();
            if (ir.Position.Y < TopSafeY)
                failures.Add($"[ScaleBeetle] Scaled Intent top Y={ir.Position.Y:F1} < {TopSafeY:F1} (Relic Row 1 overlap): {ir}");
            if (ir.End.Y > carapaceTopEstimateY)
                failures.Add($"[ScaleBeetle] Scaled Intent bottom Y={ir.End.Y:F1} buried below carapace apex Y={carapaceTopEstimateY:F1} ({ir})");
        }

        rows.Add(new
        {
            scenario = scenarioTag,
            encounter = canonical.Id.Entry,
            kind = "scale_beetle_scaled60_contract",
            stacks = scalePower.Amount,
            scaleFactor,
            actualVisualScaleX = bossNode.Visuals.Scale.X,
            normalBounds = ToRectArray(normalBounds),
            scaledBounds = ToRectArray(scaledBounds),
            visualRightX = scaledBounds.End.X,
            normalIntentTopY,
            scaledIntentRects = scaledIntents.Select(i => ToRectArray(i.GetGlobalRect())).ToArray(),
            carapaceTopEstimateY,
        });
        float scaledIntentTopY = scaledIntents.Length > 0 ? scaledIntents.Min(i => i.GetGlobalRect().Position.Y) : 0f;
        float scaledIntentBottomY = scaledIntents.Length > 0 ? scaledIntents.Max(i => i.GetGlobalRect().End.Y) : 0f;
        bool scaleBeetlePass = scaleFactor > 1.10f && scaleFactor <= 1.35f
            && scaledBounds.End.X <= view.Size.X
            && scaledIntents.All(i => i.GetGlobalRect().Position.Y >= TopSafeY && i.GetGlobalRect().End.Y <= carapaceTopEstimateY);
        GD.Print($"LAYOUT_CONTRACT [{(scaleBeetlePass ? "PASS" : "FAIL")}] {scenarioTag}: stacks={scalePower.Amount}, scaleFactor={scaleFactor:F3}, visualRightX={scaledBounds.End.X:F1}/{view.Size.X}, intentY=({scaledIntentTopY:F1}..{scaledIntentBottomY:F1}), carapaceApexY={carapaceTopEstimateY:F1}");
    }

    // Native creature HUDs and the native end-turn artwork, independently of a
    // live save. The button's shown position comes from the target game DLL.
    private async Task VerifyEncounterHudLayout()
    {
        var extension = GDExtensionManager.LoadExtension(Path.Combine(_root, "addons/spine/spine_godot_extension.gdextension"));
        Assert(extension is GDExtensionManager.LoadStatus.Ok or GDExtensionManager.LoadStatus.AlreadyLoaded, "Spine extension loaded.");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ActModel).Assembly);
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        var host = new Harmony("DepthsProbe.EncounterLayout");
        host.Patch(AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(DepthsProbeNode), nameof(SkipTestUiGuard)));
        Type? atlasType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Assets.AtlasResourceLoader");
        ResourceFormatLoader? atlas = atlasType == null ? null : (ResourceFormatLoader)Activator.CreateInstance(atlasType)!;
        if (atlas != null) ResourceLoader.AddResourceFormatLoader(atlas, true);
        var view = new SubViewport { Size = new Vector2I(1920, 1080), Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(view);
        GetTree().Root.Size = view.Size;
        string output = Path.Combine(_output, "layout");
        Directory.CreateDirectory(output);
        bool sourceScenes = OS.GetCmdlineUserArgs().Contains("--source-scenes")
            || System.Environment.GetEnvironmentVariable("THINGS_LAYOUT_SOURCE_SCENES") == "1";
        string? only = GetCmdOption("--encounter=")
            ?? System.Environment.GetEnvironmentVariable("THINGS_LAYOUT_ENCOUNTER");
        var rows = new List<object>();
        var failures = new List<string>();
        var encounters = ModelDb.AllEncounters
            .Where(e => e.GetType().Assembly == typeof(ThingsDepths).Assembly && e.HasScene)
            .Where(e => string.IsNullOrWhiteSpace(only)
                || string.Equals(e.Id.Entry, only, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.GetType().Name, only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id.Entry).ToArray();
        Assert(encounters.Length > 0, "At least one authored encounter selected.");
        foreach (var canonical in encounters)
        {
            var b = await StrongBattle(canonical);
            // Exercise authored summon locations as well as opening formations.
            MonsterModel? summon = canonical switch
            {
                ThingsBowlbugProgenitorBossEncounter => ModelDb.Monster<BowlbugRock>(),
                ThingsGravetideSlugBossEncounter => ModelDb.Monster<ThingsGravetideCorpseSlug>(),
                ThingsOriginFogmogBossEncounter => ModelDb.Monster<ThingsOriginEyeWithTeeth>(),
                ThingsSoulRoesEncounter => ModelDb.Monster<ThingsSoulRoe>(),
                _ => null,
            };
            if (summon != null)
            {
                foreach (string slot in canonical.Slots.Where(s => b.Enemies.All(e => e.SlotName != s)))
                {
                    var monster = summon.ToMutable();
                    if (monster is ThingsOriginEyeWithTeeth eye && slot == "illusion2")
                        eye.StartsWithBite = true;
                    var entity = b.State.CreateCreature(monster, CombatSide.Enemy, slot);
                    b.State.AddCreature(entity);
                    monster.SetUpForCombat();
                    monster.RollMove(b.Players.Select(p => p.Creature));
                }
            }
            var world = new Control { Name = "CombatSceneContainer", Size = new Vector2(1920, 1080), PivotOffset = new Vector2(960, 540),
                Scale = Vector2.One * b.Encounter.GetCameraScaling(), Position = b.Encounter.GetCameraOffset() };
            // Always mount every encounter's real authored background (including all 8 mod bosses) inside BgContainer.
            var background = b.Encounter.CreateBackground(b.Run.Act, b.Run.Rng.UpFront);
            view.AddChild(world);
            bool usesViewportFitter = background.GetNodeOrNull<NThingsBackgroundViewportFitter>("ThingsBackgroundViewportFitter") != null;
            float initialBgScale = canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter || usesViewportFitter
                ? 0.9f
                : 1f;
            var bgContainer = new Control
            {
                Name = "BgContainer",
                Position = world.Size * 0.5f + new Vector2(23, 0),
                Scale = Vector2.One * initialBgScale,
            };
            world.AddChild(bgContainer);
            bgContainer.AddChild(background);
            var slots = sourceScenes
                ? GD.Load<PackedScene>(Path.Combine(_root, "scenes/encounters", canonical.Id.Entry.ToLowerInvariant() + ".tscn")).Instantiate<Control>()
                : b.Encounter.CreateScene();
            world.AddChild(slots);
            await ApplyRealisticEncounterPowersAsync(canonical, b);
            var creatures = new List<NCreature>();
            foreach (var entity in b.Enemies)
            {
                var creature = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(creature, entity);
                typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(creature, entity.Monster!.CreateVisuals());
                creature.Position = slots.GetNode<Node2D>(entity.SlotName!).Position;
                world.AddChild(creature);
                await creature.UpdateIntent(b.Players.Select(p => p.Creature));
                creature.IntentContainer.Modulate = Colors.White;
                if (entity.Monster is ThingsHumanFaceTowerFace { IsActiveFace: false })
                    creature.Visible = false;
                creatures.Add(creature);
            }
            var team = new Control { Name = "AllyContainer", Position = world.Size * 0.5f };
            if (canonical is ThingsCaveGodBossEncounter)
                team.Position += Vector2.Down * NCaveGodBossBackground.PlayerStageOffsetY;
            world.AddChild(team);
            var hero = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
            typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(hero, b.Players[0].Creature);
            typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(hero, b.Players[0].Character.CreateVisuals());
            team.AddChild(hero);
            NCombatRoom.PositionPlayersAndPets([hero], b.Encounter.GetCameraScaling(), b.Encounter.FullyCenterPlayers);

            // Keep the button's native dimensions, textures, offsets and pivots;
            // detach only its combat-input behavior from this render fixture.
            var originalButton = GD.Load<PackedScene>("res://scenes/combat/end_turn_button.tscn").Instantiate<Control>();
            var buttonVisuals = originalButton.GetNode<Control>("Visuals");
            originalButton.RemoveChild(buttonVisuals);
            Vector2 shownRatio = (Vector2)AccessTools.Field(typeof(NEndTurnButton), "_showPosRatio").GetValue(null)!;
            var button = new Control
            {
                Size = originalButton.Size,
                Position = shownRatio * new Vector2(1920, 1080),
                ZAsRelative = false,
                ZIndex = 20,
            };
            originalButton.Free();
            view.AddChild(button);
            button.AddChild(buttonVisuals);
            buttonVisuals.GetNode<Label>("Label").Text = "结束回合";
            await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
            if (canonical is ThingsCaveGodBossEncounter)
            {
                var caveGodController = background.GetNodeOrNull<NCaveGodBossBackground>("CaveGod")
                    ?? background.GetNodeOrNull<NCaveGodBossBackground>("%CaveGod");
                Assert(caveGodController != null, "[CaveGod] Missing NCaveGodBossBackground controller.");
                caveGodController!.UpdateHandAnchors(creatures);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Rect2 blocker = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();

            string baseTag = canonical.Id.Entry.ToLowerInvariant();
            EvaluateCreatureUiRedlines(
                baseTag,
                canonical,
                creatures,
                view.Size,
                blocker,
                rows,
                failures,
                strictInkmawTopSafe150: canonical is ThingsAbyssalInkmawBossEncounter);

            if (canonical is ModBossEncounter)
            {
                NCreature primaryBoss = creatures.First(c => c.Visible && IsPrimaryBossCreature(c.Entity));
                int bossPowerCount = GetVisiblePowerControls(primaryBoss).Length;
                if (bossPowerCount < 4 || bossPowerCount > 6)
                    failures.Add($"[BossPowers] {canonical.Id.Entry}: primary boss has {bossPowerCount} visible powers (expected 4..6)");
            }

            if (canonical is ThingsBowlbugProgenitorBossEncounter)
                VerifySwarmBossSeparation(canonical, creatures, expectedMinionCount: 16, rows, failures);
            else if (canonical is ThingsGravetideSlugBossEncounter)
                VerifySwarmBossSeparation(canonical, creatures, expectedMinionCount: ThingsGravetideSlugBossEncounter.CorpseSlugSlotCount, rows, failures);
            else if (canonical is ThingsOriginFogmogBossEncounter)
                VerifyOriginFogmogLayout(canonical, creatures, rows, failures);
            else if (canonical is ThingsCaveGodBossEncounter)
                VerifyCaveGodLayout(canonical, view.Size, creatures, hero, rows, failures);
            else if (canonical is ThingsSoulRoesEncounter)
                VerifySoulRoesLayout(canonical, creatures, rows, failures);

            using (var image = view.GetTexture().GetImage())
            {
                Assert(image.SavePng(Path.Combine(output, baseTag + ".png")) == Error.Ok, "Layout capture saved.");
                if (canonical is ThingsHumanFaceTowerBossEncounter)
                    AssertTowerViewportAlignment(image, view.Size, hero, creatures.First(c => c.Visible), "1920x1080");
            }

            if (canonical is ThingsScaleBeetleBossEncounter)
            {
                await VerifyScaleBeetleScaled60Async(canonical, view, creatures, blocker, output, rows, failures);
            }

            if (canonical is ThingsHumanFaceTowerBossEncounter)
            {
                foreach (var (w, h, bgScale, tag) in new[] { (1920, 1200, 0.9f, "1920x1200"), (1728, 1080, 1.08f, "1728x1080") })
                {
                    view.Size = new Vector2I(w, h);
                    GetTree().Root.Size = view.Size;
                    world.Size = new Vector2(w, h);
                    bgContainer.Position = world.Size * 0.5f + new Vector2(23, 0);
                    bgContainer.Scale = Vector2.One * bgScale;
                    button.Position = shownRatio * new Vector2(w, h);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Rect2 altBlocker = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();
                    EvaluateCreatureUiRedlines($"{baseTag}_{tag}", canonical, creatures, view.Size, altBlocker, rows, failures);
                    using var altImage = view.GetTexture().GetImage();
                    Assert(altImage.SavePng(Path.Combine(output, baseTag + "_" + tag + ".png")) == Error.Ok, $"Layout capture {tag} saved.");
                    AssertTowerViewportAlignment(altImage, view.Size, hero, creatures.First(c => c.Visible), tag);
                }
                view.Size = new Vector2I(1920, 1080);
                GetTree().Root.Size = view.Size;
                world.Size = new Vector2(1920, 1080);
                bgContainer.Position = world.Size * 0.5f + new Vector2(23, 0);
                bgContainer.Scale = Vector2.One * 0.9f;
                button.Position = shownRatio * new Vector2(1920, 1080);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                blocker = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();
            }
            if (canonical is ThingsHumanFaceTowerBossEncounter or ThingsAbyssalInkmawBossEncounter)
            {
                await VerifyMonumentalMultiplayerLayoutsAsync(
                    canonical, view, world, bgContainer, team, creatures, button, buttonVisuals, shownRatio, blocker, output, sourceScenes, rows, failures);
            }
            foreach (var child in view.GetChildren()) { view.RemoveChild(child); child.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            DeactivateSyntheticCombat();
        }
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new
        {
            sourceScenes,
            redlines = new
            {
                topSafeY = TopSafeY,
                topSafeMinY = TopSafeMinY,
                caveGodBodyTopSafeMinY = CaveGodBodyTopSafeMinY,
                exhaustPile1080p = ToRectArray(GetExhaustPileRedline(new Vector2I(1920, 1080))),
                restingHand5_1080p = GetRestingHand5Rects(new Vector2I(1920, 1080)).Select(ToRectArray).ToArray(),
            },
            encounters = encounters.Length,
            rows,
            failures,
        }, new JsonSerializerOptions { WriteIndented = true }));
        view.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        host.UnpatchAll(host.Id);
        if (atlas != null) ResourceLoader.RemoveResourceFormatLoader(atlas);
        foreach (string failure in failures) GD.Print("HUD_OVERLAP " + failure);
        Assert(failures.Count == 0, $"{failures.Count} encounter layout redline violation(s); see {output}.");
    }

    private void AssertTowerViewportAlignment(Image image, Vector2I size, NCreature hero, NCreature activeFace, string tag)
    {
        float expectedCenterX = size.X * 0.5f;
        Assert(Mathf.Abs(hero.GlobalPosition.X - expectedCenterX) <= 2.5f,
            $"[HumanFaceTower {tag}] Hero X={hero.GlobalPosition.X:F1} not centered at {expectedCenterX:F1}.");
        Assert(Mathf.Abs(activeFace.GlobalPosition.X - expectedCenterX) <= 2.5f,
            $"[HumanFaceTower {tag}] Face HUD X={activeFace.GlobalPosition.X:F1} not centered at {expectedCenterX:F1}.");

        // Verify smaller player monumental scale.
        Assert(Mathf.Abs(hero.Visuals.DefaultScale - NHumanFaceTowerBossBackground.AllyVisualScale) <= 0.02f,
            $"[HumanFaceTower {tag}] Hero DefaultScale={hero.Visuals.DefaultScale:F3} expected {NHumanFaceTowerBossBackground.AllyVisualScale:F3}.");
        Rect2 heroHitbox = hero.Hitbox.GetGlobalRect();
        Assert(heroHitbox.Size.Y > 60f && heroHitbox.Size.Y < 145f,
            $"[HumanFaceTower {tag}] Hero global hitbox height={heroHitbox.Size.Y:F1}px not scaled down as expected.");

        // Verify zero collision between Boss HUD and Hero body/HP.
        Rect2 bossHp = activeFace.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
        Rect2 heroHp = hero.GetNode<Control>("HealthBar/HealthBar/HpBarContainer").GetGlobalRect();
        Assert(!bossHp.Intersects(heroHp.Grow(32)),
            $"[HumanFaceTower {tag}] Boss HP {bossHp} too close to Hero HP {heroHp}.");
        Assert(heroHitbox.Position.Y > bossHp.End.Y + 12f,
            $"[HumanFaceTower {tag}] Hero top Y={heroHitbox.Position.Y:F1} overlaps Boss HP bottom Y={bossHp.End.Y:F1}.");

        // Verify zero occlusion by resting hand card fan (5-card center apex & 3-card max apex from HandPosHelper + NCard.defaultSize).
        float handContainerGlobalY = size.Y - 25f;
        float restingCardHalfHeightWithGlow = NCard.defaultSize.Y * HandPosHelper.GetScale(5).Y * 0.5f + 9f;
        float hand5TopY = handContainerGlobalY + HandPosHelper.GetPosition(5, 2).Y - restingCardHalfHeightWithGlow;
        float hand3TopY = handContainerGlobalY + HandPosHelper.GetPosition(3, 1).Y - restingCardHalfHeightWithGlow;
        var heroPowerControls = hero.GetNode("HealthBar/PowerContainer").GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
        Assert(heroPowerControls.Length >= 2, $"[HumanFaceTower {tag}] Expected visible hero powers for HUD clearance check.");
        float heroPowersBottomY = heroPowerControls.Max(c => c.GetGlobalRect().End.Y);

        Assert(heroHp.End.Y <= hand5TopY - 40f,
            $"[HumanFaceTower {tag}] Hero HP bottom Y={heroHp.End.Y:F1} too close to 5-card hand top Y={hand5TopY:F1}.");
        Assert(heroPowersBottomY <= hand5TopY - 16f && heroPowersBottomY <= hand3TopY - 6f,
            $"[HumanFaceTower {tag}] Hero Power row bottom Y={heroPowersBottomY:F1} occluded by hand fan (hand5Top={hand5TopY:F1}, hand3Top={hand3TopY:F1}).");

        // Verify zero unrendered clear-color borders along all 4 viewport edges (including bottom corners & center).
        Color clear = ProjectSettings.GetSetting("rendering/environment/defaults/default_clear_color", new Color(0.092f, 0.122f, 0.117f, 1f)).AsColor();
        foreach (var (sx, sy, edgeName) in new[]
        {
            (6, size.Y / 2, "left"),
            (size.X - 7, size.Y / 2, "right"),
            (size.X / 2, 6, "top"),
            (size.X / 2, size.Y - 7, "bottom_center"),
            (size.X / 5, size.Y - 7, "bottom_left"),
            (size.X * 4 / 5, size.Y - 7, "bottom_right"),
        })
        {
            Color px = image.GetPixel(sx, sy);
            float dist = Mathf.Abs(px.R - clear.R) + Mathf.Abs(px.G - clear.G) + Mathf.Abs(px.B - clear.B);
            Assert(dist > 0.035f, $"[HumanFaceTower {tag}] Unrendered clear-color gap detected on {edgeName} edge at ({sx},{sy}): px={px}, clear={clear}.");
        }
    }

    private async Task VerifyMonumentalMultiplayerLayoutsAsync(
        EncounterModel canonical,
        SubViewport view,
        Control world,
        Control bgContainer,
        Control team,
        List<NCreature> enemyCreatures,
        Control button,
        Control buttonVisuals,
        Vector2 shownRatio,
        Rect2 endTurnBlocker,
        string output,
        bool sourceScenes,
        List<object> rows,
        List<string> failures)
    {
        _ = sourceScenes;
        bool isTower = canonical is ThingsHumanFaceTowerBossEncounter;
        string tagPrefix = isTower ? "HumanFaceTower" : "AbyssalInkmaw";
        string entryLower = canonical.Id.Entry.ToLowerInvariant();
        CharacterModel[] roster =
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>()
        ];

        foreach (int partySize in new[] { 3, 4 })
        {
            foreach (var child in team.GetChildren()) { team.RemoveChild(child); child.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var mpBattle = await StrongBattle(canonical, players: partySize, seed: $"mp-layout-{canonical.Id.Entry}-{partySize}");
            var heroes = new List<NCreature>();
            for (int i = 0; i < partySize; i++)
            {
                var p = mpBattle.Players[i];
                await PowerCmd.Apply<StrengthPower>(Choice, p.Creature, 2, p.Creature, null);
                await PowerCmd.Apply<VulnerablePower>(Choice, p.Creature, 1, mpBattle.Enemies[0], null);

                var node = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, p.Creature);
                typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, roster[i % roster.Length].CreateVisuals());
                team.AddChild(node);
                heroes.Add(node);
            }

            NCombatRoom.PositionPlayersAndPets(heroes, mpBattle.Encounter.GetCameraScaling(), mpBattle.Encounter.FullyCenterPlayers);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            string tag = $"{partySize}p";
            using (var mpImage = view.GetTexture().GetImage())
            {
                Assert(mpImage.SavePng(Path.Combine(output, $"{entryLower}_{tag}.png")) == Error.Ok,
                    $"[{tagPrefix} {tag}] Multiplayer layout capture saved.");
            }

            float expectedCenterX = view.Size.X * 0.5f;
            float avgX = heroes.Average(h => h.GlobalPosition.X);
            Assert(Mathf.Abs(avgX - expectedCenterX) <= 4f,
                $"[{tagPrefix} {tag}] Party average X={avgX:F1} not symmetrically centered at {expectedCenterX:F1}.");

            var orderedHeroes = heroes.OrderBy(h => h.GlobalPosition.X).ToArray();
            var hpRects = new Rect2[orderedHeroes.Length];

            NCreature bossCreature = isTower
                ? enemyCreatures.First(c => c.Visible)
                : enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkmaw);
            Rect2 bossHp = GetHpBarRect(bossCreature);
            Rect2 exhaustZone = GetExhaustPileRedline(view.Size);

            float handContainerGlobalY = view.Size.Y - 25f;
            float restingCardHalfHeightWithGlow = NCard.defaultSize.Y * HandPosHelper.GetScale(5).Y * 0.5f + 9f;
            float hand5TopY = handContainerGlobalY + HandPosHelper.GetPosition(5, 2).Y - restingCardHalfHeightWithGlow;

            for (int i = 0; i < orderedHeroes.Length; i++)
            {
                NCreature h = orderedHeroes[i];
                Assert(h.Position.Y >= MonumentalBossPartyLayoutPatch.BaseFloorLocalY - 0.5f,
                    $"[{tagPrefix} {tag}] Hero #{i} local Y={h.Position.Y:F1} pushed up off the floor into background!");
                Assert(h.Visuals.Modulate.IsEqualApprox(Colors.White),
                    $"[{tagPrefix} {tag}] Hero #{i} unexpectedly dimmed ({h.Visuals.Modulate}).");
                Assert(Mathf.Abs(h.Visuals.DefaultScale - NAbyssalInkmawBossBackground.AllyVisualScale) <= 0.02f,
                    $"[{tagPrefix} {tag}] Hero #{i} DefaultScale={h.Visuals.DefaultScale:F3} not scaled to monumental 0.67.");

                Rect2 hHitbox = h.Hitbox.GetGlobalRect();
                Rect2 hHp = GetHpBarRect(h);
                hpRects[i] = hHp;

                Assert(hHitbox.Position.Y > bossHp.End.Y + 8f,
                    $"[{tagPrefix} {tag}] Hero #{i} top Y={hHitbox.Position.Y:F1} overlaps Boss HP bottom Y={bossHp.End.Y:F1}.");
                Assert(!hHp.Intersects(endTurnBlocker.Grow(12)),
                    $"[{tagPrefix} {tag}] Hero #{i} HP {hHp} overlaps End Turn button {endTurnBlocker}.");
                Assert(!hHp.Intersects(exhaustZone),
                    $"[{tagPrefix} {tag}] Hero #{i} HP {hHp} overlaps Exhaust pile {exhaustZone}.");

                var heroPowerControls = GetVisiblePowerControls(h);
                if (heroPowerControls.Length > 0)
                {
                    float powersBottomY = heroPowerControls.Max(c => c.GetGlobalRect().End.Y);
                    Assert(powersBottomY <= hand5TopY - 8f,
                        $"[{tagPrefix} {tag}] Hero #{i} Power row bottom Y={powersBottomY:F1} too close to 5-card hand top Y={hand5TopY:F1}.");
                }

                if (i > 0)
                {
                    Assert(hHp.Position.X >= hpRects[i - 1].End.X + 16f,
                        $"[{tagPrefix} {tag}] Hero #{i} HP {hHp} crowds Hero #{i - 1} HP {hpRects[i - 1]}.");
                }
            }

            if (!isTower)
            {
                Rect2 leftTentHp = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalCrushingTentacle));
                Rect2 rightTentHp = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkTentacle));
                Assert(hpRects[0].Position.X >= leftTentHp.End.X + 32f,
                    $"[{tagPrefix} {tag}] Leftmost Hero HP {hpRects[0]} crowds Left Tentacle HP {leftTentHp}.");
                Assert(hpRects[^1].End.X <= rightTentHp.Position.X - 32f,
                    $"[{tagPrefix} {tag}] Rightmost Hero HP {hpRects[^1]} crowds Right Tentacle HP {rightTentHp}.");

                if (partySize == 4)
                {
                    var spikeMonster = (ThingsAbyssalSpikeTentacle)ModelDb.Monster<ThingsAbyssalSpikeTentacle>().ToMutable();
                    spikeMonster.ConfigureSpawn(isOuterLeft: true, playEmergeOnAdd: false);
                    var shellMonster = (ThingsAbyssalShellTentacle)ModelDb.Monster<ThingsAbyssalShellTentacle>().ToMutable();
                    shellMonster.ConfigureSpawn(isOuterLeft: false, playEmergeOnAdd: false);

                    var spikeEntity = await CreatureCmd.Add(spikeMonster, mpBattle.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterLeftTentacleSlot);
                    var shellEntity = await CreatureCmd.Add(shellMonster, mpBattle.State, CombatSide.Enemy, ThingsAbyssalInkmawBossEncounter.OuterRightTentacleSlot);
                    await PowerCmd.Apply<MinionPower>(Choice, spikeEntity, 1m, spikeEntity, null);
                    await PowerCmd.Apply<MinionPower>(Choice, shellEntity, 1m, shellEntity, null);
                    await PowerCmd.Apply<StrengthPower>(Choice, spikeEntity, 2m, spikeEntity, null);
                    await PowerCmd.Apply<StrengthPower>(Choice, shellEntity, 2m, shellEntity, null);
                    await PowerCmd.Apply<VulnerablePower>(Choice, shellEntity, 1m, mpBattle.Players[0].Creature, null);
                    spikeMonster.RollMove(mpBattle.Players.Select(p => p.Creature));
                    shellMonster.RollMove(mpBattle.Players.Select(p => p.Creature));

                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: true, alive: true, variantSkin: spikeMonster.VariantSkinName, playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: false, alive: true, variantSkin: shellMonster.VariantSkinName, playEmerge: false);

                    var smallNodes = new List<NCreature>();
                    foreach (var ent in new[] { spikeEntity, shellEntity })
                    {
                        var sc = GD.Load<PackedScene>("res://scenes/combat/creature.tscn").Instantiate<NCreature>();
                        typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(sc, ent);
                        typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(sc, ent.Monster!.CreateVisuals());
                        world.AddChild(sc);
                        await sc.UpdateIntent(mpBattle.Players.Select(p => p.Creature));
                        sc.IntentContainer.Modulate = Colors.White;
                        smallNodes.Add(sc);
                    }

                    var all5Enemies = enemyCreatures.Concat(smallNodes).ToList();
                    NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                    using (var full5Img = view.GetTexture().GetImage())
                    {
                        Assert(full5Img.SavePng(Path.Combine(output, $"{entryLower}_4p_full5.png")) == Error.Ok,
                            $"[{tagPrefix} 4p_full5] 5-enemy + 4-player layout capture saved.");
                    }

                    EvaluateCreatureUiRedlines(
                        $"{entryLower}_4p_full5",
                        canonical,
                        all5Enemies,
                        view.Size,
                        endTurnBlocker,
                        rows,
                        failures,
                        strictInkmawTopSafe150: true);

                    Rect2 leftSmallHp = GetHpBarRect(smallNodes[0]);
                    Rect2 rightSmallHp = GetHpBarRect(smallNodes[1]);
                    Assert(leftSmallHp.Position.X >= 12f && rightSmallHp.End.X <= view.Size.X - 12f,
                        $"[{tagPrefix} 4p_full5] Small tentacle HP out of viewport: L={leftSmallHp}, R={rightSmallHp}.");
                    Assert(leftSmallHp.End.X <= leftTentHp.Position.X - 8f,
                        $"[{tagPrefix} 4p_full5] Left small tentacle HP {leftSmallHp} crowds Left major tentacle HP {leftTentHp}.");
                    Assert(rightSmallHp.Position.X >= rightTentHp.End.X + 8f,
                        $"[{tagPrefix} 4p_full5] Right small tentacle HP {rightSmallHp} crowds Right major tentacle HP {rightTentHp}.");
                    Assert(!rightSmallHp.Intersects(endTurnBlocker.Grow(12)),
                        $"[{tagPrefix} 4p_full5] Right small tentacle HP {rightSmallHp} overlaps End Turn button {endTurnBlocker}.");

                    foreach (var sc in smallNodes)
                    {
                        foreach (var pc in GetVisiblePowerControls(sc))
                        {
                            Rect2 pr = pc.GetGlobalRect();
                            Assert(!pr.Intersects(endTurnBlocker.Grow(12)),
                                $"[{tagPrefix} 4p_full5] Small tentacle power {pr} overlaps End Turn button {endTurnBlocker}.");
                        }
                    }

                    await CaptureAbyssalInkVisualAuditsAsync(view, output, mpBattle, enemyCreatures, smallNodes);

                    // 16:10 Auto (1728x1080) with all 5 enemies: verify Exhaust/EndTurn/TopSafe/HandFan clearance.
                    var size1610 = new Vector2I(1728, 1080);
                    view.Size = size1610;
                    GetTree().Root.Size = size1610;
                    world.Size = new Vector2(1728, 1080);
                    bgContainer.Position = world.Size * 0.5f + new Vector2(23, 0);
                    bgContainer.Scale = Vector2.One * 1.08f;
                    button.Position = shownRatio * new Vector2(1728, 1080);
                    NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                    using (var img1610 = view.GetTexture().GetImage())
                    {
                        Assert(img1610.SavePng(Path.Combine(output, $"{entryLower}_1728x1080_full5.png")) == Error.Ok,
                            $"[{tagPrefix} 1728x1080_full5] 16:10 5-enemy layout capture saved.");
                    }

                    Rect2 blocker1610 = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();
                    EvaluateCreatureUiRedlines(
                        $"{entryLower}_1728x1080_full5",
                        canonical,
                        all5Enemies,
                        size1610,
                        blocker1610,
                        rows,
                        failures,
                        strictInkmawTopSafe150: true);

                    Rect2 leftTentHp1610 = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalCrushingTentacle));
                    Rect2 rightTentHp1610 = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkTentacle));
                    Rect2 leftSmallHp1610 = GetHpBarRect(smallNodes[0]);
                    Rect2 rightSmallHp1610 = GetHpBarRect(smallNodes[1]);
                    if (leftSmallHp1610.Position.X < 8f || rightSmallHp1610.End.X > size1610.X - 8f)
                        failures.Add($"[Inkmaw1610] Small tentacle HP out of 1728x1080 viewport: L={leftSmallHp1610}, R={rightSmallHp1610}");
                    if (leftSmallHp1610.End.X > leftTentHp1610.Position.X - 6f)
                        failures.Add($"[Inkmaw1610] Left small tentacle HP {leftSmallHp1610} crowds Left major tentacle HP {leftTentHp1610}");
                    if (rightSmallHp1610.Position.X < rightTentHp1610.End.X + 6f)
                        failures.Add($"[Inkmaw1610] Right small tentacle HP {rightSmallHp1610} crowds Right major tentacle HP {rightTentHp1610}");

                    // Restore 1920x1080 before Phase 2 floor breach capture.
                    view.Size = new Vector2I(1920, 1080);
                    GetTree().Root.Size = view.Size;
                    world.Size = new Vector2(1920, 1080);
                    bgContainer.Position = world.Size * 0.5f + new Vector2(23, 0);
                    bgContainer.Scale = Vector2.One * 0.9f;
                    button.Position = shownRatio * new Vector2(1920, 1080);

                    // Phase 2 floor breach on both major tentacles + all 5 enemies + 4 heroes.
                    NAbyssalInkmawBossBackground.Current?.PlayPhase2Surge();
                    NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(isLeft: true, alive: true, floorBreach: true, playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(isLeft: false, alive: true, floorBreach: true, playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.AlignStageAndPlayers();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                    Assert(NAbyssalInkmawBossBackground.Current is { LeftIsFloorBreach: true, RightIsFloorBreach: true },
                        $"[{tagPrefix} 4p_phase2_breach] Both major tentacles must enter Phase 2 floor breach.");

                    using (var p2Img = view.GetTexture().GetImage())
                    {
                        Assert(p2Img.SavePng(Path.Combine(output, $"{entryLower}_4p_phase2_breach.png")) == Error.Ok,
                            $"[{tagPrefix} 4p_phase2_breach] Phase 2 floor breach capture saved.");
                    }

                    Rect2 blockerP2 = buttonVisuals.GetNode<Control>("Image").GetGlobalRect();
                    EvaluateCreatureUiRedlines(
                        $"{entryLower}_4p_phase2_breach",
                        canonical,
                        all5Enemies,
                        view.Size,
                        blockerP2,
                        rows,
                        failures,
                        strictInkmawTopSafe150: true);

                    Rect2 leftFloorHp = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalCrushingTentacle));
                    Rect2 rightFloorHp = GetHpBarRect(enemyCreatures.First(c => c.Entity.Monster is ThingsAbyssalInkTentacle));
                    Rect2 leftSmallHpP2 = GetHpBarRect(smallNodes[0]);
                    Rect2 rightSmallHpP2 = GetHpBarRect(smallNodes[1]);
                    Rect2[] heroHpP2 = orderedHeroes.Select(GetHpBarRect).ToArray();
                    if (heroHpP2[0].Position.X < leftFloorHp.End.X + 16f)
                        failures.Add($"[InkmawPhase2] Leftmost Hero HP {heroHpP2[0]} crowds Left floor-breach tentacle HP {leftFloorHp}");
                    if (heroHpP2[^1].End.X > rightFloorHp.Position.X - 16f)
                        failures.Add($"[InkmawPhase2] Rightmost Hero HP {heroHpP2[^1]} crowds Right floor-breach tentacle HP {rightFloorHp}");
                    if (leftSmallHpP2.End.X > leftFloorHp.Position.X - 8f)
                        failures.Add($"[InkmawPhase2] Left small tentacle HP {leftSmallHpP2} crowds Left floor-breach tentacle HP {leftFloorHp}");
                    if (rightSmallHpP2.Position.X < rightFloorHp.End.X + 8f)
                        failures.Add($"[InkmawPhase2] Right small tentacle HP {rightSmallHpP2} crowds Right floor-breach tentacle HP {rightFloorHp}");

                    // Restore water mode & remove temporary outer small tentacles.
                    NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Normal);
                    NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(isLeft: true, alive: true, floorBreach: false, playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncTentaclePresence(isLeft: false, alive: true, floorBreach: false, playEmerge: false);
                    foreach (var sc in smallNodes)
                    {
                        world.RemoveChild(sc);
                        sc.QueueFree();
                    }
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: true, alive: false, variantSkin: "spike", playEmerge: false);
                    NAbyssalInkmawBossBackground.Current?.SyncSmallTentaclePresence(isOuterLeft: false, alive: false, variantSkin: "shell", playEmerge: false);
                }
            }
        }
    }

    private async Task CaptureAbyssalInkVisualAuditsAsync(
        SubViewport view,
        string output,
        Battle mpBattle,
        List<NCreature> majorEnemies,
        List<NCreature> smallEnemies)
    {
        var p0 = mpBattle.Players[0];
        var cardScene = GD.Load<PackedScene>("res://scenes/cards/card.tscn");

        // 1. Live GPU Zero-Leak Card Audit: 6 wildly different card archetypes top (unveiled) vs bottom (veiled)!
        var auditLayer = new Control
        {
            Name = "InkCardAuditBoard",
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            ZAsRelative = false,
            ZIndex = 90,
        };
        var backdrop = new ColorRect
        {
            Color = new Color(0.08f, 0.10f, 0.14f, 1f),
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
        };
        auditLayer.AddChild(backdrop);
        view.AddChild(auditLayer);

        CardModel MakeModel(Func<CardModel> create, bool upgrade = false)
        {
            CardModel m = create();
            if (upgrade && m.IsUpgradable)
                m.UpgradeInternal();
            return m;
        }

        CardModel[] topModels =
        [
            MakeModel(() => mpBattle.State.CreateCard<StrikeIronclad>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<DemonForm>(p0), upgrade: true),
            MakeModel(() => mpBattle.State.CreateCard<Shockwave>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Whirlwind>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Comet>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Wound>(p0)),
        ];
        CardModel[] botModels =
        [
            MakeModel(() => mpBattle.State.CreateCard<StrikeIronclad>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<DemonForm>(p0), upgrade: true),
            MakeModel(() => mpBattle.State.CreateCard<Shockwave>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Whirlwind>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Comet>(p0)),
            MakeModel(() => mpBattle.State.CreateCard<Wound>(p0)),
        ];

        var spawnedCards = new List<NCard>();
        for (int i = 0; i < topModels.Length; i++)
        {
            float cx = 195f + i * 305f;
            var topCard = cardScene.Instantiate<NCard>();
            topCard.Model = topModels[i];
            topCard.Position = new Vector2(cx, 275f);
            topCard.Scale = new Vector2(0.76f, 0.76f);
            auditLayer.AddChild(topCard);
            topCard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            spawnedCards.Add(topCard);

            await CardPileCmd.AddGeneratedCardToCombat(botModels[i], PileType.Hand, null);
            AbyssalInkBlackout.InkCard(botModels[i], randomizeCost: true);
            var botCard = cardScene.Instantiate<NCard>();
            botCard.Model = botModels[i];
            botCard.Position = new Vector2(cx, 785f);
            botCard.Scale = new Vector2(0.76f, 0.76f);
            auditLayer.AddChild(botCard);
            botCard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            botCard.CardHighlight?.AnimShow();
            NInkBlackoutCardVeil.Refresh(botCard);
            spawnedCards.Add(botCard);
        }

        for (int f = 0; f < 4; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        // Programmatic zero-leak check on all 6 veiled NCard instances!
        for (int i = 0; i < botModels.Length; i++)
        {
            NCard botCard = spawnedCards[i * 2 + 1];
            var veil = botCard.Body.GetNodeOrNull<NInkBlackoutCardVeil>("AbyssalInkBlackoutVeil");
            Assert(veil != null, $"[InkCardAudit] Veiled card #{i} ({botModels[i].Id.Entry}) must mount NInkBlackoutCardVeil.");
            foreach (string leakPath in new[] { "%Frame", "%TitleBanner", "%StarIcon", "%Highlight", "%EnergyIcon", "%TypePlaque" })
            {
                if (botCard.GetNodeOrNull<CanvasItem>(leakPath) is { } leakNode)
                {
                    Assert(leakNode.Modulate.A <= 0.01f,
                        $"[InkCardAudit] Card #{i} ({botModels[i].Id.Entry}) leaked {leakPath} with alpha={leakNode.Modulate.A}.");
                }
            }
        }

        using (var cardImg = view.GetTexture().GetImage())
        {
            Assert(cardImg.SavePng(Path.Combine(output, "abyssal_ink_cards_zero_leak_audit.png")) == Error.Ok,
                "[InkCardAudit] Zero-leak veiled card board saved.");
        }

        view.RemoveChild(auditLayer);
        auditLayer.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 2. Live GPU Screen-Drench Spray Overlay Audit (mid-sweep over live 4P Boss arena)!
        var drenchShader = GD.Load<Shader>("res://shaders/vfx/abyssal_ink_screen_drench.gdshader");
        var splatterTex = GD.Load<Texture2D>("res://images/vfx/abyssal_ink_screen_splatter.png");
        Assert(drenchShader != null && splatterTex != null, "[InkSprayAudit] Loaded screen drench shader & splatter texture.");
        var drenchMat = new ShaderMaterial { Shader = drenchShader };
        drenchMat.SetShaderParameter("sweep_progress", 0.72f);
        drenchMat.SetShaderParameter("curtain_strength", 0.88f);
        drenchMat.SetShaderParameter("border_strength", 0.96f);
        drenchMat.SetShaderParameter("drip_offset", 0.05f);
        drenchMat.SetShaderParameter("wash_progress", 0.0f);

        var drenchRect = new TextureRect
        {
            Name = "AuditInkScreenDrench",
            Texture = splatterTex,
            Material = drenchMat,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = Vector2.Zero,
            Size = new Vector2(1920f, 1080f),
            ZAsRelative = false,
            ZIndex = 80,
        };
        view.AddChild(drenchRect);

        for (int f = 0; f < 3; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        using (var drenchImg = view.GetTexture().GetImage())
        {
            Assert(drenchImg.SavePng(Path.Combine(output, "abyssal_ink_spray_screen_drench.png")) == Error.Ok,
                "[InkSprayAudit] Full-screen sweeping ink drench capture saved.");
        }

        // 3. Live GPU Turn-5 Stun + Persistent Subtle Ink Border + 5 Veiled Hand Cards Audit!
        NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Stunned);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: true, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: false, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: true, stunned: true);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: false, stunned: true);

        drenchMat.SetShaderParameter("sweep_progress", 1.35f);
        drenchMat.SetShaderParameter("curtain_strength", 0.0f);
        drenchMat.SetShaderParameter("border_strength", 0.40f);
        drenchMat.SetShaderParameter("drip_offset", 0.04f);

        var handHolder = new Node2D { Name = "AuditInkedHand", ZAsRelative = false, ZIndex = 70 };
        view.AddChild(handHolder);
        for (int i = 0; i < 5; i++)
        {
            var hc = cardScene.Instantiate<NCard>();
            hc.Model = botModels[i];
            hc.Position = new Vector2(610f + i * 175f, 940f + MathF.Abs(i - 2) * 14f);
            hc.RotationDegrees = (i - 2) * 4.5f;
            hc.Scale = new Vector2(0.56f, 0.56f);
            handHolder.AddChild(hc);
            hc.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            NInkBlackoutCardVeil.Refresh(hc);
        }

        for (int f = 0; f < 6; f++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        using (var stunImg = view.GetTexture().GetImage())
        {
            Assert(stunImg.SavePng(Path.Combine(output, "abyssal_ink_turn5_stun_blackout.png")) == Error.Ok,
                "[InkTurn5Audit] Turn 5 Stun + subtle ink screen border + veiled hand capture saved.");
        }

        // Clean up temporary audit nodes & reset stances
        view.RemoveChild(handHolder);
        handHolder.QueueFree();
        view.RemoveChild(drenchRect);
        drenchRect.QueueFree();
        foreach (CardModel m in botModels)
            AbyssalInkBlackout.Forget(m, restoreTrackedCost: true);
        NAbyssalInkmawBossBackground.Current?.SetBossStance(NAbyssalInkmawBossBackground.BossStance.Normal);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: true, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetTentacleStunned(isLeft: false, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: true, stunned: false);
        NAbyssalInkmawBossBackground.Current?.SetSmallTentacleStunned(isOuterLeft: false, stunned: false);
    }
}
