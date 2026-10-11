using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

/// <summary>Portable native save data. Identity remapping is independent of the live game.</summary>
internal static class BugReplayCodec
{
    internal const int MaxPlainBytes = 2_000_000;
    internal const int MaxCompressedBytes = 110_000;
    private static readonly Regex PrivateKey = new(
        @"password|authorization|api[_-]?key|access[_-]?token|secret|cookie|email|user[_-]?name|nick[_-]?name|display[_-]?name|device[_-]?name|host[_-]?address",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PrivateLiteral = new(@"(?:[A-Za-z]:[\\/]|user://|file://|Bearer\s|\b\w+@\w+\.\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static JsonObject Encode(JsonObject nativeSave, IReadOnlyList<ulong> players)
    {
        var ids = players.Select((id, index) => (id, anon: (ulong)index + 1)).ToDictionary(p => p.id, p => p.anon);
        int removed = 0;
        var clean = (JsonObject)Clean(nativeSave, ids, ref removed, rng: false)!;
        byte[] plain = Encoding.UTF8.GetBytes(clean.ToJsonString());
        var result = new JsonObject {
            ["format"] = "sts2-native-save", ["encoding"] = "gzip-base64", ["schema"] = 1,
            ["native_schema"] = clean["schema_version"]?.DeepClone(), ["player_count"] = players.Count,
            ["identity"] = "anonymous-player-indices", ["redacted_fields"] = removed,
            ["plain_bytes"] = plain.Length, ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant()
        };
        if (plain.Length > MaxPlainBytes) { result["available"] = false; result["reason"] = "native_save_too_large"; return result; }
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(plain);
        if (buffer.Length > MaxCompressedBytes) { result["available"] = false; result["reason"] = "compressed_save_too_large"; return result; }
        result["available"] = true;
        result["data"] = Convert.ToBase64String(buffer.ToArray());
        return result;
    }

    internal static JsonObject SanitizeSnapshot(JsonObject snapshot, IReadOnlyList<ulong> players)
    {
        var ids = players.Select((id, index) => (id, anon: (ulong)index + 1)).ToDictionary(p => p.id, p => p.anon);
        int removed = 0;
        var result = (JsonObject)Clean(snapshot, ids, ref removed, rng: false)!;
        if (removed > 0) result["privacy_redactions"] = removed;
        MakeJsonSafe(result);
        return result;
    }

    // JavaScript dashboards parse JSON numbers as doubles. The compressed native
    // save keeps its original numeric types; display snapshots use exact strings
    // for integers that a browser cannot represent without rounding.
    internal static void MakeJsonSafe(JsonObject snapshot)
    {
        int changed = 0;
        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (string key in obj.Select(p => p.Key).ToArray())
                { var child = obj[key]; var clean = Visit(child); if (!ReferenceEquals(child, clean)) obj[key] = clean; }
            else if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++)
                { var child = array[i]; var clean = Visit(child); if (!ReferenceEquals(child, clean)) array[i] = clean; }
            else if (node is JsonValue scalar)
            {
                if (scalar.TryGetValue<ulong>(out ulong unsigned) && unsigned > 9_007_199_254_740_991)
                { changed++; return JsonValue.Create(unsigned.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                if (scalar.TryGetValue<long>(out long signed) && signed < -9_007_199_254_740_991)
                { changed++; return JsonValue.Create(signed.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            }
            return node;
        }
        Visit(snapshot);
        if (changed > 0) snapshot["integer_encoding"] = "exact-decimal-strings-above-2^53";
    }

    private static JsonNode? Clean(JsonNode? value, Dictionary<ulong, ulong> ids, ref int removed, bool rng)
    {
        if (value is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, child) in obj)
            {
                if (PrivateKey.IsMatch(key)) { removed++; continue; }
                string safeKey = ulong.TryParse(key, out var id) && ids.TryGetValue(id, out var anonymous)
                    ? anonymous.ToString(System.Globalization.CultureInfo.InvariantCulture) : key;
                result[safeKey] = Clean(child, ids, ref removed, rng || key is "rng" or "rngs");
            }
            return result;
        }
        if (value is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var child in array)
            {
                // SavedProperties stores its property name as a value, not an object key.
                if (child is JsonObject item && item["name"] is JsonValue name
                    && name.TryGetValue<string>(out var property) && PrivateKey.IsMatch(property)) { removed++; continue; }
                result.Add(Clean(child, ids, ref removed, rng));
            }
            return result;
        }
        if (value is JsonValue scalar)
        {
            if (!rng && scalar.TryGetValue<ulong>(out ulong id) && ids.TryGetValue(id, out ulong anonymous))
                return JsonValue.Create(anonymous);
            if (scalar.TryGetValue<string>(out string? text) && text is not null)
            {
                if (PrivateLiteral.IsMatch(text)) { removed++; return JsonValue.Create("[redacted]"); }
                foreach (var pair in ids)
                {
                    string original = pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (original.Length >= 12) text = text.Replace(original, "player-" + pair.Value, StringComparison.Ordinal);
                }
                return JsonValue.Create(text);
            }
        }
        return value?.DeepClone();
    }
}
