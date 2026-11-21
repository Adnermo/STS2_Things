using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Compatibility;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
///     SoulRoes 死亡召唤 Power
///     Counter 层数 = 死亡时召唤的 SoulRoe 数量，模仿 PhrogParasite 的 InfestedPower
///     挂在 SoulRoes 身上:
///     1. ShouldStopCombatFromEnding() → true（死后战斗不结束）
///     2. AfterDeath() → 生成 Amount 只 SoulRoe，死亡动画后显形
///     phase 按 i % 3 循环: 0(Intangible) / 1(Beckon) / 2(Damage)
///     全部入场获得 1 层无实体，眩晕 1 回合
/// </summary>
public sealed class ThingsSoulRoesPower : ThingsPowerModel
{
    public override PowerType Type => PowerType.Buff;

    // Counter: 层数可见，数值 = 死亡召唤数
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldStopCombatFromEnding()
    {
        return true;
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || Owner != target)
            return;

        var spawnCount = (int)Math.Clamp(Amount, 0m, ThingsSoulRoesEncounter.SoulRoeSlotCount);
        var spawnedSoulRoes = new List<Creature>(spawnCount);
        var spawned = 0;
        decimal intangibleStacks = CombatState.CurrentSide == CombatSide.Enemy ? 2m : 1m;
        for (var i = 0; i < ThingsSoulRoesEncounter.SoulRoeSlotCount && spawned < spawnCount; i++)
        {
            var slotName = ThingsSoulRoesEncounter.GetSoulRoeSlotName(i);
            string? validSlot = CombatState.Encounter?.Slots.Contains(slotName) == true ? slotName : null;

            // A multi-target hit may have reduced an existing SoulRoe to 0 HP before its
            // queued death is processed. Dead creatures must not reserve a slot for the
            // replacement wave, otherwise 2 existing roes + the intended 6 only yields 4.
            if (validSlot != null && CombatState.Enemies.Any(c => c.IsAlive && c.SlotName == slotName))
                continue;
            if (validSlot == null &&
                CombatState.Enemies.Count(c => c.IsAlive && c.Monster is ThingsSoulRoe) >= ThingsSoulRoesEncounter.SoulRoeSlotCount)
                break;

            var soulRoe = (ThingsSoulRoe)ModelDb.Monster<ThingsSoulRoe>().ToMutable();
            soulRoe.StartStunned = true;
            soulRoe.StartMovePhase = spawned % 3;

            var creature = await CreatureCmd.Add(soulRoe, CombatState,
                Owner.Side, validSlot);
            // Apply to every successfully spawned roe, before its delayed reveal.
            // Use the newborn as the source: its parent has already died.
            await PowerCmd.Apply<IntangiblePower>(choiceContext, creature, intangibleStacks, creature, null);
            Sts2VersionCompatibility.SetCreatureNodeVisible(creature, visible: false);
            spawnedSoulRoes.Add(creature);
            spawned++;
        }

        // Match the native InfestedPower lifecycle: add the replacements immediately so
        // combat cannot end, then reveal them after the parent's death animation without
        // blocking processing of other creatures killed by the same attack.
        _ = TaskHelper.RunSafely(RevealAfterDeathAnimation(spawnedSoulRoes, deathAnimLength));
    }

    private static async Task RevealAfterDeathAnimation(
        IReadOnlyList<Creature> soulRoes,
        float deathAnimLength)
    {
        await Cmd.CustomScaledWait(deathAnimLength, deathAnimLength);
        foreach (var soulRoe in soulRoes)
        {
            if (!soulRoe.IsAlive)
                continue;
            Sts2VersionCompatibility.SetCreatureNodeVisible(soulRoe, visible: true);
        }
    }
}
