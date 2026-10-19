using STS2_Things.Compatibility.Internal;

namespace STS2_Things.Compatibility.Mods;

/// <summary>Explicit, verified registrations; unknown mods are never patched.</summary>
internal static class KnownModAdapters
{
    internal static readonly ModCompatibilityAdapter[] All =
    [
        new("BetterCallSpire", "BetterCallSpire", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "BetterCallSpire", harmony)),
        new("RegentCardsAnimeRework", "regentcardsanimerework", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "regentcardsanimerework", harmony)),
        new("LuolinCard", "LuolinCard", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "LuolinCard", harmony)),
        new("LifeWithSpire", "Life_with_spire", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "Life_with_spire", harmony)),
        new("Diana111", "Diana111", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "Diana111", harmony)),
        new("MidoriBlackCard", "com.demo.midori_black_card", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "com_demo_midori_black_card", harmony)),
        new("AliceDefectCard", "AliceDefectCard", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "AliceDefectCard", harmony)),
        new("CuteNecrobinderCards", "cutenecrobinderv0.7", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cutenecrobinderv0_7", harmony)),
        new("MomoLib", "MomoLib", MomoLibCompatibility.Install),
    ];
}
