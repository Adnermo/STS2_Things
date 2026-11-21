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
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.TestSupport;
using STS2_Things;
using STS2_Things.Cards;
using STS2_Things.Enchantments;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Monsters;
using STS2_Things.Powers;
using STS2_Things.Relics;
using STS2_Things.Modifiers;
using STS2_Things.Platform;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Hoursmod
{
    public sealed class Recall : CardModel
    {
        public Recall() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
        {
        }
    }
}

public partial class ThingsModelIdProbeNode : Node
{
    private sealed record ModelExpectation(
        Type Type,
        string Category,
        string Entry,
        string LocalizationTable,
        string LocalizationSuffix);

    private static readonly ModelExpectation[] RenamedModels =
    [
        new(typeof(ThingsRecall), "CARD", "THINGS_RECALL", "cards", "title"),
        new(typeof(ThingsReuse), "CARD", "THINGS_REUSE", "cards", "title"),
        new(typeof(ThingsSurrender), "CARD", "THINGS_SURRENDER", "cards", "title"),
        new(typeof(ThingsPackUp), "CARD", "THINGS_PACK_UP", "cards", "title"),
        new(typeof(ThingsDisperse), "ENCHANTMENT", "THINGS_DISPERSE", "enchantments", "title"),
        new(typeof(ThingsBackrooms), "EVENT", "THINGS_BACKROOMS", "events", "title"),
        new(typeof(ThingsMedusa), "EVENT", "THINGS_MEDUSA", "events", "title"),
        new(typeof(ThingsAlmondWater), "RELIC", "THINGS_ALMOND_WATER", "relics", "title"),
        new(typeof(ThingsCurseRemover), "RELIC", "THINGS_CURSE_REMOVER", "relics", "title"),
        new(typeof(ThingsMagicGlove), "RELIC", "THINGS_MAGIC_GLOVE", "relics", "title"),
        new(typeof(ThingsMedusaHair), "RELIC", "THINGS_MEDUSA_HAIR", "relics", "title"),
        new(typeof(ThingsWhiteFlag), "RELIC", "THINGS_WHITE_FLAG", "relics", "title"),
        new(typeof(ThingsDazedPower), "POWER", "THINGS_DAZED_POWER", "powers", "title"),
        new(typeof(ThingsOriginPower), "POWER", "THINGS_ORIGIN_POWER", "powers", "title"),
        new(typeof(ThingsQuirkPower), "POWER", "THINGS_QUIRK_POWER", "powers", "title"),
        new(typeof(ThingsRecallPower), "POWER", "THINGS_RECALL_POWER", "powers", "title"),
        new(typeof(ThingsReusePower), "POWER", "THINGS_REUSE_POWER", "powers", "title"),
        new(typeof(ThingsScaleBeetlePower), "POWER", "THINGS_SCALE_BEETLE_POWER", "powers", "title"),
        new(typeof(ThingsScaleDownPower), "POWER", "THINGS_SCALE_DOWN_POWER", "powers", "title"),
        new(typeof(ThingsScaleUpPower), "POWER", "THINGS_SCALE_UP_POWER", "powers", "title"),
        new(typeof(ThingsScaleBeetle), "MONSTER", "THINGS_SCALE_BEETLE", "monsters", "name"),
        new(typeof(ThingsTheLegacy), "MONSTER", "THINGS_THE_LEGACY", "monsters", "name")
    ];

    private static Type _hoursmodRecallType = typeof(Hoursmod.Recall);

    public override void _Ready()
    {
        try
        {
            TestMode.TurnOnInternal();
            MountPublishedPck();
            Assembly implementationAssembly = typeof(STS2_ThingsInit).Assembly;
            AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
            EnsureRuntimeDependency("System.IO.Hashing");
            RegisterSyntheticMods(implementationAssembly);
            STS2_ThingsInit.Initialize();
            InitializeModelDb(implementationAssembly);
            if (OS.GetCmdlineUserArgs().Contains("--crossroad-ledger"))
            {
                VerifyCrossroadLedger();
                GD.Print("CROSSROADS_SAVE_COMPAT_PASS native modifier JSON/network roundtrip");
                GetTree().Quit(0);
                return;
            }
            VerifyHoursmodFixtureCoexists();
            VerifyAllConcreteModelsAndRetiredTypes(implementationAssembly);
            VerifyMutableCloneIsolation(implementationAssembly);
            VerifyLegacyIdMigrator();
            VerifyRenamedModelIds();
            VerifyPools();
            VerifyLocalization();
            VerifyDerivedResources();
            GD.Print("Things ModelId namespace probe: PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static void MountPublishedPck()
    {
        string[] args = OS.GetCmdlineUserArgs().Where(a => a != "--crossroad-ledger").ToArray();
        Assert(args.Length is 1 or 2,
            "Probe requires an absolute PCK path and accepts an optional Hoursmod DLL path.");
        Assert(ProjectSettings.LoadResourcePack(args[0], replaceFiles: true),
            $"Could not mount published PCK: {args[0]}");
        if (args.Length == 2)
        {
            string hoursmodDll = Path.GetFullPath(args[1]);
            Assert(File.Exists(hoursmodDll), $"Actual Hoursmod DLL was not found: {hoursmodDll}");
            AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(typeof(ModelDb).Assembly)
                ?? AssemblyLoadContext.Default;
            Assembly assembly = loadContext.LoadFromAssemblyPath(hoursmodDll);
            Type? recallType = assembly.GetType("Hoursmod.Recall", throwOnError: false);
            if (recallType == null || !typeof(CardModel).IsAssignableFrom(recallType))
            {
                string actualBase = recallType?.BaseType?.AssemblyQualifiedName ?? "<missing>";
                throw new InvalidOperationException(
                    $"Actual Hoursmod Recall base does not bind to the active CardModel. " +
                    $"Expected {typeof(CardModel).AssemblyQualifiedName}; actual {actualBase}; DLL={hoursmodDll}");
            }
            _hoursmodRecallType = recallType;
            GD.Print($"ModelId probe loaded actual Hoursmod fixture: {hoursmodDll}");
        }
        else if (typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Models.Cards.Recall", throwOnError: false) is { } nativeRecall)
        {
            _hoursmodRecallType = nativeRecall;
        }
    }

    private static void InitializeModelDb(Assembly implementationAssembly)
    {
        Type[] implementationTypes = implementationAssembly.GetTypes();
        bool injectSyntheticRecall = _hoursmodRecallType.Assembly != typeof(ModelDb).Assembly;
        Type[] modTypes = injectSyntheticRecall
            ? implementationTypes.Concat([_hoursmodRecallType]).ToArray()
            : implementationTypes;
        Type[] modelTypes = injectSyntheticRecall
            ? [
                _hoursmodRecallType,
                .. AbstractModelSubtypes.All,
                .. implementationTypes.Where(type => !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type))
            ]
            : [
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

    private static void VerifyCrossroadLedger()
    {
        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionTypes).GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        var action = new STS2_Things.Map.ThingsNetUnlockCrossroadAction
        {
            Act = 1, Fingerprint = 123456, From = new(1, 6), To = new(3, 6), Price = 50,
        };
        Assert(MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionTypes.ToId(action) >= 0, "Unlock action was not registered");
        var actionWriter = new PacketWriter(); action.Serialize(actionWriter);
        var actionReader = new PacketReader(); actionReader.Reset(actionWriter.Buffer);
        var decodedAction = new STS2_Things.Map.ThingsNetUnlockCrossroadAction(); decodedAction.Deserialize(actionReader);
        Assert(decodedAction.Fingerprint == action.Fingerprint && decodedAction.From == action.From &&
            decodedAction.To == action.To && decodedAction.Price == 50, "Unlock action packet changed");
        var ledger = (ThingsCrossroads)ModelDb.Modifier<ThingsCrossroads>().ToMutable();
        ledger.Data = "{\"Version\":1,\"Acts\":[{\"Act\":0,\"Fingerprint\":123,\"Roads\":[{\"Row\":6,\"Left\":1,\"Right\":3,\"FromColumn\":1,\"Payer\":2}],\"History\":[{\"col\":1,\"row\":6},{\"col\":3,\"row\":6}]}]}";
        var save = ledger.ToSerializable();
        var writer = new PacketWriter(); save.Serialize(writer);
        var reader = new PacketReader(); reader.Reset(writer.Buffer);
        var decoded = new SerializableModifier(); decoded.Deserialize(reader);
        var restored = (ThingsCrossroads)ModifierModel.FromSerializable(decoded);
        Assert(restored.Data == ledger.Data, "Route ledger lost data in native save/network serialization");
        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(save, options);
        var jsonSave = JsonSerializer.Deserialize<SerializableModifier>(json, options)!;
        var jsonRestored = (ThingsCrossroads)ModifierModel.FromSerializable(jsonSave);
        Assert(jsonRestored.Data == ledger.Data, "JSON save omitted route state");
    }

    private static void VerifyHoursmodFixtureCoexists()
    {
        ModelId hoursmodId = ModelDb.GetId(_hoursmodRecallType);
        ModelId thingsId = ModelDb.GetId<ThingsRecall>();
        Assert(hoursmodId.ToString() == "CARD.RECALL", $"Hoursmod fixture ID drifted to {hoursmodId}.");
        Assert(thingsId.ToString() == "CARD.THINGS_RECALL", $"Things Recall ID drifted to {thingsId}.");
        Assert(ModelDb.GetById<CardModel>(hoursmodId).GetType() == _hoursmodRecallType,
            "Hoursmod fixture was overwritten by Things Recall.");
        Assert(ModelDb.Card<ThingsRecall>().GetType() == typeof(ThingsRecall),
            "Things Recall was overwritten by Hoursmod fixture.");
    }

    private static void VerifyAllConcreteModelsAndRetiredTypes(Assembly implementationAssembly)
    {
        Type[] allTypes = implementationAssembly.GetTypes();
        string[] retiredShortNames =
        [
            "HumanFaceColumnWeak",
            "CaveGodMartialTrial",
            "CaveGodArcaneTrial",
            "CaveGodMartialPower",
            "CaveGodArcanePower",
            "ThingsHumanFaceColumnWeak",
            "ThingsCaveGodMartialTrial",
            "ThingsCaveGodArcaneTrial",
            "ThingsCaveGodMartialPower",
            "ThingsCaveGodArcanePower",
            "ThingsCaveGodCrystalShard",
            "ThingsCaveGodCrystalVeinPower",
            "ThingsCaveGodFissurePower",
            "ThingsSpongeLeechWeak",
            "ThingsSilkMothWeak",
            "ThingsSilkMothEncounter",
            "ThingsCaveMawEncounter",
            "ThingsCaveMawSnailEncounter",
            "ThingsSpongeSnailEncounter",
            "ThingsLanternMothEncounter"
        ];
        foreach (string retiredName in retiredShortNames)
        {
            Assert(!allTypes.Any(type => string.Equals(type.Name, retiredName, StringComparison.Ordinal)),
                $"Retired model type {retiredName} still exists in {implementationAssembly.GetName().Name}.");
        }

        Type[] concreteModels = allTypes
            .Where(type => !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        int expectedFromAssembly = allTypes.Count(type =>
            !type.IsAbstract &&
            typeof(AbstractModel).IsAssignableFrom(type) &&
            ModelDb.GetByIdOrNull<AbstractModel>(ModelDb.GetId(type))?.GetType() == type);
        Assert(concreteModels.Length >= 189 && concreteModels.Length == expectedFromAssembly,
            $"Expected >= 189 registered concrete AbstractModel subclasses in STS2_Things, found {concreteModels.Length} (ModelDb matched {expectedFromAssembly}).");
        Assert(concreteModels.Select(ModelDb.GetId).Distinct().Count() == concreteModels.Length,
            "Duplicate ModelId detected among concrete AbstractModel subclasses in STS2_Things.");
        Assert(concreteModels.Contains(typeof(ThingsMerchantBargainPricePolicy)),
            "ThingsMerchantBargainPricePolicy is missing from concrete AbstractModel subclasses.");

        foreach (Type modelType in concreteModels)
        {
            Assert(modelType.Name.StartsWith("Things", StringComparison.Ordinal),
                $"Concrete model class {modelType.FullName} does not start with 'Things'.");
            ModelId id = ModelDb.GetId(modelType);
            Assert(id.Entry.StartsWith("THINGS_", StringComparison.Ordinal),
                $"Concrete model {modelType.FullName} generated unprefixed ModelId {id}.");
            AbstractModel? canonical = ModelDb.GetByIdOrNull<AbstractModel>(id);
            Assert(canonical != null && canonical.GetType() == modelType,
                $"ModelDb lookup for {id} returned {canonical?.GetType().FullName ?? "<null>"}, expected {modelType.FullName}.");
        }
    }

    private static void VerifyMutableCloneIsolation(Assembly implementationAssembly)
    {
        Type[] concreteModels = implementationAssembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (Type modelType in concreteModels)
        {
            ModelId id = ModelDb.GetId(modelType);
            AbstractModel canonical = ModelDb.GetById<AbstractModel>(id);
            Assert(canonical.IsCanonical && !canonical.IsMutable,
                $"Canonical model {id} ({modelType.FullName}) was unexpectedly mutable.");

            AbstractModel cloneA = canonical.MutableClone();
            AbstractModel cloneB = canonical.MutableClone();
            Assert(!ReferenceEquals(canonical, cloneA) && !ReferenceEquals(cloneA, cloneB),
                $"MutableClone() returned a shared instance for {id} ({modelType.FullName}).");
            Assert(cloneA.IsMutable && !cloneA.IsCanonical && cloneB.IsMutable && !cloneB.IsCanonical,
                $"MutableClone() did not mark clones mutable for {id} ({modelType.FullName}).");
            Assert(canonical.IsCanonical && !canonical.IsMutable,
                $"MutableClone() mutated canonical flag on {id} ({modelType.FullName}).");
            Assert(cloneA.Id == id && cloneB.Id == id,
                $"MutableClone() altered ModelId on {modelType.FullName}.");

            MethodInfo? toMutable = modelType.GetMethod(
                "ToMutable",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (toMutable != null && typeof(AbstractModel).IsAssignableFrom(toMutable.ReturnType))
            {
                var mutableViaTyped = (AbstractModel)toMutable.Invoke(canonical, null)!;
                Assert(!ReferenceEquals(canonical, mutableViaTyped) &&
                       !ReferenceEquals(cloneA, mutableViaTyped) &&
                       mutableViaTyped.IsMutable &&
                       canonical.IsCanonical,
                    $"ToMutable() violated canonical/mutable isolation for {id} ({modelType.FullName}).");
            }
        }

        ThingsCollision canonicalCollision = ModelDb.Card<ThingsCollision>();
        var mutableCollision = (ThingsCollision)canonicalCollision.ToMutable();
        mutableCollision.UpgradeInternal();
        mutableCollision.FinalizeUpgradeInternal();
        Assert(mutableCollision.CurrentUpgradeLevel == 1 && canonicalCollision.CurrentUpgradeLevel == 0,
            "Upgrading mutable ThingsCollision leaked state onto canonical template.");

        ThingsCrossroads canonicalCrossroads = ModelDb.Modifier<ThingsCrossroads>();
        var mutableCrossroads = (ThingsCrossroads)canonicalCrossroads.ToMutable();
        mutableCrossroads.Data = "{\"probe\":true}";
        Assert(string.IsNullOrEmpty(canonicalCrossroads.Data) && mutableCrossroads.Data == "{\"probe\":true}",
            "Mutating mutable ThingsCrossroads leaked Data onto canonical template.");
    }

    private static void VerifyLegacyIdMigrator()
    {
        Assert(LegacyIdMigrator.TotalDeclaredCount == 136,
            $"Expected 136 declared legacy ID mappings, found {LegacyIdMigrator.TotalDeclaredCount}.");
        Assert(LegacyIdMigrator.AllDeclaredMappings.Count == 136,
            $"AllDeclaredMappings count {LegacyIdMigrator.AllDeclaredMappings.Count} did not match 136.");
        Assert(LegacyIdMigrator.AllDeclaredMappings.Count(mapping => mapping.IsRetired) == 22,
            "Expected exactly 22 retired model mappings in LegacyIdMigrator.");

        LegacyIdMigrator.ActivateIfModelDbReady();
        Assert(LegacyIdMigrator.IsActive, "LegacyIdMigrator was not active after ModelDb initialization.");
        Assert(LegacyIdMigrator.PrunedCollisions.Count == 0,
            $"Unexpected pruned legacy ID collisions under clean ModelDb: {LegacyIdMigrator.PrunedCollisions.Count}.");
        Assert(LegacyIdMigrator.ActiveMigrationCount == 136,
            $"Expected 136 active legacy ID mappings, found {LegacyIdMigrator.ActiveMigrationCount}.");

        // Verify both ModelId.Deserialize(...) and new ModelId(...) roundtrips plus retired remaps.
        Assert(ModelId.Deserialize("CARD.SOULFYSH_DISEASE") == new ModelId("CARD", "THINGS_SOULFYSH_DISEASE"),
            "ModelId.Deserialize did not migrate CARD.SOULFYSH_DISEASE to CARD.THINGS_SOULFYSH_DISEASE.");
        Assert(ModelId.Deserialize("EVENT.ROBBERY_FAKE_MERCHANT") == new ModelId("EVENT", "THINGS_ROBBERY_FAKE_MERCHANT"),
            "ModelId.Deserialize did not migrate EVENT.ROBBERY_FAKE_MERCHANT.");
        Assert(new ModelId("MONSTER", "ORIGIN_FOGMOG").ToString() == "MONSTER.THINGS_ORIGIN_FOGMOG",
            "new ModelId(\"MONSTER\", \"ORIGIN_FOGMOG\") did not migrate to MONSTER.THINGS_ORIGIN_FOGMOG.");
        Assert(ModelId.Deserialize("CARD.CAVE_GOD_BROKEN_BLADE_TRIAL").ToString() == "CARD.THINGS_CAVE_GOD_BROKEN_BLADE_TRIAL",
            "Legacy CARD.CAVE_GOD_BROKEN_BLADE_TRIAL did not migrate to CARD.THINGS_CAVE_GOD_BROKEN_BLADE_TRIAL.");
        Assert(ModelId.Deserialize("CARD.CAVE_GOD_SHATTERED_SHIELD_TRIAL").ToString() == "CARD.THINGS_CAVE_GOD_SHATTERED_SHIELD_TRIAL",
            "Legacy CARD.CAVE_GOD_SHATTERED_SHIELD_TRIAL did not migrate to CARD.THINGS_CAVE_GOD_SHATTERED_SHIELD_TRIAL.");
        Assert(new ModelId("POWER", "CAVE_GOD_BROKEN_BLADE_POWER").ToString() == "POWER.THINGS_CAVE_GOD_BROKEN_BLADE_POWER",
            "Legacy POWER.CAVE_GOD_BROKEN_BLADE_POWER did not migrate to POWER.THINGS_CAVE_GOD_BROKEN_BLADE_POWER.");
        Assert(new ModelId("POWER", "CAVE_GOD_SHATTERED_SHIELD_POWER").ToString() == "POWER.THINGS_CAVE_GOD_SHATTERED_SHIELD_POWER",
            "Legacy POWER.CAVE_GOD_SHATTERED_SHIELD_POWER did not migrate to POWER.THINGS_CAVE_GOD_SHATTERED_SHIELD_POWER.");
        Assert(new ModelId("POWER", "CAVE_GOD_PENDING_TRIAL_POWER").ToString() == "POWER.THINGS_CAVE_GOD_PENDING_TRIAL_POWER",
            "Legacy POWER.CAVE_GOD_PENDING_TRIAL_POWER did not migrate to POWER.THINGS_CAVE_GOD_PENDING_TRIAL_POWER.");
        Assert(ModelId.Deserialize("CARD.CAVE_GOD_MARTIAL_TRIAL").ToString() == "CARD.THINGS_SNAIL_CRYSTAL_CHIP",
            "Retired CARD.CAVE_GOD_MARTIAL_TRIAL did not remap to CARD.THINGS_SNAIL_CRYSTAL_CHIP.");
        Assert(ModelId.Deserialize("CARD.CAVE_GOD_ARCANE_TRIAL").ToString() == "CARD.THINGS_SNAIL_CRYSTAL_CHIP",
            "Retired CARD.CAVE_GOD_ARCANE_TRIAL did not remap to CARD.THINGS_SNAIL_CRYSTAL_CHIP.");
        Assert(ModelId.Deserialize("CARD.THINGS_CAVE_GOD_CRYSTAL_SHARD").ToString() == "CARD.THINGS_SNAIL_CRYSTAL_CHIP",
            "Retired CARD.THINGS_CAVE_GOD_CRYSTAL_SHARD did not remap to CARD.THINGS_SNAIL_CRYSTAL_CHIP.");
        Assert(new ModelId("POWER", "CAVE_GOD_MARTIAL_POWER").ToString() == "POWER.THINGS_CAVE_GOD_ANCIENT_CORE_POWER",
            "Retired POWER.CAVE_GOD_MARTIAL_POWER did not remap to POWER.THINGS_CAVE_GOD_ANCIENT_CORE_POWER.");
        Assert(new ModelId("POWER", "CAVE_GOD_ARCANE_POWER").ToString() == "POWER.THINGS_CAVE_GOD_ANCIENT_CORE_POWER",
            "Retired POWER.CAVE_GOD_ARCANE_POWER did not remap to POWER.THINGS_CAVE_GOD_ANCIENT_CORE_POWER.");
        Assert(new ModelId("POWER", "THINGS_CAVE_GOD_CRYSTAL_VEIN_POWER").ToString() == "POWER.THINGS_CAVE_GOD_AGING_POWER",
            "Retired POWER.THINGS_CAVE_GOD_CRYSTAL_VEIN_POWER did not remap to POWER.THINGS_CAVE_GOD_AGING_POWER.");
        Assert(new ModelId("ENCOUNTER", "HUMAN_FACE_COLUMN_WEAK").ToString() == "ENCOUNTER.THINGS_HUMAN_FACE_COLUMN_ENCOUNTER",
            "Retired ENCOUNTER.HUMAN_FACE_COLUMN_WEAK did not remap to ENCOUNTER.THINGS_HUMAN_FACE_COLUMN_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.SPONGE_LEECH_WEAK").ToString() == "ENCOUNTER.THINGS_SANGUINE_LEECH_WEAK",
            "Retired ENCOUNTER.SPONGE_LEECH_WEAK did not remap to ENCOUNTER.THINGS_SANGUINE_LEECH_WEAK.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_SPONGE_LEECH_WEAK").ToString() == "ENCOUNTER.THINGS_SANGUINE_LEECH_WEAK",
            "Retired ENCOUNTER.THINGS_SPONGE_LEECH_WEAK did not remap to ENCOUNTER.THINGS_SANGUINE_LEECH_WEAK.");
        Assert(ModelId.Deserialize("ENCOUNTER.SILK_MOTH_WEAK").ToString() == "ENCOUNTER.THINGS_SNAIL_TRIO_WEAK",
            "Retired ENCOUNTER.SILK_MOTH_WEAK did not remap to ENCOUNTER.THINGS_SNAIL_TRIO_WEAK.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_SILK_MOTH_WEAK").ToString() == "ENCOUNTER.THINGS_SNAIL_TRIO_WEAK",
            "Retired ENCOUNTER.THINGS_SILK_MOTH_WEAK did not remap to ENCOUNTER.THINGS_SNAIL_TRIO_WEAK.");
        Assert(ModelId.Deserialize("ENCOUNTER.SILK_MOTH_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_SILK_MOTH_TRIO",
            "Retired ENCOUNTER.SILK_MOTH_ENCOUNTER did not remap to ENCOUNTER.THINGS_SILK_MOTH_TRIO.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_SILK_MOTH_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_SILK_MOTH_TRIO",
            "Retired ENCOUNTER.THINGS_SILK_MOTH_ENCOUNTER did not remap to ENCOUNTER.THINGS_SILK_MOTH_TRIO.");
        Assert(ModelId.Deserialize("ENCOUNTER.CAVE_MAW_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER",
            "Retired ENCOUNTER.CAVE_MAW_ENCOUNTER did not remap to ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_CAVE_MAW_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER",
            "Retired ENCOUNTER.THINGS_CAVE_MAW_ENCOUNTER did not remap to ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.CAVE_MAW_SNAIL_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER",
            "Retired ENCOUNTER.CAVE_MAW_SNAIL_ENCOUNTER did not remap to ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_CAVE_MAW_SNAIL_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER",
            "Retired ENCOUNTER.THINGS_CAVE_MAW_SNAIL_ENCOUNTER did not remap to ENCOUNTER.THINGS_CAVE_MAW_LANTERN_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.SPONGE_SNAIL_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_SPONGE_LEECH_ENCOUNTER",
            "Retired ENCOUNTER.SPONGE_SNAIL_ENCOUNTER did not remap to ENCOUNTER.THINGS_SPONGE_LEECH_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_SPONGE_SNAIL_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_SPONGE_LEECH_ENCOUNTER",
            "Retired ENCOUNTER.THINGS_SPONGE_SNAIL_ENCOUNTER did not remap to ENCOUNTER.THINGS_SPONGE_LEECH_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.LANTERN_MOTH_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_LANTERN_SPONGE_ENCOUNTER",
            "Retired ENCOUNTER.LANTERN_MOTH_ENCOUNTER did not remap to ENCOUNTER.THINGS_LANTERN_SPONGE_ENCOUNTER.");
        Assert(ModelId.Deserialize("ENCOUNTER.THINGS_LANTERN_MOTH_ENCOUNTER").ToString() == "ENCOUNTER.THINGS_LANTERN_SPONGE_ENCOUNTER",
            "Retired ENCOUNTER.THINGS_LANTERN_MOTH_ENCOUNTER did not remap to ENCOUNTER.THINGS_LANTERN_SPONGE_ENCOUNTER.");

        foreach (LegacyIdMigrator.LegacyMapping mapping in LegacyIdMigrator.AllDeclaredMappings)
        {
            ModelId migrated = ModelId.Deserialize($"{mapping.Category}.{mapping.LegacyEntry}");
            Assert(migrated.Category == mapping.Category && migrated.Entry == mapping.NewEntry,
                $"Legacy mapping {mapping.Category}.{mapping.LegacyEntry} migrated to {migrated}, expected {mapping.Category}.{mapping.NewEntry}.");
            Assert(ModelDb.GetByIdOrNull<AbstractModel>(migrated) != null,
                $"Legacy mapping target {migrated} does not resolve in ModelDb.");
        }

        // Canonical THINGS_* IDs and external-mod IDs pass through unchanged.
        Assert(new ModelId("CARD", "THINGS_REUSE").Entry == "THINGS_REUSE",
            "Canonical THINGS_REUSE entry was altered by LegacyIdMigrator.");
        Assert(new ModelId("CARD", "RECALL").Entry == "RECALL",
            "External-mod CARD.RECALL entry was unexpectedly altered by LegacyIdMigrator.");

        // Verify collision pruning when an external mod owns a legacy entry.
        try
        {
            LegacyIdMigrator.RebuildAndActivate(
                static (category, entry) => category == "CARD" && entry == "SOULFYSH_DISEASE");
            Assert(LegacyIdMigrator.IsActive, "LegacyIdMigrator should remain active after collision pruning.");
            Assert(LegacyIdMigrator.PrunedCollisions.Any(
                    mapping => mapping.Category == "CARD" && mapping.LegacyEntry == "SOULFYSH_DISEASE"),
                "PrunedCollisions did not record synthetic external-mod collision CARD.SOULFYSH_DISEASE.");
            Assert(LegacyIdMigrator.ActiveMigrationCount == 135,
                $"Expected 135 active mappings with 1 pruned collision, found {LegacyIdMigrator.ActiveMigrationCount}.");
            Assert(new ModelId("CARD", "SOULFYSH_DISEASE").Entry == "SOULFYSH_DISEASE",
                "Colliding external-mod ID CARD.SOULFYSH_DISEASE was still rewritten after pruning.");
            Assert(new ModelId("MONSTER", "ORIGIN_FOGMOG").Entry == "THINGS_ORIGIN_FOGMOG",
                "Non-colliding legacy ID MONSTER.ORIGIN_FOGMOG stopped migrating during collision test.");
        }
        finally
        {
            LegacyIdMigrator.RebuildAndActivate();
        }

        Assert(LegacyIdMigrator.PrunedCollisions.Count == 0 && LegacyIdMigrator.ActiveMigrationCount == 136,
            "LegacyIdMigrator did not restore full 136-mapping state after collision test.");
    }

    private static void VerifyRenamedModelIds()
    {
        foreach (ModelExpectation expectation in RenamedModels)
        {
            ModelId id = ModelDb.GetId(expectation.Type);
            Assert(id.Category == expectation.Category && id.Entry == expectation.Entry,
                $"{expectation.Type.FullName} generated {id}, expected " +
                $"{expectation.Category}.{expectation.Entry}.");
            AbstractModel canonical = ModelDb.GetById<AbstractModel>(id);
            Assert(canonical.GetType() == expectation.Type,
                $"{id} resolves to {canonical.GetType().FullName}, expected {expectation.Type.FullName}.");
        }
    }

    private static void VerifyPools()
    {
        VerifyCardPool<DefectCardPool, ThingsReuse>();
        VerifyCardPool<EventCardPool, ThingsSurrender>();
        VerifyCardPool<SilentCardPool, ThingsRecall>();
        VerifyCardPool<SilentCardPool, ThingsPackUp>();
        VerifyCardPool<TokenCardPool, ThingsCaveGodBrokenBladeTrial>();
        VerifyCardPool<TokenCardPool, ThingsCaveGodShatteredShieldTrial>();
        VerifyRelicPool<EventRelicPool, ThingsWhiteFlag>();
        VerifyRelicPool<EventRelicPool, ThingsCurseRemover>();
        VerifyRelicPool<EventRelicPool, ThingsMagicGlove>();
        VerifyRelicPool<EventRelicPool, ThingsAlmondWater>();
        VerifyRelicPool<EventRelicPool, ThingsMedusaHair>();
    }

    private static void VerifyCardPool<TPool, TCard>()
        where TPool : CardPoolModel
        where TCard : CardModel
    {
        Assert(ModelDb.CardPool<TPool>().AllCards.Any(card => card is TCard),
            $"{typeof(TCard).Name} is missing from {typeof(TPool).Name}.");
    }

    private static void VerifyRelicPool<TPool, TRelic>()
        where TPool : RelicPoolModel
        where TRelic : RelicModel
    {
        Assert(ModelDb.RelicPool<TPool>().AllRelics.Any(relic => relic is TRelic),
            $"{typeof(TRelic).Name} is missing from {typeof(TPool).Name}.");
    }

    private static void VerifyLocalization()
    {
        foreach (string language in new[] { "eng", "zhs" })
        {
            var tables = new Dictionary<string, JsonDocument>(StringComparer.Ordinal);
            try
            {
                foreach (ModelExpectation expectation in RenamedModels)
                {
                    if (!tables.TryGetValue(expectation.LocalizationTable, out JsonDocument? document))
                    {
                        string path = $"res://STS2_Things/localization/{language}/{expectation.LocalizationTable}.json";
                        Assert(GodotFileAccess.FileExists(path), $"Missing localization table in PCK: {path}");
                        document = JsonDocument.Parse(GodotFileAccess.GetFileAsString(path));
                        tables.Add(expectation.LocalizationTable, document);
                    }

                    string key = $"{expectation.Entry}.{expectation.LocalizationSuffix}";
                    Assert(document.RootElement.TryGetProperty(key, out JsonElement value) &&
                           value.ValueKind == JsonValueKind.String &&
                           !string.IsNullOrWhiteSpace(value.GetString()),
                        $"Missing non-empty localization key {key} in {language}/{expectation.LocalizationTable}.json.");

                    string legacyEntry = expectation.Entry["THINGS_".Length..];
                    Assert(!document.RootElement.EnumerateObject().Any(
                            property => property.Name.StartsWith(legacyEntry + ".", StringComparison.Ordinal)),
                        $"Legacy localization keys remain for {legacyEntry} in {language}/{expectation.LocalizationTable}.json.");
                }
            }
            finally
            {
                foreach (JsonDocument document in tables.Values)
                {
                    document.Dispose();
                }
            }
        }
    }

    private static void VerifyDerivedResources()
    {
        VerifyTexture("res://images/packed/card_portraits/defect/things_reuse.png");
        VerifyTexture("res://images/packed/card_portraits/event/things_surrender.png");
        VerifyTexture("res://images/packed/card_portraits/silent/things_recall.png");
        VerifyTexture("res://images/packed/card_portraits/silent/things_pack_up.png");
        VerifyTexture("res://images/enchantments/things_disperse.png");
        VerifyTexture("res://images/events/things_backrooms.png");
        VerifyTexture("res://images/events/things_medusa.png");

        foreach (RelicModel relic in new RelicModel[]
                 {
                     ModelDb.Relic<ThingsAlmondWater>(),
                     ModelDb.Relic<ThingsCurseRemover>(),
                     ModelDb.Relic<ThingsMagicGlove>(),
                     ModelDb.Relic<ThingsMedusaHair>(),
                     ModelDb.Relic<ThingsWhiteFlag>()
                 })
        {
            Assert(relic.PackedIconPath.Contains("/things_", StringComparison.Ordinal),
                $"{relic.GetType().Name} still derives an unprefixed icon path: {relic.PackedIconPath}");
            VerifyTexture(relic.PackedIconPath);
        }

        foreach (PowerModel power in new PowerModel[]
                 {
                     ModelDb.Power<ThingsBowlbugCarapacePower>(),
                     ModelDb.Power<ThingsBowlbugSiphonPower>(),
                     ModelDb.Power<ThingsBowlbugVigorPower>(),
                     ModelDb.Power<ThingsDazedPower>(),
                     ModelDb.Power<ThingsOriginPower>(),
                     ModelDb.Power<ThingsQuirkPower>(),
                     ModelDb.Power<ThingsRecallPower>(),
                     ModelDb.Power<ThingsReusePower>(),
                     ModelDb.Power<ThingsScaleBeetlePower>(),
                     ModelDb.Power<ThingsScaleDownPower>(),
                     ModelDb.Power<ThingsScaleUpPower>()
                 })
        {
            Assert(power.ResolvedBigIconPath.Contains("/things_", StringComparison.Ordinal),
                $"{power.GetType().Name} still derives an unprefixed icon path: {power.ResolvedBigIconPath}");
            VerifyTexture(power.ResolvedBigIconPath);
            VerifyTexture(power.PackedIconPath);
        }

        string spineExt = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../addons/spine/spine_godot_extension.gdextension"));
        GDExtensionManager.LoadExtension(spineExt);
        VerifyScene("res://scenes/creature_visuals/things_scale_beetle.tscn");
        VerifyScene("res://scenes/creature_visuals/things_the_legacy.tscn");
    }

    private static void VerifyTexture(string path)
    {
        Assert(ResourceLoader.Load<Texture2D>(path) != null, $"Texture did not load from PCK: {path}");
    }

    private static void VerifyScene(string path)
    {
        Assert(ResourceLoader.Load<PackedScene>(path) != null, $"Scene did not load from PCK: {path}");
    }

    private static void RegisterSyntheticMods(Assembly implementationAssembly)
    {
        IList mods = (IList)typeof(ModManager).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        mods.Clear();
        mods.Add(CreateSyntheticMod("Hoursmod", "Hoursmod fixture", _hoursmodRecallType.Assembly));
        mods.Add(CreateSyntheticMod("STS2_Things", "STS2_Things ModelId Probe", implementationAssembly));
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
        {
            assemblies.Add(assembly);
        }
        else
        {
            modType.GetField("assembly")!.SetValue(mod, assembly);
        }

        return mod;
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
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    private static Assembly? ResolveRuntimeDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }

        string? path = FindRuntimeDependencyPath(assemblyName.Name);
        return path == null ? null : context.LoadFromAssemblyPath(path);
    }

    private static string? FindRuntimeDependencyPath(string assemblyName)
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? assemblyDirectory = Path.GetDirectoryName(typeof(ThingsModelIdProbeNode).Assembly.Location);
        string[] candidates =
        [
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug", $"{assemblyName}.dll"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release", $"{assemblyName}.dll"),
            Path.Combine(assemblyDirectory ?? string.Empty, $"{assemblyName}.dll")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
