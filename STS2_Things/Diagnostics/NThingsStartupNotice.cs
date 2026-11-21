using System;
using System.IO;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.TestSupport;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Once-per-major-version ("2.0") menu-owned startup notice waiter.
/// Persists acknowledgment to user://STS2_Things/diagnostics/notice-ack.json and suppresses
/// automatically in headless, TestMode, NonInteractiveMode, STS2_MCP_Bridge, or env override.
/// </summary>
public partial class NThingsStartupNotice : Node
{
    public const string MajorVersion = StartupNoticePolicy.MajorVersion;
    public const string NoticeAckVirtualPath = "user://STS2_Things/diagnostics/notice-ack.json";
    public const string SuppressEnvVar = "STS2_THINGS_SUPPRESS_NOTICE";

    internal static bool ShownThisProcess { get; private set; }
    private static bool _permanentlyDismissed;
    private static bool? _mcpBridgeLoaded;
    private double _delay = 2.1; // Let the native menu fade and startup dialogs finish first.

    internal static string GetAckFilePath()
    {
        try
        {
            string globalized = ProjectSettings.GlobalizePath(NoticeAckVirtualPath);
            if (!string.IsNullOrWhiteSpace(globalized) && !globalized.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            {
                return globalized;
            }
        }
        catch (Exception)
        {
            // Fall back to OS.GetUserDataDir() when ProjectSettings path resolution is unavailable.
        }
        return Path.Combine(OS.GetUserDataDir(), "STS2_Things", "diagnostics", "notice-ack.json");
    }

    internal static bool IsSuppressed()
    {
        string? displayName = null;
        try
        {
            displayName = DisplayServer.GetName();
        }
        catch (Exception)
        {
            // Ignore if DisplayServer is not initialized.
        }

        bool nonInteractive = false;
        try
        {
            nonInteractive = NonInteractiveMode.IsActive;
        }
        catch (Exception)
        {
            // Ignore if NonInteractiveMode has not been configured.
        }

        return StartupNoticePolicy.ShouldSuppressEnvironment(
            displayServerName: displayName,
            testModeOn: TestMode.IsOn,
            nonInteractiveActive: nonInteractive,
            mcpBridgeLoaded: IsMcpBridgeLoaded(),
            suppressEnvValue: System.Environment.GetEnvironmentVariable(SuppressEnvVar));
    }

    internal static bool ShouldShow()
    {
        if (ShownThisProcess || _permanentlyDismissed)
        {
            return false;
        }

        if (IsSuppressed() || StartupNoticePolicy.IsAcknowledged(GetAckFilePath(), MajorVersion))
        {
            _permanentlyDismissed = true;
            return false;
        }

        return true;
    }

    private static bool IsMcpBridgeLoaded()
    {
        if (_mcpBridgeLoaded is { } cached)
        {
            return cached;
        }

        bool loaded = ScanMcpBridgeLoaded();
        _mcpBridgeLoaded = loaded;
        return loaded;
    }

    private static bool ScanMcpBridgeLoaded()
    {
        try
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                a.GetName().Name is { } name
                && (name.Contains("STS2_MCP_Bridge", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("McpBridge", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Assembly scan is best-effort.
        }

        try
        {
            return ModManager.GetLoadedMods().Any(m =>
                m.manifest?.id is { } id
                && (id.Contains("STS2_MCP_Bridge", StringComparison.OrdinalIgnoreCase)
                    || (id.Contains("mcp", StringComparison.OrdinalIgnoreCase)
                        && id.Contains("bridge", StringComparison.OrdinalIgnoreCase))));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public override void _Process(double delta)
    {
        if (ShownThisProcess || _permanentlyDismissed || TestMode.IsOn)
        {
            QueueFree();
            return;
        }

        var menu = GetParent<NMainMenu>();
        if (!menu.IsVisibleInTree()
            || menu.SubmenuStack.SubmenusOpen
            || NModalContainer.Instance is not { OpenModal: null } modal)
        {
            _delay = Math.Max(_delay, 0.25);
            return;
        }

        _delay -= delta;
        if (_delay > 0) return;

        if (!ShouldShow())
        {
            QueueFree();
            return;
        }

        NGenericPopup? popup = null;
        try
        {
            popup = NGenericPopup.Create();
            if (popup is null) return;
            popup.Name = "ThingsStartupNotice";
            modal.Add(popup);

            string ackPath = GetAckFilePath();
            ShownThisProcess = true;
            _permanentlyDismissed = true;
            StartupNoticePolicy.RecordAcknowledged(ackPath, MajorVersion);

            _ = ObserveConfirmationAsync(popup, ackPath);
            Log.Info($"[Things] Startup bug-report notice shown (major={MajorVersion}).");
        }
        catch (Exception exception)
        {
            // An informational notice must never prevent the game from starting.
            if (ReferenceEquals(modal.OpenModal, popup) && popup is not null) modal.Clear();
            else if (popup is not null && GodotObject.IsInstanceValid(popup)) popup.QueueFree();
            Log.Warn("[Things] Could not show startup notice: " + exception.Message);
        }
        finally
        {
            QueueFree();
        }
    }

    private static async System.Threading.Tasks.Task ObserveConfirmationAsync(NGenericPopup popup, string ackPath)
    {
        try
        {
            await popup.WaitForConfirmation(
                new LocString("settings_ui", "THINGS_STARTUP_NOTICE.body"),
                new LocString("settings_ui", "THINGS_STARTUP_NOTICE.title"),
                null,
                new LocString("settings_ui", "THINGS_STARTUP_NOTICE.confirm"));
            StartupNoticePolicy.RecordAcknowledged(ackPath, MajorVersion);
        }
        catch (Exception)
        {
            // Popup dismissal or scene teardown must never surface unhandled task exceptions.
        }
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class ThingsStartupNoticePatch
{
    private static void Postfix(NMainMenu __instance)
    {
        if (NThingsStartupNotice.ShouldShow())
        {
            __instance.AddChildSafely(new NThingsStartupNotice { Name = "ThingsStartupNoticeWaiter" });
        }
    }
}
