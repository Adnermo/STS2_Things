# STS2_Things 统一订阅包

当前统一包同时支持 **`v0.111.1`（Steam 正式版分支）** 与 **`v0.113.1`（Steam `public-beta` 测试分支）**。Steam 创意工坊单条目与 GitHub Release 共用同一套四文件，由统一引导按客户端真实运行时 API 自动选择对应实现：

```text
STS2_Things.json               统一清单（min_game_version=v0.111.1）
STS2_Things.dll                统一引导（STS2_Things.Bootstrap），内嵌 v111.1 与 v113.1 实现
STS2_Things.pck                两版共用 Godot 资源包
STS2_Things.BaseLibBridge.dll  可选设置库集成桥（无 BaseLib 时不加载）
```

嵌入资源契约：
- `STS2_Things.Implementations.v111.1.dll`（编译常量 `STS2_V111_1`，对应 `v0.111.1`）
- `STS2_Things.Implementations.v113.1.dll`（编译常量 `STS2_V113_1`，对应 `v0.113.1`）

## 启动流程

1. 原生 `ModManager` 读取 `STS2_Things.json`，挂载共用 `STS2_Things.pck` 并加载引导程序集 `STS2_Things.dll`。
2. `UnifiedBootstrap.DetectTarget` 依据已加载 `sts2.dll` 的真实方法与类型签名识别客户端目标：
   - 满足带 `CardModel` 来源参数的五参数死亡 Hook 与 `NStandardMerchantRoom` 等 `v0.113.1` ABI 时选择 `v113.1`；
   - 满足四参数死亡 Hook 与 `NMerchantRoom` 等 `v0.111.1` 正式版 ABI 时选择 `v111.1`；
   - 其他不兼容游戏版本立即返回明确的版本诊断信息。
3. 两版均直接调用官方 `ModManager.AssociateAssemblyWithMod` 将所选实现程序集关联至当前 Mod，并由原生 `ModelIdSerializationCache` 自动发现全部 `[SavedProperty]` 存档字段。
4. 调用所选实现的初始化入口完成模型池注册与 Harmony 补丁安装；未选中的另一套实现不解包、不加载、不扫描模型、不安装任何补丁。

## 构建与校验

```powershell
# 一键完成 v113.1（含共用 PCK）+ v111.1（复用 PCK）+ 双版本探针 + build\unified 组装校验
.\scripts\build-all.ps1

# 仅根据已构建的 build\v111.1 与 build\v113.1 重新打包并运行 UnifiedPackageProbe
.\scripts\build-unified.ps1

# 独立验证统一包资源完整性及两版原生启动探针
.\scripts\verify-unified-package.ps1
.\scripts\test-unified-startup.ps1 -TargetVersion v111.1
.\scripts\test-unified-startup.ps1 -TargetVersion v113.1
```

## 交付门禁

- `v111.1`、`v113.1` 两套实现与 `STS2_Things.Bootstrap` 版本号完全一致，开启 `Nullable=enable` 与 `TreatWarningsAsErrors=true` 零警告零错误编译。
- 分别针对 `v0.111.1` 与 `v0.113.1` 真实运行库通过 Harmony 目标签名探针、`[SavedProperty]` 序列化契约与 `ModelId` 命名空间探针。
- 引导 DLL 内嵌的两个资源流与 `build\v111.1\STS2_Things.dll`、`build\v113.1\STS2_Things.dll` 的 SHA256 逐字节一致，且两版导出的 Mod 模型集合完全相同。
- 两版分别挂载各自匹配的原生 `SlayTheSpire2.pck` 与共用 `STS2_Things.pck` 通过原生启动、深处章节、事件与怪物行为回归。
- 最终压缩包仅包含上述四运行文件，严禁混入调试桥、原版客户端/运行库、源美术资产、源码、临时探针或个人配置。

所有联机玩家须运行同一游戏版本并安装同一统一包。双版本编译常量与适配细节见 [双版本支持](DUAL_VERSION_SUPPORT.md)。
