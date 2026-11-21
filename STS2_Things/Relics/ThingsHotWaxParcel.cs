using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsHotWaxParcel : ThingsTimedWaxParcelRelic
{
    public const int WindowRooms = 3;
    public const int CombatsNeeded = 2;
    public const int GoldReward = 80;
    public const int CrackDamage = 14;

    protected override int RoomWindow => WindowRooms;
    protected override int RequiredCombats => CombatsNeeded;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Combats", CombatsNeeded),
        new IntVar("Rooms", WindowRooms),
        new GoldVar(GoldReward),
        new DamageVar("Damage", CrackDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new CardsVar(1),
        CreateStatusVar("THINGS_HOT_WAX_PARCEL", CombatsNeeded, WindowRooms),
    ];

    [SavedProperty]
    public int StartFloor
    {
        get => RawStartFloor;
        private set => SetStartFloor(value);
    }

    [SavedProperty]
    public int LastObservedFloor
    {
        get => RawLastObservedFloor;
        private set => SetLastObservedFloor(value);
    }

    [SavedProperty]
    public int CombatsWon
    {
        get => RawCombatsWon;
        private set => SetCombatsWon(value);
    }

    [SavedProperty]
    public int Outcome
    {
        get => RawOutcome;
        private set => SetOutcome(value);
    }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public bool CurrentFloorResolved
    {
        get => RawCurrentFloorResolved;
        private set => SetCurrentFloorResolved(value);
    }

    protected override async Task OnQuestSucceededAsync()
    {
        await PlayerCmd.GainGold(GoldReward, Owner);
        Owner.PopulateRelicGrabBagIfNecessary(Owner.RunState.Rng.UpFront);
        RelicModel rareRelic = RelicFactory.PullNextRelicFromFront(
            Owner,
            RelicRarity.Rare,
            candidate => Owner.Relics.All(owned => owned.Id != candidate.Id));
        await RelicCmd.Obtain(rareRelic.ToMutable(), Owner);
    }

    protected override async Task OnQuestExpiredAsync()
    {
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            Owner.Creature,
            CrackDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (Owner.Creature.IsDead)
            return;

        List<CardModel> curses = ModelDb.CardPool<CurseCardPool>()
            .GetUnlockedCards(Owner.UnlockState, Owner.RunState.CardMultiplayerConstraint)
            .Where(card => card.CanBeGeneratedByModifiers && IsCardAllowedInRun(card, Owner.RunState))
            .OrderBy(card => card.Id)
            .ToList();
        if (curses.Count == 0)
            return;

        CardModel curse = curses[Owner.PlayerRng.Rewards.NextInt(curses.Count)];
        await CardPileCmd.AddCursesToDeck([curse], Owner);
    }
}
