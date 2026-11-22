using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2_Things.Diagnostics;

namespace STS2_Things.Platform;

/// <summary>
/// Pins <c>STS2_Things.pck</c> with a read-only, <see cref="FileShare.Read"/> handle on Windows for
/// the lifetime of the game process. Godot's <c>FileAccessPack</c> and diagnostic SHA-256 hashing open
/// the pack read-only and remain unaffected, while Steam Workshop cannot overwrite, truncate, or swap
/// the file mid-session (preventing 16-byte PCK offset drift and <c>ResourceFormatImporter</c> parse errors).
/// </summary>
public static class PckIntegrityGuard
{
    private const string PckFileName = "STS2_Things.pck";
    private static readonly object Gate = new();
    private static FileStream? _pinnedPckStream;
    private static string? _pinnedPath;

    public static bool IsPinned => _pinnedPckStream is not null;

    public static string? PinnedPath => _pinnedPath;

    public static bool TryPinModPck()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        lock (Gate)
        {
            if (_pinnedPckStream is not null)
                return true;

            try
            {
                string? pckPath = ResolveModPckPath();
                if (string.IsNullOrWhiteSpace(pckPath))
                    return false;

                return TryPinPathCore(pckPath);
            }
            catch (Exception ex)
            {
                Log.Warn($"STS2_Things: PckIntegrityGuard could not locate or pin {PckFileName}: {ex.Message}");
                return false;
            }
        }
    }

    internal static bool TryPinPath(string pckPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(pckPath))
            return false;

        lock (Gate)
        {
            if (_pinnedPckStream is not null)
                return true;

            return TryPinPathCore(pckPath);
        }
    }

    internal static void ReleaseForTest()
    {
        lock (Gate)
        {
            try
            {
                _pinnedPckStream?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"STS2_Things: PckIntegrityGuard release error: {ex.Message}");
            }
            finally
            {
                _pinnedPckStream = null;
                _pinnedPath = null;
            }
        }
    }

    private static bool TryPinPathCore(string pckPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(pckPath);
            if (!File.Exists(fullPath))
                return false;

            _pinnedPckStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _pinnedPath = fullPath;
            Log.Info($"STS2_Things: pinned {PckFileName} read-shared to prevent mid-session Workshop hot-swap corruption.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"STS2_Things: PckIntegrityGuard failed to pin '{pckPath}': {ex.Message}");
            return false;
        }
    }

    private static string? ResolveModPckPath()
    {
        Assembly executing = typeof(PckIntegrityGuard).Assembly;

        // 1. Reuse diagnostic disk resolver (covers direct loads, ModManager mod directories,
        //    and unified bootstrap in-memory stream loads).
        string? resolvedDll = BugHarmonyInspector.ResolveAssemblyDiskPath(executing);
        if (TryPckInDirectory(Path.GetDirectoryName(resolvedDll)) is { } fromResolved)
            return fromResolved;

        // 2. Direct executing assembly location.
        if (TryPckInDirectory(Path.GetDirectoryName(SafeLocation(executing))) is { } fromExecuting)
            return fromExecuting;

        // 3. ModManager discovered mod directories (ModManager.Mods is already populated during Initialize()).
        try
        {
            foreach (Mod mod in ModManager.Mods)
            {
                bool isThings = string.Equals(mod.manifest?.id, "STS2_Things", StringComparison.OrdinalIgnoreCase)
                    || mod.assemblies.Contains(executing);
                if (!isThings)
                    continue;

                if (TryPckInDirectory(mod.path) is { } fromModDir)
                    return fromModDir;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"STS2_Things: PckIntegrityGuard ModManager lookup skipped: {ex.Message}");
        }

        // 4. Unified bootstrap assembly location fallback.
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = asm.GetName().Name;
                if (!string.Equals(name, "STS2_Things.Bootstrap", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "STS2_Things", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryPckInDirectory(Path.GetDirectoryName(SafeLocation(asm))) is { } fromBootstrap)
                    return fromBootstrap;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"STS2_Things: PckIntegrityGuard AppDomain lookup skipped: {ex.Message}");
        }

        return null;
    }

    private static string? TryPckInDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            if (!Directory.Exists(directory))
                return null;

            string candidate = Path.Combine(directory, PckFileName);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
        catch (Exception)
        {
            return null;
        }
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
