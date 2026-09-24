# kemo_card — Agent 速查

> 给 Agent / 贡献者的项目地图与**项目级规则唯一权威**。权威玩法细节以规格为准，本文不复制规格正文。  
> 维护：显式或架构变更时使用 skill `maintain-agent-doc`（见文末）。  
> **新增或修改项目约定：直接改本文**，不要再往 `.cursor/rules/` 堆叠重复规则。

**最后修订**：2026-09-25（引擎升至 Godot 4.7.2；战斗位移动画改用 4.7 offset transform）

---

## 1. 产品一句话

Godot 4.7 Mono（纯 C#）卡牌共斗 Roguelike：单人指挥官操控四槽角色；联机管道预留、当前里程碑打磨单人线。

---

## 2. 目录地图

| 路径 | 用途 |
|------|------|
| `Src/frame/` | 框架：UI、内容 Mod、GAS、音频、日志、MVC 事件、脚本宿主等（可引用 Godot；**不得**依赖 `Src/mod/`） |
| `Src/mod/` | 业务：`combat` / `run` / `global` 等；新增文件不确定时优先放此处 |
| `Src/fixed/` | 引擎补丁 / Godot workaround（如 `Localization`、`GodotAppLog`） |
| `Src/utils/` | 纯静态工具（目录可按需新增） |
| `Src/typescript/` | PuerTS TS 工程：构建脚本 + agent builtins（`src/builtins/`）；**mod 脚本源不在此**，见 `Config/mods/<mod>/scripts-src/` |
| `Src/MainRoot.cs` | 启动：日志 / 音频 / UI / 红点 / Bootstrap → 打开菜单 |
| `Resource/` | 贴图、音频、场景资源、`Locale/strings.csv`、agents 资源等 |
| `Config/` | 配置与打包相关；**mod 自包含**：`content/` 定义、`translations/`、`scripts-src/`（TS 源）、`scripts/`（esbuild 产物，提交仓库） |
| `Doc/` | 文档（本文、`INDEX.md`、权威规格、归档） |
| `Tests/` | 单元/集成测试（如 `kemo_card.Ui.Tests`） |
| `.cursor/rules/` | 仅保留指向本文的薄指针；**约定正文以本文为准** |
| `.cursor/skills/` | 项目 Agent skills |

依赖方向：**`mod` → `frame` / `utils` / `fixed`**，禁止反向。`fixed` 仅引擎补丁；通用可复用运行时优先放 `frame`。

---

## 3. 硬性约定

- **语言**：游戏本体只写 **C#**，不写 GDScript。布局用 Godot 场景编辑器，代码只写逻辑。
- **组合优先于继承**：持有并委托（节点组合、小服务/接口），避免深继承。
- **界面订阅一律经 `BindingScope`**：Godot 信号 / 事件总线 / 静态门面事件都必须用 `Binder.OnXxx(...)` 或 `Binder.Bind(...)` 登记，**不得裸写 `+=`**，也不得自写 `_eventsBound` 之类守卫。框架在离场时统一解绑（见 UI 与运行时规格「界面归属与统一订阅生命周期」，即原 ui-mod-binding 规格 §4）。
- **UI 节点不得 override `_ExitTree`**：`BaseUI` / `BaseMask` 已把它收敛为 `sealed`，请改 override 框架级生命周期 `OnExitTree()`；无法继承 `BaseUI` 的节点（`Button` / `CanvasLayer` 等）按 UI 与运行时规格的 6 行模式（原 ui-mod-binding 规格 §4.3）组合 `BindingScope`。
- **订阅登记写在 `_EnterTree`，不要写在 `_Ready`**：Godot 的 `_Ready` **每个节点只调用一次**，界面进缓存走 `RemoveChild`、重开走 `AddChild`，**不会**再触发 `_Ready`（除非显式 `RequestReady()`）。写在 `_Ready` 的订阅会在第一次离场时被 `Binder` 解绑后永不再登记（`BaseCmp` 走框架级 `InitEvent()`，已挂在 `_EnterTree`）。`_Ready` 只放一次性初始化（尺寸、样式、外部数据同步）。
- **界面必须声明归属 Mod 且只取自家门面**：`UIRegistration` 的 `OwnerModId` 必填、在 `Src/mod/FeatureModCatalog.cs` 登记；界面取数用 `Facade<T>()`，不得跨功能直接访问 `AppRoot.Services`。
- **本地化**：面向用户的文案必须用翻译键；场景 `text` 填键；C# 用 `Localization.Tr`。新增键写入 `Resource/Locale/strings.csv`（及 mod CSV）。日志 / `GD.Print` 等可用明文。
- **不创建 `.uid` 文件**（引擎自动生成）。
- **Mod 脚本归属**：mod 脚本源（TS，`scripts-src/`）必须放在 mod 自己的文件夹 `Config/mods/<mod>/scripts-src/`，**不放 `Src/typescript/`**；esbuild 编译到同 mod `scripts/`，产物随仓库提交。`Src/typescript/` 只保留 agent builtins 与构建工具。
- **战斗逻辑与表现分离**：`Src/mod/combat` 只在编排层 `simulation.Presentation.Emit(...)` 记录值事件（record，不持运行时对象引用、不引用 Godot）；动画只在 `Src/mod/run/Ui/Combat/Presentation/CombatAnimator` 内按事件编排，界面播完后以 `SyncFromState` 对账。不得让战斗逻辑直接驱动节点，也不得在界面里重算战斗结果（见战斗规格 §16、Run 规格 §14）。
- **连续大段同业务代码**（>5 个函数）用 `#region` / `#endregion`。
- **Git 提交说明**：简体中文；优先写清变更意图（为什么改），专有名词/路径可保留原文。  
  例：`补充音效管理器，统一 BGM 与 UI 点击音播放入口`；避免 `Add sound manager` / `fix bug`。
- **缩进 / 编码风格**：以仓库根目录 [`.editorconfig`](../.editorconfig) 为权威（当前 C# 为 space / 4）；不为个别目录另开特例。
- **格式化**：某文件逻辑改完且确认无需再改后，再 format **该文件**；不要批量 format 无关文件。顺序：完成逻辑 → 确认 → format → 继续其他工作。

---

## 4. 权威规格链

冲突时以上位为准，并回写实现或下级文档。

**2026-09-21 起活规格收敛为 6 份**。其余规格（含已落地的功能级设计）已并入下表对应文档并归档到 `Doc/archive/superpowers/specs/`，**原路径留重定向 stub**，旧链接仍可解析。

| # | 规格 | 承载 |
|---|------|------|
| 1 | **总规格** — [kemo-card-design](superpowers/specs/2026-05-11-kemo-card-design.md) | 产品形态、宿主/Mod 分界、Run 环与账本、卡牌双层、潜能/被动边界 |
| 2 | **战斗** — [combat-system-design](superpowers/specs/2026-07-21-combat-system-design.md) | `Src/mod/combat`：阶段机、SharedHp、能量/抽牌、标记队列、主动/蓄力、指令管线、伤害包管线、普攻（次数/追打/专项倍率）、充能球、buff 运行时、连携、槽位效果、战斗条件、**表现事件流（§16）** |
| 3 | **Run** — [run-mod-design](superpowers/specs/2026-06-22-run-mod-design.md) | `Src/mod/run`：Run 环、奖励、存档闭环、队伍编辑、ESC 系统菜单、**战斗界面 `CombatWin` 与表现管线（§14）**、团体潜能实现（与总规格冲突时以总规格为准） |
| 4 | **Global** — [global-mod-design](superpowers/specs/2026-09-21-global-mod-design.md) | `Src/mod/global`：主菜单、图鉴、卡牌/角色详情、设置、词典、界面主题（羊皮纸）、Toast、关键词提示、界面清单 |
| 5 | **内容与数据** — [content-mod-manager-design](superpowers/specs/2026-05-17-content-mod-manager-design.md) | 内容 Mod 管道 + 内容定义 DTO（卡/技能/效果/Buff + 角色/敌人/战斗/事件/道具）+ 角色与战斗实例 |
| 6 | **UI 与运行时** — [ui-manager-design](superpowers/specs/2026-05-15-ui-manager-design.md) | `Src/frame`：UI 管理器与 BaseUI、**界面归属功能 Mod + BindingScope 统一订阅生命周期**（`BindingScope` / 框架级 `OnExitTree`）、事件分发器、条件判断（Persistent/Combat 双域 CondType、内联 JSON 组合、Explain；已接 `StoryDto.unlock` 与效果 `conditions`）、Mod 脚本运行时（PuerTS ScriptEnv） |

被并入章节的**原段号保留**在新文档里（标题带「（原 §N）」标注，并各附「段号索引」），因此按旧段号引用规格的代码注释（如「战斗规格 §1.3」「ui-mod-binding 规格 §4.3」）仍可定位。

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
| 条件判断 | `Src/frame/condition/` + `Src/mod/global/Condition/` | 引擎在 frame；Persistent CondType 与 `GlobalPersistentCondContext`（`HasFlag` → 全局 `Unlocks`）在 mod；权威见 UI 与运行时规格「条件系统」 |
| GAS | `Src/frame/gas/` + `Src/mod/combat/gas/` | 属性、GE、战斗桥接 |
| UI 框架 | `Src/frame/ui/` | `UiManager`、Base*、生命周期状态机 |
| 事件 | `Src/frame/mvc/` | `EventDispatcher`、源生成器 |
| UI 订阅生命周期 | `Src/frame/ui/BindingScope.cs`，`BindingScopeSignals.cs` | 订阅登记簿 + 信号糖；框架级 `OnExitTree` 统一解绑（权威见 UI 与运行时规格） |
| 界面归属 | `Src/mod/FeatureModCatalog.cs`，`Src/frame/ui/IUiFacadeProvider.cs` | 功能 Mod 界面声明与门面解析（唯一登记处；权威见 UI 与运行时规格） |
| 音频 | `Src/frame/audio/` | `Sound` 门面 + `SoundManager` |
| 日志 | `Src/frame/logging/` + `Src/fixed/godot/GodotAppLog.cs` | `AppLog` 门面 |
| 红点 | `Src/frame/notification/` | |
| 脚本宿主 | `Src/frame/scripting/` | Puerts；agent builtins 在 `Src/typescript/src/builtins/`，mod 脚本源在 `Config/mods/<mod>/scripts-src/`（编译至 `scripts/`）；权威见 UI 与运行时规格「Mod 脚本运行时」 |
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

**布局**（2026-07-29 定，2026-09-21 收敛活规格）：

```
Doc/
  AGENT.md                # 本文：约定唯一权威 + Agent 地图
  INDEX.md                # 活规格 / 计划 / 归档清单
  superpowers/specs/      # 活规格（固定 6 份，见 §4）
  superpowers/plans/      # 仅进行中或仍有交接价值
  archive/superpowers/{specs,plans}/   # 已落地文档正文
README.md                 # 短入口，指向 AGENT.md 与 INDEX.md
.cursor/skills/maintain-agent-doc/SKILL.md
```

- 活规格（固定 6 份，见 §4）/ 进行中计划：`Doc/superpowers/{specs,plans}/`
- 已落地功能文档：`Doc/archive/superpowers/`（用 `git mv`，**原路径留短重定向 stub**，避免旧链接断裂）
- **新功能落地后**：把其耐用规则并入 §4 对应规格，再 `git mv` 原文档进归档 + 原路径留 stub + 同步 [INDEX.md](INDEX.md)；不要新增活规格，除非它属于新的域并同时更新 §4。
- **本文约束**：约 150–250 行；**不复制规格正文**（玩法权威以 §4 规格为准）；硬性约定只写在本文件，`.cursor/rules/` 仅保留指向本文的薄指针。
- **更新本文件**：使用项目 skill [maintain-agent-doc](../.cursor/skills/maintain-agent-doc/SKILL.md)  
  触发：目录重组、新模块入口、权威规格增删、归档策略变化、**硬性约定变更**；日常小改不更新。
