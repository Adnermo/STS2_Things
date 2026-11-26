using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsHotWaxBox : ThingsEventModel
{
    public const int ChilledGoldCost = 30;
    public const int PryDamage = 8;
    public const int PryGold = 40;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("UrgentRooms", ThingsHotWaxParcel.WindowRooms),
        new IntVar("UrgentCombats", ThingsHotWaxParcel.CombatsNeeded),
        new IntVar("UrgentGold", ThingsHotWaxParcel.GoldReward),
        new IntVar("CrackDamage", ThingsHotWaxParcel.CrackDamage),
        new GoldVar("ChilledCost", ChilledGoldCost),
        new IntVar("ChilledRooms", ThingsChilledWaxParcel.WindowRooms),
        new IntVar("ChilledGold", ThingsChilledWaxParcel.GoldReward),
        new IntVar("ChilledUpgrades", ThingsChilledWaxParcel.UpgradeCount),
        new DamageVar("PryDamage", PryDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar("PryGold", PryGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        FindMaxNonBossRoomsBeforeBoss(runState) >= ThingsHotWaxParcel.CombatsNeeded
        && runState.Players.Any(player =>
            !player.Creature.IsDead
            && player.GetRelic<ThingsHotWaxParcel>() == null
            && player.GetRelic<ThingsChilledWaxParcel>() == null);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "ACCEPT_URGENT", "RETAG_CHILLED", "PRY_OPEN", "LEAVE");

    /// <summary>
    /// Returns the longest forward non-Boss room chain reachable from the player's current map coordinate,
    /// so express wax parcels lock before the Act Boss when too few rooms remain.
    /// </summary>
    public static int FindMaxNonBossRoomsBeforeBoss(IRunState runState)
    {
        ActMap? map = runState.Map;
        if (map == null)
            return int.MaxValue;

        MapPoint? current = runState.CurrentMapCoord is { } coord ? map.GetPoint(coord) : null;
        if (current?.PointType == MapPointType.Boss)
            return 0;

        IEnumerable<MapPoint> nextPoints = current != null
            ? current.Children.Where(child => !child.IsInTheSameRow(current))
            : map.startMapPoints;
        Dictionary<MapCoord, int> memo = [];
        HashSet<MapCoord> activePath = [];
        if (current != null)
            activePath.Add(current.coord);

        (int Depth, bool SawCycle) LongestFrom(MapPoint point)
        {
            if (point.PointType == MapPointType.Boss)
                return (0, false);
            if (memo.TryGetValue(point.coord, out int cached))
                return (cached, false);
            if (!activePath.Add(point.coord))
                return (0, true);

            try
            {
                int bestChild = 0;
                bool sawCycle = false;
                foreach (MapPoint child in point.Children)
                {
                    if (child.IsInTheSameRow(point))
                        continue;
                    var (depth, childCycle) = LongestFrom(child);
                    sawCycle |= childCycle;
                    if (depth > bestChild)
                        bestChild = depth;
                }

                int result = 1 + bestChild;
                if (!sawCycle)
                    memo[point.coord] = result;
                return (result, sawCycle);
            }
            finally
            {
                activePath.Remove(point.coord);
            }
        }

        int maxRooms = 0;
        foreach (MapPoint next in nextPoints)
        {
            var (depth, _) = LongestFrom(next);
            if (depth > maxRooms)
                maxRooms = depth;
        }
        return maxRooms;
    }

    public static bool HasReachableEliteWithinWindow(IRunState runState, int maxRooms)
    {
        if (maxRooms <= 0)
            return false;

        ActMap? map = runState.Map;
        if (map == null)
            return true;

        MapPoint? current = runState.CurrentMapCoord is { } coord ? map.GetPoint(coord) : null;
        if (current?.PointType == MapPointType.Boss)
            return false;

        // Synthetic chain maps used in layout/depth unit probes contain only Unknown + Boss nodes.
        bool hasAssignedRoomTypes = map.GetAllMapPoints().Any(static p =>
            p.PointType is MapPointType.Monster
                or MapPointType.Elite
                or MapPointType.RestSite
                or MapPointType.Shop
                or MapPointType.Treasure);
        if (!hasAssignedRoomTypes)
            return FindMaxNonBossRoomsBeforeBoss(runState) >= 1;

        IEnumerable<MapPoint> initial = current != null
            ? current.Children.Where(child => !child.IsInTheSameRow(current))
            : map.startMapPoints;

        Queue<(MapPoint Point, int Depth)> queue = new();
        HashSet<MapCoord> visited = [];
        if (current != null)
            visited.Add(current.coord);

        foreach (MapPoint next in initial)
        {
            if (next.PointType == MapPointType.Boss)
                continue;
            if (visited.Add(next.coord))
                queue.Enqueue((next, 1));
        }

        while (queue.Count > 0)
        {
            var (point, depth) = queue.Dequeue();
            if (point.PointType == MapPointType.Elite)
                return true;
            if (depth >= maxRooms)
                continue;

            foreach (MapPoint child in point.Children)
            {
                if (child.PointType == MapPointType.Boss || child.IsInTheSameRow(point))
                    continue;
                if (visited.Add(child.coord))
                    queue.Enqueue((child, depth + 1));
            }
        }

        return false;
    }

    private bool OwnsAnyParcel() =>
        EventOwner.GetRelic<ThingsHotWaxParcel>() != null
        || EventOwner.GetRelic<ThingsChilledWaxParcel>() != null;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool ownsParcel = OwnsAnyParcel();
        int roomsBeforeBoss = FindMaxNonBossRoomsBeforeBoss(EventOwner.RunState);
        bool hasReachableElite = HasReachableEliteWithinWindow(EventOwner.RunState, ThingsChilledWaxParcel.WindowRooms);

        EventOption urgentOption = ownsParcel
            ? new EventOption(this, null, InitialOptionKey("URGENT_OWNED"),
                HoverTipFactory.FromRelic<ThingsHotWaxParcel>())
            : roomsBeforeBoss < 2
                ? new EventOption(this, null, InitialOptionKey("URGENT_BOSS_NEAR"),
                    HoverTipFactory.FromRelic<ThingsHotWaxParcel>())
                : new EventOption(this, AcceptUrgent, InitialOptionKey("ACCEPT_URGENT"),
                    HoverTipFactory.FromRelic<ThingsHotWaxParcel>());

        EventOption chilledOption = ownsParcel
            ? new EventOption(this, null, InitialOptionKey("CHILLED_OWNED"),
                HoverTipFactory.FromRelic<ThingsChilledWaxParcel>())
            : roomsBeforeBoss < 1
                ? new EventOption(this, null, InitialOptionKey("CHILLED_BOSS_NEAR"),
                    HoverTipFactory.FromRelic<ThingsChilledWaxParcel>())
                : !hasReachableElite
                    ? new EventOption(this, null, InitialOptionKey("CHILLED_NO_ELITE"),
                        HoverTipFactory.FromRelic<ThingsChilledWaxParcel>())
                    : EventOwner.Gold < ChilledGoldCost
                        ? new EventOption(this, null, InitialOptionKey("CHILLED_NO_GOLD"),
                            HoverTipFactory.FromRelic<ThingsChilledWaxParcel>())
                        : new EventOption(this, RetagChilled, InitialOptionKey("RETAG_CHILLED"),
                            HoverTipFactory.FromRelic<ThingsChilledWaxParcel>());

        EventOption pryOption = new EventOption(this, PryOpen, InitialOptionKey("PRY_OPEN"))
            .ThatDoesDamage(PryDamage);

        EventOption leaveOption = new(this, Leave, InitialOptionKey("LEAVE"));

        return [urgentOption, chilledOption, pryOption, leaveOption];
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task AcceptUrgent()
    {
        if (!BeginSettlement())
            return;
        if (OwnsAnyParcel())
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }
        var parcel = (ThingsHotWaxParcel)ModelDb.Relic<ThingsHotWaxParcel>().ToMutable();
        parcel.InitializeQuest(EventOwner.RunState.TotalFloor);
        await RelicCmd.Obtain(parcel, EventOwner);
        Finish("ACCEPT_URGENT.description");
    }

    private async Task RetagChilled()
    {
        if (!BeginSettlement())
            return;
        if (OwnsAnyParcel() ||
            EventOwner.Gold < ChilledGoldCost ||
            !HasReachableEliteWithinWindow(EventOwner.RunState, ThingsChilledWaxParcel.WindowRooms))
        {
            _settling = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await PlayerCmd.LoseGold(ChilledGoldCost, EventOwner, GoldLossType.Spent);
        var parcel = (ThingsChilledWaxParcel)ModelDb.Relic<ThingsChilledWaxParcel>().ToMutable();
        parcel.InitializeQuest(EventOwner.RunState.TotalFloor);
        await RelicCmd.Obtain(parcel, EventOwner);
        Finish("RETAG_CHILLED.description");
    }

    private async Task PryOpen()
    {
        if (!BeginSettlement())
            return;
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            EventOwner.Creature,
            PryDamage,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        if (!EventOwner.Creature.IsDead)
        {
            await PlayerCmd.GainGold(PryGold, EventOwner);
            await RewardsCmd.OfferCustom(EventOwner, [new PotionReward(EventOwner)]);
        }

        Finish(EventOwner.Creature.IsDead ? "PRY_OPEN.loss" : "PRY_OPEN.description");
    }

    private Task Leave()
    {
        if (!BeginSettlement())
            return Task.CompletedTask;
        Finish("LEAVE.description");
        return Task.CompletedTask;
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"THINGS_HOT_WAX_BOX.pages.{pageSuffix}"));
    }
}
