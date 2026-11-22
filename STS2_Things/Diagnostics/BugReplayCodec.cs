using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace STS2_Things.Diagnostics;

internal sealed record CheckpointMetadata(
    string? EntryKind = null,
    string? RoomType = null,
    string? ActId = null,
    int? ActIndex = null,
    int? Floor = null,
    int? TotalFloor = null,
    string? EncounterId = null,
    string? EventId = null,
    string? Seed = null);

/// <summary>
/// Portable native save (Checkpoint Schema 2) and anonymized combat replay codec.
/// Identity remapping and privacy scrubbing are pure .NET and independent of live game state.
/// </summary>
internal static class BugReplayCodec
{
    internal const int CheckpointSchema = 2;
    internal const int MaxPlainBytes = 4_000_000;
    internal const int MaxCompressedBytes = 330_000;
    internal const int MaxReplayPlainBytes = 2_000_000;
    internal const int MaxReplayCompressedBytes = 260_000;

    private static readonly Regex PrivateKey = new(
        @"password|authorization|api[_-]?key|access[_-]?token|secret|cookie|email|user[_-]?name|nick[_-]?name|display[_-]?name|device[_-]?name|host[_-]?address",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrivateLiteral = new(
        @"(?:[A-Za-z]:[\\/]|(?<![A-Za-z0-9_])/Users/|(?<![A-Za-z0-9_])/home/|user://|file://|Bearer\s|\b\w+@\w+\.\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static JsonObject Encode(JsonObject nativeSave, IReadOnlyList<ulong> players, CheckpointMetadata? metadata = null)
    {
        var ids = players.Select((id, index) => (id, anon: (ulong)index + 1)).ToDictionary(p => p.id, p => p.anon);
        int removed = 0;
        var clean = (JsonObject)Clean(nativeSave, ids, ref removed, rng: false)!;
        byte[] plain = Encoding.UTF8.GetBytes(clean.ToJsonString());

        int playerCount = clean["players"] is JsonArray savePlayers && savePlayers.Count > 0
            ? savePlayers.Count
            : players.Count;

        var result = new JsonObject
        {
            ["format"] = "sts2-native-save",
            ["encoding"] = "gzip-base64",
            ["schema"] = CheckpointSchema,
            ["schema_version"] = CheckpointSchema,
            ["native_schema"] = clean["schema_version"]?.DeepClone(),
            ["identity"] = "anonymous-player-indices",
            ["redacted_fields"] = removed,
            ["plain_bytes"] = plain.Length,
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant(),
        };

        // Keep Schema-2 player_count emission <= 8 so uploads stay backward-compatible with
        // any live Cloudflare Worker still enforcing <= 8 before redeployment. Checkpoint Schema 2
        // permits omitting player_count (and checkpoint.js preserves all 9–32 players inside `data`).
        if (playerCount is >= 1 and <= 8)
        {
            result["player_count"] = playerCount;
        }

        if (metadata is not null)
        {
            ApplyMetadata(result, metadata);
        }

        if (plain.Length == 0 || plain.Length > MaxPlainBytes)
        {
            result["available"] = false;
            result["reason"] = plain.Length == 0 ? "empty_native_save" : "native_save_too_large";
            return result;
        }

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        if (buffer.Length > MaxCompressedBytes)
        {
            result["available"] = false;
            result["reason"] = "compressed_save_too_large";
            return result;
        }

        string base64 = Convert.ToBase64String(buffer.ToArray());
        if (base64.Length > BugReportSchemaV4.MaxBlobBase64Length)
        {
            result["available"] = false;
            result["reason"] = "encoded_save_too_large";
            return result;
        }

        result["available"] = true;
        result["data"] = base64;
        return result;
    }

    private static void ApplyMetadata(JsonObject result, CheckpointMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.EntryKind))
        {
            string entryKind = LogSanitizer.Cap(metadata.EntryKind, 120);
            result["entry_kind"] = entryKind;
            result["kind"] = entryKind;
        }
        if (!string.IsNullOrWhiteSpace(metadata.RoomType))
        {
            result["room_type"] = LogSanitizer.Cap(metadata.RoomType, 120);
        }
        if (!string.IsNullOrWhiteSpace(metadata.ActId))
        {
            result["act_id"] = LogSanitizer.Cap(metadata.ActId, 120);
        }
        if (metadata.ActIndex.HasValue)
        {
            result["act_index"] = metadata.ActIndex.Value;
        }
        if (metadata.Floor.HasValue)
        {
            result["floor"] = metadata.Floor.Value;
        }
        if (metadata.TotalFloor.HasValue)
        {
            result["total_floor"] = metadata.TotalFloor.Value;
        }
        if (!string.IsNullOrWhiteSpace(metadata.EncounterId))
        {
            string enc = LogSanitizer.Cap(metadata.EncounterId, 200);
            result["encounter_id"] = enc;
            result["encounter"] = enc;
        }
        if (!string.IsNullOrWhiteSpace(metadata.EventId))
        {
            result["event_id"] = LogSanitizer.Cap(metadata.EventId, 200);
        }
        if (!string.IsNullOrWhiteSpace(metadata.Seed))
        {
            result["seed"] = LogSanitizer.Cap(metadata.Seed, 100);
        }
    }

    internal static JsonObject EncodeCombatReplay(ReadOnlySpan<byte> plainBytes, JsonObject? metadata = null)
    {
        if (plainBytes.Length == 0)
        {
            return UnavailableCombatReplay("empty_replay");
        }

        var result = new JsonObject
        {
            ["format"] = "sts2-combat-replay",
            ["encoding"] = "gzip-base64",
            ["plain_bytes"] = plainBytes.Length,
            ["captured_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(plainBytes)).ToLowerInvariant(),
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                if (!result.ContainsKey(key)) result[key] = value?.DeepClone();
            }
        }

        if (plainBytes.Length > MaxReplayPlainBytes)
        {
            result["available"] = false;
            result["reason"] = "replay_too_large";
            return result;
        }

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(plainBytes);
        }

        if (buffer.Length > MaxReplayCompressedBytes)
        {
            result["available"] = false;
            result["reason"] = "compressed_replay_too_large";
            return result;
        }

        string base64 = Convert.ToBase64String(buffer.ToArray());
        if (base64.Length > 350_000)
        {
            result["available"] = false;
            result["reason"] = "encoded_replay_too_large";
            return result;
        }

        result["available"] = true;
        result["data"] = base64;
        return result;
    }

    internal static JsonObject UnavailableCombatReplay(string reason) => new()
    {
        ["available"] = false,
        ["reason"] = LogSanitizer.Cap(reason, 200),
    };

    internal static byte[] DecodeGzipBase64(string base64, int maxPlainBytes = MaxPlainBytes)
    {
        byte[] compressed = Convert.FromBase64String(base64);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + read > maxPlainBytes)
            {
                throw new InvalidDataException("Decompressed payload exceeded maxPlainBytes.");
            }
            output.Write(chunk, 0, read);
        }
        return output.ToArray();
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
            {
                foreach (string key in obj.Select(p => p.Key).ToArray())
                {
                    var child = obj[key];
                    var clean = Visit(child);
                    if (!ReferenceEquals(child, clean)) obj[key] = clean;
                }
            }
            else if (node is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    var clean = Visit(child);
                    if (!ReferenceEquals(child, clean)) array[i] = clean;
                }
            }
            else if (node is JsonValue scalar)
            {
                if (scalar.TryGetValue<ulong>(out ulong unsigned) && unsigned > 9_007_199_254_740_991)
                {
                    changed++;
                    return JsonValue.Create(unsigned.ToString(CultureInfo.InvariantCulture));
                }
                if (scalar.TryGetValue<long>(out long signed) && signed < -9_007_199_254_740_991)
                {
                    changed++;
                    return JsonValue.Create(signed.ToString(CultureInfo.InvariantCulture));
                }
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
                if (PrivateKey.IsMatch(key))
                {
                    removed++;
                    continue;
                }
                string safeKey = ulong.TryParse(key, out var id) && ids.TryGetValue(id, out var anonymous)
                    ? anonymous.ToString(CultureInfo.InvariantCulture)
                    : key;
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
                    && name.TryGetValue<string>(out var property) && PrivateKey.IsMatch(property))
                {
                    removed++;
                    continue;
                }
                result.Add(Clean(child, ids, ref removed, rng));
            }
            return result;
        }
        if (value is JsonValue scalar)
        {
            if (!rng && scalar.TryGetValue<ulong>(out ulong id) && ids.TryGetValue(id, out ulong anonymous))
            {
                return JsonValue.Create(anonymous);
            }
            if (scalar.TryGetValue<string>(out string? text) && text is not null)
            {
                if (PrivateLiteral.IsMatch(text))
                {
                    removed++;
                    return JsonValue.Create("[redacted]");
                }
                foreach (var pair in ids)
                {
                    string original = pair.Key.ToString(CultureInfo.InvariantCulture);
                    if (original.Length >= 12)
                    {
                        string anonDigits = pair.Value.ToString(CultureInfo.InvariantCulture);
                        text = text.Replace("\"Payer\":" + original, "\"Payer\":" + anonDigits, StringComparison.Ordinal);
                        text = text.Replace(original, "player-" + anonDigits, StringComparison.Ordinal);
                    }
                }
                return JsonValue.Create(text);
            }
        }
        return value?.DeepClone();
    }
}
