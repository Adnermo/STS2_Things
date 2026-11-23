using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;

namespace STS2_Things.Events;

/// <summary>Shared menu choice, individual meals and rewards, synchronized curse combat.</summary>
public sealed class ThingsPoliteMaw : ThingsEventModel
{
    public const int AttackBiteDamage = 8;
    public const int SkillNipDamage = 6;
    public const int SkillGold = 45;
    public const int PowerMaxHpLoss = 5;

    private const int DescriptionMinimumHeight = 72;
    private RelicModel? _offeredRelic;
    private bool _combatRequested;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _offeredRelic = null;
        _combatRequested = false;
    }

    public override bool IsShared => true;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new GoldVar(SkillGold),
         new DamageVar("AttackDamage", AttackBiteDamage, ValueProp.Unblockable | ValueProp.Unpowered),
         new DamageVar("SkillDamage", SkillNipDamage, ValueProp.Unblockable | ValueProp.Unpowered),
         new MaxHpVar("PowerMaxHp", PowerMaxHpLoss),
         new LocalizedStringVar("Relic", static owner =>
             owner is ThingsPoliteMaw { _offeredRelic: { } r }
                 ? r.Title.GetFormattedText()
                 : new LocString("events", "THINGS_POLITE_MAW.uncommonRelic").GetFormattedText())];

    public override void OnRoomEnter()
    {
        base.OnRoomEnter();
        // Five native buttons plus the multiplayer label need less empty space
        // below this short description. Preserve native fonts and button sizes.
        if (Node?.GetNodeOrNull<Control>("%EventDescription") is { } description)
        {
            description.CustomMinimumSize = new Vector2(description.CustomMinimumSize.X, DescriptionMinimumHeight);
            if (description.GetParent() is Control box)
                box.OffsetTop = Mathf.Min(box.OffsetTop, 168f);
        }
    }

    private static bool HasFood(Player player, CardType type) => !player.Creature.IsDead &&
        player.Deck.Cards.Count > 1 && player.Deck.Cards.Any(card => card.IsRemovable && card.Type == type);

    private bool CanEveryoneFeed(CardType type)
    {
        var diners = EventOwner.RunState.Players.Where(player => !player.Creature.IsDead).ToArray();
        return diners.Length > 0 && diners.All(player => HasFood(player, type));
    }

    private bool CanFeedCurse => EventOwner.RunState.Players.Any(player => HasFood(player, CardType.Curse));

    public override bool IsAllowed(IRunState runState)
    {
        return runState.Players.Count > 0 && runState.Players.All(p => !p.Creature.IsDead && p.Creature.CurrentHp > AttackBiteDamage);
    }

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        // Reserve once, like native Wongo's featured relic. Revisiting the menu
        // never rolls another relic or consumes another grab-bag entry.
        if (!isPreFinished && _offeredRelic == null && CanEveryoneFeed(CardType.Power))
        {
            _offeredRelic = RelicFactory.PullNextRelicFromFront(EventOwner, RelicRarity.Uncommon).ToMutable();
            _offeredRelic.Owner = EventOwner;
            ((StringVar)DynamicVars["Relic"]).StringValue = _offeredRelic.Title.GetFormattedText();
        }
        return Task.CompletedTask;
    }

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "ATTACK", "SKILL", "POWER", "CURSE", "LEAVE");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool attack = CanEveryoneFeed(CardType.Attack), skill = CanEveryoneFeed(CardType.Skill);
        bool power = CanEveryoneFeed(CardType.Power) && _offeredRelic != null && EventOwner.Creature.MaxHp > PowerMaxHpLoss;
        EventOption attackOption = new(this, attack ? FeedAttack : null,
            InitialOptionKey(attack ? "ATTACK" : "ATTACK_LOCKED"),
            [HoverTipFactory.FromPotion<AttackPotion>()]);
        if (attack) attackOption.ThatDoesDamage(AttackBiteDamage);

        EventOption skillOption = new(this, skill ? FeedSkill : null,
            InitialOptionKey(skill ? "SKILL" : "SKILL_LOCKED"));
        if (skill) skillOption.ThatDoesDamage(SkillNipDamage);

        EventOption powerOption = new(this, power ? FeedPower : null,
            InitialOptionKey(power ? "POWER" : "POWER_LOCKED"),
            _offeredRelic?.HoverTips ?? Array.Empty<IHoverTip>());
        if (power) powerOption.ThatDecreasesMaxHp(PowerMaxHpLoss);

        return
        [
            attackOption,
            skillOption,
            powerOption,
            new EventOption(this, CanFeedCurse ? FeedCurse : null, InitialOptionKey(CanFeedCurse ? "CURSE" : "CURSE_LOCKED")),
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private async Task<bool> TakeFood(CardType type)
    {
        if (!HasFood(EventOwner, type)) return false;
        var prompt = L10NLookup($"THINGS_POLITE_MAW.selection.{type.ToString().ToUpperInvariant()}");
        DynamicVars.AddTo(prompt);
        var card = (await CardSelectCmd.FromDeckForRemoval(EventOwner,
            new CardSelectorPrefs(prompt, 1),
            card => card.Type == type)).FirstOrDefault();
        if (card == null || EventOwner.Creature.IsDead || EventOwner.Deck.Cards.Count <= 1 ||
            !EventOwner.Deck.Cards.Contains(card) || !card.IsRemovable || card.Type != type) return false;
        await CardPileCmd.RemoveFromDeck(card);
        return true;
    }

    private async Task FeedAttack()
    {
        if (!await TakeFood(CardType.Attack)) { Finish("ABORTED"); return; }
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, AttackBiteDamage);
        if (EventOwner.Creature.IsDead) { Finish("ATTACK_LOSS"); return; }
        Finish("ATTACK");
        // Native reward screen lets the player discard an old potion if full.
        await RewardsCmd.OfferCustom(EventOwner,
            [new PotionReward(ModelDb.Potion<AttackPotion>().ToMutable(), EventOwner)]);
    }

    private async Task FeedSkill()
    {
        if (!await TakeFood(CardType.Skill)) { Finish("ABORTED"); return; }
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, SkillNipDamage);
        if (EventOwner.Creature.IsDead) { Finish("SKILL_LOSS"); return; }
        await PlayerCmd.GainGold(DynamicVars.Gold.BaseValue, EventOwner);
        Finish("SKILL");
    }

    private async Task FeedPower()
    {
        if (_offeredRelic == null || !await TakeFood(CardType.Power)) { Finish("ABORTED"); return; }
        await LoseEventOwnerMaxHpAsync(new ThrowingPlayerChoiceContext(), PowerMaxHpLoss);
        if (EventOwner.Creature.IsDead) { Finish("POWER_LOSS"); return; }
        await RelicCmd.Obtain(_offeredRelic, EventOwner);
        Finish("POWER");
    }

    private async Task FeedCurse()
    {
        if (_combatRequested) return;
        _combatRequested = true;
        await TakeFood(CardType.Curse);
        // Even players without a curse (and dead players) must report ready.
        // The native synchronizer waits for every event instance before entering.
        EnterCombatWithoutExitingEvent<ThingsCaveMawWeak>(Array.Empty<Reward>(), shouldResumeAfterCombat: false);
    }

    private Task Leave()
    {
        Finish("LEAVE");
        return Task.CompletedTask;
    }
    private void Finish(string page)
    {
        if (!IsFinished) SetEventFinished(L10NLookup($"THINGS_POLITE_MAW.pages.{page}.description"));
    }
}
