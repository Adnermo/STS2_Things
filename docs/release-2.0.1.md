# 尖塔：琐事 2.0.1 / STS2 Things 2.0.1

- **紧急修复渊墨巨蛸（Abyssal Inkmaw）战斗卡崩（`0xc00000fd` 栈溢出）**：
  - 修复「墨浊覆牌」卡面遮罩（`NInkBlackoutCardVeil`）在摘除或洗净时先调用 `NCard.UpdateVisuals` 引发 Harmony Postfix 无限递归的问题；现改为先还原被遮罩子节点（含高亮层）并从场景树摘除节点，再通过重入保护安全刷新卡面。
  - 修正断主触腕与 Boss 死亡清洗顺序（先移除 `ThingsInkBlackoutPower` 再执行全牌库洗净），并补齐小触腕击杀单牌清洗的当回合免黑标记。
  - 优化渊墨巨蛸召腕节奏：单次召唤（含二阶段转场）最多生成 2 条触腕，并加入 2 回合召唤冷却，避免连续铺满 4 条触腕。
- **第二章「深处」精英池扩充与战斗打磨**：
  - **人面柱**正式升格为深处第 4 组专属自制精英（深处调整为 6 弱 / 10 强 / 4 专属精英）；上层免疫与中层 50% 减免掉的伤害现全部传导至最底层。
  - 调优菌根双生子、逆流蝾螈、收音水母与始源雾菇（含始源利齿之眼招式与数值）的机制表现与战斗平衡。
- **墓潮蛞蝓美术焕新**：更新墓潮蛞蝓骨骼贴图图集、水面背景、地图/战史图标与专属消化能力图标。
- **小游戏事件界面与火堆复制修复**：统一优化 5 个交互小游戏界面主题与双语提示；修复从模组事件获得的「复制（Clone）」附魔在未持有佩尔的生长时不显示火堆复制选项的问题。
- **双版本兼容**：统一支持 **v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，四文件统一包已通过双版本全量编译、实时 `NCard` 回归与原生探针验证。

---

- **Hotfix for Abyssal Inkmaw combat crash (`0xc00000fd` StackOverflowException) & tentacle pacing**:
  - Fixes infinite recursion in `NInkBlackoutCardVeil.DetachAndFree()` where calling `NCard.UpdateVisuals()` before detaching re-entered `Refresh()` via Harmony postfixes; now restores masked children (including highlight), detaches first, and guards visual refreshes against re-entrancy.
  - Fixes major-tentacle/boss-death wash order (`PowerCmd.Remove` before `WashAllCards`) and tracks per-turn washed cards so small-tentacle single-card washes stay unveiled.
  - Caps Abyssal Inkmaw spawns to at most 2 tentacles per summon turn (including Phase 2 transition) with a 2-turn summon cooldown.
- **Act II Depths elite pool & combat tuning**:
  - Promotes **Human-Faced Column** to the 4th exclusive Depths elite (6 weak / 10 regular / 4 custom Depths elites); damage prevented by upper/middle layers now redirects in full to the bottom layer.
  - Tunes Mycorrhizal Twins, Reverse-Current Salamander, Receiver Jellyfish, and Origin Fogmog (including Origin Eye with Teeth moves & values).
- **Gravetide Slug art refresh**: Updates the Gravetide Slug Spine texture atlas, water backdrop, map/run-history icons, and Digestion power icon.
- **Minigame UI polish & Clone enchantment campfire fix**: Refines UI theme & localization across all 5 interactive minigame events; ensures cards with the vanilla `Clone` enchantment gained from mod events properly offer the campfire Clone option even without Pael's Growth.
- **Dual-version support**: Unified 4-file package for **v0.111.1 (Main)** and **v0.113.1 (public-beta)**, verified across both targets.
