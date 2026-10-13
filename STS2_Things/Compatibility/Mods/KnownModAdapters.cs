using STS2_Things.Compatibility.Internal;

namespace STS2_Things.Compatibility.Mods;

/// <summary>Explicit, verified registrations; unknown mods are never patched.</summary>
internal static class KnownModAdapters
{
    internal static readonly ModCompatibilityAdapter[] All =
    [
        new("BetterCallSpire", "BetterCallSpire", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "BetterCallSpire", harmony)),
        new("RegentCardsAnimeRework", "regentCardsAnimeRework", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "regentCardsAnimeRework", harmony)),
        new("LuolinCard", "LuolinCard", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "LuolinCard", harmony)),
        new("CuteNecrobinderCards", "cutenecrobinderv0.7", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cutenecrobinderv0_7", harmony)),
        new("MomoLib", "MomoLib", MomoLibCompatibility.Install),
    ];
}
