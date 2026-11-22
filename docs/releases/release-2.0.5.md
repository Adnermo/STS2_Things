# 尖塔：琐事 2.0.5 / STS2 Things 2.0.5

- **紧急修复：渊墨巨蛸一阶段吃灾厄死亡导致回合卡死（`KillWithoutCheckingWinCondition` 递归异常）**：
  - 修复渊墨巨蛸一阶段被灾厄（`DoomPower`）直接归零生命时，`AfterPreventingDeath` 未恢复血量至半血阈值导致 `CreatureCmd.KillWithoutCheckingWinCondition` 递归 10 次并抛出 `InvalidOperationException` 使战斗卡死的问题；现在会稳定锁在半血进入二阶段过渡，二阶段受灾厄则正常死亡并清理全部存活触手。
  - 同步排查并加固活体巨岩本体（一阶段半血锁定态重复受灾厄）与双臂免死逻辑，杜绝同源递归隐患。
- **多项减伤机制大幅下调**：
  - **盛碗虫族母（护主）**：有盛碗虫存活时的攻击伤害减免由 `50%` 降至 **`25%`**。
  - **人面柱（叠柱）**：上层由免疫（`100%`）改为 **`50%`** 减伤、中层由 `50%` 改为 **`25%`** 减伤，这两层减免的伤害仍全部传导给最底层。
  - **渊墨巨蛸（触腕庇护）**：改为有存活主触腕时固定提供 **`25%`** 攻击伤害减免（不再按双触腕叠加至 `50%`）；斩断主触腕的 20 反伤、2 层易伤与冲净墨浊覆牌保留。
  - 同步更新上述三项能力的中英文描述文本。
- **多版本兼容与直链网盘**：统一四文件包实测兼容 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，同步更新国内直链网盘（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **Critical Hotfix: Abyssal Inkmaw Phase 1 Doom Softlock (`KillWithoutCheckingWinCondition` recursion)**:
  - Fixed a crash where lethal `DoomPower` triggering on Phase 1 Abyssal Inkmaw zeroed HP via `LoseHpInternal` without restoring HP in `AfterPreventingDeath`, causing `CreatureCmd.KillWithoutCheckingWinCondition` to recurse 10 times and throw `InvalidOperationException`, freezing the combat turn loop. Phase 1 Doom now cleanly clamps the Boss at its 50% threshold and queues Phase 2 Surge; Phase 2 Doom kills the Boss and clears all surviving tentacles cleanly.
  - Hardened Living Megalith (`ThingsCaveGodBody` repeated Doom while enrage-locked and `ThingsCaveGodHand` fallback HP restoration) against the same recursion path.
- **Damage Reduction Tuning**:
  - **Bowlbug Progenitor (`Protect the Master`)**: Attack damage reduction while a Bowlbug is alive lowered from `50%` to **`25%`**.
  - **Human-Faced Column (`Stacked`)**: Top layer changed from full immunity (`100%`) to **`50%`** reduction, and middle layer lowered from `50%` to **`25%`** reduction; all prevented damage continues to redirect to the bottom layer.
  - **Abyssal Inkmaw (`Tentacle Cover`)**: Changed to a flat **`25%`** Attack damage reduction while any major tentacle is alive (no longer stacks to `50%` with two tentacles); 20 recoil damage, 2 Vulnerable, and Ink Blackout wash on sever remain unchanged.
  - Updated bilingual localization for all three powers.
- **Multi-Version Support & Direct Mirror**: Unified 4-file package verified across **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with direct downloads at `https://pan.adnermo.online/STS2_Things/latest`.
