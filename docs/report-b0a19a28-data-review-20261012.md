# INCIDENT #B0A19A28 — 画面卡死的数据审查

2026-10-12。报告 `b0a19a28-cfc7-4efe-b28d-88a2df2d0467`。用户在要求推送更新后追加“先调查一下这个”，本轮优先调查；未启动游戏／探针、未修改玩法或安装、未更新后台报告状态，也未执行 Steam／GitHub 发布。

后续用户要求兼容及独立分区，已在 1.25.21 实现 LuolinCard 适配并完成双版本离线原生验证，见[兼容制作记录](mod-compatibility-20261012.md)。下文保留当时的数据审查结论。

## 直接线索

**洛琳卡图 mod 的 `LuolinCard.PortraitPatch.RestoreDefaultState` 报错，打断血蛭母体塞寄生牌的敌方回合。** 最后一次现场的异常与事件 ID、方法栈精确一致，有本次运行证据，不能只因为报告也保留了重启前的同款异常而将它们全部视为无关历史。

```text
Godot.GodotObject.GetPtr → TextureRect.SetTexture
LuolinCard.PortraitPatch.RestoreDefaultState
LuolinCard.PortraitPatch.TryReplacePortrait / UpdateVisualsPostfix
NCard.UpdateVisuals → CardPileCmd.CreateCardNodeAndUpdateVisuals
CardPileCmd.AddGeneratedCardToCombat
STS2_Things.Monsters.LeechMother.Brood
MonsterModel.PerformMove → Creature.TakeTurn
CombatManager.ExecuteEnemyTurn
STS2QuickAnimationMode.Utils.SpeedManager.TrackAsyncCore
CombatManager.StartTurn
```

这是卡图恢复层的直接失败栈；加速模块在外层等待链出现，不足以据此认定它是失效贴图的来源。

## 现场

- v0.111.0，琐事 1.25.18.0，schema 3，`active_run`。单人战士，A20，深处第 13 层／总第 38 层，种子 `WPBYX9CQA56Y`，坐标 `(0,12)`，`LEECH_MOTHER_ENCOUNTER`。
- 2026-10-12 00:39:46 北京时间再次恢复战斗；随后玩家成功打出放血、使用药水、打出剑柄打击和旋风斩。结束回合 action 4 与敌方回合确认 action 5 均完成。
- 00:40:14.037 敌方回合开始，00:40:14.534／.538／.542 的 faults `fault-a718c88e-18/-19/-20` 又出现卡图异常。`-19` 包含完整母体 Brood 链；它的 event 与 runtime buffer 方法栈完全一致。
- 母体已召唤两小血蛭，37／38 生命、`UNSET_MOVE`；母体停在 `BROOD_MOVE`。玩家手牌模型只有 1 张寄生牌，实际 UI holder 为 0；原本两张寄生牌的交付没完成。
- 敌方第 1 回合、操作禁用、队列暂停，结束按钮存在但禁用。场景未暂停、time scale=1，网络 connected、不 loading，无卡牌选择，不能用“开了暂停”解释当前卡住。

此前相同会话中的多次恢复也记录了同款失败，符合玩家反复 SL 后仍卡住的现象。最近 32 条记录中 24 条经过 LuolinCard 卡图补丁，18 条属于当前会话；这些不代表 24 次独立卡死。另有 20 条更早异常已被限额淘汰。

## 模块身份与实际源码

出错的是 [洛琳卡图 mod](https://steamcommunity.com/sharedfiles/filedetails/?id=3758401395)：manifest ID「洛琳卡图mod」、版本 0.5.0，程序集 `LuolinCard` 1.0.0.0，报告 MVID `cb968f062d414e68846fd565f4a1042d`。不是同局 `Luoling` 角色包，也不是「MechaNeko Defect Skin」。

取得的工坊包与玩家报告的两个文件哈希均一致，无订阅变动、没有安装该包：

| 文件 | 字节 | SHA256 |
| --- | --- | --- |
| 洛琳卡图mod.dll | 78848 | e3b7e76e46944471934f258d13a2bf88efe661504f227b2d4aaa0904e3f226b3 |
| 洛琳卡图mod.pck | 139084436 | cdc37b165210efbc6271e6497cc91a209c97f1e1660515042b127e6c675fd4e0 |

实际 `PortraitPatch` 也用 `DefaultPortraitStates`／`DefaultAncientPortraitStates` 缓存首张默认 Texture2D，直接写回，没有检查 Godot 对象有效性。同类型默认贴图生命周期缺口与前两包一致。

该版本还有动画替换、随机替图、动态目标 Bind/Unbind 和 `ClearGeneratedTextureIfUnrestored`。后续适配不能简单撤掉整套卡图补丁，须保留这些行为并核对清除失效默认缓存后的显示。

**当前本机 1.25.20 只适配 BetterCallSpire 与 regentCardsAnimeRework，未涵盖 LuolinCard。** 本轮没有把静态审查称为已修复或已验证 SL 恢复。

## 另一个明确问题

`sts2AllowUserPause_v0.107`（工坊 3747572624，报告版本 v0.107.0）的 `DeathGraceController.RestoreFloorStartSaveBeforeMenuReturn` 报缺失方法：

```text
RunSaveManager.GetRunSavePath(Int32, String)
```

实际 v111 API 是 `GetRunSavePath(int, string, bool? forceModState = null)`。第三个参数即使可选，也不保留旧二参数的二进制入口，因此当前报告的暂停／回退包有明确接口不兼容证据。此错误发生在 00:39:19 返回／恢复阶段，早于最后一次 00:39:46 战斗入场；不替代 00:40:14 的 LuolinCard 直接中断栈。

旧会话还记录 texture_create/texture_2d_initialize、退出时 Sentry/NMuteInBackgroundHandler 的错误；当前会话另有 ScriptManagerBridge.UpdateScriptClassInfo 报错。保留信息不足以给它们确定原因，不把所有错误归成一个问题。

## 边界与后续

原生战前检查点可用，native schema 20，SHA256 `daef9eedabbb47d9326f66a581e9c4f6319a44620cb61cc3210c87824e474211`。本轮未导入或执行，没有禁用卡图包的 A/B 对照。

修复重点应是 LuolinCard 的默认贴图恢复适配；临时核对可只禁用「洛琳卡图mod」后重新进入战斗。暂停包应选择适用于 v111 的版本或停用其回退功能，但本轮没有更改玩家模组环境。

证据在 `build/report-b0a19a28-20261012`：原始导出、摘要、当前会话关联、实际工坊回执、精确文件哈希比较及真实 PortraitPatch 反编译。发布请求仍有授权，本轮按“先调查”尚未执行推送；1.25.20 包和校验材料保持不变。
