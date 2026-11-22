using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_Things.Platform;

/// <summary>
/// Universal >4-player (5–32+ players) compatibility helpers for STS2_Things.
/// Keeps 1–4 player vanilla behavior 100% unchanged, has zero hard dependencies on any
/// external player-limit mod, and dynamically bridges effective-player-count difficulty
/// scaling from mainstream STS2 limit-unlocker mods when loaded:
/// - BAKAOLC/STS2-MultiplayerLimitBreak
/// - Rain156/sts2-RMP-Mods (RemoveMultiplayerPlayerLimit)
/// - ajivoin/Sts2Unlimited
/// </summary>
public static class MultiplayerCompat
{
    public const int VanillaMaxPlayers = 4;
    public const int MaxSupportedPlayers = 32;
    public const int MaxProtocolPlayers = 64;

    private static readonly Color[] BaseSeatColors =
    [
        new Color("#ffd15c"),
        new Color("#7fe3ff"),
        new Color("#ff8a9b"),
        new Color("#a6f28a"),
    ];

    private static readonly (string TypeName, string MethodName)[] KnownEffectiveCountResolvers =
    [
        ("STS2MultiplayerLimitBreak.Settings.RuntimeMultiplayerSettings", "GetEffectivePlayerCount"),
        ("RemoveMultiplayerPlayerLimit.ModEntry", "GetEffectivePlayerCount"),
        ("Sts2Unlimited.DifficultyPatch", "EffectivePlayerCount"),
    ];

    private static readonly object ResolverLock = new();
    private static int _cachedAssemblyCount = -1;
    private static Func<int, int>? _cachedResolver;

    /// <summary>
    /// Optional deterministic override for headless probes and automated tests.
    /// </summary>
    internal static Func<int, int>? EffectivePlayerCountOverrideForTests { get; set; }

    public static int GetRawPlayerCount(ICombatState? combatState) =>
        Math.Max(1, combatState?.Players.Count ?? 1);

    public static int GetRawPlayerCount(IRunState? runState) =>
        Math.Max(1, runState?.Players.Count ?? 1);

    public static bool IsExtendedParty(int rawPlayerCount) =>
        rawPlayerCount > VanillaMaxPlayers;

    public static bool IsExtendedParty(ICombatState? combatState) =>
        GetRawPlayerCount(combatState) > VanillaMaxPlayers;

    public static bool IsExtendedParty(IRunState? runState) =>
        GetRawPlayerCount(runState) > VanillaMaxPlayers;

    /// <summary>
    /// Returns the effective player count used for multiplayer combat scaling.
    /// Strictly returns <paramref name="rawPlayerCount"/> for 1–4 players so vanilla behavior
    /// is never altered. For 5+ players, queries any loaded limit-unlocker mod's synced
    /// difficulty scaling resolver, falling back to <paramref name="rawPlayerCount"/>.
    /// </summary>
    public static int GetEffectivePlayerCount(int rawPlayerCount)
    {
        int clampedRaw = Math.Max(1, rawPlayerCount);
        if (clampedRaw <= VanillaMaxPlayers)
            return clampedRaw;

        if (EffectivePlayerCountOverrideForTests is { } testOverride)
            return Math.Clamp(testOverride(clampedRaw), 1, MaxProtocolPlayers);

        Func<int, int>? resolver = ResolveExternalEffectiveCountDelegate();
        if (resolver != null)
        {
            try
            {
                return Math.Clamp(resolver(clampedRaw), 1, MaxProtocolPlayers);
            }
            catch (Exception)
            {
                // Fall back to raw player count if an external mod changes internals.
            }
        }

        return Math.Clamp(clampedRaw, 1, MaxProtocolPlayers);
    }

    public static int GetEffectivePlayerCount(ICombatState? combatState) =>
        GetEffectivePlayerCount(GetRawPlayerCount(combatState));

    public static int GetEffectivePlayerCount(IRunState? runState) =>
        GetEffectivePlayerCount(GetRawPlayerCount(runState));

    /// <summary>
    /// Returns 1.0f for 1–4 players, and EffectivePlayerCount / 4.0f (at least 1.0f) for 5+ players.
    /// </summary>
    public static float GetExtendedPartyScaleRatio(int rawPlayerCount)
    {
        if (rawPlayerCount <= VanillaMaxPlayers)
            return 1f;

        return MathF.Max(1f, GetEffectivePlayerCount(rawPlayerCount) / (float)VanillaMaxPlayers);
    }

    /// <summary>
    /// Returns a distinct, high-contrast color for any player slot 0..63.
    /// Preserves the exact 4 authored palette colors for slots 0..3.
    /// </summary>
    public static Color GetSeatColor(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < BaseSeatColors.Length)
            return BaseSeatColors[slotIndex];

        int extra = Math.Max(0, slotIndex - BaseSeatColors.Length);
        // Golden-angle hue stepping yields well-separated readable colors up to 32+ players.
        float hue = ((0.08f + (extra + 1) * 0.61803398875f) % 1f + 1f) % 1f;
        float sat = (extra % 2 == 0) ? 0.68f : 0.56f;
        float val = (extra % 3 == 0) ? 0.98f : 0.92f;
        return Color.FromHsv(hue, sat, val, 1f);
    }

    private static Func<int, int>? ResolveExternalEffectiveCountDelegate()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        int count = assemblies.Length;
        lock (ResolverLock)
        {
            if (_cachedAssemblyCount == count)
                return _cachedResolver;

            _cachedAssemblyCount = count;
            _cachedResolver = FindResolver(assemblies);
            return _cachedResolver;
        }
    }

    private static Func<int, int>? FindResolver(Assembly[] assemblies)
    {
        foreach (var (typeName, methodName) in KnownEffectiveCountResolvers)
        {
            foreach (Assembly asm in assemblies)
            {
                Type? type;
                try
                {
                    type = asm.GetType(typeName, throwOnError: false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (type == null)
                    continue;

                MethodInfo? method = type.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    binder: null,
                    types: [typeof(int)],
                    modifiers: null);

                if (method == null || method.ReturnType != typeof(int))
                    continue;

                try
                {
                    return (Func<int, int>)Delegate.CreateDelegate(typeof(Func<int, int>), method);
                }
                catch (Exception)
                {
                    return raw => method.Invoke(null, [raw]) is int value ? value : raw;
                }
            }
        }

        return null;
    }
}
