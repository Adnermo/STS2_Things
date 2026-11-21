# 尖塔：琐事 1.25.18 — F2 上报与现场还原

F2 上报会自动附上更多诊断资料，玩家仍只需简单说说遇到了什么问题。

- 捕获开战任务、原版日志和 Godot 引擎异常，保留方法调用栈；最近的异常重启后仍可附上。
- 记录实际加载模组的名称、版本、加载顺序、工坊 ID、程序集与文件指纹，以及琐事生效配置。
- 补充联机角色、动作队列、手牌内部状态、结束回合按钮和界面暂停信息。
- 附上游戏截图，以及包含完整 RNG 的匿名原版战前存档；开发者可从后台下载并重开该战斗。
- 发送失败后保存在本机，重试继续使用同一份资料和编号；后台兼容旧报告，修正大整数导出精度和空药水槽显示。

上报后台同步更新。不会要求玩家整理日志或填写模组清单。只在主动选择琐事 F2 分类并发送时上传；普通 F2 分类继续使用原版流程。

支持 v0.107.1 / v0.111.0。存储与匿名化 44 项断言、双版本原生探针各 34 项、服务 13 项用例通过；v111 完整客户端验证了 F2 失败保存、重试、回执，以及实际收到的存档从“继续游戏”恢复正常战斗。联机状态采集已实现，验证不等同于真实多机联机复现。

战前存档用于重新执行入场，无法还原卡住的线程或真实联机消息时序；旧报告未记录的数据不能事后补回。这次补齐诊断能力，没有将此前二层卡住报告宣称为已修复。

退出游戏后更新 ZIP 中 `STS2_Things` 文件夹的四个运行文件。BaseLib / RitsuLib 仍为可选。附同一套 ZIP、SHA256 和本说明。

## English

F2 reports now attach more useful diagnostics automatically. Players only need to briefly describe what went wrong.

- Capture combat-start task, native log and Godot engine errors, with method stacks and recent errors retained across restarts.
- Identify loaded mods by name, version, order, Workshop ID, assembly identity and file hashes; include effective Things settings.
- Record multiplayer role, action queues, card state, End Turn controls and paused UI state.
- Include a game screenshot and an anonymous native pre-combat save with complete RNG state, downloadable by the developer for replay.
- Keep failed submissions locally and retry the same immutable report and ID. The dashboard supports older reports and fixes integer precision in exports and empty potion slots.

Reporting remains opt-in through the Things F2 categories; other categories use the original game's reporting flow. The reporting service is updated alongside the mod.

Supports v0.107.1 and v0.111.0. Storage/privacy checks, both native targets, service tests and a full v111 client reporting/replay test passed. A checkpoint restarts combat entry; it cannot restore live threads or network packet timing. These tests do not establish real networked multiplayer reproduction or resolve the earlier Act II freeze report.
