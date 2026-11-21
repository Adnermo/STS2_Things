ALTER TABLE reports ADD COLUMN schema_version INTEGER DEFAULT 1;
ALTER TABLE reports ADD COLUMN payload_sha256 TEXT;
ALTER TABLE reports ADD COLUMN issue_fingerprint TEXT;
ALTER TABLE reports ADD COLUMN cluster_key TEXT;
ALTER TABLE reports ADD COLUMN cluster_title TEXT;
ALTER TABLE reports ADD COLUMN category TEXT DEFAULT '';
ALTER TABLE reports ADD COLUMN attributed_mod_id TEXT DEFAULT '';
ALTER TABLE reports ADD COLUMN fixed_in_version TEXT DEFAULT '';
ALTER TABLE reports ADD COLUMN duplicate_of TEXT DEFAULT '';
ALTER TABLE reports ADD COLUMN network_type TEXT DEFAULT '';
ALTER TABLE reports ADD COLUMN has_checkpoint INTEGER NOT NULL DEFAULT 0;
ALTER TABLE reports ADD COLUMN has_screenshot INTEGER NOT NULL DEFAULT 0;
ALTER TABLE reports ADD COLUMN has_replay INTEGER NOT NULL DEFAULT 0;
ALTER TABLE reports ADD COLUMN has_desync INTEGER NOT NULL DEFAULT 0;
ALTER TABLE reports ADD COLUMN runtime_error_count INTEGER NOT NULL DEFAULT 0;
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
