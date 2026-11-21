# RegentCardsAnimeRework 兼容 — 1.25.20

2026-10-12。针对 INCIDENT #6A50D0FC 的卡图恢复异常实现，沿用 BetterCallSpire 兼容。已安装本机，未发布 Steam／GitHub；本轮只做构建与无窗口原生 Godot 验证，没有启动完整游戏或网络联机客户端。

## 改动

`Compatibility/PortraitCacheCompatibility.cs` 替代 1.25.19 的单模块适配，检测两个已核实程序集：

| manifest / 工坊 | 程序集名称 | 已核实版本 |
| --- | --- | --- |
| BetterCallSpire / 3747671229 | BetterCallSpire | 1.0.0 |
| RegentCardsAnimeRework / 3747626664 | regentCardsAnimeRework | manifest v1.0，程序集 1.0.0.0 |

仅在这些模块的私有缓存 API 形状相符时，适配 `RememberDefaultState` 与 `RestoreDefaultState`。被释放的默认贴图条目会移除，保留原生 NCard 已重新加载的当前卡图；缓存中的合法伸展方式及 Regent 的 TextureFilter 仍在当次刷新恢复。正常替换卡图继续执行，隐藏图的合法 null 也继续恢复。

两个卡图包各有独立的私有 `PortraitState` 类型，反射元数据按原始 Restore 方法分别保存。组合加载、加载先后与重复初始化不会让一个模块使用另一个的属性访问器。初始化失败时只撤回该模块的两个适配前缀，不撤除另一个模块的兼容或其他人的补丁。

不声明卡图包为前置，不分发它们的 DLL/PCK，不全局吞异常。洞胃的原版保留碎屑、血蛭寄生数量与原生 `CardPileCmd` 未改动。

## 身份与原始失败

实际 Regent 工坊 DLL/PCK 与报告完整 SHA256 一致：DLL `9d5e3e6ced203237d0b4a4a8e1de3e7f4a4ba6dadcc394b30c07585f9ae54f6d`，PCK `6ed8e1f26502b9dcefa835e41e0925e8ef8a8fddfca9de038f434943239a4bc9`。原生探针读取的 MVID `7db9b2f8d3114673952daed6c90ca63c` 也与报告一致。

修复前使用 1.25.19 实现与真实 Regent 卡图补丁：原生 NCard 返回节点池 → 原生 AssetCache 卸载旧贴图 → 同一节点再次显示寄生牌。再次进入树与刷新视觉时抛出报告同款 `ObjectDisposedException`，路径为 `TextureRect.SetTexture → regentCardsAnimeRework.PortraitPatch.RestoreDefaultState → NCard.UpdateVisuals`。此次未恢复整个四人战斗，也没有以伪造模块制造错误。

## 验证

使用相应完整保存运行库的实际 DLL、依赖和 PCK：v107.1／59260271、v111／41cef1ea。

| 每个版本的检查组 | 加载顺序 | 每组断言 |
| --- | --- | --- |
| Regent 单独加载 | 卡图包在琐事之前／之后 | 各 35 |
| BetterCallSpire 单独加载 | 卡图包在琐事之前／之后 | 各 30 |
| Regent → Better，组合加载 | 两卡图包在琐事之前／之后 | 各 44 |
| Better → Regent，组合加载 | 两卡图包在琐事之前／之后 | 各 42 |

共 16 组、604 项通过，无引擎 ERROR。覆盖两个默认图缓存、失效贴图与失效控件、合法空图、重复初始化、独立私有类型、原生节点池复用、真实寄生卡面、带保留的原版 Debris、正常换图、过滤与伸展恢复。Debris 的失效边界使用其旧真实贴图包装器 Dispose；寄生牌使用原生 AssetCache 的实际卸载入口。

组合检查保留原有补丁顺序：两个卡图包对同一卡牌的美化优先级没有被本适配重排。正常美化与过滤逐个包单独核对，组合检查核对缓存隔离及状态牌生命周期。

最终统一包的双版本内嵌实现哈希、159 个模型集合、原生 ModManager 启动以及涅奥／深处事件池回归通过。PCK 与可选 BaseLib 桥的哈希保持 1.25.19 的已验证内容一致，包含上次补齐的 F2 日志 ScriptPath 资源。

## 交付

- 本机四文件安装完成并逐个核对 SHA256；旧 1.25.19 保存在 Workbench 安装备份中。
- `dist/v1.25.20/STS2_Things-1.25.20-unified.zip` 与同目录 `SHA256SUMS.txt`，ZIP CRC 和内部四文件哈希一致。
- `build/regent-portrait-compat-20261012` 保留未修复复现、16 组检查、双版本启动、源码冻结、安装回执及 `delivery.json`。
- `scripts/test-portrait-compatibility.ps1` 新增 `-SecondForeignDll`；原有 `-Cards`、`-ForeignLoadOrder before/after` 和 `-Baseline` 仍可使用。`scripts/package-portrait-compat.py` 验证并生成本次交付。

构建以已发布 1.25.18／`63a02e4` 冻结源码为基线，244 个原有 C# 文件不变；只接入新兼容、更新版本与引导提示，未纳入工作区 SoulRoes 的其他改动。

已中断的旧回合需要重新进入战斗后使用新代码。本轮没有宣称 Steam 四人整局已复测，也没有把报告中另四条 `SwapGCHandleForType` 入场错误宣布为已修复。缺少原包的 `Mechaneko_Defect_Card_Art` 仍不在已验证兼容范围。
