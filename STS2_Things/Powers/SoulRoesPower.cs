using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2_Things.Encounters;
using STS2_Things.Monsters;

namespace STS2_Things.Powers;

/// <summary>
///     SoulRoes 死亡召唤 Power
///     Counter 层数 = 死亡时召唤的 SoulRoe 数量，模仿 PhrogParasite 的 InfestedPower
///     挂在 SoulRoes 身上:
///     1. ShouldStopCombatFromEnding() → true（死后战斗不结束）
///     2. AfterDeath() → 等死亡动画 → 生成 Amount 只 SoulRoe
///     phase 按 i % 3 循环: 0(Intangible) / 1(Beckon) / 2(Damage)
///     全部入场眩晕 1 回合
/// </summary>
public sealed class SoulRoesPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    // Counter: 层数可见，数值 = 死亡召唤数
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldStopCombatFromEnding()
    {
        return true;
    }

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature target,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || Owner != target)
            return;

        await Cmd.CustomScaledWait(deathAnimLength, deathAnimLength);

        var spawnCount = Amount;
        for (var i = 0; i < spawnCount; i++)
        {
            var slotName = SoulRoesEncounter.GetSoulRoeSlotName(i);

            if (CombatState.Enemies.Any(c => c.SlotName == slotName))
                continue;

            var soulRoe = (SoulRoe)ModelDb.Monster<SoulRoe>().ToMutable();
            soulRoe.StartStunned = true;
            soulRoe.StartMovePhase = i % 3;

            await PowerCmd.Apply<MinionPower>(new ThrowingPlayerChoiceContext(),
                await CreatureCmd.Add(soulRoe, CombatState,
                    Owner.Side, slotName),
                1m, Owner, null);
        }
    }
}