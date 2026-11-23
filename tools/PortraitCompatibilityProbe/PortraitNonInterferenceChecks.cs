using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

public partial class PortraitCompatibilityProbeNode
{
    private static readonly HashSet<Type> ForbiddenPortraitPatchTypes =
    [
        typeof(NUpgradePreview),
        typeof(NPreviewCardHolder),
        typeof(NCardGridSelectionScreen),
        typeof(NGridCardHolder),
        typeof(NCardHolder),
        typeof(NEnchantPreview),
        typeof(NTransformPreview),
    ];

    private static bool IsForbiddenPortraitTarget(Type? declaringType, string? methodName)
    {
        if (declaringType == null)
            return false;
        if (ForbiddenPortraitPatchTypes.Contains(declaringType))
            return true;
        return declaringType == typeof(CardCmd)
            && string.Equals(methodName, "PreviewInternal", StringComparison.Ordinal);
    }

    private static IEnumerable<Patch> EnumerateAllPatches(Patches info)
    {
        if (info.Prefixes != null)
            foreach (Patch p in info.Prefixes) yield return p;
        if (info.Postfixes != null)
            foreach (Patch p in info.Postfixes) yield return p;
        if (info.Transpilers != null)
            foreach (Patch p in info.Transpilers) yield return p;
        if (info.Finalizers != null)
            foreach (Patch p in info.Finalizers) yield return p;
    }

    private static void RegisterThingsCardPools(Assembly implementation)
    {
        (Type PoolType, string CardTypeName)[] registrations =
        [
            (typeof(IroncladCardPool), "STS2_Things.Cards.ThingsCollision"),
            (typeof(DefectCardPool), "STS2_Things.Cards.ThingsReuse"),
            (typeof(EventCardPool), "STS2_Things.Cards.ThingsSurrender"),
            (typeof(SilentCardPool), "STS2_Things.Cards.ThingsSoulfyshDisease"),
            (typeof(SilentCardPool), "STS2_Things.Cards.ThingsRecall"),
            (typeof(SilentCardPool), "STS2_Things.Cards.ThingsPackUp"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsLeechParasite"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsSnailCrystalChip"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsCaveGodBrokenBladeTrial"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsCaveGodShatteredShieldTrial"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsStickyBeeswax"),
            (typeof(TokenCardPool), "STS2_Things.Cards.ThingsLamentTear"),
        ];

        foreach ((Type poolType, string cardTypeName) in registrations)
        {
            Type? cardType = implementation.GetType(cardTypeName, throwOnError: false);
            if (cardType != null)
                ModHelper.AddModelToPool(poolType, cardType);
        }
    }

    private void VerifyNoPreviewOrGridPortraitPatches(Assembly implementation)
    {
        Require(implementation.GetType("STS2_Things.Hooks.CrispCardPortraitPatch", throwOnError: false) == null,
            "CrispCardPortraitPatch must be completely removed from STS2_Things.");

        Type[] allTypes = implementation.GetTypes();
        foreach (Type type in allTypes)
        {
            HarmonyPatch[] typeAttrs = type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .OfType<HarmonyPatch>()
                .ToArray();
            Type? typeDeclaringTarget = null;
            string? typeMethodTarget = null;

            foreach (HarmonyPatch attr in typeAttrs)
            {
                HarmonyMethod? info = attr.info;
                if (info == null)
                    continue;
                if (info.declaringType != null)
                    typeDeclaringTarget = info.declaringType;
                if (!string.IsNullOrEmpty(info.methodName))
                    typeMethodTarget = info.methodName;

                Require(!IsForbiddenPortraitTarget(info.declaringType, info.methodName),
                    $"STS2_Things type {type.FullName} must not declare a HarmonyPatch targeting {info.declaringType?.Name}.{info.methodName}.");
            }

            Require(!IsForbiddenPortraitTarget(typeDeclaringTarget, typeMethodTarget),
                $"STS2_Things type {type.FullName} must not target {typeDeclaringTarget?.Name}.{typeMethodTarget}.");

            const BindingFlags MethodFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(MethodFlags))
            {
                HarmonyPatch[] methodAttrs = method.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                    .OfType<HarmonyPatch>()
                    .ToArray();
                if (methodAttrs.Length == 0)
                    continue;

                Type? resolvedType = typeDeclaringTarget;
                string? resolvedMethod = typeMethodTarget;
                foreach (HarmonyPatch attr in methodAttrs)
                {
                    HarmonyMethod? info = attr.info;
                    if (info == null)
                        continue;
                    if (info.declaringType != null)
                        resolvedType = info.declaringType;
                    if (!string.IsNullOrEmpty(info.methodName))
                        resolvedMethod = info.methodName;
                }

                Require(!IsForbiddenPortraitTarget(resolvedType, resolvedMethod),
                    $"STS2_Things method {type.FullName}.{method.Name} must not declare a HarmonyPatch targeting {resolvedType?.Name}.{resolvedMethod}.");
            }
        }

        // Also apply STS2_Things Harmony patches transiently to verify dynamic TargetMethod/TargetMethods
        // registrations produce zero active patches on any card preview/grid/holder UI method.
        var probeHarmony = new Harmony($"Adnermo.STS2_Things.PortraitNonInterference.{Guid.NewGuid():N}");
        try
        {
            foreach (Type patchType in allTypes.Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0))
            {
                probeHarmony.CreateClassProcessor(patchType).Patch();
            }

            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
            {
                if (!IsForbiddenPortraitTarget(target.DeclaringType, target.Name))
                    continue;

                Patches? info = Harmony.GetPatchInfo(target);
                if (info == null)
                    continue;

                bool hasThingsPatch = EnumerateAllPatches(info).Any(p =>
                    string.Equals(p.owner, probeHarmony.Id, StringComparison.Ordinal)
                    || string.Equals(p.owner, "Adnermo.STS2_Things", StringComparison.Ordinal)
                    || p.PatchMethod?.DeclaringType?.Assembly == implementation);

                Require(!hasThingsPatch,
                    $"STS2_Things must register zero Harmony patches on {target.DeclaringType?.Name}.{target.Name}.");
            }
        }
        finally
        {
            probeHarmony.UnpatchAll(probeHarmony.Id);
        }

        Cases.Add("zero-preview-grid-harmony-patches");
    }

    private void VerifyThingsCardsExposeNativeFullResPortraits(Assembly implementation)
    {
        Type thingsCardBase = implementation.GetType("STS2_Things.Core.Models.ThingsCardModel", throwOnError: true)!;
        Type[] thingsCardTypes = implementation.GetTypes()
            .Where(t => !t.IsAbstract && thingsCardBase.IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        Require(thingsCardTypes.Length >= 12,
            $"Expected at least 12 concrete ThingsCardModel subclasses, found {thingsCardTypes.Length}.");

        MethodInfo modelDbCard = typeof(ModelDb).GetMethod("Card", BindingFlags.Public | BindingFlags.Static)!;
        foreach (Type cardType in thingsCardTypes)
        {
            CardModel card = (CardModel)modelDbCard.MakeGenericMethod(cardType).Invoke(null, null)!;
            Require(card.HasPortrait, $"{cardType.Name} must report HasPortrait == true.");
            Require(!string.IsNullOrWhiteSpace(card.PortraitPath) && ResourceLoader.Exists(card.PortraitPath),
                $"{cardType.Name} must expose an existing PortraitPath ({card.PortraitPath}).");

            Texture2D portrait = card.Portrait;
            Require(portrait != null && GodotObject.IsInstanceValid(portrait),
                $"{cardType.Name} Portrait must load a valid Texture2D.");

            if (card.Rarity == CardRarity.Ancient)
            {
                Require(portrait!.GetWidth() == 606 && portrait.GetHeight() == 852,
                    $"Ancient card {cardType.Name} must expose native 606x852 portrait via PortraitPath (got {portrait.GetWidth()}x{portrait.GetHeight()}).");
            }
            else
            {
                Require(card.PortraitPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
                    $"Standard card {cardType.Name} must resolve directly to standalone PNG via PortraitPath (got {card.PortraitPath}).");
                Require(portrait!.GetWidth() == 1000 && portrait.GetHeight() == 760,
                    $"Standard card {cardType.Name} must expose native 1000x760 portrait via PortraitPath (got {portrait.GetWidth()}x{portrait.GetHeight()}).");
            }
        }

        Cases.Add("things-cards-native-fullres");
    }

    private async Task VerifyBigCardPortraitDoesNotPolluteMissedCache(Assembly implementation)
    {
        Type seamType = implementation.GetType("STS2_Things.Platform.Sts2VersionSeam", throwOnError: true)!;
        MethodInfo? tryGetBigPortrait = seamType.GetMethod(
            "TryGetBigCardPortrait",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(CardModel)],
            modifiers: null);
        Require(tryGetBigPortrait != null, "Sts2VersionSeam.TryGetBigCardPortrait(CardModel) must exist.");

        Require(tryGetBigPortrait!.Invoke(null, new object?[] { null }) == null,
            "Sts2VersionSeam.TryGetBigCardPortrait(null) must cleanly return null.");

        PropertyInfo pngPathProp = typeof(CardModel).GetProperty("PortraitPngPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo missedAssetsField = typeof(AssetCache).GetField("_missedCacheAssets", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var missedAssets = (HashSet<string>)missedAssetsField.GetValue(PreloadManager.Cache)!;

        CardModel[] sampleCards =
        [
            ModelDb.Card<Offering>(),
            ModelDb.Card<StrikeIronclad>(),
        ];

        foreach (CardModel card in sampleCards)
        {
            string pngPath = (string)pngPathProp.GetValue(card)!;
            Require(!string.IsNullOrWhiteSpace(pngPath) && ResourceLoader.Exists(pngPath),
                $"{card.Id.Entry} standalone portrait PNG must exist ({pngPath}).");
            Require(!missedAssets.Contains(pngPath),
                $"{card.Id.Entry} must not be pre-registered in _missedCacheAssets before seam call.");

            int missedCountBefore = PreloadManager.Cache.MissedCacheAssetCount;
            Texture2D? sharedBefore = ResourceLoader.Load<Texture2D>(pngPath, null, ResourceLoader.CacheMode.Reuse);
            Require(sharedBefore != null && GodotObject.IsInstanceValid(sharedBefore),
                $"Independent ResourceLoader.Load succeeds for {card.Id.Entry}.");

            Texture2D? bigPortrait = (Texture2D?)tryGetBigPortrait.Invoke(null, new object?[] { card });
            Require(bigPortrait != null && GodotObject.IsInstanceValid(bigPortrait),
                $"Sts2VersionSeam.TryGetBigCardPortrait({card.Id.Entry}) must return a valid Texture2D.");
            Texture2D resolvedPortrait = bigPortrait!;
            Require(resolvedPortrait.GetWidth() >= 500 && !ReferenceEquals(resolvedPortrait, card.Portrait),
                $"TryGetBigCardPortrait({card.Id.Entry}) must return standalone high-res texture distinct from atlas.");
            Require(string.Equals(resolvedPortrait.ResourcePath, pngPath, StringComparison.Ordinal),
                $"TryGetBigCardPortrait({card.Id.Entry}) must resolve to {pngPath} (got {resolvedPortrait.ResourcePath}).");

            Require(!missedAssets.Contains(pngPath),
                $"TryGetBigCardPortrait({card.Id.Entry}) must never insert {pngPath} into PreloadManager.Cache._missedCacheAssets.");
            Require(PreloadManager.Cache.MissedCacheAssetCount == missedCountBefore,
                $"TryGetBigCardPortrait({card.Id.Entry}) must not increment PreloadManager.Cache.MissedCacheAssetCount.");

            PreloadManager.Cache.UnloadMissedCacheAssets();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Require(GodotObject.IsInstanceValid(resolvedPortrait) && GodotObject.IsInstanceValid(sharedBefore!),
                $"CompressedTexture2D for {card.Id.Entry} must remain valid after UnloadMissedCacheAssets (b053fbd6 guard).");
        }

        Cases.Add("big-portrait-no-missed-cache");
    }
}
