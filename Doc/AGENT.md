# kemo_card — Agent 速查

> 给 Agent / 贡献者的项目地图与**项目级规则唯一权威**。权威玩法细节以规格为准，本文不复制规格正文。  
> 维护：显式或架构变更时使用 skill `maintain-agent-doc`（见文末）。  
> **新增或修改项目约定：直接改本文**，不要再往 `.cursor/rules/` 堆叠重复规则。

**最后修订**：2026-07-31（Story 内容类别 / 选故事与 Run 界面壳）

---

## 1. 产品一句话

Godot 4.6 Mono（纯 C#）卡牌共斗 Roguelike：单人指挥官操控四槽角色；联机管道预留、当前里程碑打磨单人线。

---

## 2. 目录地图

| 路径 | 用途 |
|------|------|
| `Src/frame/` | 框架：UI、内容 Mod、GAS、音频、日志、MVC 事件、脚本宿主等（可引用 Godot；**不得**依赖 `Src/mod/`） |
| `Src/mod/` | 业务：`combat` / `run` / `global` 等；新增文件不确定时优先放此处 |
| `Src/fixed/` | 引擎补丁 / Godot workaround（如 `Localization`、`GodotAppLog`） |
| `Src/utils/` | 纯静态工具（目录可按需新增） |
| `Src/typescript/` | Mod 脚本侧 TS 工程（经 PuerTS 宿主加载） |
| `Src/MainRoot.cs` | 启动：日志 / 音频 / UI / 红点 / Bootstrap → 打开菜单 |
| `Resource/` | 贴图、音频、场景资源、`Locale/strings.csv`、agents 资源等 |
| `Config/` | 配置与打包相关 |
| `Doc/` | 文档（本文、`INDEX.md`、权威规格、归档） |
| `Tests/` | 单元/集成测试（如 `kemo_card.Ui.Tests`） |
| `.cursor/rules/` | 仅保留指向本文的薄指针；**约定正文以本文为准** |
| `.cursor/skills/` | 项目 Agent skills |

依赖方向：**`mod` → `frame` / `utils` / `fixed`**，禁止反向。`fixed` 仅引擎补丁；通用可复用运行时优先放 `frame`。

---

## 3. 硬性约定

- **语言**：游戏本体只写 **C#**，不写 GDScript。布局用 Godot 场景编辑器，代码只写逻辑。
- **组合优先于继承**：持有并委托（节点组合、小服务/接口），避免深继承。
- **本地化**：面向用户的文案必须用翻译键；场景 `text` 填键；C# 用 `Localization.Tr`。新增键写入 `Resource/Locale/strings.csv`（及 mod CSV）。日志 / `GD.Print` 等可用明文。
- **不创建 `.uid` 文件**（引擎自动生成）。
- **连续大段同业务代码**（>5 个函数）用 `#region` / `#endregion`。
- **Git 提交说明**：简体中文；优先写清变更意图（为什么改），专有名词/路径可保留原文。  
  例：`补充音效管理器，统一 BGM 与 UI 点击音播放入口`；避免 `Add sound manager` / `fix bug`。
- **缩进 / 编码风格**：以仓库根目录 [`.editorconfig`](../.editorconfig) 为权威（当前 C# 为 space / 4）；不为个别目录另开特例。
- **格式化**：某文件逻辑改完且确认无需再改后，再 format **该文件**；不要批量 format 无关文件。顺序：完成逻辑 → 确认 → format → 继续其他工作。

---

## 4. 权威规格链

冲突时以上位为准，并回写实现或下级文档。

1. **总规格** — [kemo-card-design](superpowers/specs/2026-05-11-kemo-card-design.md)  
   产品形态、宿主/Mod 分界、Run 环与账本、卡牌双层、潜能/被动边界。
2. **战斗** — [combat-system-design](superpowers/specs/2026-07-21-combat-system-design.md)  
   阶段机、SharedHp、能量/抽牌、标记队列、主动/蓄力、指令管线。
3. **Run** — [run-mod-design](superpowers/specs/2026-06-22-run-mod-design.md)（与总规格冲突时以总规格为准）
4. **内容** — [content-mod-manager](superpowers/specs/2026-05-17-content-mod-manager-design.md) + [卡牌/技能/Buff DTO](superpowers/specs/2026-06-16-content-definition-dto-design.md) + [角色/战斗/事件/道具 DTO](superpowers/specs/2026-06-16-character-battle-event-item-dto-design.md)
5. **脚本** — [jsenv-mod-scripting](superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md)（PuerTS ScriptEnv）
6. **角色实例** — [character-instance](superpowers/specs/2026-06-18-character-instance-design.md)
7. **UI 框架** — [ui-manager](superpowers/specs/2026-05-15-ui-manager-design.md) + [event-dispatcher](superpowers/specs/2026-07-07-event-dispatcher-design.md)
8. **条件判断** — [condition-system](superpowers/specs/2026-07-30-condition-system-design.md)  
   共享求值引擎、Persistent/Combat 双域 CondType、内联 JSON 组合、Explain 结果；已接 `StoryDto.unlock`（Combat 域 v1 空表）

完整索引与归档入口：[INDEX.md](INDEX.md)。

---

## 5. 模块入口（代码）

| 领域 | 主要路径 | 备注 |
|------|----------|------|
| 启动 / 服务根 | `Src/MainRoot.cs`，`Src/mod/AppRoot.cs`，`Src/mod/ModFactory.cs` | `AppRoot.Services` 在 Bootstrap 后可用 |
| 战斗模拟 | `Src/mod/combat/`（`runtime/`、`statemachine/`、`commands/`、`rules/`、`effects/`、`gas/`） | 权威见战斗规格 |
| Run 编排 | `Src/mod/run/` | 环模型、奖励、存档；`RunRuntime` 会话门面 + `Ui/` 选故事与 Run 主界面壳 |
| 全局 UI / 图鉴 / 设置 | `Src/mod/global/` | Menu、Codex、Card/Character UI 组件 |
| 内容管道 | `Src/frame/content/` | 发现、加载、合并、校验（含 Story 类别）；`GameDefinitionStore` 为定义权威，`Registry` 管版本/owner/`Contains` |
| 条件判断 | `Src/frame/condition/` + `Src/mod/global/Condition/` | 引擎在 frame；Persistent CondType 与 `GlobalPersistentCondContext`（`HasFlag` → 全局 `Unlocks`）在 mod；权威见条件规格 |
| GAS | `Src/frame/gas/` + `Src/mod/combat/gas/` | 属性、GE、战斗桥接 |
| UI 框架 | `Src/frame/ui/` | `UiManager`、Base*、生命周期状态机 |
| 事件 | `Src/frame/mvc/` | `EventDispatcher`、源生成器 |
| 音频 | `Src/frame/audio/` | `Sound` 门面 + `SoundManager` |
| 日志 | `Src/frame/logging/` + `Src/fixed/godot/GodotAppLog.cs` | `AppLog` 门面 |
| 红点 | `Src/frame/notification/` | |
| 脚本宿主 | `Src/frame/scripting/` | Puerts；业务脚本在 `Src/typescript/` / 内容 mod |
| 显示 / 语言设置 | `Src/frame/display/`，`Src/frame/locale/` | |

---

## 6. 不要做

- 不在 `Src/frame/` 引用 `KemoCard.Mod.*` 或业务场景路径。
- 不把通用运行时塞进 `Src/fixed/`（fixed 仅引擎补丁）。
- 不硬编码面向用户的可见文案。
- 不把玩法权威写进 AGENT 长文；改规则先改规格再改代码。
- 不擅自 `git commit` / push，除非用户明确要求。
- 不批量 format 未改动的文件；不创建 `.uid`。
- 不为个别目录另写缩进特例（一律跟 `.editorconfig`）。
- 不在 `.cursor/rules/` 重复维护已写入本文的约定。

---

## 7. 常用验证

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --nologo -v q
```

格式化仅针对改过的文件，例如：

```powershell
dotnet format kemo_card.csproj --include Src/mod/combat/SomeFile.cs
```

缩进与相关风格以 `.editorconfig` 为准，不要另开特例说明。

---

## 8. 文档维护

- 活规格 / 进行中计划：`Doc/superpowers/{specs,plans}/`
- 已落地功能文档：`Doc/archive/superpowers/`（原路径留重定向 stub）
- **更新本文件**：使用项目 skill [maintain-agent-doc](../.cursor/skills/maintain-agent-doc/SKILL.md)  
  触发：目录重组、新模块入口、权威规格增删、归档策略变化、**硬性约定变更**；日常小改不更新。
