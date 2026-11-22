using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using STS2_Things.Platform;

namespace STS2_Things.Events.Minigames;

public enum CauldronTreasureKind
{
    Gold = 0,
    Potion = 1,
    Card = 2,
    CommonRelic = 3,
    UncommonRelic = 4,
    RareRelic = 5,
    MaxHp = 6,
    UpgradeCard = 7,
}

public sealed class CauldronTreasureItem
{
    public int Index { get; init; }
    public CauldronTreasureKind Kind { get; init; }
    public int Value { get; init; }
    public int VisualVariant { get; init; }
    public float TiltAngle { get; init; }
    public float OrbitRadius { get; init; }
    public float BaseAngle { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Radius { get; init; }
    public bool IsClaimed { get; set; }
    public ulong ClaimedByNetId { get; set; }
    public float ClaimAnimTimer { get; set; }
}

public sealed class CauldronHandState
{
    public required Player Player { get; init; }
    public int SlotIndex { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public bool IsClenched { get; set; }
    public float ClenchTimer { get; set; }
    public float CooldownTimer { get; set; }
    public bool IsFalseStartPenalty { get; set; }
    public int GrabsRemaining { get; set; }
    public int ItemsGrabbed { get; set; }
    public float LastBurstTimer { get; set; }
    public float LastBurstX { get; set; }
    public float LastBurstY { get; set; }
    public bool LastBurstHit { get; set; }
}

public sealed record ThingsBronzeCauldronPlayerOutcome(
    ulong PlayerNetId,
    int GoldAmount,
    int MaxHpGain,
    int UpgradeCount,
    int PotionCount,
    int CardCount,
    int CommonRelicCount,
    int UncommonRelicCount,
    int RareRelicCount,
    int ItemsGrabbed)
{
    public bool HasAnyLoot =>
        GoldAmount > 0 || MaxHpGain > 0 || UpgradeCount > 0 ||
        PotionCount > 0 || CardCount > 0 || CommonRelicCount > 0 ||
        UncommonRelicCount > 0 || RareRelicCount > 0;
}

public enum CauldronPhase
{
    Countdown = 0,
    Scramble = 1,
    Complete = 2,
}

/// <summary>
/// Shared multiplayer real-time state &amp; host-authoritative conflict resolver for the Depths
/// Bronze Tripod Scramble ("铜鼎争宝 / Bronze Tripod Scramble") minigame.
/// Players maneuver open hands (Paper) over the cauldron during a 3-2-1 countdown, then click to
/// slam shut into fists (Rock) and snatch all unclaimed treasures covered by their palm radius.
/// </summary>
public sealed class BronzeCauldronMinigame
{
    public const float BoardSize = 720f;
    public const float Center = BoardSize * 0.5f; // 360f
    public const float CauldronInnerRadius = 322f;
    public const float PalmCaptureRadius = 64f;
    public const float StageOriginX = 400f;
    public const float StageOriginY = 180f;

    /// <summary>
    /// Rapid clockwise angular velocity (rad/s) for all unclaimed cauldron treasures.
    /// In Godot 2D (+Y downward), increasing angle rotates strictly clockwise (~106°/s, 1 full rev every 3.4s).
    /// </summary>
    public const float OrbitAngularSpeed = 1.85f;

    public const int GrabsPerPlayer = 3;
    public const float CountdownDuration = 3.0f;
    public const float ScrambleDuration = 12.0f;
    public const float ClenchVisualDuration = 0.36f;
    public const float GrabCooldownDuration = 0.68f;
    public const float FalseStartLockDuration = 0.65f;

    public const int ShareCommonRelicFallbackGold = 45;
    public const int ShareGoldAmount = 20;
    public const int RelicFallbackGoldCommon = 45;
    public const int RelicFallbackGoldUncommon = 60;
    public const int RelicFallbackGoldRare = 80;
    public const int UpgradeFallbackGold = 45;
    public const int MaxHpBeadAmount = 3;
    public const int SmallCoinMinGold = 3;
    public const int SmallCoinMaxGold = 10;

    private readonly Rng _rng;
    private readonly List<CauldronTreasureItem> _items = [];
    private readonly Dictionary<ulong, CauldronHandState> _hands = [];
    private readonly List<CauldronHandState> _orderedHands = [];
    private readonly Dictionary<ulong, TaskCompletionSource<ThingsBronzeCauldronPlayerOutcome>> _playerOutcomes = [];
    private readonly TaskCompletionSource<bool> _uiDismissed = new();

    public bool IsHostAuthority { get; }
    public ulong LocalPlayerNetId { get; }
    public CauldronPhase Phase { get; private set; } = CauldronPhase.Countdown;
    public float CountdownRemaining { get; private set; } = CountdownDuration;
    public float ScrambleRemaining { get; private set; } = ScrambleDuration;
    public float OrbitAngle { get; private set; }
    public bool IsComplete => Phase == CauldronPhase.Complete;
    public bool HasFinalizedOutcomes { get; private set; }

    public string LastFeedbackKey { get; private set; } = "THINGS_BRONZE_CAULDRON.ui.statusCountdown";
    public string LastFeedbackActor { get; private set; } = string.Empty;
    public int LastFeedbackValue { get; private set; }

    public IReadOnlyList<CauldronTreasureItem> Items => _items;
    public IReadOnlyList<CauldronHandState> Hands => _orderedHands;
    public Task UiDismissedTask => _uiDismissed.Task;

    public event Action? StateChanged;
    public event Action<ulong, float, float, byte[], int>? HostGrabResolved;
    public event Action<IReadOnlyList<ThingsBronzeCauldronPlayerOutcome>>? HostFinalizedOutcomes;

    public BronzeCauldronMinigame(
        IReadOnlyList<Player> players,
        ulong localPlayerNetId,
        bool isHostAuthority,
        uint sharedSeed)
    {
        LocalPlayerNetId = localPlayerNetId;
        IsHostAuthority = isHostAuthority;
        _rng = new Rng(sharedSeed, "bronze_cauldron");

        for (int i = 0; i < players.Count; i++)
        {
            Player p = players[i];
            (float sx, float sy) = GetStartingHandPos(i, players.Count);
            var hand = new CauldronHandState
            {
                Player = p,
                SlotIndex = i,
                X = sx,
                Y = sy,
                TargetX = sx,
                TargetY = sy,
                GrabsRemaining = GrabsPerPlayer,
            };
            _hands[p.NetId] = hand;
            _orderedHands.Add(hand);
            _playerOutcomes[p.NetId] = new TaskCompletionSource<ThingsBronzeCauldronPlayerOutcome>();
        }

        GenerateCauldronTreasures(players.Count);
    }

    /// <summary>
    /// Resting screen-edge entry positions (matching vanilla Treasure Room NHandImage off-screen arms!):
    /// hands start anchored outside the giant cauldron near the screen borders and glide inward on hover.
    /// </summary>
    public static (float X, float Y) GetStartingHandPos(int slot) =>
        GetStartingHandPos(slot, MultiplayerCompat.VanillaMaxPlayers);

    /// <summary>
    /// Resting screen-edge entry positions for 1–4 players (exact Bottom/Top/Left/Right vanilla anchors)
    /// and 5–32+ players (evenly distributed around a 360° perimeter ring outside the cauldron, starting at Bottom).
    /// </summary>
    public static (float X, float Y) GetStartingHandPos(int slot, int totalPlayers)
    {
        if (totalPlayers <= MultiplayerCompat.VanillaMaxPlayers)
        {
            return (slot % 4) switch
            {
                0 => (Center, BoardSize + 105f), // Bottom screen edge (screen y ~ 1005)
                1 => (Center, -105f),            // Top screen edge    (screen y ~ 75)
                2 => (-240f, Center),            // Left screen edge   (screen x ~ 160)
                _ => (BoardSize + 240f, Center), // Right screen edge  (screen x ~ 1360)
            };
        }

        int safeTotal = Math.Max(1, totalPlayers);
        int wrappedSlot = ((slot % safeTotal) + safeTotal) % safeTotal;
        float angle = MathF.PI * 0.5f + wrappedSlot * (MathF.Tau / safeTotal);
        const float ringRadiusX = 560f;
        const float ringRadiusY = 465f;
        return (Center + MathF.Cos(angle) * ringRadiusX, Center + MathF.Sin(angle) * ringRadiusY);
    }

    private void GenerateCauldronTreasures(int playerCount)
    {
        _items.Clear();
        bool extendedParty = MultiplayerCompat.IsExtendedParty(playerCount);

        // "多但少，空处铺满小额金币（3-10 金）" + 遗物全为原版占卜纸盒子且在稀有/普通间随机！
        // All Relic boxes & featured items orbit well clear of the dead center (r >= 92px) so rapid clockwise rotation makes sniping exciting!
        var featuredSpecs = new List<(CauldronTreasureKind Kind, int Value, float Radius, float MinR, float MaxR)>();
        float featuredGap;
        int targetTotalItems;
        bool denseCoinPack;

        if (!extendedParty)
        {
            // Preserve 100% bit-for-bit identical 1–4 player layout, counts, and RNG draw sequence.
            int n = Math.Clamp(playerCount, 2, 4);
            bool firstIsRare = _rng.NextFloat() < 0.50f;
            featuredSpecs.Add((firstIsRare ? CauldronTreasureKind.RareRelic : CauldronTreasureKind.CommonRelic, 1, 31f, 92f, 148f));
            for (int i = 1; i < n; i++)
            {
                bool isRare = _rng.NextFloat() < 0.25f;
                featuredSpecs.Add((isRare ? CauldronTreasureKind.RareRelic : CauldronTreasureKind.CommonRelic, 1, 30f, 165f, 248f));
            }

            // 2. Modest special treasures (balanced "多但少": small +3 Max HP hearts, 1-2 upgrades, 2-3 potions & scrolls)
            int upgrades = n >= 4 ? 2 : 1;
            for (int i = 0; i < upgrades; i++)
                featuredSpecs.Add((CauldronTreasureKind.UpgradeCard, 1, 25f, 108f, 235f));

            featuredSpecs.Add((CauldronTreasureKind.MaxHp, MaxHpBeadAmount, 24f, 102f, 224f));
            featuredSpecs.Add((CauldronTreasureKind.MaxHp, MaxHpBeadAmount, 24f, 142f, 260f));

            int potions = n >= 3 ? 3 : 2;
            for (int i = 0; i < potions; i++)
                featuredSpecs.Add((CauldronTreasureKind.Potion, 1, 24f, 112f, 265f));

            int cards = n >= 3 ? 3 : 2;
            for (int i = 0; i < cards; i++)
                featuredSpecs.Add((CauldronTreasureKind.Card, 1, 25f, 120f, 268f));

            featuredGap = 26f;
            targetTotalItems = 60;
            denseCoinPack = false;
        }
        else
        {
            // Smoothly scale contestable loot for 5–32 player lobbies while keeping the 644px basin readable.
            bool tightLayout = playerCount >= 12;
            int relicCount = Math.Clamp(playerCount, 4, 14);
            float innerRelicR = tightLayout ? 27f : 30f;
            float outerRelicR = tightLayout ? 26f : 28f;

            bool firstIsRare = _rng.NextFloat() < 0.50f;
            featuredSpecs.Add((firstIsRare ? CauldronTreasureKind.RareRelic : CauldronTreasureKind.CommonRelic, 1, innerRelicR, 92f, 148f));
            for (int i = 1; i < relicCount; i++)
            {
                bool isRare = _rng.NextFloat() < 0.25f;
                featuredSpecs.Add((isRare ? CauldronTreasureKind.RareRelic : CauldronTreasureKind.CommonRelic, 1, outerRelicR, 152f, 258f));
            }

            int upgrades = Math.Clamp(playerCount / 2, 2, 6);
            float upgradeR = tightLayout ? 22f : 24f;
            for (int i = 0; i < upgrades; i++)
                featuredSpecs.Add((CauldronTreasureKind.UpgradeCard, 1, upgradeR, 104f, 240f));

            int maxHpHearts = Math.Clamp(2 + (playerCount - 4) / 3, 2, 8);
            float heartR = tightLayout ? 21f : 23f;
            for (int i = 0; i < maxHpHearts; i++)
                featuredSpecs.Add((CauldronTreasureKind.MaxHp, MaxHpBeadAmount, heartR, 98f, 260f));

            int potions = Math.Clamp(3 + (playerCount - 4) / 2, 3, 10);
            float potionR = tightLayout ? 21f : 23f;
            for (int i = 0; i < potions; i++)
                featuredSpecs.Add((CauldronTreasureKind.Potion, 1, potionR, 108f, 268f));

            int cards = Math.Clamp(3 + (playerCount - 4) / 2, 3, 10);
            float cardR = tightLayout ? 22f : 24f;
            for (int i = 0; i < cards; i++)
                featuredSpecs.Add((CauldronTreasureKind.Card, 1, cardR, 114f, 270f));

            featuredGap = playerCount >= 17 ? 6f : (playerCount >= 9 ? 11f : 18f);
            targetTotalItems = Math.Clamp(60 + (playerCount - 4) * 4, 60, 140);
            denseCoinPack = playerCount > 8;
        }

        int nextIdx = 0;
        foreach (var spec in featuredSpecs)
        {
            (float ix, float iy) = FindNonOverlappingPosition(spec.Radius, spec.MinR, spec.MaxR, extraGap: featuredGap, attempts: 180);
            float dx = ix - Center;
            float dy = iy - Center;
            _items.Add(new CauldronTreasureItem
            {
                Index = nextIdx++,
                Kind = spec.Kind,
                Value = spec.Value,
                VisualVariant = nextIdx & 1,
                TiltAngle = (spec.Kind == CauldronTreasureKind.RareRelic || spec.Kind == CauldronTreasureKind.CommonRelic)
                    ? _rng.NextFloat(-0.10f, 0.10f)
                    : _rng.NextFloat(-0.22f, 0.22f),
                OrbitRadius = MathF.Sqrt(dx * dx + dy * dy),
                BaseAngle = MathF.Atan2(dy, dx),
                X = ix,
                Y = iy,
                Radius = spec.Radius,
            });
        }

        // 3. Carpet EVERY remaining open space inside the giant 644px bronze basin with gleaming 3-10G coin units!
        //    1–4p packs 60 total items (identical to original); 5–32p scales up to 140 items (synced via byte[] indices).
        while (_items.Count < targetTotalItems)
        {
            int coinVal = _rng.NextInt(SmallCoinMinGold, SmallCoinMaxGold + 1); // 3..10G per coin unit
            float coinRadius = denseCoinPack
                ? (coinVal >= 7 ? 17f : 15f)
                : (coinVal >= 7 ? 20f : 17.5f);
            if (!TryFindOpenCoinSlot(coinRadius, denseCoinPack, out float cx, out float cy))
                break;

            float dx = cx - Center;
            float dy = cy - Center;
            _items.Add(new CauldronTreasureItem
            {
                Index = nextIdx++,
                Kind = CauldronTreasureKind.Gold,
                Value = coinVal,
                VisualVariant = coinVal >= 7 ? 1 : 0, // 0 = single gold coin (3-6G), 1 = small 3-coin stack (7-10G)
                TiltAngle = _rng.NextFloat(-0.32f, 0.32f),
                OrbitRadius = MathF.Sqrt(dx * dx + dy * dy),
                BaseAngle = MathF.Atan2(dy, dx),
                X = cx,
                Y = cy,
                Radius = coinRadius,
            });
        }
    }

    private (float X, float Y) FindNonOverlappingPosition(float radius, float minR, float maxR, float extraGap, int attempts)
    {
        float bestX = Center;
        float bestY = Center;
        float bestClearance = -9999f;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float angle = _rng.NextFloat(0f, MathF.Tau);
            float dist = minR + MathF.Sqrt(_rng.NextFloat(0f, 1f)) * MathF.Max(1f, maxR - minR);
            float cx = Center + MathF.Cos(angle) * dist;
            float cy = Center + MathF.Sin(angle) * dist;

            float minGap = float.MaxValue;
            foreach (CauldronTreasureItem existing in _items)
            {
                float d = MathF.Sqrt((cx - existing.X) * (cx - existing.X) + (cy - existing.Y) * (cy - existing.Y));
                float gap = d - (radius + existing.Radius + extraGap);
                if (gap < minGap)
                    minGap = gap;
            }

            if (minGap >= 0f)
                return (cx, cy);
            if (minGap > bestClearance)
            {
                bestClearance = minGap;
                bestX = cx;
                bestY = cy;
            }
        }

        return (bestX, bestY);
    }

    private bool TryFindOpenCoinSlot(float radius, bool densePack, out float outX, out float outY)
    {
        float maxDist = CauldronInnerRadius - radius - (densePack ? 10f : 14f);
        float bestX = Center;
        float bestY = Center;
        float bestGap = -9999f;
        int attempts = densePack ? 120 : 96;
        float goldPad = densePack ? 2f : 5f;
        float featuredPad = densePack ? 4f : 9f;
        float minAcceptGap = densePack ? -4f : -2f;

        // Pick the most open remaining pocket across the cauldron floor so coins evenly carpet every empty gap!
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float angle = _rng.NextFloat(0f, MathF.Tau);
            float dist = MathF.Sqrt(_rng.NextFloat(0f, 1f)) * maxDist;
            float cx = Center + MathF.Cos(angle) * dist;
            float cy = Center + MathF.Sin(angle) * dist;

            float minGap = float.MaxValue;
            foreach (CauldronTreasureItem existing in _items)
            {
                float d = MathF.Sqrt((cx - existing.X) * (cx - existing.X) + (cy - existing.Y) * (cy - existing.Y));
                float pad = existing.Kind == CauldronTreasureKind.Gold ? goldPad : featuredPad;
                float gap = d - (radius + existing.Radius + pad);
                if (gap < minGap)
                    minGap = gap;
            }

            if (minGap > bestGap)
            {
                bestGap = minGap;
                bestX = cx;
                bestY = cy;
            }
        }

        if (bestGap >= minAcceptGap)
        {
            outX = bestX;
            outY = bestY;
            return true;
        }

        outX = 0f;
        outY = 0f;
        return false;
    }

    public CauldronHandState? GetHand(ulong playerNetId) =>
        _hands.TryGetValue(playerNetId, out CauldronHandState? h) ? h : null;

    public CauldronHandState? LocalHand => GetHand(LocalPlayerNetId);

    public Task<ThingsBronzeCauldronPlayerOutcome> WaitForPlayerOutcomeAsync(ulong playerNetId)
    {
        if (_playerOutcomes.TryGetValue(playerNetId, out var tcs))
            return tcs.Task;
        return Task.FromResult(BuildPlayerOutcome(playerNetId));
    }

    public void UpdateLocalHandPosition(float x, float y)
    {
        if (!_hands.TryGetValue(LocalPlayerNetId, out CauldronHandState? hand))
            return;
        hand.X = Math.Clamp(x, -340f, BoardSize + 340f);
        hand.Y = Math.Clamp(y, -150f, BoardSize + 150f);
        hand.TargetX = hand.X;
        hand.TargetY = hand.Y;
    }

    public void ApplyRemoteHandPosition(ulong playerNetId, float x, float y, bool isClenched)
    {
        if (playerNetId == LocalPlayerNetId)
            return;
        if (!_hands.TryGetValue(playerNetId, out CauldronHandState? hand))
            return;
        hand.TargetX = Math.Clamp(x, -340f, BoardSize + 340f);
        hand.TargetY = Math.Clamp(y, -150f, BoardSize + 150f);
        if (isClenched && !hand.IsClenched)
        {
            hand.IsClenched = true;
            hand.ClenchTimer = ClenchVisualDuration;
        }
    }

    public List<CauldronTreasureItem> GetItemsCoveredByPalm(float palmX, float palmY)
    {
        var covered = new List<CauldronTreasureItem>();
        foreach (CauldronTreasureItem item in _items)
        {
            if (item.IsClaimed)
                continue;
            float dx = item.X - palmX;
            float dy = item.Y - palmY;
            float reach = PalmCaptureRadius + item.Radius * 0.50f;
            if (dx * dx + dy * dy <= reach * reach)
                covered.Add(item);
        }
        return covered;
    }

    /// <summary>
    /// Attempts a grab for the local player at their current palm position.
    /// Returns true if a live grab request should be forwarded to Host (when running as Client).
    /// </summary>
    public bool TryLocalGrab(out float grabX, out float grabY, out bool sendToHost)
    {
        grabX = Center;
        grabY = Center;
        sendToHost = false;

        if (!_hands.TryGetValue(LocalPlayerNetId, out CauldronHandState? hand))
            return false;
        // Left-clicks before the opening 3-2-1 countdown finishes must NOT trigger any grab or penalty!
        if (Phase != CauldronPhase.Scramble || hand.CooldownTimer > 0f || hand.GrabsRemaining <= 0)
            return false;

        grabX = hand.X;
        grabY = hand.Y;

        if (IsHostAuthority)
        {
            ResolveAuthoritativeGrabIndices(LocalPlayerNetId, grabX, grabY);
            return true;
        }

        // Client: play crisp local fist slam immediately & forward authoritative request to Host
        hand.IsClenched = true;
        hand.ClenchTimer = ClenchVisualDuration;
        hand.CooldownTimer = GrabCooldownDuration;
        hand.IsFalseStartPenalty = false;
        sendToHost = true;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Host-only authoritative grab resolution returning all claimed item indices (0..255)
    /// in strict arrival order so two players can NEVER duplicate the same treasure, even with
    /// 65–140 scaled items in 5–32 player lobbies.
    /// </summary>
    public byte[] ResolveAuthoritativeGrabIndices(ulong playerNetId, float x, float y)
    {
        if (!IsHostAuthority || Phase != CauldronPhase.Scramble)
            return [];
        if (!_hands.TryGetValue(playerNetId, out CauldronHandState? hand))
            return [];
        if (hand.GrabsRemaining <= 0)
            return [];

        float cx = Math.Clamp(x, -120f, BoardSize + 120f);
        float cy = Math.Clamp(y, -120f, BoardSize + 120f);
        List<CauldronTreasureItem> hits = GetItemsCoveredByPalm(cx, cy);
        var claimed = new List<byte>(Math.Min(hits.Count, ThingsCauldronGrabAckMessage.MaxClaimedIndicesPerGrab));
        foreach (CauldronTreasureItem item in hits)
        {
            if (item.Index is >= 0 and <= byte.MaxValue &&
                claimed.Count < ThingsCauldronGrabAckMessage.MaxClaimedIndicesPerGrab)
            {
                claimed.Add((byte)item.Index);
            }
        }

        byte[] claimedIndices = claimed.ToArray();
        int nextRemaining = Math.Max(0, hand.GrabsRemaining - 1);
        ApplyResolvedGrab(playerNetId, cx, cy, claimedIndices, nextRemaining);
        HostGrabResolved?.Invoke(playerNetId, cx, cy, claimedIndices, nextRemaining);

        CheckScrambleCompletion();
        return claimedIndices;
    }

    /// <summary>
    /// Compatibility wrapper for callers/probes expecting a 64-bit bitmask over item indices 0..63.
    /// Still resolves and broadcasts ALL claimed items (including indices &gt;= 64).
    /// </summary>
    public ulong ResolveAuthoritativeGrab(ulong playerNetId, float x, float y)
    {
        byte[] claimedIndices = ResolveAuthoritativeGrabIndices(playerNetId, x, y);
        ulong bitmask = 0UL;
        for (int i = 0; i < claimedIndices.Length; i++)
        {
            if (claimedIndices[i] < 64)
                bitmask |= 1UL << claimedIndices[i];
        }
        return bitmask;
    }

    public void ApplyResolvedGrab(ulong playerNetId, float x, float y, ulong claimedBitmask, int grabsRemaining)
    {
        var indices = new List<byte>();
        for (int bit = 0; bit < 64; bit++)
        {
            if ((claimedBitmask & (1UL << bit)) != 0UL)
                indices.Add((byte)bit);
        }
        ApplyResolvedGrab(playerNetId, x, y, indices, grabsRemaining);
    }

    public void ApplyResolvedGrab(ulong playerNetId, float x, float y, IReadOnlyList<byte> claimedIndices, int grabsRemaining)
    {
        if (!_hands.TryGetValue(playerNetId, out CauldronHandState? hand))
            return;

        hand.X = Math.Clamp(x, -120f, BoardSize + 120f);
        hand.Y = Math.Clamp(y, -120f, BoardSize + 120f);
        hand.TargetX = hand.X;
        hand.TargetY = hand.Y;
        hand.IsClenched = true;
        hand.ClenchTimer = ClenchVisualDuration;
        hand.CooldownTimer = GrabCooldownDuration;
        hand.IsFalseStartPenalty = false;
        hand.GrabsRemaining = Math.Clamp(grabsRemaining, 0, GrabsPerPlayer);

        int newlyClaimed = 0;
        CauldronTreasureItem? topItem = null;
        for (int i = 0; i < claimedIndices.Count; i++)
        {
            int idx = claimedIndices[i];
            if (idx < 0 || idx >= _items.Count)
                continue;
            CauldronTreasureItem item = _items[idx];
            if (item.Index != idx || item.IsClaimed)
                continue;
            item.IsClaimed = true;
            item.ClaimedByNetId = playerNetId;
            item.ClaimAnimTimer = 1.0f;
            newlyClaimed++;
            if (topItem == null || GetPriority(item.Kind) > GetPriority(topItem.Kind))
                topItem = item;
        }

        hand.ItemsGrabbed += newlyClaimed;
        hand.LastBurstTimer = 0.55f;
        hand.LastBurstX = hand.X;
        hand.LastBurstY = hand.Y;
        hand.LastBurstHit = newlyClaimed > 0;

        string actor = hand.Player.Character.Title.GetFormattedText();
        LastFeedbackActor = actor;
        if (newlyClaimed > 0 && topItem != null)
        {
            LastFeedbackValue = newlyClaimed;
            LastFeedbackKey = topItem.Kind switch
            {
                CauldronTreasureKind.RareRelic => "THINGS_BRONZE_CAULDRON.ui.statusGrabRare",
                CauldronTreasureKind.UncommonRelic => "THINGS_BRONZE_CAULDRON.ui.statusGrabUncommon",
                CauldronTreasureKind.CommonRelic => "THINGS_BRONZE_CAULDRON.ui.statusGrabCommon",
                _ => "THINGS_BRONZE_CAULDRON.ui.statusGrabMulti",
            };
        }
        else
        {
            LastFeedbackKey = "THINGS_BRONZE_CAULDRON.ui.statusGrabMiss";
            LastFeedbackValue = 0;
        }

        StateChanged?.Invoke();
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;
        float dt = Math.Min(deltaSeconds, 0.1f);

        // Smooth remote hand interpolation & clench/burst timers
        foreach (CauldronHandState hand in _orderedHands)
        {
            if (hand.Player.NetId != LocalPlayerNetId)
            {
                float lerp = Math.Min(1f, dt * 16f);
                hand.X += (hand.TargetX - hand.X) * lerp;
                hand.Y += (hand.TargetY - hand.Y) * lerp;
            }
            if (hand.ClenchTimer > 0f)
            {
                hand.ClenchTimer = MathF.Max(0f, hand.ClenchTimer - dt);
                if (hand.ClenchTimer <= 0f)
                    hand.IsClenched = false;
            }
            if (hand.CooldownTimer > 0f)
            {
                hand.CooldownTimer = MathF.Max(0f, hand.CooldownTimer - dt);
                if (hand.CooldownTimer <= 0f)
                    hand.IsFalseStartPenalty = false;
            }
            if (hand.LastBurstTimer > 0f)
                hand.LastBurstTimer = MathF.Max(0f, hand.LastBurstTimer - dt);
        }

        // Rapidly spin ALL unclaimed treasures clockwise around the cauldron center!
        if (Phase != CauldronPhase.Complete)
        {
            OrbitAngle = (OrbitAngle + OrbitAngularSpeed * dt) % MathF.Tau;
        }

        foreach (CauldronTreasureItem item in _items)
        {
            if (!item.IsClaimed && Phase != CauldronPhase.Complete)
            {
                float a = item.BaseAngle + OrbitAngle;
                item.X = Center + MathF.Cos(a) * item.OrbitRadius;
                item.Y = Center + MathF.Sin(a) * item.OrbitRadius;
            }
            if (item.ClaimAnimTimer > 0f)
                item.ClaimAnimTimer = MathF.Max(0f, item.ClaimAnimTimer - dt * 2.4f);
        }

        if (Phase == CauldronPhase.Countdown)
        {
            int prevSec = (int)MathF.Ceiling(CountdownRemaining);
            CountdownRemaining = MathF.Max(0f, CountdownRemaining - dt);
            int curSec = (int)MathF.Ceiling(CountdownRemaining);
            if (CountdownRemaining <= 0f)
            {
                Phase = CauldronPhase.Scramble;
                LastFeedbackKey = "THINGS_BRONZE_CAULDRON.ui.statusGo";
                StateChanged?.Invoke();
            }
            else if (curSec != prevSec)
            {
                StateChanged?.Invoke();
            }
            return;
        }

        if (Phase == CauldronPhase.Scramble)
        {
            int prevSec = (int)MathF.Ceiling(ScrambleRemaining);
            ScrambleRemaining = MathF.Max(0f, ScrambleRemaining - dt);
            int curSec = (int)MathF.Ceiling(ScrambleRemaining);
            if (ScrambleRemaining <= 0f && IsHostAuthority)
            {
                FinalizeAsHost();
            }
            else if (curSec != prevSec)
            {
                StateChanged?.Invoke();
            }
        }
    }

    private void CheckScrambleCompletion()
    {
        if (!IsHostAuthority || Phase != CauldronPhase.Scramble)
            return;

        bool anyUnclaimed = _items.Any(i => !i.IsClaimed);
        bool anyGrabsLeft = _orderedHands.Any(h => h.GrabsRemaining > 0);
        if (!anyUnclaimed || !anyGrabsLeft)
            FinalizeAsHost();
    }

    public void FinalizeAsHost()
    {
        if (!IsHostAuthority || HasFinalizedOutcomes)
            return;

        // Sanitize through the exact same shared caps used by client deserialization so
        // Host and Clients ALWAYS award bit-for-bit identical outcomes across 2–32+ players.
        List<ThingsBronzeCauldronPlayerOutcome> all = _orderedHands
            .Select(h => ThingsEventMinigameSync.SanitizeCauldronOutcome(BuildPlayerOutcome(h.Player.NetId)))
            .ToList();
        ApplyFinalOutcomes(all);
        HostFinalizedOutcomes?.Invoke(all);
    }

    public void ApplyFinalOutcomes(IReadOnlyList<ThingsBronzeCauldronPlayerOutcome> outcomes)
    {
        if (HasFinalizedOutcomes)
            return;
        HasFinalizedOutcomes = true;
        Phase = CauldronPhase.Complete;
        LastFeedbackKey = "THINGS_BRONZE_CAULDRON.ui.statusComplete";

        foreach (ThingsBronzeCauldronPlayerOutcome outcome in outcomes)
        {
            if (_playerOutcomes.TryGetValue(outcome.PlayerNetId, out var tcs))
                tcs.TrySetResult(ThingsEventMinigameSync.SanitizeCauldronOutcome(outcome));
        }
        // Fallback safety for any player absent in packet
        foreach (CauldronHandState hand in _orderedHands)
        {
            if (_playerOutcomes.TryGetValue(hand.Player.NetId, out var tcs))
                tcs.TrySetResult(ThingsEventMinigameSync.SanitizeCauldronOutcome(BuildPlayerOutcome(hand.Player.NetId)));
        }

        StateChanged?.Invoke();
    }

    public void ConfirmUiExit()
    {
        if (IsHostAuthority && !HasFinalizedOutcomes)
            FinalizeAsHost();
        _uiDismissed.TrySetResult(true);
    }

    public void CancelAllWaiters()
    {
        foreach (TaskCompletionSource<ThingsBronzeCauldronPlayerOutcome> tcs in _playerOutcomes.Values)
            tcs.TrySetCanceled();
        _uiDismissed.TrySetCanceled();
    }

    public void ForceEnd()
    {
        if (!ThingsEventMinigameSync.IsRunActive)
        {
            CancelAllWaiters();
            return;
        }

        if (IsHostAuthority)
        {
            if (!HasFinalizedOutcomes)
                FinalizeAsHost();
        }

        _uiDismissed.TrySetResult(true);
    }

    public ThingsBronzeCauldronPlayerOutcome BuildPlayerOutcome(ulong playerNetId)
    {
        int gold = 0, maxHp = 0, upgrades = 0, potions = 0, cards = 0, cRelic = 0, uRelic = 0, rRelic = 0, count = 0;
        foreach (CauldronTreasureItem item in _items)
        {
            if (!item.IsClaimed || item.ClaimedByNetId != playerNetId)
                continue;
            count++;
            switch (item.Kind)
            {
                case CauldronTreasureKind.Gold: gold += item.Value; break;
                case CauldronTreasureKind.MaxHp: maxHp += item.Value; break;
                case CauldronTreasureKind.UpgradeCard: upgrades++; break;
                case CauldronTreasureKind.Potion: potions++; break;
                case CauldronTreasureKind.Card: cards++; break;
                case CauldronTreasureKind.CommonRelic: cRelic++; break;
                case CauldronTreasureKind.UncommonRelic: uRelic++; break;
                case CauldronTreasureKind.RareRelic: rRelic++; break;
            }
        }
        return new ThingsBronzeCauldronPlayerOutcome(playerNetId, gold, maxHp, upgrades, potions, cards, cRelic, uRelic, rRelic, count);
    }

    private static int GetPriority(CauldronTreasureKind kind) => kind switch
    {
        CauldronTreasureKind.RareRelic => 8,
        CauldronTreasureKind.UncommonRelic => 7,
        CauldronTreasureKind.CommonRelic => 6,
        CauldronTreasureKind.UpgradeCard => 5,
        CauldronTreasureKind.MaxHp => 4,
        CauldronTreasureKind.Card => 3,
        CauldronTreasureKind.Potion => 2,
        _ => 1,
    };
}
