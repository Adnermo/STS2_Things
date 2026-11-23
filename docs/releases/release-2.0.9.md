# 尖塔：琐事 2.0.9 / STS2 Things 2.0.9

- **腐化之遗（The Legacy）血量与输出压迫感下调**：
  - 基础血量由 `300/320`（恐惧 `+40`）下调至 **`240/260`（恐惧 `+25`）**；开局仍保留固定 `2` 层「死亡律动」与 `HP/5` 硬化外壳（`48/52`，恐惧 `53/57`）。
  - 「衰竭」易伤固定为 **`1` 层**（不再覆盖第 3 回合「血弹」），脆弱维持 `1/2` 层，洗入弃牌堆的状态牌由 6 张缩减为 **`3` 张**（灼伤 / 晕眩 / 黏液各 1 张）。
  - 「回音」伤害下调至 **`13/15`（恐惧 `+2`）**；「血弹」单段伤害由 `3` 降至 **`2`**（`2×4 / 2×5`）；「强化」每次力量成长由 `2`（恐惧 `3`）降至 **`1`（恐惧 `2`）**，避免多层叠加后每回合动辄 20–30+ 伤害。
- **渊墨巨蛸（Abyssal Inkmaw）动画流畅度重制、锁血 Buff 与触手调优**：
  - **动画与性能重制**：重建全部 5 套渊墨巨蛸 Spine 骨架并升级 240Hz 贝塞尔曲线过采样优化（峰值网格三角形由 `31,386` 降至 `10,295`，CPU 裁剪开销降低 **88%**），修复背景循环队列卡顿、高频震颤抽搐与喷墨边缘首帧闪烁，精简全屏墨浸着色器并补齐墨浊覆牌洗净动画。
  - **新增半血锁血 Buff「深渊锁潮」**：开局即以带图标的 Buff 明示 50% 锁血阈值，触发后切换锁定态文案与 `∞` 血条显示；进入二阶段「深渊狂潮」时无视常规 2 触手上限与冷却，**直接补满全部 4 条触手**（2 条破板主触手 + 2 条外侧小触手）。
  - **触手平衡**：破击触手血量下调至 `38/42`（恐惧 `+6`）并降低自身格挡（`4/6`），墨涌触手下调至 `36/40`（恐惧 `+6`），小触手下调至 `20/24`（恐惧 `+4`），断腕反噬按队伍人数平滑缩放（`20 / 48 / 72 / 96`）。
- **活体巨岩（Cave God）半血锁血 Buff 与动画打磨**：
  - 新增半血锁血 Buff **「熔核锁印」**（专属图标 + 首回合明示 50% 锁血阈值 + 锁血态说明），与渊墨巨蛸统一阶段信息展示；同步清理受击反应轨道残留并打磨待机微动。
- **始源雾菇（Origin Fogmog）与盛碗虫族母（Bowlbug Progenitor）体验修复**：
  - **始源雾菇 / 始源利齿之眼**：利齿之眼「滋养」改为仅赋予始源雾菇本体力量，杜绝同回合内前序召唤物给后序攻击眼加力导致意图伤害失真；下调本体血量至 `220/230`（恐惧 `+20`）、利齿之眼血量至 `14/16`（恐惧 `+2`）及按人数回血至 `6/8×人数`；统一校准 15 条自定义音效音量并去重连击/转阶段/受击音效。
  - **盛碗虫族母**：玩家回合因受击生成的盛碗虫现在会立即进入 1 回合眩晕，不再于玩家结束回合时突然行动。
- **地图横路、事件、卡牌与显示修复**：
  - **金币横路（Crossroads）**：开通费用按章节调整为第一至三幕 **`50 / 100 / 150` 金币**；多人模式下改为全队每人各自支付、整队共享，并完善对应双语提示与地图连线规则。
  - **裂隙火盆与「永恒」词条持久化**：选择「投入炉心淬火」新增置入 1 张带「永恒」的灼伤，并新增 `EternalCardKeywordPersistencePatch` 确保非原版自带的「永恒」词条在存档读档与联机同步中持久保留。
  - **综合修复**：修复血蛭之母唤醒分支状态机与「缴械投降」恢复原意图异常、墓潮蛞蝓黏液招式补充召唤意图图标，并加固 `BossStateDisplayLayerPatch` 血条/意图层级钳制以防遮挡 ESC 菜单与顶层弹窗。

---

- **The Legacy Balance & Pacing Nerf**:
  - Reduced base HP from `300/320` (`+40` Dread) to **`240/260` (`+25` Dread)** while keeping *Beat of Death* fixed at `2` and opening *Hardened Shell* at `HP/5` (`48/52`, Dread `53/57`).
  - *Exhaust* now applies **`1` Vulnerable** (no longer carrying over into Turn 3 *Blood Bolts*), `1/2` Frail, and **`3` status cards** (1 Burn, 1 Dazed, 1 Slimed — down from 6).
  - Reduced *Echo* damage to **`13/15` (`+2` Dread)**, *Blood Bolts* base hit damage from `3` to **`2`** (`2×4 / 2×5`), and *Strengthen* Strength gain from `2` (`3` Dread) to **`1` (`2` Dread)**, eliminating persistent 20–30+ damage every turn in Act 1.
- **Abyssal Inkmaw Animation Overhaul, Phase-Lock Buff & Tentacle Tuning**:
  - **Smoothness & Performance Overhaul**: Rebuilt all 5 Spine rigs with 240Hz Bezier oversampling (peak triangles `31,386 → 10,295`, **-88%** CPU clipping cost), fixed background track queue stutter, high-frequency tremor jitter, and spray border 1-frame flash, simplified the ink drench shader, and polished Ink Blackout veil wash-off visuals.
  - **New *Abyssal Tide-Lock* Buff**: Explicitly displays the 50% HP phase lock threshold from Turn 1 with a custom icon, switches to locked state with `∞` HP display when triggered, and makes Phase 2 *Abyssal Surge* bypass the normal 2-tentacle cap/cooldown to **summon all 4 tentacles** (2 floor-breach majors + 2 outer smalls).
  - **Tentacle Tuning**: Lowered tentacle HP (Crushing `38/42`, Ink `36/40`, Small `20/24`), trimmed Crushing self-Block (`4/6`), and scaled sever recoil smoothly across 1P–4P (`20 / 48 / 72 / 96`).
- **Cave God *Magma Core Seal* Buff & Animation Polish**:
  - Added the ***Magma Core Seal*** phase-lock buff (custom icon, Turn 1 50% threshold counter, and locked-state tooltip) matching Abyssal Inkmaw's clarity; tightened reaction-track cleanup and idle motion polish.
- **Origin Fogmog & Bowlbug Progenitor Fixes**:
  - **Origin Fogmog / Origin Eye with Teeth**: *Nourish* now grants Strength only to Origin Fogmog (never mid-turn to later-acting Eyes, eliminating surprise intent mismatches); reduced boss HP (`220/230`, `+20` Dread), Eye HP (`14/16`, `+2` Dread), and per-player heal (`6/8×players`); calibrated all 15 custom WAV SFX levels and deduplicated overlapping combat sounds.
  - **Bowlbug Progenitor**: Bowlbugs spawned during the player's turn now enter stunned for 1 turn instead of attacking immediately at end of turn.
- **Crossroads, Events, Cards & UI Fixes**:
  - **Map Crossroads**: Act-scaled unlock cost (**`50 / 100 / 150` Gold** across Acts 1–3) with per-player payment in co-op, updated bilingual prompts, and improved link rules.
  - **Cracked Brazier & Eternal Keyword Persistence**: *Temper in the Core* now adds an *Eternal* Burn alongside *Tezcatara's Ember*, backed by `EternalCardKeywordPersistencePatch` for save/load and multiplayer serialization.
  - **General Fixes**: Fixed Leech Mother wake-up state transitions and *Surrender* state restoration, added the missing Summon intent icon on Gravetide Slug's Goop move, and tightened `BossStateDisplayLayerPatch` Z-index clamping so HP bars/intents never render over ESC/settings overlays.
