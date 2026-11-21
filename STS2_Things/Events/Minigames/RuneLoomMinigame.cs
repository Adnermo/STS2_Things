using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Random;

namespace STS2_Things.Events.Minigames;

public enum RuneLoomEnchantmentKind : byte
{
    None = 0,
    Sharp = 1,
    Vigorous = 2,
    Nimble = 3,
    Adroit = 4,
    Steady = 5,
    Swift = 6,
    Clone = 7,
    Sown = 8,
}

public enum RuneTileKind : byte
{
    Moss = 0,
    Upgrade = 1,
    Enchantment = 2,
    SapHeal = 3,
    AmberGold = 4,
    Heartwood = 5,
    ThornCut = 6,
    SapDrain = 7,
    BlightVine = 8,
    ClingingTwig = 9,
}

public enum RuneClickRejection : byte
{
    None = 0,
    MaxLengthReached = 1,
    NotAdjacent = 2,
    EnchantmentLimitReached = 3,
    NotEnoughGold = 4,
    NotEnoughMaxHp = 5,
}

public sealed record ThingsRuneLoomOutcome(
    bool Cancelled,
    int DeckCardIndex,
    bool UpgradeCard,
    RuneLoomEnchantmentKind EnchantmentKind,
    int EnchantmentAmount,
    int HealAmount,
    int GoldGain,
    int MaxHpGain,
    int DamageAmount,
    int GoldLoss,
    int MaxHpLoss,
    int CurseCount)
{
    public bool HasAnyCost => DamageAmount > 0 || GoldLoss > 0 || MaxHpLoss > 0 || CurseCount > 0;
    public bool HasAnyGain => UpgradeCard || EnchantmentKind != RuneLoomEnchantmentKind.None || HealAmount > 0 || GoldGain > 0 || MaxHpGain > 0;
}

public sealed class RuneLoomTile
{
    public int X { get; }
    public int Y { get; }
    public RuneTileKind Kind { get; internal set; }
    public RuneLoomEnchantmentKind EnchantmentKind { get; internal set; }
    public int Value { get; internal set; }

    public bool IsCost => Kind is RuneTileKind.ThornCut or RuneTileKind.SapDrain or RuneTileKind.BlightVine or RuneTileKind.ClingingTwig;
    public bool IsBoon => Kind is RuneTileKind.Upgrade or RuneTileKind.Enchantment or RuneTileKind.SapHeal or RuneTileKind.AmberGold or RuneTileKind.Heartwood;

    internal RuneLoomTile(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Pure deterministic state controller for the Overgrowth (Act 1) 4x4 Overgrown Rune Loom path-tracing minigame.
/// Players trace a connected orthogonal path of 1..4 carved rune stones to upgrade/enchant a chosen card
/// and gather forest boons; high-value corner pairings cross interior thorn/sap cost tiles.
/// Every mutation uses 100% vanilla save-safe card & player operations.
/// </summary>
public sealed class RuneLoomMinigame
{
    public const int GridWidth = 4;
    public const int GridHeight = 4;
    public const int MaxPathLength = 4;

    public const int ThornDamage = 6;
    public const int SapGoldCost = 30;
    public const int BlightMaxHpLoss = 4;
    public const int SapHealAmount = 5;
    public const int UpgradedFallbackHeal = 10;
    public const int AmberGoldAmount = 20;
    public const int HeartwoodMaxHpGain = 3;
    public const int FallbackGatherSapHeal = 8;

    public const int SharpAmount = 2;
    public const int VigorousAmount = 4;
    public const int NimbleAmount = 2;
    public const int AdroitAmount = 3;
    public const int SteadyAmount = 1;
    public const int SwiftAmount = 1;
    public const int CloneAmount = 1;
    public const int SownAmount = 1;

    private readonly RuneLoomTile[,] _tiles = new RuneLoomTile[GridWidth, GridHeight];
    private readonly List<RuneLoomTile> _path = new(MaxPathLength);
    private readonly TaskCompletionSource<ThingsRuneLoomOutcome> _completion = new();

    public Player Owner { get; }
    public CardModel SelectedCard { get; }
    public int DeckCardIndex { get; }
    public IReadOnlyList<RuneLoomTile> Path => _path;
    public string LastFeedbackKey { get; private set; } = "THINGS_MOSS_RUNE_LOOM.ui.statusReady";
    public Task<ThingsRuneLoomOutcome> CompletionTask => _completion.Task;

    public event Action? StateChanged;

    public RuneLoomMinigame(Player owner, CardModel selectedCard, int deckCardIndex, Rng rng)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SelectedCard = selectedCard ?? throw new ArgumentNullException(nameof(selectedCard));
        DeckCardIndex = deckCardIndex;
        ArgumentNullException.ThrowIfNull(rng);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                _tiles[x, y] = new RuneLoomTile(x, y);
            }
        }

        GenerateBoard(rng);
    }

    public static bool IsEligibleCard(CardModel? card)
    {
        if (card == null)
            return false;
        if (card.Type is not (CardType.Attack or CardType.Skill or CardType.Power))
            return false;
        return card.IsUpgradable || GetLegalEnchantments(card).Count > 0;
    }

    public static List<(RuneLoomEnchantmentKind Kind, int Amount)> GetLegalEnchantments(CardModel card)
    {
        (RuneLoomEnchantmentKind Kind, int Amount)[] candidates = card.Type switch
        {
            CardType.Attack =>
            [
                (RuneLoomEnchantmentKind.Sharp, SharpAmount),
                (RuneLoomEnchantmentKind.Vigorous, VigorousAmount),
                (RuneLoomEnchantmentKind.Swift, SwiftAmount),
                (RuneLoomEnchantmentKind.Steady, SteadyAmount),
                (RuneLoomEnchantmentKind.Clone, CloneAmount),
                (RuneLoomEnchantmentKind.Sown, SownAmount),
            ],
            CardType.Skill =>
            [
                (RuneLoomEnchantmentKind.Nimble, NimbleAmount),
                (RuneLoomEnchantmentKind.Adroit, AdroitAmount),
                (RuneLoomEnchantmentKind.Swift, SwiftAmount),
                (RuneLoomEnchantmentKind.Steady, SteadyAmount),
                (RuneLoomEnchantmentKind.Clone, CloneAmount),
                (RuneLoomEnchantmentKind.Sown, SownAmount),
            ],
            _ =>
            [
                (RuneLoomEnchantmentKind.Steady, SteadyAmount),
                (RuneLoomEnchantmentKind.Swift, SwiftAmount),
                (RuneLoomEnchantmentKind.Sown, SownAmount),
                (RuneLoomEnchantmentKind.Clone, CloneAmount),
            ],
        };

        var result = new List<(RuneLoomEnchantmentKind Kind, int Amount)>(candidates.Length);
        foreach (var candidate in candidates)
        {
            EnchantmentModel? canonical = GetCanonicalEnchantment(candidate.Kind);
            if (canonical != null && canonical.CanEnchant(card))
                result.Add(candidate);
        }
        return result;
    }

    public static EnchantmentModel? GetCanonicalEnchantment(RuneLoomEnchantmentKind kind) => kind switch
    {
        RuneLoomEnchantmentKind.Sharp => ModelDb.Enchantment<Sharp>(),
        RuneLoomEnchantmentKind.Vigorous => ModelDb.Enchantment<Vigorous>(),
        RuneLoomEnchantmentKind.Nimble => ModelDb.Enchantment<Nimble>(),
        RuneLoomEnchantmentKind.Adroit => ModelDb.Enchantment<Adroit>(),
        RuneLoomEnchantmentKind.Steady => ModelDb.Enchantment<Steady>(),
        RuneLoomEnchantmentKind.Swift => ModelDb.Enchantment<Swift>(),
        RuneLoomEnchantmentKind.Clone => ModelDb.Enchantment<Clone>(),
        RuneLoomEnchantmentKind.Sown => ModelDb.Enchantment<Sown>(),
        _ => null,
    };

    public RuneLoomTile GetTile(int x, int y) => _tiles[x, y];

    public IEnumerable<RuneLoomTile> AllTiles
    {
        get
        {
            for (int y = 0; y < GridHeight; y++)
                for (int x = 0; x < GridWidth; x++)
                    yield return _tiles[x, y];
        }
    }

    private void GenerateBoard(Rng rng)
    {
        // Use a rotated diagonal-barrier topology on the 4x4 slab:
        //   (0,0): Upgrade (or big +10 HP heal if already upgraded)
        //   (1,0), (0,1): Safe outer-corner boons (Warm Sap +5 HP / Amber Coin +20g)
        //   (2,0), (1,1), (0,2): Briar/Sap/Blight/Twig barrier (distance 2 from Upgrade)
        //   (3,0), (2,1), (1,2), (0,3): Up to 4 legal Act 1 Enchantments (distance 3 from Upgrade!)
        //   (3,1), (1,3), (3,2), (2,3), (3,3): Safe far-side boons & Moss stepping stones + 4th Cost at (2,2)
        // Result:
        //   - Zero-cost safe routes give EITHER Upgrade + 1 small boon OR 1 Enchantment + 1-2 small boons.
        //   - Linking BOTH Upgrade + any Enchantment in <= 4 steps ALWAYS requires crossing >= 1 Cost tile!
        int rotation = rng.NextInt(4);
        (int X, int Y) Rot(int x, int y) => rotation switch
        {
            1 => (GridWidth - 1 - y, x),
            2 => (GridWidth - 1 - x, GridHeight - 1 - y),
            3 => (y, GridHeight - 1 - x),
            _ => (x, y),
        };

        void Set(int lx, int ly, RuneTileKind kind, int value, RuneLoomEnchantmentKind ench = RuneLoomEnchantmentKind.None)
        {
            var (rx, ry) = Rot(lx, ly);
            RuneLoomTile tile = _tiles[rx, ry];
            tile.Kind = kind;
            tile.Value = value;
            tile.EnchantmentKind = ench;
        }

        // 1. Anchor corner (0,0): Upgrade (or +10 HP Warm Sap if card is already upgraded)
        if (SelectedCard.IsUpgradable)
            Set(0, 0, RuneTileKind.Upgrade, 1);
        else
            Set(0, 0, RuneTileKind.SapHeal, UpgradedFallbackHeal);

        // 2. Immediate safe neighbors of Upgrade: (1,0) & (0,1)
        var nearBoons = new List<(RuneTileKind Kind, int Value)>
        {
            (RuneTileKind.SapHeal, SapHealAmount),
            (RuneTileKind.AmberGold, AmberGoldAmount),
        };
        Shuffle(nearBoons, rng);
        Set(1, 0, nearBoons[0].Kind, nearBoons[0].Value);
        Set(0, 1, nearBoons[1].Kind, nearBoons[1].Value);

        // 3. Four distinct Act 1 Cost tiles: 3 on the diagonal barrier (2,0), (1,1), (0,2), plus 1 at (2,2)
        var costs = new List<(RuneTileKind Kind, int Value)>
        {
            (RuneTileKind.ThornCut, ThornDamage),
            (RuneTileKind.SapDrain, SapGoldCost),
            (RuneTileKind.BlightVine, BlightMaxHpLoss),
            (RuneTileKind.ClingingTwig, 1),
        };
        Shuffle(costs, rng);
        Set(2, 0, costs[0].Kind, costs[0].Value);
        Set(1, 1, costs[1].Kind, costs[1].Value);
        Set(0, 2, costs[2].Kind, costs[2].Value);
        Set(2, 2, costs[3].Kind, costs[3].Value);

        // 4. Distance-3 arc across the barrier: (3,0), (2,1), (1,2), (0,3) holds up to 4 legal Enchantments
        List<(RuneLoomEnchantmentKind Kind, int Amount)> legalEnchants = GetLegalEnchantments(SelectedCard);
        Shuffle(legalEnchants, rng);
        (int X, int Y)[] enchantArc = [(3, 0), (2, 1), (1, 2), (0, 3)];
        for (int i = 0; i < enchantArc.Length; i++)
        {
            if (i < legalEnchants.Count)
                Set(enchantArc[i].X, enchantArc[i].Y, RuneTileKind.Enchantment, legalEnchants[i].Amount, legalEnchants[i].Kind);
            else
                Set(enchantArc[i].X, enchantArc[i].Y, i % 2 == 0 ? RuneTileKind.AmberGold : RuneTileKind.SapHeal,
                    i % 2 == 0 ? AmberGoldAmount : SapHealAmount);
        }

        // 5. Far-side woodland tiles (3,1), (1,3), (3,2), (2,3), (3,3): modest boons + safe Moss bridges
        var farSpecs = new List<(RuneTileKind Kind, int Value)>
        {
            (RuneTileKind.Heartwood, HeartwoodMaxHpGain),
            (RuneTileKind.SapHeal, SapHealAmount),
            (RuneTileKind.AmberGold, AmberGoldAmount),
            (RuneTileKind.Moss, 0),
            (RuneTileKind.Moss, 0),
        };
        Shuffle(farSpecs, rng);
        (int X, int Y)[] farCoords = [(3, 1), (1, 3), (3, 2), (2, 3), (3, 3)];
        for (int i = 0; i < farCoords.Length; i++)
            Set(farCoords[i].X, farCoords[i].Y, farSpecs[i].Kind, farSpecs[i].Value);
    }

    public int GetPathIndex(RuneLoomTile tile) => _path.IndexOf(tile);

    public bool HasEnchantmentInPath => _path.Any(t => t.Kind == RuneTileKind.Enchantment);

    public bool CanAddTile(int x, int y, out RuneClickRejection rejection)
    {
        rejection = RuneClickRejection.None;
        if (x < 0 || x >= GridWidth || y < 0 || y >= GridHeight)
        {
            rejection = RuneClickRejection.NotAdjacent;
            return false;
        }

        RuneLoomTile tile = _tiles[x, y];
        if (_path.Contains(tile))
            return true; // Clicking a tile already in path either undoes or trims back to it.

        if (_path.Count >= MaxPathLength)
        {
            rejection = RuneClickRejection.MaxLengthReached;
            return false;
        }

        if (_path.Count > 0)
        {
            RuneLoomTile tail = _path[^1];
            int manhattan = Math.Abs(tail.X - tile.X) + Math.Abs(tail.Y - tile.Y);
            if (manhattan != 1)
            {
                rejection = RuneClickRejection.NotAdjacent;
                return false;
            }
        }

        if (tile.Kind == RuneTileKind.Enchantment && HasEnchantmentInPath)
        {
            rejection = RuneClickRejection.EnchantmentLimitReached;
            return false;
        }

        ThingsRuneLoomOutcome projected = BuildOutcome(cancelled: false, extraTile: tile);
        if (projected.GoldLoss > Owner.Gold)
        {
            rejection = RuneClickRejection.NotEnoughGold;
            return false;
        }
        if (projected.MaxHpLoss >= Owner.Creature.MaxHp)
        {
            rejection = RuneClickRejection.NotEnoughMaxHp;
            return false;
        }

        return true;
    }

    public bool ClickTile(int x, int y, out RuneClickRejection rejection)
    {
        if (!CanAddTile(x, y, out rejection))
        {
            LastFeedbackKey = rejection switch
            {
                RuneClickRejection.MaxLengthReached => "THINGS_MOSS_RUNE_LOOM.ui.rejectMaxLength",
                RuneClickRejection.NotAdjacent => "THINGS_MOSS_RUNE_LOOM.ui.rejectNotAdjacent",
                RuneClickRejection.EnchantmentLimitReached => "THINGS_MOSS_RUNE_LOOM.ui.rejectEnchantLimit",
                RuneClickRejection.NotEnoughGold => "THINGS_MOSS_RUNE_LOOM.ui.rejectNoGold",
                RuneClickRejection.NotEnoughMaxHp => "THINGS_MOSS_RUNE_LOOM.ui.rejectNoMaxHp",
                _ => "THINGS_MOSS_RUNE_LOOM.ui.statusReady",
            };
            StateChanged?.Invoke();
            return false;
        }

        RuneLoomTile tile = _tiles[x, y];
        int existingIndex = _path.IndexOf(tile);
        if (existingIndex >= 0)
        {
            if (existingIndex == _path.Count - 1)
                _path.RemoveAt(_path.Count - 1);
            else
                _path.RemoveRange(existingIndex + 1, _path.Count - (existingIndex + 1));

            LastFeedbackKey = _path.Count == 0
                ? "THINGS_MOSS_RUNE_LOOM.ui.statusReady"
                : "THINGS_MOSS_RUNE_LOOM.ui.statusTrimmed";
            StateChanged?.Invoke();
            return true;
        }

        _path.Add(tile);
        LastFeedbackKey = _path.Count >= MaxPathLength
            ? "THINGS_MOSS_RUNE_LOOM.ui.statusFullPath"
            : "THINGS_MOSS_RUNE_LOOM.ui.statusExtended";
        StateChanged?.Invoke();
        return true;
    }

    public bool UndoLast()
    {
        if (_path.Count == 0)
            return false;
        _path.RemoveAt(_path.Count - 1);
        LastFeedbackKey = _path.Count == 0
            ? "THINGS_MOSS_RUNE_LOOM.ui.statusReady"
            : "THINGS_MOSS_RUNE_LOOM.ui.statusTrimmed";
        StateChanged?.Invoke();
        return true;
    }

    public void ResetPath()
    {
        if (_path.Count == 0)
            return;
        _path.Clear();
        LastFeedbackKey = "THINGS_MOSS_RUNE_LOOM.ui.statusReady";
        StateChanged?.Invoke();
    }

    public bool CanConfirm()
    {
        if (_path.Count == 0)
            return false;
        ThingsRuneLoomOutcome outcome = BuildOutcome(cancelled: false);
        return outcome.GoldLoss <= Owner.Gold && outcome.MaxHpLoss < Owner.Creature.MaxHp;
    }

    public bool IsLethalWarning => BuildOutcome(cancelled: false).DamageAmount >= Owner.Creature.CurrentHp;

    public bool ConfirmPath()
    {
        if (!CanConfirm())
            return false;
        _completion.TrySetResult(BuildOutcome(cancelled: false));
        return true;
    }

    public void Cancel()
    {
        _completion.TrySetResult(BuildOutcome(cancelled: true));
    }

    public void ForceEnd()
    {
        if (!_completion.Task.IsCompleted)
            _completion.TrySetCanceled();
    }

    public ThingsRuneLoomOutcome BuildOutcome(bool cancelled = false, RuneLoomTile? extraTile = null)
    {
        if (cancelled)
        {
            return new ThingsRuneLoomOutcome(
                Cancelled: true,
                DeckCardIndex: DeckCardIndex,
                UpgradeCard: false,
                EnchantmentKind: RuneLoomEnchantmentKind.None,
                EnchantmentAmount: 0,
                HealAmount: 0,
                GoldGain: 0,
                MaxHpGain: 0,
                DamageAmount: 0,
                GoldLoss: 0,
                MaxHpLoss: 0,
                CurseCount: 0);
        }

        bool upgrade = false;
        RuneLoomEnchantmentKind enchKind = RuneLoomEnchantmentKind.None;
        int enchAmount = 0;
        int heal = 0;
        int goldGain = 0;
        int maxHpGain = 0;
        int damage = 0;
        int goldLoss = 0;
        int maxHpLoss = 0;
        int curses = 0;

        IEnumerable<RuneLoomTile> seq = extraTile == null ? _path : _path.Append(extraTile);
        foreach (RuneLoomTile tile in seq)
        {
            switch (tile.Kind)
            {
                case RuneTileKind.Upgrade:
                    upgrade = true;
                    break;
                case RuneTileKind.Enchantment:
                    enchKind = tile.EnchantmentKind;
                    enchAmount = tile.Value;
                    break;
                case RuneTileKind.SapHeal:
                    heal += tile.Value;
                    break;
                case RuneTileKind.AmberGold:
                    goldGain += tile.Value;
                    break;
                case RuneTileKind.Heartwood:
                    maxHpGain += tile.Value;
                    break;
                case RuneTileKind.ThornCut:
                    damage += tile.Value;
                    break;
                case RuneTileKind.SapDrain:
                    goldLoss += tile.Value;
                    break;
                case RuneTileKind.BlightVine:
                    maxHpLoss += tile.Value;
                    break;
                case RuneTileKind.ClingingTwig:
                    curses += tile.Value;
                    break;
            }
        }

        return new ThingsRuneLoomOutcome(
            Cancelled: false,
            DeckCardIndex: DeckCardIndex,
            UpgradeCard: upgrade,
            EnchantmentKind: enchKind,
            EnchantmentAmount: enchAmount,
            HealAmount: heal,
            GoldGain: goldGain,
            MaxHpGain: maxHpGain,
            DamageAmount: damage,
            GoldLoss: goldLoss,
            MaxHpLoss: maxHpLoss,
            CurseCount: curses);
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
