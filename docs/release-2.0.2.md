# 尖塔：琐事 2.0.2 / STS2 Things 2.0.2

- **全面性能优化与显存/卡顿治理**：
  - **战斗与切房零卡顿**：重构 F2 诊断录屏管线，取消战斗中每 500ms 主线程全量序列化与切房同步磁盘阻塞，报错先 O(1) 去重再解析、Harmony 扫描与指纹计算迁至按需/后台执行。
  - **显存大幅降载与洞窟合图**：事件、房间、战斗背景与深处地图底图对齐原版切至高画质 BPTC BC7 显存压缩（总导出纹理显存由约 1,818 MiB 降至 763.9 MiB，进房消除 CPU WebP 解码顿挫）；清理 4 张全空洞窟图、移除 50 层冗余背景叠层与重复着色器；36 份 Spine 骨架无损抽稀（总体积 58.76 → 38.79 MiB，骨骼关键帧减半，60Hz 全骨姿态零漂移）。
  - **Boss 战帧率专项优化**：烘焙活体巨岩拳头网格，彻底消除首次抓人时的 7.38 MiB JSON 解析与 `GetImage()` GPU 回读卡死；渊墨巨蛸隐藏触手/光束关闭 `ProcessMode` 并优化全屏/卡面墨水着色器；人面柱储备层静置停更新（活跃骨减半）；同步优化全游戏卡面遮罩快路径、选牌界面分帧高清图升级、卡池/遭遇/事件排序缓存、联机配置去装箱与音频生命周期（修复二次进房预加载释放冲突并补齐缺失预加载）。
- **新增模组自制卡牌开关配置项**：
  - BaseLib 与 RitsuLib 配置页新增「卡牌」分区，支持总开关（`CardsEnabled`）及 5 张自制职业牌独立开关（对撞、再利用、异鱼症、追忆、整顿），联机由房主同步锁定；关闭后从运行期奖励、商店、变化与随机生成池移除，同时保留卡牌图鉴已解锁可见与已有存档兼容。
- **逆流蝾螈（Reverse-Current Salamander）战斗机制强化**：
  - 新增「汹涌（Surge）」效果（下回合抽牌后使全部可打出手牌附着「回流」，打出其中任意一张后解除其余手牌的回流与减费），逆流触发收益上调为力量 +2 / 未打出格挡 +10。
- **F2 玩家反馈 Bug 修复**：
  - 修复「诡谲之店」双假商人进战前在 `PrepareForFight` 阶段过早写入能力/血量历史导致 `CombatHistoryEntry` 空引用崩退的问题。
  - 修复 `v0.113.1` 下打开 F2 反馈选择模组分类时因 `NMegaTextEdit` 控件变更引发的 `InvalidCastException` 异常。
- **双版本兼容与直链网盘**：统一支持 **v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，并同步提供国内直链网盘下载（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **Comprehensive Performance, VRAM & Stutter Overhaul**:
  - **Smooth combat & room transitions**: Refactored the F2 bug-report recorder to eliminate 500ms main-thread full-run JSON serialization and synchronous main-thread disk-I/O blocks on room entry, with O(1) raw error pre-deduplication and deferred/background Harmony & replay encoding.
  - **Major VRAM reduction & cave flattening**: Migrated 120 event, room, background, and Depths map textures to vanilla-matching high-quality BPTC BC7 VRAM compression (~1,818 MiB → 763.9 MiB total exported VRAM; eliminates CPU WebP decode hitches); removed 4 blank layers and 50 redundant background scene instances; losslessly pruned 36 Spine `.spjson` rigs (58.76 → 38.79 MiB, -51.3% bone keyframes, verified <= 0.04 px at 60 Hz).
  - **Boss & global hot-path optimization**: Baked Living Megalith's fist overlay meshes directly onto the atlas (eliminating the first-grab 7.38 MiB JSON parse & `GetImage()` GPU readback stall); disabled hidden Abyssal Inkmaw Spine rigs and streamlined ink blackout/drench shaders; paused settled Human-Faced Column reserve discs; added O(1) fast paths to card veils, viewport-throttled `CrispCardPortraitPatch`, cached pool/encounter/event/quirk catalogs, de-boxed multiplayer config lookups, and fixed `PreloadManager` audio/visual cache lifecycle & missing preloads.
- **Custom Card Toggle Settings**:
  - Added a **Cards** settings section (BaseLib & RitsuLib) with a master switch (`CardsEnabled`) and 5 per-card switches (Collision, Reuse, Soulfysh Disease, Reminisce, Pack Up) synced by the host in multiplayer. Disabled cards are excluded from all run pools while staying unlocked in the Card Library and safe in existing saves.
- **Reverse-Current Salamander Rework**:
  - Added the **Surge** mechanic (next turn applies Reflux to all playable cards in hand; playing any one clears Reflux from the rest) and boosted Reverse Current payoff to +2 Strength / +10 Block.
- **F2 Player-Reported Bug Fixes**:
  - Fixed a `NullReferenceException` in `ThingsRobberyFakeMerchantEncounter.PrepareForFight` caused by recording combat history before `CombatManager.SetUpCombat`.
  - Fixed an `InvalidCastException` on `v0.113.1` in `ThingsFeedbackSelectionPatch` when accessing `NSendFeedbackScreen`'s wrapped `NMegaTextEdit`.
- **Dual-Version Support & Direct Mirror**: Unified 4-file package for **v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with direct mirror downloads at `https://pan.adnermo.online/STS2_Things/latest`.
