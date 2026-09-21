# 文档索引

> Agent 请先读 [AGENT.md](AGENT.md)。本文列出活规格、归档入口与本轮维护约定。

**最后修订**：2026-09-20

---

## 活规格（`Doc/superpowers/specs/`）

| 文档 | 角色 |
|------|------|
| [2026-05-11-kemo-card-design.md](superpowers/specs/2026-05-11-kemo-card-design.md) | 权威总规格 |
| [2026-07-21-combat-system-design.md](superpowers/specs/2026-07-21-combat-system-design.md) | 战斗系统权威 |
| [2026-06-22-run-mod-design.md](superpowers/specs/2026-06-22-run-mod-design.md) | Run 模块（服从总规格） |
| [2026-05-17-content-mod-manager-design.md](superpowers/specs/2026-05-17-content-mod-manager-design.md) | 内容 Mod 管道（含 2026-07-30 单轨合并修订 + 2026-07-31 Story 类别 + 2026-09-20 随包内容暂存/调试强制刷新） |
| [2026-06-16-content-definition-dto-design.md](superpowers/specs/2026-06-16-content-definition-dto-design.md) | 卡牌 / 技能 / Buff / 效果 DTO |
| [2026-06-16-character-battle-event-item-dto-design.md](superpowers/specs/2026-06-16-character-battle-event-item-dto-design.md) | 角色 / 敌人 / 战斗 / 事件 / 道具 DTO |
| [2026-06-17-jsenv-mod-scripting-design.md](superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md) | Mod 脚本运行时（PuerTS） |
| [2026-06-18-character-instance-design.md](superpowers/specs/2026-06-18-character-instance-design.md) | 角色 / 战斗实例 / 卡组 / 手牌槽 |
| [2026-05-15-ui-manager-design.md](superpowers/specs/2026-05-15-ui-manager-design.md) | UI 管理器与 BaseUI |
| [2026-07-07-event-dispatcher-design.md](superpowers/specs/2026-07-07-event-dispatcher-design.md) | 事件分发器 |
| [2026-07-29-agent-doc-and-archive-design.md](superpowers/specs/2026-07-29-agent-doc-and-archive-design.md) | 本文档体系（AGENT / 归档）设计 |
| [2026-07-30-condition-system-design.md](superpowers/specs/2026-07-30-condition-system-design.md) | 条件判断系统（双域 CondType / JSON 组合 / Explain） |
| [2026-08-04-run-save-continue-design.md](superpowers/specs/2026-08-04-run-save-continue-design.md) | Run 存档闭环（保存 / 继续 / 自动保存，单槽） |
| [2026-08-04-toast-component-design.md](superpowers/specs/2026-08-04-toast-component-design.md) | Toast 通用组件（对象池 / 堆叠 / 动画） |
| [2026-08-04-multiplayer-save-impact.md](superpowers/specs/2026-08-04-multiplayer-save-impact.md) | 联机存档影响评估（开放项，不阻塞单人线） |
| [2026-09-15-ui-mod-binding-design.md](superpowers/specs/2026-09-15-ui-mod-binding-design.md) | 界面归属功能 Mod + BindingScope 统一订阅生命周期 |
| [2026-09-19-buff-potential-chain-system-design.md](superpowers/specs/2026-09-19-buff-potential-chain-system-design.md) | Buff 运行时 + 团体潜能（替代总规格 §4.5.2–4.5.3）+ 连携 + 槽位效果（chalux） |
| [2026-09-19-ui-theme-and-debug-panel.md](superpowers/specs/2026-09-19-ui-theme-and-debug-panel.md) | UI 主题统一（KemoPalette/KemoTheme/BaseKemoButton）+ Run 调试面板 + 本地化守卫 |
| [2026-09-19-charge-orb-system-design.md](superpowers/specs/2026-09-19-charge-orb-system-design.md) | 充能球（元素球）系统 + chalux 四张专属卡 |
| [2026-09-20-normal-attack-design.md](superpowers/specs/2026-09-20-normal-attack-design.md) | 普通攻击（每回合槽位轮转、敌方全体、物/魔取较高者减对应防御） |
| [2026-09-21-run-team-editor-design.md](superpowers/specs/2026-09-21-run-team-editor-design.md) | Run 队伍编辑界面（4 槽位选项卡 + 角色池 + 详情预览 + 卡组二级界面） |

## 进行中计划（`Doc/superpowers/plans/`）

| 文档 | 角色 |
|------|------|
| [2026-07-30-condition-system-implementation-plan.md](superpowers/plans/2026-07-30-condition-system-implementation-plan.md) | 条件判断系统实现（引擎 + Persistent 四件套） |
| [2026-07-31-story-selection-and-run-shell-implementation-plan.md](superpowers/plans/2026-07-31-story-selection-and-run-shell-implementation-plan.md) | 故事选择与 Run 基础壳实现（Story 内容接入 + 选故事 UI + Run 主界面） |
| [2026-08-04-run-save-continue-implementation-plan.md](superpowers/plans/2026-08-04-run-save-continue-implementation-plan.md) | Run 存档闭环 + Toast 组件实现（保存 / 继续 / 自动保存） |

新计划放此目录；完成后 `git mv` 至归档并在原路径留 stub。

## 归档（`Doc/archive/superpowers/`）

已落地功能级设计与实现计划（UI 组件、图鉴、设置、音频、日志、红点、战斗规格对齐交接等）。

- 规格归档：[archive/superpowers/specs/](archive/superpowers/specs/)
- 计划归档：[archive/superpowers/plans/](archive/superpowers/plans/)

常用交接（已归档，仍可参考）：

- [战斗规格对齐交接](archive/superpowers/plans/2026-07-28-combat-spec-alignment-handoff.md)
- [战斗规格对齐计划](archive/superpowers/plans/2026-07-28-combat-spec-alignment-implementation-plan.md)
- [ContentMod 单轨合并计划](archive/superpowers/plans/2026-07-30-content-mod-single-track-merge.md)

原 `Doc/superpowers/{specs,plans}/` 下同名路径为重定向 stub，避免旧链接断裂。

## 维护

更新地图或归档策略时使用 skill `.cursor/skills/maintain-agent-doc/`，并同步本 INDEX（若增删活规格/归档类别）。
