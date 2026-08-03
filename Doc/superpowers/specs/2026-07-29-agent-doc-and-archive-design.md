# Agent 速查文档与设计文档归档

**日期**：2026-07-29  
**状态**：已确认（对话批准）  
**范围**：归档已落地功能级 spec/plan、新增 `Doc/AGENT.md` 与维护 skill、短 README / INDEX  
**非范围**：合并权威规格正文、改业务代码、自动 git commit

---

## 1. 目标

- 降低 `Doc/superpowers/` 噪音：已完成功能文档进 `Doc/archive/`，权威规格留原地。
- 为 Agent 提供稳定入口：`Doc/AGENT.md`（地图 + 约定 + 权威链 + 模块入口）。
- 用项目 skill 持续维护 `AGENT.md`（可自动发现 + 显式调用）。

## 2. 布局

```
Doc/
  AGENT.md
  INDEX.md
  superpowers/specs/     # 权威 / 仍有效规格
  superpowers/plans/     # 仅进行中或仍有交接价值（本轮可为空）
  archive/superpowers/{specs,plans}/
README.md                # 短入口，指向 Doc/AGENT.md 与 Doc/INDEX.md
.cursor/skills/maintain-agent-doc/SKILL.md
```

归档用 `git mv`；原路径保留短重定向 stub。

## 3. 保留规格（活）

- 总规格、战斗、Run、内容 Mod、两份 DTO、脚本运行时、角色实例、UI 管理器、事件分发器

其余功能级 UI/音频/日志/图鉴等 spec 与全部已完成 plan → 归档。

## 4. AGENT.md 约束

约 150–250 行；不复制规格正文；**硬性约定以 `Doc/AGENT.md` 为唯一权威**（`.cursor/rules/` 仅保留指向 AGENT 的薄指针，不重复堆叠约定正文）。

## 5. Skill 触发

省略 `disable-model-invocation`；仅在架构 / 目录 / 权威规格 / 模块入口变化时更新；日常小改不碰。
