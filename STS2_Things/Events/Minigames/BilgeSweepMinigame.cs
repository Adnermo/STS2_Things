using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;

namespace STS2_Things.Events.Minigames;

public enum BilgeLootKind : byte
{
    None = 0,
    GoldSmall = 1,
    GoldMedium = 2,
    Potion = 3,
    Card = 4,
    CommonRelic = 5,
}

public sealed record ThingsBilgeSweepOutcome(
    bool HitMine,
    int GoldAmount,
    int PotionCount,
    int CardCount,
    int CommonRelicCount,
    int ProbesUsed,
    bool CashedOut)
{
    public bool HasAnyLoot => GoldAmount > 0 || PotionCount > 0 || CardCount > 0 || CommonRelicCount > 0;
}

public sealed class BilgeCell
{
    public int X { get; }
    public int Y { get; }
    public bool IsMine { get; internal set; }
    public bool IsDecoyGlint { get; internal set; }
    public BilgeLootKind LootKind { get; internal set; }
    public int GoldValue { get; internal set; }
    public int AdjacentMines { get; internal set; }
    public bool IsRevealed { get; internal set; }
    public bool IsFlagged { get; internal set; }
    public bool IsPreRevealed { get; internal set; }
    public bool WasTriggeredMine { get; internal set; }
    public bool LootCollected { get; internal set; }

    public bool HasGlint => LootKind != BilgeLootKind.None || IsDecoyGlint;

    internal BilgeCell(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Pure deterministic state controller for the Underdocks (Act 1) 6x6 Bilge Grate minesweeper minigame.
/// Five Act 1 loot caches and two deceptive oily toxin sacs emit glints through the grate bars;
/// Minesweeper neighbor counts let players deduce which glints are real treasure vs. bursting sacs.
/// </summary>
public sealed class BilgeSweepMinigame
{
    public const int GridWidth = 6;
    public const int GridHeight = 6;
    public const int MineCount = 6;
    public const int DecoyGlintMineCount = 2;
    public const int TotalLootCount = 5;
    public const int FreeProbes = 5;
    public const int PaidProbes = 7;
    public const int PaidProbeGoldCost = 25;
    public const int MineBurstDamage = 8;
    public const int EdgeSkimGold = 20;
    public const int RelicFallbackGold = 40;
    public const int GoldSmallMin = 20;
    public const int GoldSmallMax = 26;
    public const int GoldMediumMin = 26;
    public const int GoldMediumMax = 34;

    private readonly BilgeCell[,] _cells = new BilgeCell[GridWidth, GridHeight];
    private readonly TaskCompletionSource<ThingsBilgeSweepOutcome> _completion = new();
    private bool _finishedNotified;

    public Player Owner { get; }
    public int MaxProbes { get; }
    public int ProbesRemaining { get; private set; }
    public int ProbesUsed => MaxProbes - ProbesRemaining;
    public bool IsComplete { get; private set; }
    public bool HitMine { get; private set; }
    public bool CashedOut { get; private set; }
    public int CollectedGold { get; private set; }
    public int CollectedPotions { get; private set; }
    public int CollectedCards { get; private set; }
    public int CollectedCommonRelics { get; private set; }
    public int LootFoundCount { get; private set; }
    public int FlagCount { get; private set; }
    public string LastFeedbackKey { get; private set; } = "THINGS_BILGE_MINES.ui.statusReady";
    public int LastFeedbackValue { get; private set; }

    public Task<ThingsBilgeSweepOutcome> CompletionTask => _completion.Task;

    public event Action? StateChanged;
    public event Action? BoardCompleted;

    public BilgeSweepMinigame(Player owner, Rng rng, int probes)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(rng);
        MaxProbes = Math.Clamp(probes, 1, 12);
        ProbesRemaining = MaxProbes;

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                _cells[x, y] = new BilgeCell(x, y);
            }
        }

        GenerateBoard(rng);
    }

    public BilgeCell GetCell(int x, int y) => _cells[x, y];

    public IEnumerable<BilgeCell> AllCells
    {
        get
        {
            for (int y = 0; y < GridHeight; y++)
                for (int x = 0; x < GridWidth; x++)
                    yield return _cells[x, y];
        }
    }

    private void GenerateBoard(Rng rng)
    {
        // Choose a guaranteed safe starter anchor on the outer border, then keep its immediate
        // 3-cell corner/edge pocket free of mines so players always start with concrete numbers.
        var borderCoords = new List<(int X, int Y)>();
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                if (x == 0 || y == 0 || x == GridWidth - 1 || y == GridHeight - 1)
                    borderCoords.Add((x, y));
            }
        }

        var anchor = borderCoords[rng.NextInt(borderCoords.Count)];
        var starterSafe = new HashSet<(int X, int Y)> { anchor };
        var orthogonalNeighbors = GetOrthogonalNeighbors(anchor.X, anchor.Y).ToList();
        Shuffle(orthogonalNeighbors, rng);
        foreach (var neighbor in orthogonalNeighbors.Take(2))
            starterSafe.Add(neighbor);

        var mineCandidates = new List<(int X, int Y)>();
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                if (!starterSafe.Contains((x, y)))
                    mineCandidates.Add((x, y));
            }
        }

        Shuffle(mineCandidates, rng);
        var placedMines = new List<BilgeCell>(MineCount);
        foreach (var (mx, my) in mineCandidates.Take(MineCount))
        {
            _cells[mx, my].IsMine = true;
            placedMines.Add(_cells[mx, my]);
        }

        RecomputeAdjacency();

        // Pre-reveal the 3 starter safe cells so the player has real Minesweeper clues from click 1.
        // If all 3 happen to be 0-adjacent, also pre-reveal 1 safe numbered clue cell nearby.
        foreach (var (sx, sy) in starterSafe)
        {
            _cells[sx, sy].IsRevealed = true;
            _cells[sx, sy].IsPreRevealed = true;
        }

        if (starterSafe.All(coord => _cells[coord.X, coord.Y].AdjacentMines == 0))
        {
            BilgeCell? extraClue = AllCells
                .Where(c => !c.IsMine && !c.IsRevealed && c.AdjacentMines > 0)
                .OrderBy(c => Math.Abs(c.X - anchor.X) + Math.Abs(c.Y - anchor.Y))
                .FirstOrDefault();
            if (extraClue != null)
            {
                extraClue.IsRevealed = true;
                extraClue.IsPreRevealed = true;
            }
        }

        // Place 5 Act 1-scaled loot caches on hidden non-mine cells.
        // Common Relic prefers high-tension cells adjacent to >= 2 mines.
        var hiddenSafe = AllCells.Where(c => !c.IsMine && !c.IsRevealed).ToList();
        Shuffle(hiddenSafe, rng);

        BilgeCell relicCell = hiddenSafe.FirstOrDefault(c => c.AdjacentMines >= 2)
            ?? hiddenSafe.FirstOrDefault(c => c.AdjacentMines >= 1)
            ?? hiddenSafe[0];
        relicCell.LootKind = BilgeLootKind.CommonRelic;
        hiddenSafe.Remove(relicCell);

        // Prefer numbered cells for remaining caches so Minesweeper deduction drives every grab.
        var orderedSafe = hiddenSafe
            .OrderByDescending(c => c.AdjacentMines > 0 ? 1 : 0)
            .ToList();

        BilgeLootKind[] remainingLoot =
        [
            BilgeLootKind.GoldMedium,
            BilgeLootKind.Potion,
            BilgeLootKind.Card,
            BilgeLootKind.GoldSmall,
        ];

        for (int i = 0; i < remainingLoot.Length && i < orderedSafe.Count; i++)
        {
            BilgeCell cell = orderedSafe[i];
            cell.LootKind = remainingLoot[i];
            cell.GoldValue = remainingLoot[i] switch
            {
                BilgeLootKind.GoldSmall => rng.NextInt(GoldSmallMin, GoldSmallMax + 1),
                BilgeLootKind.GoldMedium => rng.NextInt(GoldMediumMin, GoldMediumMax + 1),
                _ => 0,
            };
        }

        // Mark 2 toxin sacs as deceptive oily glints so clicking glints blindly is risky.
        Shuffle(placedMines, rng);
        foreach (BilgeCell decoy in placedMines.Take(DecoyGlintMineCount))
            decoy.IsDecoyGlint = true;
    }

    private void RecomputeAdjacency()
    {
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                int count = 0;
                foreach (var (nx, ny) in GetAllNeighbors(x, y))
                {
                    if (_cells[nx, ny].IsMine)
                        count++;
                }
                _cells[x, y].AdjacentMines = count;
            }
        }
    }

    public bool ToggleFlag(int x, int y)
    {
        if (IsComplete || !InBounds(x, y))
            return false;
        BilgeCell cell = _cells[x, y];
        if (cell.IsRevealed)
            return false;

        cell.IsFlagged = !cell.IsFlagged;
        FlagCount += cell.IsFlagged ? 1 : -1;
        LastFeedbackKey = cell.IsFlagged
            ? "THINGS_BILGE_MINES.ui.statusFlagged"
            : "THINGS_BILGE_MINES.ui.statusUnflagged";
        LastFeedbackValue = 0;
        StateChanged?.Invoke();
        return true;
    }

    public bool ProbeCell(int x, int y)
    {
        if (IsComplete || ProbesRemaining <= 0 || !InBounds(x, y))
            return false;

        BilgeCell cell = _cells[x, y];
        if (cell.IsRevealed || cell.IsFlagged)
            return false;

        ProbesRemaining--;

        if (cell.IsMine)
        {
            cell.IsRevealed = true;
            cell.WasTriggeredMine = true;
            HitMine = true;
            IsComplete = true;
            RevealRemainingMines();
            LastFeedbackKey = "THINGS_BILGE_MINES.ui.statusBurst";
            LastFeedbackValue = MineBurstDamage;
            StateChanged?.Invoke();
            NotifyBoardCompleted();
            return true;
        }

        int lootBefore = LootFoundCount;
        BilgeLootKind latestLoot = BilgeLootKind.None;
        int latestGold = 0;

        RevealSafeWithFlood(cell, ref latestLoot, ref latestGold);

        if (LootFoundCount > lootBefore)
        {
            LastFeedbackKey = latestLoot switch
            {
                BilgeLootKind.GoldSmall or BilgeLootKind.GoldMedium => "THINGS_BILGE_MINES.ui.statusFoundGold",
                BilgeLootKind.Potion => "THINGS_BILGE_MINES.ui.statusFoundPotion",
                BilgeLootKind.Card => "THINGS_BILGE_MINES.ui.statusFoundCard",
                BilgeLootKind.CommonRelic => "THINGS_BILGE_MINES.ui.statusFoundRelic",
                _ => "THINGS_BILGE_MINES.ui.statusSafe",
            };
            LastFeedbackValue = latestGold;
        }
        else
        {
            LastFeedbackKey = cell.AdjacentMines == 0
                ? "THINGS_BILGE_MINES.ui.statusClearedPocket"
                : "THINGS_BILGE_MINES.ui.statusSafe";
            LastFeedbackValue = cell.AdjacentMines;
        }

        if (ProbesRemaining <= 0 || LootFoundCount >= TotalLootCount)
        {
            IsComplete = true;
            RevealRemainingMines();
            if (LootFoundCount >= TotalLootCount)
                LastFeedbackKey = "THINGS_BILGE_MINES.ui.statusAllFound";
            else
                LastFeedbackKey = "THINGS_BILGE_MINES.ui.statusOutOfProbes";
            StateChanged?.Invoke();
            NotifyBoardCompleted();
            return true;
        }

        StateChanged?.Invoke();
        return true;
    }

    private void RevealSafeWithFlood(BilgeCell start, ref BilgeLootKind latestLoot, ref int latestGold)
    {
        var queue = new Queue<BilgeCell>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            BilgeCell current = queue.Dequeue();
            if (current.IsRevealed || current.IsMine)
                continue;

            if (current.IsFlagged)
            {
                current.IsFlagged = false;
                FlagCount = Math.Max(0, FlagCount - 1);
            }

            current.IsRevealed = true;
            if (current.LootKind != BilgeLootKind.None && !current.LootCollected)
            {
                current.LootCollected = true;
                LootFoundCount++;
                latestLoot = current.LootKind;
                switch (current.LootKind)
                {
                    case BilgeLootKind.GoldSmall:
                    case BilgeLootKind.GoldMedium:
                        CollectedGold += current.GoldValue;
                        latestGold = current.GoldValue;
                        break;
                    case BilgeLootKind.Potion:
                        CollectedPotions++;
                        break;
                    case BilgeLootKind.Card:
                        CollectedCards++;
                        break;
                    case BilgeLootKind.CommonRelic:
                        CollectedCommonRelics++;
                        break;
                }
            }

            if (current.AdjacentMines != 0)
                continue;

            foreach (var (nx, ny) in GetAllNeighbors(current.X, current.Y))
            {
                BilgeCell next = _cells[nx, ny];
                if (!next.IsRevealed && !next.IsMine)
                    queue.Enqueue(next);
            }
        }
    }

    public void CashOutAndFinish()
    {
        if (!IsComplete)
        {
            IsComplete = true;
            CashedOut = true;
            RevealRemainingMines();
            LastFeedbackKey = "THINGS_BILGE_MINES.ui.statusCashedOut";
            LastFeedbackValue = 0;
            StateChanged?.Invoke();
            NotifyBoardCompleted();
        }
        ConfirmExit();
    }

    public void ConfirmExit()
    {
        if (!IsComplete)
        {
            IsComplete = true;
            CashedOut = true;
        }
        _completion.TrySetResult(BuildOutcome());
    }

    public void ForceEnd()
    {
        if (_completion.Task.IsCompleted)
            return;

        if (ThingsEventMinigameSync.IsRunActive)
        {
            if (!IsComplete)
            {
                IsComplete = true;
                CashedOut = true;
            }
            _completion.TrySetResult(BuildOutcome());
            return;
        }

        _completion.TrySetCanceled();
    }

    public ThingsBilgeSweepOutcome BuildOutcome() => new(
        HitMine,
        CollectedGold,
        CollectedPotions,
        CollectedCards,
        CollectedCommonRelics,
        ProbesUsed,
        CashedOut);

    private void RevealRemainingMines()
    {
        foreach (BilgeCell cell in AllCells)
        {
            if (cell.IsMine)
                cell.IsRevealed = true;
        }
    }

    private void NotifyBoardCompleted()
    {
        if (_finishedNotified)
            return;
        _finishedNotified = true;
        BoardCompleted?.Invoke();
    }

    private static bool InBounds(int x, int y) => x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

    private static IEnumerable<(int X, int Y)> GetOrthogonalNeighbors(int x, int y)
    {
        if (x > 0) yield return (x - 1, y);
        if (x + 1 < GridWidth) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y + 1 < GridHeight) yield return (x, y + 1);
    }

    private static IEnumerable<(int X, int Y)> GetAllNeighbors(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                int nx = x + dx;
                int ny = y + dy;
                if (InBounds(nx, ny))
                    yield return (nx, ny);
            }
        }
    }

    private static void Shuffle<T>(IList<T> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
