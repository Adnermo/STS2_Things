using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Powers;

namespace STS2_Things.Monsters;

public sealed class ThingsLeechMother : ThingsSpineMonster
{
    public const string BroodMoveId = "BROOD_MOVE";
    public const string SleepLoopSfx = "event:/sfx/enemy/enemy_attacks/slumbering_beetle/slumbering_beetle_sleep_loop";
    private NSleepingVfx? _sleepingVfx;
    private bool _isAwake;
    private bool _hasSummonedBrood;
    public bool IsAwake => _isAwake;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _sleepingVfx = null;
        _isAwake = false;
        _hasSummonedBrood = false;
    }
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 70, 66);
    public override int MaxInitialHp => MinInitialHp;
    public override float HpBarSizeReduction => 80;
    private int Plating => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 10, 8);
    private int SipDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12);
    private int CrushDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 18, 16);
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_attack_light";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/egg_layer/egg_layer_lay";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat(
        ModelDb.Monster<ThingsSanguineLeech>().AssetPaths).Concat([
            ModelDb.Power<ThingsLeechMotherSlumberPower>().ResolvedBigIconPath,
            ModelDb.Power<PlatingPower>().ResolvedBigIconPath,
            ModelDb.Card<ThingsLeechParasite>().PortraitPath]).Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmd.Apply<PlatingPower>(new ThrowingPlayerChoiceContext(), Creature, Plating, Creature, null);
        await PowerCmd.Apply<ThingsLeechMotherSlumberPower>(new ThrowingPlayerChoiceContext(), Creature, 3, Creature, null);
        SfxCmd.PlayLoop(Creature, SleepLoopSfx);
        var marker = Creature.GetCreatureNode()?.GetSpecialNode<Marker2D>("%SleepVfxPos");
        if (marker != null)
        {
            _sleepingVfx = NSleepingVfx.Create(marker.GlobalPosition);
            if (_sleepingVfx != null)
            {
                marker.AddChildSafely(_sleepingVfx);
                _sleepingVfx.Position = Vector2.Zero;
            }
        }
    }

    private void StopSleeping()
    {
        SfxCmd.StopLoop(Creature, SleepLoopSfx);
        _sleepingVfx?.Stop();
        _sleepingVfx = null;
    }

    public async Task Wake()
    {
        if (_isAwake || !Creature.IsAlive || CombatManager.Instance.IsOverOrEnding) return;
        _isAwake = true;
        StopSleeping();
        if (Creature.GetPower<ThingsLeechMotherSlumberPower>() is { } sleep) await PowerCmd.Remove(sleep);
        if (Creature.GetPower<PlatingPower>() is { } plating) await PowerCmd.Remove(plating);
        SetMoveImmediate((MoveState)MoveStateMachine!.States[BroodMoveId]);
        SfxCmd.Play("event:/sfx/enemy/enemy_attacks/slumbering_beetle/slumbering_beetle_wake_up");
        await CreatureCmd.TriggerAnim(Creature, "WakeUp", 1.0f);
    }

    public async Task LeaveUndisturbed()
    {
        if (_isAwake || !Creature.IsAlive || !CombatState.IsLiveCombat()) return;
        if (CombatState.Encounter is not ThingsLeechMotherEncounter encounter) return;
        encounter.LeaveUndisturbed();
        StopSleeping();
        await CreatureCmd.TriggerAnim(Creature, "Retreat", .95f);
        await CreatureCmd.Escape(Creature);
    }

    protected override Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature == Creature) StopSleeping();
        return Task.CompletedTask;
    }
    public override Task AfterCombatEnd(CombatRoom room)
    {
        StopSleeping();
        return Task.CompletedTask;
    }
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Enemy && participants.Contains(Creature) && Creature.IsAlive &&
            _isAwake && !_hasSummonedBrood && NextMove.Id == "STUNNED" && NextMove.CanTransitionAway)
            NextMove.FollowUpState = MoveStateMachine!.States[BroodMoveId];
        return Task.CompletedTask;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var sleep = new MoveState("SLEEP_MOVE", _ => Task.CompletedTask, new SleepIntent());
        var brood = new MoveState(BroodMoveId, Brood, new SummonIntent(), new StatusIntent(2));
        var sip = new MoveState("SIP_MOVE", Sip, new SingleAttackIntent(SipDamage));
        var crush = new MoveState("CRUSH_MOVE", Crush, new SingleAttackIntent(CrushDamage), new DefendIntent());
        var rest = new MoveState("REST_MOVE", Rest, new DefendIntent(), new HealIntent());
        var afterBrood = new ConditionalBranchState("BROOD_NEXT");
        afterBrood.AddState(brood, () => !_hasSummonedBrood);
        afterBrood.AddState(sip, () => _hasSummonedBrood);
        sleep.FollowUpState = sleep;
        brood.FollowUpState = afterBrood;
        sip.FollowUpState = crush;
        crush.FollowUpState = rest;
        rest.FollowUpState = sip;
        return new MonsterMoveStateMachine([sleep, brood, afterBrood, sip, crush, rest], sleep);
    }

    private async Task Brood(IReadOnlyList<Creature> targets)
    {
        if (_hasSummonedBrood) return;
        _hasSummonedBrood = true;
        SfxCmd.Play(CastSfx);
        await CreatureCmd.TriggerAnim(Creature, "Summon", .76f);
        List<Creature> spawnedLeeches = [];
        for (int i = 0; i < 2; i++)
        {
            string slot = i == 0 ? "brood_front" : "brood_rear";
            if (!Creature.IsAlive || !CombatState.IsLiveCombat()) return;
            string? validSlot = CombatState.Encounter?.Slots.Contains(slot) == true ? slot : null;
            if (validSlot != null && CombatState.Enemies.Any(c => c.IsAlive && c.SlotName == validSlot)) continue;
            var leech = (ThingsSanguineLeech)ModelDb.Monster<ThingsSanguineLeech>().ToMutable();
            leech.SetOpeningPhase(i == 0 ? 0 : 2);
            leech.SuppressOpeningParasites(CombatState.Players);
            await CreatureCmd.Add(leech, CombatState, CombatSide.Enemy, validSlot);
            spawnedLeeches.Add(leech.Creature);
            // BeforeCombatStart is not dispatched for mid-combat summons.
            // Initialize only the hidden native reinfestation hook, without extra cards.
            await leech.BeforeCombatStart();
        }
        Creature? firstLeech = spawnedLeeches.ElementAtOrDefault(0);
        Creature? secondLeech = spawnedLeeches.ElementAtOrDefault(1) ?? firstLeech;
        foreach (Creature target in targets.Where(c => c.IsAlive))
        {
            Player? player = target.Player ?? target.PetOwner;
            if (player == null || player.Creature.IsDead) continue;
            ICombatState? combatState = target.CombatState;
            if (combatState != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    var card = combatState.CreateCard<ThingsLeechParasite>(player);
                    card.BindOwnerLeech(i == 0 ? firstLeech : secondLeech);
                    await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, null);
                }
            }
            if (LocalContext.IsMe(player))
                await Cmd.Wait(0.1f);
        }
        await Cmd.Wait(.64f);
    }

    private async Task Sip(IReadOnlyList<Creature> targets)
    {
        var attack = await DamageCmd.Attack(SipDamage).FromMonster(this).WithAttackerAnim("Attack", .52f)
            .WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_bite").Execute(null);
        int stolen = attack.Results.SelectMany(r => r).Sum(r => Math.Max(0, r.UnblockedDamage - r.OverkillDamage));
        if (stolen > 0 && Creature.IsAlive && Creature.CurrentHp < Creature.MaxHp)
            await CreatureCmd.Heal(Creature, Math.Min(stolen, 6));
    }
    private async Task Crush(IReadOnlyList<Creature> targets)
    {
        await DamageCmd.Attack(CrushDamage).FromMonster(this).WithAttackerAnim("Crush", .66f)
            .WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_attack_blunt").Execute(null);
        if (Creature.IsAlive) await CreatureCmd.GainBlock(Creature, 6, ValueProp.Move, null);
    }
    private async Task Rest(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(Creature, "Curl", .48f);
        await CreatureCmd.GainBlock(Creature, 8, ValueProp.Move, null);
        if (Creature.IsAlive) await CreatureCmd.Heal(Creature, 4);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite sprite)
    {
        var sleep = new AnimState("sleep_loop", isLooping: true);
        var idle = new AnimState("idle_loop", isLooping: true);
        var wake = new AnimState("wake_up") { NextState = idle };
        var animator = new CreatureAnimator(sleep, sprite);
        animator.AddAnyState("WakeUp", wake);
        animator.AddAnyState("Idle", idle, () => _isAwake);
        foreach (var (trigger, clip) in new[] { ("Attack", "attack"), ("Cast", "cast"),
            ("Summon", "summon"), ("Crush", "crush"), ("Curl", "curl"), ("PowerUp", "power_up") })
            animator.AddAnyState(trigger, new AnimState(clip) { NextState = idle });
        animator.AddAnyState("Hit", new AnimState("hurt") { NextState = idle }, () => _isAwake);
        animator.AddAnyState("Dead", new AnimState("die"));
        animator.AddAnyState("Retreat", new AnimState("retreat"));
        return animator;
    }
}
