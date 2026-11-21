import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import {createRequire} from "node:module";
import {test, before, after} from "node:test";
import {fileURLToPath} from "node:url";
import {gzipSync} from "node:zlib";
import {randomBytes} from "node:crypto";
import {missingColumns, POST_COLUMN_DDL} from "../migrate.mjs";
import {analyzeReport, sha256Hex} from "../fingerprint.js";

// Prefer project dependencies; use the already installed Wrangler runtime on the author's machine.
const require = createRequire(import.meta.url);
let Miniflare;
try { ({Miniflare} = require("miniflare")); }
catch { ({Miniflare} = require(path.join(process.env.APPDATA, "npm/node_modules/wrangler/node_modules/miniflare"))); }

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fixturePassword = "local-fixture-password-only";
const fixtureBearer = "local-fixture-bearer-api-token-only";
let mf, db;

before(async () => {
  mf = new Miniflare({
    scriptPath: path.join(root, "worker.js"),
    modules: true,
    modulesRules: [{type: "ESModule", include: ["**/*.js"]}, {type: "Text", include: ["**/*.html"]}],
    // Installed workerd supports up to 2026-08-08; production keeps its existing compatibility date.
    compatibilityDate: "2026-08-01",
    d1Databases: {DB: "report-v2-test", LEGACY_DB: "report-legacy-upgrade-test"},
    bindings: {
      ADMIN_PASSWORD: fixturePassword,
      SESSION_SECRET: "local-fixture-session-secret-only",
      ADMIN_API_TOKEN: fixtureBearer,
    },
  });
  db = await mf.getD1Database("DB");
  await db.exec((await fs.readFile(path.join(root, "schema.sql"), "utf8")).replaceAll("\n", " "));
});

after(async () => { await mf?.dispose(); });

async function sha256Buffer(bytes) {
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)), b => b.toString(16).padStart(2, "0")).join("");
}

function payload(schema = 2) {
  return {
    schema,
    source: "sts2_things",
    client_report_id: crypto.randomUUID().replaceAll("-", ""),
    description: "isolated regression report",
    reported_at: new Date().toISOString(),
    run: {
      schema,
      game_version: "v0.111.0",
      mod_version: "2.0.0",
      client_run_id: crypto.randomUUID().replaceAll("-", ""),
      active_mods: [{id: "STS2_Things", version: "2.0.0"}],
      events: [],
      state: {seed: "test", ascension: 10, current_act_index: 1, players: [], route: []},
    },
  };
}

async function makeCheckpointBlob(schema = 1, extraMeta = {}, padChars = 0) {
  const saveObj = {
    schema_version: 20,
    players: [{net_id: 1}],
    acts: padChars > 0 ? ["ACT_PAD_" + "A".repeat(padChars)] : [],
    rng: {seed: "checkpoint", rngs: {shuffle: {counter: 37, s0: 123}}},
  };
  const native = Buffer.from(JSON.stringify(saveObj));
  const sha256 = await sha256Buffer(native);
  return {
    native,
    checkpoint: {
      schema,
      available: true,
      format: "sts2-native-save",
      encoding: "gzip-base64",
      data: gzipSync(native).toString("base64"),
      sha256,
      plain_bytes: native.length,
      player_count: 1,
      ...extraMeta,
    },
  };
}

async function makeScreenshotBlob(extraBytes = 0) {
  const middle = Buffer.alloc(extraBytes, 0x42);
  const bytes = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x02]), middle, Buffer.from([0xff, 0xd9])]);
  const sha256 = await sha256Buffer(bytes);
  return {
    bytes,
    screenshot: {
      available: true,
      format: "things-screenshot",
      encoding: "jpeg-base64",
      width: 1920,
      height: 1080,
      bytes: bytes.length,
      sha256,
      data: bytes.toString("base64"),
    },
  };
}

async function makeReplayBlob(asJson = false, extraBytes = 0) {
  const plain = asJson
    ? Buffer.from(JSON.stringify({version: 1, encounter: "LEECH_MOTHER_ENCOUNTER", actions: [{id: 1, card: "STRIKE"}], pad: "R".repeat(extraBytes)}))
    : Buffer.concat([Buffer.from([0x4d, 0x43, 0x52, 0x01, 0x00, 0x10]), randomBytes(extraBytes || 32)]);
  const sha256 = await sha256Buffer(plain);
  return {
    plain,
    combat_replay: {
      available: true,
      format: "sts2-combat-replay",
      encoding: "gzip-base64",
      sha256,
      plain_bytes: plain.length,
      data: gzipSync(plain).toString("base64"),
    },
  };
}

function intake(value, ip = crypto.randomUUID(), route = "/api/reports") {
  return mf.dispatchFetch("https://test.invalid" + route, {
    method: "POST",
    headers: {"content-type": "application/json", "cf-connecting-ip": ip},
    body: JSON.stringify(value),
  });
}

async function auth() {
  const r = await mf.dispatchFetch("https://test.invalid/api/admin/login", {
    method: "POST",
    headers: {"content-type": "application/json", origin: "https://test.invalid", "cf-connecting-ip": crypto.randomUUID()},
    body: JSON.stringify({password: fixturePassword}),
  });
  assert.equal(r.status, 200);
  return r.headers.get("set-cookie").split(";")[0];
}

function bearerHeaders(extra = {}) {
  return {authorization: "Bearer " + fixtureBearer, ...extra};
}

test("fresh v2 schema, second execution, dashboard CSP, and health v2.0.0", async () => {
  await db.exec((await fs.readFile(path.join(root, "schema.sql"), "utf8")).replaceAll("\n", " "));
  const r = await mf.dispatchFetch("https://test.invalid/health");
  assert.equal(r.status, 200);
  const h = await r.json();
  assert.equal(h.version, "2.0.0");
  assert.equal(h.database, "ready");
  assert.equal(h.replay, true);
  assert.equal(h.clustering, true);

  const dash = await mf.dispatchFetch("https://test.invalid/admin");
  assert.equal(dash.status, 200);
  assert.match(dash.headers.get("content-security-policy"), /default-src 'none'/);
  const html = await dash.text();
  assert.ok(html.includes("问题聚类"));
  assert.ok(html.includes("了解每一次不寻常。"));

  assert.equal((await mf.dispatchFetch("https://test.invalid/api/admin/stats")).status, 401);
  const cookie = await auth();
  assert.equal((await mf.dispatchFetch("https://test.invalid/api/admin/stats", {headers: {cookie}})).status, 200);
});

test("legacy schema 1/2/3 and both /api/reports + /api/v1/reports intake routes are accepted", async () => {
  for (const schema of [1, 2, 3]) {
    assert.equal((await intake(payload(schema), crypto.randomUUID(), "/api/reports")).status, 201);
  }
  assert.equal((await intake(payload(4), crypto.randomUUID(), "/api/v1/reports")).status, 201);
});

test("schema 3 checkpoint is offloaded to report_blobs, authenticated, and downloaded byte-for-byte", async () => {
  const {native, checkpoint} = await makeCheckpointBlob(1);
  const p = payload(3);
  p.run.checkpoint = checkpoint;
  const r = await intake(p);
  assert.equal(r.status, 201);
  const id = (await r.json()).report_id;

  // Verify lightweight payload_json in reports table and offloaded data in report_blobs.
  const storedRow = await db.prepare("SELECT payload_json, has_checkpoint, payload_sha256 FROM reports WHERE id=?").bind(id).first();
  assert.equal(storedRow.has_checkpoint, 1);
  assert.ok(/^[0-9a-f]{64}$/.test(storedRow.payload_sha256));
  const storedParsed = JSON.parse(storedRow.payload_json);
  assert.equal(storedParsed.run.checkpoint.data, null);
  assert.equal(storedParsed.run.checkpoint.blob_offloaded, true);

  const blobRow = await db.prepare("SELECT kind, data_text FROM report_blobs WHERE report_id=?").bind(id).first();
  assert.equal(blobRow.kind, "checkpoint");
  assert.equal(blobRow.data_text, checkpoint.data);

  const url = "https://test.invalid/api/admin/reports/" + id + "/checkpoint";
  assert.equal((await mf.dispatchFetch(url)).status, 401);
  const downloaded = await mf.dispatchFetch(url, {headers: bearerHeaders()});
  assert.equal(downloaded.status, 200);
  assert.deepEqual(Buffer.from(await downloaded.arrayBuffer()), native);
  assert.match(downloaded.headers.get("content-disposition"), /attachment/);

  const bad = payload(3);
  bad.run.checkpoint = {...checkpoint, sha256: "0".repeat(64)};
  assert.equal((await intake(bad)).status, 422);
});

test("schema 4 full 1MB+ payload with checkpoint v2, 1080p screenshot, combat replay, assemblies, harmony_patches, runtime_faults, and desync", async () => {
  // Construct pseudo-random incompressible blobs so total JSON exceeds 1.05 MB while within 1.5 MB.
  const randomSavePayload = randomBytes(310000).toString("base64");
  const {native, checkpoint} = await makeCheckpointBlob(2, {
    entry_kind: "combat_entry",
    room_type: "MonsterRoom",
    encounter_id: "LEECH_MOTHER_ENCOUNTER",
    event_id: "",
  }, 0);
  // Rebuild checkpoint with large incompressible content (~415KB plain -> ~420KB base64).
  const bigSaveObj = {
    schema_version: 22,
    players: [{net_id: 1}, {net_id: 2}],
    acts: [randomSavePayload],
    rng: {seed: "v4-seed", rngs: {}},
  };
  const bigNative = Buffer.from(JSON.stringify(bigSaveObj));
  const bigCpSha = await sha256Buffer(bigNative);
  const bigCpB64 = gzipSync(bigNative, {level: 1}).toString("base64");
  assert.ok(bigCpB64.length > 350000 && bigCpB64.length <= 450000);

  // Screenshot ~235 KB JPEG -> ~314 KB base64
  const randomJpegBody = randomBytes(235000);
  const jpegBytes = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), randomJpegBody, Buffer.from([0xff, 0xd9])]);
  const jpegSha = await sha256Buffer(jpegBytes);

  // Binary .mcr combat replay ~250 KB -> ~334 KB base64
  const mcrPlain = Buffer.concat([Buffer.from("MCR2"), randomBytes(250000)]);
  const mcrSha = await sha256Buffer(mcrPlain);
  const mcrB64 = gzipSync(mcrPlain, {level: 1}).toString("base64");

  const p = payload(4);
  p.description = "Schema v4 full 1MB+ multiplayer desync + replay diagnostic bundle";
  p.run.checkpoint = {
    ...checkpoint,
    schema: 2,
    player_count: 2,
    plain_bytes: bigNative.length,
    sha256: bigCpSha,
    data: bigCpB64,
  };
  p.run.screenshot = {
    available: true,
    format: "things-screenshot",
    encoding: "jpeg-base64",
    width: 1920,
    height: 1080,
    bytes: jpegBytes.length,
    sha256: jpegSha,
    data: jpegBytes.toString("base64"),
  };
  p.run.combat_replay = {
    available: true,
    format: "sts2-combat-replay",
    encoding: "gzip-base64",
    plain_bytes: mcrPlain.length,
    sha256: mcrSha,
    data: mcrB64,
  };
  p.run.assemblies = [
    {name: "STS2_Things", version: "2.0.0.0", mvid: "11111111-2222-3333-4444-555555555555", sha256: "a".repeat(64), mod_id: "STS2_Things", is_dynamic: false},
    {name: "DynamicHarmonyPatch", is_dynamic: true},
  ];
  p.run.harmony_patches = [
    {method: "MegaCrit.Sts2.Core.Combat.CombatManager.ExecuteEnemyTurn", owners: ["STS2_Things"]},
  ];
  p.run.desync = {
    detected: true,
    at: new Date().toISOString(),
    state_id: 42,
    local_checksum: "deadbeef01",
    remote_checksum: "deadbeef99",
    model_cache_hash: "cafebabe",
    summary: "Enemy HP diverged after turn 3",
    field_diffs: ["monsters[0].hp: local=45 remote=49"],
  };
  p.run.runtime_faults = [
    {
      signature: "NullReferenceException@STS2_Things.Monsters.LeechMother.Brood",
      exception_type: "System.NullReferenceException",
      message: "Object reference not set to an instance of an object.",
      top_frame: "STS2_Things.Monsters.LeechMother.Brood",
      harmony_owners: ["STS2_Things"],
      count: 3,
      first_at: new Date().toISOString(),
      last_at: new Date().toISOString(),
      session: "current",
      inner_exceptions: ["System.InvalidOperationException: Pile locked"],
      stack: [
        "at Godot.GD.PushError(String message)",
        "at STS2_Things.Diagnostics.BugRuntimeFaults.Capture(Exception ex)",
        "at STS2_Things.Monsters.LeechMother+<Brood>d__8.MoveNext()",
      ],
    },
  ];
  p.run.modifiers = ["DAILY_MOD_HOARDER"];
  p.run.config = {custom_events: true};
  p.run.network = {type: "MultiplayerHost", peers: 2};

  const serialized = JSON.stringify(p);
  assert.ok(serialized.length > 1050000 && serialized.length < 1500000, `expected >1MB payload, got ${serialized.length}`);

  const ip = crypto.randomUUID();
  const res = await intake(p, ip, "/api/v1/reports");
  assert.equal(res.status, 201);
  const {report_id: id, duplicate} = await res.json();
  assert.equal(duplicate, false);

  // Idempotent retry of 1MB+ offloaded report uses payload_sha256 and returns 201 duplicate:true
  const retryRes = await intake(p, ip, "/api/v1/reports");
  assert.equal(retryRes.status, 201);
  const retryBody = await retryRes.json();
  assert.equal(retryBody.report_id, id);
  assert.equal(retryBody.duplicate, true);

  // Mutating 1 field on same client_report_id triggers 409 conflict even after blob offload
  const mutated = {...p, description: "tampered body with same client_report_id"};
  assert.equal((await intake(mutated, ip, "/api/v1/reports")).status, 409);

  // Detail endpoint omits huge blobs by default, and rehydrates when ?include_blobs=1
  const detailLite = await (await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}`, {headers: bearerHeaders()})).json();
  assert.equal(detailLite.report.schema_version, 4);
  assert.equal(detailLite.report.has_checkpoint, 1);
  assert.equal(detailLite.report.has_screenshot, 1);
  assert.equal(detailLite.report.has_replay, 1);
  assert.equal(detailLite.report.has_desync, 1);
  assert.equal(detailLite.report.runtime_error_count, 3);
  assert.equal(detailLite.report.category, "multiplayer_desync");
  assert.equal(detailLite.report.cluster_key, "desync:LEECH_MOTHER_ENCOUNTER");
  assert.equal(detailLite.report.network_type, "MultiplayerHost");
  assert.equal(detailLite.report.payload.run.checkpoint.data, null);
  assert.equal(detailLite.report.payload.run.screenshot.data, null);
  assert.equal(detailLite.report.payload.run.combat_replay.data, null);

  const detailFull = await (await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}?include_blobs=1`, {headers: bearerHeaders()})).json();
  assert.equal(detailFull.report.payload.run.checkpoint.data, bigCpB64);
  assert.equal(detailFull.report.payload.run.screenshot.data, jpegBytes.toString("base64"));
  assert.equal(detailFull.report.payload.run.combat_replay.data, mcrB64);

  // /export rehydrates all 3 blobs into a 100% self-contained JSON file
  const exported = await (await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}/export`, {headers: bearerHeaders()})).json();
  assert.equal(exported.run.checkpoint.data, bigCpB64);
  assert.equal(exported.run.screenshot.data, jpegBytes.toString("base64"));
  assert.equal(exported.run.combat_replay.data, mcrB64);

  // /checkpoint, /screenshot, and /replay download & verify integrity from report_blobs
  const dlCp = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}/checkpoint`, {headers: bearerHeaders()});
  assert.equal(dlCp.status, 200);
  assert.deepEqual(Buffer.from(await dlCp.arrayBuffer()), bigNative);

  const dlSc = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}/screenshot`, {headers: bearerHeaders()});
  assert.equal(dlSc.status, 200);
  assert.equal(dlSc.headers.get("content-type"), "image/jpeg");
  assert.deepEqual(Buffer.from(await dlSc.arrayBuffer()), jpegBytes);

  const dlRp = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}/replay`, {headers: bearerHeaders()});
  assert.equal(dlRp.status, 200);
  assert.equal(dlRp.headers.get("content-type"), "application/octet-stream");
  assert.match(dlRp.headers.get("content-disposition"), /things-replay-.*\.mcr"/);
  assert.deepEqual(Buffer.from(await dlRp.arrayBuffer()), mcrPlain);

  // DELETE cleans up both reports AND report_blobs
  const delRes = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}`, {method: "DELETE", headers: bearerHeaders()});
  assert.equal(delRes.status, 200);
  const remainingBlobs = await db.prepare("SELECT COUNT(*) AS n FROM report_blobs WHERE report_id=?").bind(id).first();
  assert.equal(remainingBlobs.n, 0);
});

test("JSON-formatted combat replay downloads with .json extension and corrupt replay is rejected", async () => {
  const {plain, combat_replay} = await makeReplayBlob(true);
  const p = payload(4);
  p.run.combat_replay = combat_replay;
  const r = await intake(p);
  assert.equal(r.status, 201);
  const id = (await r.json()).report_id;

  const dl = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id}/replay`, {headers: bearerHeaders()});
  assert.equal(dl.status, 200);
  assert.match(dl.headers.get("content-disposition"), /things-replay-.*\.json"/);
  assert.deepEqual(Buffer.from(await dl.arrayBuffer()), plain);

  const bad = payload(4);
  bad.run.combat_replay = {...combat_replay, sha256: "f".repeat(64)};
  assert.equal((await intake(bad)).status, 422);
});

test("unavailable checkpoint/replay are explicit 404s and legacy uint64 JSON export stays exact", async () => {
  const p = payload(4);
  p.run.checkpoint = {available: false, reason: "capture_failed"};
  p.run.combat_replay = {available: false, reason: "not_in_combat"};
  const r1 = await intake(p);
  assert.equal(r1.status, 201);
  const id1 = (await r1.json()).report_id;
  assert.equal((await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id1}/checkpoint`, {headers: bearerHeaders()})).status, 404);
  assert.equal((await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id1}/replay`, {headers: bearerHeaders()})).status, 404);

  const p2 = payload(3);
  let raw = JSON.stringify(p2);
  raw = raw.replace('"seed":"test"', '"seed":"test","legacy_rng":18446744073709551614');
  const r2 = await mf.dispatchFetch("https://test.invalid/api/reports", {
    method: "POST",
    headers: {"content-type": "application/json", "cf-connecting-ip": crypto.randomUUID()},
    body: raw,
  });
  assert.equal(r2.status, 201);
  const id2 = (await r2.json()).report_id;
  const downloaded = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${id2}/export`, {headers: bearerHeaders()});
  assert.equal(downloaded.status, 200);
  assert.equal(await downloaded.text(), raw);
});

test("deterministic fingerprinting & external mod attribution (PortraitPatch vs STS2_Things vs MomoLib vs manual)", async () => {
  // Verify pure-JS sha256Hex matches WebCrypto SHA-256
  const sample = "sts2_things_fingerprint_test_vector";
  assert.equal(sha256Hex(sample), await sha256Buffer(new TextEncoder().encode(sample)));

  // 1. CardArtEditor.PortraitPatch without matching active mod -> external:CardArtEditor
  const ext1 = payload(4);
  ext1.run.runtime_faults = [{
    exception_type: "System.ObjectDisposedException",
    message: "Cannot access a disposed object. Object name: 'Godot.TextureRect'.",
    stack: [
      "at Godot.GD.PushError(String msg)",
      "at MegaCrit.Sts2.Core.Logging.Log.Error(String msg)",
      "at System.Environment.get_StackTrace()",
      "at STS2_Things.Diagnostics.BugRuntimeFaults.OnError(String msg)",
      "at Godot.GodotObject.GetPtr(GodotObject instance)",
      "at Godot.TextureRect.SetTexture(Texture2D value)",
      "at CardArtEditor.PortraitPatch.RestoreDefaultState(NCard card)",
      "at MegaCrit.Sts2.Core.Nodes.Cards.NCard.UpdateVisuals()",
      "at STS2_Things.Monsters.LeechMother+<Brood>d__8.MoveNext()",
    ],
    count: 2,
  }];
  const a1 = analyzeReport(ext1);
  assert.equal(a1.attributed_mod_id, "external:CardArtEditor");
  assert.equal(a1.category, "external_mod_conflict");
  assert.equal(a1.runtime_error_count, 2);
  assert.match(a1.cluster_key, /^ext:CardArtEditor:ObjectDisposedException:PortraitPatch\.RestoreDefaultState$/);

  // 2. PortraitPatch WITH an active portrait pack mod in active_mods -> attributes to that portrait mod
  const ext2 = structuredClone(ext1);
  ext2.run.active_mods.push({id: "KoreanRobotPortraitPack", version: "1.2.0"});
  const a2 = analyzeReport(ext2);
  assert.equal(a2.attributed_mod_id, "KoreanRobotPortraitPack");
  assert.equal(a2.category, "external_mod_conflict");

  // 3. STS2_Things culprit stack (ignoring STS2_Things.Diagnostics wrapper)
  const own = payload(4);
  own.run.runtime_faults = [{
    exception_type: "System.NullReferenceException",
    message: "Object reference not set",
    stack: [
      "at STS2_Things.Diagnostics.BugRuntimeFaults.Capture(Exception ex)",
      "at System.Collections.Generic.Dictionary`2.FindValue(TKey key)",
      "at STS2_Things.Relics.AncientCompass+<AfterRoomEntered>d__5.MoveNext()",
    ],
    count: 1,
  }];
  const a3 = analyzeReport(own);
  assert.equal(a3.attributed_mod_id, "STS2_Things");
  assert.equal(a3.category, "mod_runtime_error");
  assert.equal(a3.cluster_key, "things:NullReferenceException:AncientCompass.AfterRoomEntered");

  // 4. Harmony-patched vanilla method attributed via run.harmony_patches
  const harm = payload(4);
  harm.run.harmony_patches = [{method: "MegaCrit.Sts2.Core.Combat.CombatManager.EndTurn", owners: ["MomoLib"]}];
  harm.run.runtime_faults = [{
    exception_type: "System.IndexOutOfRangeException",
    message: "Index was outside the bounds of the array.",
    stack: ["at MegaCrit.Sts2.Core.Combat.CombatManager.EndTurn_Patch1(Object self)"],
    count: 1,
  }];
  const a4 = analyzeReport(harm);
  assert.equal(a4.attributed_mod_id, "external:MomoLib");
  assert.equal(a4.category, "external_mod_conflict");

  // 5. Zero runtime errors & no desync -> manual:<location> gameplay_feedback
  const manual = payload(4);
  manual.run.events = [{kind: "room_entered", room: "TreasureRoom"}];
  const a5 = analyzeReport(manual);
  assert.equal(a5.category, "gameplay_feedback");
  assert.equal(a5.attributed_mod_id, "");
  assert.equal(a5.cluster_key, "manual:TreasureRoom");
});

test("Bearer ADMIN_API_TOKEN bypasses browser CSRF origin while Cookie enforces sameOrigin", async () => {
  const r = await intake(payload(4));
  const id = (await r.json()).report_id;
  const cookie = await auth();
  const url = `https://test.invalid/api/admin/reports/${id}`;

  // Cookie mode check
  const meCookie = await (await mf.dispatchFetch("https://test.invalid/api/admin/me", {headers: {cookie}})).json();
  assert.equal(meCookie.mode, "cookie");
  // Cookie mutation with wrong or missing Origin -> 403
  assert.equal((await mf.dispatchFetch(url, {
    method: "PATCH",
    headers: {cookie, "content-type": "application/json", origin: "https://evil.invalid"},
    body: JSON.stringify({status: "triaged"}),
  })).status, 403);
  assert.equal((await mf.dispatchFetch(url, {
    method: "PATCH",
    headers: {cookie, "content-type": "application/json"},
    body: JSON.stringify({status: "triaged"}),
  })).status, 403);
  // Cookie mutation with matching Origin -> 200
  assert.equal((await mf.dispatchFetch(url, {
    method: "PATCH",
    headers: {cookie, "content-type": "application/json", origin: "https://test.invalid"},
    body: JSON.stringify({status: "triaged", developer_note: "cookie ok"}),
  })).status, 200);

  // Bearer mode check (no Origin header required!)
  const meBearer = await (await mf.dispatchFetch("https://test.invalid/api/admin/me", {headers: bearerHeaders()})).json();
  assert.equal(meBearer.mode, "bearer");
  const patchBearer = await mf.dispatchFetch(url, {
    method: "PATCH",
    headers: bearerHeaders({"content-type": "application/json"}),
    body: JSON.stringify({
      status: "fixed_in_version",
      fixed_in_version: "2.0.1",
      attributed_mod_id: "STS2_Things",
      category: "mod_runtime_error",
      duplicate_of: "12345678",
      developer_note: "headless CLI triage",
    }),
  });
  assert.equal(patchBearer.status, 200);

  // Invalid Bearer token is rejected with 401 even if valid cookie is present
  assert.equal((await mf.dispatchFetch("https://test.invalid/api/admin/stats", {
    headers: {authorization: "Bearer wrong-token", cookie},
  })).status, 401);
});

test("clusters, rich filters, stats v2, and batch-update endpoints", async () => {
  const extPayloadA = payload(4);
  extPayloadA.run.mod_version = "2.0.0-rc1";
  extPayloadA.run.runtime_faults = [{
    exception_type: "System.ObjectDisposedException",
    message: "Disposed TextureRect",
    stack: ["at CardArtEditor.PortraitPatch.RestoreDefaultState(NCard c)"],
    count: 1,
  }];
  const extPayloadB = structuredClone(extPayloadA);
  extPayloadB.client_report_id = crypto.randomUUID().replaceAll("-", "");
  extPayloadB.run.mod_version = "2.0.0-rc2";

  const idA = await (await (await intake(extPayloadA)).json()).report_id;
  const idB = await (await (await intake(extPayloadB)).json()).report_id;

  // Clusters endpoint aggregates both under the same cluster_key
  const clustersRes = await (await mf.dispatchFetch("https://test.invalid/api/admin/clusters?attributed_mod_id=external:CardArtEditor", {
    headers: bearerHeaders(),
  })).json();
  assert.equal(clustersRes.total, 1);
  const cluster = clustersRes.items[0];
  assert.equal(cluster.count, 2);
  assert.equal(cluster.open_count, 2);
  assert.equal(cluster.attributed_mod_id, "external:CardArtEditor");
  assert.ok(cluster.mod_versions.includes("2.0.0-rc1") && cluster.mod_versions.includes("2.0.0-rc2"));

  // Batch update by cluster_key + filter_status
  const batchCluster = await (await mf.dispatchFetch("https://test.invalid/api/admin/reports/batch-update", {
    method: "POST",
    headers: bearerHeaders({"content-type": "application/json"}),
    body: JSON.stringify({
      cluster_key: cluster.cluster_key,
      filter_status: "new",
      patch: {status: "external_mod", developer_note: "CardArtEditor disposed node bug"},
    }),
  })).json();
  assert.equal(batchCluster.ok, true);
  assert.equal(batchCluster.updated, 2);

  // Filter GET /api/admin/reports by comma-separated status, cluster_key, limit=100
  const filtered = await (await mf.dispatchFetch(
    `https://test.invalid/api/admin/reports?status=external_mod,triaged&cluster_key=${encodeURIComponent(cluster.cluster_key)}&limit=100`,
    {headers: bearerHeaders()}
  )).json();
  assert.equal(filtered.total, 2);
  assert.ok(filtered.items.every(x => x.status === "external_mod" && x.developer_note === "CardArtEditor disposed node bug"));

  // Batch update by explicit ids
  const batchIds = await (await mf.dispatchFetch("https://test.invalid/api/admin/reports/batch-update", {
    method: "POST",
    headers: bearerHeaders({"content-type": "application/json"}),
    body: JSON.stringify({
      ids: [idA, idB],
      patch: {fixed_in_version: "external"},
    }),
  })).json();
  assert.equal(batchIds.updated, 2);

  // Stats v2 includes by_status, top_clusters, open_count, external_mod, resolved_total
  const s = await (await mf.dispatchFetch("https://test.invalid/api/admin/stats", {headers: bearerHeaders()})).json();
  assert.ok(s.total >= 2);
  assert.ok(s.external_mod >= 2);
  assert.ok(s.resolved_total >= 2);
  assert.equal(typeof s.by_status.external_mod, "number");
  assert.ok(Array.isArray(s.top_clusters) && s.top_clusters.length >= 1);
});

test("legacy v1 inline rows support fallback blob download AND upgrade cleanly via migrate + /api/admin/recluster", async () => {
  // 1. Verify migrate.mjs column diff on original v1 schema vs complete v2 schema
  assert.equal(missingColumns(["id", "payload_json"]).length, 18);
  assert.equal(missingColumns(["id", "status", "developer_note", "updated_at"]).length, 15);
  const allV2Cols = [
    "id", "status", "developer_note", "updated_at", "schema_version", "payload_sha256",
    "issue_fingerprint", "cluster_key", "cluster_title", "category", "attributed_mod_id",
    "fixed_in_version", "duplicate_of", "network_type", "has_checkpoint", "has_screenshot",
    "has_replay", "has_desync", "runtime_error_count",
  ];
  assert.deepEqual(missingColumns(allV2Cols), []);

  // 2. Test upgrading a genuine v1 SQLite database with pre-existing rows via migration SQL
  const legacyDb = await mf.getD1Database("LEGACY_DB");
  await legacyDb.exec(
    "CREATE TABLE reports (id TEXT PRIMARY KEY, client_report_id TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL, source TEXT NOT NULL, description TEXT NOT NULL, game_version TEXT, mod_version TEXT, seed TEXT, ascension INTEGER, act_index INTEGER, payload_json TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'new', developer_note TEXT NOT NULL DEFAULT '', updated_at TEXT);"
  );
  await legacyDb.prepare(
    "INSERT INTO reports(id,client_report_id,created_at,source,description,payload_json) VALUES (?,?,?,?,?,?)"
  ).bind(crypto.randomUUID(), "a".repeat(32), "2026-10-10T00:00:00Z", "sts2_things", "live row 1", "{}").run();
  const pragmaBefore = await legacyDb.prepare("PRAGMA table_info(reports)").all();
  for (const sql of missingColumns(pragmaBefore.results.map(c => c.name))) {
    await legacyDb.prepare(sql).run();
  }
  for (const sql of POST_COLUMN_DDL) {
    await legacyDb.prepare(sql.replaceAll("\n", " ")).run();
  }
  const preserved = await legacyDb.prepare("SELECT COUNT(*) AS n, status, has_checkpoint FROM reports").first();
  assert.equal(preserved.n, 1);
  assert.equal(preserved.status, "new");
  assert.equal(preserved.has_checkpoint, 0);

  // 3. Insert an un-migrated v1/v3 row directly into main DB (payload_sha256 NULL, inline checkpoint + legacy runtime_error event)
  const {native, checkpoint} = await makeCheckpointBlob(1);
  const {bytes: scBytes, screenshot} = await makeScreenshotBlob();
  const legacyPayload = payload(3);
  legacyPayload.run.checkpoint = checkpoint;
  legacyPayload.run.screenshot = screenshot;
  legacyPayload.run.events = [{
    at: "2026-10-12T00:31:15Z",
    kind: "runtime_error",
    detail: "System.ObjectDisposedException: Disposed\n   at BetterCallSpire.PortraitPatch.RestoreDefaultState(NCard c)\n   at STS2_Things.Monsters.LeechMother.Brood()",
  }];
  const legacyId = crypto.randomUUID();
  await db.prepare(
    "INSERT INTO reports(id,client_report_id,created_at,source,description,game_version,mod_version,payload_json,status) VALUES (?,?,?,?,?,?,?,?,?)"
  ).bind(
    legacyId, legacyPayload.client_report_id, "2026-10-12T00:33:44Z",
    "sts2_things", "legacy inline report", "v0.107.1", "1.25.18",
    JSON.stringify(legacyPayload), "new"
  ).run();

  // Before recluster: /checkpoint and /screenshot transparently fall back to inline payload_json!
  const inlineCp = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${legacyId}/checkpoint`, {headers: bearerHeaders()});
  assert.equal(inlineCp.status, 200);
  assert.deepEqual(Buffer.from(await inlineCp.arrayBuffer()), native);
  const inlineSc = await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${legacyId}/screenshot`, {headers: bearerHeaders()});
  assert.equal(inlineSc.status, 200);
  assert.deepEqual(Buffer.from(await inlineSc.arrayBuffer()), scBytes);

  // Run POST /api/admin/recluster with offload_blobs: true
  const reclusterRes = await (await mf.dispatchFetch("https://test.invalid/api/admin/recluster", {
    method: "POST",
    headers: bearerHeaders({"content-type": "application/json"}),
    body: JSON.stringify({offload_blobs: true, overwrite_manual_attribution: false}),
  })).json();
  assert.equal(reclusterRes.ok, true);
  assert.ok(reclusterRes.scanned >= 1);
  assert.ok(reclusterRes.blobs_offloaded >= 2);

  // Verify legacy row is now clustered, attributed to external:BetterCallSpire, blobs offloaded, AND idempotent retry still works!
  const upgraded = await (await mf.dispatchFetch(`https://test.invalid/api/admin/reports/${legacyId}`, {headers: bearerHeaders()})).json();
  assert.equal(upgraded.report.attributed_mod_id, "external:BetterCallSpire");
  assert.equal(upgraded.report.category, "external_mod_conflict");
  assert.equal(upgraded.report.has_checkpoint, 1);
  assert.equal(upgraded.report.has_screenshot, 1);
  assert.equal(upgraded.report.runtime_error_count, 1);
  assert.equal(upgraded.report.payload.run.checkpoint.data, null);

  const retryLegacy = await intake(legacyPayload);
  assert.equal(retryLegacy.status, 201);
  assert.equal((await retryLegacy.json()).duplicate, true);
});

test("malformed nested arrays, oversized streams (>1.5MB), parallel rate limits, and expired rate cleanup", async () => {
  for (const mutate of [
    p => p.run.active_mods = 42,
    p => p.run.state.players = "wrong",
    p => p.run.state.route = {},
    p => p.run.events = [{kind: 42}],
    p => p.run.state.players = [{powers: [{id: "POWER.STRENGTH", amount: "wrong"}]}],
    p => p.run.assemblies = [{name: 123}],
    p => p.run.desync = {detected: "yes"},
  ]) {
    const p = payload(4);
    mutate(p);
    assert.equal((await intake(p)).status, 422);
  }

  // Stream exceeding 1,500,000 bytes without Content-Length returns 413
  const hugeStream = new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode("x".repeat(1505000)));
      controller.close();
    },
  });
  const tooLarge = await mf.dispatchFetch("https://test.invalid/api/v1/reports", {
    method: "POST",
    headers: {"content-type": "application/json"},
    body: hugeStream,
    duplex: "half",
  });
  assert.equal(tooLarge.status, 413);

  const largeLogin = await mf.dispatchFetch("https://test.invalid/api/admin/login", {
    method: "POST",
    headers: {"content-type": "application/json", origin: "https://test.invalid", "cf-connecting-ip": crypto.randomUUID()},
    body: JSON.stringify({password: "x".repeat(2000)}),
  });
  assert.equal(largeLogin.status, 413);

  // Parallel rate limit (12/hour per IP bucket) + free idempotent duplicate retry
  const ip = crypto.randomUUID();
  const initial = payload(4);
  assert.equal((await intake(initial, ip)).status, 201);
  const responses = await Promise.all(Array.from({length: 20}, () => intake(payload(4), ip)));
  assert.equal(responses.filter(r => r.status === 201).length, 11);
  assert.equal(responses.filter(r => r.status === 429).length, 9);
  const retry = await intake(initial, ip);
  assert.equal(retry.status, 201);
  assert.equal((await retry.json()).duplicate, true);

  // Expired rate buckets cleaned up
  await db.prepare("INSERT OR REPLACE INTO intake_rate(bucket,count,updated_at) VALUES('expired-test',5,'2000-01-01')").run();
  assert.equal((await intake(payload(4))).status, 201);
  assert.equal(await db.prepare("SELECT bucket FROM intake_rate WHERE bucket='expired-test'").first(), null);
});
