# STS2_Things 双版本支持

## 当前目标（1.25.27）

| 实现目标 | 实际游戏版本 | 编译常量 | 引导内嵌资源 |
| --- | --- | --- | --- |
| `v107.1` | `v0.107.1`，兼容保留 | `STS2_V107_1` | `STS2_Things.Implementations.v107.1.dll` |
| `v113` | `v0.113.0`，Steam public-beta | `STS2_V113` | `STS2_Things.Implementations.v113.dll` |

旧现代目标 v111 已由 v113 替代。v0.110.1 和 v0.111.0 的参考源码、私有运行库及历史发布包保留；它们不是 1.25.27 的编译目标。旧游戏可使用对应历史 Mod 版本。

对外仍为 manifest、统一引导 DLL、共用 PCK、可选 BaseLib 配置桥四文件。两个实现程序集内部名都是 `STS2_Things`，每个进程只加载其中一个。所有联机玩家需使用同一游戏版本和统一包。

## 原生 API 适配

- 死亡回调：107.1 保留四参数；113 使用包含 `CardModel? source` 的五参数。父类调用透传来源，不替换死亡、召唤或复活机制。
- 生命上限损失：107.1 使用 `isFromCard=false`，113 使用 `cardSource=null`。
- 巨岩交互：107.1 保留 `IsHealthBarVisible` 和 `ToggleIsInteractable`；113 通过 `NCreature.SetIsInteractable` 动态控制核心和手臂。新版 `MonsterModel.IsInteractable` 只承担静态规则，不放入动态阶段判断。
- 巨岩释放玩家：113 走 `NCreatureStateDisplay.AnimateIn(FromHidden)`；旧版保持原入口。
- 章节生成：113 精确定位四参数 `ActModel.GenerateRooms(Rng, UnlockState, bool, bool)`，避开新的 `GenerateRooms(RunState)` 包装重载。
- 商人购买：113 在 `MerchantSynchronizer.PurchaseLocalEntry` 广播前处理猜拳。获胜先发送可靠、有原生位置缓冲的协商价格消息，再执行原生购买；远端只设置发送者所属库存的价格，结算后清理。金币、奖励、库存、补货和购买 Hook 留给原版。
- 商人预载：113 挂接 `NStandardMerchantRoom.AssetPaths`；107.1 使用 `NMerchantRoom.AssetPaths`。
- 联机设置：分别定位 StartRunLobby、JoinFlow 的真实构造签名，适配新增的 `isCustom` 和连接初始化器。
- F2：113 使用 screenshot bytes 与 owned `FeedbackLogFiles`；107.1 保持两条 owned streams。只截获本 Mod 明确选择的反馈类别。
- 探针：`tools/ProbeSupport/ProbeRuntimeApi.cs` 适配开局、进阶、选牌、死亡和 RunModel 生命周期；不进入运行包。

## 引导与注册

107.1 的五参数伤害 Hook 选择旧实现，保留类型追加及 assembly 提升桥。113 需同时满足六参数伤害 Hook、CombatId、五参数死亡 Hook（第三参数为 CardModel）以及 NStandardMerchantRoom 类型，随后走官方 `AssociateAssemblyWithMod`。旧现代 ABI 不会误选新实现。

## 构建与交付

`build-all.ps1` 的现代参数已迁移为 `DataDirV113`、`SourceRootV113`；最低版本仍为 `v0.107.1`。指定真实引用目录和 Godot 4.5.1 Mono 路径构建，不混用原版 PCK。

本轮专用产物在 `build/beta-v113-20261113/`，保留先前公开包。共用资源复用 1.25.26 已验证 PCK；原版依赖、客户端、临时探针和个人配置不进入交付。实际差异及验证见 [V113 迁移](MIGRATION_V113.md)。
