# STS2_Things 统一订阅包

1.25.27 支持 **v0.107.1 / v0.113.0 public-beta**。Steam 单条目仍使用同一套四文件，由引导按真实 API 选实现：

```text
STS2_Things.json               min_game_version=v0.107.1
STS2_Things.dll                引导，内嵌 v107.1 与 v113 实现
STS2_Things.pck                两版共用资源
STS2_Things.BaseLibBridge.dll  可选设置库集成桥
```

## 启动

1. 原生 ModManager 加载统一 DLL/PCK。
2. 五参数伤害 Hook 选择 v107.1；六参数伤害 Hook、CombatId、带 CardModel 来源的五参数死亡 Hook、NStandardMerchantRoom 同时成立时选择 v113。其他现代 ABI 返回明确的版本诊断。
3. v113 使用官方 `AssociateAssemblyWithMod`；v107.1 保留类型追加及 assembly 提升桥。
4. 调用所选实现的 initializer。未选实现不加载、不扫描模型、不安装补丁。

## 交付门禁

- 两实现及引导版本一致，编译零警告零错误；分别验证 Harmony 和 SavedProperty 合同。
- 固定的两个嵌入资源与独立 DLL 的 SHA256 一致，Mod 模型集合相同（本次各 160 个）。
- 两版原生加载、深处事件、涅奥及章节推进检查；新版完整客户端烟雾验证。
- 共用 PCK 挂载核验；ZIP CRC、四文件条目与 SHA256 一致。
- 仅交付四运行文件，不含调试桥、原版客户端/依赖、原画、源码、日志或凭据。

1.25.27 的 Steam 与 GitHub 使用同一套已验证运行文件。所有联机玩家需同一游戏版本及统一包。详见 [双版本实现](DUAL_VERSION_SUPPORT.md) 和 [V113 迁移](MIGRATION_V113.md)。
