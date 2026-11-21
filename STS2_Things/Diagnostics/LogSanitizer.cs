using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

internal sealed record ParsedInnerException(
    string ExceptionType,
    string Message,
    IReadOnlyList<string> Frames);

internal sealed record ParsedFaultInfo(
    string ExceptionType,
    string Message,
    string TopFrame,
    IReadOnlyList<string> Frames,
    IReadOnlyList<ParsedInnerException> InnerExceptions,
    string Code,
    string? MissingMember,
    string Signature);

/// <summary>
/// Pure .NET privacy scrubber and exception chain parser for Schema v4 diagnostics.
/// Retains actionable error messages and inner exceptions while scrubbing PII, local paths,
/// Steam IDs, IPv4/IPv6 addresses, credentials, and emails.
/// </summary>
internal static class LogSanitizer
{
    internal const int MaxMessageLength = 2000;
    internal const int MaxInnerExceptions = 4;
    internal const int MaxFramesPerException = 30;
    internal const int MaxFrameLength = 300;

    private static readonly Regex WindowsUserHomeRegex = new(
        @"[A-Za-z]:[\\/]Users[\\/][^\\/\r\n""':<>|*?\t]+(?:[\\/]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MacUserHomeRegex = new(
        @"(?<![A-Za-z0-9_])/Users/[^/\r\n""':<>|*?\t]+(?:/|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LinuxUserHomeRegex = new(
        @"(?<![A-Za-z0-9_])/home/[^/\r\n""':<>|*?\t]+(?:/|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Non-Users Windows drive paths (e.g. D:\PrivatePlayer\Game\test.cs -> <LOCAL_PATH>/test.cs)
    private static readonly Regex WindowsOtherDriveFileRegex = new(
        @"[A-Za-z]:[\\/](?:[^\\/\r\n""':<>|*?\t]+[\\/])+([A-Za-z0-9_.-]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WindowsOtherDriveBareRegex = new(
        @"[A-Za-z]:[\\/][^\s""'<>|*?\r\n:]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // 17-digit Steam64 IDs (7656119xxxxxxxxxx) and Steam3 [U:1:...] IDs
    private static readonly Regex SteamId64Regex = new(
        @"\b7656119\d{10}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SteamId3Regex = new(
        @"\[U:\d+:\d+\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Bearer tokens and secret key-value pairs
    private static readonly Regex BearerTokenRegex = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SecretKeyValueRegex = new(
        @"\b(authorization|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|session[_-]?secret|password|passwd|secret|cookie|private[_-]?key)\s*([:=])\s*(?!Bearer\b)([^\s,;'""&\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UrlQuerySecretRegex = new(
        @"([?&](?:token|access_token|api_key|apikey|key|secret|password|sig|signature|auth)=)[^&\s#'""\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KnownTokenPrefixRegex = new(
        @"\b(?:sk-[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9]{16,}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{5,})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // IPv4 addresses (excluding Version=1.2.3.4 assembly metadata, while scrubbing ip=1.2.3.4 / bare IPs)
    private static readonly Regex IpV4Regex = new(
        @"(?<!\bVersion=)(?<![A-Za-z0-9.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?::\d{1,5})?(?![.\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // IPv6 addresses (full and :: compressed forms, avoiding ISO timestamps like T02:08:11Z and C# ::)
    private static readonly Regex IpV6Regex = new(
        @"(?<![A-Za-z0-9:.\-+])(?:(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,6}(?::[0-9a-fA-F]{1,4}){1,6}|(?:[0-9a-fA-F]{1,4}:){1,7}:|:(?::[0-9a-fA-F]{1,4}){1,7}|fe80:(?::?[0-9a-fA-F]{1,4}){0,4}(?:%[0-9a-zA-Z]+)?)(?![A-Za-z0-9:.\-+])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExceptionHeaderRegex = new(
        @"^(?:--->)?\s*(?:\(Inner Exception #\d+\)\s*)?(?:ERROR:\s*|SCRIPT ERROR:\s*|\[ERROR\]\s*)?([A-Za-z_][\w.+`]*(?:Exception|Error))\s*(?::\s*(.*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InnerArrowSplitRegex = new(
        @"(?:\r?\n|\s+)--->\s*(?:\(Inner Exception #\d+\)\s*)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MissingMethodRegex = new(
        @"Method not found:\s*'([A-Za-z0-9_.+<>`\[\], ()]{1,500})'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeMethodPathRegex = new(
        @"^(?:DMD<[^>]+>|Trampoline<[^>]+>|[\w.+<>`]+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumberedFramePrefixRegex = new(
        @"^\[\d+\]\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string Scrub(string? input, int maxLength = MaxMessageLength)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        string value = input.Length > 32_000 ? input[..32_000] : input;
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        // Also scrub OS-reported user profile directory if non-standard location
        try
        {
            string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile) && profile.Length >= 4)
            {
                string trimmed = profile.TrimEnd('\\', '/');
                value = value.Replace(trimmed + "\\", "<USER_HOME>/", StringComparison.OrdinalIgnoreCase);
                value = value.Replace(trimmed + "/", "<USER_HOME>/", StringComparison.OrdinalIgnoreCase);
                value = value.Replace(trimmed, "<USER_HOME>", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            // Environment lookup is best-effort only.
        }

        value = WindowsUserHomeRegex.Replace(value, "<USER_HOME>/");
        value = MacUserHomeRegex.Replace(value, "<USER_HOME>/");
        value = LinuxUserHomeRegex.Replace(value, "<USER_HOME>/");
        value = NormalizeUserHomeSlashes(value);

        value = WindowsOtherDriveFileRegex.Replace(value, "<LOCAL_PATH>/$1");
        value = WindowsOtherDriveBareRegex.Replace(value, "<LOCAL_PATH>");

        value = BearerTokenRegex.Replace(value, "Bearer <REDACTED>");
        value = SecretKeyValueRegex.Replace(value, "$1$2<REDACTED>");
        value = UrlQuerySecretRegex.Replace(value, "$1<REDACTED>");
        value = KnownTokenPrefixRegex.Replace(value, "<REDACTED_TOKEN>");
        value = EmailRegex.Replace(value, "<EMAIL>");
        value = SteamId64Regex.Replace(value, "<STEAM_ID>");
        value = SteamId3Regex.Replace(value, "<STEAM_ID>");
        value = IpV4Regex.Replace(value, "<IP_ADDR>");
        value = IpV6Regex.Replace(value, "<IP_ADDR>");

        value = value.Trim();
        if (maxLength > 0 && value.Length > maxLength)
        {
            value = value[..maxLength];
        }
        return value;
    }

    private static string NormalizeUserHomeSlashes(string text)
    {
        int idx = text.IndexOf("<USER_HOME>/", StringComparison.Ordinal);
        if (idx < 0) return text;
        var sb = new StringBuilder(text.Length);
        bool inHomePath = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (i + 12 <= text.Length && string.CompareOrdinal(text, i, "<USER_HOME>/", 0, 12) == 0)
            {
                sb.Append("<USER_HOME>/");
                i += 11;
                inHomePath = true;
                continue;
            }
            char c = text[i];
            if (inHomePath)
            {
                if (c == '\\') { sb.Append('/'); continue; }
                if (char.IsWhiteSpace(c) || c is '"' or '\'' or ')' or ']' or ';' or ',') inHomePath = false;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    internal static string? NormalizeFrame(string? rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return null;
        string frame = rawLine.Trim();
        if (frame.StartsWith("---", StringComparison.Ordinal)) return null;

        if (frame.StartsWith("at ", StringComparison.Ordinal)) frame = frame[3..].TrimStart();
        else if (frame.StartsWith("in ", StringComparison.Ordinal)) frame = frame[3..].TrimStart();
        else if (frame.StartsWith("在 ", StringComparison.Ordinal)) frame = frame[2..].TrimStart();
        else if (NumberedFramePrefixRegex.IsMatch(frame)) frame = NumberedFramePrefixRegex.Replace(frame, "");

        // Strip trailing " in <file>:line N" or " [0x0000] in <file>:0"
        int ilOffset = frame.IndexOf(" [0x", StringComparison.OrdinalIgnoreCase);
        if (ilOffset > 0) frame = frame[..ilOffset].TrimEnd();
        int inPath = frame.IndexOf(" in ", StringComparison.Ordinal);
        if (inPath > 0) frame = frame[..inPath].TrimEnd();

        // Handle Harmony DMD<ReturnType Namespace.Type::Method(Args)> cleanly
        if (frame.StartsWith("DMD<", StringComparison.Ordinal) || frame.StartsWith("Trampoline<", StringComparison.Ordinal))
        {
            int open = frame.IndexOf('<');
            int close = frame.LastIndexOf('>');
            if (open > 0 && close > open)
            {
                string inner = frame[(open + 1)..close].Trim();
                int paren = inner.IndexOf('(');
                if (paren >= 0) inner = inner[..paren];
                int space = inner.LastIndexOf(' ');
                if (space >= 0) inner = inner[(space + 1)..];
                frame = inner.Replace("::", ".", StringComparison.Ordinal) + "_Patch1";
            }
        }
        else
        {
            int args = frame.IndexOf('(');
            if (args >= 0) frame = frame[..args].TrimEnd();
            if (frame.Contains(' ')) frame = frame[(frame.LastIndexOf(' ') + 1)..];
            frame = frame.Replace("::", ".", StringComparison.Ordinal);
        }

        frame = frame.Replace('`', '_');
        if (frame.Length is 0 or > MaxFrameLength) return null;
        return SafeMethodPathRegex.IsMatch(frame) ? frame : null;
    }

    internal static List<string> ExtractFramesFromLines(IEnumerable<string> lines, IEnumerable<string>? extraFunctions = null, int maxFrames = MaxFramesPerException)
    {
        var frames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in lines)
        {
            string trimmed = raw.Trim();
            bool isStackLine = trimmed.StartsWith("at ", StringComparison.Ordinal)
                || trimmed.StartsWith("在 ", StringComparison.Ordinal)
                || NumberedFramePrefixRegex.IsMatch(trimmed);
            if (!isStackLine) continue;

            string? normalized = NormalizeFrame(trimmed);
            if (normalized is not null && seen.Add(normalized))
            {
                frames.Add(normalized);
                if (frames.Count >= maxFrames) return frames;
            }
        }

        if (extraFunctions is not null)
        {
            foreach (string fn in extraFunctions)
            {
                string? normalized = NormalizeFrame(fn);
                if (normalized is not null && seen.Add(normalized))
                {
                    frames.Add(normalized);
                    if (frames.Count >= maxFrames) break;
                }
            }
        }

        return frames;
    }

    internal static List<string> ExtractFramesFromException(Exception exception, int maxFrames = MaxFramesPerException)
    {
        var frames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var traceFrames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
        if (traceFrames is not null)
        {
            foreach (var sf in traceFrames)
            {
                var method = sf.GetMethod();
                if (method is null) continue;
                string raw = (method.DeclaringType?.FullName is { Length: > 0 } dt ? dt + "." : "") + method.Name;
                string? normalized = NormalizeFrame(raw);
                if (normalized is not null && seen.Add(normalized))
                {
                    frames.Add(normalized);
                    if (frames.Count >= maxFrames) return frames;
                }
            }
        }

        if (frames.Count == 0 && !string.IsNullOrEmpty(exception.StackTrace))
        {
            return ExtractFramesFromLines(exception.StackTrace.Split('\n'), null, maxFrames);
        }

        return frames;
    }

    internal static ParsedFaultInfo ParseException(Exception exception, int skipFallbackFrames = 0)
    {
        string outerType = Cap(exception.GetType().FullName ?? exception.GetType().Name, 300);
        string outerMessage = Scrub(exception.Message, MaxMessageLength);
        var outerFrames = ExtractFramesFromException(exception);

        var inners = new List<ParsedInnerException>();
        CollectInnerExceptions(exception, inners);

        // If outer exception has no stack frames, seed top frames from first inner with frames or caller stack.
        if (outerFrames.Count == 0)
        {
            var innerWithFrames = inners.FirstOrDefault(i => i.Frames.Count > 0);
            if (innerWithFrames is not null)
            {
                outerFrames.AddRange(innerWithFrames.Frames.Take(MaxFramesPerException));
            }
            else
            {
                outerFrames.AddRange(CaptureCallerFrames(skipFallbackFrames));
            }
        }

        string topFrame = ChooseTopFrame(outerFrames, inners);
        string combinedSearch = exception.GetType().FullName + ": " + exception.Message + "\n"
            + string.Join('\n', inners.Select(i => i.ExceptionType + ": " + i.Message));
        string code = ClassifyErrorCode(outerType, combinedSearch);
        string? missingMember = ExtractMissingMember(combinedSearch);
        string signature = ComputeSignature(outerType, topFrame, outerMessage);

        return new ParsedFaultInfo(
            outerType,
            outerMessage,
            topFrame,
            outerFrames,
            inners,
            code,
            missingMember,
            signature);
    }

    private static void CollectInnerExceptions(Exception root, List<ParsedInnerException> results)
    {
        var queue = new Queue<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { root };

        EnqueueDirectChildren(root, queue, visited);

        while (queue.Count > 0 && results.Count < MaxInnerExceptions)
        {
            var current = queue.Dequeue();
            string exType = Cap(current.GetType().FullName ?? current.GetType().Name, 300);
            string msg = Scrub(current.Message, 1500);
            var frames = ExtractFramesFromException(current, 20);
            results.Add(new ParsedInnerException(exType, msg, frames));

            if (results.Count < MaxInnerExceptions)
            {
                EnqueueDirectChildren(current, queue, visited);
            }
        }
    }

    private static void EnqueueDirectChildren(Exception current, Queue<Exception> queue, HashSet<Exception> visited)
    {
        if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            foreach (var child in aggregate.InnerExceptions)
            {
                if (child is not null && visited.Add(child)) queue.Enqueue(child);
            }
        }
        else if (current.InnerException is { } inner && visited.Add(inner))
        {
            queue.Enqueue(inner);
        }
    }

    internal static ParsedFaultInfo ParseLogText(string rawText, int skipFallbackFrames = 0, IEnumerable<string>? engineFunctions = null)
    {
        string boundedRaw = rawText.Length > 24_000 ? rawText[..24_000] : rawText;
        boundedRaw = boundedRaw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] extraFns = engineFunctions?.Take(20).ToArray() ?? [];

        // Split nested exception segments separated by "---> "
        string[] segments = InnerArrowSplitRegex.Split(boundedRaw);
        string primarySegment = segments.Length > 0 ? segments[0] : boundedRaw;

        var (outerType, outerRawMsg, outerSegFrames) = ParseSingleSegment(primarySegment, isInner: false);

        var inners = new List<ParsedInnerException>();
        var allFrames = new List<string>(outerSegFrames);
        var seenFrames = new HashSet<string>(outerSegFrames, StringComparer.Ordinal);

        for (int i = 1; i < segments.Length && inners.Count < MaxInnerExceptions; i++)
        {
            string seg = segments[i].Replace("<---", "", StringComparison.Ordinal).Trim();
            if (seg.Length == 0) continue;

            // Notice in .NET ex.ToString(), outer stack frames follow "--- End of inner exception stack trace ---"
            string innerPart = seg;
            const string endMarker = "--- End of inner exception stack trace ---";
            int markerIdx = seg.IndexOf(endMarker, StringComparison.Ordinal);
            if (markerIdx >= 0)
            {
                innerPart = seg[..markerIdx];
                string trailingOuter = seg[(markerIdx + endMarker.Length)..];
                foreach (string f in ExtractFramesFromLines(trailingOuter.Split('\n')))
                {
                    if (seenFrames.Add(f) && allFrames.Count < MaxFramesPerException) allFrames.Add(f);
                }
            }

            var (innerType, innerRawMsg, innerFrames) = ParseSingleSegment(innerPart, isInner: true);
            inners.Add(new ParsedInnerException(
                innerType,
                Scrub(innerRawMsg, 1500),
                innerFrames));

            foreach (string f in innerFrames)
            {
                if (seenFrames.Add(f) && allFrames.Count < MaxFramesPerException) allFrames.Add(f);
            }
        }

        foreach (string fn in extraFns)
        {
            string? norm = NormalizeFrame(fn);
            if (norm is not null && seenFrames.Add(norm) && allFrames.Count < MaxFramesPerException)
            {
                allFrames.Add(norm);
            }
        }

        if (allFrames.Count == 0)
        {
            allFrames.AddRange(CaptureCallerFrames(skipFallbackFrames));
        }

        string scrubbedOuterMsg = Scrub(outerRawMsg, MaxMessageLength);
        string topFrame = ChooseTopFrame(allFrames, inners);
        string code = ClassifyErrorCode(outerType, boundedRaw);
        string? missingMember = ExtractMissingMember(boundedRaw);
        string signature = ComputeSignature(outerType, topFrame, scrubbedOuterMsg);

        return new ParsedFaultInfo(
            outerType,
            scrubbedOuterMsg,
            topFrame,
            allFrames,
            inners,
            code,
            missingMember,
            signature);
    }

    private static (string ExceptionType, string RawMessage, List<string> Frames) ParseSingleSegment(string segment, bool isInner)
    {
        string[] lines = segment.Split('\n');
        var frames = ExtractFramesFromLines(lines);
        var messageLines = new List<string>();
        string exceptionType = isInner ? "InnerException" : "LoggedError";
        bool headerParsed = false;

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("---", StringComparison.Ordinal)) break;
            if (trimmed.StartsWith("at ", StringComparison.Ordinal)
                || trimmed.StartsWith("在 ", StringComparison.Ordinal)
                || NumberedFramePrefixRegex.IsMatch(trimmed))
            {
                continue;
            }

            if (!headerParsed)
            {
                headerParsed = true;
                var match = ExceptionHeaderRegex.Match(trimmed);
                if (match.Success)
                {
                    exceptionType = Cap(match.Groups[1].Value, 300);
                    string remainder = match.Groups[2].Value.Trim();
                    if (remainder.Length > 0) messageLines.Add(remainder);
                    continue;
                }
                string cleanedPrefix = Regex.Replace(trimmed, @"^(?:ERROR:\s*|SCRIPT ERROR:\s*|\[ERROR\]\s*)", "", RegexOptions.CultureInvariant);
                messageLines.Add(cleanedPrefix);
            }
            else if (messageLines.Count < 6)
            {
                messageLines.Add(trimmed);
            }
        }

        return (exceptionType, string.Join("\n", messageLines), frames);
    }

    private static List<string> CaptureCallerFrames(int skipFrames)
    {
        var result = new List<string>();
        var trace = new StackTrace(Math.Clamp(skipFrames, 0, 28) + 2, fNeedFileInfo: false).GetFrames();
        if (trace is null) return result;
        foreach (var frame in trace)
        {
            var method = frame.GetMethod();
            if (method is null) continue;
            string declaring = method.DeclaringType?.FullName ?? string.Empty;
            if (declaring.StartsWith("STS2_Things.Diagnostics.", StringComparison.Ordinal)) continue;
            string? norm = NormalizeFrame((declaring.Length > 0 ? declaring + "." : "") + method.Name);
            if (norm is not null && !result.Contains(norm))
            {
                result.Add(norm);
                if (result.Count >= 12) break;
            }
        }
        return result;
    }

    private static string ChooseTopFrame(IReadOnlyList<string> outerFrames, IReadOnlyList<ParsedInnerException> inners)
    {
        foreach (var inner in inners)
        {
            string? candidate = inner.Frames.FirstOrDefault(f => !IsDiagnosticsOrWrapperFrame(f));
            if (candidate is not null) return Cap(candidate, 600);
        }
        string? outerCandidate = outerFrames.FirstOrDefault(f => !IsDiagnosticsOrWrapperFrame(f))
            ?? outerFrames.FirstOrDefault();
        return Cap(outerCandidate ?? "UnknownFrame", 600);
    }

    private static bool IsDiagnosticsOrWrapperFrame(string frame)
    {
        return frame.StartsWith("STS2_Things.Diagnostics.", StringComparison.Ordinal)
            || frame.StartsWith("Godot.GD.", StringComparison.Ordinal)
            || frame.StartsWith("MegaCrit.Sts2.Core.Logging.", StringComparison.Ordinal)
            || frame.StartsWith("MegaCrit.Sts2.Core.Helpers.TaskHelper.LogTaskExceptions", StringComparison.Ordinal)
            || frame.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)
            || frame.StartsWith("System.Runtime.ExceptionServices.", StringComparison.Ordinal);
    }

    private static string ClassifyErrorCode(string exceptionType, string fullText)
    {
        if (fullText.Contains("Method not found", StringComparison.Ordinal)) return "missing_method";
        if (fullText.Contains("not in a scene tree", StringComparison.Ordinal)) return "node_outside_tree";
        if (fullText.Contains("out of bounds", StringComparison.OrdinalIgnoreCase)
            || exceptionType.EndsWith("IndexOutOfRangeException", StringComparison.Ordinal)
            || exceptionType.EndsWith("ArgumentOutOfRangeException", StringComparison.Ordinal)) return "index_out_of_bounds";
        if (exceptionType.EndsWith("ObjectDisposedException", StringComparison.Ordinal)) return "object_disposed";
        if (exceptionType.EndsWith("NullReferenceException", StringComparison.Ordinal)) return "null_reference";
        return "runtime_error";
    }

    private static string? ExtractMissingMember(string fullText)
    {
        var match = MissingMethodRegex.Match(fullText);
        return match.Success ? Cap(match.Groups[1].Value, 500) : null;
    }

    internal static string ComputeSignature(string exceptionType, string topFrame, string scrubbedMessage)
    {
        string shortType = ShortTypeName(exceptionType);
        string normalizedFrame = string.IsNullOrWhiteSpace(topFrame) ? "UnknownFrame" : topFrame.Trim();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scrubbedMessage ?? string.Empty));
        string msgHash = Convert.ToHexString(hash)[..12].ToLowerInvariant();
        return Cap($"{shortType}@{normalizedFrame}#{msgHash}", 500);
    }

    internal static string ShortTypeName(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return "RuntimeError";
        string trimmed = rawType.Trim();
        int tick = trimmed.IndexOf('`');
        if (tick > 0) trimmed = trimmed[..tick];
        int dot = trimmed.LastIndexOf('.');
        return dot >= 0 && dot + 1 < trimmed.Length ? trimmed[(dot + 1)..] : trimmed;
    }

    internal static string Cap(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
