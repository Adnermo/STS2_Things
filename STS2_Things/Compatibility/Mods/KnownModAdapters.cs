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
        new("AiSilentNsfw", "11", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "Mod_11", harmony)),
        new("CardArtProjectExportMod", "project-export-mod", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "project_export_mod", harmony)),
        new("MechanekoDefectCardArt", "Mechaneko_Defect_Card_Art", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "Mechaneko_Defect_Card_Art", harmony)),
        new("PornographicBeautificationCardsV21", "cae_7c1701bbd70a", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cae_7c1701bbd70a", harmony)),
        new("PornographicBeautificationCardsV20", "cae_06f512f381a8", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cae_06f512f381a8", harmony)),
        new("PornographicBeautificationCardsV19", "cae_b59d672a862f", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cae_b59d672a862f", harmony)),
        new("PornographicBeautificationCardsV18", "cae_13a927d9decf", static (assembly, harmony) =>
            PortraitCacheGuard.Install(assembly, "cae_13a927d9decf", harmony)),
        new("MomoLib", "MomoLib", MomoLibCompatibility.Install),
    ];
}
