import DASHBOARD from "./dashboard.html";
import {canonical, validateReport} from "./validation.js";
import {decodeCheckpoint} from "./checkpoint.js";
import {decodeScreenshot} from "./screenshot.js";
import {decodeReplay, replayExtension} from "./replay.js";
import {analyzeReport} from "./fingerprint.js";

const encoder = new TextEncoder();
const HEADERS = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store, private",
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer",
};
const respond = (data, status = 200, extra = {}) =>
  new Response(JSON.stringify(data), {status, headers: {...HEADERS, ...extra}});
const str = (v, n) => typeof v === "string" ? v.slice(0, n) : "";
const time = () => new Date().toISOString();

export const STATUSES = new Set([
  "new",
  "triaged",
  "investigating",
  "fixed_in_version",
  "external_mod",
  "duplicate",
  "not_a_bug",
  "needs_info",
  "wontfix",
  "resolved",
]);
const OPEN_STATUSES = ["new", "triaged", "investigating", "needs_info"];
const CLOSED_STATUSES = ["fixed_in_version", "external_mod", "duplicate", "not_a_bug", "wontfix", "resolved"];
const BLOB_KINDS = ["checkpoint", "screenshot", "combat_replay"];
const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const SUMMARY_COLUMNS = [
  "id", "client_report_id", "created_at", "updated_at", "source", "description",
  "game_version", "mod_version", "seed", "ascension", "act_index", "status", "developer_note",
  "schema_version", "issue_fingerprint", "cluster_key", "cluster_title", "category",
  "attributed_mod_id", "fixed_in_version", "duplicate_of", "network_type",
  "has_checkpoint", "has_screenshot", "has_replay", "has_desync", "runtime_error_count",
].join(",");

class RequestFailure extends Error {
  constructor(status, code) {
    super(code);
    this.status = status;
  }
}

async function readJson(req, maximum) {
  if (!(req.headers.get("content-type") || "").toLowerCase().startsWith("application/json")) {
    throw new RequestFailure(415, "invalid_content_type");
  }
  if (Number(req.headers.get("content-length") || 0) > maximum) throw new RequestFailure(413, "too_large");
  if (!req.body) throw new RequestFailure(400, "invalid_json");
  const reader = req.body.getReader(), chunks = [];
  let length = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > maximum) {
        await reader.cancel();
        throw new RequestFailure(413, "too_large");
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.length;
  }
  const raw = new TextDecoder().decode(bytes);
  try {
    return {raw, data: JSON.parse(raw)};
  } catch {
    throw new RequestFailure(400, "invalid_json");
  }
}

async function readOptionalJson(req, maximum) {
  const ct = (req.headers.get("content-type") || "").toLowerCase();
  if (!req.body) return {};
  if (Number(req.headers.get("content-length") || 0) > maximum) throw new RequestFailure(413, "too_large");
  const reader = req.body.getReader(), chunks = [];
  let length = 0;
  try {
    while (true) {
      const {done, value} = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > maximum) {
        await reader.cancel();
        throw new RequestFailure(413, "too_large");
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  if (length === 0) return {};
  if (ct && !ct.startsWith("application/json")) throw new RequestFailure(415, "invalid_content_type");
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.length;
  }
  try {
    const parsed = JSON.parse(new TextDecoder().decode(bytes));
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) throw new Error("not_object");
    return parsed;
  } catch {
    throw new RequestFailure(400, "invalid_json");
  }
}

function cookie(name, value, age) {
  return name + "=" + value + "; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=" + age;
}
function fail(status = 401, error = "unauthorized") {
  return respond({error}, status);
}
async function digest(text) {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(text)));
}
function hex(bytes) {
  return Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
}
function compare(a, b) {
  if (a.length !== b.length) return false;
  let x = 0;
  for (let i = 0; i < a.length; i++) x |= a[i] ^ b[i];
  return x === 0;
}
function encBase(s) {
  return btoa(s).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}
function decBase(s) {
  return atob(s.replaceAll("-", "+").replaceAll("_", "/"));
}
async function key(env) {
  return crypto.subtle.importKey("raw", encoder.encode(env.SESSION_SECRET), {name: "HMAC", hash: "SHA-256"}, false, ["sign", "verify"]);
}
async function createSession(env) {
  const plain = Date.now() + "|" + crypto.randomUUID();
  const unsigned = encBase(plain);
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", await key(env), encoder.encode(unsigned)));
  return unsigned + "." + hex(signature);
}

async function authorizedCookie(req, env) {
  if (!env.SESSION_SECRET) return false;
  const raw = req.headers.get("cookie") || "";
  const m = raw.match(/(?:^|;\s*)spire_admin_session=([^;]+)/);
  if (!m) return false;
  const [unsigned, sign] = m[1].split(".");
  if (!unsigned || !sign || !/^[0-9a-f]{64}$/i.test(sign)) return false;
  try {
    const s = decBase(unsigned);
    const [t, n] = s.split("|");
    const issued = Number(t);
    if (!n || !Number.isFinite(issued) || issued > Date.now() + 60_000 || issued < Date.now() - 12 * 3600000) return false;
    const bytes = new Uint8Array(sign.match(/../g).map(x => parseInt(x, 16)));
    return crypto.subtle.verify("HMAC", await key(env), bytes, encoder.encode(unsigned));
  } catch {
    return false;
  }
}

export async function checkAdminAuth(req, env) {
  const authHeader = req.headers.get("authorization") || "";
  if (authHeader) {
    const m = authHeader.match(/^Bearer\s+(.+)$/i);
    if (!m) return {ok: false, mode: null};
    const provided = m[1].trim();
    const expected = typeof env.ADMIN_API_TOKEN === "string" ? env.ADMIN_API_TOKEN.trim() : "";
    if (!expected || !provided || provided.length > 512) return {ok: false, mode: null};
    const valid = compare(await digest(provided), await digest(expected));
    return valid ? {ok: true, mode: "bearer"} : {ok: false, mode: null};
  }
  if (await authorizedCookie(req, env)) return {ok: true, mode: "cookie"};
  return {ok: false, mode: null};
}

function sameOrigin(req) {
  return req.headers.get("origin") === new URL(req.url).origin;
}
function canMutate(req, authInfo) {
  return authInfo.mode === "bearer" || sameOrigin(req);
}

function htmlPage() {
  return new Response(DASHBOARD, {
    headers: {
      "content-type": "text/html; charset=utf-8",
      "cache-control": "no-store, private",
      "x-content-type-options": "nosniff",
      "referrer-policy": "no-referrer",
      "x-frame-options": "DENY",
      "content-security-policy": "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'",
    },
  });
}

async function rateBucket(req, tag) {
  const ip = req.headers.get("cf-connecting-ip") || "unknown";
  return tag + "-" + hex((await digest(ip + ":" + new Date().toISOString().slice(0, 13) + ":" + tag)).slice(0, 12));
}
async function claimRate(env, bucket, limit) {
  const result = await env.DB.prepare(
    "INSERT INTO intake_rate(bucket,count,updated_at) VALUES (?,1,?) ON CONFLICT(bucket) DO UPDATE SET count=count+1,updated_at=excluded.updated_at WHERE intake_rate.count<? RETURNING count"
  ).bind(bucket, time(), limit).all();
  return result.results.length > 0;
}

async function login(req, env) {
  if (!env.ADMIN_PASSWORD || !env.SESSION_SECRET) return respond({error: "admin_not_configured"}, 503);
  if (!sameOrigin(req)) return fail(403, "origin_mismatch");
  const bucket = await rateBucket(req, "admin_login");
  if (!(await claimRate(env, bucket, 10))) return fail(429, "rate_limited");
  const {data: body} = await readJson(req, 1024);
  const input = typeof body?.password === "string" ? body.password : "";
  const passwordOk = input.length > 0 && input.length <= 200 && compare(await digest(input), await digest(env.ADMIN_PASSWORD));
  if (!passwordOk) return fail();
  const token = await createSession(env);
  return respond({ok: true}, 200, {"set-cookie": cookie("spire_admin_session", token, 43200)});
}

function base64DecodedByteLength(b64) {
  const clean = b64.replace(/=+$/, "");
  return Math.floor((clean.length * 3) / 4);
}

function extractPayloadBlobs(payload, raw) {
  const blobs = [];
  if (!payload?.run || typeof payload.run !== "object") {
    return {blobs, storedPayloadJson: raw};
  }
  const clonedRun = {...payload.run};
  let modified = false;
  for (const kind of BLOB_KINDS) {
    const entry = payload.run[kind];
    if (entry && typeof entry === "object" && entry.available === true && typeof entry.data === "string" && entry.data.length > 0) {
      const {data, ...meta} = entry;
      blobs.push({
        kind,
        format: String(entry.format || kind),
        encoding: String(entry.encoding || "base64"),
        sha256: typeof entry.sha256 === "string" ? entry.sha256.toLowerCase() : null,
        plain_bytes: Number.isInteger(entry.plain_bytes) ? entry.plain_bytes : (Number.isInteger(entry.bytes) ? entry.bytes : null),
        byte_length: Number.isInteger(entry.bytes) ? entry.bytes : base64DecodedByteLength(data),
        meta_json: JSON.stringify(meta),
        data_text: data,
      });
      clonedRun[kind] = {...meta, data: null, blob_offloaded: true};
      modified = true;
    }
  }
  if (!modified) return {blobs, storedPayloadJson: raw};
  return {blobs, storedPayloadJson: JSON.stringify({...payload, run: clonedRun})};
}

function rehydratePayload(payload, blobRows) {
  if (!payload || typeof payload !== "object" || !payload.run || typeof payload.run !== "object") return payload;
  const run = {...payload.run};
  for (const row of blobRows) {
    if (run[row.kind] && typeof run[row.kind] === "object") {
      const copy = {...run[row.kind], data: row.data_text};
      delete copy.blob_offloaded;
      delete copy.blob_inline_omitted;
      run[row.kind] = copy;
    }
  }
  return {...payload, run};
}

function omitInlineBlobs(payload) {
  if (!payload || typeof payload !== "object" || !payload.run || typeof payload.run !== "object") return payload;
  let changed = false;
  const run = {...payload.run};
  for (const kind of BLOB_KINDS) {
    const entry = run[kind];
    if (entry && typeof entry === "object" && typeof entry.data === "string" && entry.data.length > 0) {
      run[kind] = {...entry, data: null, blob_offloaded: true};
      changed = true;
    }
  }
  return changed ? {...payload, run} : payload;
}

async function receive(req, env) {
  const {raw, data: p} = await readJson(req, 1500000);
  if (!validateReport(p)) return respond({error: "invalid_report"}, 422);
  if (p.run.checkpoint?.available) {
    try { await decodeCheckpoint(p.run.checkpoint); } catch { return respond({error: "invalid_checkpoint"}, 422); }
  }
  if (p.run.screenshot?.available) {
    try { await decodeScreenshot(p.run.screenshot); } catch { return respond({error: "invalid_screenshot"}, 422); }
  }
  if (p.run.combat_replay?.available) {
    try { await decodeReplay(p.run.combat_replay); } catch { return respond({error: "invalid_replay"}, 422); }
  }

  const canonicalFull = canonical(p);
  const payloadSha256 = hex(await digest(canonicalFull));
  const analysis = analyzeReport(p);
  const {blobs, storedPayloadJson} = extractPayloadBlobs(p, raw);

  const bucket = await rateBucket(req, "intake");
  const id = crypto.randomUUID(), created = time();

  try {
    const statements = [
      // 0: Reserve a rate slot only for a brand-new client_report_id.
      env.DB.prepare(
        "INSERT INTO intake_rate(bucket,count,updated_at) SELECT ?,1,? WHERE NOT EXISTS(SELECT 1 FROM reports WHERE client_report_id=?) ON CONFLICT(bucket) DO UPDATE SET count=count+1,updated_at=excluded.updated_at WHERE intake_rate.count<12 AND NOT EXISTS(SELECT 1 FROM reports WHERE client_report_id=?) RETURNING count"
      ).bind(bucket, created, p.client_report_id, p.client_report_id),
      // 1: Insert report iff the rate reservation above modified 1 row.
      env.DB.prepare(
        `INSERT INTO reports(
          id,client_report_id,created_at,source,description,game_version,mod_version,seed,ascension,act_index,payload_json,
          schema_version,payload_sha256,issue_fingerprint,cluster_key,cluster_title,category,attributed_mod_id,network_type,
          has_checkpoint,has_screenshot,has_replay,has_desync,runtime_error_count
        ) SELECT ?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,? WHERE changes()=1 ON CONFLICT(client_report_id) DO NOTHING`
      ).bind(
        id, p.client_report_id, created, p.source, p.description,
        str(p.run.game_version, 80), str(p.run.mod_version, 80), str(p.run.state?.seed, 100),
        Number.isInteger(p.run.state?.ascension) ? p.run.state.ascension : null,
        Number.isInteger(p.run.state?.current_act_index) ? p.run.state.current_act_index : null,
        storedPayloadJson,
        analysis.schema_version, payloadSha256, analysis.issue_fingerprint,
        analysis.cluster_key, analysis.cluster_title, analysis.category,
        analysis.attributed_mod_id, analysis.network_type,
        analysis.has_checkpoint, analysis.has_screenshot, analysis.has_replay,
        analysis.has_desync, analysis.runtime_error_count
      ),
    ];

    for (const b of blobs) {
      statements.push(
        env.DB.prepare(
          "INSERT OR REPLACE INTO report_blobs(report_id,kind,format,encoding,sha256,plain_bytes,byte_length,meta_json,data_text,created_at) SELECT ?,?,?,?,?,?,?,?,?,? WHERE EXISTS(SELECT 1 FROM reports WHERE id=?)"
        ).bind(
          id, b.kind, b.format, b.encoding, b.sha256, b.plain_bytes,
          b.byte_length, b.meta_json, b.data_text, created, id
        )
      );
    }

    const selectIdx = statements.length;
    statements.push(
      env.DB.prepare("SELECT id,payload_json,payload_sha256 FROM reports WHERE client_report_id=?").bind(p.client_report_id),
      env.DB.prepare("DELETE FROM intake_rate WHERE updated_at<?").bind(new Date(Date.now() - 48 * 3600000).toISOString())
    );

    const results = await env.DB.batch(statements);
    const receipt = results[selectIdx].results[0];
    if (!receipt) return respond({error: "rate_limited"}, 429, {"retry-after": "3600"});

    if (receipt.payload_sha256) {
      if (receipt.payload_sha256 !== payloadSha256) return respond({error: "report_id_conflict"}, 409);
    } else {
      if (canonical(JSON.parse(receipt.payload_json)) !== canonicalFull) return respond({error: "report_id_conflict"}, 409);
    }
    return respond({ok: true, report_id: receipt.id, duplicate: results[1].meta.changes === 0}, 201);
  } catch (err) {
    console.error("intake error", err?.message);
    return respond({error: "storage_unavailable"}, 503);
  }
}

function parseStatusFilter(rawStatus) {
  if (!rawStatus || rawStatus === "all") return [];
  if (rawStatus === "open") return [...OPEN_STATUSES];
  return rawStatus.split(",").map(s => s.trim()).filter(s => STATUSES.has(s));
}

async function stats(env) {
  const weekIso = new Date(Date.now() - 7 * 86400000).toISOString();
  const [c, byStatusRows, topClustersRows, t] = await Promise.all([
    env.DB.prepare(
      `SELECT
        COUNT(*) AS total,
        SUM(CASE WHEN status IN ('new','triaged','investigating','needs_info') THEN 1 ELSE 0 END) AS open_count,
        SUM(CASE WHEN status='new' THEN 1 ELSE 0 END) AS new_count,
        SUM(CASE WHEN status='fixed_in_version' THEN 1 ELSE 0 END) AS fixed_in_version,
        SUM(CASE WHEN status='external_mod' THEN 1 ELSE 0 END) AS external_mod,
        SUM(CASE WHEN status='resolved' THEN 1 ELSE 0 END) AS resolved,
        SUM(CASE WHEN status IN ('fixed_in_version','external_mod','duplicate','not_a_bug','wontfix','resolved') THEN 1 ELSE 0 END) AS resolved_total,
        SUM(CASE WHEN has_desync=1 THEN 1 ELSE 0 END) AS desync_count,
        SUM(CASE WHEN created_at>=? THEN 1 ELSE 0 END) AS week
      FROM reports`
    ).bind(weekIso).first(),
    env.DB.prepare("SELECT status, COUNT(*) AS count FROM reports GROUP BY status").all(),
    env.DB.prepare(
      `SELECT
        cluster_key,
        MAX(cluster_title) AS cluster_title,
        MAX(category) AS category,
        MAX(attributed_mod_id) AS attributed_mod_id,
        COUNT(*) AS count,
        SUM(CASE WHEN status IN ('new','triaged','investigating','needs_info') THEN 1 ELSE 0 END) AS open_count,
        MAX(created_at) AS last_seen
      FROM reports
      WHERE cluster_key IS NOT NULL AND cluster_key != ''
      GROUP BY cluster_key
      ORDER BY count DESC, last_seen DESC
      LIMIT 10`
    ).all(),
    env.DB.prepare(
      "SELECT substr(created_at,1,10) AS day, COUNT(*) AS count FROM reports WHERE created_at>=? GROUP BY substr(created_at,1,10) ORDER BY day"
    ).bind(weekIso).all(),
  ]);

  const by_status = {};
  for (const s of STATUSES) by_status[s] = 0;
  for (const row of byStatusRows.results || []) {
    if (row.status) by_status[row.status] = row.count || 0;
  }

  const byDay = new Map((t.results || []).map(x => [x.day, x.count]));
  const trend = Array.from({length: 7}, (_, i) => {
    const date = new Date(Date.now() - (6 - i) * 86400000).toISOString().slice(0, 10);
    return {day: date, count: byDay.get(date) || 0};
  });

  return respond({
    total: c?.total || 0,
    open_count: c?.open_count || 0,
    new: c?.new_count || 0,
    fixed_in_version: c?.fixed_in_version || 0,
    external_mod: c?.external_mod || 0,
    resolved: c?.resolved || 0,
    resolved_total: c?.resolved_total || 0,
    desync_count: c?.desync_count || 0,
    week: c?.week || 0,
    by_status,
    top_clusters: topClustersRows.results || [],
    trend,
  });
}

async function list(req, env) {
  const url = new URL(req.url);
  const search = str(url.searchParams.get("q"), 120).trim();
  const statusList = parseStatusFilter(url.searchParams.get("status"));
  const clusterKey = str(url.searchParams.get("cluster_key"), 240).trim();
  const attributedMod = str(url.searchParams.get("attributed_mod_id"), 200).trim();
  const category = str(url.searchParams.get("category"), 120).trim();
  const modVersion = str(url.searchParams.get("mod_version"), 80).trim();
  const gameVersion = str(url.searchParams.get("game_version"), 80).trim();
  const hasDesync = url.searchParams.get("has_desync");
  const hasCheckpoint = url.searchParams.get("has_checkpoint");
  const hasReplay = url.searchParams.get("has_replay");
  const sort = url.searchParams.get("sort") === "oldest" ? "ASC" : "DESC";
  const limit = Math.floor(Math.min(200, Math.max(1, Number(url.searchParams.get("limit")) || 20)));
  const page = Math.floor(Math.min(10000, Math.max(0, Number(url.searchParams.get("page")) || 0)));

  const where = ["1=1"];
  const binds = [];

  if (statusList.length === 1) {
    where.push("status=?");
    binds.push(statusList[0]);
  } else if (statusList.length > 1) {
    where.push("status IN (" + statusList.map(() => "?").join(",") + ")");
    binds.push(...statusList);
  }
  if (clusterKey) {
    where.push("cluster_key=?");
    binds.push(clusterKey);
  }
  if (attributedMod === "__external__") {
    where.push("attributed_mod_id IS NOT NULL AND attributed_mod_id != '' AND attributed_mod_id != 'STS2_Things'");
  } else if (attributedMod) {
    where.push("attributed_mod_id=?");
    binds.push(attributedMod);
  }
  if (category) {
    where.push("category=?");
    binds.push(category);
  }
  if (modVersion) {
    where.push("mod_version=?");
    binds.push(modVersion);
  }
  if (gameVersion) {
    where.push("game_version=?");
    binds.push(gameVersion);
  }
  if (hasDesync === "1" || hasDesync === "0") {
    where.push("has_desync=?");
    binds.push(Number(hasDesync));
  }
  if (hasCheckpoint === "1" || hasCheckpoint === "0") {
    where.push("has_checkpoint=?");
    binds.push(Number(hasCheckpoint));
  }
  if (hasReplay === "1" || hasReplay === "0") {
    where.push("has_replay=?");
    binds.push(Number(hasReplay));
  }
  if (search) {
    where.push(
      "(description LIKE ? OR seed LIKE ? OR game_version LIKE ? OR mod_version LIKE ? OR id LIKE ? OR client_report_id LIKE ? OR cluster_key LIKE ? OR cluster_title LIKE ? OR attributed_mod_id LIKE ? OR issue_fingerprint LIKE ?)"
    );
    const term = "%" + search + "%";
    binds.push(term, term, term, term, term, term, term, term, term, term);
  }

  const clause = where.join(" AND ");
  const [rows, count] = await Promise.all([
    env.DB.prepare(
      "SELECT " + SUMMARY_COLUMNS + " FROM reports WHERE " + clause + " ORDER BY created_at " + sort + " LIMIT ? OFFSET ?"
    ).bind(...binds, limit, page * limit).all(),
    env.DB.prepare("SELECT COUNT(*) AS n FROM reports WHERE " + clause).bind(...binds).first(),
  ]);
  return respond({items: rows.results || [], total: count?.n || 0, page, limit});
}

async function clustersRoute(req, env) {
  const url = new URL(req.url);
  const statusList = parseStatusFilter(url.searchParams.get("status"));
  const modVersion = str(url.searchParams.get("mod_version"), 80).trim();
  const gameVersion = str(url.searchParams.get("game_version"), 80).trim();
  const category = str(url.searchParams.get("category"), 120).trim();
  const attributedMod = str(url.searchParams.get("attributed_mod_id"), 200).trim();
  const search = str(url.searchParams.get("q"), 120).trim();

  const where = ["1=1"];
  const binds = [];
  if (statusList.length === 1) {
    where.push("status=?");
    binds.push(statusList[0]);
  } else if (statusList.length > 1) {
    where.push("status IN (" + statusList.map(() => "?").join(",") + ")");
    binds.push(...statusList);
  }
  if (modVersion) {
    where.push("mod_version=?");
    binds.push(modVersion);
  }
  if (gameVersion) {
    where.push("game_version=?");
    binds.push(gameVersion);
  }
  if (category) {
    where.push("category=?");
    binds.push(category);
  }
  if (attributedMod === "__external__") {
    where.push("attributed_mod_id IS NOT NULL AND attributed_mod_id != '' AND attributed_mod_id != 'STS2_Things'");
  } else if (attributedMod) {
    where.push("attributed_mod_id=?");
    binds.push(attributedMod);
  }
  if (search) {
    const term = "%" + search + "%";
    where.push("(cluster_key LIKE ? OR cluster_title LIKE ? OR attributed_mod_id LIKE ? OR description LIKE ?)");
    binds.push(term, term, term, term);
  }

  const rows = await env.DB.prepare(
    "SELECT id,created_at,status,game_version,mod_version,issue_fingerprint,cluster_key,cluster_title,category,attributed_mod_id FROM reports WHERE "
      + where.join(" AND ")
      + " ORDER BY created_at DESC"
  ).bind(...binds).all();

  const map = new Map();
  for (const r of rows.results || []) {
    const keyName = r.cluster_key || "unclustered";
    let group = map.get(keyName);
    if (!group) {
      group = {
        cluster_key: keyName,
        cluster_title: r.cluster_title || keyName,
        issue_fingerprint: r.issue_fingerprint || "",
        category: r.category || "",
        attributed_mod_id: r.attributed_mod_id || "",
        count: 0,
        open_count: 0,
        statuses: {},
        sample_report_id: r.id,
        first_seen: r.created_at,
        last_seen: r.created_at,
        _modVersions: new Set(),
        _gameVersions: new Set(),
      };
      map.set(keyName, group);
    }
    group.count++;
    if (OPEN_STATUSES.includes(r.status)) group.open_count++;
    group.statuses[r.status] = (group.statuses[r.status] || 0) + 1;
    if (!group.cluster_title && r.cluster_title) group.cluster_title = r.cluster_title;
    if (!group.issue_fingerprint && r.issue_fingerprint) group.issue_fingerprint = r.issue_fingerprint;
    if (!group.category && r.category) group.category = r.category;
    if (!group.attributed_mod_id && r.attributed_mod_id) group.attributed_mod_id = r.attributed_mod_id;
    if (r.created_at < group.first_seen) group.first_seen = r.created_at;
    if (r.created_at > group.last_seen) {
      group.last_seen = r.created_at;
      group.sample_report_id = r.id;
    }
    if (r.mod_version) group._modVersions.add(r.mod_version);
    if (r.game_version) group._gameVersions.add(r.game_version);
  }

  const items = [...map.values()]
    .map(({_modVersions, _gameVersions, ...rest}) => ({
      ...rest,
      mod_versions: [..._modVersions],
      game_versions: [..._gameVersions],
    }))
    .sort((a, b) => (b.count - a.count) || (b.open_count - a.open_count) || (b.last_seen > a.last_seen ? 1 : -1));

  return respond({items, total: items.length});
}

function validateTriagePatch(patch, allowedFields) {
  if (!patch || typeof patch !== "object" || Array.isArray(patch)) {
    throw new RequestFailure(422, "invalid_update");
  }
  const keys = Object.keys(patch);
  if (keys.length === 0 || keys.some(k => !allowedFields.has(k))) {
    throw new RequestFailure(422, "invalid_update");
  }
  const sets = [];
  const args = [];
  if (keys.includes("status")) {
    if (!STATUSES.has(patch.status)) throw new RequestFailure(422, "invalid_status");
    sets.push("status=?");
    args.push(patch.status);
  }
  if (keys.includes("developer_note")) {
    if (typeof patch.developer_note !== "string" || patch.developer_note.length > 3000) {
      throw new RequestFailure(422, "invalid_note");
    }
    sets.push("developer_note=?");
    args.push(patch.developer_note);
  }
  if (keys.includes("category")) {
    if (typeof patch.category !== "string" || patch.category.length > 120) {
      throw new RequestFailure(422, "invalid_category");
    }
    sets.push("category=?");
    args.push(patch.category);
  }
  if (keys.includes("attributed_mod_id")) {
    if (typeof patch.attributed_mod_id !== "string" || patch.attributed_mod_id.length > 200) {
      throw new RequestFailure(422, "invalid_attributed_mod_id");
    }
    sets.push("attributed_mod_id=?");
    args.push(patch.attributed_mod_id);
  }
  if (keys.includes("fixed_in_version")) {
    if (typeof patch.fixed_in_version !== "string" || patch.fixed_in_version.length > 80) {
      throw new RequestFailure(422, "invalid_fixed_in_version");
    }
    sets.push("fixed_in_version=?");
    args.push(patch.fixed_in_version);
  }
  if (keys.includes("duplicate_of")) {
    if (typeof patch.duplicate_of !== "string" || patch.duplicate_of.length > 120) {
      throw new RequestFailure(422, "invalid_duplicate_of");
    }
    sets.push("duplicate_of=?");
    args.push(patch.duplicate_of);
  }
  if (keys.includes("cluster_key")) {
    if (typeof patch.cluster_key !== "string" || patch.cluster_key.length > 250) {
      throw new RequestFailure(422, "invalid_cluster_key");
    }
    sets.push("cluster_key=?");
    args.push(patch.cluster_key);
  }
  if (keys.includes("cluster_title")) {
    if (typeof patch.cluster_title !== "string" || patch.cluster_title.length > 300) {
      throw new RequestFailure(422, "invalid_cluster_title");
    }
    sets.push("cluster_title=?");
    args.push(patch.cluster_title);
  }
  sets.push("updated_at=?");
  args.push(time());
  return {sets, args};
}

const SINGLE_PATCH_FIELDS = new Set([
  "status", "developer_note", "category", "attributed_mod_id",
  "fixed_in_version", "duplicate_of", "cluster_key", "cluster_title",
]);
const BATCH_PATCH_FIELDS = new Set([
  "status", "developer_note", "category", "attributed_mod_id",
  "fixed_in_version", "duplicate_of",
]);

async function batchUpdateRoute(req, env, authInfo) {
  if (!canMutate(req, authInfo)) return fail(403, "origin_mismatch");
  const {data: body} = await readJson(req, 64000);
  if (!body || typeof body !== "object" || Array.isArray(body)) return fail(422, "invalid_batch");

  const hasIds = Array.isArray(body.ids) && body.ids.length > 0;
  const hasCluster = typeof body.cluster_key === "string" && body.cluster_key.trim().length > 0;
  if (!hasIds && !hasCluster) return fail(422, "invalid_batch_target");

  if (body.ids !== undefined) {
    if (!Array.isArray(body.ids) || body.ids.length === 0 || body.ids.length > 500 || !body.ids.every(x => typeof x === "string" && ID.test(x))) {
      return fail(422, "invalid_ids");
    }
  }

  const {sets, args: setArgs} = validateTriagePatch(body.patch, BATCH_PATCH_FIELDS);
  const filterStatuses = body.filter_status ? parseStatusFilter(String(body.filter_status)) : [];
  if (body.filter_status && body.filter_status !== "all" && filterStatuses.length === 0) {
    return fail(422, "invalid_status");
  }

  const extraWhere = [];
  const extraBinds = [];
  if (hasCluster) {
    extraWhere.push("cluster_key=?");
    extraBinds.push(body.cluster_key.trim());
  }
  if (filterStatuses.length === 1) {
    extraWhere.push("status=?");
    extraBinds.push(filterStatuses[0]);
  } else if (filterStatuses.length > 1) {
    extraWhere.push("status IN (" + filterStatuses.map(() => "?").join(",") + ")");
    extraBinds.push(...filterStatuses);
  }

  let updated = 0;
  if (hasIds) {
    const statements = [];
    for (let i = 0; i < body.ids.length; i += 50) {
      const slice = body.ids.slice(i, i + 50);
      const whereParts = ["id IN (" + slice.map(() => "?").join(",") + ")", ...extraWhere];
      statements.push(
        env.DB.prepare("UPDATE reports SET " + sets.join(",") + " WHERE " + whereParts.join(" AND "))
          .bind(...setArgs, ...slice, ...extraBinds)
      );
    }
    const results = await env.DB.batch(statements);
    for (const r of results) updated += r.meta?.changes || 0;
  } else {
    const res = await env.DB.prepare(
      "UPDATE reports SET " + sets.join(",") + " WHERE " + extraWhere.join(" AND ")
    ).bind(...setArgs, ...extraBinds).run();
    updated = res.meta?.changes || 0;
  }

  return respond({ok: true, updated});
}

async function reclusterRoute(req, env, authInfo) {
  if (!canMutate(req, authInfo)) return fail(403, "origin_mismatch");
  const body = await readOptionalJson(req, 8192);
  const offloadBlobs = Boolean(body.offload_blobs);
  const overwriteManual = Boolean(body.overwrite_manual_attribution);

  const idRows = await env.DB.prepare("SELECT id FROM reports ORDER BY created_at ASC").all();
  const ids = (idRows.results || []).map(r => r.id);
  let scanned = 0;
  let updated = 0;
  let blobs_offloaded = 0;

  for (const reportId of ids) {
    const row = await env.DB.prepare("SELECT * FROM reports WHERE id=?").bind(reportId).first();
    if (!row) continue;
    scanned++;
    let payload;
    try {
      payload = JSON.parse(row.payload_json);
    } catch {
      continue;
    }

    let payloadSha256 = row.payload_sha256 || null;
    if (!payloadSha256) {
      const existingBlobs = await env.DB.prepare("SELECT kind,data_text FROM report_blobs WHERE report_id=?").bind(reportId).all();
      const fullForHash = (existingBlobs.results?.length > 0)
        ? rehydratePayload(payload, existingBlobs.results)
        : payload;
      payloadSha256 = hex(await digest(canonical(fullForHash)));
    }

    const analysis = analyzeReport(payload);
    const nextCategory = (!overwriteManual && row.category) ? row.category : analysis.category;
    const nextAttributedMod = (!overwriteManual && row.attributed_mod_id) ? row.attributed_mod_id : analysis.attributed_mod_id;

    const batchStatements = [];
    let nextPayloadJson = null;

    if (offloadBlobs) {
      const extracted = extractPayloadBlobs(payload, row.payload_json);
      if (extracted.blobs.length > 0) {
        nextPayloadJson = extracted.storedPayloadJson;
        for (const b of extracted.blobs) {
          batchStatements.push(
            env.DB.prepare(
              "INSERT OR REPLACE INTO report_blobs(report_id,kind,format,encoding,sha256,plain_bytes,byte_length,meta_json,data_text,created_at) VALUES (?,?,?,?,?,?,?,?,?,?)"
            ).bind(
              reportId, b.kind, b.format, b.encoding, b.sha256, b.plain_bytes,
              b.byte_length, b.meta_json, b.data_text, row.created_at || time()
            )
          );
          blobs_offloaded++;
        }
      }
    }

    if (nextPayloadJson !== null) {
      batchStatements.push(
        env.DB.prepare(
          `UPDATE reports SET
            payload_json=?,schema_version=?,payload_sha256=?,issue_fingerprint=?,
            cluster_key=?,cluster_title=?,category=?,attributed_mod_id=?,network_type=?,
            has_checkpoint=?,has_screenshot=?,has_replay=?,has_desync=?,runtime_error_count=?
          WHERE id=?`
        ).bind(
          nextPayloadJson, analysis.schema_version, payloadSha256, analysis.issue_fingerprint,
          analysis.cluster_key, analysis.cluster_title, nextCategory, nextAttributedMod, analysis.network_type,
          analysis.has_checkpoint, analysis.has_screenshot, analysis.has_replay,
          analysis.has_desync, analysis.runtime_error_count, reportId
        )
      );
    } else {
      batchStatements.push(
        env.DB.prepare(
          `UPDATE reports SET
            schema_version=?,payload_sha256=?,issue_fingerprint=?,
            cluster_key=?,cluster_title=?,category=?,attributed_mod_id=?,network_type=?,
            has_checkpoint=?,has_screenshot=?,has_replay=?,has_desync=?,runtime_error_count=?
          WHERE id=?`
        ).bind(
          analysis.schema_version, payloadSha256, analysis.issue_fingerprint,
          analysis.cluster_key, analysis.cluster_title, nextCategory, nextAttributedMod, analysis.network_type,
          analysis.has_checkpoint, analysis.has_screenshot, analysis.has_replay,
          analysis.has_desync, analysis.runtime_error_count, reportId
        )
      );
    }

    const res = await env.DB.batch(batchStatements);
    const updateRes = res[res.length - 1];
    if (updateRes.meta?.changes) updated++;
  }

  return respond({ok: true, scanned, updated, blobs_offloaded});
}

async function reportRoute(req, env, id, authInfo) {
  if (!ID.test(id)) return fail(404, "not_found");
  if (req.method === "GET") {
    const url = new URL(req.url);
    const includeBlobs = url.searchParams.get("include_blobs") === "1" || url.searchParams.get("include_blobs") === "true";
    const report = await env.DB.prepare("SELECT * FROM reports WHERE id=?").bind(id).first();
    if (!report) return fail(404, "not_found");
    const {payload_json, ...info} = report;
    let payload;
    try {
      payload = JSON.parse(payload_json);
    } catch {
      payload = null;
    }
    if (payload) {
      if (includeBlobs) {
        const blobs = await env.DB.prepare("SELECT kind,data_text FROM report_blobs WHERE report_id=?").bind(id).all();
        if (blobs.results?.length) payload = rehydratePayload(payload, blobs.results);
      } else {
        payload = omitInlineBlobs(payload);
      }
    }
    return respond({report: {...info, payload}});
  }
  if (req.method === "PATCH") {
    if (!canMutate(req, authInfo)) return fail(403, "origin_mismatch");
    const {data: body} = await readJson(req, 16000);
    const {sets, args} = validateTriagePatch(body, SINGLE_PATCH_FIELDS);
    const result = await env.DB.prepare("UPDATE reports SET " + sets.join(",") + " WHERE id=?").bind(...args, id).run();
    if (!result.meta.changes) return fail(404, "not_found");
    return respond({ok: true});
  }
  if (req.method === "DELETE") {
    if (!canMutate(req, authInfo)) return fail(403, "origin_mismatch");
    const [, reportDel] = await env.DB.batch([
      env.DB.prepare("DELETE FROM report_blobs WHERE report_id=?").bind(id),
      env.DB.prepare("DELETE FROM reports WHERE id=?").bind(id),
    ]);
    return reportDel.meta.changes ? respond({ok: true}) : fail(404, "not_found");
  }
  return fail(405, "method_not_allowed");
}

async function resolveBlobObject(env, id, kind) {
  const [row, blobRow] = await Promise.all([
    env.DB.prepare("SELECT payload_json FROM reports WHERE id=?").bind(id).first(),
    env.DB.prepare("SELECT format,encoding,sha256,plain_bytes,meta_json,data_text FROM report_blobs WHERE report_id=? AND kind=?")
      .bind(id, kind).first(),
  ]);
  if (!row) return {exists: false, entry: null};
  let payload = null;
  try { payload = JSON.parse(row.payload_json); } catch {}
  let entry = payload?.run?.[kind] && typeof payload.run[kind] === "object" ? {...payload.run[kind]} : null;
  if (blobRow && blobRow.data_text) {
    let meta = {};
    try { meta = JSON.parse(blobRow.meta_json || "{}"); } catch {}
    entry = {
      ...meta,
      ...(entry || {}),
      available: true,
      format: entry?.format || blobRow.format,
      encoding: entry?.encoding || blobRow.encoding,
      sha256: entry?.sha256 || blobRow.sha256,
      plain_bytes: entry?.plain_bytes ?? blobRow.plain_bytes,
      data: blobRow.data_text,
    };
    delete entry.blob_offloaded;
  }
  return {exists: true, entry};
}

async function checkpointRoute(env, id) {
  if (!ID.test(id)) return fail(404, "not_found");
  const {exists, entry} = await resolveBlobObject(env, id, "checkpoint");
  if (!exists) return fail(404, "not_found");
  if (!entry?.available) return fail(404, "checkpoint_unavailable");
  try {
    const bytes = await decodeCheckpoint(entry);
    return new Response(bytes, {
      headers: {
        ...HEADERS,
        "content-type": "application/octet-stream",
        "content-disposition": 'attachment; filename="things-checkpoint-' + id + '.save"',
      },
    });
  } catch {
    return fail(422, "invalid_checkpoint");
  }
}

async function screenshotRoute(env, id) {
  if (!ID.test(id)) return fail(404, "not_found");
  const {exists, entry} = await resolveBlobObject(env, id, "screenshot");
  if (!exists) return fail(404, "not_found");
  if (!entry?.available) return fail(404, "screenshot_unavailable");
  try {
    return new Response(await decodeScreenshot(entry), {headers: {...HEADERS, "content-type": "image/jpeg"}});
  } catch {
    return fail(422, "invalid_screenshot");
  }
}

async function replayRoute(env, id) {
  if (!ID.test(id)) return fail(404, "not_found");
  const {exists, entry} = await resolveBlobObject(env, id, "combat_replay");
  if (!exists) return fail(404, "not_found");
  if (!entry?.available) return fail(404, "replay_unavailable");
  try {
    const bytes = await decodeReplay(entry);
    const ext = replayExtension(bytes);
    return new Response(bytes, {
      headers: {
        ...HEADERS,
        "content-type": "application/octet-stream",
        "content-disposition": 'attachment; filename="things-replay-' + id + ext + '"',
      },
    });
  } catch {
    return fail(422, "invalid_replay");
  }
}

async function exportRoute(env, id) {
  if (!ID.test(id)) return fail(404, "not_found");
  const [row, blobs] = await Promise.all([
    env.DB.prepare("SELECT payload_json FROM reports WHERE id=?").bind(id).first(),
    env.DB.prepare("SELECT kind,data_text FROM report_blobs WHERE report_id=?").bind(id).all(),
  ]);
  if (!row) return fail(404, "not_found");
  let bodyText = row.payload_json;
  if (blobs.results && blobs.results.length > 0) {
    try {
      const parsed = JSON.parse(row.payload_json);
      bodyText = JSON.stringify(rehydratePayload(parsed, blobs.results));
    } catch {}
  }
  return new Response(bodyText, {
    headers: {
      ...HEADERS,
      "content-disposition": 'attachment; filename="sts2-report-' + id + '.json"',
    },
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url), path = url.pathname, method = request.method;
    try {
      if (path === "/health" && method === "GET") {
        try {
          await env.DB.prepare(
            "SELECT id,status,developer_note,updated_at,schema_version,payload_sha256,issue_fingerprint,cluster_key,attributed_mod_id,has_checkpoint,has_replay,has_desync FROM reports LIMIT 0"
          ).all();
          await env.DB.prepare("SELECT report_id,kind,data_text FROM report_blobs LIMIT 0").all();
          await env.DB.prepare("SELECT bucket,count,updated_at FROM intake_rate LIMIT 0").all();
          return respond({
            ok: true,
            service: "sts2-things-reports",
            version: "2.0.0",
            database: "ready",
            dashboard: true,
            checkpoint: true,
            screenshot: true,
            replay: true,
            clustering: true,
          });
        } catch {
          return respond({ok: false, service: "sts2-things-reports", version: "2.0.0", database: "unavailable"}, 503);
        }
      }
      if ((path === "/api/reports" || path === "/api/v1/reports") && method === "POST") {
        return await receive(request, env);
      }
      if (path === "/" && method === "GET") return Response.redirect(url.origin + "/admin", 302);
      if (path === "/admin" && method === "GET") return htmlPage();
      if (path === "/api/admin/login" && method === "POST") return await login(request, env);

      if (path.startsWith("/api/admin/")) {
        const authInfo = await checkAdminAuth(request, env);
        if (!authInfo.ok) return fail();

        if (path === "/api/admin/me" && method === "GET") return respond({ok: true, mode: authInfo.mode});
        if (path === "/api/admin/logout" && method === "POST") {
          if (!canMutate(request, authInfo)) return fail(403, "origin_mismatch");
          return respond({ok: true}, 200, {"set-cookie": cookie("spire_admin_session", "", 0)});
        }
        if (path === "/api/admin/stats" && method === "GET") return stats(env);
        if (path === "/api/admin/reports" && method === "GET") return list(request, env);
        if (path === "/api/admin/clusters" && method === "GET") return clustersRoute(request, env);
        if (path === "/api/admin/reports/batch-update" && method === "POST") return await batchUpdateRoute(request, env, authInfo);
        if (path === "/api/admin/recluster" && method === "POST") return await reclusterRoute(request, env, authInfo);

        const checkpoint = path.match(/^\/api\/admin\/reports\/([a-f0-9-]+)\/checkpoint$/i);
        if (checkpoint && method === "GET") return checkpointRoute(env, checkpoint[1]);
        const screenshot = path.match(/^\/api\/admin\/reports\/([a-f0-9-]+)\/screenshot$/i);
        if (screenshot && method === "GET") return screenshotRoute(env, screenshot[1]);
        const replay = path.match(/^\/api\/admin\/reports\/([a-f0-9-]+)\/replay$/i);
        if (replay && method === "GET") return replayRoute(env, replay[1]);
        const exported = path.match(/^\/api\/admin\/reports\/([a-f0-9-]+)\/export$/i);
        if (exported && method === "GET") return exportRoute(env, exported[1]);

        const m = path.match(/^\/api\/admin\/reports\/([a-f0-9-]+)$/i);
        if (m) return await reportRoute(request, env, m[1], authInfo);
      }
      return fail(404, "not_found");
    } catch (err) {
      if (err instanceof RequestFailure) return respond({error: err.message}, err.status);
      console.error("worker error", err?.message);
      return respond({error: "server_error"}, 500);
    }
  },
};
