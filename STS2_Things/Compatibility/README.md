# 兼容分区

本体游戏版本适配保留在 `Sts2VersionCompatibility.cs`。第三方模组兼容统一从 `ModCompatibility.Initialize()` 接入；玩法代码只调用这个入口，不感知第三方类型。

```text
Compatibility/
  ModCompatibility.cs              检测、加载时序、幂等、按适配隔离失败
  Mods/KnownModAdapters.cs           明确的模组注册表
  Internal/PortraitCacheGuard.cs     已核实卡图缓存合同的共享实现
  Sts2VersionCompatibility.cs        游戏版本适配
```

目前注册 BetterCallSpire、RegentCardsAnimeRework、LuolinCard、CuteNecrobinderCards 的卡图适配，以及 MomoLib 的本模组 WAV 音乐适配；未安装对应模组时不打补丁，不新增强制依赖。配置页库的可选注册仍由 `Config/LibraryIntegration.cs` 管理，不混入玩法兼容。

新增第三方兼容时：

1. 取得实际包，与报告的程序集／文件哈希对照，确认支持版本及具体失败链。
2. 在 `Mods/` 增加独立适配或共享实现的明确注册项，区分 manifest ID、程序集名称和类型命名空间。不要按含糊名称扫描并修改未知模组。
3. 由统一入口处理加载先后与重复初始化。每项使用独立 Harmony ID；形状不符时跳过该项，不撤除其他适配。
4. 玩法继续走原生命令；只修已证实的缺口，保留第三方正常功能。共享反射信息按目标方法／程序集保存。
5. 根据支持版本验证未安装、单独安装、组合安装、失效引用与正常显示，记录真实验证层级。

当前证据和限制见 `docs/mod-compatibility-20261012.md` 和 `docs/latest-reports-20261013.md`。这里是扩展方式，不代表所有卡图包或所有第三方模组已兼容。
