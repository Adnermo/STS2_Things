using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using STS2_Things.Diagnostics;

static partial class Tests
{
    private static void ReplayTests()
    {
        const ulong first = 76561199999999991, second = 76561199999999992;
        var save = JsonNode.Parse("""
            {"schema_version":20,"players":[{"net_id":76561199999999991},{"net_id":76561199999999992}],
             "rng":{"seed":"same-seed","rngs":{"shuffle":{"counter":39,"s0":76561199999999991}}},
             "map_point_history":[[{"player_stats":[{"player_id":76561199999999992}]}]],
             "extra_rewards":{"76561199999999991":[]},
             "props":{"strings":[{"name":"ApiKey","value":"PRIVATE_TEST_CREDENTIAL"},
                                    {"name":"SourceCard","value":"CARD.DUALCAST"},
                                    {"name":"Path","value":"C:\\PrivatePlayer\\secret.txt"}]}}
            """)!.AsObject();
        var encoded = BugReplayCodec.Encode(save, [first, second]);
        Check(encoded["available"]!.GetValue<bool>(), "native replay is available and compressed");
        using var zipped = new MemoryStream(Convert.FromBase64String(encoded["data"]!.GetValue<string>()));
        using var gzip = new GZipStream(zipped, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string text = reader.ReadToEnd();
        var restored = JsonNode.Parse(text)!;
        Check(restored["players"]![0]!["net_id"]!.GetValue<ulong>() == 1 && restored["players"]![1]!["net_id"]!.GetValue<ulong>() == 2, "native identities mapped to stable slots");
        Check(restored["map_point_history"]![0]![0]!["player_stats"]![0]!["player_id"]!.GetValue<ulong>() == 2, "history references remapped consistently");
        Check(restored["extra_rewards"]!["1"] is JsonArray, "identity dictionary keys remapped");
        Check(restored["rng"]!["rngs"]!["shuffle"]!["s0"]!.GetValue<ulong>() == first, "RNG state is unchanged");
        var display=BugReplayCodec.SanitizeSnapshot(save,[first,second]);
        Check(display["rng"]!["rngs"]!["shuffle"]!["s0"]!.GetValue<string>()==first.ToString(),"browser snapshot preserves uint64 RNG precision");
        Check(display["integer_encoding"]?.GetValue<string>()=="exact-decimal-strings-above-2^53","snapshot declares lossless integer encoding");
        Check(!text.Contains("PRIVATE_TEST_CREDENTIAL") && !text.Contains("PrivatePlayer"), "credentials and local paths removed");
        Check(text.Contains("CARD.DUALCAST"), "native gameplay property preserved");
        Check(encoded["redacted_fields"]!.GetValue<int>() == 2, "redactions declared rather than hidden");
        Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()
            == encoded["sha256"]!.GetValue<string>(), "export integrity matches anonymous native data");
        var huge = new JsonObject { ["schema_version"] = 20, ["padding"] = new string('a', BugReplayCodec.MaxPlainBytes + 1) };
        var denied = BugReplayCodec.Encode(huge, [first]);
        Check(denied["available"]!.GetValue<bool>() == false && denied["data"] is null, "oversized save explicitly unavailable");
    }
}
