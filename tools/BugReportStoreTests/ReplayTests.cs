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
            {"schema_version":20,"acts":[],"players":[{"net_id":76561199999999991},{"net_id":76561199999999992}],
             "rng":{"seed":"same-seed","rngs":{"shuffle":{"counter":39,"s0":76561199999999991}}},
             "map_point_history":[[{"player_stats":[{"player_id":76561199999999992}]}]],
             "extra_rewards":{"76561199999999991":[]},
             "props":{"strings":[{"name":"ApiKey","value":"PRIVATE_TEST_CREDENTIAL"},
                                    {"name":"SourceCard","value":"CARD.DUALCAST"},
                                    {"name":"Path","value":"C:\\PrivatePlayer\\secret.txt"}]}}
            """)!.AsObject();

        var metadata = new CheckpointMetadata(
            EntryKind: "event_entry",
            RoomType: "Event",
            ActId: "ACT.THINGS_DEPTHS",
            ActIndex: 1,
            Floor: 6,
            TotalFloor: 22,
            EventId: "EVENT.THINGS_ECHOING_WELL",
            Seed: "same-seed");

        var encoded = BugReplayCodec.Encode(save, [first, second], metadata);
        Check(encoded["available"]!.GetValue<bool>(), "native replay is available and compressed");
        Check(encoded["schema"]!.GetValue<int>() == 2 && encoded["schema_version"]!.GetValue<int>() == 2, "checkpoint uses Schema 2");
        Check(encoded["entry_kind"]!.GetValue<string>() == "event_entry"
            && encoded["room_type"]!.GetValue<string>() == "Event"
            && encoded["act_id"]!.GetValue<string>() == "ACT.THINGS_DEPTHS"
            && encoded["floor"]!.GetValue<int>() == 6
            && encoded["event_id"]!.GetValue<string>() == "EVENT.THINGS_ECHOING_WELL",
            "Checkpoint Schema 2 records non-combat room metadata");
        Check(BugReportSchemaV4.ValidCheckpoint(encoded), "Checkpoint Schema 2 satisfies Worker v2 validCheckpoint");

        using var zipped = new MemoryStream(Convert.FromBase64String(encoded["data"]!.GetValue<string>()));
        using var gzip = new GZipStream(zipped, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string text = reader.ReadToEnd();
        var restored = JsonNode.Parse(text)!;
        Check(restored["players"]![0]!["net_id"]!.GetValue<ulong>() == 1 && restored["players"]![1]!["net_id"]!.GetValue<ulong>() == 2, "native identities mapped to stable slots");
        Check(restored["map_point_history"]![0]![0]!["player_stats"]![0]!["player_id"]!.GetValue<ulong>() == 2, "history references remapped consistently");
        Check(restored["extra_rewards"]!["1"] is JsonArray, "identity dictionary keys remapped");
        Check(restored["rng"]!["rngs"]!["shuffle"]!["s0"]!.GetValue<ulong>() == first, "RNG state is unchanged");

        var display = BugReplayCodec.SanitizeSnapshot(save, [first, second]);
        Check(display["rng"]!["rngs"]!["shuffle"]!["s0"]!.GetValue<string>() == first.ToString(), "browser snapshot preserves uint64 RNG precision");
        Check(display["integer_encoding"]?.GetValue<string>() == "exact-decimal-strings-above-2^53", "snapshot declares lossless integer encoding");
        Check(!text.Contains("PRIVATE_TEST_CREDENTIAL") && !text.Contains("PrivatePlayer"), "credentials and local paths removed");
        Check(text.Contains("CARD.DUALCAST"), "native gameplay property preserved");
        Check(encoded["redacted_fields"]!.GetValue<int>() == 2, "redactions declared rather than hidden");
        Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()
            == encoded["sha256"]!.GetValue<string>(), "export integrity matches anonymous native data");

        var huge = new JsonObject { ["schema_version"] = 20, ["padding"] = new string('a', BugReplayCodec.MaxPlainBytes + 1) };
        var denied = BugReplayCodec.Encode(huge, [first]);
        Check(denied["available"]!.GetValue<bool>() == false && denied["data"] is null, "oversized save explicitly unavailable");
        Check(BugReportSchemaV4.ValidCheckpoint(denied), "unavailable checkpoint satisfies Worker v2 validCheckpoint");

        // Combat replay roundtrip
        byte[] sampleReplay = Encoding.UTF8.GetBytes("STS2_BINARY_REPLAY_FRAME_PAYLOAD_V2_ANONYMIZED");
        var replayMeta = new JsonObject { ["version"] = "v0.113.1", ["event_count"] = 14, ["choice_count"] = 3 };
        var encodedReplay = BugReplayCodec.EncodeCombatReplay(sampleReplay, replayMeta);
        Check(encodedReplay["available"]!.GetValue<bool>()
            && encodedReplay["format"]!.GetValue<string>() == "sts2-combat-replay"
            && encodedReplay["encoding"]!.GetValue<string>() == "gzip-base64",
            "combat replay encodes as sts2-combat-replay gzip-base64");
        Check(BugReportSchemaV4.ValidCombatReplay(encodedReplay), "combat replay satisfies Worker v2 validCombatReplay");
        byte[] decodedReplay = BugReplayCodec.DecodeGzipBase64(encodedReplay["data"]!.GetValue<string>(), BugReplayCodec.MaxReplayPlainBytes);
        Check(decodedReplay.SequenceEqual(sampleReplay), "combat replay roundtrips byte-for-byte");

        byte[] oversizedReplay = new byte[BugReplayCodec.MaxReplayPlainBytes + 16];
        var rejectedReplay = BugReplayCodec.EncodeCombatReplay(oversizedReplay);
        Check(rejectedReplay["available"]!.GetValue<bool>() == false && BugReportSchemaV4.ValidCombatReplay(rejectedReplay),
            "oversized combat replay cleanly degrades to unavailable");
    }
}
