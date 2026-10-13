# 最新报告审查与 1.25.22 修复 — 2026-10-13

后台共 27 条报告，本轮检查最新 7 条 1.25.21 报告；发布前刷新列表，没有新增报告。没有改变后台状态，也没有启动完整游戏或 Steam 多人客户端。取得实际第三方包和原版运行文件后，重现并修复四处问题，纳入 1.25.22。用户已要求同步发布 Steam 与 GitHub。

| 报告 | 结论与处理 |
| --- | --- |
| C73FBDB9：多人天际钻头打晶壳蜗牛掉线 | `Mechaneko_Defect_Card_Art.PortraitPatch.RestoreDefaultState` 的失效卡图异常，包括破壳晶片预览路径；报告与之前缺失的手动卡图包是同一程序集。原包仍不可获取，不能以别的皮肤代替或宣布已验证兼容。 |
| 7D32C86E：墓潮蛞蝓／升级两张牌后卡住 | 实际最新失败在 `MomoLib.Patch.MomoCustomBgmPatch.Prefix`，它把蛞蝓的 WAV 当作 MP3 加载，InvalidCastException 中断 StartCombat。已取得同版包并重现、修复；不是仅凭空手截图推断升级卡牌失败。 |
| C7880A7C：利齿之眼 SL 后血变多 | 现场为始源雾菇、眼睛 9/9，尚不足以证明异常血量累积。该场另外有本模组背景的节点缺失／NullReferenceException，已修背景错误；未随意重置眼睛数值或宣布血量反馈已解决。 |
| F0E07340：牌打不出去 | 经典模式卡牌 `Bash_C`／`Clash_C` 调旧版 `AttackCommand.FromCard(CardModel)`，`TrueGrit_C` 调旧返回类型的 `CardCmd.Exhaust`，在 v111 报缺失方法。新下载的同号工坊包与报告 DLL 不同，已直接使用两参数 FromCard 和新的 Exhaust 返回类型。旧 DLL 暂无原包，不通过吞异常将失败出牌当成功。 |
| 4C71744A：活体巨岩与储君星尘索敌 | 报告的最新状态已是战士开局事件，无法还原当时星数和目标序列。但离线原生战斗重现了同类缺陷：随机攻击只筛选 IsAlive，会命中仍存活的埋藏核心／倒地或恢复中的手臂。已修正本 Boss 的随机目标筛选，并执行真实 Stardust OnPlay 验证。 |
| FF82E3DB：多人塞牌时一人卡死 | 洞胃 Offer 的原生碎屑生成被 `cutenecrobinderv0_7.PortraitPatch.RestoreDefaultState` 打断。取得原工坊 DLL/PCK，哈希与报告一致；已新增 CuteNecrobinderCards 兼容。另有 Orchis 皮肤的遗物／VFX 错误，不混为此次缓存修复。 |
| 7CD9F950：寄生悬浮、无手牌 | 现场属于重启保留状态，异常均来自旧运行，其中有已在 1.25.21 适配的 Regent 卡图包；缺少当前构建发生时的直接异常，不据旧日志宣布新版兼容失效。 |

## 实际修复

1. `NThingsCombatBackground._Ready` 按真实模板处理可选 Layer_01／Foreground。始源雾菇只有 Layer_00 与 Foreground，盛碗虫族母只有 Layer_00；原代码无条件读不存在节点，原生 PCK 中可稳定复现。保留已有层的 ZIndex，不增加空图层或更改贴图。
2. `MomoLibCompatibility` 只在本模组墓潮蛞蝓的确切 WAV 路径上，把 MomoLib 的 MP3 专用前缀交回既有琐事／原生音乐流程。先调用其原有 StopBgm 清理上一首，保留其他模组音乐和原版 event 音乐行为；不改其他 res:// 路径。
3. 注册 manifest `cutenecrobinder-cards` 对应的程序集 `cutenecrobinderv0.7`、命名空间 `cutenecrobinderv0_7`，复用已核实的缓存适配。与 LuolinCard 同时加载时各私有类型独立，不改正常换图。
4. `CaveGodRandomTargetPatch` 在原版 `AttackCommand.GetPossibleTargets` 上增加窄后置补丁：仅玩家侧随机攻击排除本模组巨岩中不可击中的部位。每段由原版重新选目标，断臂后立即纳入刚暴露的核心；保留候选顺序与原生 RNG。普通眩晕的健康手臂仍可被打，单体指定和群攻的原版候选列表保持不变，不修改其他怪物。

Cute 包：工坊 3758862593／0.0.7，DLL SHA256 `8f0927845448354edff266a4c9a20937051d6e38dc7c6966991ad857203347b8`，PCK `47501971b9935e8386fd1b54a2349867a2593b98580f7b1fa486d4b30977bea0`；与报告一致。MomoLib 为工坊 3753137192／1.3.7，实际前缀明确使用 `ResourceLoader.Load<AudioStreamMP3>`。

ClassicMode：工坊 3749025602／0.1.20。报告 DLL 为 501248 字节，SHA256 `0eb89cdbf5464199462c8167c45914efbdb9a345ac632046ef612d1c5e20cab8`；当前下载 DLL 为 501760 字节，SHA256 `7e1b74551c1f5e820e343e23b65471bfa9406494fe30ee2ef3bea894efcd3e6b`，PCK 相同。当前 DLL 中 Bash_C／Clash_C 调 `FromCard(card, cardPlay)`，TrueGrit_C 使用 `TaskAwaiter<CardPileAddResult?>`；不能把这个新包冒充报告旧包的修复前后验证。受影响玩家须使用匹配其游戏分支的更新包；本模组没有增加未经验证的旧接口适配。

## 验证与交付

- 修改前，实际 1.25.21/PCK 中背景 _Ready 重现节点缺失和 NullReference；真实 Momo 前缀加载原 WAV 重现 InvalidCast；真实 Cute 卡图在原生节点池／资源卸载后重现 ObjectDisposed。日志保留。
- 两真实目标 v107.1 与 v111 的背景／音乐探针各 14 项通过，包括四类原生背景模板、前一首 Momo 音乐停止和清理、原版音乐路径不变。
- Cute＋Luolin 先／后加载：两版本各两组 39 项通过；Cute 单独加载的正常卡图与节点复用：两版本各 30 项通过。
- 巨岩随机攻击：修改前 v111 的固定轨迹中，24 次随机攻击有 7 次命中埋藏核心。两版本修复后均通过原生命令与真实 Stardust OnPlay 检查，覆盖 1／2／4 人同进程模型、埋藏、暴露、倒地、恢复、普通眩晕，以及连续六击途中断臂后的目标更新。指定单体与群攻的候选列表也作了原版对照。不是 Steam 多机联机验证。
- 最终统一包双版本加载、涅奥／深处事件池回归通过。仅离线原生 Godot 检查，不称整场通关或真实多人验证。

工作源码以 1.25.21 冻结源为基线，排除工作区原有 SoulRoes 的另一个任务改动；资源包及 BaseLib 桥保持已验证版本一致。证据与安装／打包记录在 `build/latest-reports-20261013`，报告原始导出不进入发布包。

发布文件为 `dist/v1.25.22/STS2_Things-1.25.22-unified.zip`、`SHA256SUMS.txt` 与中英更新说明。加入随机索敌修复前的三项修复候选包单独保留在构建目录，不覆盖唯一回滚材料。发布核对源码索引与冻结构建源、ZIP CRC／条目哈希、Steam 无缓存完整详情和 GitHub 附件哈希；最终回执位于本轮构建目录的 `publication/publication-report.json`。
