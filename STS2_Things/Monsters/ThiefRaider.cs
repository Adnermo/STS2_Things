using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

/// <summary>
///     劫掠者窃贼 — 普通敌人
///     登场自带劫掠者Buff（死亡+50金）
///     状态机: 隐匿 → 准备 → 反击 → 逃离（生成2个随机劫掠者）
/// </summary>
public sealed class ThiefRaider : MonsterModel
{
    private static readonly Type[] _raiderTypes =
    {
        typeof(AxeRubyRaider),
        typeof(AssassinRubyRaider),
        typeof(BruteRubyRaider),
        typeof(CrossbowRubyRaider),
        typeof(TrackerRubyRaider)
    };

    // 缓存 CreatureCmd.Add<T>(ICombatState, string) 泛型方法定义，避免每次召唤都反射查找
    private static readonly MethodInfo AddCreatureMethod;

    // 固定召唤顺序计数器
    private int _nextRaiderIndex;
    private bool _hasEscaped;

    static ThiefRaider()
    {
        AddCreatureMethod = typeof(CreatureCmd).GetMethods()
            .FirstOrDefault(m => m.Name == "Add" && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(ICombatState)
                && m.GetParameters()[1].ParameterType == typeof(string));
    }

    protected override string VisualsPath =>
        SceneHelper.GetScenePath("creature_visuals/fallback");

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 17, 15);

    public override int MaxInitialHp => MinInitialHp;

    private int RetaliateDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies, 5, 4);

    private int PlayerCount => CombatState?.Players.Count ?? 1;

    // ========== 入场 ==========
    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmd.Apply<ThiefRaiderPower>(new ThrowingPlayerChoiceContext(),
            Creature, 1m, Creature, null);

        // 开局给所有队友 1 层虚弱（掩护代价）
        foreach (var enemy in CombatState.Enemies)
        {
            if (enemy != Creature && enemy.IsAlive)
                await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(),
                    new[] { enemy }, 1m, Creature, null);
        }
    }

    // ========== 状态机 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();

        var stealthMove = new MoveState("STEALTH_MOVE", StealthMove, new DebuffIntent());
        var prepareMove = new MoveState("PREPARE_MOVE", PrepareMove, new DefendIntent());
        var retaliateMove = new MoveState("RETALIATE_MOVE", RetaliateMove, new SingleAttackIntent(RetaliateDamage),
            new DefendIntent());
        var escapeMove = new MoveState("ESCAPE_MOVE", EscapeMove, new EscapeIntent());

        stealthMove.FollowUpState = prepareMove;
        prepareMove.FollowUpState = retaliateMove;
        retaliateMove.FollowUpState = escapeMove;
        escapeMove.FollowUpState = escapeMove; // 逃离后不再行动

        list.Add(stealthMove);
        list.Add(prepareMove);
        list.Add(retaliateMove);
        list.Add(escapeMove);

        return new MonsterMoveStateMachine(list, stealthMove);
    }

    // ========== 招式 ==========

    private async Task StealthMove(IReadOnlyList<Creature> targets)
    {
        TalkCmd.Play(new LocString("monsters", "THIEF_RAIDER.moves.STEALTH_MOVE.speakLine"), Creature, VfxColor.Blue);
        await Cmd.Wait(0.5f);
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);
        await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(),
            targets, 1m, Creature, null);
    }

    private async Task PrepareMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);
        await CreatureCmd.GainBlock(Creature, 8 * PlayerCount, ValueProp.Move, null);
    }

    private async Task RetaliateMove(IReadOnlyList<Creature> targets)
    {
        TalkCmd.Play(new LocString("monsters", "THIEF_RAIDER.moves.RETALIATE_MOVE.speakLine"), Creature, VfxColor.Blue);
        await Cmd.Wait(0.5f);
        await DamageCmd.Attack(RetaliateDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", 0.5f)
            .WithAttackerFx(null, AttackSfx)
            .Execute(null);
        await CreatureCmd.GainBlock(Creature, 6 * PlayerCount, ValueProp.Move, null);
    }

    private async Task EscapeMove(IReadOnlyList<Creature> targets)
    {
        if (_hasEscaped) return; // 防止 Escape 失败时重复召唤
        _hasEscaped = true;

        // 固定顺序召唤1只劫掠者
        var raiderType = _raiderTypes[_nextRaiderIndex % _raiderTypes.Length];
        _nextRaiderIndex++;

        var slotName = CombatState.Encounter.Slots
            .FirstOrDefault(s => s.StartsWith("raider_") && CombatState.Enemies.All(c => c.SlotName != s),
                null);
        if (slotName != null && AddCreatureMethod != null)
        {
            // 使用静态缓存的泛型方法定义，避免每次召唤都反射查找
            var mi = AddCreatureMethod.MakeGenericMethod(raiderType);
            await (Task)mi.Invoke(null, new object[] { CombatState, slotName })!;
        }

        // 逃离
        NCombatRoom.Instance?.GetCreatureNode(Creature)?.ToggleIsInteractable(false);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);
        await CreatureCmd.Escape(Creature);
    }
}