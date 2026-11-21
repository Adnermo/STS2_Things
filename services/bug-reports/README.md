## 管理后台与自动分拣系统 (v2.0.0)

地址：**https://reports.adnermo.online/admin**。提供上报总量与近 7 日趋势、问题自动聚类（Issue Clusters）、外部模组冲突自动归因、联机不同步（Desync）分析、多选与整组批量分拣（Batch Triage）、完整本局快照、章节路径、战斗事件、原版战前还原存档下载、战斗回放（Combat Replay）下载、现场截图及自包含 JSON 导出。

### 管理员登录与 Headless API 鉴权

浏览器初始口令保存在本机当前 Windows 用户私有目录（**严禁写入 git、Steam 更新日志或公开工单**）：

`%LOCALAPPDATA%\STS2_Things\private\reports-admin-password.txt`

远端通过加密 Cloudflare Worker Secrets 配置：
- `ADMIN_PASSWORD`（必需）：后台网页登录口令（SHA-256 常量时间比对）。
- `SESSION_SECRET`（必需）：12 小时 `spire_admin_session` HMAC-SHA256 Cookie 签名密钥；Cookie 会话的写操作（`POST` / `PATCH` / `DELETE`）强制校验同源 `Origin`。
- `ADMIN_API_TOKEN`（可选，推荐用于 CLI / STS2-MCP 自动化分拣）：配置后支持 `Authorization: Bearer <ADMIN_API_TOKEN>` 访问全部 `/api/admin/*` 接口，常量时间比对且免除浏览器同源 `Origin` 限制。未配置时不影响 Worker 部署与网页登录。

```powershell
# 配置可选的 CLI / MCP Bearer 密钥（本地生成高熵随机值后粘贴）
wrangler secret put ADMIN_API_TOKEN
```

### 源码结构与非破坏式升级部署

- `worker.js`：v2.0.0 公开上报路由（`/api/reports` 与 `/api/v1/reports`）、大附件自动分流与按需回填、Cookie + Bearer 双模鉴权及管理后台 API。
- `validation.js`：向后兼容 Schema `1..4`，最大请求体 `1.5 MB`（`1,500,000` 字节），严格校验嵌套深度、节点数与 Base64 附件边界。
- `fingerprint.js`：确定性异常提取、调用栈包装帧过滤、Harmony 补丁归属、外部模组识别（如 `CardArtEditor.PortraitPatch`、`BetterCallSpire`、`MomoLib`、`BaseLib`、`RitsuLib` 等）与问题指纹生成。
- `checkpoint.js` / `screenshot.js` / `replay.js`：战前存档（Schema `1..2`，解压上限 `4 MB`）、截图（上限 `1920x2160` / `250 KB`）与战斗回放（`sts2-combat-replay`，解压上限 `2 MB`）GZip 解压与 SHA-256 完整性核验。
- `dashboard.html`：单文件管理工作台 v2（严格 CSP：`default-src 'none'`，内联样式与脚本，无外部 CDN）。
- `schema.sql`：包含 `reports`（含 v2 分拣/聚类列）、`report_blobs`（大附件独立表）和 `intake_rate` 的完整库表与索引。
- `migrations/0002_v2_triage_and_blobs.sql`：v1 升 v2 增量列、索引与 `report_blobs` 建表 SQL 参考。
- `migrate.mjs`：幂等迁移脚本，自动执行 `PRAGMA table_info(reports)` 比对缺失列后逐列 `ALTER TABLE`，对现有生产数据零破坏、可重复运行。

生产升级与发布步骤：

```powershell
cd D:\Things\Things-Workspace\STS2_Things\services\bug-reports
# 1. 先执行非破坏式 D1 结构升级（保留全部既有生产报告）
node migrate.mjs --remote
# 2. 部署 Worker v2.0.0
wrangler deploy
# 3. 登录后在后台点击「⚡ 重新聚类全部」或用 Bearer Token 触发历史报告回填与附件分流：
# POST https://reports.adnermo.online/api/admin/recluster  {"offload_blobs": true, "overwrite_manual_attribution": false}
```

### API 速览

- **公开接口**
  - `GET /health`：检查 `reports`、`report_blobs`、`intake_rate` 就绪状态，返回 `version: "2.0.0"`。
  - `POST /api/reports`、`POST /api/v1/reports`：接收 Schema `1..4` 上报（上限 `1.5 MB`）。入库前按完整规范化 JSON 计算 `payload_sha256` 保障幂等重传（`201 duplicate: true` / 冲突 `409`），自动将 `checkpoint`、`screenshot`、`combat_replay` 的 Base64 数据分流至 `report_blobs`。
- **管理接口（需 Cookie 会话或 `Authorization: Bearer <ADMIN_API_TOKEN>`）**
  - `GET /api/admin/me`：返回 `{ ok: true, mode: "bearer" | "cookie" }`。
  - `GET /api/admin/stats`：返回 `total`、`open_count`、`new`、`fixed_in_version`、`external_mod`、`resolved_total`、`desync_count`、`week`、`by_status`、`top_clusters`（Top 10）与 7 日 `trend`。
  - `GET /api/admin/reports`：支持 `q`、`status`（单个、逗号分隔或 `open`）、`cluster_key`、`attributed_mod_id`（具体模组 ID 或 `__external__`）、`category`、`mod_version`、`game_version`、`has_desync`（`1`/`0`）、`has_checkpoint`（`1`/`0`）、`sort`（`newest`/`oldest`）、`limit`（`1..200`）、`page`。
  - `GET /api/admin/clusters`：按 `cluster_key` 聚合报告计数、待处理数、状态分布、最新样本 ID、首末次出现时间及涉及的游戏/模组版本。
  - `GET /api/admin/reports/:id`：返回报告元数据与轻量 `payload`（默认省略大 Base64，传 `?include_blobs=1` 时从 `report_blobs` 自动回填）。
  - `PATCH /api/admin/reports/:id`：更新 `{ status, developer_note, category, attributed_mod_id, fixed_in_version, duplicate_of, cluster_key, cluster_title }` 的任意非空子集。
  - `POST /api/admin/reports/batch-update`：按 `ids` 或 `cluster_key`（可配合 `filter_status`）批量更新分拣字段。
  - `POST /api/admin/recluster`：遍历历史报告重算 `analyzeReport`，支持 `offload_blobs` 与 `overwrite_manual_attribution`。
  - `GET /api/admin/reports/:id/checkpoint` | `/screenshot` | `/replay` | `/export`：优先读取 `report_blobs` 并兼容未迁移的内联旧行；`/export` 自动回填分流附件，确保导出的 `.json` 100% 自包含。
  - `DELETE /api/admin/reports/:id`：原子删除报告主行及关联的 `report_blobs` 记录。

状态枚举：`new` | `triaged` | `investigating` | `fixed_in_version` | `external_mod` | `duplicate` | `not_a_bug` | `needs_info` | `wontfix` | `resolved`。

### 本地验证

```powershell
# 单元与本地 Miniflare D1 全链路集成测试（覆盖 Schema 1..4、1MB+ 分流、幂等、Bearer/CSRF、聚类归因、v1->v2 迁移与回填）
node --test tests/worker.test.mjs

# 本地迁移脚本幂等验证
node migrate.mjs --local
```
