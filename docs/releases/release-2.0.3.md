# 尖塔：琐事 2.0.3 / STS2 Things 2.0.3

- **紧急修复：异彩史莱姆（`THINGS_CHROMATIC_SLIME_WEAK`）进房黑屏卡死 / 闪退**：
  - 将中/小型异彩史莱姆的 6 个分色场景合并为 2 个共享层级场景（`chromatic_slime_medium.tscn`、`chromatic_slime_small.tscn`），对齐原版 `MonsterModel.SetupSkins` 切换炽红/琥金/苍碧三色皮肤与受击框/意图偏移，彻底消除 Godot 4.5.1 多线程 `ResourceLoader` 并发加载共享 `SpineSkeletonDataResource` 引发的 `WorkerThreadPool::wait_for_task_completion` `Invalid Task ID` 竞态、原生崩溃与黑屏死锁。
  - 修复双假商人遭遇背景（`things_robbery_fake_merchant_encounter_background.tscn`）残留 `NBgLayerDebug` 与静态引用 `layers/*.tscn` 导致的并发预加载静默死锁。
  - 新增 `ThreadedScenePreloadGuardPatch`（Harmony 补丁 `AssetLoadingSession` 构造），将本模组自有战斗/怪物/背景 `.tscn` 路由至后台串行预加载队列（不触碰原版资源），从根源杜绝全模组（含渊墨巨蛸等）共享外部资源的并发预加载竞态；同步加固 `DepthsActMusicController` 异步预加载状态去重。
- **战斗 UI 遮挡、缩放意图与遭遇站位全面修正**：
  - 新增 `BossStateDisplayLayerPatch`，提升本模组怪物与角色在模组战斗中的血条、能力栏与意图显示层级（高于前景岩石/藤蔓与召唤物，低于战斗/全局 UI），修复腐化之遗等 Boss 战背景前景遮挡玩家血条的问题。
  - 修正巨型甲虫（Scale Beetle）成长缩放公式与 `ScaleBeetleIntentSyncPatch`，缩放时平滑同步意图位置并限制顶边安全区，避免高体型下意图飘出屏幕。
  - 统一调优深处与一二幕十余场 Boss/精英/常规遭遇的相机缩放、偏移、血条缩减与槽位坐标（含盛碗虫族母、腐化之遗、始源雾菇、巨型甲虫、活体巨岩、渊墨巨蛸外侧触手多画幅防裁切等）。
- **双版本兼容与直链网盘**：统一支持 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**（`min_game_version` 放宽至 `v0.111.0` 并经全量启动与探针验证），同步提供国内直链网盘下载（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **Critical Hotfix: Chromatic Slime (`THINGS_CHROMATIC_SLIME_WEAK`) Black-Screen Freeze / Crash**:
  - Consolidated the 6 per-color Medium/Small Chromatic Slime scenes into 2 tier-shared scenes (`chromatic_slime_medium.tscn`, `chromatic_slime_small.tscn`) using native `MonsterModel.SetupSkins` for Crimson/Amber/Azure skins and per-variant bounds/marker offsets, eliminating the Godot 4.5.1 `WorkerThreadPool::wait_for_task_completion` `Invalid Task ID` race, native crashes, and permanent black-screen deadlocks caused by concurrently preloading multiple `.tscn` files sharing the same `SpineSkeletonDataResource`.
  - Removed leftover editor-only `NBgLayerDebug` and static `layers/*.tscn` `ext_resource` self-references from `things_robbery_fake_merchant_encounter_background.tscn` that caused silent preloading deadlocks on the Robbery Fake Merchant fight.
  - Added `ThreadedScenePreloadGuardPatch` (constructor postfix on `AssetLoadingSession`) to serialize `STS2_Things` combat/creature/background `.tscn` preloads on Godot's background sequential queue without touching vanilla scenes, and hardened `DepthsActMusicController` against duplicate threaded load requests.
- **Combat HUD Layering, Scale Beetle Intent Sync & Encounter Layout Polish**:
  - Added `BossStateDisplayLayerPatch` to elevate HP bars, power strips, and intents cleanly above foreground props and minions (fixing boss foreground rocks/props occluding player HP bars in Gravetide Slug and other encounters) while staying below Combat/Global UI.
  - Smoothed Scale Beetle's visual growth curve and added `ScaleBeetleIntentSyncPatch` to keep intents aligned and clamped within safe viewport bounds as the beetle grows.
  - Tuned camera framing, HP bar sizing, and slot layouts across Boss, Elite, and Regular encounters (including Abyssal Inkmaw outer tentacle viewport clamping, Bowlbug Progenitor, Gravetide Slug, Origin Fogmog, Living Megalith, and Depths encounters).
- **Multi-Version Support & Direct Mirror**: Unified 4-file package verified across **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)** (`min_game_version` lowered to `v0.111.0`), with direct mirror downloads at `https://pan.adnermo.online/STS2_Things/latest`.
