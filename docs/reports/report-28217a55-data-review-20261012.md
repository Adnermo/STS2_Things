# INCIDENT / #28217A55：仅上报数据审查

2026-10-12，按用户要求不启动游戏、不运行探针或实机测试。报告完整 ID：`28217a55-a20e-4635-82f3-25049c8c58e6`，描述“血蛭塞牌会卡住”。本轮只读取后台匿名报告及本地相关源码，没有修改正式代码、存档、安装、订阅或后台状态／备注。

最强线索是 **BetterCallSpire v1.0.0 的卡图恢复补丁访问已释放 TextureRect**，异常沿原生生成牌流程传回 `LeechMother.Brood`，随后敌方回合未完成。这与 #9F838F87 同属 PortraitPatch／失效控件问题，但出错模组及触发阶段不同；不能将两条当成同一个包，未做实机因果验证。

## 现场

- 游戏 v0.107.1、Things 1.25.18.0、schema 3。这是稳定版单人 Silent，不是上一条 v111 的观者＋骨妹多人开场。
- 深处第 6 层，总第 23 层，种子 D5PK7EN4LC，坐标 (6,5)，遭遇为 LEECH_MOTHER_ENCOUNTER。
- 第一回合已正常抽牌并完成多次出牌，包括杂技、逃脱计划、震荡波、刀扇与小刀。结束回合动作 18 和准备敌方回合动作 19 均记录 Finished。
- 血蛭母体处于 BROOD_MOVE，已有两个新召唤的小血蛭，最大生命 38／36，两者意图仍为 UNSET_MOVE。母体 49/89，敌方第 1 回合；玩家 34/74、手牌空，玩家阶段 None，动作禁用、队列暂停。未显示手牌选择窗口，场景不暂停、时间倍率 1。
- 结合原版敌方回合流程及本项目 Brood 代码，状态符合“召唤已完成，随后的寄生生成流程抛错，敌方行动没有正常返回”。队列空闲不能证明敌方异步任务仍在运行。

## 时间与异常关联

北京时间：

| 时间 | 记录 |
| --- | --- |
| 2026-10-12 00:31:14.267–.287 | 结束玩家回合和准备敌方回合动作完成 |
| 00:31:14.806 | 回合开始事件，快照侧别为 Enemy |
| 00:31:15.469 | `fault-6854e766-9`：NCard.EnterTree 中 BetterCallSpire.RestoreDefaultState 抛出已释放对象异常 |
| 00:31:15.471 | `fault-6854e766-10`：NCard.UpdateVisuals 中同补丁抛错，栈进入 LeechMother.Brood／ExecuteEnemyTurn |
| 00:31:15.482 | 最后保存的运行记录更新时间 |
| 00:32:46 | 新一次游戏启动产生模组加载类日志 |
| 00:33:44 | F2 上报，report_context 为 last_recorded_run_or_menu |

此次报告使用上一局保存的现场。异常缓冲 32 条中 28 条标为 from_previous_session；其中上述两条的 fault_id、时间和帧，与该局事件表最后两条 runtime_error 精确一致。因此虽然它们来自上次进程，仍是这次卡住场景的直接关联证据，不能因 previous_session 标签忽略。

其余旧的选牌／药水／节点移除异常和本次进程的模组加载日志，没有同样的时间、事件和调用链关联，不作为根因证据。报告还记录 dropped_runtime_errors=4，不能把错误列表当成完整原始日志。

关键调用链：

```text
System.ObjectDisposedException
Godot.GodotObject.GetPtr
Godot.TextureRect.SetTexture
BetterCallSpire.PortraitPatch.RestoreDefaultState
BetterCallSpire.PortraitPatch.TryReplacePortrait
BetterCallSpire.PortraitPatch.UpdateVisualsPostfix
NCard.UpdateVisuals
CardPileCmd.CreateCardNodeAndUpdateVisuals
CardPileCmd.AddGeneratedCardsToCombat
CardPileCmd.AddGeneratedCardToCombat
STS2_Things.Monsters.LeechMother.Brood
MonsterModel.PerformMove / Creature.TakeTurn
CombatManager.ExecuteEnemyTurn
... TaskHelper.LogTaskExceptions
```

本地源码 Brood 的顺序是唤醒／施放动画→召唤两只血蛭→通过原生 AddToCombatAndPreview 为玩家加两张寄生→等待动画结束。v107 的兼容调用进入上述原生生成接口。快照里两只新怪已存在且生成牌异常出现，支持故障位置在召唤后的塞牌步骤。

## 相关模组与限制

BetterCallSpire v1.0.0，工坊 ID `3747671229`，加载序号 15；程序集 MVID `1abc6b205f5a426794a66475c15a974d`。它与 #9F838F87 的本地“韩国机器人卡图”是不同程序集，二者都出现 PortraitPatch.RestoreDefaultState→TextureRect.SetTexture→ObjectDisposedException。

报告中 BetterCallSpire／Things 等文件指纹仍为 pending，不能据此确认本地包逐字节相同。原生战前检查点存在，native schema 16、单人、SHA256 `c8d32d7718b870eb0080e716ead947d80298e424a9683155edbe594e21be13c3`，属于这场战斗入场前，不是冻结时所有异步任务的保存。按本轮范围没有将检查点导入或启动游戏。

仅凭本条数据可判断：它是敌方母体塞牌期间发生的真实已释放卡面控件异常，根因方向高度指向 BetterCallSpire 的卡图生命周期补丁。仍需包代码审查或单变量对照才能确认其内部缓存键、对象复用方式及修复位置；尚未证明通过禁用它就一定恢复。

下一步最小验证可只关闭 BetterCallSpire 后读取同一局，或审查其 PortraitPatch 的 TextureRect 引用保存／释放处理。当前不建议改变血蛭召唤规则、全局吞掉异常或直接判为游戏版本／多人问题。

原始匿名导出及提取摘要在 `build/report-28217a55-20261012/`，没有发送联系玩家的消息，也没有启动任何游戏窗口。
