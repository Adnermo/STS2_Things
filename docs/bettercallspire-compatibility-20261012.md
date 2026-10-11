# BetterCallSpire 卡图缓存兼容 — 1.25.19

2026-10-12。针对 INCIDENT #28217A55「血蛭塞牌会卡住」实现。已安装到本机，尚未发布 Steam／GitHub。本轮遵守“先别实机测试”，只运行构建与独立、无窗口的原生 Godot 探针，没有启动完整游戏或联机客户端。

## 已确认的失败路径

报告中，血蛭母体已召唤两只小血蛭，随后向玩家手牌生成寄生牌时中断。异常为：

```text
System.ObjectDisposedException
Godot.GodotObject.GetPtr → TextureRect.SetTexture
BetterCallSpire.PortraitPatch.RestoreDefaultState
BetterCallSpire.PortraitPatch.TryReplacePortrait / UpdateVisualsPostfix
NCard.UpdateVisuals → CardPileCmd.AddGeneratedCardToCombat
LeechMother.Brood → CombatManager.ExecuteEnemyTurn
```

实际取得 [BetterCallSpire 工坊包](https://steamcommunity.com/sharedfiles/filedetails/?id=3747671229)，版本 `v1.0.0`。程序集 MVID `1abc6b205f5a426794a66475c15a974d` 与报告一致；DLL SHA256 `37d945be0a097f21dd15734f968b45743c49601c46a4126e99b4a0307deb97dd`。报告当时文件指纹尚未计算完成，因此不声称比对了玩家 DLL 的 SHA256。

其 `DefaultPortraitStates` 与 `DefaultAncientPortraitStates` 按卡牌类型保存首张默认 `Texture2D`。原生 `AssetCache` 在资源卸载时会 `Dispose` 贴图；缓存中仍持有的托管引用并不会因此变成 `null`。再次恢复默认卡图时，已失效贴图传入 `TextureRect.Texture`，引发异常并中断原生造牌。

离线使用真实工坊 DLL、原生 NCard 场景和节点池：生成寄生牌 → 返回节点池 → 原生资源卸载 → 重用卡牌。未启用兼容时，真实 `EnterTree`／`UpdateVisuals` 后缀重现上述异常；未伪造第三方模块或异常。

## 实现

- `Compatibility/BetterCallSpireCompatibility.cs` 只在检测到实际 `BetterCallSpire` 程序集后，检查已知私有缓存 API 并安装两个 helper 的 Harmony 前缀。
- 失效的默认贴图条目被移除；保留 `NCard.UpdateVisuals` 已加载的当前原生卡图。下一次刷新重新记录有效默认贴图。
- 已释放的卡图控件不再读写；隐藏卡图的合法空贴图仍可正常缓存与恢复。
- BetterCallSpire 正常替换卡图、伸展方式、其他补丁继续执行。没有更改血蛭数值、寄生数量或原版 `CardPileCmd` 流程，没有全局吞掉异常。
- 已加载程序集检测与 `AssemblyLoad` 订阅覆盖两种加载顺序，不新增前置依赖。重复初始化不重复安装；未来 API 形状改变时记录提示并跳过适配。

启动检查另发现旧资源包遗漏 `BugEngineLogger.cs` 的 Godot ScriptPath 资源。本次补齐该单一文件，并加入导出包含列表；原有 1308 个资源内容哈希保持一致。未更改 F2 采集逻辑。

## 验证

| 检查 | v0.107.1 / 59260271 | v0.111.0 / 41cef1ea |
| --- | --- | --- |
| 编译各自实现、统一引导的内嵌哈希与模型集合 | PASS | PASS |
| BetterCallSpire 先初始化，随后安装兼容 | 26 项 PASS | 26 项 PASS |
| 琐事先安装检测，随后加载并初始化 BetterCallSpire | 26 项 PASS | 26 项 PASS |
| 原生池化寄生牌、资源卸载后重新进树与刷新 | PASS | PASS |
| 正常替换卡图、替换牌节点重新用于状态牌 | PASS | PASS |
| 未安装 BetterCallSpire 时加载最终统一包 | PASS | PASS |
| 涅奥选遗物／读档／领取与深处事件池回归 | PASS | PASS |

两版本引用、依赖、原生 PCK 均来自相应完整保存运行库。104 项兼容检查与最终启动检查无引擎 ERROR。这里验证的是原生卡图生命周期和加载，不是恢复报告后通完整战斗，也不是 Steam 多人联机测试。

复现入口为 `scripts/test-portrait-compatibility.ps1`：传入 `-NativeGameDir`、对应 `-ImplementationDll`、`-PackagePck`、实际 `-ForeignDll` 和 `-OutputDir`；`-Cards` 执行真实卡牌生命周期，`-ForeignLoadOrder before/after` 核对加载顺序。`-Baseline -Cards` 配合原 1.25.18 实现会在原错误路径失败。

## 交付与边界

统一包：`dist/v1.25.19/STS2_Things-1.25.19-unified.zip`，四个运行文件及同目录 SHA256SUMS。安装器备份原包后覆盖本机 `mods/STS2_Things`，安装内容与 ZIP 每个条目哈希均核对。

证据在 `build/bettercallspire-compat-20261012`：实际工坊包获取回执、未修复原生复现、四组最终检查、双版本启动、资源逐文件校验、源码冻结、安装回执与 `delivery.json`。构建以已发布 `63a02e4`／1.25.18 冻结源码为基线，244 个原有 C# 文件保持不变，排除工作区其他 SoulRoes 改动。

本适配范围是取得的 BetterCallSpire 1.0.0。#9F838F87 的 `Mechaneko_Defect_Card_Art` 虽有相似异常，原包仍未取得，不能据此宣布那个模组也已兼容。已经因异常中断的当前回合不会被补丁追溯恢复；加载更新后须重新进入战斗。
