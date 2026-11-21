# 玩家报告修复 — 1.25.23（2026-10-20）

后台从 27 条增加到 49 条。本轮读取了 1.25.22 的 22 条新报告，下载并核对了报告中可取得的工坊包；没有改变后台报告状态，也没有启动完整 Steam 客户端或真实多机联机。修复版本为 1.25.23。

## 已修复

### 卡图缓存失效导致加牌、预览或悬停卡死

报告 `32215d71`、`4d2713cc`、`8bfed9dd`、`15dd88fc`、`6559398b`、`fe4f8f0a`、`87daf665`、`fe83b910`、`7726e220` 都出现第三方 `PortraitPatch` 在 `TextureRect.SetTexture` 上读取已释放 Godot 纹理。报告中的 `Life_with_spire`、`Diana111`、`com_demo_midori_black_card`、`AliceDefectCard` 工坊包均可取得，并与 `PortraitState`／默认缓存方法合同匹配；手动安装的 `regentcardsanimerework` 也匹配同一合同。

1.25.23 新增这些适配器。程序集名与私有类型解析不再受大小写影响；适配器只在私有 `PortraitPatch` 形状完整时安装。共用缓存守卫会在第三方恢复默认图前丢弃已释放引用；若包有经过签名核对的 `TryResolveReplacementTexture(cardId, instanceId, logicalCardKey, out texture)` 方法，也会把失效的替换图视作未命中，使原版卡图继续显示。正常替换、伸展和过滤行为保持不变。`PortraitCacheGuard` 仍按每个程序集保存独立的私有 `PropertyInfo`。

### 菌根双生子连根空引用

报告 `4d2713cc` 的当前异常直接来自 `MycorrhizalTwin.Partner` 与 `NMycorrhizalLink._Process`。原实现读取已从 `CombatState` 移除的伙伴。现在伙伴查找从 107.1／111 都支持的 `Creature.CombatState` 开始，单位脱离战斗即返回空伙伴；根节点同时检查宿主父节点和视觉节点，战斗移除后连根淡出。两版本原生探针在移除双生子后均通过。

### 活体巨岩意图投影中断回合

报告 `75a0d6d8` 的直接异常为 `ThingsCaveGodHand.Plan` 的 `KeyNotFoundException`，发生在 `AfterSideTurnStart` 的手臂意图投影。玩家启用了替换怪物意图的组件，检查点仍有 `ANTHONY_MOVE_*` 外部意图。现在缺少投影键时保持手臂上一次合法意图并记录警告，玩家回合 Hook 继续执行；正常状态机和原生目标筛选不变。两版本探针用缺少 `REST` 的外部状态机重现并覆盖修复。

## 验证

- v107.1 与 v111 实现编译通过，0 警告、0 错误。
- 两版本卡图缓存合同探针通过：Life with spire、Diana111、Midori、AliceDefectCard 各 19 项；RegentCardsAnimeRework 各 23 项。
- 两版本真实原生卡牌池回收探针通过：五个报告相关卡图包各 30 项，Regent 各 35 项，覆盖寄生牌、保留碎屑、换图回退和池化复用；Life with spire 失效动画替换图在守卫后通过。另有三包组合、Things 后加载 44 项通过。
- v111 多卡图组合加载通过，44 项独立缓存检查；Life with spire 的完整池化卡牌路径在守卫后通过 30 项。
- 两版本活体巨岩目标与意图探针通过，覆盖 1／2／4 人、埋藏核心、倒地手臂、恢复、眩晕、伙伴移除和缺失 `REST` 投影键。
- 两版本 Depths 原生章节回归通过，各 8289 项；统一启动、Neo 三选一与续跑回归通过。

## 未纳入本轮

- `095c6dcb` 与 `e0e7cf3d` 的蜗牛多人 checksum 分歧栈只有原版校验器，报告动作中碎屑牌已成功结算，缺少两端差异字段，暂不改蜗牛奖励逻辑。
- `d17aa05d`、`6d54c6ac` 的先古报告当前栈是 `Sts2MapMod` 自身 Harmony 补丁；本模组的 Neow 过滤仅排除 `ThingsCrossroads`，已有 Neow 回归仍通过，尚未用完整先古组合构成确定性复现。
- Mechaneko 手动卡图包没有可取得的原始 DLL；`Mechaneko_Defect` 工坊包是不同程序集，未伪造兼容。
- Death Rhythm、蛞蝓音乐听感、AncientAffection、Orchis 皮肤和无具体现场的“卡了”报告，当前证据不足以归因到本模组。

1.25.23 构建、双版本原生回归与包哈希回执位于 `build/latest-reports-20261020`；平台发布状态以对应的工具回执为准。
