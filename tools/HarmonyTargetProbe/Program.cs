using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine(
        "Usage: HarmonyTargetProbe <sts2-data-dir> <mod-dll> [dependency-data-dir]");
    return 2;
}

var dataDirectories = args
    .Where((_, index) => index != 1)
    .Select(Path.GetFullPath)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
var modDll = Path.GetFullPath(args[1]);
var gameDll = Path.Combine(dataDirectories[0], "sts2.dll");

if (!File.Exists(gameDll) || !File.Exists(modDll))
{
    Console.Error.WriteLine($"Missing probe input: game={gameDll}, mod={modDll}");
    return 2;
}

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var loaded = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
    if (loaded != null)
        return loaded;

    foreach (var directory in dataDirectories)
    {
        var candidate = Path.Combine(directory, $"{name.Name}.dll");
        if (File.Exists(candidate))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};

try
{
    var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameDll);
    var modAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modDll);
    var harmony = new Harmony($"Adnermo.STS2_Things.TargetProbe.{Guid.NewGuid():N}");
    var patchFailures = new List<string>();
    foreach (var patchType in modAssembly.GetTypes().Where(type =>
                 type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
    {
        try { harmony.CreateClassProcessor(patchType).Patch(); }
        catch (Exception error)
        {
            string failure = $"{patchType.FullName}: {error.GetBaseException().Message}";
            patchFailures.Add(failure);
            Console.Error.WriteLine($"PATCH_TARGET_FAIL {failure}");
        }
    }
    if (patchFailures.Count > 0)
        throw new InvalidOperationException($"{patchFailures.Count} patch target contracts failed.");

    var savedCache = gameAssembly.GetType("MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache");
    var merchantBargainType = modAssembly.GetType(
        "STS2_Things.Features.MerchantBargain.MerchantBargainManager");
    var merchantPolicyType = modAssembly.GetType(
        "STS2_Things.Modifiers.ThingsMerchantBargainPricePolicy");
    // Both supported targets have shipped merchant bargaining since v1.25.16.
    if (merchantBargainType == null || merchantPolicyType == null)
        throw new InvalidOperationException("Implementation is missing MerchantBargainManager or ThingsMerchantBargainPricePolicy.");
    if (modAssembly.GetType("STS2_Things.Features.MerchantBargain.MerchantEntryCostPatch") != null)
        throw new InvalidOperationException("Deprecated MerchantEntryCostPatch still exists in implementation.");
    Console.WriteLine("Merchant bargain presence: PASS");
    if (savedCache != null)
        throw new InvalidOperationException("Target runtime still exposes legacy SavedPropertiesTypeCache.");

    var modelIdCache = gameAssembly.GetType(
        "MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache",
        throwOnError: true)!;
    foreach (var member in new[]
             {
                 "Init",
                 "CacheSavedPropertiesForTypeDebug",
                 "GetJsonPropertiesForType",
                 "GetNetIdForPropertyName"
             })
    {
        if (modelIdCache.GetMethod(member, BindingFlags.Public | BindingFlags.Static) == null)
            throw new InvalidOperationException($"Unified serialization cache is missing {member}.");
    }

    modelIdCache.GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)!
        .SetValue(null, true);
    var cacheDebug = modelIdCache.GetMethod(
        "CacheSavedPropertiesForTypeDebug", BindingFlags.Public | BindingFlags.Static)!;
    var getJsonProps = modelIdCache.GetMethod(
        "GetJsonPropertiesForType", BindingFlags.Public | BindingFlags.Static)!;
    var getNetId = modelIdCache.GetMethod(
        "GetNetIdForPropertyName", BindingFlags.Public | BindingFlags.Static)!;

    int savedTypeCount = 0;
    foreach (var type in modAssembly.GetTypes())
    {
        var declaredSaved = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.CustomAttributes.Any(attribute =>
                attribute.AttributeType.FullName ==
                "MegaCrit.Sts2.Core.Saves.Runs.SavedPropertyAttribute"))
            .ToArray();
        if (declaredSaved.Length == 0)
            continue;

        savedTypeCount++;
        cacheDebug.Invoke(null, [type]);
        var cachedProps = (IEnumerable<PropertyInfo>?)getJsonProps.Invoke(null, [type])
            ?? throw new InvalidOperationException($"ModelIdSerializationCache returned null properties for {type.FullName}.");
        var cachedNames = cachedProps.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in declaredSaved)
        {
            if (!cachedNames.Contains(property.Name))
                throw new InvalidOperationException($"ModelIdSerializationCache omitted {type.FullName}.{property.Name}.");
            if ((int)getNetId.Invoke(null, [property.Name])! < 0)
                throw new InvalidOperationException($"ModelIdSerializationCache assigned invalid net ID to {property.Name}.");
        }
    }

    foreach (var (typeName, propName) in new[]
             {
                 ("STS2_Things.Relics.ThingsCurseRemover", "TimesUsed"),
                 ("STS2_Things.Modifiers.ThingsCrossroads", "Data"),
                 ("STS2_Things.Enchantments.ThingsSplit", "SplitState")
             })
    {
        var type = modAssembly.GetType(typeName, throwOnError: true)!;
        var cachedProps = (IEnumerable<PropertyInfo>?)getJsonProps.Invoke(null, [type]);
        if (cachedProps?.Any(property => property.Name == propName) != true)
            throw new InvalidOperationException($"{typeName}.{propName} is missing from ModelIdSerializationCache.");
    }

    Console.WriteLine($"Unified SavedProperty cache contract: PASS ({savedTypeCount} types)");

    Console.WriteLine($"Harmony target probe: PASS ({modAssembly.GetName().Version})");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Harmony target probe: FAIL");
    Console.Error.WriteLine(exception);
    return 1;
}
