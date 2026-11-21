using System.Reflection;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Inspects active Harmony patches and loaded non-system .NET assemblies (MVID + mod SHA-256)
/// for Schema v4 fault attribution and environment diagnostics.
/// </summary>
internal static class BugHarmonyInspector
{
    private const int MaxReportedPatches = 250;
    private const int MaxPatchEntriesPerKind = 20;
    private const int MaxReportedAssemblies = 300;

    private sealed class PatchOwnerEntry(
        string targetSymbol,
        string[] targetOwners,
        (string PatchSymbol, string PatchDeclaring, string Owner)[] patchMethods)
    {
        internal string TargetSymbol { get; } = targetSymbol;
        internal string[] TargetOwners { get; } = targetOwners;
        internal (string PatchSymbol, string PatchDeclaring, string Owner)[] PatchMethods { get; } = patchMethods;
    }

    private sealed class BatchScope : IDisposable
    {
        public void Dispose() => _batchIndex = null;
    }

    private sealed class NoopDisposable : IDisposable
    {
        internal static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    [ThreadStatic] private static PatchOwnerEntry[]? _batchIndex;

    internal static IDisposable BeginOwnerResolutionBatch()
    {
        if (_batchIndex is not null) return NoopDisposable.Instance;
        _batchIndex = BuildPatchOwnerIndex();
        return new BatchScope();
    }

    private static PatchOwnerEntry[] BuildPatchOwnerIndex()
    {
        var entries = new List<PatchOwnerEntry>();
        try
        {
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
            {
                Patches? info = Harmony.GetPatchInfo(target);
                if (info is null || info.Owners.Count == 0) continue;

                string targetSymbol = FormatMethodSymbol(target);
                string[] targetOwners = info.Owners
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                var patchList = new List<(string PatchSymbol, string PatchDeclaring, string Owner)>();
                foreach (Patch patch in EnumerateAllPatches(info))
                {
                    if (string.IsNullOrWhiteSpace(patch.owner) || patch.PatchMethod is null) continue;
                    string patchSymbol = FormatMethodSymbol(patch.PatchMethod);
                    string patchDeclaring = NormalizeTypeName(patch.PatchMethod.DeclaringType);
                    patchList.Add((patchSymbol, patchDeclaring, patch.owner));
                }

                entries.Add(new PatchOwnerEntry(targetSymbol, targetOwners, patchList.ToArray()));
            }
        }
        catch (Exception)
        {
            // Harmony global state inspection is read-only and best-effort.
        }
        return entries.ToArray();
    }

    internal static IReadOnlyList<string> ResolveOwnersForFrames(IReadOnlyList<string> frames)
    {
        if (frames.Count == 0) return [];

        var owners = new List<string>();
        var seenOwners = new HashSet<string>(StringComparer.Ordinal);
        var frameSet = new HashSet<string>(frames, StringComparer.Ordinal);
        var index = _batchIndex ?? BuildPatchOwnerIndex();

        foreach (var entry in index)
        {
            bool matchesTarget = entry.TargetSymbol.Length > 0 && frames.Any(f =>
                string.Equals(f, entry.TargetSymbol, StringComparison.Ordinal)
                || f.StartsWith(entry.TargetSymbol + "_Patch", StringComparison.Ordinal));

            if (matchesTarget)
            {
                foreach (string owner in entry.TargetOwners)
                {
                    if (seenOwners.Add(owner))
                    {
                        owners.Add(LogSanitizer.Cap(owner, 300));
                        if (owners.Count >= 50) return owners;
                    }
                }
            }

            foreach (var (patchSymbol, patchDeclaring, owner) in entry.PatchMethods)
            {
                bool matchesPatch = (patchSymbol.Length > 0 && frameSet.Contains(patchSymbol))
                    || (patchDeclaring.Length > 0 && frames.Any(f => f.StartsWith(patchDeclaring + ".", StringComparison.Ordinal)));
                if (matchesPatch && seenOwners.Add(owner))
                {
                    owners.Add(LogSanitizer.Cap(owner, 300));
                    if (owners.Count >= 50) return owners;
                }
            }
        }

        return owners;
    }

    internal static JsonArray CaptureHarmonyPatches(IEnumerable<string>? priorityFrames = null)
    {
        var frameList = priorityFrames?.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        var prioritized = new List<(int PriorityScore, string SortKey, JsonObject Entry)>();

        try
        {
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
            {
                Patches? info = Harmony.GetPatchInfo(target);
                if (info is null) continue;

                var owners = info.Owners
                    .Concat(EnumerateAllPatches(info).Select(p => p.owner))
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (owners.Length == 0) continue;

                string declaring = NormalizeTypeName(target.DeclaringType);
                if (declaring.Length == 0) declaring = "<Global>";
                string methodName = (target.Name ?? "Unknown").Replace('`', '_');
                string symbol = declaring + "." + methodName;

                bool onFaultStack = frameList.Any(f =>
                    string.Equals(f, symbol, StringComparison.Ordinal)
                    || f.StartsWith(symbol + "_Patch", StringComparison.Ordinal));
                bool touchesGameOrMod = declaring.StartsWith("MegaCrit.Sts2.", StringComparison.Ordinal)
                    || declaring.StartsWith("STS2_Things.", StringComparison.Ordinal)
                    || declaring.StartsWith("Godot.", StringComparison.Ordinal);

                if (!onFaultStack && !touchesGameOrMod && prioritized.Count >= MaxReportedPatches)
                {
                    continue;
                }

                var entry = BugReportSchemaV4.BuildHarmonyPatchEntry(
                    declaring,
                    methodName,
                    owners,
                    BuildPatchKindArray(info.Prefixes),
                    BuildPatchKindArray(info.Postfixes),
                    BuildPatchKindArray(info.Transpilers),
                    BuildPatchKindArray(info.Finalizers));

                int score = onFaultStack ? 0 : (touchesGameOrMod ? 1 : 2);
                prioritized.Add((score, symbol, entry));
            }
        }
        catch (Exception)
        {
            // Harmony reflection must never interrupt snapshot creation.
        }

        var result = new JsonArray();
        foreach (var item in prioritized
            .OrderBy(x => x.PriorityScore)
            .ThenBy(x => x.SortKey, StringComparer.Ordinal)
            .Take(MaxReportedPatches))
        {
            result.Add(item.Entry);
        }
        return result;
    }

    internal static JsonArray CaptureLoadedAssemblies()
    {
        var modByAssembly = BuildModAssemblyMap(out var modDirs);
        var entries = new List<(int Order, string Name, JsonObject Entry)>();

        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? rawName;
                string? version;
                try
                {
                    var an = asm.GetName();
                    rawName = an.Name;
                    version = an.Version?.ToString();
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rawName) || IsSystemAssemblyName(rawName))
                {
                    continue;
                }

                bool isDynamic = asm.IsDynamic;
                string? mvid = isDynamic ? null : SafeMvid(asm);
                string? location = isDynamic ? null : ResolveAssemblyDiskPath(asm);

                modByAssembly.TryGetValue(asm, out string? modId);
                if (modId is null && string.Equals(rawName, "STS2_Things", StringComparison.OrdinalIgnoreCase))
                {
                    modId = "STS2_Things";
                }

                bool isGameDll = string.Equals(rawName, "sts2", StringComparison.OrdinalIgnoreCase);
                bool isModDll = modId is not null || IsInModDirectory(location, modDirs);

                string locationKind = isDynamic ? "dynamic"
                    : isModDll ? "mod_dll"
                    : isGameDll ? "game_dll"
                    : string.IsNullOrWhiteSpace(location) ? "memory_or_bundled"
                    : "engine_or_dependency";

                string? sha256 = (!isDynamic && (isModDll || isGameDll) && !string.IsNullOrWhiteSpace(location))
                    ? BugFileFingerprints.TryGetSha256(location)
                    : null;

                var entry = BugReportSchemaV4.BuildAssemblyEntry(
                    rawName,
                    version,
                    mvid,
                    isDynamic,
                    locationKind,
                    sha256,
                    modId);

                int order = isModDll ? 0 : (isGameDll ? 1 : 2);
                entries.Add((order, rawName, entry));
            }
        }
        catch (Exception)
        {
            // Assembly enumeration is best-effort.
        }

        var result = new JsonArray();
        foreach (var item in entries
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxReportedAssemblies))
        {
            result.Add(item.Entry);
        }
        return result;
    }

    internal static IReadOnlyList<string> CollectAllFingerprintPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void AddIfOnDisk(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            try
            {
                if (File.Exists(candidate))
                {
                    string full = Path.GetFullPath(candidate);
                    if (seen.Add(full)) ordered.Add(full);
                }
            }
            catch (Exception)
            {
                // Ignore virtual URIs such as probe://STS2_Things.
            }
        }

        // Prioritize core game and STS2_Things binaries first so sequential hashing completes them immediately.
        AddIfOnDisk(ResolveAssemblyDiskPath(typeof(MegaCrit.Sts2.Core.Models.AbstractModel).Assembly));

        string? thingsDll = ResolveAssemblyDiskPath(typeof(BugRunRecorder).Assembly);
        AddIfOnDisk(thingsDll);
        if (!string.IsNullOrWhiteSpace(thingsDll))
        {
            try
            {
                string? dir = Path.GetDirectoryName(thingsDll);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    AddIfOnDisk(Path.Combine(dir, "STS2_Things.pck"));
                }
            }
            catch (Exception)
            {
                // Ignore invalid path shapes.
            }
        }

        try
        {
            foreach (Mod mod in ModManager.GetLoadedMods())
            {
                if (!string.IsNullOrWhiteSpace(mod.path) && Directory.Exists(mod.path))
                {
                    foreach (string file in Directory.EnumerateFiles(mod.path)
                        .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".dll" or ".pck")
                        .Take(20))
                    {
                        AddIfOnDisk(file);
                    }
                }
                foreach (var asm in mod.assemblies)
                {
                    if (!asm.IsDynamic) AddIfOnDisk(ResolveAssemblyDiskPath(asm));
                }
            }
        }
        catch (Exception)
        {
            // ModManager may not be populated in minimal unit harnesses.
        }

        return ordered.ToArray();
    }

    private static Dictionary<Assembly, string> BuildModAssemblyMap(out List<string> modDirectories)
    {
        var map = new Dictionary<Assembly, string>();
        modDirectories = [];
        try
        {
            foreach (Mod mod in ModManager.GetLoadedMods())
            {
                string modId = mod.manifest?.id ?? "unknown";
                if (!string.IsNullOrWhiteSpace(mod.path))
                {
                    try
                    {
                        if (Directory.Exists(mod.path))
                        {
                            modDirectories.Add(Path.GetFullPath(mod.path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
                        }
                    }
                    catch (Exception)
                    {
                        // Non-filesystem virtual paths in synthetic tests are skipped cleanly.
                    }
                }
                foreach (var asm in mod.assemblies)
                {
                    map[asm] = modId;
                }
            }
        }
        catch (Exception)
        {
            // ModManager lookup is best-effort.
        }
        return map;
    }

    private static bool IsInModDirectory(string? location, List<string> modDirectories)
    {
        if (string.IsNullOrWhiteSpace(location)) return false;
        try
        {
            string full = Path.GetFullPath(location);
            foreach (string dir in modDirectories)
            {
                if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return full.Contains(Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.Contains("/mods/", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSystemAssemblyName(string name) =>
        string.Equals(name, "System", StringComparison.Ordinal)
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || string.Equals(name, "netstandard", StringComparison.Ordinal)
        || string.Equals(name, "mscorlib", StringComparison.Ordinal);

    private static JsonArray? BuildPatchKindArray(IReadOnlyCollection<Patch>? patches)
    {
        if (patches is null || patches.Count == 0) return null;
        var arr = new JsonArray();
        foreach (Patch patch in patches.OrderByDescending(p => p.priority).ThenBy(p => p.index).Take(MaxPatchEntriesPerKind))
        {
            string assemblyName = patch.PatchMethod?.DeclaringType?.Assembly.GetName().Name ?? "unknown";
            string patchMethod = patch.PatchMethod is null ? "unknown" : FormatMethodSymbol(patch.PatchMethod);
            arr.Add(new JsonObject
            {
                ["owner"] = LogSanitizer.Cap(patch.owner ?? "unknown", 300),
                ["assembly"] = LogSanitizer.Cap(assemblyName, 200),
                ["method"] = LogSanitizer.Cap(patchMethod, 400),
                ["priority"] = patch.priority,
            });
        }
        return arr;
    }

    private static IEnumerable<Patch> EnumerateAllPatches(Patches info)
    {
        if (info.Prefixes is not null) foreach (var p in info.Prefixes) yield return p;
        if (info.Postfixes is not null) foreach (var p in info.Postfixes) yield return p;
        if (info.Transpilers is not null) foreach (var p in info.Transpilers) yield return p;
        if (info.Finalizers is not null) foreach (var p in info.Finalizers) yield return p;
    }

    private static string NormalizeTypeName(Type? type) =>
        (type?.FullName ?? string.Empty).Replace('`', '_');

    private static string FormatMethodSymbol(MethodBase method)
    {
        string declaring = NormalizeTypeName(method.DeclaringType);
        string name = (method.Name ?? string.Empty).Replace('`', '_');
        return declaring.Length > 0 ? declaring + "." + name : name;
    }

    private static string? SafeMvid(Assembly asm)
    {
        try
        {
            return asm.ManifestModule.ModuleVersionId.ToString("D");
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string? ResolveAssemblyDiskPath(Assembly asm)
    {
        try
        {
            if (asm.IsDynamic) return null;

            string? direct = SafeLocation(asm);
            if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct))
            {
                return Path.GetFullPath(direct);
            }

            string? asmName = asm.GetName().Name;
            if (string.IsNullOrWhiteSpace(asmName)) return null;

            bool isThingsImpl = ReferenceEquals(asm, typeof(BugRunRecorder).Assembly)
                || string.Equals(asmName, "STS2_Things", StringComparison.OrdinalIgnoreCase);

            // 1. Check ModManager mod directory (covers UnifiedBootstrap stream-loaded STS2_Things and stream-loaded mods).
            try
            {
                foreach (Mod mod in ModManager.GetLoadedMods())
                {
                    bool matchesMod = (isThingsImpl && string.Equals(mod.manifest?.id, "STS2_Things", StringComparison.OrdinalIgnoreCase))
                        || string.Equals(mod.manifest?.id, asmName, StringComparison.OrdinalIgnoreCase);
                    if (!matchesMod && mod.assemblies.Contains(asm)) matchesMod = true;
                    if (!matchesMod || string.IsNullOrWhiteSpace(mod.path) || !Directory.Exists(mod.path))
                    {
                        continue;
                    }

                    string exactModDll = Path.Combine(mod.path, asmName + ".dll");
                    if (File.Exists(exactModDll)) return Path.GetFullPath(exactModDll);

                    if (isThingsImpl)
                    {
                        string unifiedModDll = Path.Combine(mod.path, "STS2_Things.dll");
                        if (File.Exists(unifiedModDll)) return Path.GetFullPath(unifiedModDll);
                    }
                }
            }
            catch (Exception)
            {
                // ModManager may be uninitialized in minimal unit tests.
            }

            // 2. If STS2_Things was loaded from STS2_Things.Bootstrap via LoadFromStream, check the bootstrap assembly's location.
            if (isThingsImpl)
            {
                foreach (Assembly candidateAsm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string? cname = candidateAsm.GetName().Name;
                    if (!string.Equals(cname, "STS2_Things.Bootstrap", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(cname, "STS2_Things", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? bootLoc = SafeLocation(candidateAsm);
                    if (!string.IsNullOrWhiteSpace(bootLoc) && File.Exists(bootLoc))
                    {
                        return Path.GetFullPath(bootLoc);
                    }
                }
            }

            // 3. Check AppContext.BaseDirectory.
            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            {
                string baseCandidate = Path.Combine(AppContext.BaseDirectory, asmName + ".dll");
                if (File.Exists(baseCandidate)) return Path.GetFullPath(baseCandidate);
            }

            // 4. Check exported Godot game directory (<exeDir> and <exeDir>/data_*/<asmName>.dll).
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
                {
                    string besideExe = Path.Combine(exeDir, asmName + ".dll");
                    if (File.Exists(besideExe)) return Path.GetFullPath(besideExe);

                    foreach (string dataDir in Directory.EnumerateDirectories(exeDir, "data_*"))
                    {
                        string inDataDir = Path.Combine(dataDir, asmName + ".dll");
                        if (File.Exists(inDataDir)) return Path.GetFullPath(inDataDir);
                    }
                }
            }

            // 5. Check Godot project mono build output (<cwd>/.godot/mono/temp/bin/{Debug|Release}/<asmName>.dll).
            string cwd = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                foreach (string cfg in new[] { "Debug", "Release" })
                {
                    string monoTemp = Path.Combine(cwd, ".godot", "mono", "temp", "bin", cfg, asmName + ".dll");
                    if (File.Exists(monoTemp)) return Path.GetFullPath(monoTemp);
                }
            }
        }
        catch (Exception)
        {
            // Disk path resolution must never fault diagnostic capture.
        }

        return null;
    }

    private static string? SafeLocation(Assembly asm)
    {
        try
        {
            return string.IsNullOrWhiteSpace(asm.Location) ? null : asm.Location;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
