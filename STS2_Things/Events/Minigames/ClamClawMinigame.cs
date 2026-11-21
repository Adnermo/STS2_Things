using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;

namespace STS2_Things.Events.Minigames;

public enum ClamPrizeKind
{
    GoldSmall = 0,
    GoldBig = 1,
    Potion = 2,
    Card = 3,
    CommonRelic = 4,
    BarnacleScrap = 5,
    RareRelic = 6,
}

public enum ClamPrizeState
{
    InTank = 0,
    InShell = 1,
    FallingSlip = 2,
    Collected = 3,
}

public enum ClamCranePhase
{
    Aiming = 0,
    Descending = 1,
    Clamping = 2,
    Ascending = 3,
    Depositing = 4,
    Done = 5,
}

public sealed class ClamPrize
{
    public int Id { get; init; }
    public ClamPrizeKind Kind { get; init; }
    public int GoldValue { get; init; }
    public int VisualVariant { get; init; }
    public float BodyRadius { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Vx { get; set; }
    public float Vy { get; set; }
    public float TiltAngle { get; set; }
    public float CoreRadius { get; init; }
    public float GrazeRadius { get; init; }
    public float SlipChance { get; init; }
    public ClamPrizeState State { get; set; } = ClamPrizeState.InTank;
}

public sealed record ThingsClamClawOutcome(
    int GoldAmount,
    int PotionCount,
    int CardCount,
    int CommonRelicCount,
    int RareRelicCount,
    int GrabsUsed,
    int SlippedCount)
{
    public bool HasAnyLoot => GoldAmount > 0 || PotionCount > 0 || CardCount > 0 || CommonRelicCount > 0 || RareRelicCount > 0;
}

/// <summary>
/// Pure-logic deterministic physics and state controller for the Underdocks (Act 1B)
/// Sewer Clam Claw Machine ("蚌口夹珍 / Clam Claw Crane").
/// 20 hand-painted prizes settle under real 2D gravity &amp; circle-stacking into a natural
/// arcade claw-machine mound at the bottom of the flooded pit; plucking a prize makes
/// neighboring loot tumble down to fill the void!
/// </summary>
public sealed class ClamClawMinigame
{
    public const int Tier1GoldCost = 25;
    public const int Tier1Grabs = 1;
    public const int Tier2GoldCost = 45;
    public const int Tier2Grabs = 2;
    public const int Tier3GoldCost = 65;
    public const int Tier3Grabs = 3;
    public const int RelicFallbackGold = 45;
    public const int RareRelicFallbackGold = 80;

    public const int TotalPrizeCount = 28;

    public const float TankWidth = 630f;
    public const float TankHeight = 630f;
    public const float PileLeftX = 32f;
    public const float PileRightX = 598f;
    public const float PileFloorY = 608f;
    public const float MinCraneX = 64f;
    public const float MaxCraneX = 566f;
    public const float RestCraneY = 76f;
    public const float FloorCraneY = 568f;

    private const float SweepSpeed = 178f;
    private const float DescendSpeed = 395f;
    private const float AscendSpeed = 255f;
    private const float ClampDuration = 0.34f;
    private const float DepositDuration = 0.42f;
    private const float FixedStepDt = 1f / 60f;
    private const float PileGravity = 820f;

    private readonly Rng _rng;
    private readonly List<ClamPrize> _prizes = [];
    private readonly TaskCompletionSource<ThingsClamClawOutcome> _completion = new();

    private float _elapsed;
    private int _sweepDir = 1;
    private float _phaseTimer;
    private float _targetDescendY = FloorCraneY;
    private bool _willSlip;
    private float _slipTriggerY;
    private float _accumulator;

    public Player Owner { get; }
    public int MaxGrabs { get; }
    public int GrabsRemaining { get; private set; }
    public int GrabsUsed { get; private set; }
    public int SlippedCount { get; private set; }
    public bool IsComplete { get; private set; }

    public ClamCranePhase Phase { get; private set; } = ClamCranePhase.Aiming;
    public float CraneX { get; private set; } = 140f;
    public float CraneY { get; private set; } = RestCraneY;

    /// <summary>
    /// Normalized opening factor of the Sewer Clam's upper and lower hinged shells:
    /// 0.0 = snapped shut, 1.0 = wide open jaws.
    /// </summary>
    public float ShellOpenAmount { get; private set; } = 0.14f;

    /// <summary>
    /// Subtle horizontal sway angle (radians) of the hanging Sewer Clam.
    /// </summary>
    public float ClamSwayAngle { get; private set; }

    public ClamPrize? HeldPrize { get; private set; }
    public int CollectedGold { get; private set; }
    public int CollectedPotions { get; private set; }
    public int CollectedCards { get; private set; }
    public int CollectedCommonRelics { get; private set; }
    public int CollectedRareRelics { get; private set; }
    public bool SpawnedRareRelic { get; private set; }
    public string LastFeedbackKey { get; private set; } = "THINGS_CLAM_CLAW.ui.statusReady";
    public int LastFeedbackValue { get; private set; }

    public IReadOnlyList<ClamPrize> Prizes => _prizes;
    public Task<ThingsClamClawOutcome> CompletionTask => _completion.Task;

    public event Action? StateChanged;

    public ClamClawMinigame(Player owner, Rng rng, int maxGrabs)
    {
        Owner = owner;
        _rng = rng;
        MaxGrabs = Math.Clamp(maxGrabs, 1, 3);
        GrabsRemaining = MaxGrabs;
        GenerateTankLayout();
    }

    private void GenerateTankLayout()
    {
        _prizes.Clear();

        // Build an abundant, balanced 28-prize Act 1 arcade claw machine mound stacked 3-4 layers high
        // at the bottom of the flooded pit! Lower bed hides 1 mystery cardboard relic box
        // (75% Common / 25% Rare with gold-tape rare markings!), topped by balanced Act 1 loot & barnacle clutter.
        var baseBedKinds = new List<ClamPrizeKind>
        {
            ClamPrizeKind.GoldBig,
            ClamPrizeKind.Card,
            ClamPrizeKind.GoldBig,
            ClamPrizeKind.Potion,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.BarnacleScrap,
        };
        Shuffle(baseBedKinds);

        var moundKinds = new List<ClamPrizeKind>
        {
            ClamPrizeKind.GoldBig,
            ClamPrizeKind.Card,
            ClamPrizeKind.Potion,
            ClamPrizeKind.Potion,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.GoldSmall,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
            ClamPrizeKind.BarnacleScrap,
        };
        Shuffle(moundKinds);

        int nextId = 1;
        // 1. Place 1 mystery cardboard relic box (randomly 75% Common / 25% Rare) buried in the lower bed!
        SpawnedRareRelic = _rng.NextFloat() < 0.25f;
        ClamPrizeKind relicKind = SpawnedRareRelic ? ClamPrizeKind.RareRelic : ClamPrizeKind.CommonRelic;
        float relicSpawnX = _rng.NextInt(3) switch
        {
            0 => _rng.NextFloat(135f, 215f),
            1 => _rng.NextFloat(275f, 355f),
            _ => _rng.NextFloat(415f, 495f),
        };
        _prizes.Add(CreatePrize(nextId++, relicKind, relicSpawnX, 558f, 0));

        // 2. Base floor layer (8 prizes spread tightly across the pit floor)
        for (int i = 0; i < baseBedKinds.Count; i++)
        {
            float sx = 64f + i * 72f + _rng.NextFloat(-16f, 16f);
            float sy = 556f + _rng.NextFloat(-12f, 10f);
            _prizes.Add(CreatePrize(nextId++, baseBedKinds[i], sx, sy, i));
        }

        // 3. Multi-layered upper arcade mound (19 prizes poured into a natural hill crest!)
        for (int i = 0; i < moundKinds.Count; i++)
        {
            int layer = i < 8 ? 0 : (i < 15 ? 1 : 2);
            int idxInLayer = layer == 0 ? i : (layer == 1 ? i - 8 : i - 15);
            int layerCount = layer == 0 ? 8 : (layer == 1 ? 7 : 4);
            float span = layer == 0 ? 504f : (layer == 1 ? 430f : 270f);
            float startX = (TankWidth - span) * 0.5f;
            float stepX = layerCount > 1 ? span / (layerCount - 1) : 0f;
            float sx = startX + idxInLayer * stepX + _rng.NextFloat(-20f, 20f);
            float sy = (layer == 0 ? 488f : (layer == 1 ? 426f : 368f)) + _rng.NextFloat(-14f, 12f);
            _prizes.Add(CreatePrize(nextId++, moundKinds[i], sx, sy, i));
        }

        // Deterministically settle the 28-prize mountain tightly under gravity onto the pit floor!
        for (int tick = 0; tick < 220; tick++)
            StepPilePhysics(FixedStepDt, isInitialSettle: true);

        // Zero residual micro-velocities so the initial mound starts calm and grounded
        foreach (ClamPrize p in _prizes)
        {
            p.Vx = 0f;
            p.Vy = 0f;
        }
    }

    private ClamPrize CreatePrize(int id, ClamPrizeKind kind, float x, float y, int variantSeed)
    {
        (int gold, float bodyR, float coreR, float grazeR, float slipChance) = kind switch
        {
            ClamPrizeKind.RareRelic => (0, 32f, 25f, 41f, 0.58f),
            ClamPrizeKind.CommonRelic => (0, 32f, 26f, 42f, 0.50f),
            ClamPrizeKind.GoldBig => (_rng.NextInt(32, 41), 30f, 28f, 44f, 0.30f),
            ClamPrizeKind.Card => (0, 29f, 28f, 44f, 0.34f),
            ClamPrizeKind.Potion => (0, 29f, 29f, 45f, 0.26f),
            ClamPrizeKind.GoldSmall => (_rng.NextInt(16, 23), 26f, 29f, 44f, 0.20f),
            ClamPrizeKind.BarnacleScrap => (_rng.NextInt(6, 11), 27f, 30f, 45f, 0.14f),
            _ => (0, 28f, 27f, 43f, 0.28f),
        };

        float clampedX = Math.Clamp(x, PileLeftX + bodyR, PileRightX - bodyR);
        return new ClamPrize
        {
            Id = id,
            Kind = kind,
            GoldValue = gold,
            VisualVariant = variantSeed & 1,
            BodyRadius = bodyR,
            X = clampedX,
            Y = Math.Min(y, PileFloorY - bodyR),
            TiltAngle = _rng.NextFloat(-0.28f, 0.28f),
            CoreRadius = coreR,
            GrazeRadius = grazeR,
            SlipChance = slipChance,
        };
    }

    private void StepPilePhysics(float dt, bool isInitialSettle = false)
    {
        // 1. Gravity integration for all prizes resting/tumbling in the pit
        for (int i = 0; i < _prizes.Count; i++)
        {
            ClamPrize p = _prizes[i];
            if (p.State != ClamPrizeState.InTank && p.State != ClamPrizeState.FallingSlip)
                continue;

            p.Vy += PileGravity * dt;
            p.Vx *= isInitialSettle ? 0.90f : 0.93f;
            p.Vy *= isInitialSettle ? 0.96f : 0.985f;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;

            // Gentle natural rolling tilt when sliding laterally
            if (MathF.Abs(p.Vx) > 10f)
                p.TiltAngle = Math.Clamp(p.TiltAngle + p.Vx * dt * 0.006f, -0.38f, 0.38f);
        }

        // 2. Iterative circle-to-circle stacking + pit wall & floor containment
        int iterations = isInitialSettle ? 5 : 4;
        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < _prizes.Count; i++)
            {
                ClamPrize a = _prizes[i];
                if (a.State != ClamPrizeState.InTank && a.State != ClamPrizeState.FallingSlip)
                    continue;

                for (int j = i + 1; j < _prizes.Count; j++)
                {
                    ClamPrize b = _prizes[j];
                    if (b.State != ClamPrizeState.InTank && b.State != ClamPrizeState.FallingSlip)
                        continue;

                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    // Allow snug 0.90 overlap factor so sprites visually nestle tightly in a real claw pile
                    float minDist = (a.BodyRadius + b.BodyRadius) * 0.92f;
                    float distSq = dx * dx + dy * dy;
                    if (distSq >= minDist * minDist)
                        continue;

                    float dist = MathF.Sqrt(distSq);
                    float nx;
                    float ny;
                    if (dist < 0.001f)
                    {
                        nx = ((a.Id + b.Id) % 2 == 0) ? 0.6f : -0.6f;
                        ny = -0.8f;
                        dist = 0.001f;
                    }
                    else
                    {
                        nx = dx / dist;
                        ny = dy / dist;
                    }

                    float overlap = (minDist - dist) * 0.52f;
                    // Items lower in the pile carry slightly higher mass/inertia so upper items tumble cleanly over them
                    float aWeight = a.Y > b.Y ? 0.35f : 0.65f;
                    float bWeight = 1f - aWeight;

                    a.X -= nx * overlap * aWeight * 2f;
                    a.Y -= ny * overlap * aWeight * 2f;
                    b.X += nx * overlap * bWeight * 2f;
                    b.Y += ny * overlap * bWeight * 2f;

                    // Dampen relative velocity along collision normal
                    float rvx = b.Vx - a.Vx;
                    float rvy = b.Vy - a.Vy;
                    float vn = rvx * nx + rvy * ny;
                    if (vn < 0f)
                    {
                        float impulse = -vn * 0.55f;
                        a.Vx -= nx * impulse * aWeight;
                        a.Vy -= ny * impulse * aWeight;
                        b.Vx += nx * impulse * bWeight;
                        b.Vy += ny * impulse * bWeight;
                    }
                }
            }

            // Constrain against tank floor & side walls
            for (int i = 0; i < _prizes.Count; i++)
            {
                ClamPrize p = _prizes[i];
                if (p.State != ClamPrizeState.InTank && p.State != ClamPrizeState.FallingSlip)
                    continue;

                float minX = PileLeftX + p.BodyRadius;
                float maxX = PileRightX - p.BodyRadius;
                float maxY = PileFloorY - p.BodyRadius;

                if (p.X < minX)
                {
                    p.X = minX;
                    if (p.Vx < 0f) p.Vx = -p.Vx * 0.25f;
                }
                else if (p.X > maxX)
                {
                    p.X = maxX;
                    if (p.Vx > 0f) p.Vx = -p.Vx * 0.25f;
                }

                if (p.Y > maxY)
                {
                    p.Y = maxY;
                    if (p.Vy > 0f) p.Vy = 0f;
                    p.Vx *= 0.82f;
                }
            }
        }

        // 3. Settle FallingSlip prizes back into InTank once they land on the floor or pile
        for (int i = 0; i < _prizes.Count; i++)
        {
            ClamPrize p = _prizes[i];
            if (p.State == ClamPrizeState.FallingSlip && p.Y >= 380f && MathF.Abs(p.Vy) < 42f)
            {
                p.State = ClamPrizeState.InTank;
            }
            else if (p.State == ClamPrizeState.InTank && MathF.Abs(p.Vx) < 4f && MathF.Abs(p.Vy) < 12f)
            {
                p.Vx = 0f;
            }
        }
    }

    /// <summary>
    /// Returns the topmost prize currently intersecting the claw's vertical drop column (used for aim highlight).
    /// </summary>
    public ClamPrize? PeekAlignedPrize()
    {
        ClamPrize? best = null;
        foreach (ClamPrize p in _prizes)
        {
            if (p.State != ClamPrizeState.InTank)
                continue;
            if (MathF.Abs(p.X - CraneX) <= p.GrazeRadius)
            {
                if (best == null || p.Y < best.Y)
                    best = p;
            }
        }
        return best;
    }

    public bool CanDrop() => !IsComplete && Phase == ClamCranePhase.Aiming && GrabsRemaining > 0;

    public bool TryDropClaw()
    {
        if (!CanDrop())
            return false;

        GrabsRemaining--;
        GrabsUsed++;
        Phase = ClamCranePhase.Descending;
        _phaseTimer = 0f;
        HeldPrize = null;
        _willSlip = false;
        _targetDescendY = FloorCraneY;
        LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusDropping";
        LastFeedbackValue = 0;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Deterministic test/accessibility helper: positions the hoist at x and drops the Sewer Clam immediately.
    /// </summary>
    public bool DropAtX(float x)
    {
        if (!CanDrop())
            return false;
        CraneX = Math.Clamp(x, MinCraneX, MaxCraneX);
        return TryDropClaw();
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

    /// <summary>
    /// Steps the crane and any falling/tumbling prizes until the crane returns to Aiming or Done.
    /// </summary>
    public void StepUntilIdle(int maxTicks = 1200)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            StepFixed(FixedStepDt);
            bool anyFalling = _prizes.Any(p => p.State == ClamPrizeState.FallingSlip);
            if ((Phase == ClamCranePhase.Aiming || Phase == ClamCranePhase.Done) && !anyFalling)
                break;
        }
    }

    private void StepFixed(float dt)
    {
        _elapsed += dt;

        // 1. Live 2D gravity pile physics so remaining prizes naturally tumble & re-settle when one is lifted!
        StepPilePhysics(dt, isInitialSettle: false);

        // 2. Step the Sewer Clam crane state machine
        switch (Phase)
        {
            case ClamCranePhase.Aiming:
            {
                float edgeRatio = MathF.Min(
                    (CraneX - MinCraneX) / 56f,
                    (MaxCraneX - CraneX) / 56f);
                float speedScale = 0.45f + 0.55f * Math.Clamp(edgeRatio, 0f, 1f);
                CraneX += _sweepDir * SweepSpeed * speedScale * dt;
                if (CraneX >= MaxCraneX)
                {
                    CraneX = MaxCraneX;
                    _sweepDir = -1;
                }
                else if (CraneX <= MinCraneX)
                {
                    CraneX = MinCraneX;
                    _sweepDir = 1;
                }
                CraneY = RestCraneY;
                ShellOpenAmount = MoveTowards(ShellOpenAmount, 0.16f + 0.05f * MathF.Sin(_elapsed * 3.0f), dt * 2.4f);
                ClamSwayAngle = -_sweepDir * 0.08f * speedScale + MathF.Sin(_elapsed * 2.6f) * 0.03f;
                break;
            }

            case ClamCranePhase.Descending:
            {
                CraneY += DescendSpeed * dt;
                ShellOpenAmount = MoveTowards(ShellOpenAmount, 1.0f, dt * 3.6f);
                ClamSwayAngle = MoveTowards(ClamSwayAngle, 0f, dt * 2.0f);

                // Check contact against topmost intersecting prize at jaw depth (CraneY + 24)
                ClamPrize? contact = null;
                float jawY = CraneY + 24f;
                foreach (ClamPrize p in _prizes)
                {
                    if (p.State != ClamPrizeState.InTank)
                        continue;
                    if (MathF.Abs(p.X - CraneX) <= p.GrazeRadius && jawY >= p.Y - 10f)
                    {
                        if (contact == null || p.Y < contact.Y)
                            contact = p;
                    }
                }

                if (contact != null || CraneY >= _targetDescendY)
                {
                    Phase = ClamCranePhase.Clamping;
                    _phaseTimer = 0f;
                    if (contact != null)
                    {
                        float offset = MathF.Abs(contact.X - CraneX);
                        HeldPrize = contact;
                        contact.State = ClamPrizeState.InShell;
                        contact.Vx = 0f;
                        contact.Vy = 0f;
                        if (offset <= contact.CoreRadius)
                        {
                            _willSlip = false;
                            LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusClampedTight";
                        }
                        else
                        {
                            _willSlip = _rng.NextFloat() < contact.SlipChance;
                            _slipTriggerY = MathF.Max(RestCraneY + 95f, CraneY - _rng.NextFloat(115f, 185f));
                            LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusClampedEdge";
                        }
                    }
                    else
                    {
                        CraneY = _targetDescendY;
                        HeldPrize = null;
                        _willSlip = false;
                        LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusMiss";
                    }
                    StateChanged?.Invoke();
                }
                break;
            }

            case ClamCranePhase.Clamping:
            {
                _phaseTimer += dt;
                float t = Math.Clamp(_phaseTimer / ClampDuration, 0f, 1f);
                // Upper and lower shells snap shut around the caught item, pulling it right inside the shell!
                ShellOpenAmount = MathF.Max(HeldPrize != null ? 0.08f : 0.0f, 1.0f - t * 1.15f);
                if (HeldPrize != null)
                {
                    HeldPrize.X = Lerp(HeldPrize.X, CraneX, Math.Min(1f, dt * 14f));
                    HeldPrize.Y = Lerp(HeldPrize.Y, CraneY + 14f, Math.Min(1f, dt * 14f));
                    HeldPrize.TiltAngle = Lerp(HeldPrize.TiltAngle, 0f, Math.Min(1f, dt * 12f));
                }
                if (_phaseTimer >= ClampDuration)
                {
                    Phase = ClamCranePhase.Ascending;
                    _phaseTimer = 0f;
                    StateChanged?.Invoke();
                }
                break;
            }

            case ClamCranePhase.Ascending:
            {
                CraneY -= AscendSpeed * dt;
                if (HeldPrize != null)
                {
                    HeldPrize.X = CraneX;
                    HeldPrize.Y = CraneY + 14f;
                    HeldPrize.TiltAngle = ClamSwayAngle * 0.5f;
                    ClamSwayAngle = _willSlip ? MathF.Sin(_elapsed * 28f) * 0.14f : MathF.Sin(_elapsed * 5f) * 0.03f;

                    if (_willSlip && CraneY <= _slipTriggerY)
                    {
                        // Edge graze slips out halfway up and tumbles back onto the bottom prize heap!
                        ClamPrize slipped = HeldPrize;
                        HeldPrize = null;
                        _willSlip = false;
                        SlippedCount++;
                        slipped.State = ClamPrizeState.FallingSlip;
                        slipped.Vx = _rng.NextFloat(-40f, 40f);
                        slipped.Vy = 65f;
                        ShellOpenAmount = 0.42f;
                        LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusSlipped";
                        LastFeedbackValue = 0;
                        StateChanged?.Invoke();
                    }
                }
                else
                {
                    ShellOpenAmount = MoveTowards(ShellOpenAmount, 0.06f, dt * 2.5f);
                    ClamSwayAngle = MoveTowards(ClamSwayAngle, 0f, dt * 2.5f);
                }

                if (CraneY <= RestCraneY)
                {
                    CraneY = RestCraneY;
                    Phase = ClamCranePhase.Depositing;
                    _phaseTimer = 0f;
                    if (HeldPrize != null)
                    {
                        CollectPrize(HeldPrize);
                        HeldPrize = null;
                    }
                    StateChanged?.Invoke();
                }
                break;
            }

            case ClamCranePhase.Depositing:
            {
                _phaseTimer += dt;
                ShellOpenAmount = _phaseTimer < DepositDuration * 0.55f
                    ? MoveTowards(ShellOpenAmount, 0.85f, dt * 4.5f)
                    : MoveTowards(ShellOpenAmount, 0.14f, dt * 3.5f);
                ClamSwayAngle = MoveTowards(ClamSwayAngle, 0f, dt * 4.0f);

                if (_phaseTimer >= DepositDuration)
                {
                    bool anyLeft = _prizes.Any(p => p.State == ClamPrizeState.InTank || p.State == ClamPrizeState.FallingSlip);
                    if (GrabsRemaining <= 0 || !anyLeft)
                    {
                        Phase = ClamCranePhase.Done;
                        IsComplete = true;
                    }
                    else
                    {
                        Phase = ClamCranePhase.Aiming;
                    }
                    StateChanged?.Invoke();
                }
                break;
            }

            case ClamCranePhase.Done:
            {
                ShellOpenAmount = MoveTowards(ShellOpenAmount, 0.12f, dt * 2.0f);
                ClamSwayAngle = MoveTowards(ClamSwayAngle, 0f, dt * 3.0f);
                break;
            }
        }
    }

    private void CollectPrize(ClamPrize prize)
    {
        prize.State = ClamPrizeState.Collected;
        switch (prize.Kind)
        {
            case ClamPrizeKind.GoldSmall:
            case ClamPrizeKind.GoldBig:
                CollectedGold += prize.GoldValue;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonGold";
                LastFeedbackValue = prize.GoldValue;
                break;
            case ClamPrizeKind.BarnacleScrap:
                CollectedGold += prize.GoldValue;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonScrap";
                LastFeedbackValue = prize.GoldValue;
                break;
            case ClamPrizeKind.Potion:
                CollectedPotions++;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonPotion";
                LastFeedbackValue = 0;
                break;
            case ClamPrizeKind.Card:
                CollectedCards++;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonCard";
                LastFeedbackValue = 0;
                break;
            case ClamPrizeKind.CommonRelic:
                CollectedCommonRelics++;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonRelic";
                LastFeedbackValue = 0;
                break;
            case ClamPrizeKind.RareRelic:
                CollectedRareRelics++;
                LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusWonRareRelic";
                LastFeedbackValue = 0;
                break;
        }
    }

    public void FinishEarly()
    {
        if (IsComplete)
            return;
        if (Phase == ClamCranePhase.Descending || Phase == ClamCranePhase.Clamping || Phase == ClamCranePhase.Ascending || Phase == ClamCranePhase.Depositing)
            StepUntilIdle();
        IsComplete = true;
        Phase = ClamCranePhase.Done;
        LastFeedbackKey = "THINGS_CLAM_CLAW.ui.statusFinished";
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
        _completion.TrySetResult(BuildOutcome());
    }

    public ThingsClamClawOutcome BuildOutcome() => new(
        CollectedGold,
        CollectedPotions,
        CollectedCards,
        CollectedCommonRelics,
        CollectedRareRelics,
        GrabsUsed,
        SlippedCount);

    private void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = _rng.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
