# 尖塔：琐事 2.0.0 / STS2 Things 2.0.0

- **双版本统一包更新为 v0.111.1（正式版）+ v0.113.1（public-beta）**：退役旧版 v0.107.1 目标；四文件统一包（`STS2_Things.json`、`STS2_Things.dll`、`STS2_Things.pck`、`STS2_Things.BaseLibBridge.dll`）运行时按客户端真实 API 自动切换实现。
- **全面适配 v0.113.1 新模式（恐惧模式 / 急速模式）**：
  - **恐惧模式（Dread）**：全部 8 位模组 Boss 均配有专属 `Dominion` 章节支配效果、恐惧难度战斗数值强化与完整中英文案。
  - **急速模式（Blitz）**：急速商店附魔池接入「消散」「过载」「配重」，新增「琐事遗物」专属货架（6 件独立正向模组遗物池）；深处多段配乐按第 4 层切段，横路与商人猜拳在急速模式下自动适配，F2 诊断补充记录模式与支配信息。
- **v2.0.0 命名空间统一与旧档无损迁移**：全部 213 个模组模型统一迁至 `THINGS_*` 命名空间，避免与其他模组冲突；内置 `LegacyIdMigrator`（136 条迁移规则）自动承接 `1.x` 进度、进行中存档与历史记录。
- **第二章「深处」大型扩充与遭遇池正规化**：
  - 新增两位深处专属 Boss：**人面塔**（四面轮转合击，专属配乐）与**渊墨巨蛸**（水域古桥巨物战、多触腕协作、高压渊墨覆牌，专属着色器与配乐），全部 Boss 增至 8 位并配齐独立 Boss 战曲与深处多段无缝配乐。
  - 新增普通怪物**刮甲石蚓**与**异彩史莱姆**（七种分裂形态）；遭遇池正规化为 **6 组纯种弱敌 + 11 组强敌（纯种与互补双族组合）+ 3 组深处专属自制精英**，不再混入原版巢穴精英；同步重做活体巨岩与盛碗虫族母机制与动画表现。
- **扩展至 38 个跨章节事件与 5 个多人同步小游戏**：新增 23 个覆盖一至三章的事件，包含「古藤刻痕盘」「古林落籽盘」「暗渠货栅」「蚌口夹珍」「铜鼎争宝」5 个带独立交互界面与原生联机同步的小游戏事件，以及「过载接线亭」「记仇的贩卖机」「退货柜台」「发烫的蜡封匣」「发条调速仪」「镀银试衣镜」「虚荣天平」「墙缝账房」「谁去推门」等策略事件；新增 5 件遗物与 2 种附魔（过载、配重）。
- **高清美术重制与原版风味文案对齐**：重绘高清 `3440×1616` 事件插画、`1000×760` 清晰卡面与原生视口适配战斗背景；全量对齐中英文卡牌、能力、遗物、附魔、遭遇与事件文案至原版 v0.113.1 规范；配置系统升级 v3（BaseLib / RitsuLib 可选设置页覆盖全部 8 位 Boss 与 38 个事件），F2 诊断升级 Schema v4。

---

- **Dual-version unified package updated to v0.111.1 (Main) + v0.113.1 (public-beta)**: Retires the legacy v0.107.1 target. All players share the same 4-file unified package (`STS2_Things.json`, `STS2_Things.dll`, `STS2_Things.pck`, `STS2_Things.BaseLibBridge.dll`), which selects the matching implementation at runtime.
- **Full v0.113.1 Dread & Blitz mode support**:
  - **Dread Mode**: All 8 mod bosses now feature dedicated act-wide `Dominion` effects, Dread combat scaling, and localized tooltips.
  - **Blitz Mode**: Adds mod enchantments (`Disperse`, `Overclocked`, `Weighted`) to the Blitz shop pool plus a `Trivia Relic` merchant slot (6 standalone positive mod relics), adapts Depths music progression to Floor 4, cleanly bypasses Crossroads and Merchant Bargain in Blitz runs, and records Dread/Blitz metadata in F2 diagnostics.
- **v2.0.0 `THINGS_*` namespace migration & save compatibility**: Migrates all 213 mod models into the `THINGS_*` namespace to prevent cross-mod ID collisions; built-in `LegacyIdMigrator` (136 mappings) transparently migrates `1.x` progress, active runs, and run history.
- **Act II "Depths" expansion & encounter regularization**:
  - Adds two Depths-exclusive bosses — **The Visage Tower** (four rotating stone visages with combo attacks) and **Abyssal Inkmaw** (monumental subterranean bridge encounter with multi-tentacle coordination and ink-blackout beam) — bringing the roster to **8 bosses**, all with dedicated boss BGM plus a multi-stage adaptive Depths music suite.
  - Adds **Scale-Peeler Wyrm** and **Chromatic Slime** (7 split variants); regularizes Depths into **6 pure weak encounters, 11 regular encounters (pure & complementary pairings), and 3 exclusive custom Depths elites** (no vanilla Hive elites leaking into Depths); refines Living Megalith and Bowlbug Progenitor mechanics and rigs.
- **38 events across Acts I–III, including 5 multiplayer-synced minigames**: Adds 23 new events spanning Overgrowth, Underdocks, Hive, Depths, and Glory, highlighted by 5 interactive custom-UI minigame events (`Overgrown Rune Loom`, `Overgrown Plinko`, `Bilge Grate`, `Clam Claw Crane`, `Bronze Tripod Scramble`) alongside tactical events (`The Splice Booth`, `Grudge Vending Machine`, `Returns Counter`, `Hot Wax Box`, `Chronometer Rig`, `Silvered Gallery`, `Scales of Vanity`, `The Wall-Crack Ledger`, `The Spiked Door`, and more), plus 5 new relics and 2 new enchantments (`Overclocked`, `Weighted`).
- **High-res visual refresh & vanilla localization alignment**: Ships crisp `3440×1616` event art, `1000×760` card portraits, and viewport-fitted combat backgrounds; aligns bilingual (`zhs`/`eng`) card, power, relic, enchantment, encounter, and event copy to vanilla v0.113.1 conventions; upgrades Config to v3 (BaseLib/RitsuLib optional UI covering all 8 bosses and 38 events) and F2 Bug Reporter to Schema v4.
