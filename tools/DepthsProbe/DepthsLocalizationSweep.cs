using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Saves;
using STS2_Things.Acts;
using STS2_Things.Cards;
#if STS2_V113_1
using STS2_Things.Encounters;
#endif
using STS2_Things.Powers;
using STS2_Things.Relics;

public partial class DepthsProbeNode
{
    private static void VerifyBilingualModelLocalization()
    {
        var modAsm = typeof(ThingsDepths).Assembly;
        string originalLang = SaveManager.Instance.SettingsSave.Language ?? "zhs";
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                SaveManager.Instance.SettingsSave.Language = lang;
                LocManager.Initialize();

                var cards = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(CardModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(cards.Length >= 10, $"Expected >=10 mod cards ({lang}), got {cards.Length}.");
                foreach (var card in cards)
                {
                    AssertLocClean(card.Title, $"Card.Title:{card.Id.Entry} ({lang})");
                    AssertLocClean(card.GetDescriptionForPile(PileType.Hand, null), $"Card.Desc:{card.Id.Entry} ({lang})");
                    if (card.MaxUpgradeLevel > 0)
                    {
                        var upgraded = (CardModel)card.MutableClone();
                        upgraded.UpgradeInternal();
                        upgraded.UpdateDynamicVarPreview(CardPreviewMode.Upgrade, null, card.DynamicVars);
                        AssertLocClean(upgraded.GetDescriptionForUpgradePreview(), $"Card.UpgradePreview:{card.Id.Entry} ({lang})");
                        AssertLocClean(upgraded.GetDescriptionForPile(PileType.Hand, null), $"Card.UpgradedDesc:{card.Id.Entry} ({lang})");
                    }
                    var tips = card.HoverTips.ToArray();
                    foreach (var ht in tips.OfType<HoverTip>())
                        AssertLocClean(ht.Description, $"Card.HoverTip:{card.Id.Entry}:{ht.Id} ({lang})");
                }

                // Spot-check key card vanilla alignments & hover tips.
                var disease = ModelDb.Card<ThingsSoulfyshDisease>();
                var diseaseUpgraded = (CardModel)disease.MutableClone();
                diseaseUpgraded.UpgradeInternal();
                diseaseUpgraded.UpdateDynamicVarPreview(CardPreviewMode.Upgrade, null, disease.DynamicVars);
                string diseasePreview = diseaseUpgraded.GetDescriptionForUpgradePreview();
                Assert(diseasePreview.Contains("[green]1[/green]") && !diseasePreview.Contains("[red]1[/red]"),
                    $"Soulfysh Disease upgrade preview must tint 2->1 green via :inverseDiff() ({lang}): {diseasePreview}");
                Assert(disease.HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("INTANGIBLE", StringComparison.OrdinalIgnoreCase)),
                    $"Soulfysh Disease must cascade Intangible hover tip ({lang}).");
                Assert(ModelDb.Card<ThingsStickyBeeswax>().HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("ENERGY", StringComparison.OrdinalIgnoreCase)),
                    $"Sticky Beeswax must expose Energy hover tip ({lang}).");
                Assert(ModelDb.Card<ThingsReuse>().HoverTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is Fuel)
                    && ModelDb.Card<ThingsReuse>().HoverTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is Soot),
                    $"Reuse must expose Fuel and Soot hover tips ({lang}).");

                var afflictions = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(AfflictionModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<AfflictionModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(afflictions.Length == 3, $"Expected 3 mod afflictions ({lang}), got {afflictions.Length}.");
                foreach (var aff in afflictions)
                {
                    AssertLocClean(aff.Title.GetFormattedText(), $"Affliction.Title:{aff.Id.Entry} ({lang})");
                    AssertLocClean(aff.DynamicDescription.GetFormattedText(), $"Affliction.Desc:{aff.Id.Entry} ({lang})");
                    if (aff.DynamicExtraCardText != null)
                    {
                        string extra = aff.DynamicExtraCardText.GetFormattedText();
                        AssertLocClean(extra, $"Affliction.ExtraCardText:{aff.Id.Entry} ({lang})");
                        Assert(!extra.Contains("[gold]"), $"Affliction extraCardText must not wrap [gold] inside native [purple] ({aff.Id.Entry}, {lang}): {extra}");
                    }
                    _ = aff.HoverTips.ToArray();
                }

                var enchantments = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(EnchantmentModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<EnchantmentModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(enchantments.Length == 4, $"Expected 4 mod enchantments ({lang}), got {enchantments.Length}.");
                foreach (var ench in enchantments)
                {
                    AssertLocClean(ench.Title.GetFormattedText(), $"Enchantment.Title:{ench.Id.Entry} ({lang})");
                    AssertLocClean(ench.DynamicDescription.GetFormattedText(), $"Enchantment.Desc:{ench.Id.Entry} ({lang})");
                    if (ench.DynamicExtraCardText != null)
                        AssertLocClean(ench.DynamicExtraCardText.GetFormattedText(), $"Enchantment.ExtraCardText:{ench.Id.Entry} ({lang})");
                    _ = ench.HoverTips.ToArray();
                }

                var relics = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(RelicModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<RelicModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(relics.Length == 15, $"Expected 15 mod relics ({lang}), got {relics.Length}.");
                foreach (var relic in relics)
                {
                    AssertLocClean(relic.Title.GetFormattedText(), $"Relic.Title:{relic.Id.Entry} ({lang})");
                    AssertLocClean(relic.DynamicDescription.GetFormattedText(), $"Relic.Desc:{relic.Id.Entry} ({lang})");
                    AssertLocClean(relic.Flavor.GetFormattedText(), $"Relic.Flavor:{relic.Id.Entry} ({lang})");
                    AssertLocClean(relic.DynamicEventDescription.GetFormattedText(), $"Relic.EventDesc:{relic.Id.Entry} ({lang})");
                    foreach (var ht in relic.HoverTips.OfType<HoverTip>())
                        AssertLocClean(ht.Description, $"Relic.HoverTip:{relic.Id.Entry}:{ht.Id} ({lang})");
                }
                var gloveTips = ModelDb.Relic<ThingsMagicGlove>().HoverTips.ToArray();
                Assert(gloveTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is FlashOfSteel)
                    && gloveTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is Finesse)
                    && gloveTips.OfType<HoverTip>().Any(t => t.Id.Contains("TRANSFORM", StringComparison.OrdinalIgnoreCase)),
                    $"Magic Glove must expose Transform + FlashOfSteel + Finesse hover tips ({lang}).");
                var medusaTips = ModelDb.Relic<ThingsMedusaHair>().HoverTips.ToArray();
                Assert(medusaTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is StoneArmor && t.Card.IsUpgraded)
                    && medusaTips.OfType<CardHoverTip>().Any(t => t.Card.CanonicalInstance is GiantRock && t.Card.IsUpgraded)
                    && medusaTips.OfType<HoverTip>().Any(t => t.Id.Contains("EXHAUST", StringComparison.OrdinalIgnoreCase)),
                    $"Medusa's Hair must expose upgraded StoneArmor+, GiantRock+, and Exhaust hover tips ({lang}).");
                Assert(ModelDb.Relic<ThingsWhiteFlag>().HasUponPickupEffect && ModelDb.Relic<ThingsCurseRemover>().HasUponPickupEffect,
                    $"White Flag and Curse Remover must set HasUponPickupEffect = true ({lang}).");

                var powers = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(PowerModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<PowerModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(powers.Length >= 40, $"Expected >=40 mod powers ({lang}), got {powers.Length}.");
                foreach (var power in powers)
                {
                    if (!power.IsVisible) continue;
                    AssertLocClean(power.Title.GetFormattedText(), $"Power.Title:{power.Id.Entry} ({lang})");
                    AssertLocClean(power.Description.GetFormattedText(), $"Power.Desc:{power.Id.Entry} ({lang})");
                    if (power.HasSmartDescription)
                    {
                        foreach (int amount in new[] { 1, 2 })
                        {
                            foreach (bool isMultiplayer in new[] { false, true })
                            {
                                var smart = power.SmartDescription;
                                smart.Add("Amount", amount);
                                smart.Add("OnPlayer", false);
                                smart.Add("IsMultiplayer", isMultiplayer);
                                smart.Add("PlayerCount", isMultiplayer ? 2 : 1);
                                smart.Add("OwnerName", "TestOwner");
                                smart.Add("ApplierName", "TestApplier");
                                smart.Add("TargetName", "TestTarget");
                                power.DynamicVars.AddTo(smart);
                                AssertLocClean(smart.GetFormattedText(), $"Power.SmartDesc:{power.Id.Entry}[Amt={amount},MP={isMultiplayer}] ({lang})");
                            }
                        }
                    }
                    foreach (var ht in power.HoverTips.OfType<HoverTip>())
                        AssertLocClean(ht.Description, $"Power.HoverTip:{power.Id.Entry}:{ht.Id} ({lang})");
                }
                Assert(ModelDb.Power<ThingsOriginGainEnergyPower>().HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("ENERGY", StringComparison.OrdinalIgnoreCase)),
                    $"Origin Gain Energy power must expose Energy hover tip ({lang}).");
                Assert(ModelDb.Power<ThingsChromaticFissionPower>().HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("WEAK", StringComparison.OrdinalIgnoreCase)),
                    $"Chromatic Fission power must expose Weak hover tip ({lang}).");
                Assert(ModelDb.Power<ThingsBorrowedShadowPower>().HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("POISON", StringComparison.OrdinalIgnoreCase)),
                    $"Borrowed Shadow power must expose Poison hover tip ({lang}).");
                Assert(!ModelDb.Power<ThingsAbyssalSurgePower>().HoverTips.OfType<HoverTip>().Any(t => t.Id.Contains("INK_BLACKOUT", StringComparison.OrdinalIgnoreCase)),
                    $"Abyssal Surge must not expose stale Ink Blackout hover tip ({lang}).");
                foreach (string extraKey in new[]
                {
                    "THINGS_INK_BLACKOUT_POWER.activeDescription",
                    "THINGS_INK_BLACKOUT_POWER.activeMultiDescription",
                    "THINGS_SILK_THREAD_POWER.activeDescription",
                    "THINGS_SILK_THREAD_POWER.activeMultiDescription",
                    "THINGS_TOWER_PIVOT_POWER.activeCycleDescription",
                    "THINGS_TOWER_PIVOT_POWER.activeComboDescription",
                    "THINGS_TOWER_PIVOT_POWER.finalStandDescription",
                    "THINGS_BASTION_SEAL_POWER.boltedDescription",
                    "THINGS_CHROMATIC_FISSION_POWER.largeDescription",
                    "THINGS_CHROMATIC_FISSION_POWER.mediumDescription",
                    "THINGS_CHROMATIC_FISSION_POWER.pendingSplitDescription",
                    "THINGS_CHROMATIC_FISSION_POWER.pendingMediumSplitDescription",
                })
                {
                    Assert(LocString.Exists("powers", extraKey), $"Missing power localization key {extraKey} ({lang}).");
                    var loc = new LocString("powers", extraKey);
                    loc.Add("Amount", 1);
                    loc.Add("ExtraTurns", 2);
                    loc.Add("RemainingTurns", 1);
                    loc.Add("CycleRemaining", 1);
                    loc.Add("ShatterStrength", 2);
                    loc.Add("ComboStrength", 1);
                    loc.Add("FinalStandStrength", 2);
                    loc.Add("OwnerName", "TestOwner");
                    loc.Add("NextFace", "TestFace");
                    loc.Add("EntryBlock", 14);
                    loc.Add("VulnerableTurns", 2);
                    loc.Add("ExcessThreshold", 5);
                    loc.Add("InheritedHp", 32);
                    loc.Add("Child1", "TestChild1");
                    loc.Add("Child2", "TestChild2");
                    AssertLocClean(loc.GetFormattedText(), $"Power.ExtraKey:{extraKey} ({lang})");
                }

                var ironclad = ModelDb.Character<Ironclad>();
                var encounters = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(EncounterModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<EncounterModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(encounters.Length >= 25, $"Expected >=25 mod encounters ({lang}), got {encounters.Length}.");
                foreach (var enc in encounters)
                {
                    string title = enc.Title.GetFormattedText();
                    AssertLocClean(title, $"Encounter.Title:{enc.Id.Entry} ({lang})");
                    Assert(!title.StartsWith("深处：") && !title.StartsWith("Depths:"),
                        $"Encounter title must not have Act prefix ({enc.Id.Entry}, {lang}): {title}");
                    string loss = enc.GetLossMessageFor(ironclad).GetFormattedText();
                    AssertLocClean(loss, $"Encounter.Loss:{enc.Id.Entry} ({lang})");
                    Assert(!loss.EndsWith("……") && !loss.EndsWith("..."),
                        $"Encounter loss line must end cleanly without trailing ellipsis ({enc.Id.Entry}, {lang}): {loss}");
                }

                var monsters = ModelDb.AllAbstractModelSubtypes
                    .Where(t => t.Assembly == modAsm && typeof(MonsterModel).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => ModelDb.GetById<MonsterModel>(ModelDb.GetId(t)))
                    .ToArray();
                Assert(monsters.Length >= 35, $"Expected >=35 mod monsters ({lang}), got {monsters.Length}.");
                foreach (var mon in monsters)
                    AssertLocClean(mon.Title.GetFormattedText(), $"Monster.Title:{mon.Id.Entry} ({lang})");

                string[] dreadAscensionKeys =
                [
                    "DREAD.THINGS_ORIGIN_FOGMOG.description",
                    "DREAD.THINGS_SCALE_BEETLE.description",
                    "DREAD.THINGS_BOWLBUG_PROGENITOR.description",
                    "DREAD.THINGS_CAVE_GOD.description",
                    "DREAD.THINGS_HUMAN_FACE_TOWER.description",
                    "DREAD.THINGS_ABYSSAL_INKMAW.description",
                    "DREAD.THINGS_GRAVETIDE_SLUG.description",
                    "DREAD.THINGS_THE_LEGACY.description",
                ];
                foreach (string key in dreadAscensionKeys)
                {
                    Assert(LocString.Exists("ascension", key), $"Missing ascension localization key {key} ({lang}).");
                    AssertLocClean(new LocString("ascension", key).GetFormattedText(), $"Ascension:{key} ({lang})");
                }

                string[] blitzMerchantKeys =
                [
                    "MERCHANT.blitz.things_trivia_relic_entry.title",
                    "MERCHANT.blitz.things_trivia_relic_entry.description",
                ];
                foreach (string key in blitzMerchantKeys)
                {
                    Assert(LocString.Exists("merchant_room", key), $"Missing merchant_room localization key {key} ({lang}).");
                    AssertLocClean(new LocString("merchant_room", key).GetFormattedText(), $"MerchantRoom:{key} ({lang})");
                }

#if STS2_V113_1
                EncounterModel[] dreadModBosses =
                [
                    ModelDb.Encounter<ThingsOriginFogmogBossEncounter>(),
                    ModelDb.Encounter<ThingsScaleBeetleBossEncounter>(),
                    ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>(),
                    ModelDb.Encounter<ThingsCaveGodBossEncounter>(),
                    ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(),
                    ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(),
                    ModelDb.Encounter<ThingsGravetideSlugBossEncounter>(),
                    ModelDb.Encounter<ThingsTheLegacyBossEncounter>(),
                ];
                foreach (var boss in dreadModBosses)
                {
                    var dominion = boss.GetDominion();
                    Assert(dominion != null, $"Mod boss {boss.Id.Entry} must return a non-null Dominion ({lang}).");
                    var resolvedDominion = dominion!;
                    AssertLocClean(resolvedDominion.Description.GetFormattedText(), $"Dominion.Desc:{resolvedDominion.Id.Entry} ({lang})");
                    var extraTips = resolvedDominion.ExtraHoverTips.ToArray();
                    foreach (var ht in extraTips.OfType<HoverTip>())
                        AssertLocClean(ht.Description, $"Dominion.HoverTip:{resolvedDominion.Id.Entry}:{ht.Id} ({lang})");
                }
#endif
            }
            GD.Print("PASS bilingual SmartFormat & HoverTips runtime sweep across all mod Cards, Afflictions, Enchantments, Relics, Powers, Encounters, Monsters, Dread Dominions, and Blitz Merchant keys (zhs + eng).");
        }
        finally
        {
            SaveManager.Instance.SettingsSave.Language = originalLang;
            LocManager.Initialize();
        }
    }

    private static void AssertLocClean(string text, string context)
    {
        Assert(!string.IsNullOrWhiteSpace(text), $"Empty formatted localization for {context}.");
        Assert(!text.Contains('{') && !text.Contains('}'), $"Unresolved SmartFormat placeholder in {context}: {text}");
    }
}
