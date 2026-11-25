# 尖塔：琐事 2.0.11 / STS2 Things 2.0.11

- **升级双版本基线至《杀戮尖塔 2》`v0.114.0`（public-beta）+ `v0.111.0 / v0.111.1`（正式版）**：
  - 统一四文件包由 `v111.1 + v113.1` 迁移升级为 **`v111.1`（正式版）+ `v114.0`（最新 public-beta）** 双目标原生编译（退役旧 `v113.1` 测试目标），`UnifiedBootstrap` 启动时依据客户端真实程序集 ABI 自动加载匹配实现。
  - 原生适配 `v0.114.0` 的 `CardModel.UpgradeInternal(bool silent)`、`DowngradeInternal(int)`、`Creature.DamageBlockInternal / LoseHpInternal(..., bool dryRun)` 与 `DreadRunModel.BeforeActEntered` 等 API 变更（蛇发女妖「蛇发」、异鱼症、分裂附魔与恐惧模式章节支配全路径兼容）。
- **紧急修复墓潮蛞蝓（Gravetide Slug）吞噬尸体永久卡死（含「节日拉炮」开局击杀小蛞蝓 & F2 报告）**：
  - 根因：`v0.114.0` 重做 `QuietusPower` 时移除了 `ArrowSlot.DestroyAfterImpaled` 与 `ArrowStatus.Destroyed`。旧版未针对 `v0.114.0` 编译，在 `ThingsGravetideDigestionPower` 播放吞噬动作并调用 `Sts2VersionSeam.RemoveCreatureWithoutDeathOrEscape` 移除尸体时抛出 `MissingMethodException`，导致敌方回合结束循环中止，Boss 永久卡在趴地吞噬循环（携带「节日拉炮」首回合击杀低血量小蛞蝓时必现）。
  - 修复：面向 `v0.114.0` 重写 `RemoveCreatureWithoutDeathOrEscape` 与 `FoolArrowTargetabilityPatch` 的箭矢回收路径，同时一并修复 `v0.114.0` 下**异彩史莱姆分裂**与**愚者箭矢**的同源崩溃；加固 `ThingsGravetideCorpseSlug.RevealCorpseAfterDeathAnimation` 在尸体已被清理时的空引用保护，并在 `GravetideSlugProbe` 中新增「节日拉炮」首回合击杀 + 吞噬尸体专项回归测试。
- **第三方战斗计算模组兼容加固（F2 报告）**：
  - 收紧 `ThingsQuirkyHopperRewardPolicy` 的战利品评估房间过滤，仅在怪癖草蜢遭遇房间内参与拦截，避免在普通战斗中触发外部战斗求解/模拟类模组的兼容性报错。

---

- **Dual-Version Baseline Upgraded to `v0.114.0` (public-beta) + `v0.111.0 / v0.111.1` (Main)**:
  - Migrated the single 4-file unified package from `v111.1 + v113.1` to **`v111.1` (Main) + `v114.0` (latest public-beta)** native compilation (retiring the older `v113.1` beta target), with `UnifiedBootstrap` fingerprinting the live `sts2.dll` ABI at startup and unpacking only the matching implementation.
  - Adapted to `v0.114.0` breaking IL/API changes including `CardModel.UpgradeInternal(bool silent = false)`, `DowngradeInternal(int)`, `dryRun` damage preview overloads, and `DreadRunModel.BeforeActEntered`.
- **Critical Fix: Gravetide Slug Boss Corpse-Devour Softlock (Including *Festive Popper* Turn 1 Kill & F2 Reports)**:
  - Root cause: `v0.114.0` reworked `QuietusPower` and removed `ArrowSlot.DestroyAfterImpaled` and `ArrowStatus.Destroyed`. When `ThingsGravetideDigestionPower.AfterSideTurnEnd` entered `devour_loop` and called `Sts2VersionSeam.RemoveCreatureWithoutDeathOrEscape(corpse)`, JIT threw `MissingMethodException`, aborting `CombatManager`'s turn-end loop and freezing the boss in `devour_loop` permanently (triggered reliably when *Festive Popper* killed a low-HP opening Corpse Slug on Turn 1, and confirmed in player F2 reports).
  - Fix: Updated `RemoveCreatureWithoutDeathOrEscape` and `FoolArrowTargetabilityPatch` for `v0.114.0` arrow retrieval (also fixing the same `v0.114.0` break on **Chromatic Slime** splits and **Fool** arrow shots), hardened `ThingsGravetideCorpseSlug.RevealCorpseAfterDeathAnimation` against already-removed corpses, and added a dedicated *Festive Popper* Turn 1 kill + digestion regression test in `GravetideSlugProbe`.
- **Third-Party Combat Solver Compatibility Hardening (F2 Report)**:
  - Scoped `ThingsQuirkyHopperRewardPolicy` strictly to `CombatRoom` instances running `ThingsQuirkyHopperWeak`, avoiding unnecessary hook interference with external combat-solver mods in normal fights.
