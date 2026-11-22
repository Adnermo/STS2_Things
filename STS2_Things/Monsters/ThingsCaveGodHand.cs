using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Cards;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// A damageable arm and an intent/attack proxy for the body's single move cycle.
/// Both concrete models keep their shipped IDs and visuals; lifecycle rules live here.
/// </summary>
public abstract class ThingsCaveGodHand : ThingsMonsterModel
{
    public enum Action
    {
        Rest,
        Down,
        Sweep,
        CleaveSweep,
        HeavyPunch,
        Grab,
        Slam,
        WeakStun,
        WeakAttack,
        Recover,
        EscapeStun
    }

    /// <summary>
    /// Move-state id for an action. Ids double as localization keys
    /// (THINGS_CAVE_GOD_LEFT_HAND / _RIGHT_HAND .moves.&lt;id&gt;.title), so they follow the body's UPPER_SNAKE style.
    /// </summary>
    public static string MoveId(Action action) => action switch
    {
        Action.Rest => "REST",
        Action.Down => "DOWN_STATE",
        Action.Sweep => "FRONT_SWEEP",
        Action.CleaveSweep => "CLEAVE_SWEEP",
        Action.HeavyPunch => "HEAVY_PUNCH",
        Action.Grab => "GRAB_PLAYER",
        Action.Slam => "AIR_SLAM",
        Action.WeakStun => "WEAK_STUN",
        Action.WeakAttack => "WEAK_ATTACK",
        Action.Recover => "RECOVER",
        Action.EscapeStun => "GRAB_STUNNED",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    public abstract bool IsLeft { get; }
    public bool IsDown { get; private set; }
    public bool IsStunned { get; private set; }
    public bool IsGrabbing { get; private set; }
    private bool _applyingGrowth;
    private int _savedPositiveStrength;
    private List<CardModel> _stolenCards = [];
    public IReadOnlyList<CardModel> StolenCards => _stolenCards;

    // ToMutable() is a MemberwiseClone: without this every arm instance (and the canonical model)
    // would share one stolen-card list, leaking cards across combats and runs.
    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _stolenCards = [.. _stolenCards];
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        IsDown = false;
        IsStunned = false;
        IsGrabbing = false;
        _applyingGrowth = false;
        _savedPositiveStrength = 0;
    }

    private static readonly Func<CardModel, bool>[] StealPriorities =
    [
        c => c.Rarity == CardRarity.Ancient,
        c => c.Rarity == CardRarity.Rare,
        c => c.Rarity == CardRarity.Uncommon,
        c => c.Rarity == CardRarity.Event,
        c => c.Rarity == CardRarity.Common,
        c => c.Rarity is CardRarity.Basic or CardRarity.Quest,
        c => c.Rarity == CardRarity.Token,
        c => c.Rarity is CardRarity.Status or CardRarity.Curse or CardRarity.None
    ];

    public ThingsCaveGodBody? Body => CombatState?.Enemies.Select(c => c.Monster).OfType<ThingsCaveGodBody>().FirstOrDefault();
    private NCaveGodBossBackground? Background => Body?.Background;
    private bool CanAct => !IsDown && !IsStunned && Body is { IsDefeated: false, IsEnrageLocked: false };

    private bool IsPairedFirstHit =>
        IsLeft &&
        Body is { Phase: >= 2, IsWeakPhase: false, IsEnrageLocked: false } body &&
        body.RightHand is { IsDown: false, IsStunned: false };

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 75, 68) + (Sts2VersionSeam.IsDread() ? 18 : 0);
    public override int MaxInitialHp => MinInitialHp;
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Stone;
    public override string DeathSfx => "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone";
    public override bool ShouldFadeAfterDeath => false;
    public override bool ShouldDisappearFromDoom => false;
    public override float DeathAnimLengthOverride => 0f;
    protected override bool IsMonsterInteractable => !IsDown && !IsGrabbing;
    public override bool ShouldAllowHitting(Creature creature) => creature != Creature || _applyingGrowth || (!IsDown && !IsGrabbing);
    public override bool ShouldAllowTargeting(Creature target) => target != Creature || (!IsDown && !IsGrabbing);

    // Direct poison/retaliation damage does not use target selection. A downed arm
    // must also be protected on the actual damage path.
    protected override decimal OnModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
        => target == Creature && IsDown ? 0m : 1m;

    private int Phase1SingleArmBonus => Body is { Phase: >= 2 } ? 0 : 3;
    private int SweepDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 8) + Phase1SingleArmBonus;
    private int CleaveSweepDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 11, 9) + Phase1SingleArmBonus;
    private int HeavyPunchDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 13, 11) + Phase1SingleArmBonus;
    private int GrabDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6) + Phase1SingleArmBonus;
    private int SlamDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 18, 16) + Phase1SingleArmBonus;
    private int WeakDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 16, 14);

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState rest = new(MoveId(Action.Rest), Rest);
        MoveState down = new(MoveId(Action.Down), Rest);
        MoveState sweep = new(MoveId(Action.Sweep), Sweep, new SingleAttackIntent(() => SweepDamage), new CardDebuffIntent());
        MoveState cleaveSweep = new(MoveId(Action.CleaveSweep), CleaveSweep, new SingleAttackIntent(() => CleaveSweepDamage), new DebuffIntent());
        MoveState heavyPunch = new(MoveId(Action.HeavyPunch), HeavyPunch, new SingleAttackIntent(() => HeavyPunchDamage));
        MoveState grab = new(MoveId(Action.Grab), Grab, new SingleAttackIntent(() => GrabDamage), new DebuffIntent());
        MoveState slam = new(MoveId(Action.Slam), Slam, new SingleAttackIntent(() => SlamDamage));
        MoveState weakStun = new(MoveId(Action.WeakStun), Stun, new StunIntent());
        MoveState weakAttack = new(MoveId(Action.WeakAttack), WeakAttack, new SingleAttackIntent(() => WeakDamage));
        MoveState recover = new(MoveId(Action.Recover), Rest, new StunIntent(), new HealIntent());
        MoveState escapeStun = new(MoveId(Action.EscapeStun), Stun, new StunIntent());

        rest.FollowUpState = rest;
        down.FollowUpState = down;
        sweep.FollowUpState = rest;
        cleaveSweep.FollowUpState = rest;
        heavyPunch.FollowUpState = rest;
        grab.FollowUpState = rest;
        slam.FollowUpState = rest;
        weakStun.FollowUpState = weakAttack;
        weakAttack.FollowUpState = weakAttack;
        recover.FollowUpState = weakAttack;
        escapeStun.FollowUpState = rest;

        return new MonsterMoveStateMachine(
            [rest, down, sweep, cleaveSweep, heavyPunch, grab, slam, weakStun, weakAttack, recover, escapeStun],
            rest);
    }

    public void Plan(Action action)
    {
        // Preserve a native CreatureCmd.Stun until it has actually consumed its enemy action.
        if (!IsDown && NextMove?.Id == "STUNNED" && !NextMove.CanTransitionAway)
        {
            IsStunned = true;
            return;
        }
        if (IsDown && action != Action.Recover) action = Action.Down;
        IsStunned = action is Action.WeakStun or Action.Recover or Action.EscapeStun or Action.Down;
        string moveId = MoveId(action);
        if (MoveStateMachine == null || !MoveStateMachine.States.TryGetValue(moveId, out MonsterState? state) || state is not MoveState move)
        {
            Log.Warn($"STS2_Things: Cave God hand state '{moveId}' is unavailable; keeping the native hand state.");
            return;
        }
        SetMoveImmediate(move, forceTransition: true);
        NCombatRoom.Instance?.GetCreatureNode(Creature)?.ToggleIsInteractable(IsHealthBarVisible);
        ConfigureCreatureNode();
    }

    internal void Disable()
    {
        IsDown = true;
        IsGrabbing = false;
        Plan(Body is { IsDefeated: false, NextMove.Id: ThingsCaveGodBody.RecoverId or ThingsCaveGodBody.EnrageRecoverId }
            ? Action.Recover
            : Action.Down);
    }

    internal async Task Restore()
    {
        IsDown = false;
        IsStunned = false;
        IsGrabbing = false;
        if (Creature.CurrentHp < Creature.MaxHp)
            await CreatureCmd.Heal(Creature, Creature.MaxHp - Creature.CurrentHp, playAnim: true);
        await EnsureAgingPower();
        if (Body is { } body)
        {
            int bodyStrength = Math.Max(0, body.Creature.GetPower<StrengthPower>()?.Amount ?? 0);
            int targetStrength = Math.Max(_savedPositiveStrength, bodyStrength);
            int currentStrength = Creature.GetPower<StrengthPower>()?.Amount ?? 0;
            if (targetStrength > currentStrength)
                await GainStrength(targetStrength - currentStrength, body.Creature);
        }
        _savedPositiveStrength = 0;
        Plan(Action.Rest);
        ConfigureCreatureNode();
    }

    private void ConfigureCreatureNode()
    {
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(Creature);
        if (creatureNode != null && GodotObject.IsInstanceValid(creatureNode))
        {
            creatureNode.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
            creatureNode.ZAsRelative = false;
            Background?.UpdateHandAnchors([creatureNode]);

            Node? healthBar = creatureNode.GetNodeOrNull("%HealthBar")
                ?? creatureNode.GetNodeOrNull("HealthBar")
                ?? creatureNode.GetNodeOrNull("StateDisplay")
                ?? creatureNode.FindChild("HealthBar", recursive: true, owned: false)
                ?? creatureNode.FindChild("StateDisplay", recursive: true, owned: false);
            if (healthBar is CanvasItem hpCanvas)
            {
                hpCanvas.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                hpCanvas.ZAsRelative = false;
            }

            Node? intents = creatureNode.GetNodeOrNull("%Intents")
                ?? creatureNode.GetNodeOrNull("Intents")
                ?? creatureNode.GetNodeOrNull("%IntentContainer")
                ?? creatureNode.GetNodeOrNull("IntentContainer")
                ?? creatureNode.FindChild("Intents", recursive: true, owned: false)
                ?? creatureNode.FindChild("IntentContainer", recursive: true, owned: false);
            if (intents is CanvasItem intentCanvas)
            {
                intentCanvas.ZIndex = NCaveGodHandCreatureVisuals.CreatureUiZIndex;
                intentCanvas.ZAsRelative = false;
            }
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        ConfigureCreatureNode();
        await EnsureAgingPower();
    }

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        await base.BeforeSideTurnStart(choiceContext, side, participants, combatState);
        await EnsureAgingPower();
    }

    public async Task EnsureAgingPower()
    {
        if (Creature != null && !Creature.HasPower<ThingsCaveGodAgingPower>())
        {
            _applyingGrowth = true;
            try
            {
                await PowerCmd.Apply<ThingsCaveGodAgingPower>(
                    new ThrowingPlayerChoiceContext(), Creature, 1m, Creature, null);
            }
            finally { _applyingGrowth = false; }
        }
    }

    public override bool ShouldDie(Creature creature) =>
        creature != Creature || Body is not { IsDefeated: false };

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature) =>
        creature != Creature || Body is not { IsDefeated: false };

    public override async Task AfterPreventingDeath(Creature creature)
    {
        if (creature != Creature || Body is not { IsDefeated: false } body) return;
        await HandleArmDefeatedAsync(body);
    }

    protected override async Task OnCreatureDeathAsync(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        await BaseAfterDeathAsync(choiceContext, creature, source, wasRemovalPrevented, deathAnimLength);
        if (creature != Creature) return;
        await ReleaseStolenCards();
        // Force-kill mods (e.g. CreatureCmd.Kill(arm, force: true)) bypass ShouldDie/AfterPreventingDeath;
        // ShouldCreatureBeRemovedFromCombatAfterDeath keeps the arm in CombatState so we still expose the core.
        if (!wasRemovalPrevented && Body is { IsDefeated: false } body)
            await HandleArmDefeatedAsync(body);
    }

    private async Task HandleArmDefeatedAsync(ThingsCaveGodBody body)
    {
        if (IsDown)
        {
            if (Creature.CurrentHp <= 0)
            {
                await CreatureCmd.Heal(Creature, 1m, playAnim: false);
                if (Creature.CurrentHp <= 0)
                    await CreatureCmd.SetCurrentHp(Creature, 1);
            }
            await Sts2VersionSeam.DislodgeImpaledArrowsAsync(Creature);
            await ReleaseStolenCards();
            return;
        }

        _savedPositiveStrength = Math.Max(_savedPositiveStrength, Creature.GetPower<StrengthPower>()?.Amount ?? 0);
        bool wasExposed = body.IsWeakPhase;
        await Sts2VersionSeam.DislodgeImpaledArrowsAsync(Creature);
        Disable();
        if (Creature.CurrentHp <= 0)
        {
            await CreatureCmd.Heal(Creature, 1m, playAnim: false);
            if (Creature.CurrentHp <= 0)
                await CreatureCmd.SetCurrentHp(Creature, 1);
        }
        await ClearDebuffs(Creature);
        await EnsureAgingPower();
        if (Creature.GetPower<ThingsCaveGodAgingPower>() is { } aging)
            await aging.OnArmDowned();
        await ReleaseStolenCards();
        if (!wasExposed)
            await body.ExposeCore(this);
        else
            body.RefreshHandIntents();
    }

    public override void BeforeRemovedFromRoom()
    {
        base.BeforeRemovedFromRoom();
        ClearStolenCardVisual();
    }

    internal static Task ClearDebuffs(Creature creature) => ClearDebuffsSafelyAsync(creature);

    internal async Task GainStrength(int amount, Creature source)
    {
        _applyingGrowth = true;
        try
        {
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Creature, amount, source, null);
        }
        finally { _applyingGrowth = false; }
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature == Creature && delta < 0 && Body?.IsDefeated != true)
            Background?.PlayHurtAnim(forceGroan: false, hitPos: creature.GetCreatureNode()?.VfxSpawnPosition);
        return Task.CompletedTask;
    }

    internal void CancelGrab(bool stun)
    {
        IsGrabbing = false;
        if (stun && !IsDown) Plan(Action.EscapeStun);
        NCombatRoom.Instance?.GetCreatureNode(Creature)?.ToggleIsInteractable(IsHealthBarVisible);
        ConfigureCreatureNode();
    }

    private static Task Rest(IReadOnlyList<Creature> _) => Task.CompletedTask;
    private Task Stun(IReadOnlyList<Creature> _) => Task.CompletedTask;

    private async Task Sweep(IReadOnlyList<Creature> targets)
    {
        if (!CanAct || Body?.IsWeakPhase != false) return;
        Background?.StartCardSnatchAnim(IsLeft, targets);
        try
        {
            if (Background is { } background)
                await background.WaitForAttackFrame("card_snatch", CaveGodAnimTiming.CardSnatchTouch, CaveGodAnimTiming.CardSnatchTouch);
            else
                await Cmd.Wait(CaveGodAnimTiming.CardSnatchTouch);
            if (!CanAct || Body?.IsWeakPhase != false) return;
            await DamageCmd.Attack(SweepDamage).FromMonster(this)
                .SpawningHitVfxOnEachCreature()
                .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "blunt_attack.mp3")
                .Execute(null);

            float closeDelta = CaveGodAnimTiming.CardSnatchClose - CaveGodAnimTiming.CardSnatchTouch;
            if (Background is { } closingBackground)
                await closingBackground.WaitForAttackFrame("card_snatch", CaveGodAnimTiming.CardSnatchClose,
                    closeDelta, subtractPreviousDamageWait: true);
            else
                await Cmd.CustomScaledWait(Math.Max(0f, closeDelta - 0.1f), Math.Max(0f, closeDelta - 0.2f));

            if (CanAct && Body?.IsWeakPhase == false)
            {
                await StealCards(targets);
            }

            if (IsPairedFirstHit)
            {
                await Cmd.Wait(0.32f);
            }
            else if (Background is { } recoveringBackground)
            {
                await recoveringBackground.WaitForAttackFrame("card_snatch", CaveGodAnimTiming.CardSnatchLength,
                    CaveGodAnimTiming.CardSnatchLength - CaveGodAnimTiming.CardSnatchClose);
            }
            else
            {
                await Cmd.Wait(CaveGodAnimTiming.CardSnatchLength - CaveGodAnimTiming.CardSnatchClose);
            }
        }
        finally { Background?.ResetArmScale(); }
    }

    private async Task CleaveSweep(IReadOnlyList<Creature> targets)
    {
        if (!CanAct || Body?.IsWeakPhase != false) return;
        Background?.StartAttackAnim("front_sweep", isFlipped: !IsLeft);
        if (Background is { } background)
            await background.WaitForAttackFrame("front_sweep", CaveGodAnimTiming.FrontSweepHit, CaveGodAnimTiming.FrontSweepHit);
        else
            await Cmd.Wait(CaveGodAnimTiming.FrontSweepHit);

        if (!CanAct || Body?.IsWeakPhase != false) return;
        await DamageCmd.Attack(CleaveSweepDamage).FromMonster(this)
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "blunt_attack.mp3")
            .Execute(null);

        if (CanAct && Body?.IsWeakPhase == false)
        {
            IReadOnlyList<Creature> playerTargets = targets != null && targets.Count > 0
                ? targets.Where(c => c.IsAlive && c.Player != null).ToList()
                : CombatState?.PlayerCreatures.Where(c => c.IsAlive).ToList() ?? [];
            if (playerTargets.Count > 0)
            {
                await PowerCmd.Apply<FrailPower>(
                    new ThrowingPlayerChoiceContext(), playerTargets, 1m, Creature, null);
            }
        }

        float settle = IsPairedFirstHit ? 0.35f : (CaveGodAnimTiming.FrontSweepLength - CaveGodAnimTiming.FrontSweepHit);
        await Cmd.Wait(settle);
    }

    private async Task HeavyPunch(IReadOnlyList<Creature> _)
    {
        if (!CanAct || Body?.IsWeakPhase != false) return;
        string punchClip = IsLeft ? "leftpunch" : "rightpunch";
        Background?.StartAttackAnim("leftpunch", isFlipped: !IsLeft);
        if (Background is { } background)
            await background.WaitForAttackFrame(punchClip, CaveGodAnimTiming.HeavyPunchHit, CaveGodAnimTiming.HeavyPunchHit);
        else
            await Cmd.Wait(CaveGodAnimTiming.HeavyPunchHit);

        if (!CanAct || Body?.IsWeakPhase != false) return;
        NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Short);
        if (NCombatRoom.Instance != null && CombatState != null)
            VfxCmd.PlayOnCreatureCenters(CombatState.PlayerCreatures.Where(c => c.IsAlive).ToList(), "vfx/vfx_rock_shatter");
        await DamageCmd.Attack(HeavyPunchDamage).FromMonster(this)
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3")
            .Execute(null);

        float settle = IsPairedFirstHit ? 0.35f : (CaveGodAnimTiming.HeavyPunchLength - CaveGodAnimTiming.HeavyPunchHit);
        await Cmd.Wait(settle);
    }

    private async Task StealCards(IReadOnlyList<Creature>? targets)
    {
        IReadOnlyList<Creature> playerTargets = targets != null && targets.Count > 0
            ? targets.Where(c => c.IsAlive && c.Player != null).ToList()
            : CombatState?.PlayerCreatures.Where(c => c.IsAlive).ToList() ?? [];

        if (playerTargets.Count == 0) return;

        List<CardModel> stolenInThisSweep = [];
        foreach (Creature target in playerTargets)
        {
            if (target.Player == null) continue;
            CardModel? card = SelectCardToSteal(target.Player);
            if (card != null)
                stolenInThisSweep.Add(card);
        }
        if (stolenInThisSweep.Count == 0) return;

        Task removal = CardPileCmd.RemoveFromCombat(stolenInThisSweep);
        if (removal.IsFaulted) await removal;
        _stolenCards.AddRange(stolenInThisSweep);
        foreach (CardModel card in stolenInThisSweep) AttachStolenCardVisual(card);
        SfxCmd.Play(FmodSfx.cardMovementIntoDeck);
        await removal;
    }

    internal CardModel? SelectCardToSteal(Player player)
    {
        List<CardModel> list = CardPile.GetCards(player, PileType.Draw, PileType.Discard)
            .Where(c => c.DeckVersion != null)
            .ToList();

        if (list.Count == 0)
        {
            list = CardPile.GetCards(player, PileType.Hand)
                .Where(c => c.DeckVersion != null)
                .ToList();
        }

        if (list.Count == 0)
        {
            list = CardPile.GetCards(player, PileType.Draw, PileType.Discard, PileType.Hand)
                .ToList();
        }

        if (list.Count == 0) return null;

        IEnumerable<CardModel> candidates = list;
        foreach (Func<CardModel, bool> predicate in StealPriorities)
        {
            List<CardModel> matches = list.Where(predicate).ToList();
            if (matches.Count > 0)
            {
                candidates = matches;
                break;
            }
        }

        List<CardModel> candidateList = candidates.ToList();
        return (RunRng?.CombatCardGeneration != null)
            ? RunRng.CombatCardGeneration.NextItem(candidateList)
            : candidateList[0];
    }

    public async Task ReleaseStolenCards()
    {
        if (_stolenCards.Count == 0) return;
        List<CardModel> cardsToReturn = [.. _stolenCards];
        _stolenCards.Clear();
        ClearStolenCardVisual();

        foreach (CardModel card in cardsToReturn)
        {
            if (card.Owner?.Creature is { IsAlive: true })
            {
                card.HasBeenRemovedFromState = false;
                try
                {
                    await CardPileCmd.Add(card, PileType.Hand);
                }
                catch (Exception ex)
                {
                    Log.Warn($"[CaveGodHand] Failed to return stolen card {card.Id} to Hand ({ex.Message}), falling back to Discard pile.");
                    try
                    {
                        card.HasBeenRemovedFromState = false;
                        await CardPileCmd.Add(card, PileType.Discard);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[CaveGodHand] Failed to return stolen card {card.Id} to Discard pile ({ex2.Message}).");
                    }
                }
            }
            else
            {
                card.HasBeenRemovedFromState = false;
            }
        }
    }

    private const int MaxFallbackVisibleStolenCards = 8;
    private const float MaxFallbackStolenCardSpreadX = 96f;
    private const float MaxFallbackStolenCardSpreadY = 48f;

    private void AttachStolenCardVisual(CardModel card)
    {
        if (Background != null)
        {
            Background.AttachStolenCard(IsLeft, card);
        }
        else
        {
            NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(Creature);
            Marker2D? marker = creatureNode?.GetSpecialNode<Marker2D>("%StolenCardPos");
            if (marker != null && GodotObject.IsInstanceValid(marker))
            {
                List<NCard> existing = marker.GetChildren().OfType<NCard>()
                    .Where(c => GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion())
                    .ToList();
                NCard? evicted = null;
                if (existing.Count >= MaxFallbackVisibleStolenCards)
                {
                    bool isLocalCard = card.Owner != null && LocalContext.IsMe(card.Owner);
                    if (!isLocalCard)
                    {
                        marker.Visible = true;
                        return;
                    }

                    evicted = existing.FirstOrDefault(c => c.Model?.Owner == null || !LocalContext.IsMe(c.Model.Owner));
                    if (evicted == null)
                    {
                        marker.Visible = true;
                        return;
                    }

                    CardPreviewVisualFactory.ReturnPreviewSafely(evicted);
                }

                NCard? nCard = CardPreviewVisualFactory.TryAttachPreview(marker, card);
                if (nCard != null)
                {
                    NCard[] heldCards = marker.GetChildren().OfType<NCard>()
                        .Where(c => c != evicted && GodotObject.IsInstanceValid(c) && !c.IsQueuedForDeletion())
                        .ToArray();
                    if (heldCards.Length <= MultiplayerCompat.VanillaMaxPlayers)
                    {
                        int idx = heldCards.Length - 1;
                        nCard.Position = new Vector2(idx * 12f, -idx * 8f);
                    }
                    else
                    {
                        float denom = Math.Max(1, heldCards.Length - 1);
                        float stepX = MathF.Min(12f, MaxFallbackStolenCardSpreadX / denom);
                        float stepY = MathF.Min(8f, MaxFallbackStolenCardSpreadY / denom);
                        for (int i = 0; i < heldCards.Length; i++)
                        {
                            heldCards[i].Position = new Vector2(i * stepX, -i * stepY);
                        }
                    }
                }
                marker.Visible = true;
            }
        }
    }

    private void ClearStolenCardVisual()
    {
        if (Background != null)
        {
            Background.ClearStolenCard(IsLeft);
        }
        else
        {
            NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(Creature);
            Marker2D? marker = creatureNode?.GetSpecialNode<Marker2D>("%StolenCardPos");
            if (marker != null && GodotObject.IsInstanceValid(marker))
            {
                foreach (Node child in marker.GetChildren())
                {
                    child.QueueFreeSafely();
                }
                marker.Visible = false;
            }
        }
    }

    private async Task WeakAttack(IReadOnlyList<Creature> _)
    {
        if (!CanAct || Body?.IsWeakPhase != true) return;
        Background?.StartWeakAttackAnim(isLeftArmBroken: !IsLeft);
        if (Background is { } background)
            await background.WaitForAttackFrame("weak_attack", 1.20f, 1.20f);
        else await Cmd.Wait(1.20f);
        if (!CanAct || Body?.IsWeakPhase != true) return;
        await DamageCmd.Attack(WeakDamage).FromMonster(this)
            .SpawningHitVfxOnEachCreature()
            .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3")
            .Execute(null);
        await Cmd.Wait(1.40f);
    }

    private async Task Grab(IReadOnlyList<Creature> targets)
    {
        if (!CanAct || Body is not { IsWeakPhase: false } body) return;
        bool completed = false;
        try
        {
            Background?.StartAttackAnim("grab_player", isFlipped: !IsLeft);
            if (Background is { } background)
                await background.WaitForAttackFrame("grab_player", 1.10f, 1.10f);
            else await Cmd.Wait(1.10f);
            await DamageCmd.Attack(GrabDamage).FromMonster(this)
                .SpawningHitVfxOnEachCreature()
                .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "blunt_attack.mp3")
                .Execute(null);
            // Thorns/retaliation can break this arm or lock Body enrage during the grab's initial hit.
            if (!CanAct || body.IsWeakPhase) return;
            Creature[] captives = targets.Where(c => c.IsAlive && c.Player != null).ToArray();
            if (captives.Length == 0) return;
            await Sts2VersionSeam.DislodgeImpaledArrowsAsync(Creature);
            IsGrabbing = true;
            NCombatRoom.Instance?.GetCreatureNode(Creature)?.ToggleIsInteractable(false);
            await PowerCmd.Apply<VulnerablePower>(new ThrowingPlayerChoiceContext(), captives, 1m, Creature, null);
            // Native Cmd waits determine gameplay timing equally on rendered/headless peers.
            Background?.StartGrabTracking(captives, isFlipped: !IsLeft);
            await Cmd.Wait(0.75f);
            Background?.HoldGrabAnim();
            var claw = (ThingsCaveGodCaptiveClaw)ModelDb.Monster<ThingsCaveGodCaptiveClaw>().ToMutable();
            claw.IsPhase2 = body.Phase >= 2;
            claw.IsLeftSide = IsLeft;
            string? validSlot = CombatState!.Encounter?.Slots.Contains(ThingsCaveGodBossEncounter.CaptiveHandSlot) == true
                ? ThingsCaveGodBossEncounter.CaptiveHandSlot
                : null;
            await CreatureCmd.Add(claw, CombatState!, CombatSide.Enemy, validSlot);
            if (!CanAct || !IsGrabbing || body.IsWeakPhase) return;
            if (body.Phase >= 2)
            {
                await PowerCmd.Apply<ThingsCaveGodPendingTrialPower>(
                    new ThrowingPlayerChoiceContext(),
                    captives.Where(c => c.IsAlive),
                    1m,
                    Creature,
                    null);
            }
            completed = true;
        }
        finally
        {
            if (!completed) await body.ReleaseCaptives(stun: false);
        }
    }

    internal async Task ChooseTrialAfterDraw(PlayerChoiceContext choiceContext, Creature target)
    {
        if (target.IsDead || target.Player == null || CombatState == null || !IsGrabbing ||
            Body is not { Phase: >= 2, IsWeakPhase: false, IsEnrageLocked: false, IsDefeated: false })
        {
            return;
        }

        // Remote choices use the native context but must not acquire or restore
        // the local overlay's Z order while the local player is still choosing.
        NOverlayStack? overlay = LocalContext.IsMe(target.Player) ? NOverlayStack.Instance : null;
        int originalZ = overlay?.ZIndex ?? 0;
        bool originalRelative = overlay?.ZAsRelative ?? true;
        try
        {
            if (overlay != null)
            {
                overlay.ZAsRelative = false;
                overlay.ZIndex = 100;
            }

            List<CardModel> choices =
            [
                CombatState.CreateCard(ModelDb.Card<ThingsCaveGodBrokenBladeTrial>(), target.Player),
                CombatState.CreateCard(ModelDb.Card<ThingsCaveGodShatteredShieldTrial>(), target.Player)
            ];
            CardModel? chosen = await CardSelectCmd.FromChooseACardScreen(choiceContext, choices, target.Player, canSkip: false);
            if (IsGrabbing &&
                Body is { Phase: >= 2, IsWeakPhase: false, IsEnrageLocked: false, IsDefeated: false } &&
                target.IsAlive &&
                chosen is KnowledgeDemon.IChoosable trial)
            {
                await trial.OnChosen();
            }
        }
        finally
        {
            if (overlay != null && GodotObject.IsInstanceValid(overlay))
            {
                overlay.ZAsRelative = originalRelative;
                overlay.ZIndex = originalZ;
            }
        }
    }

    private async Task Slam(IReadOnlyList<Creature> _)
    {
        if (!CanAct || !IsGrabbing || Body is not { IsWeakPhase: false } body) return;
        bool impacted = false;
        try
        {
            Background?.ResumeSlamAnim();
            if (Background is { } background)
                await background.WaitForAttackFrame("grab_slam", 0.75f, 0.75f);
            else await Cmd.Wait(0.75f);
            if (!CanAct || !IsGrabbing || body.IsWeakPhase) return;
            NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal);
            await DamageCmd.Attack(SlamDamage).FromMonster(this)
                .SpawningHitVfxOnEachCreature()
                .WithHitFx("vfx/vfx_attack_blunt", tmpSfx: "heavy_attack.mp3")
                .Execute(null);
            impacted = !body.IsWeakPhase && !body.IsDefeated;
        }
        finally { await body.ReleaseCaptives(stun: false, slam: impacted); }
    }
}
