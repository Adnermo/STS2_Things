# 文档目录 · Documentation Index

本目录集中存放 **尖塔：琐事（`STS2_Things`）** 的技术文档、制作规范、版本迁移记录与历史归档。

## 核心维护与使用文档

| 文档 | 说明 |
| --- | --- |
| [配置说明 (`CONFIGURATION.md`)](CONFIGURATION.md) | 全部配置键、权重/强制冲突规则、BaseLib/RitsuLib 可选入口与联机同步机制 |
| [双版本支持合同 (`DUAL_VERSION_SUPPORT.md`)](DUAL_VERSION_SUPPORT.md) | `v0.111.1` 与 `v0.113.1` 游戏 API 差异、版本隔离缝隙与发布要求 |
| [统一订阅包架构 (`UNIFIED_PACKAGE.md`)](UNIFIED_PACKAGE.md) | 四文件统一引导 DLL、运行时指纹选择与验证门禁 |
| [目录与清理约定 (`WORKSPACE_LAYOUT.md`)](WORKSPACE_LAYOUT.md) | 工程目录职责、编辑源隔离及 `clean_workspace.ps1` 清理规则 |
| [F2 诊断上报系统 (`bug-reporting-system.md`)](bug-reporting-system.md) | 内置反馈收集、战斗/手牌快照、脱敏存档与后台架构 |

## 美术与内容制作规范

| 文档 | 适用范围 |
| --- | --- |
| [怪物画风规范 (`monster-art-guidelines.md`)](monster-art-guidelines.md) | 怪物原画风格、提示词与 Spine 切分规范 |
| [战斗背景美术规范 (`background-art-guidelines.md`)](background-art-guidelines.md) | 战斗背景原画、原创地板与多画幅站位验收标准 |
| [事件美术规范 (`event-art-guidelines.md`)](event-art-guidelines.md) | 全幅事件插画构图、渐暗边界与 UI 安全区规范 |
| [事件设计规范 (`event-design-guidelines.md`)](event-design-guidelines.md) | 事件机制、奖励/代价梯度、多人流程与原版风味文案规范 |

## 子目录分类

| 目录 | 内容 |
| --- | --- |
| [`releases/`](releases/) | 各正式版本的中英文更新说明与工坊发布记录（`v1.25.x` – `v2.0.2`） |
| [`migrations/`](migrations/) | 历次游戏大版本升级（`V108` – `V113`）的 API 漂移与迁移文档 |
| [`adr/`](adr/) | 架构决策记录（Architecture Decision Records） |
| [`reports/`](reports/) | 玩家 F2 反馈报告排查、复现记录与性能专题分析 |
| [`archive/`](archive/) | `1.x` 阶段各怪物、事件、动作精修与数值校准的历史研发记录 |
| [`previews/`](previews/) | 文档引用的界面预览图 |
