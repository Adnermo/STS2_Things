using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Localization;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared floor-window quest lifecycle for temporary wax parcel relics using RunState.TotalFloor
/// so internal event sub-rooms and save/reload restoration cannot double-tick the countdown.
/// </summary>
public abstract class ThingsTimedWaxParcelRelic : ThingsRelicModel
{
    protected const int QuestPending = 0;
    protected const int QuestSucceeded = 1;
    protected const int QuestExpired = 2;

    protected int RawStartFloor = -1;
    protected int RawLastObservedFloor = -1;
    protected int RawCombatsWon;
    protected int RawOutcome = QuestPending;
    protected bool RawCurrentFloorResolved;

    protected abstract int RoomWindow { get; }
    protected abstract int RequiredCombats { get; }

    public int RoomsElapsed => RawStartFloor < 0 ? 0 : Math.Max(0, RawLastObservedFloor - RawStartFloor);
    public int RoomsRemaining => RawOutcome != QuestPending
        ? 0
        : RoomsElapsed == 0
            ? RoomWindow
            : Math.Max(0, RoomWindow - RoomsElapsed + (RawCurrentFloorResolved ? 0 : 1));

    public override RelicRarity Rarity => RelicRarity.Event;
    public override bool ShowCounter => true;
    public override int DisplayAmount => RoomsRemaining;
    public override bool IsUsedUp => RawOutcome != QuestPending;

    internal void InitializeQuest(int startFloor)
    {
        AssertMutable();
        RawStartFloor = Math.Max(0, startFloor);
        RawLastObservedFloor = RawStartFloor;
        RawCombatsWon = 0;
        RawOutcome = QuestPending;
        RawCurrentFloorResolved = true;
        RefreshQuestDisplay();
    }

    protected void SetStartFloor(int value)
    {
        AssertMutable();
        RawStartFloor = value;
        RefreshQuestDisplay();
    }

    protected void SetLastObservedFloor(int value)
    {
        AssertMutable();
        RawLastObservedFloor = value;
        RefreshQuestDisplay();
    }

    protected void SetCombatsWon(int value)
    {
        AssertMutable();
        RawCombatsWon = Math.Max(0, value);
        RefreshQuestDisplay();
    }

    protected void SetOutcome(int value)
    {
        AssertMutable();
        RawOutcome = value;
        RefreshQuestDisplay();
    }

    protected void SetCurrentFloorResolved(bool value)
    {
        AssertMutable();
        RawCurrentFloorResolved = value;
        RefreshQuestDisplay();
    }

    protected static LocalizedStringVar CreateStatusVar(string entry, int initialCombats, int initialRooms) =>
        new("Status", owner => owner is ThingsTimedWaxParcelRelic parcel
            ? parcel.FormatQuestStatus()
            : FormatQuestStatusText(entry, QuestPending, initialCombats, initialRooms));

    protected virtual string FormatQuestStatus() =>
        FormatQuestStatusText(Id.Entry, RawOutcome, Math.Max(0, RequiredCombats - RawCombatsWon), RoomsRemaining);

    private static string FormatQuestStatusText(string entry, int outcome, int combats, int rooms)
    {
        if (outcome == QuestSucceeded)
            return new LocString("relics", $"{entry}.statusSucceeded").GetFormattedText();
        if (outcome == QuestExpired)
            return new LocString("relics", $"{entry}.statusExpired").GetFormattedText();
        var pending = new LocString("relics", $"{entry}.statusPending");
        pending.Add("Combats", combats);
        pending.Add("Rooms", rooms);
        return pending.GetFormattedText();
    }

    protected void RefreshQuestDisplay()
    {
        if (DynamicVars.ContainsKey("Combats"))
            DynamicVars["Combats"].BaseValue = Math.Max(0, RequiredCombats - RawCombatsWon);
        if (DynamicVars.ContainsKey("Rooms"))
            DynamicVars["Rooms"].BaseValue = RoomsRemaining;
        if (DynamicVars.ContainsKey("Status") && DynamicVars["Status"] is StringVar statusVar)
            statusVar.StringValue = FormatQuestStatus();
        Status = IsUsedUp
            ? RelicStatus.Disabled
            : RoomsRemaining <= 1 ? RelicStatus.Active : RelicStatus.Normal;
        InvokeDisplayAmountChanged();
    }

    protected virtual bool QualifiesForProgress(CombatRoom room) => true;

    protected abstract Task OnQuestSucceededAsync();

    protected abstract Task OnQuestExpiredAsync();

    public override Task AfterObtained()
    {
        if (RawStartFloor < 0 && Owner != null)
            InitializeQuest(Owner.RunState.TotalFloor);
        return Task.CompletedTask;
    }

    public override async Task BeforeRoomEntered(AbstractRoom room)
    {
        if (Owner == null || Owner.Creature.IsDead || RawOutcome != QuestPending)
            return;
        if (RawStartFloor < 0)
            InitializeQuest(Owner.RunState.TotalFloor);

        int currentFloor = Owner.RunState.TotalFloor;
        if (currentFloor > RawLastObservedFloor)
        {
            RawCurrentFloorResolved = false;
            SetLastObservedFloor(currentFloor);
        }

        if (RoomsElapsed > RoomWindow)
        {
            SetOutcome(QuestExpired);
            Flash();
            await OnQuestExpiredAsync();
        }
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (Owner == null || Owner.Creature.IsDead || RawOutcome != QuestPending || RoomsElapsed <= 0)
            return Task.CompletedTask;

        if (room is EventRoom)
        {
            int observedFloor = Owner.RunState.TotalFloor;
            EventModel? playerEvent = RunManager.Instance?.EventSynchronizer?.Events
                .FirstOrDefault(e => e.Owner == Owner);
            if (playerEvent != null)
            {
                if (playerEvent.IsFinished)
                {
                    SetCurrentFloorResolved(true);
                }
                else
                {
                    void OnStateChanged(EventModel model)
                    {
                        if (!model.IsFinished)
                            return;
                        model.StateChanged -= OnStateChanged;
                        if (IsMutable && Owner != null && !Owner.Creature.IsDead &&
                            RawOutcome == QuestPending && Owner.RunState.TotalFloor == observedFloor)
                        {
                            SetCurrentFloorResolved(true);
                        }
                    }

                    playerEvent.StateChanged += OnStateChanged;
                }
            }

            return Task.CompletedTask;
        }

        if (room is not CombatRoom)
            SetCurrentFloorResolved(true);

        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        if (Owner != null && !Owner.Creature.IsDead && RawOutcome == QuestPending && RoomsElapsed > 0)
            SetCurrentFloorResolved(true);
        return Task.CompletedTask;
    }

    public override async Task AfterCombatVictory(CombatRoom room)
    {
        if (Owner == null || Owner.Creature.IsDead || RawOutcome != QuestPending)
            return;
        if (RoomsElapsed > RoomWindow || !QualifiesForProgress(room))
            return;

        SetCombatsWon(RawCombatsWon + 1);
        Flash();

        if (RawCombatsWon >= RequiredCombats)
        {
            SetOutcome(QuestSucceeded);
            await OnQuestSucceededAsync();
        }
    }
}
