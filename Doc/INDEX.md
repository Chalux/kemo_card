# 文档索引

> Agent 请先读 [AGENT.md](AGENT.md)。本文列出活规格、归档入口与本轮维护约定。

**最后修订**：2026-09-21（活规格收敛为 6 份）

---

## 活规格（`Doc/superpowers/specs/`）

**2026-09-21 起收敛为 6 份**（权威链与上位关系见 [AGENT.md §4](AGENT.md)）。其余规格（含此前所有已落地的功能级设计）已把耐用规则合并进下列文档，正文归档到 `Doc/archive/superpowers/specs/`，**原路径留重定向 stub**（旧链接仍可解析）。

| 文档 | 角色 |
|------|------|
| [2026-05-11-kemo-card-design.md](superpowers/specs/2026-05-11-kemo-card-design.md) | **权威总规格**：产品形态 / 宿主·Mod 分界 / Run 环与账本 / 卡牌双层 / 潜能·被动边界 |
| [2026-07-21-combat-system-design.md](superpowers/specs/2026-07-21-combat-system-design.md) | **战斗规格**（`Src/mod/combat`）：阶段机 / 伤害包管线 / SharedHp / 能量·抽牌 / 普攻（次数·追打·专项倍率）/ 充能球 / buff 运行时 / 连携 / 槽位效果 / 战斗条件 |
| [2026-06-22-run-mod-design.md](superpowers/specs/2026-06-22-run-mod-design.md) | **Run 规格**（`Src/mod/run`）：Run 环 / 奖励 / 存档闭环 / 队伍编辑 / ESC 系统菜单 / 团体潜能实现 |
| [2026-09-21-global-mod-design.md](superpowers/specs/2026-09-21-global-mod-design.md) | **Global 规格**（`Src/mod/global`）：主菜单 / 图鉴 / 卡牌与角色详情 / 设置 / 词典 / 界面主题（羊皮纸）/ Toast / 关键词提示 / 界面清单 |
| [2026-05-17-content-mod-manager-design.md](superpowers/specs/2026-05-17-content-mod-manager-design.md) | **内容与数据规格**：内容 Mod 管道 + 内容定义 DTO（卡·技能·效果·Buff + 角色·敌人·战斗·事件·道具）+ 角色/战斗实例 |
| [2026-05-15-ui-manager-design.md](superpowers/specs/2026-05-15-ui-manager-design.md) | **UI 与运行时规格**（`Src/frame`）：UI 管理器与 BaseUI / 界面归属 + `BindingScope` 统一订阅生命周期 / 事件分发器 / 条件系统 / Mod 脚本运行时 |
| [2026-08-04-multiplayer-save-impact.md](superpowers/specs/2026-08-04-multiplayer-save-impact.md) | 联机存档影响评估（**开放项**：未实现，不阻塞单人线） |

> **段号可定位**：被并入章节的原段号在目标文档里原样保留（标题带「（原 §N）」标注），每份文档另附「段号索引」。因此代码注释与旧讨论里按段号引用规格的写法（例：「战斗规格 §1.3」「ui-mod-binding 规格 §4.3」）仍能定位到内容。

## 进行中计划（`Doc/superpowers/plans/`）

**当前没有进行中的计划。** 原有三条计划（条件判断系统、故事选择与 Run 基础壳、Run 存档闭环 + Toast 组件）对应功能均已落地，权威规则已并入活规格（见上表与 [AGENT.md §4](AGENT.md)），计划正文已 `git mv` 进归档，原路径留 stub。

新计划放此目录；完成后 `git mv` 至归档并在原路径留 stub（与规格同规）。

## 归档（`Doc/archive/superpowers/`）

已落地功能级设计与实现计划（UI 组件、图鉴、设置、音频、日志、红点、战斗规格对齐交接，以及 2026-09-21 收起的一批规格等）。

- 规格归档：[archive/superpowers/specs/](archive/superpowers/specs/)
- 计划归档：[archive/superpowers/plans/](archive/superpowers/plans/)

常用交接（已归档，仍可参考）：

- [战斗规格对齐交接](archive/superpowers/plans/2026-07-28-combat-spec-alignment-handoff.md)
- [战斗规格对齐计划](archive/superpowers/plans/2026-07-28-combat-spec-alignment-implementation-plan.md)
- [ContentMod 单轨合并计划](archive/superpowers/plans/2026-07-30-content-mod-single-track-merge.md)

原 `Doc/superpowers/{specs,plans}/` 下同名路径为重定向 stub，避免旧链接断裂。

## 维护

更新地图或归档策略时使用 skill `.cursor/skills/maintain-agent-doc/`，并同步本 INDEX（若增删活规格/归档类别）。

> 2026-09-21 起的约定：**活规格固定 6 份**。新功能落地时把耐用规则并入对应规格（见 [AGENT.md §4](AGENT.md)），原文档 `git mv` 进归档并在原路径留 stub——**不再新增活规格**，除非它属于全新领域并同时更新 AGENT §4 与本表。
