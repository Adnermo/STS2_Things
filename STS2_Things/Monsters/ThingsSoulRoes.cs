using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using STS2_Things.Encounters;
using STS2_Things.Powers;
using STS2_Things.Visuals;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Monsters;

/// <summary>
///     灵魂鱼子精英 — 4回合循环 + 死亡爆6只小 SoulRoe（原生 Spine 4.2 十二籽母体骨骼）
///     招式循环:
///     回合1: BeckonStrength         → 十二籽暖金点亮 + 给玩家 2 张 Beckon + 自身 +1 力量
///     回合2: MultiAttack6           → 离心加速依次甩出 6 颗外围鱼籽造成 1 点伤害 × 6 次
///     回合3: MultiAttack4Intangible → 依次甩出 4 颗外围鱼籽造成 1 点伤害 × 4 次 + 全簇珠光点亮获无实体
///     回合4: SpawnSoulRoe           → 有丝分裂萌出 2 只 SoulRoe（phase 1 beckon / phase 2 damage）
///     死亡机制:
///     AfterAddedToRoom 给自己挂 SoulRoesPower
///     SoulRoesPower.AfterDeath 等放射状鱼子爆发动画结束后显形 6 只 SoulRoe
/// </summary>
public sealed class ThingsSoulRoes : ThingsSpineMonster
{
    public const float BeckonRelease = 0.58f;
    public const float SummonRelease = 0.64f;
    public const float Multi4IntangiblePeak = 1.38f;
    public static readonly float[] Multi6Contacts = [0.34f, 0.54f, 0.74f, 0.94f, 1.14f, 1.34f];
    public static readonly float[] Multi4Contacts = [0.34f, 0.58f, 0.82f, 1.06f];

    protected override string AttackSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_attack";
    protected override string CastSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_beckon";
    public override string DeathSfx =>
        "event:/sfx/enemy/enemy_attacks/soul_fysh/soul_fysh_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override Vector2 ExtraDeathVfxPadding => new(1.6f, 3.0f);

    protected override void AddExtraAnimationStates(CreatureAnimator animator, AnimState idle)
    {
        animator.AddAnyState("MultiBite6", new AnimState("multi_bite_6") { NextState = idle });
        animator.AddAnyState("MultiBite4Intangible", new AnimState("multi_bite_4_intangible") { NextState = idle });
    }

    // 像原版 Queen 预加载动态随从一样，把 SoulRoe 的全部视觉/意图资源挂到召唤者。
    public override IEnumerable<string> AssetPaths =>
        base.AssetPaths
            .Concat(ModelDb.Monster<ThingsSoulRoe>().AssetPaths)
            .Append(ModelDb.Power<ThingsSoulRoesPower>().ResolvedBigIconPath)
            .Append(ModelDb.Power<IntangiblePower>().ResolvedBigIconPath)
            .Append(ModelDb.Power<StrengthPower>().ResolvedBigIconPath)
            .Distinct();

    // ========== 血量：普通进阶 80~88，高进阶 90~96（原数值翻倍） ==========
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 90, 80);

    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(
        AscensionLevel.ToughEnemies, 96, 88);

    private Task WaitForPose(string animation, float moment, float fallback)
        => WaitForSpineMomentAsync(animation, moment, fallback);

    private async Task BeginMotion(string trigger, string animation, float contact)
    {
        await CreatureCmd.TriggerAnim(Creature, trigger, 0f);
        await WaitForPose(animation, contact, contact);
    }

    private async Task FinishMotion(string animation)
    {
        var sprite = Creature.GetCreatureNode()?.Visuals?.SpineBody;
        float end;
        using (TrackEntryScope(sprite?.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? track))
        {
            if (track == null || track.GetAnimationName() != animation) return;
            end = track.GetAnimationEnd();
        }
        await WaitForPose(animation, end, 0f);
    }

    private bool IsPlayingTrackZero(string animation)
    {
        var sprite = Creature.GetCreatureNode()?.Visuals?.SpineBody;
        using var scope = TrackEntryScope(sprite?.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? current);
        return current != null && current.GetAnimationName() == animation;
    }

    // ========== 入场：给自己挂上死亡召唤 Power ==========
    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmd.Apply<ThingsSoulRoesPower>(new ThrowingPlayerChoiceContext(), Creature, 6m, Creature, null);
    }

    // ========== 4 回合招式循环 ==========
    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var list = new List<MonsterState>();

        var beckonStrMove = new MoveState(
            "BECKON_STR_MOVE", BeckonStrengthMove,
            new StatusIntent(2), new BuffIntent());

        var multi6Move = new MoveState(
            "MULTI6_MOVE", MultiAttack6Move,
            new MultiAttackIntent(1, 6));

        var multi4IntMove = new MoveState(
            "MULTI4_INT_MOVE", MultiAttack4IntangibleMove,
            new MultiAttackIntent(1, 4), new BuffIntent());

        var spawnMove = new MoveState(
            "SPAWN_MOVE", SpawnSoulRoeMove,
            new SummonIntent());

        beckonStrMove.FollowUpState = multi6Move;
        multi6Move.FollowUpState = multi4IntMove;
        multi4IntMove.FollowUpState = spawnMove;
        spawnMove.FollowUpState = beckonStrMove;

        list.Add(beckonStrMove);
        list.Add(multi6Move);
        list.Add(multi4IntMove);
        list.Add(spawnMove);

        return new MonsterMoveStateMachine(list, beckonStrMove);
    }

    // ========== 回合1: 暖金点亮 + 2 张 Beckon + 自身 +1 力量 ==========
    private async Task BeckonStrengthMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await BeginMotion("Cast", "cast", BeckonRelease);

        await CardPileCmd.AddToCombatAndPreview<Beckon>(
            targets, PileType.Discard, 2, null);

        await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(),
            Creature, 1m, Creature, null);
        await FinishMotion("cast");
    }

    // ========== 回合2: 离心加速依次甩出 6 颗外围鱼籽（1 点伤害 × 6 次） ==========
    private async Task MultiAttack6Move(IReadOnlyList<Creature> targets)
    {
        int strike = 0;
        var motion = Creature.GetCreatureNode()?.Visuals?.GetNodeOrNull<NThingsSoulRoesMotion>("Visuals/MotionTiming");
        try
        {
            await DamageCmd.Attack(1)
                .FromMonster(this)
                .WithHitCount(6)
                .WithNoAttackerAnim()
                .AfterAttackerAnim(async () =>
                {
                    int beat = strike++ % Multi6Contacts.Length;
                    if (beat == 0 || !IsPlayingTrackZero("multi_bite_6"))
                    {
                        if (beat == 0 && strike > 1)
                        {
                            motion?.FinishCombo();
                            await FinishMotion("multi_bite_6");
                        }
                        await CreatureCmd.TriggerAnim(Creature, "MultiBite6", 0f);
                    }
                    motion?.AllowBeat("multi_bite_6", beat + 1);
                    float previous = beat == 0 ? 0f : Multi6Contacts[beat - 1];
                    await WaitForPose("multi_bite_6", Multi6Contacts[beat], Multi6Contacts[beat] - previous);
                })
                .WithAttackerFx(null, AttackSfx)
                .Execute(null);
        }
        finally
        {
            motion?.FinishCombo();
        }
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;
        await FinishMotion("multi_bite_6");
    }

    // ========== 回合3: 依次甩出 4 颗鱼籽（1 点伤害 × 4 次）+ 珠光点亮获无实体 ==========
    private async Task MultiAttack4IntangibleMove(IReadOnlyList<Creature> targets)
    {
        int strike = 0;
        var motion = Creature.GetCreatureNode()?.Visuals?.GetNodeOrNull<NThingsSoulRoesMotion>("Visuals/MotionTiming");
        try
        {
            await DamageCmd.Attack(1)
                .FromMonster(this)
                .WithHitCount(4)
                .WithNoAttackerAnim()
                .AfterAttackerAnim(async () =>
                {
                    int beat = strike++ % Multi4Contacts.Length;
                    if (beat == 0 || !IsPlayingTrackZero("multi_bite_4_intangible"))
                    {
                        if (beat == 0 && strike > 1)
                        {
                            motion?.FinishCombo();
                            await FinishMotion("multi_bite_4_intangible");
                        }
                        await CreatureCmd.TriggerAnim(Creature, "MultiBite4Intangible", 0f);
                    }
                    motion?.AllowBeat("multi_bite_4_intangible", beat + 1);
                    float previous = beat == 0 ? 0f : Multi4Contacts[beat - 1];
                    await WaitForPose("multi_bite_4_intangible", Multi4Contacts[beat], Multi4Contacts[beat] - previous);
                })
                .WithAttackerFx(null, AttackSfx)
                .Execute(null);
        }
        finally
        {
            motion?.FinishCombo();
        }

        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        if (!IsPlayingTrackZero("multi_bite_4_intangible"))
        {
            await CreatureCmd.TriggerAnim(Creature, "PowerUp", 0f);
            await WaitForPose("power_up", 0.52f, 0.32f);
            if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
                return;
            await PowerCmd.Apply<IntangiblePower>(new ThrowingPlayerChoiceContext(),
                Creature, 2m, Creature, null);
            await FinishMotion("power_up");
        }
        else
        {
            await WaitForPose("multi_bite_4_intangible", Multi4IntangiblePeak, Multi4IntangiblePeak - Multi4Contacts[^1]);
            if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
                return;
            await PowerCmd.Apply<IntangiblePower>(new ThrowingPlayerChoiceContext(),
                Creature, 2m, Creature, null);
            await FinishMotion("multi_bite_4_intangible");
        }
    }

    // ========== 回合4: 有丝分裂生成 2 只 SoulRoe ==========
    private async Task SpawnSoulRoeMove(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await BeginMotion("Summon", "summon", SummonRelease);

        int[] phases = { 1, 2 }; // beckon, damage
        var spawned = 0;

        for (var i = 0; i < ThingsSoulRoesEncounter.SoulRoeSlotCount && spawned < 2; i++)
        {
            var slotName = ThingsSoulRoesEncounter.GetSoulRoeSlotName(i);
            string? validSlot = CombatState.Encounter?.Slots.Contains(slotName) == true ? slotName : null;
            if (validSlot != null && CombatState.Enemies.Any(c => c.IsAlive && c.SlotName == slotName))
                continue;
            if (validSlot == null &&
                CombatState.Enemies.Count(c => c.IsAlive && c.Monster is ThingsSoulRoe) >= ThingsSoulRoesEncounter.SoulRoeSlotCount)
                break;

            var soulRoe = (ThingsSoulRoe)ModelDb.Monster<ThingsSoulRoe>().ToMutable();
            soulRoe.StartMovePhase = phases[spawned];

            await CreatureCmd.Add(soulRoe, CombatState,
                Creature.Side, validSlot);
            spawned++;
        }

        await FinishMotion("summon");
    }
}
