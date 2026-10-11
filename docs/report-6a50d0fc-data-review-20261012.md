# INCIDENT #6A50D0FC — 塞状态牌卡住的数据审查

2026-10-12。完整 ID `6a50d0fc-465f-4f59-80f0-4f062630e817`。本轮读取后台原始导出、核对异常与事件、取得对应工坊包并静态审查；没有启动游戏／原生探针，没有修改玩法、版本、安装或发布，也没有改后台报告状态。

后续经用户要求，已在 1.25.20 实现该卡图包兼容并完成双版本离线原生验证、安装；见[兼容交付](regent-portrait-compatibility-20261012.md)。下文保留最初数据审查时的结论与验证边界。

## 结论

有直接异常证据：**RegentCardsAnimeRework 的默认卡图恢复补丁报 `ObjectDisposedException`，中断洞胃第一回合的塞碎屑结算**。报告并非仅描述动画停住；调用栈包含实际 `CaveMaw.Offer`、原生造牌和敌方回合任务入口。

它与此前 BetterCallSpire 是同类缓存生命周期问题，但程序集不同。当前本机 1.25.19 的兼容类只检测 `BetterCallSpire`，**未覆盖本报告中的 `regentCardsAnimeRework`**。不能把刚完成的 BetterCallSpire 验证当作这个包已修复。

## 现场与时序

- 报告时间为 2026-10-11 15:32:50 北京时间；schema 3，`report_context=active_run`，没有重启历史异常标记。
- 游戏 v0.107.1，琐事 1.25.18.0。Steam 四人联机房主：骨妹、机器人、储君、静默猎手。A10，深处第 8 层／总第 25 层，种子 `8JTATW6SN9`，坐标 `(4,7)`，遭遇 `CAVE_MAW_LANTERN_ENCOUNTER`。
- 四名玩家的 `ReadyToBeginEnemyTurnAction` 均完成，最后一名在 15:31:51.327 完成。15:31:51.867 开始敌方回合。
- 15:31:52.774 的 fault `-54` 在卡牌进树时失败；15:31:52.778 的 fault `-55` 在刷新卡图时向外抛出，包含完整洞胃结算链。事件记录与异常缓存中的 fault ID、frames 精确一致。
- 房主停在敌方第 1 回合，洞胃 `OFFER_MOVE`，操作禁用、玩家队列暂停；结束按钮存在但禁用。网络连接正常且没有加载，场景未暂停、time scale=1，也没有等待玩家选牌。
- 玩家之后仍尝试操作，出牌队列预览与远端悬浮预览继续触发同模块错误。因此后续排队动作不应被当成这次卡住的起点。

关键调用栈：

```text
Godot.GodotObject.GetPtr → Godot.TextureRect.SetTexture
regentCardsAnimeRework.PortraitPatch.RestoreDefaultState
regentCardsAnimeRework.PortraitPatch.TryReplacePortrait
regentCardsAnimeRework.PortraitPatch.UpdateVisualsPostfix
NCard.UpdateVisuals → CardPileCmd.CreateCardNodeAndUpdateVisuals
CardPileCmd.AddGeneratedCardsToCombat
STS2_Things.Monsters.CaveMaw.Offer
MonsterModel.PerformMove → Creature.TakeTurn
CombatManager.ExecuteEnemyTurn → StartTurn
CombatManager.AfterAllPlayersReadyToBeginEnemyTurn
TaskHelper.LogTaskExceptions
```

洞胃的 `Offer` 通过原版 `CardPileCmd.AddGeneratedCardsToCombat` 为各目标生成两张带保留的原版 `Debris`；该 await 在本例抛错，未完成后续回位与敌方回合结算。不是从空手截图猜“未发牌”，也没有证据需要先重做怪物 Spine。

## 模块身份与代码

出错包是 [RegentCardsAnimeRework](https://steamcommunity.com/sharedfiles/filedetails/?id=3747626664)，不是同局另一个 `RegentFemPortraits`（显示名「储君卡图娘化」）。

- manifest ID：`RegentCardsAnimeRework`；报告 manifest version `unknown`，当前取得的 manifest `v1.0`。
- 程序集名称：`regentCardsAnimeRework`，报告程序集版本 1.0.0.0，MVID `7db9b2f8d3114673952daed6c90ca63c`。
- DLL：48640 字节，SHA256 `9d5e3e6ced203237d0b4a4a8e1de3e7f4a4ba6dadcc394b30c07585f9ae54f6d`。
- PCK：34682076 字节，SHA256 `6ed8e1f26502b9dcefa835e41e0925e8ef8a8fddfca9de038f434943239a4bc9`。

通过 Steam SDK 下载的两文件 SHA256 均与报告完整指纹一致，没有订阅变动或安装该卡图包。

实际 DLL 的 `PortraitPatch` 同样使用 `DefaultPortraitStates`／`DefaultAncientPortraitStates` 按卡牌类型保存首次贴图，`RememberDefaultState` 仅检查 null、键是否存在与替换图身份；`RestoreDefaultState` 直接把保存的 Texture2D 写回，没有 `GodotObject.IsInstanceValid` 检查。它额外保存 TextureFilter。默认缓存失效的静态代码与 BetterCallSpire 同构，后续适配应保留其过滤和换图行为。

该补丁作用于所有 NCard，不只储君卡牌：没有对应替换图的状态牌也会执行默认恢复。这解释了为什么洞胃／血蛭的塞牌、普通卡牌悬浮和联机队列预览可能触发同类错误；不能据此推断所有塞牌报告都只有一个原因。

## 其他错误与验证边界

最近 32 条异常里，23 条为该卡图包的 ObjectDisposedException，另外 5 条 LoggedError 的栈也经过同一卡图补丁；这些不是 28 次独立卡死，很多来自悬浮和队列预览。此前 48 条异常已被限额淘汰，早期事件也有丢弃。

另有 4 条 `ScriptManagerBridge.SwapGCHandleForType` 的 InvalidOperationException，在角色、能量 UI 和召唤物入场时出现。当前保留的数据不能定位是哪一个模组造成，也不能将它们直接归因于卡图缓存。第一轮后续出牌及四人结束确认确实完成，故不能用这些早于塞牌的错误代替本次直接中断栈。

原生四人战前检查点可用：native schema 16，SHA256 `86581cd6476bc302ab3311bba25f651b840bcc1fb4b233d2494b5db8a007c3de`。本轮没有恢复或运行它，也没有进行禁用卡图包的 A/B 测试；不声称已经实测整局恢复。

修复目标应是新增这个实际模块的默认贴图缓存适配，避免全局吞异常或跳过原生造牌。临时排查可只停用 **RegentCardsAnimeRework** 后重新进入同一战斗；未经执行不能称其已验证有效。

原始数据与审查证据位于 `build/report-6a50d0fc-20261012`：`report.json`、`findings.json`、`correlation.json`、工坊获取回执、`package-match.json` 及实际 PortraitPatch 的反编译结果。
