using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

/// <summary>
/// Pure .NET Schema v4 builders, 5-minute duplicate F2 guard, startup notice state policy,
/// and exact validation mirror of services/bug-reports/validation.js.
/// </summary>
internal static class BugReportSchemaV4
{
    internal const int SchemaVersion = 4;
    internal const int CheckpointSchemaVersion = 2;

    internal const int MaxAssemblies = 500;
    internal const int MaxHarmonyPatches = 600;
    internal const int MaxRuntimeFaults = 200;
    internal const int MaxEvents = 1000;
    internal const int MaxDesyncDiffs = 500;
    internal const int MaxStringLength = 16_000;
    internal const int MaxBlobBase64Length = 450_000;
    internal const int MaxTreeDepth = 20;
    internal const int MaxTreeNodes = 60_000;

    private static readonly HashSet<string> BlobFormats = new(StringComparer.Ordinal)
    {
        "sts2-native-save",
        "things-screenshot",
        "sts2-combat-replay",
    };

    private static readonly Regex Hex32Regex = new("^[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Hex64Regex = new("^[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Base64Regex = new("^[A-Za-z0-9+/]+={0,2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static JsonObject BuildAssemblyEntry(
        string name,
        string? version,
        string? mvid,
        bool isDynamic,
        string? locationKind = null,
        string? sha256 = null,
        string? modId = null)
    {
        var obj = new JsonObject
        {
            ["name"] = Cap(string.IsNullOrWhiteSpace(name) ? "unknown" : name, 300),
            ["is_dynamic"] = isDynamic,
        };
        if (!string.IsNullOrWhiteSpace(version)) obj["version"] = Cap(version, 120);
        if (!string.IsNullOrWhiteSpace(mvid)) obj["mvid"] = Cap(mvid, 100);
        if (!string.IsNullOrWhiteSpace(sha256) && Hex64Regex.IsMatch(sha256)) obj["sha256"] = sha256.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(modId)) obj["mod_id"] = Cap(modId, 200);
        if (!string.IsNullOrWhiteSpace(locationKind)) obj["location_kind"] = Cap(locationKind, 60);
        return obj;
    }

    internal static JsonObject BuildHarmonyPatchEntry(
        string declaringType,
        string methodName,
        IEnumerable<string> owners,
        JsonArray? prefixes = null,
        JsonArray? postfixes = null,
        JsonArray? transpilers = null,
        JsonArray? finalizers = null)
    {
        string safeDeclaring = Cap(string.IsNullOrWhiteSpace(declaringType) ? "<Global>" : declaringType, 400);
        string safeMethod = Cap(string.IsNullOrWhiteSpace(methodName) ? "Unknown" : methodName, 200);
        string fullTarget = Cap(safeDeclaring + "." + safeMethod, 600);

        var distinctOwners = owners
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => Cap(o.Trim(), 300))
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .Select(o => (JsonNode?)JsonValue.Create(o))
            .ToArray();

        var obj = new JsonObject
        {
            ["method"] = fullTarget,
            ["target"] = fullTarget,
            ["declaring_type"] = safeDeclaring,
            ["method_name"] = safeMethod,
            ["owners"] = new JsonArray(distinctOwners),
        };
        if (prefixes is { Count: > 0 }) obj["prefixes"] = prefixes;
        if (postfixes is { Count: > 0 }) obj["postfixes"] = postfixes;
        if (transpilers is { Count: > 0 }) obj["transpilers"] = transpilers;
        if (finalizers is { Count: > 0 }) obj["finalizers"] = finalizers;
        return obj;
    }

    internal static List<string> DiffStateDumps(string? localDump, string? remoteDump, int maxDiffs = 80)
    {
        string[] localLines = (localDump ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string[] remoteLines = (remoteDump ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int maxLen = Math.Max(localLines.Length, remoteLines.Length);
        var diffs = new List<string>();
        int limit = Math.Clamp(maxDiffs, 1, MaxDesyncDiffs);

        for (int i = 0; i < maxLen && diffs.Count < limit; i++)
        {
            string l = i < localLines.Length ? localLines[i].Trim() : "<missing>";
            string r = i < remoteLines.Length ? remoteLines[i].Trim() : "<missing>";
            if (string.Equals(l, r, StringComparison.Ordinal)) continue;
            string scrubbedLocal = LogSanitizer.Scrub(l, 600);
            string scrubbedRemote = LogSanitizer.Scrub(r, 600);
            diffs.Add(Cap($"L{i + 1}: local=[{scrubbedLocal}] vs remote=[{scrubbedRemote}]", 1800));
        }

        return diffs;
    }

    internal static JsonObject BuildDesyncReport(
        int? combatRound,
        uint? stateId,
        string? localChecksum,
        string? remoteChecksum,
        string? modelCacheHash,
        string? context,
        string? localStateDump,
        string? remoteStateDump,
        DateTimeOffset? observedAt = null)
    {
        var diffs = DiffStateDumps(localStateDump, remoteStateDump, 80);
        string rawSummary = string.IsNullOrWhiteSpace(context)
            ? $"Multiplayer state divergence detected (round={combatRound?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}, local={localChecksum ?? "?"}, remote={remoteChecksum ?? "?"}, diffs={diffs.Count})."
            : $"Multiplayer state divergence at '{context}' (round={combatRound?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}, local={localChecksum ?? "?"}, remote={remoteChecksum ?? "?"}, diffs={diffs.Count}).";
        string summary = LogSanitizer.Scrub(rawSummary, 3800);

        var diffNodes = diffs.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray();
        var diffCopyNodes = diffs.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray();

        var obj = new JsonObject
        {
            ["detected"] = true,
            ["at"] = (observedAt ?? DateTimeOffset.UtcNow).ToString("O"),
            ["summary"] = summary,
            ["field_diffs"] = new JsonArray(diffNodes),
            ["diff_lines"] = new JsonArray(diffCopyNodes),
        };
        if (combatRound.HasValue) obj["combat_round"] = combatRound.Value;
        if (stateId.HasValue) obj["state_id"] = stateId.Value;
        if (!string.IsNullOrWhiteSpace(localChecksum)) obj["local_checksum"] = Cap(localChecksum, 160);
        if (!string.IsNullOrWhiteSpace(remoteChecksum)) obj["remote_checksum"] = Cap(remoteChecksum, 160);
        if (!string.IsNullOrWhiteSpace(modelCacheHash)) obj["model_cache_hash"] = Cap(modelCacheHash, 160);
        if (!string.IsNullOrWhiteSpace(context)) obj["context"] = Cap(LogSanitizer.Scrub(context, 300), 300);
        return obj;
    }

    internal static int CountJsonNodes(JsonNode? node)
    {
        int count = 0;
        void Walk(JsonNode? current)
        {
            count++;
            if (current is JsonObject obj)
            {
                foreach (var kv in obj) Walk(kv.Value);
            }
            else if (current is JsonArray arr)
            {
                foreach (var item in arr) Walk(item);
            }
        }
        Walk(node);
        return count;
    }

    internal static bool ExceedsJsonNodeLimit(JsonNode? node, int maxNodes)
    {
        int count = 0;
        bool Walk(JsonNode? current)
        {
            if (++count > maxNodes) return true;
            if (current is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    if (Walk(kv.Value)) return true;
                }
            }
            else if (current is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (Walk(item)) return true;
                }
            }
            return false;
        }
        return Walk(node);
    }

    /// <summary>
    /// Clamps non-blob strings, array lengths, object key counts, and non-finite numbers
    /// so any generated payload strictly satisfies validation.js boundedTree().
    /// </summary>
    internal static bool EnforceBoundedTree(JsonObject root)
    {
        bool modified = false;
        JsonNode? SanitizeNode(JsonNode? node, int depth)
        {
            if (node is null) return null;
            if (depth >= MaxTreeDepth) { modified = true; return null; }

            if (node is JsonValue val)
            {
                if (val.TryGetValue<double>(out double dbl) && !double.IsFinite(dbl)) { modified = true; return JsonValue.Create(0); }
                if (val.TryGetValue<float>(out float flt) && !float.IsFinite(flt)) { modified = true; return JsonValue.Create(0); }
                if (val.TryGetValue<string>(out string? str) && str is not null && str.Length > MaxStringLength)
                {
                    modified = true;
                    return JsonValue.Create(str[..MaxStringLength]);
                }
                return node;
            }

            if (node is JsonArray arr)
            {
                while (arr.Count > 1500) { modified = true; arr.RemoveAt(arr.Count - 1); }
                for (int i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    var clean = SanitizeNode(child, depth + 1);
                    if (!ReferenceEquals(child, clean)) arr[i] = clean;
                }
                return arr;
            }

            if (node is JsonObject obj)
            {
                string? format = obj["format"] is JsonValue fv && fv.TryGetValue<string>(out string? fStr) ? fStr : null;
                bool isBlobContainer = format is not null && BlobFormats.Contains(format);

                var keys = obj.Select(kv => kv.Key).ToList();
                foreach (string key in keys)
                {
                    if (key.Length > 150 || key is "__proto__" or "constructor" or "prototype")
                    {
                        modified = true;
                        obj.Remove(key);
                        continue;
                    }
                    if (isBlobContainer && key == "data")
                    {
                        if (obj["data"] is JsonValue dv && dv.TryGetValue<string>(out string? b64) && b64 is not null && b64.Length > MaxBlobBase64Length)
                        {
                            modified = true;
                            obj.Remove("data");
                            obj["available"] = false;
                            obj["reason"] = "blob_exceeded_wire_cap";
                        }
                        continue;
                    }
                    var child = obj[key];
                    var clean = SanitizeNode(child, depth + 1);
                    if (!ReferenceEquals(child, clean)) obj[key] = clean;
                }
                while (obj.Count > 128)
                {
                    modified = true;
                    string lastKey = obj.Last().Key;
                    obj.Remove(lastKey);
                }
                return obj;
            }

            return node;
        }

        SanitizeNode(root, 0);
        return modified;
    }

    /// <summary>
    /// Exact C# mirror of services/bug-reports/validation.js validateReport(p).
    /// Used in tests and pre-flight verification to guarantee 100% server compatibility.
    /// </summary>
    internal static bool ValidateReportPayload(JsonNode? node, out string? error)
    {
        error = null;
        if (node is not JsonObject p) { error = "root_not_object"; return false; }
        int? schema = GetInt(p["schema"]);
        if (schema is not (1 or 2 or 3 or 4)) { error = "invalid_root_schema"; return false; }
        if (GetString(p["source"]) != "sts2_things") { error = "invalid_source"; return false; }
        string? reportId = GetString(p["client_report_id"]);
        if (reportId is null || !Hex32Regex.IsMatch(reportId)) { error = "invalid_client_report_id"; return false; }
        string? desc = GetString(p["description"]);
        if (desc is null || desc.Length > 4000 || desc.Trim().Length < 2) { error = "invalid_description"; return false; }

        if (p["run"] is not JsonObject run) { error = "missing_run"; return false; }
        int? runSchema = GetInt(run["schema"]);
        if (runSchema is not (1 or 2 or 3 or 4)) { error = "invalid_run_schema"; return false; }
        if (!IsText(run["game_version"] ?? JsonValue.Create("unknown"), 80)) { error = "invalid_game_version"; return false; }
        if (!IsText(run["mod_version"] ?? JsonValue.Create("unknown"), 80)) { error = "invalid_mod_version"; return false; }

        if (run["events"] is not JsonArray events || events.Count > MaxEvents) { error = "invalid_events_list"; return false; }
        foreach (var evNode in events)
        {
            if (evNode is not JsonObject ev || !IsText(ev["kind"], 100)
                || !OptionalCheck(ev, "at", n => IsText(n, 80))
                || !OptionalCheck(ev, "detail", n => IsText(n, 4000) || n is JsonObject))
            {
                error = "invalid_event_item";
                return false;
            }
        }

        if (!OptionalCheck(run, "active_mods", n => IsList(n, 300, m => m is JsonObject mo && IsText(mo["id"], 300) && IsText(mo["version"] ?? JsonValue.Create("unknown"), 80))))
        { error = "invalid_active_mods"; return false; }

        if (!OptionalCheck(run, "state", ValidSnapshot)) { error = "invalid_state_snapshot"; return false; }
        if (!OptionalCheck(run, "initial_state", ValidSnapshot)) { error = "invalid_initial_state_snapshot"; return false; }
        if (!OptionalCheck(run, "checkpoint", ValidCheckpoint)) { error = "invalid_checkpoint"; return false; }
        if (!OptionalCheck(run, "screenshot", ValidScreenshot)) { error = "invalid_screenshot"; return false; }
        if (!OptionalCheck(run, "combat_replay", ValidCombatReplay)) { error = "invalid_combat_replay"; return false; }
        if (!OptionalCheck(run, "assemblies", n => IsList(n, MaxAssemblies, ValidAssembly))) { error = "invalid_assemblies"; return false; }
        if (!OptionalCheck(run, "harmony_patches", n => IsList(n, MaxHarmonyPatches, ValidHarmonyPatch))) { error = "invalid_harmony_patches"; return false; }
        if (!OptionalCheck(run, "desync", ValidDesync)) { error = "invalid_desync"; return false; }
        if (!OptionalCheck(run, "runtime_faults", n => IsList(n, MaxRuntimeFaults, ValidRuntimeFault))) { error = "invalid_runtime_faults"; return false; }
        if (!OptionalCheck(run, "modifiers", n => n is JsonObject || IsList(n, 200, m => IsText(m, 300) || m is JsonObject))) { error = "invalid_modifiers"; return false; }
        if (!OptionalCheck(run, "config", n => n is JsonObject)) { error = "invalid_config"; return false; }
        if (!OptionalCheck(run, "network", n => n is JsonObject)) { error = "invalid_network"; return false; }

        int counter = 0;
        if (!BoundedTree(p, 0, ref counter)) { error = "failed_bounded_tree"; return false; }
        return true;
    }

    internal static bool ValidCheckpoint(JsonNode? node)
    {
        if (node is not JsonObject obj || GetBool(obj["available"]) is not { } available) return false;
        if (!available) return OptionalCheck(obj, "reason", n => IsText(n, 200));
        int? schema = GetInt(obj["schema"]);
        bool validPlayers = schema == 1
            ? (GetInt(obj["player_count"]) is >= 1 and <= 8)
            : OptionalCheck(obj, "player_count", n => GetInt(n) is >= 1 and <= 8);
        return GetString(obj["format"]) == "sts2-native-save"
            && GetString(obj["encoding"]) == "gzip-base64"
            && schema is 1 or 2
            && IsHex64(obj["sha256"])
            && GetInt(obj["plain_bytes"]) is > 0 and <= 4_000_000
            && IsBase64(obj["data"], 450_000)
            && validPlayers
            && OptionalCheck(obj, "entry_kind", n => IsText(n, 120))
            && OptionalCheck(obj, "room_type", n => IsText(n, 120))
            && OptionalCheck(obj, "encounter_id", n => IsText(n, 200))
            && OptionalCheck(obj, "event_id", n => IsText(n, 200));
    }

    internal static bool ValidScreenshot(JsonNode? node)
    {
        if (node is not JsonObject obj || GetBool(obj["available"]) is not { } available) return false;
        if (!available) return OptionalCheck(obj, "reason", n => IsText(n, 200));
        return GetString(obj["format"]) == "things-screenshot"
            && GetString(obj["encoding"]) == "jpeg-base64"
            && IsHex64(obj["sha256"])
            && GetInt(obj["bytes"]) is > 0 and <= 250_000
            && GetInt(obj["width"]) is > 0 and <= 1920
            && GetInt(obj["height"]) is > 0 and <= 2160
            && IsBase64(obj["data"], 350_000);
    }

    internal static bool ValidCombatReplay(JsonNode? node)
    {
        if (node is not JsonObject obj || GetBool(obj["available"]) is not { } available) return false;
        if (!available) return OptionalCheck(obj, "reason", n => IsText(n, 200));
        return GetString(obj["format"]) == "sts2-combat-replay"
            && GetString(obj["encoding"]) == "gzip-base64"
            && IsHex64(obj["sha256"])
            && GetInt(obj["plain_bytes"]) is > 0 and <= 2_000_000
            && IsBase64(obj["data"], 350_000);
    }

    private static bool ValidAssembly(JsonNode? node) =>
        node is JsonObject obj && IsText(obj["name"], 300)
        && OptionalCheck(obj, "version", n => IsText(n, 120))
        && OptionalCheck(obj, "mvid", n => IsText(n, 100))
        && OptionalCheck(obj, "sha256", n => IsText(n, 128))
        && OptionalCheck(obj, "mod_id", n => IsText(n, 200))
        && OptionalCheck(obj, "is_dynamic", n => GetBool(n) is not null);

    private static bool ValidHarmonyPatch(JsonNode? node) =>
        node is JsonObject obj
        && OptionalCheck(obj, "method", n => IsText(n, 600))
        && OptionalCheck(obj, "target", n => IsText(n, 600))
        && OptionalCheck(obj, "declaring_type", n => IsText(n, 400))
        && OptionalCheck(obj, "method_name", n => IsText(n, 200))
        && OptionalCheck(obj, "owners", n => IsList(n, 100, x => IsText(x, 300)));

    private static bool ValidDesync(JsonNode? node) =>
        node is JsonObject obj && GetBool(obj["detected"]) is not null
        && OptionalCheck(obj, "at", n => IsText(n, 80))
        && OptionalCheck(obj, "state_id", n => IsFiniteNumber(n) || IsText(n, 120))
        && OptionalCheck(obj, "local_checksum", n => IsFiniteNumber(n) || IsText(n, 160))
        && OptionalCheck(obj, "remote_checksum", n => IsFiniteNumber(n) || IsText(n, 160))
        && OptionalCheck(obj, "model_cache_hash", n => IsFiniteNumber(n) || IsText(n, 160))
        && OptionalCheck(obj, "summary", n => IsText(n, 4000))
        && OptionalCheck(obj, "field_diffs", n => IsList(n, 500, d => IsText(d, 2000) || d is JsonObject));

    private static bool ValidRuntimeFault(JsonNode? node) =>
        node is JsonObject obj
        && OptionalCheck(obj, "signature", n => IsText(n, 500))
        && OptionalCheck(obj, "exception_type", n => IsText(n, 300))
        && OptionalCheck(obj, "message", n => IsText(n, 4000))
        && OptionalCheck(obj, "top_frame", n => IsText(n, 600))
        && OptionalCheck(obj, "harmony_owners", n => IsList(n, 100, x => IsText(x, 300)))
        && OptionalCheck(obj, "count", n => GetInt(n) is >= 1)
        && OptionalCheck(obj, "first_at", n => IsText(n, 80))
        && OptionalCheck(obj, "last_at", n => IsText(n, 80))
        && OptionalCheck(obj, "session", n => IsText(n, 80))
        && OptionalCheck(obj, "inner_exceptions", n => IsList(n, 30, ie => IsText(ie, 4000) || ie is JsonObject))
        && OptionalCheck(obj, "stack", n => IsText(n, 16000) || IsList(n, 300, line => IsText(line, 1000)));

    private static bool ValidCard(JsonNode? n) =>
        IsText(n, 300) || (n is JsonObject o && IsText(o["id"], 300)
            && OptionalCheck(o, "energy", IsFiniteNumber) && OptionalCheck(o, "upgrade", x => GetInt(x) is not null));

    private static bool ValidPower(JsonNode? n) =>
        IsText(n, 300) || (n is JsonObject o && IsText(o["id"], 300) && OptionalCheck(o, "amount", IsFiniteNumber));

    private static bool ValidChoice(JsonNode? n) =>
        n is JsonObject o && OptionalCheck(o, "id", x => IsText(x, 300))
        && OptionalCheck(o, "key", x => IsText(x, 300))
        && OptionalCheck(o, "table", x => IsText(x, 300))
        && OptionalCheck(o, "picked", x => GetBool(x) is not null);

    private static bool ValidCombat(JsonNode? n) =>
        n is JsonObject o
        && new[] { "hand", "draw_pile", "discard_pile", "exhaust_pile", "play_pile" }.All(k => OptionalCheck(o, k, v => IsList(v, 1500, ValidCard)))
        && new[] { "energy", "stars", "turn" }.All(k => OptionalCheck(o, k, IsFiniteNumber))
        && OptionalCheck(o, "phase", v => IsText(v, 80));

    private static bool ValidPlayer(JsonNode? n) =>
        n is JsonObject o && OptionalCheck(o, "character", x => IsText(x, 300))
        && OptionalCheck(o, "deck", v => IsList(v, 1500, ValidCard))
        && OptionalCheck(o, "relics", v => IsList(v, 500, x => IsText(x, 300)))
        && OptionalCheck(o, "potions", v => IsList(v, 100, x => x is null || IsText(x, 300)))
        && OptionalCheck(o, "powers", v => IsList(v, 500, ValidPower))
        && OptionalCheck(o, "combat", ValidCombat);

    private static bool ValidRoutePlayer(JsonNode? n) =>
        n is JsonObject o
        && new[] { "event_choices", "card_choices", "relic_choices", "potion_choices" }.All(k => OptionalCheck(o, k, v => IsList(v, 500, ValidChoice)))
        && new[] { "rest_choices", "cards_gained", "cards_removed" }.All(k => OptionalCheck(o, k, v => IsList(v, 1500, x => IsText(x, 300))));

    private static bool ValidRoute(JsonNode? n) =>
        IsList(n, 20, act => act is JsonObject ao && GetInt(ao["act_index"]) is not null
            && IsList(ao["points"], 500, pt => pt is JsonObject po
                && OptionalCheck(po, "rooms", r => IsList(r, 50, rm => rm is JsonObject ro
                    && OptionalCheck(ro, "type", x => IsText(x, 300))
                    && OptionalCheck(ro, "model_id", x => IsText(x, 300))
                    && OptionalCheck(ro, "monster_ids", m => IsList(m, 100, x => IsText(x, 300)))))
                && OptionalCheck(po, "players", pl => IsList(pl, 8, ValidRoutePlayer))));

    private static bool ValidSnapshot(JsonNode? n) =>
        n is JsonObject o
        && OptionalCheck(o, "seed", v => IsText(v, 100))
        && OptionalCheck(o, "acts", v => IsList(v, 20, x => IsText(x, 300)))
        && OptionalCheck(o, "players", v => IsList(v, 8, ValidPlayer))
        && OptionalCheck(o, "route", ValidRoute)
        && OptionalCheck(o, "combat", v => v is JsonObject co && OptionalCheck(co, "monsters", m => IsList(m, 100,
            mo => mo is JsonObject mObj && IsText(mObj["id"], 300) && OptionalCheck(mObj, "powers", p => IsList(p, 500, ValidPower)))))
        && OptionalCheck(o, "ui", v => v is JsonObject uo && OptionalCheck(uo, "holders", h => IsList(h, 1500, x => x is JsonObject)));

    private static bool BoundedTree(JsonNode? value, int depth, ref int counter)
    {
        if (++counter > MaxTreeNodes || depth > MaxTreeDepth) return false;
        if (value is null) return true;
        if (value is JsonValue scalar)
        {
            if (scalar.GetValueKind() is JsonValueKind.True or JsonValueKind.False) return true;
            if (scalar.GetValueKind() == JsonValueKind.Number) return IsFiniteNumber(scalar);
            if (scalar.GetValueKind() == JsonValueKind.String) return GetString(scalar) is { } s && s.Length <= MaxStringLength;
            return false;
        }
        if (value is JsonArray arr)
        {
            if (arr.Count > 1500) return false;
            foreach (var item in arr)
            {
                if (!BoundedTree(item, depth + 1, ref counter)) return false;
            }
            return true;
        }
        if (value is JsonObject obj)
        {
            if (obj.Count > 128) return false;
            string? format = GetString(obj["format"]);
            bool isBlob = format is not null && BlobFormats.Contains(format);
            foreach (var (key, child) in obj)
            {
                if (key.Length > 150 || key is "__proto__" or "constructor" or "prototype") return false;
                if (key == "data" && isBlob)
                {
                    if (child is not null && !IsText(child, MaxBlobBase64Length)) return false;
                }
                else if (!BoundedTree(child, depth + 1, ref counter))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private static bool OptionalCheck(JsonObject parent, string key, Func<JsonNode?, bool> check) =>
        !parent.ContainsKey(key) || parent[key] is null || check(parent[key]);

    private static bool IsList(JsonNode? node, int max, Func<JsonNode?, bool> check) =>
        node is JsonArray arr && arr.Count <= max && arr.All(check);

    private static bool IsText(JsonNode? node, int max) =>
        GetString(node) is { } s && s.Length <= max;

    private static bool IsHex64(JsonNode? node) =>
        GetString(node) is { } s && Hex64Regex.IsMatch(s);

    private static bool IsBase64(JsonNode? node, int max) =>
        GetString(node) is { Length: > 0 } s && s.Length <= max && Base64Regex.IsMatch(s);

    private static string? GetString(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.TryGetValue<string>(out string? s) ? s : null;

    private static bool? GetBool(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False && v.TryGetValue<bool>(out bool b) ? b : null;

    private static int? GetInt(JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return null;
        if (v.TryGetValue<int>(out int i)) return i;
        if (v.TryGetValue<uint>(out uint u) && u <= int.MaxValue) return (int)u;
        if (v.TryGetValue<long>(out long l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (v.TryGetValue<short>(out short s)) return s;
        if (v.TryGetValue<ushort>(out ushort us)) return us;
        if (v.TryGetValue<byte>(out byte b)) return b;
        return int.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static bool IsFiniteNumber(JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return false;
        if (v.TryGetValue<double>(out double d)) return double.IsFinite(d);
        if (v.TryGetValue<float>(out float f)) return float.IsFinite(f);
        if (v.TryGetValue<int>(out _)
            || v.TryGetValue<uint>(out _)
            || v.TryGetValue<long>(out _)
            || v.TryGetValue<ulong>(out _)
            || v.TryGetValue<short>(out _)
            || v.TryGetValue<ushort>(out _)
            || v.TryGetValue<byte>(out _)
            || v.TryGetValue<sbyte>(out _)
            || v.TryGetValue<decimal>(out _))
        {
            return true;
        }
        return double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            && double.IsFinite(parsed);
    }

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// 5-minute duplicate F2 guard: suppresses duplicate network uploads when an identical
/// room + fault fingerprint was already submitted within 5 minutes and fault counts have not changed,
/// while still saving local diagnostic bundles and allowing immediate override if any fault count changes.
/// </summary>
internal sealed class BugSubmissionGuard(TimeSpan? cooldown = null)
{
    internal static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _cooldown = cooldown ?? DefaultCooldown;
    private readonly object _gate = new();
    private string? _lastFingerprint;
    private int _lastFaultTotal;
    private DateTimeOffset _lastSubmittedUtc = DateTimeOffset.MinValue;
    private string? _lastReportId;

    internal static (string Fingerprint, int TotalFaultCount) ComputeStateFingerprint(JsonObject journal)
    {
        string runId = journal["client_run_id"]?.GetValue<string>() ?? "menu";
        string room = journal["state"]?["current_room"]?.GetValue<string>() ?? "none";
        int actIndex = journal["state"]?["current_act_index"]?.GetValue<int>() ?? -1;
        int floor = journal["state"]?["current_floor"]?.GetValue<int>() ?? -1;
        string cpRoom = journal["checkpoint"]?["room_type"]?.GetValue<string>() ?? "";
        string cpEncounter = journal["checkpoint"]?["encounter_id"]?.GetValue<string>()
            ?? journal["checkpoint"]?["encounter"]?.GetValue<string>() ?? "";
        string cpEvent = journal["checkpoint"]?["event_id"]?.GetValue<string>() ?? "";
        bool desync = journal["desync"]?["detected"]?.GetValue<bool>() == true;
        string desyncHash = journal["desync"]?["local_checksum"]?.ToString() ?? "";

        var faultBuilder = new StringBuilder();
        int totalFaults = 0;
        if (journal["runtime_faults"] is JsonArray faults)
        {
            foreach (var f in faults.OfType<JsonObject>())
            {
                string sig = f["signature"]?.GetValue<string>() ?? f["exception_type"]?.GetValue<string>() ?? "fault";
                int count = f["count"]?.GetValue<int>() ?? f["occurrences"]?.GetValue<int>() ?? 1;
                bool prev = f["session"]?.GetValue<string>() == "previous" || f["from_previous_session"]?.GetValue<bool>() == true;
                if (!prev) totalFaults += count;
                faultBuilder.Append(sig).Append(':').Append(count).Append(';');
            }
        }
        else if (journal["runtime_errors"] is JsonArray legacyErrors)
        {
            foreach (var e in legacyErrors.OfType<JsonObject>())
            {
                string sig = e["signature"]?.GetValue<string>() ?? e["type"]?.GetValue<string>() ?? "fault";
                int count = e["occurrences"]?.GetValue<int>() ?? 1;
                bool prev = e["from_previous_session"]?.GetValue<bool>() == true;
                if (!prev) totalFaults += count;
                faultBuilder.Append(sig).Append(':').Append(count).Append(';');
            }
        }

        int dropped = journal["dropped_runtime_errors"]?.GetValue<int>() ?? 0;
        totalFaults += dropped;

        string raw = $"{runId}|{actIndex}|{floor}|{room}|{cpRoom}|{cpEncounter}|{cpEvent}|desync={desync}:{desyncHash}|dropped={dropped}|faults={faultBuilder}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return (hash, totalFaults);
    }

    internal bool ShouldThrottleDuplicate(JsonObject journal, DateTimeOffset nowUtc, out string? priorReportId)
    {
        var (fp, totalFaults) = ComputeStateFingerprint(journal);
        lock (_gate)
        {
            if (_lastFingerprint is not null
                && string.Equals(_lastFingerprint, fp, StringComparison.Ordinal)
                && _lastFaultTotal == totalFaults
                && nowUtc >= _lastSubmittedUtc
                && (nowUtc - _lastSubmittedUtc) < _cooldown)
            {
                priorReportId = _lastReportId;
                return true;
            }
            priorReportId = null;
            return false;
        }
    }

    internal void RecordSubmitted(JsonObject journal, string? reportId, DateTimeOffset nowUtc)
    {
        var (fp, totalFaults) = ComputeStateFingerprint(journal);
        lock (_gate)
        {
            _lastFingerprint = fp;
            _lastFaultTotal = totalFaults;
            _lastSubmittedUtc = nowUtc;
            _lastReportId = reportId;
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _lastFingerprint = null;
            _lastFaultTotal = 0;
            _lastSubmittedUtc = DateTimeOffset.MinValue;
            _lastReportId = null;
        }
    }
}

/// <summary>
/// Pure .NET startup notice acknowledgment & suppression policy for STS2_Things 2.0.x.
/// </summary>
internal static class StartupNoticePolicy
{
    internal const string MajorVersion = "2.0";

    internal static bool IsEnvTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (string.Equals(value.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "yes", StringComparison.OrdinalIgnoreCase));

    internal static bool ShouldSuppressEnvironment(
        string? displayServerName,
        bool testModeOn,
        bool nonInteractiveActive,
        bool mcpBridgeLoaded,
        string? suppressEnvValue)
    {
        if (testModeOn || nonInteractiveActive || mcpBridgeLoaded) return true;
        if (string.Equals(displayServerName, "headless", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsEnvTruthy(suppressEnvValue)) return true;
        return false;
    }

    internal static bool IsAcknowledged(string ackFilePath, string majorVersion = MajorVersion)
    {
        try
        {
            var obj = BugReportStore.ReadObject(ackFilePath);
            if (obj is null) return false;
            if (string.Equals(obj["major_version"]?.GetValue<string>(), majorVersion, StringComparison.Ordinal))
                return true;
            if (obj["acknowledged_versions"] is JsonArray arr
                && arr.Any(n => string.Equals(n?.GetValue<string>(), majorVersion, StringComparison.Ordinal)))
                return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static void RecordAcknowledged(string ackFilePath, string majorVersion = MajorVersion)
    {
        try
        {
            var prior = BugReportStore.ReadObject(ackFilePath);
            var versions = new HashSet<string>(StringComparer.Ordinal) { majorVersion };
            if (prior?["acknowledged_versions"] is JsonArray existing)
            {
                foreach (var item in existing)
                {
                    if (item?.GetValue<string>() is { Length: > 0 } v) versions.Add(v);
                }
            }
            if (prior?["major_version"]?.GetValue<string>() is { Length: > 0 } prevMajor)
            {
                versions.Add(prevMajor);
            }

            var doc = new JsonObject
            {
                ["major_version"] = majorVersion,
                ["acknowledged_versions"] = new JsonArray(versions.Order(StringComparer.Ordinal).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
                ["acknowledged_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            };
            BugReportStore.WriteAtomic(ackFilePath, doc.ToJsonString());
        }
        catch (Exception)
        {
            // Notice persistence must never interrupt main menu execution.
        }
    }
}
