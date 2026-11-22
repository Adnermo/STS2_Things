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
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Powers;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Monsters;

public sealed class ThingsSanguineLeech : ThingsSpineMonster
{
    public const float BiteContact = .46f;
    public const float ParasiteRelease = .62f;
    public const float CurlContact = .38f;
    public const int CurlBlock = 6;
    public const string ReinfestMoveId = "REINFEST_MOVE";
    private int _openingPhase;
    private MoveState? _reinfest;
    private bool _reinfestPending;
    private HashSet<ulong> _openingParasitePlayers = [];
    public int OpeningPhase => _openingPhase;
    public void SuppressOpeningParasites(IEnumerable<Player> players)
    {
        AssertMutable();
        foreach (var player in players) _openingParasitePlayers.Add(player.NetId);
    }
    public void SetOpeningPhase(int phase) { AssertMutable(); _openingPhase = Math.Clamp(phase, 0, 3); }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _openingPhase = 0;
        _reinfest = null;
        _reinfestPending = false;
        _openingParasitePlayers.Clear();
    }
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 32, 29);
    public override int MaxInitialHp => MinInitialHp + 4;
    public override float HpBarSizeReduction => 130f;
    private int BiteDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_attack_light";
    protected override string CastSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_devour";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_die";
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Card<ThingsLeechParasite>().PortraitPath]);

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _openingParasitePlayers = [.. _openingParasitePlayers];
    }

    public override async Task BeforeCombatStart()
    {
        if (!Creature.IsAlive) return;
        await PowerCmd.Apply<ThingsLeechInfestationPower>(new ThrowingPlayerChoiceContext(), Creature, 1, Creature, null);
    }

    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (!Creature.IsAlive || !player.Creature.IsAlive || player.PlayerCombatState?.TurnNumber != 1 ||
            !ReferenceEquals(combatState, CombatState) || !_openingParasitePlayers.Add(player.NetId)) return;
        // Toolbox uses the same native hook and generation command. Hand ownership,
        // visual holders, notifications, overflow and indices stay in one lifecycle.
        var card = combatState.CreateCard<ThingsLeechParasite>(player);
        card.BindOwnerLeech(Creature);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
    }

    public void RequestReinfestation()
    {
        AssertMutable();
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding) return;
        // Keep the already advertised action. Consume the request only when
        // preparing a later turn, including when the current move is Infest.
        _reinfestPending = true;
    }

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Enemy && participants.Contains(Creature)) PrepareReinfestation();
        return Task.CompletedTask;
    }

    private void PrepareReinfestation()
    {
        if (!_reinfestPending || _reinfest == null || !Creature.IsAlive ||
            IsPerformingMove || !NextMove.CanTransitionAway || CombatManager.Instance.IsOverOrEnding) return;
        // Insert without advancing or resetting the normal cycle. Stun uses a
        // follow-up ID; a stun that resumes this insertion must retain its cursor.
        MonsterState resume = NextMove.FollowUpState ?? MoveStateMachine!.States[NextMove.FollowUpStateId!];
        if (resume != _reinfest) _reinfest.FollowUpState = resume;
        // The mandatory insertion survives the native next-turn roll. Further
        // requests during it keep the same saved normal action.
        SetMoveImmediate(_reinfest);
        _reinfestPending = false;
    }

    protected override void AddExtraAnimationStates(CreatureAnimator animator, AnimState idle)
    {
        animator.AddAnyState("Curl", new AnimState("curl") { NextState = idle });
        animator.AddAnyState("Feed", new AnimState("feed") { NextState = idle });
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState[] cycle = [
            new("SIP_MOVE", Sip, new SingleAttackIntent(BiteDamage)),
            new("INFEST_MOVE", Infest, new StatusIntent(1)),
            new("SIP_AGAIN_MOVE", Sip, new SingleAttackIntent(BiteDamage)),
            new("CURL_MOVE", Curl, new DefendIntent())];
        for (int i = 0; i < cycle.Length; i++) cycle[i].FollowUpState = cycle[(i + 1) % cycle.Length];
        _reinfestPending = false;
        _reinfest = new MoveState(ReinfestMoveId, Infest, new StatusIntent(1))
        {
            MustPerformOnceBeforeTransitioning = true,
            FollowUpState = cycle[_openingPhase],
        };
        return new MonsterMoveStateMachine([.. cycle, _reinfest], cycle[_openingPhase]);
    }

    private Task WaitForPose(string animation, float moment, float fallback)
        => WaitForSpineMomentAsync(animation, moment, fallback);

    private async Task Begin(string trigger, string animation, float contact)
    {
        await CreatureCmd.TriggerAnim(Creature, trigger, 0f);
        await WaitForPose(animation, contact, contact);
    }

    private async Task Sip(IReadOnlyList<Creature> targets)
    {
        var attack = await DamageCmd.Attack(BiteDamage).FromMonster(this).WithNoAttackerAnim()
            .AfterAttackerAnim(() => Begin("Attack", "attack", BiteContact))
            .WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_bite").Execute(null);
        await WaitForPose("attack", 1.12f, 0);
        int stolen = attack.Results.SelectMany(hit => hit).Sum(hit => Math.Max(0, hit.UnblockedDamage - hit.OverkillDamage));
        if (Creature.IsAlive && Creature.CurrentHp < Creature.MaxHp && stolen > 0)
        {
            await Begin("Feed", "feed", .30f);
            await CreatureCmd.Heal(Creature, stolen);
            await WaitForPose("feed", .85f, 0);
        }
    }

    private async Task Infest(IReadOnlyList<Creature> targets)
    {
        SfxCmd.Play(CastSfx);
        await Begin("Cast", "cast", ParasiteRelease);
        foreach (Creature target in targets.Where(c => c.IsAlive))
        {
            Player? player = target.Player ?? target.PetOwner;
            if (player == null || player.Creature.IsDead) continue;
            ICombatState? combatState = target.CombatState;
            if (combatState != null)
            {
                var card = combatState.CreateCard<ThingsLeechParasite>(player);
                card.BindOwnerLeech(Creature);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, null);
            }
            if (LocalContext.IsMe(player))
                await Cmd.Wait(0.1f);
        }
        await WaitForPose("cast", 1.45f, 0);
    }

    private async Task Curl(IReadOnlyList<Creature> targets)
    {
        await Begin("Curl", "curl", CurlContact);
        await CreatureCmd.GainBlock(Creature, CurlBlock, ValueProp.Move, null);
        await WaitForPose("curl", 1.1f, 0);
    }
}
