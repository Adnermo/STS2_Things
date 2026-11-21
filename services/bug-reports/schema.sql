CREATE TABLE IF NOT EXISTS reports (
  id TEXT PRIMARY KEY,
  client_report_id TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL,
  source TEXT NOT NULL,
  description TEXT NOT NULL,
  game_version TEXT,
  mod_version TEXT,
  seed TEXT,
  ascension INTEGER,
  act_index INTEGER,
  payload_json TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'new',
  developer_note TEXT NOT NULL DEFAULT '',
  updated_at TEXT,
  schema_version INTEGER DEFAULT 1,
  payload_sha256 TEXT,
  issue_fingerprint TEXT,
  cluster_key TEXT,
  cluster_title TEXT,
  category TEXT DEFAULT '',
  attributed_mod_id TEXT DEFAULT '',
  fixed_in_version TEXT DEFAULT '',
  duplicate_of TEXT DEFAULT '',
  network_type TEXT DEFAULT '',
  has_checkpoint INTEGER NOT NULL DEFAULT 0,
  has_screenshot INTEGER NOT NULL DEFAULT 0,
  has_replay INTEGER NOT NULL DEFAULT 0,
  has_desync INTEGER NOT NULL DEFAULT 0,
  runtime_error_count INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_reports_date ON reports(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_reports_version ON reports(game_version,mod_version);
CREATE INDEX IF NOT EXISTS idx_reports_status ON reports(status,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_reports_cluster ON reports(cluster_key, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_reports_attributed_mod ON reports(attributed_mod_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_reports_fingerprint ON reports(issue_fingerprint);
CREATE TABLE IF NOT EXISTS report_blobs (
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
);
CREATE INDEX IF NOT EXISTS idx_report_blobs_report ON report_blobs(report_id);
CREATE TABLE IF NOT EXISTS intake_rate (bucket TEXT PRIMARY KEY, count INTEGER NOT NULL, updated_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_intake_rate_date ON intake_rate(updated_at);
