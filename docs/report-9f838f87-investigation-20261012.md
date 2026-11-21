# INCIDENT / #9F838F87 血蛭多人开场卡住

2026-10-12。完整报告 ID `9f838f87-c110-4b09-94c6-a4be2da405f9`，描述“测试版多人吸血血至开局卡”。本轮没有修改后台状态或发布修复。

本条已有明确异常位置：生成寄生状态牌的原生卡牌流程，调用本地“韩国机器人卡图”模组的 `Mechaneko_Defect_Card_Art.PortraitPatch`，在恢复卡面时抛出 `ObjectDisposedException`。本地使用原生检查点、相同四个核心构件和真实双进程 ENet 联机，未加载该卡图包时开场正常。原卡图包无法取得，因此不宣称已完成启用／禁用因果对照，也不宣称修复或完整重现了其全部 30 个模组环境。

## 原报告证据

- 游戏 v0.111.0、Things 1.25.18.0、schema 3。上报方为 Steam 客机，`local_player_index=1`，两名玩家为观者／骨妹。
- 深处第 6 层，总第 22 层，坐标 (2,5)，种子 V9QP4E2QL5Z3。三只吸血血蛭的最大生命依次 81、86、91，起手 SIP／INFEST／CURL。
- 两名玩家停留在 Start。观者模型已有奇迹、3 张寄生和 5 张普通牌，共 9 张；骨妹只有 1 张寄生，原 26 张牌仍在抽牌堆。客机手牌控件为 0，结束按钮存在但禁用，场景未暂停、时间倍率 1、无执行中的出牌动作。
- 日志在开场造牌处反复记录同一异常，玩家重新加载后依旧出现。不是将普通等待超时或战士消耗手牌误判为开场卡死。

关联调用链中的关键帧：

```text
System.ObjectDisposedException
Godot.GodotObject.GetPtr
Godot.TextureRect.SetTexture
Mechaneko_Defect_Card_Art.PortraitPatch.RestoreDefaultState
Mechaneko_Defect_Card_Art.PortraitPatch.TryReplacePortrait
Mechaneko_Defect_Card_Art.PortraitPatch.UpdateVisualsPostfix
MegaCrit.Sts2.Core.Nodes.Cards.NCard.UpdateVisuals_Patch8
MegaCrit.Sts2.Core.Commands.CardPileCmd.CreateCardNodeAndUpdateVisuals
... CardPileCmd.AddGeneratedCardToCombat
STS2_Things.Monsters.SanguineLeech.BeforeHandDraw
... Hook.BeforeHandDraw / CombatManager.SetupPlayerTurn
```

同一模组的 `NCard._EnterTree` 补丁也记录了恢复卡面控件时的已释放对象异常。完整结构化异常及时间在原始匿名报告中保留。栈明确指出抛错位置及血蛭触发的生成链；基于这些记录，第三方卡图补丁阻断开场流程是目前最强的原因线索。没有用无依据的异常吞掉补丁绕过流程。

“韩国机器人卡图”在报告里是 ModsDirectory 本地包，工坊 ID 为 null，程序集 MVID `5036fe2544bb45a2b7a56811fa021dfe`。

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| 韩国机器人卡图.dll | 48128 | eab0432d3779599723d340381780a52f97c1d58e93e4028026a770ea91fc3435 |
| 韩国机器人卡图.pck | 9316052 | 1b4faba6e61e6e7aa8e347bc4048538e0145a63b8acb2c9baf546693869ece4e |

本机安装／工坊缓存／工作区未找到该包，按程序集及名称搜索也未取得可核验的原包。用户确认手边没有该包或链接。不能用相似的机器人皮肤或其他卡图包替代，不能制造模拟第三方异常来声称已复现。

## 本轮实际测试

从后台下载原生检查点，验证压缩数据 SHA256 `b7887a803374f22341f3e0e096571938759fbb37cad5df0ccb9dcbf7faa24480`；解包结果与后台独立检查点下载逐字节一致。此次无需重建牌组、地图或 RNG 计数。

使用两个全新 Workbench 私有客户端：Host `3bb330952307`、Client `dca21f518fdc`。原生 ENet 连接匿名玩家 1／2、LoadRunLobby、SetUpSavedMultiplayer 和 NGame.LoadRun 直接读取检查点；没有调试替换遭遇，没有覆盖敌人 HP，没有向手牌注入测试卡或补画手牌节点。

加载 Things、Boninall Watcher、BaseLib、RitsuLib，加仅独立目录允许的固定集成测试驱动和标准只读联机桥接。上述四个核心模组所有已报告的 DLL／PCK SHA256 均与报告匹配。仍未还原其余第三方包和 Steam P2P 传输，因此只报告这个控制环境的真实结果。

| 检查项 | 房主 | 客机 |
| --- | --- | --- |
| 原生网络角色 | Host，已连接 | Client，已连接 |
| 本地玩家 | 观者 | 骨妹 |
| 起手寄生数 | 3 | 3 |
| 模型手牌／实际控件 | 9／9 | 8／8 |
| 普通牌抽出 | 5 | 5 |
| 玩家阶段 | Play | Play |
| 结束按钮 | 可见、启用 | 可见、启用 |
| 当前动作／队列 | 无执行动作，队列空闲 | 同左 |

观者的 9 张牌及顺序与上报模型一致，骨妹则完成原报告未完成的其余寄生生成和五张普通牌抽取。三只血蛭的最大生命及起手意图与原报告一致，界面实际截图已查看。因观者只有 1 点生命，本轮目标限定开场恢复，没有为测试下一轮注入格挡或更改生命。

复核脚本 `build/report-9f838f87-20261012/verify.py` 实际通过检查点完整性、四个核心包身份、网络角色、遭遇、三只敌人生命、寄生数、玩家阶段、手牌／节点匹配和按钮状态。无需依赖“没有崩溃”这种弱信号。

## 可用结论和后续

血蛭在该检查点的基础联机环境能正常完成开场；上报现场有第三方卡图的真实已释放对象异常。最直接的现场验证是让持有原包的玩家临时禁用“韩国机器人卡图”，再重新载入同一局，比较骨妹是否获得 3 张寄生＋5 张普通牌以及结束按钮能否使用。应单独禁用该卡图包，不能将它与 `Mechaneko_Defect` 角色皮肤或其他卡图模组混为一项。

如果该验证恢复，可将原包及完整异常转交其维护者处理 `PortraitPatch` 的卡面控件生命周期。未经原包启用对照，本轮不制作全局关闭第三方补丁的正式兼容补丁，也不改血蛭正常的原生生成机制。

全部证据位于 `build/report-9f838f87-20261012/`：匿名原报告、原生检查点／解包保存、包匹配、两端日志、开场状态、报告缩略图和本地截图、驱动源码、`verification.json`。两独立游戏停止，临时测试驱动在私有设置禁用；用户原游戏及存档、订阅保持原样。其他 SoulRoes 未提交改动保留，未编入正式包。
