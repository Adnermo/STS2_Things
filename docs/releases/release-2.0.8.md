# 尖塔：琐事 2.0.8 / STS2 Things 2.0.8

- **适配 `ActLikeIt2`（Act Like It 2 v0.2.6）**：
  - 零硬依赖注册第二幕「深处」（`ThingsDepths`）至 `ActLikeIt2` 第二幕分支岔路口，并补充中英文选项副标题；未安装 `ActLikeIt2` 时不加载任何额外补丁，双向加载顺序均兼容。
  - 完整联动「必定遇到深处（第二幕）」开关（`FeatureGuaranteedDepthsEnabled`）：关闭时遵循 `ActLikeIt2` 岔路口自由选择（巢穴 / 深处 / 随机）；开启时自动将开局第二幕锁定为「深处」并跳过第二幕岔路口直达（不影响第 1/3/4+ 幕）。
  - 修复 `ActLikeIt2 v0.113.0` 在切换第二幕重生成房间时硬编码 `isBlitz: false` 的问题，确保急速模式（Blitz）经岔路口进入「深处」仍正确生成 1 弱敌 + 1 常规敌。
- **Boss 机制与数值精调**：
  - **人面塔（The Visage Tower）**：「暴怒凝视」「戏谑反讽」、破面震荡、双面连携与残局轮转现在会为**所有存活石面**提供力量；「悲叹」额外伤害从仅限悲叹面扩展至人面塔所有石面的攻击。
  - **活体巨岩（Cave God）**：一阶段破防倒地后的虚弱输出窗口扩展为 **2 回合**（`WEAK_PRONE_1` → `WEAK_PRONE_2` → `WEAK_UNKNOWN_3`），且虚弱期间任意回合触发狂暴阈值均可立即衔接「狂暴苏醒」。
  - **腐化之遗（The Legacy）**：重塑为暗渠高难 Boss——「死亡律动」固定保持 2 层（强化与恐惧模式不再追加自伤）；基础血量调整为 `300/320`（恐惧 `+40`）、硬化外壳开局收紧至 `HP/5` 并随强化逐步收紧至 `HP/6`、`HP/7`…（最低保底 20）；「衰竭」附带 `1/2` 层易伤与脆弱，「回音」升至 `18/20`（恐惧 `+2`），「血弹」升至 `3×5 / 3×6`，每次强化稳定获得 `2`（恐惧 `3`）力量。
- **恐惧模式（Dread）章节支配（Dominion）平衡与修复**：
  - 巨型甲虫支配移除玩家开局 `10` 层缩小，仅保留所有敌人 `10` 层放大。
  - 盛碗虫族母支配的队友死亡加力由 `2` 下调至 `1`（单敌开局仍为 `2`）。
  - 人面塔支配调整为每回合**首次**连续打出 3 张同类型牌时所有敌人获得 `4` 点格挡（原为无限触发 `6` 点）。
  - 墓潮蛞蝓支配移除全敌 `3` 层再生，保留开局洗入 `2` 张黏液。
  - 腐化之遗支配调整为每回合**从第 4 张牌起**每打出一张牌受到 `1` 点伤害。
  - 始源雾菇支配排除 `IsSecondaryEnemy` 次级召唤物；新增 `ThingsWaterfallGiantDominionSpawnPatch`，防止原版瀑布巨兽支配将精英级 7 点自爆赋予灵鱼卵分裂子体（`ThingsSoulRoe`）或模组次级召唤物。
- **多版本兼容与国内直链网盘加速**：统一四文件包实测兼容 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**；个人直链网盘（`https://pan.adnermo.online/STS2_Things/latest`）现已启用 Workers 原生边缘缓存，支持断点续传直命中边缘节点。

---

- **`ActLikeIt2` (`Act Like It 2 v0.2.6`) Optional Compatibility**:
  - Zero-dependency registration of Act II **The Depths (`ThingsDepths`)** into `ActLikeIt2`'s Act 2 Fork slot with localized option descriptions (`eng`/`zhs`), supporting both mod load orders cleanly.
  - Interoperates with the **Guaranteed Depths** toggle (`FeatureGuaranteedDepthsEnabled`): leaves the Fork screen intact when disabled, and honors explicit opt-in by preserving `ThingsDepths` in Act 2 and skipping only the Act 2 Fork without affecting Acts 1, 3, or 4+.
  - Fixes `ActLikeIt2 v0.113.0`'s hardcoded `isBlitz: false` room regeneration for `ThingsDepths`, ensuring Blitz runs swapped through the Fork properly roll 1 Weak + 1 Regular combat.
- **Boss Mechanics & Balance Tuning**:
  - **The Visage Tower**: *Wrathful Glare*, *Mocking Echo*, shatter tremor, dual-face combos, and final-stand cycles now grant Strength to **all living faces**; *Sorrow* bonus damage now applies to attacks from **all Visage Tower faces**.
  - **Cave God**: Phase 1 exposed knockdown window expanded to **2 turns** (`WEAK_PRONE_1` → `WEAK_PRONE_2` → `WEAK_UNKNOWN_3`), while still transitioning immediately into `ENRAGE_RECOVER` on any weak turn once the enrage threshold is met.
  - **The Legacy**: Reworked into a high-challenge Act 1 Underdocks boss — *Beat of Death* stays fixed at 2 (no longer stacks self-damage via Strengthen/Dread), HP increased to `300/320` (`+40` Dread), *Hardened Shell* tightened to `HP/5` at start and `HP/6`, `HP/7`… across Strengthen stages (floor 20), *Exhaust* now adds `1/2` Vulnerable & Frail, *Echo* raised to `18/20` (`+2` Dread), *Blood Bolts* raised to `3×5 / 3×6`, and every Strengthen consistently grants `2` (`3` Dread) Strength.
- **Dread Mode Dominion Balancing & Fixes**:
  - **Scale Beetle Dominion**: removed `10` Shrunk on players (retains `10` Enlarged on enemies).
  - **Bowlbug Progenitor Dominion**: on-death ally Strength reduced from `2` to `1` (solo enemy start stays `2`).
  - **Visage Tower Dominion**: now triggers once per turn on the first 3 consecutive same-type cards for `4` Block to all enemies (was `6` every trigger).
  - **Gravetide Slug Dominion**: removed `3` Regeneration from enemies (retains `2` Slimed shuffled at combat start).
  - **The Legacy Dominion**: now deals `1` damage starting from the **4th card played each turn**.
  - **Origin Fogmog & Waterfall Giant Dominion Guards**: skips secondary enemies on Origin Fogmog Dominion and prevents vanilla Waterfall Giant Dominion from applying elite-tier 7-damage Explosive onto spawned `ThingsSoulRoe` fragments or secondary mod minions.
- **Multi-Version Support & Accelerated Direct Mirror**: Unified 4-file package verified on **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with native edge-cached downloads at `https://pan.adnermo.online/STS2_Things/latest`.
