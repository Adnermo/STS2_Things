# STS2_Things 双版本支持

## 当前目标

| 实现目标 | 实际游戏版本与分支 | 编译常量 | 引导内嵌资源 | 目标清单 |
| --- | --- | --- | --- | --- |
| `v111.1` | `v0.111.0 / v0.111.1`，Steam 正式版分支（official） | `STS2_V111_1` | `STS2_Things.Implementations.v111.1.dll` | `manifests/v111.1/STS2_Things.json` |
| `v114.0` | `v0.114.0`，Steam 最新测试分支（public-beta） | `STS2_V113_1;STS2_V114_0` | `STS2_Things.Implementations.v114.0.dll` | `manifests/v114.0/STS2_Things.json` |

双版本基线已由旧 `v111.1 + v113.1` 迁移至 **`v111.1 + v114.0`**。统一清单 `STS2_Things.json` 的 `min_game_version` 保持为 `v0.111.0`；`v0.107.1`、`v0.110.1`、`v0.113.0`、`v0.113.1` 等历史参考目标不再作为当前编译目标。

对外交付保持同一套四文件（`STS2_Things.json`、统一引导 `STS2_Things.dll`、共用 `STS2_Things.pck`、可选 `STS2_Things.BaseLibBridge.dll`）。两套实现程序集内部名均为 `STS2_Things`，每个游戏进程仅解包并加载匹配当前客户端的一套实现。联机玩家须使用相同游戏版本与同一统一包。

## 原生 API 适配（`STS2_V111_1` / `STS2_V114_0`）

- **`v0.114.0` 增量变更适配**：
  - `ArrowSlot.DestroyAfterImpaled` 与 `ArrowStatus.Destroyed` 已随 `QuietusPower` 重做被移除；`Sts2VersionSeam.RemoveCreatureWithoutDeathOrEscape` 与 `FoolArrowTargetabilityPatch` 直接回收箭矢，彻底修复墓潮蛞蝓吞噬尸体、异彩史莱姆分裂及愚者箭矢时的 `MissingMethodException`。
  - `CardModel.UpgradeInternal` 签名变为 `UpgradeInternal(bool silent = false)`、`DowngradeInternal` 变为 `DowngradeInternal(int downgradeAmount = 1)`、`Creature.DamageBlockInternal` / `LoseHpInternal` 增加 `dryRun` 预览参数，通过独立面向 `v0.114.0` `sts2.dll` 编译保持零 IL 签名错配。
  - `DreadRunModel` 将 `ApplyCurrentDominion()` 迁移至 `BeforeActEntered()`，本模组 `EncounterModel.GetDominion` 后置补丁无感兼容。
- **程序集关联与存档属性缓存**：`v111.1` 与 `v114.0` 均原生支持官方 `ModManager.AssociateAssemblyWithMod`，并由 `ModelIdSerializationCache` 自动扫描关联程序集中的 `[SavedProperty]`。
- **战斗单位节点解析**：两版统一通过 `NCombatRoom.Instance?.GetCreatureNode(creature)` 获取战斗场景单位节点。
- **死亡回调与生命上限损失**：`v111.1` 使用四参数死亡 Hook 与 `isFromCard: false`；`v114.0` 使用包含 `CardModel?` 来源的五参数死亡 Hook 与 `cardSource: null`。
- **巨岩（CaveGod）交互与章节房间生成**：按两版各自 `NCreature` 可交互接口与 `ActModel.GenerateRooms` 重载签名条件编译。
- **商人猜拳、联机大厅与 F2 反馈**：分别按 `v111.1` 与 `v114.0` 的 `MerchantSynchronizer`、`NMerchantRoom`/`NStandardMerchantRoom`、`StartRunLobby`/`JoinFlow` 及 `FeedbackLogFiles` 签名条件编译。
- **隔离探针**：`tools/ProbeSupport/ProbeRuntimeApi.cs` 统一封装两版开局、进阶、选牌、死亡和生命周期差异，仅用于验证探针，不进入发布包。

## 引导判定（`DetectTarget`）与注册

1. `UnifiedBootstrap.DetectTarget` 在进程内检查当前已加载 `sts2.dll` 的真实运行时签名：
   - 当具备现代五参数死亡 Hook、`NStandardMerchantRoom`、`BeforeAnimPlayedInCompendium` 与 `v0.114.0` 指纹（`BeforeActEntered`、`CardModel.UpgradeInternal(bool)` 且移除 `ArrowSlot.DestroyAfterImpaled`）时，判定为 `v114.0`；
   - 当具备 `v0.111.0 / v0.111.1` 正式版签名（六参数伤害 Hook、`CombatId`、无 `CardModel` 参数的四参数死亡 Hook 及传统 `NMerchantRoom`）时，判定为 `v111.1`；
   - 其余客户端 ABI 直接抛出清晰的版本范围诊断，绝不误加载不匹配实现。
2. 选定目标后，引导从嵌入资源读取对应 `STS2_Things.Implementations.<target>.dll`，调用 `ModManager.AssociateAssemblyWithMod` 完成官方模型、本地化与 `[SavedProperty]` 归属注册，再触发所选实现的初始化入口。

## 构建与验证命令

- **源码与契约静态审查**：
  ```powershell
  python .\scripts\verify_project.py
  ```
- **单版本构建**（未显式传 `-DataDir` / `-SourceRoot` 时自动读取 `STS2_DATA_DIR_V111_1` / `STS2_DATA_DIR_V114_0` 或隔离运行时，以及同级 `STS2-V111.1` / `STS2-V114.0` 源码树）：
  ```powershell
  .\scripts\build.ps1 -TargetVersion v114.0
  .\scripts\build.ps1 -TargetVersion v111.1 -ReusePck .\build\v114.0\STS2_Things.pck
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
  .\scripts\test-unified-startup.ps1 -TargetVersion v114.0
  ```
