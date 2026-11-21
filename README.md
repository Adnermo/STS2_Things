<div align="center">

# 尖塔：琐事 · STS2_Things

**《杀戮尖塔 2》（Slay the Spire 2）原生内容扩展模组**

原创第二章「深处」 · 8 位专属 Boss · 38 个跨章节事件与 5 款联机小游戏 · 单包双版本零依赖运行

[![Version](https://img.shields.io/badge/Version-v2.0.3-2e7d32?style=flat-square)](https://github.com/adnermo/STS2_Things/releases)
[![STS2 Support](https://img.shields.io/badge/STS2-v0.111.0%20%7C%20v0.111.1%20%7C%20v0.113.1%20Beta-1565c0?style=flat-square)](docs/DUAL_VERSION_SUPPORT.md)
[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-1b2838?style=flat-square&logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3747607944)
[![Direct Mirror](https://img.shields.io/badge/国内直链-网盘下载-00838f?style=flat-square)](https://pan.adnermo.online/STS2_Things/latest)
[![Source Audit](https://github.com/adnermo/STS2_Things/actions/workflows/source-audit.yml/badge.svg)](https://github.com/adnermo/STS2_Things/actions/workflows/source-audit.yml)
[![License](https://img.shields.io/badge/License-MIT-6a1b9a?style=flat-square)](LICENSE)

[核心特色](#核心特色) · [安装与下载](#安装与下载) · [内容一览](#内容一览) · [配置与多人联机](#配置与多人联机) · [兼容与反馈](#第三方模组兼容与问题反馈) · [从源码构建](#从源码构建) · [更新日志](CHANGELOG.md)

</div>

---

## 项目简介

**尖塔：琐事（`STS2_Things`）** 是一款面向《杀戮尖塔 2》（Slay the Spire 2）的原生内容扩展模组。工程完全基于游戏原生 API、Godot 4.5.1 Mono 与 Harmony 构建，**无需安装任何前置依赖库**。

- **双版本统一发行**：所有玩家共用同一套 4 文件统一包，运行时依据客户端真实程序集自动加载 **`v0.111.1`（正式版）** 或 **`v0.113.1`（public-beta）** 对应实现。
- **全模式深度适配**：完整支持 `v0.113.1` **恐惧模式（Dread）**（全部 8 位模组 Boss 均配有专属章节支配 `Dominion` 与恐惧强化）与 **急速模式（Blitz）**。
- **确定性多人联机与双语支持**：完整适配 1–4 人合作联机与房主配置同步，提供全套**简体中文（`zhs`）**与**英文（`eng`）**原版风格文案，并内置旧版 `1.x` 存档无损迁移（`LegacyIdMigrator`）。

---

## 核心特色

- **原创第二章分支「深处」（The Depths）**：与巢穴并列的完整第二幕，包含原创石板大地图、多套分层洞穴战斗背景、专属火堆场景与多段无缝自适应配乐。
- **8 位专属 Boss 与原版画风骨骼动画**：全部 Boss 配备 Spine 4.2 骨骼动画、独立 Boss 战曲与专属恐惧支配（Dominion）。
- **38 个跨章节事件 & 5 款联机小游戏**：覆盖一至三章的 33 个策略事件，以及 5 款带独立交互界面的多人同步小游戏（古藤刻痕盘、古林落籽盘、暗渠货栅、蚌口夹珍、铜鼎争宝）。
- **地图横路（Crossroads）与商店互动**：每幕生成最多 3 条付费横路（50 金币解锁，多人共享）；支持商人猜拳砍价互动。
- **细粒度自定义与零依赖配置**：支持通过 **BaseLib** / **RitsuLib** 游戏内设置页或 JSON 文件独立开关音乐、Boss 权重/强制遭遇、精英、事件与自制卡牌。
- **内置 `F2` 诊断上报**：游戏内按 `F2` 即可提交问题反馈，自动附带模组指纹、异常堆栈与战斗快照，并支持离线失败重试。

---

## 安装与下载

### 方式一：Steam 创意工坊（推荐）

前往 Steam 创意工坊直接订阅启用即可自动保持更新：

- **Steam Workshop**：[尖塔：琐事 - STS2_Things (`3747607944`)](https://steamcommunity.com/sharedfiles/filedetails/?id=3747607944)

### 方式二：GitHub Releases / 国内直链网盘

- **GitHub Releases**：[github.com/adnermo/STS2_Things/releases](https://github.com/adnermo/STS2_Things/releases)
- **国内直链网盘**：[pan.adnermo.online/STS2_Things/latest](https://pan.adnermo.online/STS2_Things/latest)

下载统一发布压缩包后，将其中的全部四个文件解压至游戏模组目录 `<Slay the Spire 2 安装目录>/mods/STS2_Things/`：

```text
mods/
└── STS2_Things/
    ├── STS2_Things.json
    ├── STS2_Things.dll
    ├── STS2_Things.pck
    └── STS2_Things.BaseLibBridge.dll
```

> **提示**
> - 正式版 `v0.111.1` 与测试版 `v0.113.1` 下载完全相同的统一包，无需手动区分版本。
> - 多人联机时，房间内所有玩家需保持相同的游戏版本与模组版本。

---

## 内容一览

### 8 位专属 Boss

| 中文名 | 英文名 | 出现章节 | 机制亮点 |
| --- | --- | --- | --- |
| **始源雾菇** | Origin Fogmog | 第一章 · 茂林 (`Overgrowth`) | 幻象分身与孢子压迫，完整重制跨步挥爪与三连击骨骼动画 |
| **放缩巨甲虫** | Scale Beetle | 第一章 · 茂林 (`Overgrowth`) | 体型缩放、六足 IK 扑咬与蜕壳强化机制 |
| **墓潮蛞蝓** | Gravetide Slug | 第一章 · 暗渠 (`Underdocks`) | 尸潮召唤与消化成长，专属 Boss 战曲与独立 UI 图标 |
| **腐化之遗** | The Legacy | 第一章 · 暗渠 (`Underdocks`) | 死亡律动节拍与节奏型多阶段压制 |
| **盛碗虫族母** | Bowlbug Progenitor | 第二章 · 巢穴 (`Hive`) | 虫群协作、未格挡本能触发与汲取转化爆发 |
| **活体巨岩** | Living Megalith | 第二章 · 巢穴 (`Hive`) / 深处 (`Depths`) | 全屏多轨骨骼泰坦战、双臂断肢露核、石爪抓取与试炼抉择 |
| **人面塔** | The Visage Tower | 第二章 · 深处 (`Depths`) | 四面石雕轮转与协同合击，专属 Boss 战曲 |
| **渊墨巨蛸** | Abyssal Inkmaw | 第二章 · 深处 (`Depths`) | 深湖古桥巨物战、多触腕协作与渊墨覆牌遮罩着色器 |

### 第二章「深处」生态与精英 / 特殊遭遇

- **深处普通遭遇池**：包含 **6 组纯种弱敌**（结伴灯笼鱼、血蛭双生、馋嘴洞胃、余影魔、洞穴蜗牛、掘岩孤蚓）与 **10 组强敌**（灯笼鱼群、血蛭群落、血蛭之母、织茧蛾群、异彩史莱姆，以及浸水蛭群、潭边生灵、蛾与蜗牛、幽潭掠甲阵、洞胃与灯鱼等双族互补组合）。
- **深处 4 组专属精英**：
  - **菌根双生子（`Mycorrhizal Twins`）**：长兄专攻、幼弟专守，回合末互换生命并切换丰盛/萎蔫体态与换血反哺，任一阵亡触发断根狂暴。
  - **逆流蝾螈（`Reverse-Current Salamander`）**：每回合回流上回合末张牌并临时减费，搭配「汹涌」全手牌回流博弈。
  - **收音水母（`Receiver Jellyfish`）**：按拍记录每名玩家前两张攻击/技能牌并合并回放，支持多人独立录音与原生校验。
  - **人面柱（`Human-Faced Column`）**：六层石饼三层同场、反向转脸与上/中层免伤向底层传导机制。
- **跨章节特殊遭遇**：**灵魂鱼子团（`Soul Roes`）**（精英）、**怪癖草蜢（`Quirky Hopper`）**（同步偷牌偷药，击杀返还）、**诡谲双商（`Shifty Partners`）**（事件触发双假商人战）。

### 事件、卡牌、遗物与附魔

| 类别 | 数量 | 内容概要 |
| --- | --- | --- |
| **跨章节事件** | **38 个** | 包含 **5 款联机同步小游戏**（古藤刻痕盘、古林落籽盘、暗渠货栅、蚌口夹珍、铜鼎争宝）及 33 个覆盖一至三章的策略事件（如对齐之屋、影子寄存处、收音井、礼貌的洞胃、菌根借贷所、还没烧起来的火、过载接线亭、记仇的贩卖机等） |
| **扩展卡牌** | **12 张** | 包含 **5 张可独立开关的自制职业牌**（对撞、再利用、异鱼症、追忆、整顿）与 7 张遭遇/事件衍生卡牌 |
| **专属遗物** | **15 件** | 包含事件专属遗物、涅奥起始遗物与急速模式「琐事遗物」专属货架 |
| **卡牌附魔** | **4 种** | **消散**（获得虚无）、**分裂**（费用与数值减半）、**过载**（耗能 -1，打出失血）、**配重**（耗能 +1，攻防翻倍） |
| **地图与商店** | **2 项** | **地图横路**（每幕最多 3 条，50 金币解锁，多人共享）与 **商人猜拳砍价** |

---

## 配置与多人联机

所有配置统一保存在 `user://mod_configs/STS2_Things.cfg`（Windows 默认位于 `%APPDATA%\SlayTheSpire2\mod_configs\STS2_Things.cfg`），首次启动自动生成，完整键名与冲突裁决规则详见 [配置说明文档](docs/CONFIGURATION.md)。

| 配置方式 | 前置条件 | 说明 |
| --- | --- | --- |
| **BaseLib 设置页** | 可选安装 BaseLib | 通过随包附带的 `STS2_Things.BaseLibBridge.dll` 自动接入游戏内配置界面 |
| **RitsuLib 设置页** | 可选安装 RitsuLib (`v0.5.12+`) | 通过零依赖运行时互操作协议自动注册设置页 |
| **直接编辑 JSON** | 无需任何第三方库 | 退出游戏后修改 `STS2_Things.cfg`，下次启动生效 |

- **可配置范围**：自定义背景音乐（支持战斗中实时开关）、8 位 Boss 与灵魂鱼子团的独立开关 / 权重（`0–1000`）/ 强制遭遇 / 仅本模组 Boss、38 个事件开关、5 张自制职业牌总开关与单卡开关、地图横路、商人猜拳与涅奥起始遗物。
- **多人联机同步**：联机房间内由房主自动同步玩法配置并在开局后锁定，背景音乐仍可实时切换；离开房间后自动恢复个人配置。

---

## 第三方模组兼容与问题反馈

### 第三方模组兼容

本模组在 [`STS2_Things/Compatibility/`](STS2_Things/Compatibility/README.md) 中维护独立的零依赖兼容隔离区，仅在检测到对应第三方模组时才应用针对性补丁，不引入任何强制依赖：

- 已针对多款热门卡图美化包（包括 BetterCallSpire、RegentCardsAnimeRework、LuolinCard、Life with spire、Diana111、Midori Black Card、AliceDefectCard、CuteNecrobinderCards 等）处理默认卡图缓存与状态牌兼容，并支持 MomoLib 音频共存。
- 详细架构与扩展规范请参阅 [兼容分区说明](STS2_Things/Compatibility/README.md)。

### 问题反馈

1. **推荐方式（游戏内 `F2`）**：在游戏内按下 `F2` 选择「尖塔：琐事」分类并简述问题即可直接提交，系统会自动附带模组指纹、异常堆栈与战局诊断信息。
2. **GitHub Issues**：也可以通过本仓库的 [Issue 模板](https://github.com/adnermo/STS2_Things/issues/new/choose) 提交 Bug 报告或平衡/功能建议。

---

## 从源码构建

### 开发环境要求

| 工具 / 基线 | 版本要求 |
| --- | --- |
| **Godot Engine** | `4.5.1 Mono` |
| **.NET SDK** | `.NET 9.0`（C# `13`） |
| **Python** | `3.12+`（需安装 `Pillow`，用于源码/资源审计） |
| **游戏程序集基线** | `v0.111.1`（正式版，`cc1f9462`） / `v0.113.1`（Beta，`d09d48e5`） |

### 常用构建与验证命令

```powershell
# 1. 构建双版本实现、导出共用 PCK、生成四文件统一包并运行校验探针（追加 -Install 可安装至游戏目录）
.\scripts\build-all.ps1 `
  -DataDirV1111 "D:\path\to\v0.111.1\data_sts2_windows_x86_64" `
  -DataDirV1131 "D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"

# 2. 单目标诊断构建（v113.1 / v111.1）
.\scripts\build.ps1 -TargetVersion v113.1
.\scripts\build.ps1 -TargetVersion v111.1 -DataDir "D:\path\to\v0.111.1\data_sts2_windows_x86_64"

# 3. 运行源码、本地化与确定性静态审计（与 GitHub Actions CI 一致；FMOD 审计由 build.ps1 自动执行）
python .\scripts\verify_project.py

# 4. 预览或清理本地可再生的构建缓存与旧日志
.\scripts\clean_workspace.ps1
.\scripts\clean_workspace.ps1 -Apply
```

构建产物默认输出至：
- **统一发布包**：`build/unified/`（`STS2_Things.json`、`STS2_Things.dll`、`STS2_Things.pck`、`STS2_Things.BaseLibBridge.dll`）
- **单版本诊断构件**：`build/v111.1/` 与 `build/v113.1/`

---

## 仓库结构与文档索引

```text
STS2_Things/
├── STS2_Things/          # 模组核心 C# 源码（模型、Hook、视觉、配置、兼容层与 zhs/eng 本地化）
├── bootstrap/            # 统一包引导入口、客户端版本指纹检测与内嵌程序集加载
├── bridges/              # 可选 BaseLib 配置桥接工程（运行时按需加载）
├── manifests/            # 分版本诊断 manifest
├── images/ scenes/ sfx/ music/ shaders/ animations/ materials/ audios/
│                         # 打包进共用 PCK 的运行期美术、场景、着色器与音频资源
├── scripts/              # 构建、测试、审计与发布脚本（历史一次性脚本归档于 scripts/archive/）
├── tools/                # 独立 Spine 骨骼工具链与原生运行时行为/平衡探针工程
├── services/             # F2 诊断上报等辅助服务源码
├── source_assets/        # 原画、编辑源与制作规范（已通过 .gdignore 排除 Godot 导入与导出）
├── design/               # 机制与数值设计文档
└── docs/                 # 项目技术文档、规范与归档记录
```

### 核心文档导航

| 文档 | 内容说明 |
| --- | --- |
| [文档总览 (`docs/README.md`)](docs/README.md) | 全部核心指南、版本迁移、发布记录与历史归档目录 |
| [配置说明 (`docs/CONFIGURATION.md`)](docs/CONFIGURATION.md) | 全量配置键、权重/强制规则与三套配置入口 |
| [双版本合同 (`docs/DUAL_VERSION_SUPPORT.md`)](docs/DUAL_VERSION_SUPPORT.md) | `v0.111.1` 与 `v0.113.1` API 差异与双版本发布规范 |
| [统一订阅包 (`docs/UNIFIED_PACKAGE.md`)](docs/UNIFIED_PACKAGE.md) | 4 文件统一引导架构、校验门禁与探针流程 |
| [目录与清理约定 (`docs/WORKSPACE_LAYOUT.md`)](docs/WORKSPACE_LAYOUT.md) | 仓库目录职责、编辑源规则与安全清理说明 |
| [F2 诊断上报系统 (`docs/bug-reporting-system.md`)](docs/bug-reporting-system.md) | 内置反馈收集、脱敏快照与离线重试架构 |
| [架构决策记录 (`docs/adr/`)](docs/adr/) | 关键核心架构决策（ADR） |

---

## 近期更新

完整历史版本记录请参阅 **[CHANGELOG.md](CHANGELOG.md)** 或 **[`docs/releases/`](docs/releases/)**。

- **`v2.0.3`**（[完整说明](docs/releases/release-2.0.3.md)）：紧急修复异彩史莱姆（`THINGS_CHROMATIC_SLIME_WEAK`）进房黑屏卡死/闪退与双假商人事件战斗死锁（合并中/小史莱姆共享层级场景并通过 `SetupSkins` 切皮，清除背景自引用，新增 `ThreadedScenePreloadGuardPatch` 串行预加载模组场景）；统一修复各 Boss/小怪血条与意图被前景遮挡、巨型甲虫缩放意图偏移及深处各场遭遇站位布局；`min_game_version` 正式兼容至 **`v0.111.0`**。
- **`v2.0.2`**（[完整说明](docs/releases/release-2.0.2.md)）：全面性能与显存治理（导出纹理显存 ~1,818 → 763.9 MiB，36 份 Spine 骨架无损抽稀，消除战斗/切房卡顿与活体巨岩首抓顿挫）；新增 5 张自制职业牌配置开关；逆流蝾螈新增「汹涌」机制；修复诡谲之店战前空引用与 `v0.113.1` F2 控件转换异常。
- **`v2.0.1`**（[完整说明](docs/releases/release-2.0.1.md)）：修复渊墨巨蛸墨浊覆牌栈溢出崩退；人面柱升格为深处第 4 组专属精英并新增分层免伤传导底层；调优深处精英与始源雾菇；统一打磨 5 款小游戏 UI；修复模组事件「复制」附魔火堆缺失选项。
- **`v2.0.0`**（[完整说明](docs/releases/release-2.0.0.md)）：升级双版本基线至 `v0.111.1 + v0.113.1`；213 个模型统一迁入 `THINGS_*` 命名空间并提供 `LegacyIdMigrator` 无损旧档迁移；完整支持恐惧模式与急速模式；新增人面塔、渊墨巨蛸等 Boss 与 23 个新事件（含 5 款联机小游戏）。

---

## License

本项目基于 [MIT License](LICENSE) 开源。
