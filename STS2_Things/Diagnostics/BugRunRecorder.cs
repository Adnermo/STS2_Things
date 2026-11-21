using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;

namespace STS2_Things.Diagnostics;

/// <summary>Adds opt-in Things categories to the original F2 dropdown; other categories remain unchanged.</summary>
[HarmonyPatch(typeof(NFeedbackCategoryDropdown), "PopulateOptions")]
internal static class ThingsFeedbackCategoryPatch
{
    private static readonly FieldInfo? CategoriesField = AccessTools.Field(typeof(NFeedbackCategoryDropdown), "_categories");
    private static readonly FieldInfo? CategoryLocField = AccessTools.Field(typeof(NFeedbackCategoryDropdown), "_categoryLoc");
    private static readonly FieldInfo? DropdownItemSceneField = AccessTools.Field(typeof(NFeedbackCategoryDropdown), "_dropdownItemScene");
    private static readonly MethodInfo? OnDropdownItemSelectedMethod = AccessTools.Method(typeof(NFeedbackCategoryDropdown), "OnDropdownItemSelected");

    internal static readonly string[] Entries = ["things_bug", "things_bug_retry"];

    internal static string Title(string entry) => MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language == "zhs"
        ? entry == "things_bug_retry" ? "琐事 Bug（重试待发送）" : "琐事 Bug（上传本局）"
        : entry == "things_bug_retry" ? "Things Bug (retry pending)" : "Things Bug (upload run)";

    private static void Postfix(NFeedbackCategoryDropdown __instance)
    {
        if (CategoriesField is null || CategoryLocField is null || DropdownItemSceneField is null) return;
        var categories = (string[])CategoriesField.GetValue(__instance)!;
        if (categories.Contains("things_bug")) return;

        CategoriesField.SetValue(__instance, categories.Concat(Entries).ToArray());
        var localized = (MegaCrit.Sts2.Core.Localization.LocString[])CategoryLocField.GetValue(__instance)!;
        CategoryLocField.SetValue(
            __instance,
            localized.Concat(Entries.Select(_ => new MegaCrit.Sts2.Core.Localization.LocString("settings_ui", "FEEDBACK_CATEGORY.bug"))).ToArray());

        var scene = (PackedScene)DropdownItemSceneField.GetValue(__instance)!;
        var container = __instance.GetNode<Control>("DropdownContainer/VBoxContainer");
        for (int i = 0; i < Entries.Length; i++)
        {
            var item = scene.Instantiate<NFeedbackCategoryDropdownItem>();
            container.AddChild(item);
            item.Connect(NDropdownItem.SignalName.Selected, Callable.From<NDropdownItem>(selected =>
                OnDropdownItemSelectedMethod?.Invoke(__instance, [selected])));
            item.Init(categories.Length + i, Title(Entries[i]));
        }
        container.GetParent<NDropdownContainer>().RefreshLayout();
    }
}

[HarmonyPatch(typeof(NFeedbackCategoryDropdown), "OnDropdownItemSelected")]
internal static class ThingsFeedbackSelectionPatch
{
    private static readonly FieldInfo? DescriptionInputField = AccessTools.Field(typeof(NSendFeedbackScreen), "_descriptionInput");
    private static readonly FieldInfo? CurrentOptionLabelField = AccessTools.Field(typeof(NDropdown), "_currentOptionLabel");
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextEdit, OriginalHint> Hints = new();
    private sealed class OriginalHint(string text) { internal string Text { get; } = text; }

    private static TextEdit? TryGetDescriptionInput(NSendFeedbackScreen screen)
    {
        object? raw = DescriptionInputField?.GetValue(screen);
        if (raw is TextEdit direct)
            return direct;
#if STS2_V113_1
        if (raw is NMegaTextEdit wrapper)
            return wrapper.TextEdit ?? wrapper.GetNodeOrNull<TextEdit>("TextEdit");
#endif
        return raw is Node node ? node.GetNodeOrNull<TextEdit>("TextEdit") : null;
    }

    private static void Postfix(NFeedbackCategoryDropdown __instance)
    {
        bool ours = ThingsFeedbackCategoryPatch.Entries.Contains(__instance.CurrentCategory);
        for (Node? parent = __instance.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is NSendFeedbackScreen screen)
            {
                if (TryGetDescriptionInput(screen) is { } input)
                {
                    var original = Hints.GetValue(input, field => new OriginalHint(field.PlaceholderText));
                    input.PlaceholderText = ours
                        ? MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language == "zhs"
                            ? "随便说说哪里不对劲就行。\n游戏画面、脱敏异常、模组补丁信息与房间前存档会自动附上。"
                            : "Just tell us what went wrong.\nA game screenshot, scrubbed faults, mod patches and an anonymous pre-room save are attached automatically."
                        : original.Text;
                }
                break;
            }
        }

        if (!ours) return;
        var label = CurrentOptionLabelField?.GetValue(__instance) as MegaCrit.Sts2.addons.mega_text.MegaLabel;
        label?.SetTextAutoSize(ThingsFeedbackCategoryPatch.Title(__instance.CurrentCategory));

        if (__instance.CurrentCategory == "things_bug_retry")
        {
            for (Node? parent = __instance.GetParent(); parent is not null; parent = parent.GetParent())
            {
                if (parent is NSendFeedbackScreen screen)
                {
                    if (TryGetDescriptionInput(screen) is { } input && string.IsNullOrWhiteSpace(input.Text))
                    {
                        input.Text = "Retry pending Things reports";
                        input.EmitSignal(TextEdit.SignalName.TextChanged);
                    }
                    break;
                }
            }
        }
    }
}

/// <summary>Intercept only our opt-in category; vanilla sends other categories directly to MegaCrit.</summary>
[HarmonyPatch(typeof(NSendFeedbackScreen), "SendFeedback")]
internal static class ThingsFeedbackSendPatch
{
#if STS2_V113_1
    private static bool Prefix(
        FeedbackData data,
        byte[] screenshotBytes,
        MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.FeedbackLogFiles logFiles,
        ref Task<bool> __result)
    {
        if (!ThingsFeedbackCategoryPatch.Entries.Contains(data.category)) return true;
        bool retry = data.category == "things_bug_retry";
        System.Text.Json.Nodes.JsonObject? screenshot;
        try
        {
            using var stream = new MemoryStream(screenshotBytes, writable: false);
            screenshot = retry ? null : BugScreenshotCapture.Capture(stream);
        }
        finally
        {
            logFiles.Dispose();
        }
        __result = BugRunRecorder.SubmitAsync(data.description ?? string.Empty, retry, screenshot);
        return false;
    }
#else
    private static bool Prefix(FeedbackData data, Stream screenshotStream, Stream logsMemoryStream, ref Task<bool> __result)
    {
        if (!ThingsFeedbackCategoryPatch.Entries.Contains(data.category)) return true;
        bool retry = data.category == "things_bug_retry";
        System.Text.Json.Nodes.JsonObject? screenshot;
        try
        {
            screenshot = retry ? null : BugScreenshotCapture.Capture(screenshotStream);
        }
        finally
        {
            screenshotStream.Dispose();
            logsMemoryStream.Dispose();
        }
        __result = BugRunRecorder.SubmitAsync(data.description ?? string.Empty, retry, screenshot);
        return false;
    }
#endif
}

[HarmonyPatch(typeof(NSendFeedbackScreen), "OnFeedbackSuccess")]
internal static class ThingsFeedbackReceiptPatch
{
    internal static readonly FieldInfo? CategoryDropdownField = AccessTools.Field(typeof(NSendFeedbackScreen), "_categoryDropdown");
    private static readonly FieldInfo? SuccessLabelField = AccessTools.Field(typeof(NSendFeedbackScreen), "_successLabel");
    private static readonly FieldInfo? FailedLabelField = AccessTools.Field(typeof(NSendFeedbackScreen), "_failedLabel");

    private static void Prefix(NSendFeedbackScreen __instance) => SetLabel(__instance, "_successLabel", true);

    internal static void SetLabel(NSendFeedbackScreen screen, string field, bool success)
    {
        if (CategoryDropdownField?.GetValue(screen) is not NFeedbackCategoryDropdown dropdown) return;
        if (!ThingsFeedbackCategoryPatch.Entries.Contains(dropdown.CurrentCategory)) return;

        bool chinese = MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language == "zhs";
        var result = BugRunRecorder.LastSubmission;

        string message;
        if (success && result?.Reason == "duplicate_throttled_saved_locally")
        {
            string shortId = result.ReportId is { Length: > 0 } dupId ? dupId[..Math.Min(8, dupId.Length)] : "local";
            message = chinese
                ? "5 分钟内已提交过相同状态（完整记录已保存在本机）\n编号：" + shortId
                : "Identical state submitted within 5m (saved locally)\nID: " + shortId;
        }
        else if (success)
        {
            message = result?.ReportId is { Length: > 0 } id
                ? (chinese ? "琐事报告已收到\n编号：" : "Things report received\nID: ") + id[..Math.Min(8, id.Length)]
                : chinese ? "没有待发送的报告" : "No pending reports";
        }
        else
        {
            string reason = result?.Reason switch
            {
                "description_too_short" => chinese ? "请至少填写两个字符" : "Please enter at least two characters",
                "network_error" or "timeout" => chinese ? "网络连接失败，请稍后重试" : "Connection failed; retry later",
                "http_429" => chinese ? "提交过于频繁，请稍后重试" : "Submission limit reached; retry later",
                "invalid_receipt" => chinese ? "服务器回执异常" : "Invalid server receipt",
                "local_storage_error" => chinese ? "未能保存报告，请检查本机存储" : "Unable to save the report locally",
                "too_large_saved_locally" => chinese ? "报告过大，完整内容已保存在本机" : "Report too large; full evidence is saved locally",
                _ => chinese ? "服务暂时不可用，请稍后重试" : "Service unavailable; retry later",
            };
            message = result?.Pending > 0
                ? (chinese ? "报告已保存在本机，可选择“重试待发送”\n" : "Report saved locally; choose retry pending\n") + reason
                : reason;
        }

        FieldInfo? labelField = field switch
        {
            "_successLabel" => SuccessLabelField,
            "_failedLabel" => FailedLabelField,
            _ => AccessTools.Field(typeof(NSendFeedbackScreen), field),
        };
        if (labelField?.GetValue(screen) is MegaCrit.Sts2.addons.mega_text.MegaLabel label)
        {
            label.SetTextAutoSize(message);
        }
    }
}

[HarmonyPatch(typeof(NSendFeedbackScreen), "OnFeedbackFailed")]
internal static class ThingsFeedbackFailurePatch
{
    private static readonly FieldInfo? SendButtonField = AccessTools.Field(typeof(NSendFeedbackScreen), "_sendButton");

    private static void Prefix(NSendFeedbackScreen __instance) => ThingsFeedbackReceiptPatch.SetLabel(__instance, "_failedLabel", false);

    private static void Postfix(NSendFeedbackScreen __instance, ref Task __result)
    {
        if (ThingsFeedbackReceiptPatch.CategoryDropdownField?.GetValue(__instance) is NFeedbackCategoryDropdown dropdown
            && ThingsFeedbackCategoryPatch.Entries.Contains(dropdown.CurrentCategory))
        {
            __result = EnableRetry(__result, __instance);
        }
    }

    private static async Task EnableRetry(Task nativeFailure, NSendFeedbackScreen screen)
    {
        await nativeFailure; // Resume on the existing Godot synchronization context.
        if (GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree()
            && SendButtonField?.GetValue(screen) is NButton sendButton)
        {
            sendButton.Enable();
        }
    }
}
