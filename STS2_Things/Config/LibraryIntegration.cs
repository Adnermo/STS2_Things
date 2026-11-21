using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace STS2_Things.Config;

/// <summary>
/// BaseLib / RitsuLib 的可选配置页接入。
///
/// 原则：两库都不是前置依赖。检测到对应程序集才注册：
///   - RitsuLib：通过互操作镜像（字符串注册）注册本模组的设置页提供器；
///   - BaseLib：加载同目录可选桥程序集 <c>STS2_Things.BaseLibBridge.dll</c> 并调用其注册入口。
///
/// 加载时序：模组不声明对两库的依赖，两库可能在本模组之后才加载。因此在
/// ModManager.State 变为 Initialized（全部模组加载完成）或 TryLoadMod 完成后
/// 经 Harmony 补丁重试接入，兼容 v107.1 与 v113。
/// </summary>
internal static class LibraryIntegration
{
    private const string BaseLibAssemblyName = "BaseLib";
    private const string RitsuLibAssemblyName = "STS2-RitsuLib";
    private const string RitsuSettingsAssemblyName = "STS2-RitsuLib.Settings";
    private const string BridgeFileName = "STS2_Things.BaseLibBridge.dll";
    private const string BridgeEntryTypeName = "STS2_Things.BaseLibBridge.BridgeEntry";
    private const string RitsuMirrorTypeName =
        "STS2RitsuLib.Settings.ModSettingsRuntimeReflectionInteropMirror";
    private const string HarmonyId = "Adnermo.STS2_Things.LibraryIntegration";

    private static readonly object Gate = new();
    private static readonly HashSet<AssemblyLoadContext> RegisteredContexts = [];
    private static bool _domainResolverInstalled;
    private static Assembly? _resolvedBaseLib;
    private static bool _loggedBrokenBaseLib;
    private static bool _baseLibDone;
    private static bool _ritsuDone;
    private static Harmony? _retryHarmony;

    public static void Initialize()
    {
        lock (Gate)
        {
            TryIntegrate();
            InstallRetryHook();
        }
    }

    private static void TryIntegrate()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = assembly.GetName().Name ?? string.Empty;
            if (name == BaseLibAssemblyName)
                TryRegisterBaseLib();
            else if (name is RitsuLibAssemblyName or RitsuSettingsAssemblyName)
                TryRegisterRitsuLib(assembly);
        }
    }

    private static void TryRegisterBaseLib()
    {
        if (_baseLibDone)
            return;
        try
        {
            // BaseLib 尚未加载（本模组先于它初始化）时跳过；ModManager 重试钩子会再次尝试。
            Assembly? baseLibAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == BaseLibAssemblyName)
                {
                    baseLibAssembly = assembly;
                    break;
                }
            }
            if (baseLibAssembly is null)
                return;

            // 若 ModManager 登记了 BaseLib 但尚未完成加载或初始化失败（例如玩家在 v113 中残留了
            // 旧版 v107.1 的 BaseLib，DLL 已进入内存但 GetTypes 抛异常标记为 Failed），跳过桥接。
            if (!IsModHealthyOrStandalone(BaseLibAssemblyName, ref _loggedBrokenBaseLib))
                return;

            string? directory = FindModDirectory();
            if (directory is null)
            {
                MegaCrit.Sts2.Core.Logging.Log.Warn(
                    "STS2_Things: BaseLib detected but the mod directory is unavailable; skipping config bridge.");
                return;
            }

            string bridgePath = Path.Combine(directory, BridgeFileName);
            if (!File.Exists(bridgePath))
            {
                MegaCrit.Sts2.Core.Logging.Log.Info(
                    "STS2_Things: BaseLib detected but the optional config bridge DLL is not installed; " +
                    "falling back to the standalone config file.");
                return;
            }

            // 桥必须载入与 BaseLib 相同的 ALC，并把低版本/跨小版本的 BaseLib 引用（如桥按 3.4.8 编译、
            // v107.1 运行时加载 3.4.7）重定向到内存中的 BaseLib 实例。
            AssemblyLoadContext loadContext =
                AssemblyLoadContext.GetLoadContext(baseLibAssembly) ?? AssemblyLoadContext.Default;
            EnsureBridgeAssemblyResolver(loadContext, baseLibAssembly);
            Assembly bridge = loadContext.LoadFromAssemblyPath(bridgePath);
            Type? entry = bridge.GetType(BridgeEntryTypeName, throwOnError: false);
            MethodInfo? register = entry?.GetMethod(
                "Register", BindingFlags.Public | BindingFlags.Static);
            if (register is null)
            {
                MegaCrit.Sts2.Core.Logging.Log.Warn(
                    "STS2_Things: BaseLib config bridge is missing BridgeEntry.Register; skipping.");
                return;
            }

            register.Invoke(null, null);
            MegaCrit.Sts2.Core.Logging.Log.Info(
                "STS2_Things: registered the config page with BaseLib.");
            _baseLibDone = true;
        }
        catch (Exception exception)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                "STS2_Things: BaseLib config bridge failed; continuing standalone. " + exception);
        }
    }

    private static void TryRegisterRitsuLib(Assembly ritsuLib)
    {
        if (_ritsuDone)
            return;
        try
        {
            Type? mirror = ritsuLib.GetType(RitsuMirrorTypeName, throwOnError: false);
            MethodInfo? register = mirror?.GetMethod(
                "RegisterProviderTypeAndTryRegister",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(string)],
                modifiers: null);
            if (register is null)
            {
                // Newer RitsuLib versions keep the mirror in the shared Settings
                // assembly. Its bootstrap can be present before that is loaded.
                return;
            }

            int registered = (int)(register.Invoke(
                null,
                [typeof(RitsuLibInteropProvider).FullName,
                 typeof(RitsuLibInteropProvider).Assembly.GetName().Name]) ?? 0);
            MegaCrit.Sts2.Core.Logging.Log.Info(
                $"STS2_Things: registered the config page with RitsuLib ({registered} page(s)).");
            _ritsuDone = registered > 0;
        }
        catch (Exception exception)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                "STS2_Things: RitsuLib settings registration failed; continuing standalone. " + exception);
        }
    }

    /// <summary>
    /// 全部模组加载完成后（ModManager.State -> Initialized，或无 State 成员时每次 TryLoadMod 完成后）
    /// 重试接入，覆盖两库在本模组之后才加载的情况。
    /// </summary>
    private static void InstallRetryHook()
    {
        if (_retryHarmony is not null)
            return;
        try
        {
            Type modManager = typeof(MegaCrit.Sts2.Core.Modding.ModManager);
            MethodInfo? stateSetter = AccessTools.PropertySetter(modManager, "State");
            if (stateSetter is not null)
            {
                _retryHarmony = new Harmony(HarmonyId);
                _retryHarmony.Patch(
                    stateSetter,
                    postfix: new HarmonyMethod(typeof(LibraryIntegration).GetMethod(
                        nameof(OnModManagerStateChanged),
                        BindingFlags.NonPublic | BindingFlags.Static)!));
                return;
            }

            MethodInfo? tryLoadMod = AccessTools.Method(modManager, "TryLoadMod");
            if (tryLoadMod is not null)
            {
                _retryHarmony = new Harmony(HarmonyId);
                _retryHarmony.Patch(
                    tryLoadMod,
                    postfix: new HarmonyMethod(typeof(LibraryIntegration).GetMethod(
                        nameof(OnModLoadedRetry),
                        BindingFlags.NonPublic | BindingFlags.Static)!));
            }
        }
        catch (Exception exception)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                "STS2_Things: could not install the config integration retry hook. " + exception.Message);
        }
    }

    private static void OnModManagerStateChanged(object? value)
    {
        if (value?.ToString() != "Initialized")
            return;
        lock (Gate)
            TryIntegrate();
    }

    private static void OnModLoadedRetry()
    {
        if (_baseLibDone && _ritsuDone)
            return;
        lock (Gate)
            TryIntegrate();
    }

    private static void EnsureBridgeAssemblyResolver(AssemblyLoadContext loadContext, Assembly baseLibAssembly)
    {
        _resolvedBaseLib = baseLibAssembly;
        if (RegisteredContexts.Add(loadContext))
            loadContext.Resolving += ResolveBridgeAssembly;
        if (!_domainResolverInstalled)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveBridgeAssemblyOnDomain;
            _domainResolverInstalled = true;
        }
    }

    private static Assembly? ResolveBridgeAssembly(AssemblyLoadContext _, AssemblyName name) =>
        ResolveBridgeAssemblyCore(name.Name);

    private static Assembly? ResolveBridgeAssemblyOnDomain(object? _, ResolveEventArgs args)
    {
        try
        {
            return ResolveBridgeAssemblyCore(new AssemblyName(args.Name).Name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Assembly? ResolveBridgeAssemblyCore(string? simpleName)
    {
        if (string.Equals(simpleName, BaseLibAssemblyName, StringComparison.Ordinal))
            return _resolvedBaseLib;
        if (string.Equals(simpleName, "STS2_Things", StringComparison.Ordinal))
            return typeof(LibraryIntegration).Assembly;
        return null;
    }

    private static bool IsModHealthyOrStandalone(string modId, ref bool loggedBroken)
    {
        try
        {
            Type modManager = typeof(MegaCrit.Sts2.Core.Modding.ModManager);
            if (modManager.GetProperty("Mods", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                is not System.Collections.IEnumerable allMods)
            {
                return true;
            }

            object? trackedMod = null;
            foreach (object? mod in allMods)
            {
                if (mod is null)
                    continue;
                string? id = GetManifestId(mod);
                if (string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))
                {
                    trackedMod = mod;
                    break;
                }
            }

            // 独立探针未通过 ModManager 注册外部模组清单时直接放行。
            if (trackedMod is null)
                return true;

            if (modManager.GetMethod("GetLoadedMods", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null)
                is System.Collections.IEnumerable loadedMods)
            {
                foreach (object? loaded in loadedMods)
                {
                    if (loaded is not null &&
                        string.Equals(GetManifestId(loaded), modId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            string? loadState = trackedMod.GetType()
                .GetField("state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(trackedMod)
                ?.ToString();
            if (string.Equals(loadState, "Failed", StringComparison.OrdinalIgnoreCase) && !loggedBroken)
            {
                loggedBroken = true;
                MegaCrit.Sts2.Core.Logging.Log.Info(
                    $"STS2_Things: {modId} failed ModManager initialization; skipping optional config integration.");
            }
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string? GetManifestId(object mod)
    {
        object? manifest = mod.GetType()
            .GetField("manifest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(mod);
        return manifest?.GetType()
            .GetField("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(manifest) as string;
    }

    /// <summary>模组目录：实现程序集从流加载时 Location 为空，退回引导程序集的目录。</summary>
    private static string? FindModDirectory()
    {
        string location = typeof(LibraryIntegration).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(location))
            return Path.GetDirectoryName(location);

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "STS2_Things.Bootstrap" &&
                !string.IsNullOrWhiteSpace(assembly.Location))
            {
                return Path.GetDirectoryName(assembly.Location);
            }
        }

        return null;
    }
}
