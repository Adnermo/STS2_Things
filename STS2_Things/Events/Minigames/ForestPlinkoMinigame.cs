using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;

namespace STS2_Things.Events.Minigames;

public enum PlinkoBinKind
{
    CommonRelic = 0,
    Potion = 1,
    Gold = 2,
    Heal = 3,
    CardReward = 4,
    MaxHp = 5,
    UpgradeCard = 6,
    GoldBig = 7,
    RareRelic = 8,
}

public sealed class PlinkoPeg
{
    public int Index { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Radius { get; init; }
    public bool IsBumper { get; init; }
    public float Flash { get; set; }
}

public sealed class PlinkoBin
{
    public int Index { get; init; }
    public PlinkoBinKind Kind { get; set; }
    public int Amount { get; set; }
    public float Flash { get; set; }
}

public sealed class PlinkoSeedBall
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Vx { get; set; }
    public float Vy { get; set; }
    public bool Active { get; set; }
    public bool HitBumperThisDrop { get; set; }
    public int LastBinIndex { get; set; } = -1;
    public List<(float X, float Y)> Trail { get; } = new(24);
}

public sealed record ThingsForestPlinkoOutcome(
    int GoldAmount,
    int HealAmount,
    int MaxHpGain,
    int PotionCount,
    int CardCount,
    int CommonRelicCount,
    int RareRelicCount,
    int UpgradeCount,
    int DropsUsed)
{
    public bool HasAnyLoot =>
        GoldAmount > 0 || HealAmount > 0 || MaxHpGain > 0 ||
        PotionCount > 0 || CardCount > 0 || CommonRelicCount > 0 || RareRelicCount > 0 || UpgradeCount > 0;
}

/// <summary>
/// Deterministic 2D fixed-timestep physics and state controller for the Overgrowth (Act 1A)
/// Briar Plinko board ("古林落籽盘 / Overgrown Plinko").
/// Players choose one of 7 release notches along the carved hollow trunk to drop glowing amber seeds
/// through 45 briar pegs and 2 bonus glow-shroom bumpers into 7 asymmetric root-cup reward bins.
/// </summary>
public sealed class ForestPlinkoMinigame
{
    public const int Tier1HpCost = 6;
    public const int Tier1Drops = 1;
    public const int Tier2HpCost = 11;
    public const int Tier2Drops = 2;
    public const int Tier3HpCost = 16;
    public const int Tier3Drops = 3;

    public const int BumperBonusGold = 5;
    public const int BinGoldAmount = 20;
    public const int BinGoldBigAmount = 35;
    public const int BinHealAmount = 5;
    public const int BinMaxHpAmount = 3;

    public const float BoardWidth = 630f;
    public const float BoardHeight = 630f;
    public const int DropSlotCount = 7;
    public const int BinCount = 7;
    public const float BinWidth = BoardWidth / BinCount; // 90f exact
    public const float BinTopY = 524f;
    public const float BinFloorY = 602f;
    public const float BallRadius = 13.5f;
    public const float DropOriginY = 38f;

    private const float Gravity = 890f;
    private const float FixedStepDt = 1f / 60f;
    private const float PegRestitution = 0.60f;
    private const float BumperRestitution = 0.86f;

    private readonly Rng _rng;
    private readonly List<PlinkoPeg> _pegs = [];
    private readonly List<PlinkoBin> _bins = [];
    private readonly TaskCompletionSource<ThingsForestPlinkoOutcome> _completion = new();

    private float _accumulator;
    private float _dropElapsed;
    private bool _relicAwarded;

    public Player Owner { get; }
    public int MaxDrops { get; }
    public int DropsRemaining { get; private set; }
    public int DropsUsed { get; private set; }
    public int SelectedSlot { get; private set; } = 3;
    public bool IsComplete { get; private set; }

    public PlinkoSeedBall ActiveBall { get; } = new();

    public int CollectedGold { get; private set; }
    public int CollectedHeal { get; private set; }
    public int CollectedMaxHp { get; private set; }
    public int CollectedPotions { get; private set; }
    public int CollectedCards { get; private set; }
    public int CollectedCommonRelics { get; private set; }
    public int CollectedRareRelics { get; private set; }
    public int CollectedUpgrades { get; private set; }
    public int BumperHits { get; private set; }
    public bool SpawnedRareRelic { get; private set; }

    public string LastFeedbackKey { get; private set; } = "THINGS_FOREST_PLINKO.ui.statusReady";
    public int LastFeedbackValue { get; private set; }

    public IReadOnlyList<PlinkoPeg> Pegs => _pegs;
    public IReadOnlyList<PlinkoBin> Bins => _bins;
    public Task<ThingsForestPlinkoOutcome> CompletionTask => _completion.Task;

    public event Action? StateChanged;

    public ForestPlinkoMinigame(Player owner, Rng rng, int maxDrops)
    {
        Owner = owner;
        _rng = rng;
        MaxDrops = Math.Clamp(maxDrops, 1, 3);
        DropsRemaining = MaxDrops;
        BuildBoard();
    }

    public static float GetDropSlotX(int slotIndex)
    {
        int clamped = Math.Clamp(slotIndex, 0, DropSlotCount - 1);
        return 75f + clamped * 80f;
    }

    private void BuildBoard()
    {
        _pegs.Clear();
        int idx = 0;
        float[] rowYs = [108f, 166f, 224f, 282f, 340f, 398f, 456f];
        for (int row = 0; row < rowYs.Length; row++)
        {
            bool even = row % 2 == 0;
            int count = even ? 6 : 7;
            float startX = even ? 115f : 75f;
            for (int col = 0; col < count; col++)
            {
                bool isBumper = row == 3 && (col == 1 || col == 5);
                _pegs.Add(new PlinkoPeg
                {
                    Index = idx++,
                    X = startX + col * 80f,
                    Y = rowYs[row],
                    Radius = isBumper ? 15.5f : 10.5f,
                    IsBumper = isBumper,
                });
            }
        }

        _bins.Clear();
        // Bin 0 mystery cardboard relic box rolls randomly between Common (75%) and Rare (25%)!
        SpawnedRareRelic = _rng.NextFloat() < 0.25f;
        _bins.Add(new PlinkoBin { Index = 0, Kind = SpawnedRareRelic ? PlinkoBinKind.RareRelic : PlinkoBinKind.CommonRelic, Amount = 1 });
        _bins.Add(new PlinkoBin { Index = 1, Kind = PlinkoBinKind.Potion, Amount = 1 });
        _bins.Add(new PlinkoBin { Index = 2, Kind = PlinkoBinKind.Gold, Amount = BinGoldAmount });
        _bins.Add(new PlinkoBin { Index = 3, Kind = PlinkoBinKind.Heal, Amount = BinHealAmount });
        _bins.Add(new PlinkoBin { Index = 4, Kind = PlinkoBinKind.CardReward, Amount = 1 });
        _bins.Add(new PlinkoBin { Index = 5, Kind = PlinkoBinKind.MaxHp, Amount = BinMaxHpAmount });
        _bins.Add(new PlinkoBin { Index = 6, Kind = PlinkoBinKind.UpgradeCard, Amount = 1 });
    }

    public void SelectSlot(int slotIndex)
    {
        int clamped = Math.Clamp(slotIndex, 0, DropSlotCount - 1);
        if (SelectedSlot == clamped)
            return;
        SelectedSlot = clamped;
        StateChanged?.Invoke();
    }

    public bool CanDrop() => !IsComplete && !ActiveBall.Active && DropsRemaining > 0;

    public bool TryDropSeed(int? slotOverride = null)
    {
        if (!CanDrop())
            return false;

        if (slotOverride.HasValue)
            SelectedSlot = Math.Clamp(slotOverride.Value, 0, DropSlotCount - 1);

        DropsRemaining--;
        DropsUsed++;
        _dropElapsed = 0f;

        ActiveBall.X = GetDropSlotX(SelectedSlot) + _rng.NextFloat(-6f, 6f);
        ActiveBall.Y = DropOriginY;
        ActiveBall.Vx = _rng.NextFloat(-22f, 22f);
        ActiveBall.Vy = 25f;
        ActiveBall.Active = true;
        ActiveBall.HitBumperThisDrop = false;
        ActiveBall.LastBinIndex = -1;
        ActiveBall.Trail.Clear();
        ActiveBall.Trail.Add((ActiveBall.X, ActiveBall.Y));

        LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusDropping";
        LastFeedbackValue = 0;
        StateChanged?.Invoke();
        return true;
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;
        _accumulator += Math.Min(deltaSeconds, 0.1f);
        while (_accumulator >= FixedStepDt)
        {
            _accumulator -= FixedStepDt;
            StepFixed(FixedStepDt);
        }
    }

    public void StepUntilBallLands(int maxTicks = 900)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            StepFixed(FixedStepDt);
            if (!ActiveBall.Active)
                break;
        }
    }

    private void StepFixed(float dt)
    {
        // Decay peg & bin flashes smoothly
        foreach (PlinkoPeg peg in _pegs)
        {
            if (peg.Flash > 0f)
                peg.Flash = MathF.Max(0f, peg.Flash - dt * 2.6f);
        }
        foreach (PlinkoBin bin in _bins)
        {
            if (bin.Flash > 0f)
                bin.Flash = MathF.Max(0f, bin.Flash - dt * 1.4f);
        }

        if (!ActiveBall.Active)
            return;

        _dropElapsed += dt;

        // 2 physics substeps per 60Hz frame for crisp tunneling-free peg collisions
        const int subSteps = 2;
        float subDt = dt / subSteps;
        for (int s = 0; s < subSteps; s++)
        {
            ActiveBall.Vy += Gravity * subDt;
            ActiveBall.Vx *= 0.9985f;
            ActiveBall.X += ActiveBall.Vx * subDt;
            ActiveBall.Y += ActiveBall.Vy * subDt;

            // Left / right board cavity walls
            if (ActiveBall.X < BallRadius + 8f)
            {
                ActiveBall.X = BallRadius + 8f;
                ActiveBall.Vx = MathF.Abs(ActiveBall.Vx) * 0.65f + 24f;
            }
            else if (ActiveBall.X > BoardWidth - BallRadius - 8f)
            {
                ActiveBall.X = BoardWidth - BallRadius - 8f;
                ActiveBall.Vx = -MathF.Abs(ActiveBall.Vx) * 0.65f - 24f;
            }

            // Peg & bonus glow-shroom bumper circle collisions
            foreach (PlinkoPeg peg in _pegs)
            {
                float dx = ActiveBall.X - peg.X;
                float dy = ActiveBall.Y - peg.Y;
                float minDist = BallRadius + peg.Radius;
                float distSq = dx * dx + dy * dy;
                if (distSq >= minDist * minDist)
                    continue;

                float dist = MathF.Sqrt(MathF.Max(distSq, 0.0001f));
                float nx = dx / dist;
                float ny = dy / dist;

                // Push out of overlap cleanly
                ActiveBall.X = peg.X + nx * (minDist + 0.4f);
                ActiveBall.Y = peg.Y + ny * (minDist + 0.4f);

                float vn = ActiveBall.Vx * nx + ActiveBall.Vy * ny;
                if (vn < 0f)
                {
                    float e = peg.IsBumper ? BumperRestitution : PegRestitution;
                    ActiveBall.Vx -= (1f + e) * vn * nx;
                    ActiveBall.Vy -= (1f + e) * vn * ny;
                }

                // Prevent dead-center stalling on top of any peg
                if (MathF.Abs(nx) < 0.16f)
                {
                    float kickDir = _rng.NextBool() ? 1f : -1f;
                    ActiveBall.Vx += kickDir * _rng.NextFloat(42f, 74f);
                }
                else
                {
                    ActiveBall.Vx += _rng.NextFloat(-16f, 16f);
                }

                // Clamp runaway velocity so bounces look weighty and readable
                float speed = MathF.Sqrt(ActiveBall.Vx * ActiveBall.Vx + ActiveBall.Vy * ActiveBall.Vy);
                if (speed > 620f)
                {
                    float scale = 620f / speed;
                    ActiveBall.Vx *= scale;
                    ActiveBall.Vy *= scale;
                }

                peg.Flash = 1.0f;
                if (peg.IsBumper && !ActiveBall.HitBumperThisDrop)
                {
                    ActiveBall.HitBumperThisDrop = true;
                    BumperHits++;
                    CollectedGold += BumperBonusGold;
                    LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusHitBumper";
                    LastFeedbackValue = BumperBonusGold;
                    StateChanged?.Invoke();
                }
            }

            // Bottom bin vertical divider ribs (y >= BinTopY)
            if (ActiveBall.Y + BallRadius >= BinTopY)
            {
                for (int d = 1; d < BinCount; d++)
                {
                    float divX = d * BinWidth;
                    float distX = ActiveBall.X - divX;
                    if (MathF.Abs(distX) < BallRadius + 3f)
                    {
                        float sign = distX >= 0f ? 1f : -1f;
                        ActiveBall.X = divX + sign * (BallRadius + 3.2f);
                        ActiveBall.Vx = sign * MathF.Max(18f, MathF.Abs(ActiveBall.Vx) * 0.45f);
                    }
                }
            }
        }

        // Record glowing amber trail
        ActiveBall.Trail.Add((ActiveBall.X, ActiveBall.Y));
        if (ActiveBall.Trail.Count > 22)
            ActiveBall.Trail.RemoveAt(0);

        // Safety fallback against ultra-rare low-velocity stall
        if (_dropElapsed > 8.0f && ActiveBall.Y < BinFloorY)
            ActiveBall.Vy = MathF.Max(ActiveBall.Vy, 260f);

        // Landing in bottom root cup!
        if (ActiveBall.Y >= BinFloorY)
        {
            ActiveBall.Y = BinFloorY;
            ActiveBall.Active = false;
            int binIdx = Math.Clamp((int)(ActiveBall.X / BinWidth), 0, BinCount - 1);
            ActiveBall.LastBinIndex = binIdx;
            ResolveBinLanding(_bins[binIdx]);
        }
    }

    private void ResolveBinLanding(PlinkoBin bin)
    {
        bin.Flash = 1.0f;
        switch (bin.Kind)
        {
            case PlinkoBinKind.CommonRelic:
                CollectedCommonRelics++;
                _relicAwarded = true;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonRelic";
                LastFeedbackValue = BinGoldBigAmount;
                // Cap board to 1 Relic so repeat left-wing hits stay balanced & rewarding!
                bin.Kind = PlinkoBinKind.GoldBig;
                bin.Amount = BinGoldBigAmount;
                break;

            case PlinkoBinKind.RareRelic:
                CollectedRareRelics++;
                _relicAwarded = true;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonRareRelic";
                LastFeedbackValue = BinGoldBigAmount;
                bin.Kind = PlinkoBinKind.GoldBig;
                bin.Amount = BinGoldBigAmount;
                break;

            case PlinkoBinKind.GoldBig:
                CollectedGold += bin.Amount;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonGold";
                LastFeedbackValue = bin.Amount;
                break;

            case PlinkoBinKind.Potion:
                CollectedPotions++;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonPotion";
                LastFeedbackValue = 0;
                break;

            case PlinkoBinKind.Gold:
                CollectedGold += bin.Amount;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonGold";
                LastFeedbackValue = bin.Amount;
                break;

            case PlinkoBinKind.Heal:
                CollectedHeal += bin.Amount;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonHeal";
                LastFeedbackValue = bin.Amount;
                break;

            case PlinkoBinKind.CardReward:
                CollectedCards++;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonCard";
                LastFeedbackValue = 0;
                break;

            case PlinkoBinKind.MaxHp:
                CollectedMaxHp += bin.Amount;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonMaxHp";
                LastFeedbackValue = bin.Amount;
                break;

            case PlinkoBinKind.UpgradeCard:
                CollectedUpgrades++;
                LastFeedbackKey = "THINGS_FOREST_PLINKO.ui.statusWonUpgrade";
                LastFeedbackValue = BinGoldBigAmount;
                // Cap far-right bowl to 1 Upgrade so repeat hits convert cleanly into gold!
                bin.Kind = PlinkoBinKind.GoldBig;
                bin.Amount = BinGoldBigAmount;
                break;
        }

        if (DropsRemaining <= 0)
            IsComplete = true;

        StateChanged?.Invoke();
    }

    public bool HasAwardedBoardRelic => _relicAwarded;

    public void FinishEarly()
    {
        if (IsComplete)
            return;
        if (ActiveBall.Active)
            StepUntilBallLands();
        IsComplete = true;
        StateChanged?.Invoke();
    }

    public void ConfirmExit()
    {
        if (!IsComplete)
            FinishEarly();
        _completion.TrySetResult(BuildOutcome());
    }

    public void ForceEnd()
    {
        if (_completion.Task.IsCompleted)
            return;

        if (ThingsEventMinigameSync.IsRunActive)
            _completion.TrySetResult(BuildOutcome());
        else
            _completion.TrySetCanceled();
    }

    public ThingsForestPlinkoOutcome BuildOutcome() => new(
        CollectedGold,
        CollectedHeal,
        CollectedMaxHp,
        CollectedPotions,
        CollectedCards,
        CollectedCommonRelics,
        CollectedRareRelics,
        CollectedUpgrades,
        DropsUsed);
}
