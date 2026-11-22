using System;
using System.Reflection;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Audio;
using STS2_Things.Cards;
using STS2_Things.Compatibility;
using STS2_Things.Config;
using STS2_Things.Diagnostics;
using STS2_Things.Modifiers;
using STS2_Things.Platform;
using STS2_Things.Relics;

[ModInitializer(nameof(Initialize))]
public static class STS2_ThingsInit
{
    public const string HarmonyId = "Adnermo.STS2_Things";
    public const string ModVersion = "2.0.4";
    private static bool _initialized;
    private static AbstractModel[]? _quirkyHopperPolicy;
    private static AbstractModel[]? _spentEmberPolicy;

    public static void Initialize()
    {
        if (_initialized)
        {
            Log.Warn("STS2_Things - duplicate initialization ignored.");
            return;
        }

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            ScriptManagerBridge.LookupScriptsInAssembly(assembly);
            Sts2VersionSeam.InitializeBeforeModelDatabase();
            ModCompatibility.Initialize();

            ThingsModConfig.Load();
            MultiplayerConfig.Initialize();
            ModMusicPolicy.Initialize();
            LibraryIntegration.Initialize();
            BugRunRecorder.Initialize();

            // ---- Card & Relic Pool Registration ----
            ModHelper.AddModelToPool<IroncladCardPool, ThingsCollision>();
            ModHelper.AddModelToPool<DefectCardPool, ThingsReuse>();
            ModHelper.AddModelToPool<EventCardPool, ThingsSurrender>();
            ModHelper.AddModelToPool<SilentCardPool, ThingsSoulfyshDisease>();
            ModHelper.AddModelToPool<SilentCardPool, ThingsRecall>();
            ModHelper.AddModelToPool<SilentCardPool, ThingsPackUp>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsLeechParasite>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsSnailCrystalChip>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsCaveGodBrokenBladeTrial>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsCaveGodShatteredShieldTrial>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsStickyBeeswax>();
            ModHelper.AddModelToPool<TokenCardPool, ThingsLamentTear>();

            ModHelper.AddModelToPool<EventRelicPool, ThingsWhiteFlag>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsCurseRemover>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsMagicGlove>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsAlmondWater>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsMedusaHair>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsShadowClaimTicket>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsBottledEcho>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsMycelialDeposit>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsBorrowedEmber>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsAnestheticChart>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsReturnReceipt>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsNarrowViewfinder>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsHotWaxParcel>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsChilledWaxParcel>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsAmberCombCore>();

            // ---- Deterministic RunState Hook Policies ----
            ModHelper.SubscribeForRunStateHooks(
                "Adnermo.STS2_Things.QuirkyHopperRewardPolicy",
                static runState => runState.CurrentRoom is CombatRoom
                    ? _quirkyHopperPolicy ??= [ModelDb.Modifier<ThingsQuirkyHopperRewardPolicy>()]
                    : Array.Empty<AbstractModel>());
            ModHelper.SubscribeForRunStateHooks(
                "Adnermo.STS2_Things.SpentEmberRestPolicy",
                static runState => runState.CurrentRoom is RestSiteRoom
                    ? _spentEmberPolicy ??= [ModelDb.Modifier<ThingsSpentEmberRestPolicy>()]
                    : Array.Empty<AbstractModel>());
            ThingsMerchantBargainPricePolicy.EnsureSubscribed();

            // ---- Harmony Patch Installation & Legacy ID Migration ----
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(assembly);
            LegacyIdMigrator.ActivateIfModelDbReady();

            _initialized = true;
            Log.Info($"STS2_Things v{ModVersion} ({Sts2VersionSeam.TargetTag}) - 加载成功!");
        }
        catch (Exception e)
        {
            Log.Error("STS2_Things - 加载失败");
            Log.Error(e.ToString());
            throw;
        }
    }
}
