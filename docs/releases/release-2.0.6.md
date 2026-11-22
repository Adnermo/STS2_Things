# 尖塔：琐事 2.0.6 / STS2 Things 2.0.6

- **新增「必定遇到深处（第二幕）」总开关**：
  - 在 BaseLib / RitsuLib 设置页新增「章节（Acts）」分区及 **`FeatureGuaranteedDepthsEnabled`（必定遇到深处）** 开关（默认关闭），支持联机由房主同步锁定。
  - 开启后在标准/自定义/恐惧/急速/每日挑战及单人/多人对局中稳定将第 2 幕设为「深处」，且完全不改变原版 RNG 消耗序列；同步修复 `ModelDb._actsByIndex` 懒缓存刷新。
- **全面适配多人人数上限解除模组（支持 5–32 人大房间联机）**：
  - 完整适配常见联机上限解除模组：自动扩展大房间下巨物 Boss 站位、活体巨岩接触锚点、深处火堆多角色站位、怪癖/收音/逆流/吸血水蛭/渊墨等战斗机制与特效、5 款联机小游戏（含铜鼎争宝）网络同步，以及 F2 诊断快照与存档校验（最高 32 人）。
- **线上玩家反馈紧急修复 & 运行期 PCK 防覆写保护**：
  - **运行期 PCK 保护（`PckIntegrityGuard`）**：在 Windows 启动时对 `STS2_Things.pck` 保持只读共享句柄锁，防止游戏运行期间 Steam 创意工坊后台热更新覆写磁盘 PCK 引发场景/贴图解析失败及怪物/营地/事件缺失。
  - **人面柱「彼岸咆哮」卡屏幕修复**：修复 AoE 击破底层格挡且中/顶层伤害转嫁击杀移除底层后，`Hook.AfterBlockBroken` 收到空 `CombatState` 触发 `NullReferenceException` 导致卡牌滞留屏幕中央的问题。
  - **战后选卡界面血条穿透遮挡修复**：重新设计 `BossStateDisplayLayerPatch`，递归计算有效 Z 层级并严格钳制在 `GlobalUi` 之下，且在战斗结束时复位 `%HealthBar.ZIndex`。
  - **馋嘴洞胃 / 刮甲石蚓越界与日志脱敏修复**：修复多段受击接触点数组越界崩溃，以及 F2 日志脱敏将 `res://` / `user://` / `https://` 误替换为 `<LOCAL_PATH>` 的问题。
- **全库隐藏 Bug 深度排查与修复（30+ 项）**：
  - **战斗 / Boss / 能力**：修复人面柱三层同帧 AoE 团灭跳过备用层（覆写 `ShouldStopCombatFromEnding`）；修复人面塔连击/高潮遭荆棘反杀后重复旋转或死后叠增益；修复恐惧模式 Boss 领域在战斗结束后仍扣血致 1 HP 判负；修复电台水母调频回合残留旧收听状态；修复异彩史莱姆受晕眩后丢失已触发的分裂动作；修复灵魂鱼籽与菌根双生子被反杀后的死后动画/增益；修复重复利用每回合计数及盛碗虫族母号令迭代安全。
  - **卡牌 / 遗物 / 视觉 / 音频**：灯笼鱼致盲预遮罩严格限定在真实抽牌流程内，不再误遮许愿/导师/秘技等检索牌；卡池回收与复入时彻底剥离墨黑/致盲遮罩防止污染后续卡牌；克隆墨黑卡牌洗净后正确恢复费用；修复狭小取景器意外免费升级商店全部卡牌；修复灵鱼疾症与原版 `SoulsPower` 指数级刷牌；修复熄灭营火误显「克隆」选项；修复美杜莎之发升级收口、尼奥险剪原型守卫、山洞神背景与深处 BGM 切房释放竞态崩溃；同步优化墓潮蛞蝓 Boss 战曲母带与循环衔接。
  - **事件 / 小游戏 / 地图 / 商店议价**：加固 5 款联机小游戏的断线/退局防死锁与符文织机取消重进防卡死；商人猜拳期间禁止并发购买并在失败/取消时清理远端价格覆盖；加固十字路口地图切换防竞态；修复深处事件池、后室、切得太近、淤泥绞盘、诡谲之店的边界软锁与重复遗物；修复配置冲突规范化顺序与旧版 ID 迁移器 `Finalizer` 异常恢复。
- **多版本兼容与直链网盘**：统一四文件包实测兼容 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，同步更新国内直链网盘（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **New Master Toggle: Guaranteed Act II — The Depths**:
  - Added an **Acts** settings section with **`FeatureGuaranteedDepthsEnabled`** (default `false`) across BaseLib, RitsuLib, and host-locked multiplayer config sync.
  - Deterministically forces Act II to **The Depths** across Standard, Custom, Dread, Blitz, Daily, singleplayer, and multiplayer without altering vanilla RNG draws; also invalidates stale `ModelDb._actsByIndex` caches after patching.
- **Extended Multiplayer (>4 Players, up to 32P) Compatibility**:
  - Adapted boss/party positioning, Living Megalith contact anchors, Depths rest-site layouts, combat powers/VFX (Quirk, Radio Reception, Reverse Current, Leech, Ink Blackout), all 5 co-op minigames (including Bronze Tripod Scramble), and F2 diagnostics/checkpoints for unlimited-player mods up to 32 players.
- **Live Player-Report Hotfixes & Runtime PCK Integrity Guard**:
  - **Runtime PCK Guard (`PckIntegrityGuard`)**: Pins `STS2_Things.pck` with a shared read-only handle on Windows while the game runs, preventing mid-session Steam Workshop background updates from overwriting the active PCK and causing scene/resource parse errors.
  - **Human-Faced Column AoE Freeze Fix**: Guarded `Hook.AfterBlockBroken` against a null `CombatState` when an AoE (such as `HowlFromBeyond`) breaks the bottom disc's Block while redirected damage simultaneously kills and removes the bottom disc.
  - **Post-Combat Reward Screen Health Bar Overlap Fix**: Clamped `BossStateDisplayLayerPatch` strictly below `GlobalUi` via effective ancestor Z calculation and reset `%HealthBar.ZIndex` on combat end.
  - **Cave Maw / Scale-Peeler Bounds & Log Sanitizer**: Clamped multi-hit contact array indexing and fixed `LogSanitizer` mangling `res://`, `user://`, and `https://` URIs.
- **Codebase-Wide Hidden Bug Audit & Fixes (30+ Fixes)**:
  - **Combat / Bosses / Powers**: Prevented premature combat end on simultaneous 3-disc Column AoE wipe while reserves remain; fixed Visage Tower post-Thorns double rotation/climax buffs; prevented post-victory Dread Dominion HP loss; fixed Radio Reception tuning-turn carryover, Chromatic Slime post-Stun split loss, Soul Roes / Mycorrhizal Twins post-Thorns triggers, Reuse per-turn tracking, and Bowlbug Progenitor `RallyMove` enumeration safety.
  - **Cards / Relics / Visuals / Audio**: Scoped Lantern Blindness draw priming strictly to real draws (no longer veiling tutored cards like Wish/Tutor/Secret Technique); stripped pooled card veils cleanly on recycle; restored energy cost on washed clones of inked cards; fixed Narrow Viewfinder accidentally upgrading every Merchant card for free; fixed Soulfysh Disease exponential duplication with vanilla `SoulsPower`; hid Clone option on spent campfires; hardened Medusa Hair upgrade finalization, Precarious Shears prototype check, Living Megalith background disposal, and Depths BGM lifecycle; refined Gravetide Slug boss theme master & loop seam.
  - **Events / Minigames / Map / Merchant Bargain**: Eliminated co-op minigame disconnect deadlocks & Rune Loom cancel-retry softlocks; blocked concurrent purchases during Merchant RPS and cleared stale remote price overrides on rejection; fixed Crossroads deferred map race; fixed edge softlocks/duplicate relics in Depths Event Pool, Backrooms, Cutting It Close, Silt Winch, and Shifty Partners; fixed config conflict resolution ordering and `LegacyIdMigrator` `Finalizer` cleanup.
- **Multi-Version Support & Direct Mirror**: Unified 4-file package verified across **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with direct downloads at `https://pan.adnermo.online/STS2_Things/latest`.
