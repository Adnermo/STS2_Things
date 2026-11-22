using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using GodotFileAccess = Godot.FileAccess;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things;
using STS2_Things.Acts;
using STS2_Things.Cards;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Hooks;

public partial class ThingsConfigProbeNode : Node
{
    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("Sentry");
        EnsureRuntimeDependency("Sentry.Godot");
        _ = RunProbeAsync();
    }

    private async System.Threading.Tasks.Task RunProbeAsync()
    {
        try
        {
            TestMode.TurnOnInternal();
            MountPublishedPck();
            Assembly implementationAssembly = typeof(STS2_ThingsInit).Assembly;
            EnsureRuntimeDependency("System.IO.Hashing");
            RegisterSyntheticMod(implementationAssembly);
            STS2_ThingsInit.Initialize();
            InitializeModelDb(implementationAssembly);
            Assert(OS.GetUserDataDir().Contains("Things Config Probe", StringComparison.OrdinalIgnoreCase),
                "Probe must use an isolated user-data directory.");

            // 归一化：清掉上次探针运行遗留的配置写入，从默认值开始。
            ResetAll();
            ThingsModConfig.Save();

            if (OS.GetCmdlineUserArgs().Contains("--neow-only"))
            {
                await NeowOptionRegression.Run();
                GD.Print("Things Neow probe: PASS");
                GetTree().Quit(0);
                return;
            }

            if (OS.GetCmdlineUserArgs().Length > 1)
            {
                AddChild(new ConfigNetworkLiveProbe());
                return;
            }

            VerifyConfigDefaultsAndFile();
            await NeowOptionRegression.Run();
            VerifyConfigRoundTrip();
            VerifyTypedConfigMigration();
            VerifyConflictRule();
            VerifyForceImpliesEnabled();
            VerifyGuaranteedDepths();
            VerifyCatalogGating();
            VerifyBossForcing();
            VerifyActualBossSelection();
            VerifyWeightedSelection();
            VerifyEliteFrequency();
            VerifyMusicSetting();
            VerifyEventGating();
            VerifyCardPoolGating();
            MultiplayerConfigChecks.Run();
            BugFixRegressions.Run(this);

            GD.Print("Things config probe: PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    // ---- 配置模型 ----

    private static void VerifyConfigDefaultsAndFile()
    {
        Assert(ThingsModConfig.CurrentSchemaVersion == "3",
            $"Expected CurrentSchemaVersion=3, got {ThingsModConfig.CurrentSchemaVersion}.");
        Assert(ThingsConfigSnapshotMessage.Protocol == 2,
            $"Expected ThingsConfigSnapshotMessage.Protocol=2, got {ThingsConfigSnapshotMessage.Protocol}.");
        Assert(ThingsModConfig.Entries.Count == 79,
            $"Expected 79 config entries, got {ThingsModConfig.Entries.Count}.");
        Assert(ThingsModConfig.Entries.Any(entry =>
                   entry.Key == ThingsModConfig.FeatureGuaranteedDepthsEnabled &&
                   entry.Category == ThingsModConfig.Category.Act &&
                   entry.Default is false),
            "Missing FeatureGuaranteedDepthsEnabled entry in Category.Act.");
        Assert(!ThingsModConfig.GuaranteedDepthsEnabled,
            "GuaranteedDepthsEnabled hot cache must default to false.");
        Assert(ThingsModConfig.Entries.Any(entry =>
                   entry.Key == ThingsModConfig.FeatureDefensiveEngineGuardsEnabled &&
                   entry.Category == ThingsModConfig.Category.Stability &&
                   entry.Default is true),
            "Missing FeatureDefensiveEngineGuardsEnabled entry in Category.Stability.");
        Assert(ThingsModConfig.Entries.Count(entry =>
                   entry.Category == ThingsModConfig.Category.Card &&
                   entry.Default is true) == 6,
            "Expected 6 default-enabled entries in Category.Card.");

        foreach (ThingsModConfig.Entry entry in ThingsModConfig.Entries)
        {
            Assert(Equals(ThingsModConfig.GetValue(entry.Key), entry.Default),
                $"{entry.Key} default is {ThingsModConfig.GetValue(entry.Key)}, expected {entry.Default}.");
        }

        string path = ThingsModConfig.ConfigPath;
        Assert(File.Exists(path), $"Config file was not created at {path}.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert(document.RootElement.ValueKind == JsonValueKind.Object, "Config file is not an object.");
        foreach (ThingsModConfig.Entry entry in ThingsModConfig.Entries)
        {
            Assert(document.RootElement.TryGetProperty(entry.Key, out _),
                $"Config file misses key {entry.Key}.");
        }

        Assert(document.RootElement.TryGetProperty(ThingsModConfig.SchemaVersion, out JsonElement schema) &&
              schema.GetString() == "3",
            "Config file misses SchemaVersion=3.");
        Assert(document.RootElement.EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.String),
            "Shared config must remain readable by BaseLib's string dictionary.");
    }

    private static void VerifyConfigRoundTrip()
    {
        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogEnabled, false);
        ThingsModConfig.Save();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ThingsModConfig.ConfigPath));
        Assert(document.RootElement.GetProperty(ThingsModConfig.BossOriginFogmogEnabled).GetString() == "False",
            "Config file did not persist BossOriginFogmogEnabled=false.");
        ThingsModConfig.Load();
        Assert(!ThingsModConfig.IsEnabled(ThingsModConfig.BossOriginFogmogEnabled),
            "Load() did not restore BossOriginFogmogEnabled=false.");
        ResetAll();
    }

    private static void VerifyTypedConfigMigration()
    {
        File.WriteAllText(ThingsModConfig.ConfigPath,
            "{\"SchemaVersion\":\"1\",\"CustomBgmEnabled\":\"False\",\"MerchantBargainEnabled\":\"False\"," +
            "\"DefensiveEngineGuardsEnabled\":\"False\"}");
        ThingsModConfig.Load();
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.FeatureCustomBgmEnabled),
            "Legacy CustomBgmEnabled alias did not migrate to FeatureCustomBgmEnabled.");
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.FeatureMerchantBargainEnabled),
            "Legacy MerchantBargainEnabled alias did not migrate to FeatureMerchantBargainEnabled.");
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled),
            "Legacy DefensiveEngineGuardsEnabled alias did not migrate to FeatureDefensiveEngineGuardsEnabled.");
        using (JsonDocument migrated = JsonDocument.Parse(File.ReadAllText(ThingsModConfig.ConfigPath)))
        {
            Assert(migrated.RootElement.GetProperty(ThingsModConfig.SchemaVersion).GetString() == "3",
                "Migrated v1 config did not upgrade SchemaVersion to 3.");
            Assert(migrated.RootElement.GetProperty(ThingsModConfig.FeatureCustomBgmEnabled).GetString() == "False" &&
                   migrated.RootElement.GetProperty(ThingsModConfig.FeatureMerchantBargainEnabled).GetString() == "False" &&
                   migrated.RootElement.GetProperty(ThingsModConfig.FeatureDefensiveEngineGuardsEnabled).GetString() == "False",
                "Migrated v1 config did not persist canonical v3 feature keys.");
            Assert(!migrated.RootElement.TryGetProperty("CustomBgmEnabled", out _) &&
                   !migrated.RootElement.TryGetProperty("MerchantBargainEnabled", out _) &&
                   !migrated.RootElement.TryGetProperty("DefensiveEngineGuardsEnabled", out _),
                "Migrated v1 config left deprecated alias keys on disk.");
        }

        File.WriteAllText(ThingsModConfig.ConfigPath,
            "{\"SchemaVersion\":\"2\",\"EnableCustomBgm\":\"False\",\"EnableMerchantBargain\":\"False\"}");
        ThingsModConfig.Load();
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.FeatureCustomBgmEnabled) &&
               !ThingsModConfig.GetBool(ThingsModConfig.FeatureMerchantBargainEnabled),
            "Secondary Enable* legacy aliases did not migrate cleanly.");

        File.WriteAllText(ThingsModConfig.ConfigPath,
            "{\"SchemaVersion\":true,\"FeatureCustomBgmEnabled\":false,\"BossOriginFogmogEnabled\":false," +
            "\"BossScaleBeetleWeightPercent\":\"275\",\"EncounterSoulRoesWeightPercent\":50}");
        ThingsModConfig.Load();
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.FeatureCustomBgmEnabled), "Legacy bool preference was lost.");
        Assert(!ThingsModConfig.GetBool(ThingsModConfig.BossOriginFogmogEnabled), "Existing disabled boss was re-enabled.");
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossScaleBeetleWeightPercent) == 275, "String integer did not load.");
        Assert(ThingsModConfig.GetInt(ThingsModConfig.EncounterSoulRoesWeightPercent) == 50, "JSON number did not load.");
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossCaveGodWeightPercent) == 100, "Missing weight did not default to 100.");
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleWeightPercent, -1);
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossScaleBeetleWeightPercent) == 0, "Negative rate was not clamped.");
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleWeightPercent, 50000.0);
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossScaleBeetleWeightPercent) == 1000, "Large rate was not clamped.");
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleWeightPercent, double.NaN);
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossScaleBeetleWeightPercent) == 1000, "NaN changed a valid rate.");
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleWeightPercent, "125.5");
        ThingsModConfig.Save();
        ThingsModConfig.Load();
        Assert(ThingsModConfig.GetInt(ThingsModConfig.BossScaleBeetleWeightPercent) == 126, "Fractional input did not normalize deterministically.");
        ResetAll();
        ThingsModConfig.Save();
    }

    private static void VerifyConflictRule()
    {
        // 同槽位（Overgrowth）两个强制：规范顺序先到先得，后者降级。
        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogForced, true);
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleForced, true);
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossOriginFogmogForced),
            "First forced boss (Origin Fogmog) was demoted.");
        Assert(!ThingsModConfig.IsForced(ThingsModConfig.BossScaleBeetleForced),
            "Second forced boss (Scale Beetle) was not demoted.");
        Assert(ThingsModConfig.GetForcedBossKeyForSlot(ThingsModConfig.SlotOvergrowth) ==
               ThingsModConfig.BossOriginFogmogForced,
            "Overgrowth slot reports the wrong forced boss.");
        Assert(!ThingsModConfig.CanForce(ThingsModConfig.BossScaleBeetleForced),
            "CanForce(Scale Beetle) must be false while Origin Fogmog is forced.");
        Assert(ThingsModConfig.CanForce(ThingsModConfig.BossOriginFogmogForced),
            "CanForce(Origin Fogmog) must stay true.");

        // 不同槽位可同时强制。
        ThingsModConfig.SetValue(ThingsModConfig.BossGravetideSlugForced, true);
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossGravetideSlugForced),
            "Gravetide Slug (Underdocks) was demoted by an Overgrowth force.");
        Assert(ThingsModConfig.GetForcedBossKeyForSlot(ThingsModConfig.SlotUnderdocks) ==
               ThingsModConfig.BossGravetideSlugForced,
            "Underdocks slot reports the wrong forced boss.");
        ResetAll();

        // P2 回归：同槽位两个 Boss 同时 Forced=true 且后者 Enabled=false 时，
        // 降级后者的 Forced 不得顺带把其 Enabled=false 翻成 true。
        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.BossOriginFogmogForced] = true,
            [ThingsModConfig.BossScaleBeetleEnabled] = false,
            [ThingsModConfig.BossScaleBeetleForced] = true,
        });
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossOriginFogmogForced) &&
               ThingsModConfig.IsEnabled(ThingsModConfig.BossOriginFogmogEnabled),
            "First forced boss (Origin Fogmog) must remain forced and enabled.");
        Assert(!ThingsModConfig.IsForced(ThingsModConfig.BossScaleBeetleForced),
            "Second forced boss (Scale Beetle) must be demoted.");
        Assert(!ThingsModConfig.IsEnabled(ThingsModConfig.BossScaleBeetleEnabled),
            "Demoted duplicate forced boss must preserve Enabled=false instead of flipping to true.");

        File.WriteAllText(ThingsModConfig.ConfigPath,
            "{\"SchemaVersion\":\"3\",\"BossOriginFogmogForced\":\"True\",\"BossOriginFogmogEnabled\":\"False\"," +
            "\"BossScaleBeetleForced\":\"True\",\"BossScaleBeetleEnabled\":\"False\"}");
        ThingsModConfig.Load();
        Assert(ThingsModConfig.IsForced(ThingsModConfig.BossOriginFogmogForced) &&
               ThingsModConfig.IsEnabled(ThingsModConfig.BossOriginFogmogEnabled),
            "Load() must promote the winning forced boss to Enabled=true.");
        Assert(!ThingsModConfig.IsForced(ThingsModConfig.BossScaleBeetleForced) &&
               !ThingsModConfig.IsEnabled(ThingsModConfig.BossScaleBeetleEnabled),
            "Load() must demote the duplicate forced boss without flipping its Enabled=false to true.");
        ResetAll();
        ThingsModConfig.Save();
    }

    private static void VerifyForceImpliesEnabled()
    {
        ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodEnabled, false);
        ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodForced, true);
        Assert(ThingsModConfig.IsEnabled(ThingsModConfig.BossCaveGodEnabled),
            "Forcing Cave God must imply enabled.");
        ResetAll();
    }

    private static void VerifyGuaranteedDepths()
    {
        // 1) Stale ModelDb._actsByIndex cache invalidation (if another mod populated it before our patch).
        FieldInfo actsByIndexField = typeof(ModelDb).GetField("_actsByIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
        actsByIndexField.SetValue(null, new List<List<ActModel>>
        {
            new() { ModelDb.Act<Overgrowth>(), ModelDb.Act<Underdocks>() },
            new() { ModelDb.Act<Hive>() },
            new() { ModelDb.Act<Glory>() },
        });
        typeof(STS2_ThingsInit).Assembly
            .GetType("STS2_Things.Hooks.DepthsActCatalogPatch", throwOnError: true)!
            .GetMethod("InvalidateActsByIndexCache", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null);
        Assert(ModelDb.ActsByIndex[1].Any(act => act is ThingsDepths),
            "InvalidateActsByIndexCache did not restore ThingsDepths to ModelDb.ActsByIndex[1].");

        // 2) Default false: Act 2 still rolls both Hive and ThingsDepths across seeds.
        ResetAll();
        var seenDefaultAct2 = new HashSet<Type>();
        var baselineBySeed = new Dictionary<uint, (List<ActModel> Acts, int NextRng)>();
        for (uint seed = 1; seed <= 64; seed++)
        {
            var rng = new Rng(seed);
            List<ActModel> acts = ActModel.GetRandomList(rng, UnlockState.all, isMultiplayer: true).ToList();
            Assert(acts.Count >= 3, $"Expected at least 3 acts from GetRandomList, got {acts.Count}.");
            seenDefaultAct2.Add(acts[1].GetType());
            baselineBySeed[seed] = (acts, rng.NextInt());
        }
        Assert(seenDefaultAct2.Contains(typeof(Hive)) && seenDefaultAct2.Contains(typeof(ThingsDepths)),
            "Default ActModel.GetRandomList did not roll both Hive and ThingsDepths for Act 2.");

        // 3) Enabled true: Act 2 is ALWAYS ThingsDepths while preserving Act 1, Act 3, and exact RNG consumption.
        ThingsModConfig.SetValue(ThingsModConfig.FeatureGuaranteedDepthsEnabled, true);
        Assert(ThingsModConfig.GuaranteedDepthsEnabled &&
               ThingsModConfig.IsEnabled(ThingsModConfig.FeatureGuaranteedDepthsEnabled),
            "FeatureGuaranteedDepthsEnabled did not update hot cache.");
        for (uint seed = 1; seed <= 64; seed++)
        {
            var rng = new Rng(seed);
            List<ActModel> acts = ActModel.GetRandomList(rng, UnlockState.all, isMultiplayer: true).ToList();
            int nextRng = rng.NextInt();
            var (baselineActs, baselineNextRng) = baselineBySeed[seed];
            Assert(acts.Count == baselineActs.Count,
                $"Guaranteed Depths altered act count for seed {seed}: {acts.Count} != {baselineActs.Count}.");
            Assert(acts[1] is ThingsDepths,
                $"Guaranteed Depths failed to force Act 2 to ThingsDepths for seed {seed} (got {acts[1].Id}).");
            Assert(acts[0].Id == baselineActs[0].Id && acts[2].Id == baselineActs[2].Id,
                $"Guaranteed Depths altered Act 1 or Act 3 for seed {seed}.");
            Assert(nextRng == baselineNextRng,
                $"Guaranteed Depths drifted downstream RNG consumption for seed {seed}.");
        }

        ResetAll();
        Assert(!ThingsModConfig.GuaranteedDepthsEnabled,
            "ResetAll did not restore GuaranteedDepthsEnabled to false.");
    }


    // ---- 门控集成（补丁已随 STS2_ThingsInit.Initialize 安装）----

    private static void VerifyCatalogGating()
    {
        Overgrowth act = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
        List<EncounterModel> encounters = act.GenerateAllEncounters().ToList();
        Assert(encounters.Any(e => e.Id == ModelDb.Encounter<ThingsOriginFogmogBossEncounter>().Id),
            "Origin Fogmog is missing from the default Overgrowth encounter pool.");
        Assert(encounters.Any(e => e.Id == ModelDb.Encounter<ThingsScaleBeetleBossEncounter>().Id),
            "Scale Beetle is missing from the default Overgrowth encounter pool.");

        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogEnabled, false);
        encounters = act.GenerateAllEncounters().ToList();
        Assert(encounters.All(e => e.Id != ModelDb.Encounter<ThingsOriginFogmogBossEncounter>().Id),
            "Disabled Origin Fogmog is still in the Overgrowth encounter pool.");
        Assert(encounters.Any(e => e.Id == ModelDb.Encounter<ThingsScaleBeetleBossEncounter>().Id),
            "Disabling Origin Fogmog removed Scale Beetle.");
        ResetAll();
    }

    private static void VerifyBossForcing()
    {
        Overgrowth act = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleForced, true);
        List<EncounterModel> bosses = act.BossDiscoveryOrder.ToList();
        Assert(bosses.Count == 1 &&
               bosses[0].Id == ModelDb.Encounter<ThingsScaleBeetleBossEncounter>().Id,
            $"Forced Scale Beetle produced {bosses.Count} boss candidate(s); expected only Scale Beetle.");
        ResetAll();

        // 无强制时：禁用项从 Boss 候选移除，其余保留。
        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogEnabled, false);
        bosses = act.BossDiscoveryOrder.ToList();
        Assert(bosses.All(b => b.Id != ModelDb.Encounter<ThingsOriginFogmogBossEncounter>().Id),
            "Disabled Origin Fogmog is still a boss candidate.");
        Assert(bosses.Any(b => b.Id == ModelDb.Encounter<ThingsScaleBeetleBossEncounter>().Id),
            "Disabling Origin Fogmog removed Scale Beetle from boss candidates.");
        ResetAll();
    }

    private static void VerifyEventGating()
    {
        Overgrowth act = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
        List<EventModel> events = act.AllEvents.ToList();
        Assert(events.Any(e => e.Id == ModelDb.Event<ThingsBackrooms>().Id),
            "The ThingsBackrooms event is missing from the default Overgrowth events.");
        Assert(events.Any(e => e.Id == ModelDb.Event<ThingsCuttingItClose>().Id),
            "Cutting It Close is missing from the default Overgrowth events.");

        ThingsModConfig.SetValue(ThingsModConfig.EventBackroomsEnabled, false);
        events = act.AllEvents.ToList();
        Assert(events.All(e => e.Id != ModelDb.Event<ThingsBackrooms>().Id),
            "The disabled ThingsBackrooms event is still offered in Overgrowth events.");
        Assert(events.Any(e => e.Id == ModelDb.Event<ThingsCuttingItClose>().Id),
            "Disabling the ThingsBackrooms event removed Cutting It Close.");
        ResetAll();

        Hive hive = (Hive)ModelDb.Act<Hive>().ToMutable();
        Assert(hive.AllEvents.Any(e => e.Id == ModelDb.Event<ThingsMedusa>().Id),
            "The ThingsMedusa event is missing from the default Hive events.");
        ThingsModConfig.SetValue(ThingsModConfig.EventMedusaEnabled, false);
        Assert(hive.AllEvents.All(e => e.Id != ModelDb.Event<ThingsMedusa>().Id),
            "The disabled ThingsMedusa event is still offered in Hive events.");
        ResetAll();
    }

    private static void VerifyCardPoolGating()
    {
        CardPoolModel ironclad = ModelDb.CardPool<IroncladCardPool>();
        CardPoolModel defect = ModelDb.CardPool<DefectCardPool>();
        CardPoolModel silent = ModelDb.CardPool<SilentCardPool>();
        CardPoolModel eventPool = ModelDb.CardPool<EventCardPool>();
        CardPoolModel tokenPool = ModelDb.CardPool<TokenCardPool>();

        ModelId collisionId = ModelDb.Card<ThingsCollision>().Id;
        ModelId reuseId = ModelDb.Card<ThingsReuse>().Id;
        ModelId soulfyshId = ModelDb.Card<ThingsSoulfyshDisease>().Id;
        ModelId recallId = ModelDb.Card<ThingsRecall>().Id;
        ModelId packUpId = ModelDb.Card<ThingsPackUp>().Id;
        ModelId surrenderId = ModelDb.Card<ThingsSurrender>().Id;
        ModelId leechParasiteId = ModelDb.Card<ThingsLeechParasite>().Id;

        static bool HasCard(CardPoolModel pool, ModelId id) =>
            pool.GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.None).Any(card => card.Id == id);

        // 1) 默认状态：5 张自制职业牌均在各自卡池中。
        Assert(HasCard(ironclad, collisionId), "ThingsCollision missing from default IroncladCardPool.");
        Assert(HasCard(defect, reuseId), "ThingsReuse missing from default DefectCardPool.");
        Assert(HasCard(silent, soulfyshId) && HasCard(silent, recallId) && HasCard(silent, packUpId),
            "Silent custom cards missing from default SilentCardPool.");
        Assert(HasCard(eventPool, surrenderId), "ThingsSurrender missing from EventCardPool.");
        Assert(HasCard(tokenPool, leechParasiteId), "ThingsLeechParasite missing from TokenCardPool.");

        // 2) 单卡禁用：仅移除对应卡牌，其余自制牌、AllCards 与 ModelDb.AllCards 保持完整。
        ThingsModConfig.SetValue(ThingsModConfig.CardSoulfyshDiseaseEnabled, false);
        Assert(!HasCard(silent, soulfyshId), "Disabled ThingsSoulfyshDisease still appeared in SilentCardPool.");
        Assert(HasCard(silent, recallId) && HasCard(silent, packUpId),
            "Disabling ThingsSoulfyshDisease removed other Silent custom cards.");
        Assert(HasCard(ironclad, collisionId) && HasCard(defect, reuseId),
            "Disabling ThingsSoulfyshDisease affected other character pools.");
        Assert(silent.AllCards.Any(card => card.Id == soulfyshId) &&
               ModelDb.AllCards.Any(card => card.Id == soulfyshId),
            "Disabling a custom card must not mutate AllCards or ModelDb.AllCards.");

        // 3) 总开关关闭：移除全部 5 张自制职业牌，但不影响事件牌/衍生牌及卡牌图鉴。
        ThingsModConfig.SetValue(ThingsModConfig.CardsEnabled, false);
        Assert(!HasCard(ironclad, collisionId) &&
               !HasCard(defect, reuseId) &&
               !HasCard(silent, soulfyshId) &&
               !HasCard(silent, recallId) &&
               !HasCard(silent, packUpId),
            "Disabling CardsEnabled left custom class cards in unlocked pools.");
        Assert(HasCard(eventPool, surrenderId),
            "Disabling CardsEnabled unexpectedly affected EventCardPool (ThingsSurrender).");
        Assert(HasCard(tokenPool, leechParasiteId),
            "Disabling CardsEnabled unexpectedly affected TokenCardPool (ThingsLeechParasite).");
        Assert(ironclad.AllCards.Any(card => card.Id == collisionId) &&
               defect.AllCards.Any(card => card.Id == reuseId) &&
               silent.AllCards.Any(card => card.Id == recallId) &&
               silent.AllCards.Any(card => card.Id == packUpId),
            "Disabling CardsEnabled must not mutate CardPoolModel.AllCards.");

        // 4) 卡牌图鉴豁免：NCardLibraryGrid.RefreshVisibility 期间保持已解锁，退出后恢复过滤。
        SaveManager.Instance.InitProfileId(1);
        SaveManager.Instance.InitProgressData();
        var libraryGrid = new NCardLibraryGrid();
        try
        {
            libraryGrid.RefreshVisibility();
            object unlockedObj = typeof(NCardLibraryGrid)
                .GetField("_unlockedCards", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(libraryGrid)!;
            HashSet<ModelId> libraryUnlockedIds = unlockedObj switch
            {
                HashSet<ModelId> ids => ids,
                HashSet<CardModel> cards => cards.Select(card => card.Id).ToHashSet(),
                _ => throw new InvalidOperationException($"Unexpected _unlockedCards type: {unlockedObj.GetType().FullName}"),
            };
            foreach (ModelId id in new[] { collisionId, reuseId, soulfyshId, recallId, packUpId })
            {
                Assert(libraryUnlockedIds.Contains(id),
                    $"Card Library hid or locked disabled custom card {id}.");
            }
        }
        finally
        {
            libraryGrid.Free();
        }
        Assert(!HasCard(ironclad, collisionId),
            "Card Library unfiltered scope leaked after RefreshVisibility completed.");

        // 5) 重新开启总开关后，单卡偏好仍保留（仅异鱼症保持禁用）。
        ThingsModConfig.SetValue(ThingsModConfig.CardsEnabled, true);
        Assert(HasCard(ironclad, collisionId) &&
               HasCard(defect, reuseId) &&
               HasCard(silent, recallId) &&
               HasCard(silent, packUpId) &&
               !HasCard(silent, soulfyshId),
            "Re-enabling CardsEnabled did not preserve individual card toggle state.");

        ResetAll();
    }

    private static void VerifyActualBossSelection()
    {
        ResetAll();
        Overgrowth cached = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
        _ = cached.AllBossEncounters.ToList();
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleForced, true);
        ThingsModConfig.SetValue(ThingsModConfig.BossScaleBeetleWeightPercent, 0);
        for (uint seed = 0; seed < 24; seed++)
        {
            Overgrowth act = seed == 0 ? cached : (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
            act.GenerateRooms(new Rng(seed), UnlockState.all);
            act.ApplyDiscoveryOrderModifications(UnlockState.all);
            Assert(act.BossEncounter is ThingsScaleBeetleBossEncounter, "Forced boss was not selected after actual room generation/discovery.");
            act.SetSecondBossEncounter(null);
            Assert(act.SecondBossEncounter is ThingsScaleBeetleBossEncounter, "Single-boss mode removed the Double Boss fight.");
        }
        ResetAll();
        ThingsModConfig.SetValue(ThingsModConfig.BossOnlyModBosses, true);
        foreach (ActModel canonical in new ActModel[] { ModelDb.Act<Overgrowth>(), ModelDb.Act<Underdocks>(), ModelDb.Act<Hive>() })
        {
            for (uint seed = 0; seed < 12; seed++)
            {
                ActModel act = (ActModel)canonical.ToMutable();
                act.GenerateRooms(new Rng(seed), UnlockState.all);
                act.ApplyDiscoveryOrderModifications(UnlockState.all);
                Assert(EncounterSelectionPolicy.IsOwnedBoss(act.BossEncounter), "Only-mod mode selected a vanilla boss.");
            }
        }
        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.BossOriginFogmogWeightPercent] = 0,
            [ThingsModConfig.BossScaleBeetleEnabled] = false,
        });
        var fallback = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
        fallback.GenerateRooms(new Rng(36), UnlockState.all);
        Assert(fallback.BossEncounter is not null && !EncounterSelectionPolicy.IsOwnedBoss(fallback.BossEncounter),
            "All-disabled / only-mod mode failed to keep a playable fallback.");
        ResetAll();

        ThingsModConfig.SetValue(ThingsModConfig.BossHumanFaceTowerForced, true);
        ThingsModConfig.SetValue(ThingsModConfig.BossHumanFaceTowerWeightPercent, 0);
        for (uint seed = 0; seed < 12; seed++)
        {
            var depths = (ThingsDepths)ModelDb.Act<ThingsDepths>().ToMutable();
            depths.GenerateRooms(new Rng(seed + 500), UnlockState.all);
            depths.ApplyDiscoveryOrderModifications(UnlockState.all);
            Assert(depths.BossEncounter is ThingsHumanFaceTowerBossEncounter,
                "Forced Human-Faced Tower was not selected in Depths.");
            depths.SetSecondBossEncounter(null);
            Assert(depths.SecondBossEncounter is ThingsHumanFaceTowerBossEncounter,
                "Forced Human-Faced Tower did not populate Double Boss in Depths.");
        }
        ResetAll();

        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.BossHumanFaceTowerForced] = true,
            [ThingsModConfig.BossCaveGodForced] = true,
            [ThingsModConfig.BossHumanFaceTowerWeightPercent] = 0,
            [ThingsModConfig.BossCaveGodWeightPercent] = 0,
        });
        var dualSeen = new HashSet<Type>();
        for (uint seed = 0; seed < 24; seed++)
        {
            var depths = (ThingsDepths)ModelDb.Act<ThingsDepths>().ToMutable();
            Assert(depths.AllBossEncounters.Select(e => e.GetType()).SequenceEqual(
                [typeof(ThingsHumanFaceTowerBossEncounter), typeof(ThingsCaveGodBossEncounter)]),
                "Dual-forced Depths + Hive bosses did not retain both bosses in Depths.");
            var rng = new Rng(seed + 900);
            depths.GenerateRooms(rng, UnlockState.all);
            depths.ApplyDiscoveryOrderModifications(UnlockState.all);
            dualSeen.Add(depths.BossEncounter.GetType());
            EncounterModel? second = rng.NextItem(depths.AllBossEncounters.Where(e => e.Id != depths.BossEncounter.Id));
            depths.SetSecondBossEncounter(second);
            Assert(depths.SecondBossEncounter is not null && depths.SecondBossEncounter.Id != depths.BossEncounter.Id,
                "Dual-forced Double Boss in Depths did not select the other forced boss.");
        }
        Assert(dualSeen.SetEquals([typeof(ThingsHumanFaceTowerBossEncounter), typeof(ThingsCaveGodBossEncounter)]),
            "Dual-forced Depths bosses did not roll both Human-Faced Tower and Cave God across seeds.");
        ResetAll();
        ThingsModConfig.SetValue(ThingsModConfig.BossAbyssalInkmawForced, true);
        ThingsModConfig.SetValue(ThingsModConfig.BossAbyssalInkmawWeightPercent, 0);
        for (uint seed = 0; seed < 12; seed++)
        {
            var depths = (ThingsDepths)ModelDb.Act<ThingsDepths>().ToMutable();
            depths.GenerateRooms(new Rng(seed + 600), UnlockState.all);
            depths.ApplyDiscoveryOrderModifications(UnlockState.all);
            Assert(depths.BossEncounter is ThingsAbyssalInkmawBossEncounter,
                "Forced Abyssal Inkmaw was not selected in Depths.");
            depths.SetSecondBossEncounter(null);
            Assert(depths.SecondBossEncounter is ThingsAbyssalInkmawBossEncounter,
                "Forced Abyssal Inkmaw did not populate Double Boss in Depths.");
        }
        ResetAll();
        ThingsModConfig.SetValues(new Dictionary<string, object?>
        {
            [ThingsModConfig.BossCaveGodEnabled] = false,
            [ThingsModConfig.BossHumanFaceTowerEnabled] = false,
            [ThingsModConfig.BossAbyssalInkmawEnabled] = false,
        });
        var depthsFallback = (ThingsDepths)ModelDb.Act<ThingsDepths>().ToMutable();
        depthsFallback.GenerateRooms(new Rng(777), UnlockState.all);
        Assert(depthsFallback.BossEncounter is KaiserCrabBoss,
            "Disabling all three Depths bosses did not fall back to Kaiser Crab.");
        ResetAll();
    }

    private static void VerifyWeightedSelection()
    {
        EncounterModel mod = ModelDb.Encounter<ThingsOriginFogmogBossEncounter>();
        EncounterModel vanilla = ModelDb.Act<Overgrowth>().BossDiscoveryOrder.First(encounter => !EncounterSelectionPolicy.IsOwnedBoss(encounter));
        EncounterModel[] pool = [mod, vanilla];
        var actualRng = new Rng(901);
        var expectedRng = new Rng(901);
        for (int i = 0; i < 100; i++)
            Assert(EncounterSelectionPolicy.ChooseBoss(actualRng, pool) == expectedRng.NextItem(pool), "Defaults changed seeded selection.");
        Assert(actualRng.NextInt() == expectedRng.NextInt(), "Defaults consumed extra randomness.");
        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogWeightPercent, 900);
        var first = new Rng(761);
        var second = new Rng(761);
        int selected = 0;
        for (int i = 0; i < 2000; i++)
        {
            EncounterModel? pick = EncounterSelectionPolicy.ChooseBoss(first, pool);
            Assert(pick == EncounterSelectionPolicy.ChooseBoss(second, pool), "Weighted selection is not seed-deterministic.");
            if (pick == mod) selected++;
        }
        Assert(selected is > 1680 and < 1900, $"900:100 weights produced {selected}/2000 mod bosses.");
        ThingsModConfig.SetValue(ThingsModConfig.BossOriginFogmogWeightPercent, 0);
        for (int i = 0; i < 100; i++)
            Assert(EncounterSelectionPolicy.ChooseBoss(first, pool) == vanilla, "Zero-weight boss was selected.");
        ResetAll();
    }

    private static void VerifyEliteFrequency()
    {
        static int CountAtRate(int rate)
        {
            ThingsModConfig.SetValue(ThingsModConfig.EncounterSoulRoesWeightPercent, rate);
            int count = 0;
            for (uint seed = 0; seed < 64; seed++)
            {
                Underdocks act = (Underdocks)ModelDb.Act<Underdocks>().ToMutable();
                act.GenerateRooms(new Rng(seed + 124), UnlockState.all);
                Underdocks replay = (Underdocks)ModelDb.Act<Underdocks>().ToMutable();
                replay.GenerateRooms(new Rng(seed + 124), UnlockState.all);
                EncounterModel? previous = null;
                for (int room = 0; room < 15; room++)
                {
                    EncounterModel next = act.PullNextEncounter(RoomType.Elite);
                    Assert(next.Id == replay.PullNextEncounter(RoomType.Elite).Id,
                        "Elite selection is not seed-deterministic.");
                    if (rate is not (0 or 100))
                        Assert(next != previous && !next.SharesTagsWith(previous),
                            "Custom elite frequency repeated a consecutive encounter or tag.");
                    if (next is ThingsSoulRoesEncounter) count++;
                    previous = next;
                    // PullNextEncounter peeks; the game advances on room entry.
                    act.MarkRoomVisited(RoomType.Elite);
                    replay.MarkRoomVisited(RoomType.Elite);
                }
            }
            return count;
        }
        int zero = CountAtRate(0), low = CountAtRate(10), standard = CountAtRate(100), high = CountAtRate(1000);
        Assert(zero == 0, $"Zero-weight elite appeared {zero} times.");
        Assert(high > low * 3 && high > standard && low < standard,
            $"Elite weights only reordered bags instead of changing frequency: low={low}, default={standard}, high={high}.");
        GD.Print($"Elite sampling: disabled={zero}, 10%={low}, default={standard}, 1000%={high} / 960 slots");
        ResetAll();
    }

    private static void VerifyMusicSetting()
    {
        EncounterModel[] bosses = [ModelDb.Encounter<ThingsOriginFogmogBossEncounter>(), ModelDb.Encounter<ThingsScaleBeetleBossEncounter>(),
            ModelDb.Encounter<ThingsGravetideSlugBossEncounter>(), ModelDb.Encounter<ThingsTheLegacyBossEncounter>(),
            ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>(), ModelDb.Encounter<ThingsCaveGodBossEncounter>(),
            ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>()];
        string[] original = bosses.Select(boss => boss.CustomBgm).ToArray();
        Assert(original.All(track => !string.IsNullOrEmpty(track)), "Default boss music disappeared.");
        ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, false);
        Assert(bosses.All(boss => boss.CustomBgm == string.Empty && !boss.HasBgm), "Music off did not use the native empty-track sentinel.");
        ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, true);
        Assert(bosses.Select(boss => boss.CustomBgm).SequenceEqual(original), "Music on did not restore the original tracks.");
    }

    private static void ResetAll()
    {
        ThingsModConfig.SetValues(ThingsModConfig.Entries.ToDictionary(entry => entry.Key, entry => (object?)entry.Default));
    }

    // ---- 脚手架（沿用 ModelId 探针模式）----

    private static void MountPublishedPck()
    {
        string[] args = OS.GetCmdlineUserArgs();
        Assert(args.Length >= 1, "Probe requires an absolute PCK path.");
        Assert(ProjectSettings.LoadResourcePack(args[0], replaceFiles: true),
            $"Could not mount published PCK: {args[0]}");
    }

    private static void RegisterSyntheticMod(Assembly implementationAssembly)
    {
        IList mods = (IList)typeof(ModManager).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        mods.Clear();
        mods.Add(CreateSyntheticMod("STS2_Things", "STS2_Things Config Probe", implementationAssembly));
        if (OS.GetCmdlineUserArgs().Length > 1 && !OS.GetCmdlineUserArgs().Contains("--neow-only"))
            mods.Add(CreateSyntheticMod("ConfigNetworkProbe", "Config Network Probe", typeof(ThingsConfigProbeNode).Assembly));
    }

    private static object CreateSyntheticMod(string id, string name, Assembly assembly)
    {
        Assembly gameAssembly = typeof(ModManager).Assembly;
        Type modType = gameAssembly.GetType("MegaCrit.Sts2.Core.Modding.Mod", throwOnError: true)!;
        Type manifestType = gameAssembly.GetType("MegaCrit.Sts2.Core.Modding.ModManifest", throwOnError: true)!;
        object mod = Activator.CreateInstance(modType)!;
        object manifest = Activator.CreateInstance(manifestType)!;
        manifestType.GetField("id")!.SetValue(manifest, id);
        manifestType.GetField("name")?.SetValue(manifest, name);
        manifestType.GetField("affectsGameplay")?.SetValue(manifest, true);
        modType.GetField("path")!.SetValue(mod, $"probe://{id}");
        modType.GetField("manifest")!.SetValue(mod, manifest);
        FieldInfo state = modType.GetField("state")!;
        state.SetValue(mod, Enum.Parse(state.FieldType, "Loaded"));
        if (modType.GetField("assemblies")?.GetValue(mod) is IList assemblies)
            assemblies.Add(assembly);
        else
            modType.GetField("assembly")!.SetValue(mod, assembly);
        return mod;
    }

    private static void InitializeModelDb(Assembly implementationAssembly)
    {
        Type[] implementationTypes = implementationAssembly.GetTypes();
        Type[] modTypes = OS.GetCmdlineUserArgs().Length > 1 && !OS.GetCmdlineUserArgs().Contains("--neow-only")
            ? [.. implementationTypes, typeof(ConfigNetworkProbeMessage)] : implementationTypes;
        Type[] modelTypes = [
            .. AbstractModelSubtypes.All,
            .. implementationTypes.Where(type => !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type))
        ];
        modelTypes = modelTypes.Distinct().ToArray();
        typeof(ReflectionHelper).GetField("_modTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, modTypes);
        SetModManagerStateInitialized();
        Type? assemblyInfo = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo");
        assemblyInfo?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        typeof(ModelDb).GetMethod("ResetForTest", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
        MethodInfo init = typeof(ModelDb).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [modelTypes]);
        Type serializationCache = typeof(ModelDb).Assembly.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache",
            throwOnError: true)!;
        serializationCache.GetMethod("Init", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
    }

    private static void SetModManagerStateInitialized()
    {
        PropertyInfo? state = typeof(ModManager).GetProperty("State", BindingFlags.Public | BindingFlags.Static);
        if (state != null)
        {
            state.SetValue(null, Enum.Parse(state.PropertyType, "Initialized"));
        }
    }

    private static void EnsureRuntimeDependency(string assemblyName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(
                assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
        {
            return;
        }

        string? path = FindRuntimeDependencyPath(assemblyName);
        if (path != null)
            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    private static Assembly? ResolveRuntimeDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;
        string? path = FindRuntimeDependencyPath(assemblyName.Name);
        return path == null ? null : context.LoadFromAssemblyPath(path);
    }

    private static string? FindRuntimeDependencyPath(string assemblyName)
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? assemblyDirectory = Path.GetDirectoryName(typeof(ThingsConfigProbeNode).Assembly.Location);
        string[] candidates =
        [
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug", $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release", $"{assemblyName}.dll"),
            Path.Combine(assemblyDirectory ?? string.Empty, $"{assemblyName}.dll"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Things config probe: " + message);
    }
}
