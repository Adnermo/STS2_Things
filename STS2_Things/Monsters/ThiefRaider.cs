using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    private readonly HashSet<Type> _spawnedRaiders = new();

    protected override string VisualsPath =>
        SceneHelper.GetScenePath("creature_visuals/fallback");

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 28, 26);

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
        // 收集场上已有的劫掠者类型
        foreach (var enemy in CombatState.Enemies)
        {
            var t = enemy.Monster?.GetType();
            if (t != null && _raiderTypes.Contains(t))
                _spawnedRaiders.Add(t);
        }

        var rng = new Random();
        for (var i = 0; i < 2; i++)
        {
            // 优先选未出现过的劫掠者
            var candidates = _raiderTypes.Where(t => !_spawnedRaiders.Contains(t)).ToList();
            if (candidates.Count == 0)
                candidates = _raiderTypes.ToList();

            var raiderType = candidates[rng.Next(candidates.Count)];
            _spawnedRaiders.Add(raiderType);

            var slotName = CombatState.Encounter.Slots
                .FirstOrDefault(s => s.StartsWith("raider_") && CombatState.Enemies.All(c => c.SlotName != s),
                    null);
            if (slotName != null)
            {
                var mi = typeof(CreatureCmd).GetMethod("Add")!.MakeGenericMethod(raiderType);
                await (Task)mi.Invoke(null, new object[] { CombatState, slotName })!;
            }
        }

        // 逃离
        NCombatRoom.Instance?.GetCreatureNode(Creature)?.ToggleIsInteractable(false);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f);
        await CreatureCmd.Escape(Creature);
    }
}