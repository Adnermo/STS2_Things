// Backward-compatible report validation for Schema 1, 2, 3, and 4.
const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value, max = 300) => typeof value === "string" && value.length <= max;
const optional = (parent, key, check) => parent[key] === undefined || parent[key] === null || check(parent[key]);
const list = (value, max, check) => Array.isArray(value) && value.length <= max && value.every(check);
const id = value => text(value, 300);
const hex64 = value => typeof value === "string" && /^[a-f0-9]{64}$/i.test(value);
const base64Text = (value, max) => text(value, max) && value.length > 0 && /^[A-Za-z0-9+/]+={0,2}$/.test(value);
const BLOB_FORMATS = new Set(["sts2-native-save", "things-screenshot", "sts2-combat-replay"]);

const card = value => typeof value === "string" ? id(value) : object(value) && id(value.id)
  && optional(value, "energy", Number.isFinite) && optional(value, "upgrade", Number.isInteger);
const power = value => typeof value === "string" ? id(value) : object(value) && id(value.id)
  && optional(value, "amount", Number.isFinite);
const choice = value => object(value) && optional(value, "id", id) && optional(value, "key", id)
  && optional(value, "table", id) && optional(value, "picked", v => typeof v === "boolean");

function player(value) {
  return object(value) && optional(value, "character", id) && optional(value, "deck", v => list(v, 1500, card))
    && optional(value, "relics", v => list(v, 500, id)) && optional(value, "potions", v => list(v, 100, x => x === null || id(x)))
    && optional(value, "powers", v => list(v, 500, power)) && optional(value, "combat", combat);
}
function combat(value) {
  return object(value) && ["hand", "draw_pile", "discard_pile", "exhaust_pile", "play_pile"].every(key => optional(value, key, v => list(v, 1500, card)))
    && ["energy", "stars", "turn"].every(key => optional(value, key, Number.isFinite)) && optional(value, "phase", v => text(v, 80));
}
function routePlayer(value) {
  return object(value) && ["event_choices", "card_choices", "relic_choices", "potion_choices"].every(key => optional(value, key, v => list(v, 500, choice)))
    && ["rest_choices", "cards_gained", "cards_removed"].every(key => optional(value, key, v => list(v, 1500, id)));
}
function route(value) {
  return list(value, 20, act => object(act) && Number.isInteger(act.act_index) && list(act.points, 500, point => object(point)
    && optional(point, "rooms", v => list(v, 50, room => object(room) && optional(room, "type", id)
      && optional(room, "model_id", id) && optional(room, "monster_ids", v => list(v, 100, id))))
    && optional(point, "players", v => list(v, 8, routePlayer))));
}
function snapshot(value) {
  return object(value) && optional(value, "seed", v => text(v, 100))
    && optional(value, "acts", v => list(v, 20, id)) && optional(value, "players", v => list(v, 8, player))
    && optional(value, "route", route) && optional(value, "combat", v => object(v) && optional(v, "monsters", m => list(m, 100,
      monster => object(monster) && id(monster.id) && optional(monster, "powers", p => list(p, 500, power)))))
    && optional(value, "ui", v => object(v) && optional(v, "holders", h => list(h, 1500, object)));
}

function validAssembly(value) {
  return object(value) && text(value.name, 300)
    && optional(value, "version", v => text(v, 120))
    && optional(value, "mvid", v => text(v, 100))
    && optional(value, "sha256", v => text(v, 128))
    && optional(value, "mod_id", v => text(v, 200))
    && optional(value, "is_dynamic", v => typeof v === "boolean");
}

function validHarmonyPatch(value) {
  return object(value)
    && optional(value, "method", v => text(v, 600))
    && optional(value, "target", v => text(v, 600))
    && optional(value, "declaring_type", v => text(v, 400))
    && optional(value, "method_name", v => text(v, 200))
    && optional(value, "owners", v => list(v, 100, id));
}

function validDesync(value) {
  return object(value) && typeof value.detected === "boolean"
    && optional(value, "at", v => text(v, 80))
    && optional(value, "state_id", v => Number.isFinite(v) || text(v, 120))
    && optional(value, "local_checksum", v => Number.isFinite(v) || text(v, 160))
    && optional(value, "remote_checksum", v => Number.isFinite(v) || text(v, 160))
    && optional(value, "model_cache_hash", v => Number.isFinite(v) || text(v, 160))
    && optional(value, "summary", v => text(v, 4000))
    && optional(value, "field_diffs", v => list(v, 500, d => text(d, 2000) || object(d)));
}

function validRuntimeFault(value) {
  return object(value)
    && optional(value, "signature", v => text(v, 500))
    && optional(value, "exception_type", v => text(v, 300))
    && optional(value, "message", v => text(v, 4000))
    && optional(value, "top_frame", v => text(v, 600))
    && optional(value, "harmony_owners", v => list(v, 100, id))
    && optional(value, "count", v => Number.isInteger(v) && v >= 1)
    && optional(value, "first_at", v => text(v, 80))
    && optional(value, "last_at", v => text(v, 80))
    && optional(value, "session", v => text(v, 80))
    && optional(value, "inner_exceptions", v => list(v, 30, ie => text(ie, 4000) || object(ie)))
    && optional(value, "stack", v => text(v, 16000) || list(v, 300, line => text(line, 1000)));
}

function boundedTree(value, depth = 0, counter = {count: 0}) {
  if (++counter.count > 60000 || depth > 20) return false;
  if (value === null || typeof value === "boolean") return true;
  if (typeof value === "number") return Number.isFinite(value);
  if (typeof value === "string") return value.length <= 16000;
  if (Array.isArray(value)) return value.length <= 1500 && value.every(v => boundedTree(v, depth + 1, counter));
  return object(value) && Object.keys(value).length <= 128 && Object.entries(value).every(([key, v]) =>
    key.length <= 150 && !["__proto__", "constructor", "prototype"].includes(key)
    && (key === "data" && BLOB_FORMATS.has(value.format)
      ? (v === null || text(v, 450000))
      : boundedTree(v, depth + 1, counter)));
}

export function validScreenshot(value) {
  if (!object(value) || typeof value.available !== "boolean") return false;
  if (!value.available) return optional(value, "reason", v => text(v, 200));
  return value.format === "things-screenshot" && value.encoding === "jpeg-base64"
    && hex64(value.sha256)
    && Number.isInteger(value.bytes) && value.bytes > 0 && value.bytes <= 250000
    && Number.isInteger(value.width) && value.width > 0 && value.width <= 1920
    && Number.isInteger(value.height) && value.height > 0 && value.height <= 2160
    && base64Text(value.data, 350000);
}

export function validCheckpoint(value) {
  if (!object(value) || typeof value.available !== "boolean") return false;
  if (!value.available) return optional(value, "reason", v => text(v, 200));
  const validPlayers = value.schema === 1
    ? (Number.isInteger(value.player_count) && value.player_count >= 1 && value.player_count <= 8)
    : optional(value, "player_count", v => Number.isInteger(v) && v >= 1 && v <= 8);
  return value.format === "sts2-native-save" && value.encoding === "gzip-base64"
    && [1, 2].includes(value.schema)
    && hex64(value.sha256)
    && Number.isInteger(value.plain_bytes) && value.plain_bytes > 0 && value.plain_bytes <= 4000000
    && base64Text(value.data, 450000)
    && validPlayers
    && optional(value, "entry_kind", v => text(v, 120))
    && optional(value, "room_type", v => text(v, 120))
    && optional(value, "encounter_id", v => text(v, 200))
    && optional(value, "event_id", v => text(v, 200));
}

export function validCombatReplay(value) {
  if (!object(value) || typeof value.available !== "boolean") return false;
  if (!value.available) return optional(value, "reason", v => text(v, 200));
  return value.format === "sts2-combat-replay" && value.encoding === "gzip-base64"
    && hex64(value.sha256)
    && Number.isInteger(value.plain_bytes) && value.plain_bytes > 0 && value.plain_bytes <= 2000000
    && base64Text(value.data, 350000);
}

export function validateReport(p) {
  return object(p) && [1, 2, 3, 4].includes(p.schema) && p.source === "sts2_things"
    && /^[0-9a-f]{32}$/i.test(p.client_report_id ?? "") && text(p.description, 4000) && p.description.trim().length >= 2
    && object(p.run) && [1, 2, 3, 4].includes(p.run.schema) && text(p.run.game_version ?? "unknown", 80)
    && text(p.run.mod_version ?? "unknown", 80)
    && list(p.run.events, 1000, event => object(event) && text(event.kind, 100)
      && optional(event, "at", v => text(v, 80)) && optional(event, "detail", v => text(v, 4000) || object(v)))
    && optional(p.run, "active_mods", v => list(v, 300, m => object(m) && id(m.id) && text(m.version ?? "unknown", 80)))
    && optional(p.run, "state", snapshot) && optional(p.run, "initial_state", snapshot)
    && optional(p.run, "checkpoint", validCheckpoint)
    && optional(p.run, "screenshot", validScreenshot)
    && optional(p.run, "combat_replay", validCombatReplay)
    && optional(p.run, "assemblies", v => list(v, 500, validAssembly))
    && optional(p.run, "harmony_patches", v => list(v, 600, validHarmonyPatch))
    && optional(p.run, "desync", validDesync)
    && optional(p.run, "runtime_faults", v => list(v, 200, validRuntimeFault))
    && optional(p.run, "modifiers", v => object(v) || list(v, 200, m => id(m) || object(m)))
    && optional(p.run, "config", object)
    && optional(p.run, "network", object)
    && boundedTree(p);
}

export function canonical(value) {
  if (Array.isArray(value)) return "[" + value.map(canonical).join(",") + "]";
  if (object(value)) return "{" + Object.keys(value).sort().map(key => JSON.stringify(key) + ":" + canonical(value[key])).join(",") + "}";
  return JSON.stringify(value);
}
