# 尖塔：琐事 2.0.12 / STS2 Things 2.0.12

- **30 张事件插画比例重构与原版式纯色剪影过渡**：
  - 对照《杀戮尖塔 2》`v0.114.0` 原版事件插画（如「酷刑博物馆 / 拇指夹」等）的画面比例与留白规范，重制全部 30 张先前主体过满的事件插画：统一收紧主体构图、大幅扩展四周纯色背景留白（全图纯色中位数 `88.1%`，16:9 有效主体中位数 `16.8%` 贴合原版 `16.3%`，右侧 UI 文本区零侵入）。
  - 为每张重制插画引入贴合各自场景的纯色前景剪影过渡元素（锁链、树根、藤蔓、水渠铁栅、垂幕、石柱、铁钩等，与外围纯色底无缝融合），彻底消除生硬的矩形或椭圆边缘切口。
- **战斗背景视口缩放与活体巨岩远景适配修复**：
  - 优化 `NThingsBackgroundViewportFitter`：微调超采样边距（`1.04 → 1.015`）、钳制战斗震屏位移，并在视口尺寸变化（`SizeChanged`）时即时同步缩放，避免画面放得过大或切窗后构图偏移。
  - 修复 `NCaveGodBossBackground` 远景背景层的视口同步缩放，确保活体巨岩 Boss 战在不同分辨率与宽屏比例下完整铺满且构图自然。
- **怪物能力（Buff）与全事件中英文案原版风格化**：
  - **怪物 Buff（`powers.json`）**：对照原版 `v0.114.0` 能力文案重修中英文全部怪物能力，去除「一阶段 / 二阶段」「已触发锁血」等非原版腔调，统一采用原版简洁直白的机制句式（如「减少」「移除所有负面效果」「加入牌组」等）与 `{OwnerName}` 措辞。
  - **全部事件（`events.json`）**：精炼中英文事件叙事与结果段落，去除冗长修饰，统一全部 6 处遗留非标准「锁定 / Locked」选项标题与诅咒/金币/附魔/离场描述格式，补齐英文漏标颜色标签并统一弯引号排版。
- **多人联机事件准入与低血安全离场加固**：
  - 将事件 `IsAllowed` 准入判定统一调整为联机友好模式（任一存活玩家满足条件即可刷新事件），避免单名队友血量或卡牌不达标导致整队无缘遇到事件。
  - 补齐多处高风险事件在低生命值/低最大生命值状态下的零伤害「离开」兜底选项，并统一共享事件在多人房间内的选项锁态一致性，彻底杜绝无路可选的软锁。

---

- **Event Illustration Scaling Overhaul & Vanilla-Style Solid-Silhouette Transitions (30 Events)**:
  - Rebuilt 30 oversized event illustrations to match *Slay the Spire 2* `v0.114.0` vanilla framing and generous solid-color framing margins (median full-canvas solid margin `88.1%`, median 16:9 active subject area `16.8%` vs. vanilla `16.3%`, keeping the right-hand event text column clear).
  - Replaced abrupt edges with bespoke in-scene solid-silhouette foreground transitions matching each event's outer backdrop color (chains, roots, vines, grates, curtains, arches, hooks), matching vanilla `v0.114.0` events such as *Museum of Torture*.
- **Combat Background Viewport Scaling & Living Megalith Backdrop Fix**:
  - Tuned `NThingsBackgroundViewportFitter` overscan (`1.04 → 1.015`), clamped screen-shake offsets, and wired live `SizeChanged` resyncing so combat rooms no longer appear overly zoomed or misframed across resolutions.
  - Added viewport-synced far-backdrop scaling to `NCaveGodBossBackground` for clean framing across aspect ratios.
- **Vanilla-Style Polish for Monster Buffs & Event Copy (`zhs` & `eng`)**:
  - **Monster Powers (`powers.json`)**: Rewrote monster buff descriptions in both Chinese and English to match Mega Crit's crisp native wording, removing phase jargon and verbose phrasing while unifying terminology and `{OwnerName}` formatting.
  - **Events (`events.json`)**: Tightened verbose event prose across both languages, standardized all remaining non-standard `Locked` option headers, unified Curse/Gold/Enchant/Leave option descriptions, fixed missing BBCode color tags, and normalized English curly quotation marks.
- **Multiplayer-Friendly Event Gates & Low-HP Safe Leave Fallbacks**:
  - Updated event `IsAllowed` checks across the event pool to evaluate living players inclusively (`Players.Any` + `!IsDead`) rather than blocking an entire co-op party when one ally cannot pay a cost.
  - Added zero-damage safe `Leave` fallbacks for low-HP / low-Max-HP states and unified shared-event option lock states across multiplayer rooms.
