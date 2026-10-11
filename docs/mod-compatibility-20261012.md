# 独立兼容分区与 LuolinCard — 1.25.21

2026-10-12。整合此前未发布的 BetterCallSpire／RegentCardsAnimeRework 修复，新增洛琳卡图 mod。用户要求兼容独立成区，后续可继续增加模组；此前 Steam 更新授权及同步 GitHub Release 要求继续执行。

## 分区

- `Compatibility/ModCompatibility.cs` 是单一第三方启动入口：检测已加载与后加载程序集、幂等注册、各项独立 Harmony ID 与失败回滚。
- `Compatibility/Mods/KnownModAdapters.cs` 是明确注册表；目前为 BetterCallSpire、regentCardsAnimeRework、LuolinCard。
- `Compatibility/Internal/PortraitCacheGuard.cs` 保存共用缓存合同与按目标方法分开的反射信息。
- 游戏版本兼容保留在 `Sts2VersionCompatibility.cs`；配置页库接入保留在 Config。

扩展方法、注册条件和验证要求见[兼容目录说明](../STS2_Things/Compatibility/README.md)。不扫描未知模组并盲目修改，不增加必需前置；适配失败不撤除其他模块的补丁。

## 修复与原始证据

三款卡图补丁把默认 Texture2D 托管引用缓存在字典里，原生资源卸载后引用非 null，但 Godot 对象已失效。恢复默认卡图时抛错，中断原生造牌。适配只保护其 Remember／Restore 两个私有缓存入口：保留当前已加载的原生卡图，删除失效旧条目，仍恢复合法伸展方式及可选过滤设置。正常换图、随机／动画逻辑继续由原包执行。

实际 LuolinCard 工坊 3758401395／0.5.0 的 DLL、PCK 哈希与 #B0A19A28 完整指纹一致。未修复的 1.25.20 在真实 NCard、节点池和 AssetCache 卸载后复现 `LuolinCard.PortraitPatch.RestoreDefaultState → NCard.UpdateVisuals` 的 ObjectDisposedException。对应其他两个包的原始失败证据保留在此前制作记录中。

没有更改怪物数值、寄生／碎屑数量、原版 CardPileCmd、网络状态或 RNG，不全局吞异常，不随包分发第三方美化资源。

## 验证

真实 v107.1／59260271 与 v111／41cef1ea 的 DLL、依赖和 PCK；每版三个包各自先／后加载，及 Luolin→Regent→Better、Better→Regent→Luolin 的组合先／后加载，共 20 组、848 项检查通过，无引擎 ERROR。

覆盖失效纹理／控件、合法空图、伸展／过滤、重复初始化、各私有缓存类型隔离、真实池化寄生牌、带保留的原版 Debris、正常卡图替换与跨牌型复用。Luolin 当前实际配置中的动画／随机记录数均为 0；额外以测试专用记录和原包真实图片调用原模块 Bind／Tick／Unbind 与顺序换图方法，验证切换到不同图片及解除绑定，不把这些夹具称为工坊自带动画。

最终统一包的两目标内嵌实现 SHA256、159 模型集合、ModManager 启动、涅奥领取／恢复与深处事件池回归通过。PCK、BaseLib 桥复用 1.25.20 已验证资源，包含 F2 日志脚本补包。四文件本机安装与 ZIP 条目哈希一致。

本轮没有启动完整游戏或 Steam 多人客户端，没有恢复 #B0A19A28 的完整战斗；没有宣布暂停模组的旧 GetRunSavePath 接口错误、其他 ScriptManager 错误或缺包 Mechaneko_Defect_Card_Art 已修复。

## 源码与交付

构建源以已发布 1.25.18／63a02e4 为基线，244 个原有 C# 文件不变；新分区、初始化调用及版本提示与对应提交一致。工作区 SoulRoes 的另一个任务改动保留，不进入此包。

证据：`build/mod-compatibility-20261012` 的原始失败、20 组验证、双版本启动、安装回执、源码冻结与 delivery。统一 ZIP、SHA256SUMS、双语说明位于 `dist/v1.25.21`。发布结果另存该目录下 `publication/publication-report.json`，必须双平台核实后才算发布完成。
