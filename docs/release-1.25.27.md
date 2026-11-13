# 尖塔：琐事 1.25.27 / STS2 Things 1.25.27

- 将现代游戏目标更新为 Steam public-beta **v0.113.0**，保留 **v0.107.1** 支持。旧现代目标 v0.111.0 由新版替代。
- 适配新版死亡回调、生命上限损失、巨岩动态交互与血条恢复、章节生成重载、联机大厅和连接构造签名。
- 商人猜拳接入新版原生购买同步器；协商价格先于原生购买消息同步，结算后清理，保留个人库存与原版奖励流程。
- 适配新版 F2 截图字节与日志附件结构，旧版反馈流程保留。
- 「分裂」兼容新版 HitCount 命中次数变量，并迁移旧存档中的 Repeat 字段，保留已有数值与成长。
- 保留 1.25.26 的 Boss 动作与数值内容；本轮不调整玩法数值。工作区未完成的 Soul Roes 改动未进入统一包。

---

- Updates the modern target to Steam public-beta **v0.113.0** while retaining **v0.107.1**. Replaces the previous v0.111.0 target.
- Adapts native death hooks, max-HP loss, Living Megalith interactability and state display, act-generation overloads, and multiplayer lobby/connection constructors.
- Routes merchant bargaining through the new native purchase synchronizer. Negotiated prices arrive before native purchases and are cleared after settlement; personal inventories and native rewards remain intact.
- Adapts the new F2 screenshot-byte and log-attachment API while preserving the legacy feedback path.
- Adapts Split to the new HitCount variable and migrates legacy Repeat snapshots without losing saved values or growth.
- Preserves the 1.25.26 animations and balance. This update makes no balance changes and excludes unfinished Soul Roes work.
