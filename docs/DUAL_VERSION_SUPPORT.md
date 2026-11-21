# STS2_Things 双版本支持

## 当前目标

| 实现目标 | 实际游戏版本与分支 | 编译常量 | 引导内嵌资源 | 目标清单 |
| --- | --- | --- | --- | --- |
| `v111.1` | `v0.111.1`，Steam 正式版分支（official） | `STS2_V111_1` | `STS2_Things.Implementations.v111.1.dll` | `manifests/v111.1/STS2_Things.json` |
| `v113.1` | `v0.113.1`，Steam 测试分支（public-beta） | `STS2_V113_1` | `STS2_Things.Implementations.v113.1.dll` | `manifests/v113.1/STS2_Things.json` |

双版本基线已由旧 `v107.1 + v113` 迁移至 **`v111.1 + v113.1`**。统一清单 `STS2_Things.json` 的 `min_game_version` 为 `v0.111.1`；`v0.107.1`、`v0.110.1`、`v0.111.0`、`v0.113.0` 等历史参考源码与旧发布包不再作为当前编译目标。

对外交付保持同一套四文件（`STS2_Things.json`、统一引导 `STS2_Things.dll`、共用 `STS2_Things.pck`、可选 `STS2_Things.BaseLibBridge.dll`）。两套实现程序集内部名均为 `STS2_Things`，每个游戏进程仅解包并加载匹配当前客户端的一套实现。联机玩家须使用相同游戏版本与同一统一包。

## 原生 API 适配（`STS2_V111_1` / `STS2_V113_1`）

- **程序集关联与存档属性缓存**：`v0.111.1` 与 `v0.113.1` 均原生支持官方 `ModManager.AssociateAssemblyWithMod`，并由 `ModelIdSerializationCache` 自动扫描关联程序集中的 `[SavedProperty]`；无需也不再保留旧版手工类型表注入或 `SavedPropertiesTypeCache` 反射桥。
- **战斗单位节点解析**：两版统一通过 `NCombatRoom.Instance?.GetCreatureNode(creature)` 获取战斗场景单位节点。
- **死亡回调**：`v111.1` 使用四参数死亡 Hook；`v113.1` 使用包含 `CardModel?` 来源的五参数死亡 Hook，父类调用透传来源，不替换原版死亡、召唤或复活流程。
- **生命上限损失**：`v111.1` 使用 `isFromCard: false`，`v113.1` 使用 `cardSource: null`。
- **巨岩（CaveGod）交互与释放动画**：`v111.1` 与 `v113.1` 按各自 `NCreature` 可交互状态接口与 `NCreatureStateDisplay` 动画入口分支编译；`v113.1` 下 `MonsterModel.IsInteractable` 仅保持静态规则，动态阶段切换交由 `NCreature.SetIsInteractable`。
- **章节房间生成**：`v113.1` 精确绑定四参数 `ActModel.GenerateRooms(Rng, UnlockState, bool, bool)`，避开新增的 `GenerateRooms(RunState)` 包装重载；`v111.1` 绑定其对应原生签名。
- **商人猜拳与资源预载**：`v113.1` 在 `MerchantSynchronizer.PurchaseLocalEntry` 广播前处理猜拳并挂接 `NStandardMerchantRoom.AssetPaths`；`v111.1` 挂接 `v0.111.1` 原生购买同步入口与 `NMerchantRoom.AssetPaths`。金币、奖励、库存、补货和购买 Hook 均留给原版。
- **联机大厅与 F2 反馈**：分别按 `v111.1` 与 `v113.1` 的 `StartRunLobby` / `JoinFlow` 构造签名（如 `isCustom`、连接初始化器）以及 `FeedbackLogFiles` / 截图参数签名条件编译，仅截获本 Mod 显式选择的反馈类别。
- **隔离探针**：`tools/ProbeSupport/ProbeRuntimeApi.cs` 统一封装两版开局、进阶、选牌、死亡和生命周期差异，仅用于验证探针，不进入发布包。

## 引导判定（`DetectTarget`）与注册

1. `UnifiedBootstrap.DetectTarget` 在进程内检查当前已加载 `sts2.dll` 的真实运行时签名：
   - 当六参数 `Hook.ModifyDamageMultiplicative`、`CombatId`、第三参数为 `CardModel` 的五参数死亡 Hook 以及 `NStandardMerchantRoom` 同时存在时，判定为 `v113.1`；
   - 当具备 `v0.111.1` 现代基线签名（六参数伤害 Hook、`CombatId`、无 `CardModel` 参数的四参数死亡 Hook 及传统 `NMerchantRoom`）且不含 `v113.1` 新增类型/签名时，判定为 `v111.1`；
   - 其余客户端 ABI 直接抛出清晰的版本范围诊断，绝不误加载不匹配实现。
2. 选定目标后，引导从嵌入资源读取对应 `STS2_Things.Implementations.<target>.dll`，调用 `ModManager.AssociateAssemblyWithMod` 完成官方模型、本地化与 `[SavedProperty]` 归属注册，再触发所选实现的初始化入口。

## 构建与验证命令

- **源码与契约静态审查**：
  ```powershell
  python .\scripts\verify_project.py
  ```
- **单版本构建**（未显式传 `-DataDir` / `-SourceRoot` 时自动读取 `STS2_DATA_DIR_V111_1` / `STS2_DATA_DIR_V113_1` 或隔离运行时，以及同级 `STS2-V111.1` / `STS2-V113.1` 源码树）：
  ```powershell
  .\scripts\build.ps1 -TargetVersion v113.1
  .\scripts\build.ps1 -TargetVersion v111.1 -ReusePck .\build\v113.1\STS2_Things.pck
  ```
- **全量双版本构建、探针回归与统一包生成**：
  ```powershell
  .\scripts\build-all.ps1
  ```
- **单独组装与校验统一包**：
  ```powershell
  .\scripts\build-unified.ps1
  .\scripts\verify-unified-package.ps1
  .\scripts\test-unified-startup.ps1 -TargetVersion v111.1
  .\scripts\test-unified-startup.ps1 -TargetVersion v113.1
  ```
