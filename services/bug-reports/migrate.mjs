import fs from "node:fs";
import path from "node:path";
import {spawnSync} from "node:child_process";
import {fileURLToPath} from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));

export const REPORT_COLUMN_MIGRATIONS = [
  ["status", "ALTER TABLE reports ADD COLUMN status TEXT NOT NULL DEFAULT 'new'"],
  ["developer_note", "ALTER TABLE reports ADD COLUMN developer_note TEXT NOT NULL DEFAULT ''"],
  ["updated_at", "ALTER TABLE reports ADD COLUMN updated_at TEXT"],
  ["schema_version", "ALTER TABLE reports ADD COLUMN schema_version INTEGER DEFAULT 1"],
  ["payload_sha256", "ALTER TABLE reports ADD COLUMN payload_sha256 TEXT"],
  ["issue_fingerprint", "ALTER TABLE reports ADD COLUMN issue_fingerprint TEXT"],
  ["cluster_key", "ALTER TABLE reports ADD COLUMN cluster_key TEXT"],
  ["cluster_title", "ALTER TABLE reports ADD COLUMN cluster_title TEXT"],
  ["category", "ALTER TABLE reports ADD COLUMN category TEXT DEFAULT ''"],
  ["attributed_mod_id", "ALTER TABLE reports ADD COLUMN attributed_mod_id TEXT DEFAULT ''"],
  ["fixed_in_version", "ALTER TABLE reports ADD COLUMN fixed_in_version TEXT DEFAULT ''"],
  ["duplicate_of", "ALTER TABLE reports ADD COLUMN duplicate_of TEXT DEFAULT ''"],
  ["network_type", "ALTER TABLE reports ADD COLUMN network_type TEXT DEFAULT ''"],
  ["has_checkpoint", "ALTER TABLE reports ADD COLUMN has_checkpoint INTEGER NOT NULL DEFAULT 0"],
  ["has_screenshot", "ALTER TABLE reports ADD COLUMN has_screenshot INTEGER NOT NULL DEFAULT 0"],
  ["has_replay", "ALTER TABLE reports ADD COLUMN has_replay INTEGER NOT NULL DEFAULT 0"],
  ["has_desync", "ALTER TABLE reports ADD COLUMN has_desync INTEGER NOT NULL DEFAULT 0"],
  ["runtime_error_count", "ALTER TABLE reports ADD COLUMN runtime_error_count INTEGER NOT NULL DEFAULT 0"],
];

export const POST_COLUMN_DDL = [
  `CREATE TABLE IF NOT EXISTS report_blobs (
    report_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    format TEXT NOT NULL,
    encoding TEXT NOT NULL,
    sha256 TEXT,
    plain_bytes INTEGER,
    byte_length INTEGER NOT NULL,
    meta_json TEXT NOT NULL DEFAULT '{}',
    data_text TEXT NOT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY (report_id, kind)
  )`,
  "CREATE INDEX IF NOT EXISTS idx_report_blobs_report ON report_blobs(report_id)",
  "CREATE TABLE IF NOT EXISTS intake_rate (bucket TEXT PRIMARY KEY, count INTEGER NOT NULL, updated_at TEXT NOT NULL)",
  "CREATE INDEX IF NOT EXISTS idx_intake_rate_date ON intake_rate(updated_at)",
  "CREATE INDEX IF NOT EXISTS idx_reports_date ON reports(created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_reports_version ON reports(game_version,mod_version)",
  "CREATE INDEX IF NOT EXISTS idx_reports_status ON reports(status,created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_reports_cluster ON reports(cluster_key, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_reports_attributed_mod ON reports(attributed_mod_id, created_at DESC)",
  "CREATE INDEX IF NOT EXISTS idx_reports_fingerprint ON reports(issue_fingerprint)",
];

export function missingColumns(existing) {
  const columns = new Set(existing);
  return REPORT_COLUMN_MIGRATIONS
    .filter(([name]) => !columns.has(name))
    .map(([, statement]) => statement);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const remote = process.argv.includes("--remote");
  if (!remote && !process.argv.includes("--local")) throw new Error("Specify --local or --remote explicitly");
  const script = process.env.WRANGLER_JS || (process.env.APPDATA
    ? path.join(process.env.APPDATA, "npm/node_modules/wrangler/bin/wrangler.js") : null);
  if (!script || !fs.existsSync(script)) throw new Error("Set WRANGLER_JS to the installed Wrangler entrypoint");
  function query(sql) {
    const result = spawnSync(process.execPath, [script, "d1", "execute", "sts2-things-bug-reports",
      remote ? "--remote" : "--local", "--json", "--command", sql], {cwd: root, encoding: "utf8", timeout: 60000});
    if (result.status !== 0) throw new Error("D1 migration command failed: " + result.stderr);
    return JSON.parse(result.stdout);
  }
  // 1. Ensure base reports table exists (no-op on live production DB with existing rows).
  const schema = fs.readFileSync(path.join(root, "schema.sql"), "utf8");
  const createReportsTable = schema.slice(0, schema.indexOf("CREATE INDEX"));
  query(createReportsTable);
  // 2. Inspect existing columns and apply only missing ALTER TABLE ADD COLUMN statements.
  const info = query("PRAGMA table_info(reports)");
  const columns = info.flatMap(r => r.results || []).map(r => r.name);
  const added = missingColumns(columns);
  for (const sql of added) query(sql);
  // 3. Create report_blobs, intake_rate, and all v1 + v2 indexes idempotently.
  for (const sql of POST_COLUMN_DDL) query(sql);
  const finalInfo = query("PRAGMA table_info(reports)");
  const finalColumns = finalInfo.flatMap(r => r.results || []).map(r => r.name);
  console.log(JSON.stringify({
    ok: true,
    target: remote ? "remote" : "local",
    added_columns: added.length,
    total_columns: finalColumns.length,
    reentrant: missingColumns(finalColumns).length === 0,
  }));
}
