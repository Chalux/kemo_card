---
name: maintain-agent-doc
description: >-
  Keeps Doc/AGENT.md (and Doc/INDEX.md when needed) accurate as the project map for Agents.
  Use when the user asks to update AGENT docs, after architecture or directory changes,
  when authoritative specs are added/removed/archived, when module entry points move,
  or when documenting new Src/frame or Src/mod areas. Skip for routine bugfixes and small UI tweaks.
---

# 维护 Agent 速查文档

## 目标文件

| 文件 | 职责 |
|------|------|
| [Doc/AGENT.md](../../../Doc/AGENT.md) | Agent 地图：定位、目录、约定、权威链、模块入口、不要做 |
| [Doc/INDEX.md](../../../Doc/INDEX.md) | 活规格 / 计划 / 归档清单（入口变化时同步） |

不要把玩法细则抄进 AGENT；权威正文留在 `Doc/superpowers/specs/`。

## 何时更新（自动发现也适用）

**应当更新：**

- `Src/` 顶层或 `frame` / `mod` 主要子目录增删、重命名
- 新的稳定模块入口（启动链、战斗/Run/内容/UI 等关键类型路径变化）
- 权威规格新增、降级为归档、或权威链顺序变化
- 硬性约定变更（与 `.cursor/rules` 不一致时）
- 用户明确要求「更新 AGENT / 项目说明」

**不要更新：**

- 单文件 bugfix、文案、小 UI、测试修补
- 仅实现计划进度勾选、与地图无关的注释

有疑义时：**宁可少改 AGENT**，只在 INDEX 补一条归档链接即可。

## 工作流

复制进度清单：

```
Agent Doc Sync:
- [ ] 1. 读 Doc/AGENT.md 与 Doc/INDEX.md
- [ ] 2. 对照现状（目录 / 规格 / 入口）
- [ ] 3. 最小 diff 更新 AGENT（必要时 INDEX）
- [ ] 4. 若有归档：git mv + 原路径 stub
- [ ] 5. 自检链接与「不要做」是否仍正确
```

### 1. 读取现状

打开 `Doc/AGENT.md`、`Doc/INDEX.md`；需要时扫 `Src/frame/`、`Src/mod/`、`Doc/superpowers/specs/`、`Doc/archive/`。

### 2. 对照差异

只记录影响「地图」的变化：新目录、迁移的入口类型、活规格集合变化。

### 3. 最小更新 AGENT

保持结构稳定（§1–§8）。优先改表格行与链接，避免重写整节。

约束：

- 简体中文
- 控制体量（约 150–250 行）；不粘贴规格章节
- 路径用正斜杠 `Src/mod/combat/`
- 更新文首 **最后修订** 日期
- 权威冲突规则：总规格 > 战斗 / Run 等下级（与 AGENT §4 一致）

### 4. 同步 INDEX（按需）

活规格增删、plans 目录从空变非空、新归档类别时更新 `Doc/INDEX.md` 表格/链接。

### 5. 归档已完成文档（若本次包含清理）

1. `git mv` 到 `Doc/archive/superpowers/specs/` 或 `plans/`
2. 在原路径写短 stub（标题、归档日期、指向归档副本、指向 INDEX / AGENT）
3. 从 INDEX「活规格/进行中」移除，并在「归档」保留有用交接链接

**保留为活规格的典型集合**：总规格、战斗、Run、内容 Mod、两份 DTO、脚本运行时、角色实例、UI 管理器、事件分发器。功能级 UI/音频/日志等已落地文档进归档。

### 6. 自检

- [ ] AGENT 内相对链接可解析
- [ ] INDEX 与 `superpowers/specs` 实际文件一致（stub 不算活规格）
- [ ] 未引入与 `.cursor/rules` 冲突的约定
- [ ] 未自动 git commit（除非用户要求）

## 显式触发示例

- 「更新 AGENT 文档」
- 「同步项目说明」
- 「把已完成的 XXX 设计归档并更新索引」

## 反例

- 修一个判空后改 AGENT「战斗」整节 → **不要**
- 新增 `Src/mod/shop/` 并成为业务入口 → **要** 在 AGENT §5 加一行，INDEX 视情况加规格链接
