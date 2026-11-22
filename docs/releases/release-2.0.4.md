# 尖塔：琐事 2.0.4 / STS2 Things 2.0.4

- **第二章「深处」整体数值下调与菌根双生子专项削弱**：
  - 将深处全部 **38 个怪物类**（19 个普通/弱敌含异彩史莱姆分裂体、5 个精英、以及全部 3 位深处 Boss——人面塔、渊墨巨蛸含 5 种触手、活体巨岩含双臂与囚爪）的生命值与直接攻击伤害同步下调约 **10%**（普通与高进阶同步缩放，Boss 恐惧模式加成保持递增）。
  - **菌根双生子（精英）专项削弱**：取消「同根」每回合换血附带的双方免费力量成长，不等血互疗下调为 `3/4`（原 `6/8`），同伴死亡奖励下调为 `3/4` 力量与 `10/12` 格挡（原 `4/5` 力量与 `15/18` 格挡）；每只血量降至 `76/84`，战孢力量 `2/1`、护根格挡基础 `10/12`（固定 1 层脆弱）、菌甲覆甲 `3/2`，并同步下调常规与狂暴各招式伤害（A0 单人 8 回合无防御总伤由 `246` 降至 `155`、峰值单回合由 `52` 降至 `27`，回到同章精英健康区间）。
  - 保留其余全部核心机制、格挡/护甲/力量成长、余影魔 999 血机制与断腕反伤 20 等玩法设计。
- **18 枚能力（Power）图标高清重绘**：
  - 按原版平面矢量与硬边轮廓画风全面重制 18 枚能力图标及 `power_atlas` 打包图集（涵盖盛碗虫族母、晕眩、人面柱、渊墨遮蔽、血蛭之母沉睡、死亡律动、菌根羁绊/狂怒、始源/始源获能、保护主人、追忆、再利用、放缩甲虫/放大/缩小、灵魂鱼子团、异鱼症）。
- **能力动态文案与悬浮提示精修**：
  - 为「墨浊覆牌」「蛛丝缠绕」「逆流」「腐潮吞噬」补齐实时回合数/动态变量悬浮说明，将活体巨岩试炼负面能力统一为单层显示，并移除「暗潮狂涌」中过时的墨雾悬浮提示。
  - 优化「再利用」与「追忆」卡牌双语文案及悬浮提示，同步强化全模型中英双语本地化自动巡检。
- **多版本兼容与直链网盘**：统一四文件包实测兼容 **v0.111.0 / v0.111.1（正式版）** 与 **v0.113.1（public-beta）**，同步更新国内直链网盘（`https://pan.adnermo.online/STS2_Things/latest`）。

---

- **Act II: The Depths Balance Pass (~-10% HP & Attack) & Mycorrhizal Twins Nerf**:
  - Lowered HP and direct attack damage by ~10% across all **38 Depths monster classes** (19 regular/weak monsters including Chromatic Slime splits, all 5 elites, and all 3 Depths bosses — The Visage Tower, Abyssal Inkmaw with all 5 tentacle types, and Living Megalith with both arms and Captive Claw), scaled cleanly across Base, Ascension, and Dread tiers.
  - **Targeted Mycorrhizal Twins (Elite) nerf**: Removed free per-turn Strength gain on HP swap, reduced uneven-swap mutual heal from `6/8` to `3/4`, lowered death enrage bonus from `4/5` Strength + `15/18` Block to `3/4` Strength + `10/12` Block, lowered per-twin HP to `76/84`, tuned War Spores (`2/1` Str), Root Shelter (`10/12` base Block, 1 Frail), Fungal Plating (`3/2`), and trimmed attack damage across normal and enraged moves.
  - Preserved all core mechanics, Fleeting Echo's 999-HP fade mechanic, and Abyssal Inkmaw's 20-damage sever recoil.
- **18 Power Icons Remastered**:
  - Rebuilt 18 power icons and `power_atlas` sprites in authentic flat-vector STS2 style (Bowlbug Progenitor, Dazed, Human-Faced Column, Ink Blackout, Leech Mother Slumber, Beat of Death, Mycorrhizal Bond/Fury, Origin/Origin Gain Energy, Protect the Master, Recall, Reuse, Scale Beetle/Scale Up/Scale Down, Soul Roes, and Soulfysh Disease).
- **Power Tooltips & Dynamic Localization Polish**:
  - Added live active-turn and dynamic-variable descriptions for Ink Blackout, Silk Thread, Reverse Current, and Gravetide Digestion; unified Living Megalith trial debuffs to `StackType.Single`; removed the stale Ink Blackout hover tip from Abyssal Surge.
  - Refined bilingual card descriptions and hover tips for Reuse and Recall, with expanded automated bilingual localization sweeps.
- **Multi-Version Support & Direct Mirror**: Unified 4-file package verified across **v0.111.0 / v0.111.1 (Main)** and **v0.113.1 (public-beta)**, with direct downloads at `https://pan.adnermo.online/STS2_Things/latest`.
