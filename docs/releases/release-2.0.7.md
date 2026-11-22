# 尖塔：琐事 2.0.7 / STS2 Things 2.0.7

- **始源雾菇召唤物机制调整**：
  - 始源雾菇的召唤物「始源利齿之眼（`ThingsOriginEyeWithTeeth`）」经 `IllusionPower` 满血复活后，现在会先进入 **1 回合眩晕（`STUN_AFTER_REVIVE`）**，待眩晕结束后再恢复各自错峰起手招式（`illusion1` 接「牵制」，`illusion2` 接「双噬」）。
  - 图鉴（Bestiary）自动隐藏该内部眩晕过渡招式，且眩晕期间再次击杀仍可正常强制衔接复活状态。
- **包含 `2.0.6` 全部更新内容（面向 Steam 创意工坊直升玩家）**：
  - 新增「必定遇到深处（第二幕）」总开关（`FeatureGuaranteedDepthsEnabled`，支持 BaseLib / RitsuLib 与联机房主同步，不改变原版 RNG 消耗）。
  - 全面适配 5–32 人多人人数上限解除模组；新增 Windows 运行期 `STS2_Things.pck` 只读共享句柄锁防止 Steam 后台热更新覆写引发资源解析失败；修复人面柱「彼岸咆哮」破盾转嫁卡屏、战后选卡血条穿透遮挡及 30+ 项全库隐藏 Bug。
- **多版本兼容与直链网盘**：统一四文件包实测兼容 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，同步更新国内直链网盘（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **Origin Fogmog Summon Tuning**:
  - **Origin Eye with Teeth (`ThingsOriginEyeWithTeeth`)** now enters a **1-turn Stun (`STUN_AFTER_REVIVE`)** immediately after reviving via `IllusionPower`, before resuming its staggered opening move (`Distract` for `illusion1`, `Double Bite` for `illusion2`).
  - Hidden cleanly from the Bestiary move list, and killing a revived Eye again while it is stunned still cleanly transitions back into `REVIVE_MOVE`.
- **Includes All `2.0.6` Changes (for Steam Workshop Subscribers)**:
  - New **Guaranteed Act II: The Depths** master toggle (`FeatureGuaranteedDepthsEnabled`), full **>4P (up to 32P)** unlimited-player mod compatibility, Windows runtime PCK read-shared lock (`PckIntegrityGuard`), Human-Faced Column AoE `Hook.AfterBlockBroken` freeze fix, post-combat health bar overlap fix, and 30+ codebase-wide bug fixes.
- **Multi-Version Support & Direct Mirror**: Unified 4-file package verified across **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with direct downloads at `https://pan.adnermo.online/STS2_Things/latest`.
