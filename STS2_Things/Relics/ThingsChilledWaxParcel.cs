using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Core.Models;

namespace STS2_Things.Relics;

public sealed class ThingsChilledWaxParcel : ThingsTimedWaxParcelRelic
{
    public const int WindowRooms = 4;
    public const int CombatsNeeded = 1;
    public const int GoldReward = 75;
    public const int UpgradeCount = 2;

    protected override int RoomWindow => WindowRooms;
    protected override int RequiredCombats => CombatsNeeded;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("Combats", CombatsNeeded),
        new IntVar("Rooms", WindowRooms),
        new GoldVar(GoldReward),
        new CardsVar(UpgradeCount),
        CreateStatusVar("THINGS_CHILLED_WAX_PARCEL", CombatsNeeded, WindowRooms),
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

    protected override bool QualifiesForProgress(CombatRoom room) => room.RoomType == RoomType.Elite;

    protected override async Task OnQuestSucceededAsync()
    {
        List<CardModel> upgradable = PileType.Deck.GetPile(Owner)
            .Cards
            .Where(card => card.IsUpgradable)
            .ToList();
        if (upgradable.Count > 0)
        {
            List<CardModel> chosen = upgradable
                .StableShuffle(Owner.PlayerRng.Rewards)
                .Take(UpgradeCount)
                .ToList();
            CardCmd.Upgrade(chosen, CardPreviewStyle.HorizontalLayout);
        }

        await PlayerCmd.GainGold(GoldReward, Owner);
    }

    protected override Task OnQuestExpiredAsync() => Task.CompletedTask;
}
