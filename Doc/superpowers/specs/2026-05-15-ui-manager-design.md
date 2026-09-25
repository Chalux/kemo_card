# UI 与运行时规格（框架域基础：UI 框架 / 订阅生命周期 / 事件分发 / 条件引擎 / 脚本宿主）

**（原「UI 管理器与 BaseUI」规格；2026-09-21 并入界面归属/订阅生命周期、事件分发器、条件系统、Mod 脚本运行时规格）**

> 本文是 `Src/frame/` **跨域基础**（UI 框架、订阅生命周期、事件分发、条件引擎、脚本宿主）的**唯一权威文档**。四份已实装的下级规格已整篇并入本文 **§14–§17**，原文文件归档（见下表），此后只在本文维护。

**日期**：2026-05-15（原「UI 管理器与 BaseUI」规格首次定稿）  
**最后修订**：2026-09-21（框架域下级规格合并）  
**状态**：活规格（框架域基础的唯一权威）
- **§1–§13**（原「UI 管理器与 BaseUI」，2026-05-15）：原状态「已定稿待实现评审」；现已实现并与代码对齐，见 §12 实现备注。
- **§14**（原 ui-mod-binding，2026-09-15）：原状态「活规格（2026-09-15 起）」；迁移步骤 1–9 已落地，自检见 §14.12。
- **§15**（原 event-dispatcher，2026-07-07）：原状态「已实现」。
- **§16**（原 condition-system，2026-07-30 / 原文最后修订 2026-07-31）：原状态「引擎与 Persistent 四件套已实现；首个内容接入 `StoryDto.unlock`（2026-07-31）；Combat CondType / 其余内容 DTO 字段未接」。
- **§17**（原 jsenv-mod-scripting，2026-06-17）：原状态「已定稿（brainstorming 确认）」。

**关系**：本文承载 `Src/frame/` 跨域基础的权威规格，服从 [2026-05-11 总规格](2026-05-11-kemo-card-design.md)；战斗相关服从 [2026-07-21 战斗系统规格](2026-07-21-combat-system-design.md)；内容侧条件字段（`StoryDto.unlock` 等）与内容 Mod 管道见 [2026-05-17 内容 Mod 管道规格](2026-05-17-content-mod-manager-design.md)；Combat 域条件接入见 [2026-07-21 战斗规格](2026-07-21-combat-system-design.md) §14.7。

**范围**：Godot 4.x Mono（C#）下，除 `MainRoot` 启动场景外，以 **UI 管理器** 为唯一 UI 打开入口；`BaseUI` / `BaseWin` / `BaseDlg` / `BasePge` / `BasePop` 生命周期、层级、注册表与强类型载荷；**界面归属功能 Mod 与统一订阅生命周期**（`BindingScope` / 框架级 `OnExitTree` / `IUiFacadeProvider` / `CloseByOwner`）；**类型安全事件分发**（`EventDispatcher` / `EventKey<TPayload>` / Source Generator 声明式事件表 / `GlobalEvents.Bus` / Mod 内部 `InternalBus`）；**可扩展条件求值引擎**与 Persistent / Combat 双域 CondType 注册、JSON 组合语法、Explain 结构化结果与提示模板约定；**单一 `Puerts.ScriptEnv`** 的 Mod 脚本运行时、预编译 JS 加载、窄面板沙箱与 Rebuild 重建。

**非范围**（各章 YAGNI 细目见 §1.2 / §14.1.2 / §15.1.2 / §16.1.2 / §17.1.2，此处只列跨域总纲）：
- **UI**：多 `SubViewport` 套嵌与复杂对象池（首版可直接 `QueueFree`，后续可扩展池）；全项目 i18n、主题系统；编辑器内可视化「连线」注册；跨 Mod 界面复用（需要复用的部分下沉到 `frame` 组件）。
- **归属与订阅**：内容 Mod 提供界面（`Config/mods/*` 仍是纯数据）；反射/程序集扫描自动发现 Mod（违背组合根纯手工装配取向）。
- **事件分发**：跨进程 / 跨网络事件总线；事件持久化、重放与 QoS 语义；`filter`/`map`/`throttle` 类事件流管道。
- **条件**：扣除/支付（Cost）；具名条件包（`cond_pack_id`）；脚本动态注册 CondType；引擎级依赖追踪 / 脏标记与订阅；NOT 组合糖（否定用独立 CondType）。
- **脚本运行时**：静态全局 `ScriptEnv.Instance`；运行期编译 TS；业务脚本 async/Promise；HybridCLR 可信 Mod 通道；LLM Agent 专用 VM。

---

## 本文承载的下级规格（2026-09-21 合并并归档）

| 原规格文件（`Doc/superpowers/specs/`） | 原文档标题 | 归档路径 | 并入章节 |
|---|---|---|---|
| `2026-09-15-ui-mod-binding-design.md` | 界面与功能 Mod 绑定 + 统一订阅生命周期设计 | `Doc/archive/superpowers/specs/2026-09-15-ui-mod-binding-design.md` | **§14**（原 §1–§13） |
| `2026-07-07-event-dispatcher-design.md` | 事件分发器设计 | `Doc/archive/superpowers/specs/2026-07-07-event-dispatcher-design.md` | **§15**（原 §1–§13） |
| `2026-07-30-condition-system-design.md` | 条件判断系统（Condition）设计 | `Doc/archive/superpowers/specs/2026-07-30-condition-system-design.md` | **§16**（原 §1–§11） |
| `2026-06-17-jsenv-mod-scripting-design.md` | Mod 脚本运行时（PuerTS ScriptEnv）设计规格 | `Doc/archive/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md` | **§17**（原 §1–§10） |

本文原有部分（原「UI 管理器与 BaseUI」，2026-05-15）保留为 **§1–§13**，**段号未变**（本次只把 §13 与 §13.1–§13.4 的标题层级统一为与其余章节一致，标题文字、段号与正文均未改）。

**段号引用约定（重要）**：`Doc/AGENT.md` §3 与大量 `Src/`、`Tests/` 代码注释仍按**原规格段号**引用（例：「见 ui-mod-binding 规格 §4」「按 ui-mod-binding 规格 §4.3 的 6 行模式」「条件规格 §8」「事件分发器 §4.2」「jsenv-mod-scripting 规格 §3」）。因此：

- 被并入章节的标题一律写成 **新段号 + 原标题 + （原 §N）**，例如 `### 14.4 层 1：BindingScope 与框架级生命周期（原 §4）`、`#### 14.4.3 框架级生命周期 OnExitTree（决策 2）（原 §4.3）`；
- 各章**正文里的 `§N` 一律保留原文**（即原规格段号，不做重新编号）；
- 本文新段号一律带章号前缀（`§14.N` / `§15.N` / `§16.N` / `§17.N` / `§18.N`…），与旧段号不会混淆；
- 按旧段号跳转见下一节「段号索引」。

---

## 段号索引（旧段号 → 本文段号）

### 原 `2026-05-15-ui-manager-design.md`（本文原有部分）

| 原段号 | 本文段号 |
|---|---|
| §1–§13（含 §1.1–§13.4） | 未变，仍是 §1–§13 |

### 原 `2026-09-15-ui-mod-binding-design.md` → 本文 §14

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| §1 | 目标与非目标 | §14.1 |
| §1.1 | 目标 | §14.1.1 |
| §1.2 | 非目标（YAGNI） | §14.1.2 |
| §2 | 现状与问题（含证据） | §14.2 |
| §2.1 | 两套并行的绑定实现 | §14.2.1 |
| §2.2 | D1（真实缺陷，高）：`_eventsBound` 守卫击败框架契约 | §14.2.2 |
| §2.3 | D2：约 40 处裸 `+=` 不在框架覆盖范围内 | §14.2.3 |
| §2.4 | D3：没有「归属」概念 | §14.2.4 |
| §2.5 | D4：界面取数靠服务定位 | §14.2.5 |
| §2.6 | D5（新发现，决策 1 的前置阻塞）：`UIOpenOpt` 合并在 5 个字段上无条件覆盖 | §14.2.6 |
| §3 | 方案总览 | §14.3 |
| §4 | 层 1：BindingScope 与框架级生命周期 | §14.4 |
| §4.1 | `BindingScope`（纯 C#，可单测） | §14.4.1 |
| §4.2 | 信号糖：一个扩展方法集，两种宿主共用 | §14.4.2 |
| §4.3 | 框架级生命周期 `OnExitTree`（决策 2） | §14.4.3 |
| §4.4 | 解绑职责表 | §14.4.4 |
| §5 | 层 2：归属契约 | §14.5 |
| §5.1 | 归属契约：声明与门面**分离**（`IUiOwner` 单接口方案已废弃） | §14.5.1 |
| §5.2 | 归属落到运行时 | §14.5.2 |
| §5.3 | 注册期硬校验（`UIRuntimeRegistry.Validate()` 增补） | §14.5.3 |
| §5.4 | 归属门面解析（决策 3：统一修正 D4） | §14.5.4 |
| §6 | 层 3：Mod 级界面生命周期 | §14.6 |
| §6.1 | `UIManager` 增补 | §14.6.1 |
| §6.2 | 决策 1：Run 结束销毁 `RunMainWin` | §14.6.2 |
| §6.3 | 决策 3 前置：`UIOpenOpt` 字段显式化（修 D5） | §14.6.3 |
| §7 | 组合根（决策 3 的装配侧） | §14.7 |
| §8 | 生命周期时序 | §14.8 |
| §9 | 测试 | §14.9 |
| §10 | 迁移步骤（每步可独立编译与验证） | §14.10 |
| §11 | 风险与兼容 | §14.11 |
| §12 | 自检记录 | §14.12 |
| §12.1 | 实施中修正的规格问题（记录，避免重蹈） | §14.12.1 |
| §12.2 | 实现备注（与当前代码对齐） | §14.12.2 |
| §12.2 末节 | 已知未处理的跨功能读 | §14.12.2.1 |
| §13 | 后续步骤 | §14.13 |

### 原 `2026-07-07-event-dispatcher-design.md` → 本文 §15

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| §1 | 目标与非目标 | §15.1 |
| §1.1 | 目标 | §15.1.1 |
| §1.2 | 非目标 | §15.1.2 |
| §2 | 核心架构 | §15.2 |
| §2.1 | 组件图 | §15.2.1 |
| §2.2 | 关键类型 | §15.2.2 |
| §3 | EventKey——事件标识 | §15.3 |
| §4 | EventDispatcher——核心分发器 | §15.4 |
| §4.1 | 内部数据结构 | §15.4.1 |
| §4.2 | API | §15.4.2 |
| §4.2 子目 | 注册 | §15.4.2.1 |
| §4.2 子目 | 派发 | §15.4.2.2 |
| §4.2 子目 | 取消 | §15.4.2.3 |
| §4.2 子目 | 查询 | §15.4.2.4 |
| §4.3 | 线程模型 | §15.4.3 |
| §4.4 | 派发失败策略 | §15.4.4 |
| §5 | 日志抽象 | §15.5 |
| §6 | 全局事件总线 | §15.6 |
| §7 | Mod 内部事件总线 | §15.7 |
| §7.1 | BaseMod | §15.7.1 |
| §7.2 | BaseController | §15.7.2 |
| §8 | 声明式事件表（Source Generator） | §15.8 |
| §8.1 | 使用方式 | §15.8.1 |
| §8.2 | 编译期校验 | §15.8.2 |
| §9 | 典型使用模式 | §15.9 |
| §9.1 | 声明载荷 | §15.9.1 |
| §9.2 | 订阅 | §15.9.2 |
| §9.3 | 派发 | §15.9.3 |
| §9.4 | UI 事件 | §15.9.4 |
| §9.5 | 一次性监听 | §15.9.5 |
| §10 | 设计权衡 | §15.10 |
| §11 | 测试 | §15.11 |
| §12 | 文件布局 | §15.12 |
| §13 | 后续步骤 | §15.13 |

### 原 `2026-07-30-condition-system-design.md` → 本文 §16

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| §1 | 目标与非目标 | §16.1 |
| §1.1 | 目标 | §16.1.1 |
| §1.2 | 非目标 | §16.1.2 |
| §2 | 架构 | §16.2 |
| §3 | JSON 组合语法 | §16.3 |
| §3.1 | 规则 | §16.3.1 |
| §3.2 | 示例 | §16.3.2 |
| §3.3 | 空值与缺失 | §16.3.3 |
| §3.4 | 保留 / 非法 key | §16.3.4 |
| §4 | CondType 注册 | §16.4 |
| §5 | 求值与结果 | §16.5 |
| §5.1 | Context | §16.5.1 |
| §5.2 | 结果结构 | §16.5.2 |
| §5.3 | 提示约定 | §16.5.3 |
| §5.4 | 刷新 | §16.5.4 |
| §6 | 与 Cost 的边界 | §16.6 |
| §7 | v1 内置 CondType（Persistent） | §16.7 |
| §8 | 内容接入 | §16.8 |
| §9 | 测试要点 | §16.9 |
| §10 | 有意延后 | §16.10 |
| §11 | 文档位置 | §16.11 |

### 原 `2026-06-17-jsenv-mod-scripting-design.md` → 本文 §17

| 原段号 | 原标题 | 本文段号 |
|---|---|---|
| §1 | 目标与非目标 | §17.1 |
| §1.1 | 目标 | §17.1.1 |
| §1.2 | 非目标 | §17.1.2 |
| §2 | 架构 | §17.2 |
| §3 | 模块标识与路径 | §17.3 |
| §4 | 加载策略 | §17.4 |
| §5 | 沙箱与通信 | §17.5 |
| §5.1 | ScriptContextFacade（唯一参数） | §17.5.1 |
| §5.2 | 入口约定 | §17.5.2 |
| §5.3 | 各类型返回形状 | §17.5.3 |
| §5.4 | 沙箱局限 | §17.5.4 |
| §6 | 生命周期 | §17.6 |
| §7 | 错误处理 | §17.7 |
| §8 | 构建链 | §17.8 |
| §9 | 测试要点 | §17.9 |
| §10 | 自检 | §17.10 |

---

## 1. 目标与非目标

### 1.1 目标

- **单一入口**：业务不直接 `Instantiate` 游戏内 UI 场景再挂树；除 `MainRoot` 外，**仅通过 UI 管理器**打开界面。
- **注册表**：以 **字符串 Id** 注册（绑定 `PackedScene` 或工厂）。**未注册的 Id 无法打开**（拒绝并记录错误）。
- **层级**：`BaseDlg` 与 `BasePopup` 分槽；Dlg 始终在 Popup **之下**；Popup **栈式**叠加。
- **生命周期**：每个 `BaseUI` 自带 **状态机**，与管理器协同管理加载、打开、关闭、释放。
- **数据传输**：每个具体 UI 在 **自身类中定义载荷类型**；注册与打开 API 在 **编译期** 绑定 **UI 类型 + 载荷类型**（强类型）。

### 1.2 非目标（YAGNI）

- 多 `SubViewport` 套嵌、复杂对象池（首版可直接 `QueueFree`，后续可扩展池）。
- 全项目 i18n、主题系统（与 UI 管理器无强绑定）。
- 编辑器内可视化「连线」注册（首版以代码注册为主）。

---

## 2. 场景树与层级约定

### 2.1 MainRoot 侧

- `MainRoot._Ready` 在 `ModFactory.Bootstrap` 之前调用 `InitUIManager()`，创建 `UIRuntimeRegistry`、注册 UI、创建 `UIManager` 节点、调用 `Init(UIManagerInitOpt)`。
- `UIManagerInitOpt` 指定 `Layers`（基础层：Win/Dlg/Loading/Pop）和 `TopLayers`（顶层：Debug/Notice/Guide），构建双根布局（`UIRoot` / `UITopRoot`）。

### 2.2 层级槽位

| 层级 | 枚举值 | 说明 |
|---|---|---|
| `Win` | `EUILayer.Win` | 完整窗口层，默认 HideBelow = true |
| `Dlg` | `EUILayer.Dlg` | 对话框层，Dlg 默认在此层 |
| `Loading` | `EUILayer.Loading` | 加载层 |
| `Pop` | `EUILayer.Pop` | 气泡弹窗层，Pop 默认在此层 |
| `Guide` | `EUILayer.Guide` | 引导层（顶层） |
| `Debug` | `EUILayer.Debug` | 调试层（顶层） |
| `Notice` | `EUILayer.Notice` | 通知层（顶层） |

- `EUILayer` 枚举中越靠后的层级，渲染越靠上。`TopLayers` 挂在独立的 `UITopRoot` 节点下，始终渲染在基础层之上。
- 每个层级由 `UILayer`（`Control` 子类）承载，管理子 UI 的增删和排序。

---

## 3. BaseWin 体系与 Dlg 行为

### 3.1 类型层次

`BaseUI` → `BaseWin` → `BaseDlg` / `BasePge` / `BasePop`。

| 类 | 用途 | 默认层级 | 默认对齐 | 默认 HideBelow |
|---|---|---|---|---|
| `BaseWin` | 完整窗口 | `Win` | `Full` | `true`（遮挡下层） |
| `BaseDlg` | 对话框 | `Dlg` | `Center` | `false` |
| `BasePge` | 页面（嵌套于父 UI） | — | `Full` | `false` |
| `BasePop` | 气泡弹窗 | `Pop` | `None` | `false` |

### 3.2 Dlg 行为

- **同一 Dlg 层最多一个** `BaseDlg` 实例处于已打开状态。
- **无遮罩**（不挡全屏点击；业务若需要局部遮罩在子类中自行实现，但不作为 Dlg 默认语义）。
- **替换策略**：当已存在 Dlg 且要打开 **另一个** Dlg 时：**先异步准备新实例至可展示状态**，再 **切换**：旧实例保持显示直至新实例就绪，然后移除旧、挂上新，避免中间「无 Dlg」空窗。
- **快速重复打开**：通过 `UIOpenCoordinator.EnqueueOpen` 管理打开队列，**以最后一次有效请求为准**，取消未完成的旧加载任务。

### 3.3 Win 行为

- `BaseWin` 默认 `HideBelow = true`，打开时遮挡其下所有层级，并压入 `UIStack` 导航栈。
- 支持 `BackAsync()` 返回上一级全屏 UI。

---

## 4. BasePopup（BasePop）行为

### 4.1 栈与遮罩

- **可无限叠加**（仅受性能与业务约束）。
- `UIMaskOpt` 控制遮罩行为：`Runtime`（遮罩资源 Id）、`Alpha`（透明度）、`ClickClose`（点击关闭）、`Color` 等。
- 遮罩层级由 `UILayerManager.UpdateLayers()` 统一刷新：从顶层向下遍历，`HideBelow` 和 `NoCover` 控制界面可见性。

### 4.2 同 Id 再次打开

通过 `UIOpenCoordinator` 管理打开队列和当前打开项（`_currOpening`），支持：
- 若已有实例且处于加载中（`Load`/`PreLoad`），新请求会取消旧的加载任务（取消 `CancellationToken`）。
- 从 `Cache` 状态恢复时跳过 `Create` 步骤直接进入 `Open`。
- `EAnimType.SkipReOpen` 控制重新打开时是否跳过打开动画。

### 4.3 与 BaseDlg 的相对顺序

- Pop 层级 `EUILayer.Pop` 在 `EUILayer.Dlg` 之上（通过 `_layers` 数组顺序保证）；顶层遮罩可拦截对 Dlg 的点击。

---

## 5. BaseUI 与状态机

### 5.1 职责划分

- **UI 管理器**：注册校验、`OpenAsync`/`Close` 调度、层级管理、Dlg 替换顺序、导航栈 `Push`/`Back`、层级可见性更新。
- **状态处理器**：7 个 `IStateHandler<EUIState, IUIStateContext>` 分别驱动每个状态的进入/退出逻辑。
- **BaseUI**：自身 **状态** 与 **可覆盖钩子**（`IUILifecycleInvoker` 接口实现）；**不**自行决定挂在哪个层（由管理器传入或查询已注入的层引用）。

### 5.2 状态枚举

```csharp
enum EUIState { Wait, Load, PreLoad, Create, Open, Close, CloseDone, Cache, Destroy }
```

**状态流转**：

```
Wait → Load → PreLoad → Create → Open → Close → CloseDone → Cache → Destroy
              ↑                                                    │
              └──────────────── Cache（命中缓存直接跳入 Open）─────┘
```

- **Load**：加载场景资源 + 遮罩资源。**必须在主线程同步加载**（`ResourceLoader.Load<PackedScene>`，见下）。
- **PreLoad**：调用 `OnPreLoad(done, fail)`，业务可在此进行数据准备。
- **Create**：首次打开，初始化界面和遮罩，挂入场景。
- **Open**：播放打开动画，派发 `UIEvent.Open` 事件。
- **Close**：播放关闭动画。
- **CloseDone**：从层级移除、决定缓存或销毁。
- **Cache**：节点保持存在但移出场景树，`CacheTime = -1` 为永久缓存，`> 0` 为定时销毁，`0` 为立即销毁。
- **Destroy**：清理资源、`QueueFree`、从 `UIVoRegistry` 移除。

每个 `UIVo` 持有一个 `StateMachine<EUIState, IUIStateContext>`，通过注入的 `IStateHandler` 列表驱动流转。

> **界面加载必须主线程同步（2026-09-19 修复）**：此前 `UIResourceLoader` 用
> `ResourceLoader.LoadThreadedRequest` + 轮询 + `await Task.Delay` 异步加载，而**后台线程无法创建 C# 脚本**，
> 含 `script = ExtResource(...)` 的场景会在脚本赋值那一行报 `Parse Error: Failed.`——
> 所有经管理器打开的界面（主菜单 / 设置 / 图鉴 / 卡牌详情 / Run 系列）都加载失败。
> 现在 `UIResourceLoader.LoadSceneAsync` 在主线程同步 `ResourceLoader.Load<PackedScene>`，
> 保留 `Task` 签名与取消令牌，状态机与调用方无需改动。若要恢复真正的异步加载，
> 必须先解决"场景内的 C# 脚本无法在线程中实例化"这一前提。
>
> 同一批修复的约定：**场景的脚本 `ext_resource` 只写 `path`、不写 `uid`**——
> `.cs.uid` 是 Godot 机器生成且不进版本库（`.gitignore`），把 UID 写死进提交的场景会在别的机器上
> 变成失效引用（"invalid UID, using text path instead" 警告）。编辑器下次保存会自动补回 UID，
> 届时按本约定手工去掉即可。

### 5.3 载荷（强类型）

- `UiId<TPayload>` 将字符串 Id 与载荷类型在编译期绑定。
- `BaseWin<TPayload>` / `BaseDlg<TPayload>` / `BasePge<TPayload>` / `BasePop<TPayload>` 暴露 `TypedPayload` 属性（运行时校验）。
- **无参窗体**使用 `EmptyPayload` 单例结构，走同一套强类型注册。

---

## 6. UI 管理器 API 形态

### 6.1 注册

声明式注册通过 `GlobalMod.GetUIRegistrations()` → `UIRuntimeRegistry.Register(UIRuntimeEntry)`。

```csharp
// 在各模块的 BaseMod 子类中声明
public static IEnumerable<UIRegistration> GetUIRegistrations()
{
    yield return UIRegistration.Window("Menu", "Src/mod/global/Ui");
    yield return UIRegistration.Dialog("Codex", "Src/mod/global/Ui");
}

// MainRoot._Ready 中统一注册
GlobalMod.RegisterUi(registry);
```

> 2026-09-21 合并：原 UI 管理器规格 的旧表述（`UIRegistration.Window("Menu", "Src/mod/global/Ui")` 式调用**不带归属**、注册一律由 `MainRoot` 调 `GlobalMod.RegisterUi(registry)`）已由本文 §14.5.2（`UIRegistration` 工厂方法首参为 `ownerModId`，漏填=启动报错）与 §14.7 / §14.12.2（注册改由组合根 `FeatureModCatalog`（`AppRoot.Services.Features`）迭代装配，`MainRoot` 不再点名具体 Mod）取代；上面两段示例代码保留为旧写法记录。

`UIRuntimeEntry` 包含 `Id`、`Dir`、`Type`、`BaseOpenOpt`、`OpenOpt`、可选 `RouteMeta`（父子嵌套路由）。

### 6.2 打开/关闭

```csharp
// 强类型入口
Task<UIVo?> OpenAsync<TPayload>(UiId<TPayload> id, TPayload payload, UIOpenOpt? openOpt = null);

// 弱类型入口（保留兼容）
Task<UIVo?> OpenAsync(string id, object? payload = null, UIOpenOpt? openOpt = null);

// 打开子页面（自动解析路由链）
Task<UIVo?> OpenChildAsync(string childId, object? payload = null, UIOpenOpt? openOpt = null);

void Close(string id);
void CloseAllPop();
void CloseAllByType(EUIType type);
void CloseAllExclude(IReadOnlyList<string>? excludeIds = null);

// 返回上一级（关闭当前，恢复上一个全屏 UI）
Task<UIVo?> BackAsync();
```

### 6.3 查询

```csharp
UIVo? GetUIVo(string id);
BaseWin? GetWin(string id);
T? GetWin<T>(string id) where T : BaseWin;
UILayer? GetLayer(EUILayer layer);
bool IsUITop(string Id);
string GetPath(string id);
UIStack NavStack { get; }
```

### 6.4 打开选项合并

`UIOpenOpt` 按优先级链合并：`默认值 → BaseOpenOpt → 层级 → 注册项 OpenOpt → 调用参数`，通过 `MergeInto` 逐级覆盖。

> 2026-09-21 合并：原 UI 管理器规格 的旧表述（`UiManager.MergeInto` 对 `CacheTime` / `AnimType` / `HideBelow` / `NoCover` / `Align` 五个字段**无条件覆盖**）已由本文 §14.6.3（字段显式化 + `MergeFrom`，只覆盖显式设置的字段）与 §14.12.2 取代——`UiManager.MergeInto` 已删除，改名为 `UIOpenOpt.MergeFrom`，读取方一律走 `Effective*`（`EffectiveCacheTime` / `EffectiveAnimType` / `EffectiveHideBelow` / `EffectiveNoCover` / `EffectiveAlign`）。合并链本身（`默认值 → BaseOpenOpt → 层级 → 注册项 OpenOpt → 调用参数`）不变。

### 6.5 Task 完成保证

`UIVo.OpenTaskSource` 关联每次 `OpenAsync` 的 `TaskCompletionSource`，在 `Close`（未打开时）、`Destroy`（加载/预加载失败时）、`OnOpen` 回调、重用时统一 `TrySetResult`，保证 `await OpenAsync` 必定返回。

---

## 7. 与现有 Frame.Mvc 的关系

- **弱耦合**：控制器通过 `IUIManager` 接口依赖，不引用具体界面类。
- `EventDispatcher` 派发 `UIEvent.Open` / `UIEvent.Close` 事件，供外部订阅。
- 业务模块通过 `GlobalMod.GetUIRegistrations()` 声明式注册 UI，初始化时统一注入 `UIRuntimeRegistry`。

---

## 8. 错误处理与边界

- **未注册 Id**：拒绝打开，记录错误。
- **异步竞争**：Dlg 连续打开见 §3；Popup 异步若首版同步 `Instantiate`，仍须文档化后续异步资源扩展点。
- **遮罩点击关闭被禁用**：栈顶仍拦截输入，但点击遮罩不关闭（由参数/子类控制）。

---

## 9. 测试

- **注册表**：未注册拒绝打开（`GD.PushError` + `TaskCompletionSource.SetResult(null)`）。
- **状态机流转**：7 个状态处理器协同工作，`OpenTransitionData` 控制重开/关闭后重开语义。
- **层级**：`UILayerManager.UpdateLayers()` 从顶层向下遍历，`HideBelow`/`NoCover` 控制可见性。
- **导航栈**：`UIStack.Push`/`Back`/`BackAsync()` 行为。
- **强类型载荷**：编译期通过 `UiId<TPayload>` 覆盖，运行时通过 `TypedPayload` 属性校验。
- **缓存超时**：`VORegistry.TickCacheDestroy` 每秒检查 `Cache` 状态 UIVo 是否超时。
- **加载超时**：`UIOpenCoordinator.CheckLoadTimeout` 每帧检查（10s）。
- **自动化**：`Tests/kemo_card.Ui.Tests/UiFrameworkTests.cs` 覆盖无 Godot 节点实例化的逻辑。
- **手动测试**：层级叠放、遮罩点击关闭、动画流程以编辑器运行验证。

---

## 10. 自检记录

- **状态**：已实现，本文档 §12 与代码保持一致。
- **一致性**：Dlg 替换、层级遮挡、强类型载荷、导航栈、缓存策略均已落地。
- **范围**：对象池与多 Viewport 明确排除在非目标外。

---

## 11. 后续步骤

1. 本文档 §1–§11 为设计规格，§12 为当前实现备注。
2. 实现计划已完成（见 `Doc/superpowers/plans/2026-05-15-ui-manager-implementation-plan.md`）。
3. 持续迭代：随代码变更同步更新 §12 实现备注。

---

## 12. 实现备注（与当前代码对齐）

以下描述仓库内 **当前实现** 相对上文规格的细节与命名，便于评审与迭代。

### 12.1 文件布局

```
Src/frame/ui/
├── UiManager.cs              # 门面角色，实现 IUIManager
├── UIRuntimeRegistry.cs      # 运行时注册表 + 路由校验
├── UIVoRegistry.cs           # UIVo 实例管理 + 缓存超时销毁
├── UILayerManager.cs         # 层级创建、查找、可见性刷新
├── UIOpenCoordinator.cs      # 打开队列 + 并发控制 + 加载超时检查
├── UIAnimController.cs       # UI/Mask 动画状态及回调管理
├── UIRegistration.cs         # 声明式注册 record
├── UIStack.cs                # 导航栈（Back/返回）
├── UIVo.cs                   # 界面数据组合容器
├── UILoadContext.cs          # 异步加载上下文（flag、token）
├── UILifecycleState.cs       # 生命周期时间戳
├── UIRuntimeData.cs          # 运行时引用（UI节点、遮罩、层级）
├── UILayer.cs                # 层级节点（Control 子类）
├── Base/
│   ├── BaseUI.cs             # 基础 UI（点击事件管理等）
│   ├── BaseWin.cs            # 窗口基类（生命周期钩子）
│   ├── BaseWinTPayload.cs    # 泛型窗口基类
│   ├── BaseDlg.cs            # 对话框基类
│   ├── BaseDlgTPayload.cs    # 泛型对话框基类
│   ├── BasePge.cs            # 页面基类
│   ├── BasePgeTPayload.cs    # 泛型页面基类
│   ├── BasePop.cs            # 气泡基类
│   ├── BasePopTPayload.cs    # 泛型气泡基类
│   ├── BaseCmp.cs            # 组件基类
│   ├── BaseMask.cs           # 遮罩基类
│   └── IUILifecycleInvoker.cs # 生命周期调用接口
├── States/
│   ├── IUIStateContext.cs    # 状态上下文接口
│   ├── UILoadStateHandler.cs # Load -> PreLoad
│   ├── UIPreLoadStateHandler.cs # PreLoad -> Create/Open
│   ├── UICreateStateHandler.cs # Create -> Open
│   ├── UIOpenStateHandler.cs   # Open（动画 + 事件）
│   ├── UICloseStateHandler.cs  # Close（动画）
│   ├── UICloseDoneStateHandler.cs # CloseDone -> Cache/Destroy
│   └── UIDestroyStateHandler.cs  # Destroy（清理）
├── Def/
│   ├── EUIState.cs / EUIType.cs / EUILayer.cs / EAnimType.cs
│   ├── EUIAlign.cs / EUIAnimState.cs / EUIEvent.cs / UIConsts.cs
│   ├── UIOpenOpt.cs / UIMaskOpt.cs / UIPopOpt.cs
│   ├── DefaultUIOpenOpt.cs
│   ├── UiId.cs / EmptyPayload.cs / OpenTransitionData.cs / UIStateTags.cs
│   └── IUIMeta.cs / IUIVoHandle.cs / IUIPayloadHandle.cs
└── util/
    └── GodotMainThreadSyncContext.cs # 主线程同步上下文
```

### 12.2 启动接线

`MainRoot._Ready` 流程：

1. 创建 `UIRuntimeRegistry`，调用 `GlobalMod.RegisterUi(registry)` 注册所有 UI。
2. 创建 `UIManager` 节点并 `AddChild`。
3. 调用 `uiManager.Init(new UIManagerInitOpt { ... })`：
   - 安装 `GodotMainThreadSyncContext`，确保 `async/await` 续体回到主线程。
   - 创建 `UIVoRegistry`（注入 7 个 `IStateHandler`）。
   - 创建 `UIOpenCoordinator`。
   - 调用 `LayerManager.Init` 构建 `UIRoot` / `UITopRoot` 双根布局及其下层 `UILayer` 节点。
   - 调用 `_registry.Validate()` 校验所有路由。

每帧 `_Process`：
- `_syncContext.Pump()` 泵送异步回调。
- `OpenCoordinator.CheckLoadTimeout()` 检查加载超时。
- `VoRegistry.TickCacheDestroy(delta)` 检查缓存超时销毁。

### 12.3 UIVo 组合结构

`UIVo` 是多个职责单一的子对象的组合容器：

| 子对象 | 职责 |
|---|---|
| `UILifecycleState` | 生命周期时间戳：LoadTime / CreateTime / OpenTime / CloseTime / DestroyTime |
| `UIRuntimeData` | 运行时引用：UI 节点 / Mask / Layer / HideBool |
| `UILoadContext` | 异步加载控制：LoadFlag / PreLoadFlag / LoadToken (CancellationToken) |
| `UIAnimController` | 动画管理：注册/清除 UI 和 Mask 的动画回调 |

### 12.4 状态处理器职责

| 处理器 | 状态 | 核心职责 |
|---|---|---|
| `UILoadStateHandler` | `Load` | 主线程同步加载场景 + 遮罩资源（见 §5.2 注），命中缓存直接跳到 PreLoad |
| `UIPreLoadStateHandler` | `PreLoad` | 调用 `OnPreLoad(done, fail)`，重开时跳过 Create 直接进 Open |
| `UICreateStateHandler` | `Create` | 首次打开初始化、挂入场景树 |
| `UIOpenStateHandler` | `Open` | 播放打开动画、InitEvent、派发 `UIEvent.Open` |
| `UICloseStateHandler` | `Close` | 播放关闭动画 |
| `UICloseDoneStateHandler` | `CloseDone` | 从层级移除、按 `CacheTime` 决定缓存/销毁、更新层级可见性 |
| `UIDestroyStateHandler` | `Destroy` | 取消加载 token、清理动画、QueueFree、移除注册 |

### 12.5 UIManager 子组件

| 组件 | 职责 |
|---|---|
| `UILayerManager` | 创建双根（`UIRoot`/`UITopRoot`）和 `UILayer` 节点；`UpdateLayers()` 从顶层向下遍历刷新可见性 |
| `UIVoRegistry` | `GetOrCreate`/`Get`/`Remove`；`TickCacheDestroy` 每秒检查 Cache 超时 |
| `UIOpenCoordinator` | 打开队列管理、当前打开项 `_currOpening`、队列去重、加载超时（10s）检测（2026-09-19 起场景加载改主线程同步、`Task` 签名与取消令牌保留，见 §5.2 注；超时检查保留为防御） |
| `UIAnimController` | `StartOpenAnim`/`StartCloseAnim`/`StartMaskOpenAnim`/`StartMaskCloseAnim` |

### 12.6 生命周期接口

`IUILifecycleInvoker` 由 `BaseWin` 和 `BaseMask` 显式实现，确保生命周期钩子仅对框架（状态处理器）可见：

- **Win 侧**：`InvokePreLoad` / `InvokeCreate` / `InvokeInitEvent` / `InvokeOpen` / `InvokeOpenAnim` / `InvokeCloseAnim` / `InvokeClose` / `InvokeLayerVisibleUpdate`
- **Mask 侧**：`InvokeMaskOpen` / `InvokeMaskUIOpen` / `InvokeMaskOpenAnimDone` / `InvokeMaskClose` / `InvokeMaskUIClose` / `InvokeMaskUIDestroy` / `InvokeMaskOpenAnim` / `InvokeMaskCloseAnim`

### 12.7 注册与路由

- `UIRegistration`：声明式注册 record，提供 `Window()`/`Dialog()`/`Page()`/`Popup()` 工厂方法。
- `UIRuntimeEntry.RouteMeta` 承载父子路由关系（`ParentId`、`ParentOpenOpt`、`ParentOpenPayload`）。
- `UIOpenChildAsync` 沿路由链逐级解析，自动打开所需父级。
- `UIRuntimeRegistry.Validate()` 检查循环引用和未注册父路由。

### 12.8 导航栈

`UIStack` 是 `List<string>` 封装，提供 `Push`/`Pop`/`Back`/`Clear`。当 `HideBelow = true` 的 UI 打开时自动压栈；`BackAsync()` 关闭当前 UI 并恢复 / 重新打开上一个。

### 12.9 测试

- **自动化**：`Tests/kemo_card.Ui.Tests/UiFrameworkTests.cs` 覆盖 `UIRuntimeRegistry.TryGet` 等无 Godot 节点实例化的逻辑。
- **Godot 树与输入**：层级叠放、遮罩点击关闭、动画流程等以编辑器运行 + 手测为主。
- **场景布局约定**：`Tests/kemo_card.Ui.Tests/UiSceneLayoutTests.cs` 扫描 `Src/**/*.tscn`，强制 §13.1 的折行 Label 约定。

## 13. 场景编写约定（评审中踩过的坑）

### 13.1 容器内的自动换行 Label 必须给最小宽度

```gdscript
[node name="LblDesc" type="Label" parent="Panel/VBox"]
custom_minimum_size = Vector2(400, 0)   # ← 必填：宽度非零
autowrap_mode = 3
```

自动换行的 Label 自身最小宽度几乎为 0（"能折行"意味着它可以任意窄），于是：

- 容器的可用宽度被兄弟节点挤压时，Label 会被压成**竖排单字**甚至 0 宽不可见；
- 容器若是"按内容撑开"的（`PanelContainer` / 提示气泡），整个面板会塌成一条。

显式给出最小宽度（取该列的预期宽度）才能让折行发生在预期列宽上。约定范围是**容器**
（类型名以 `Container` 结尾）的 Label 子节点；锚定在普通 `Control` 上的 Label 由锚点决定宽度，
折行本来就正常（例如 `AlertDlg.LblDesc`），不受此约束。

### 13.2 节点类型的导出属性必须写进 `node_paths`

```gdscript
[node name="PoolList" type="Control" parent="Panel/VBox/Body/Center" node_paths=PackedStringArray("ScrollArea")]
script = ExtResource("3_vlist")
ScrollArea = NodePath("Scroll")     # ← 相对**该节点自身**，不是场景根
```

`.tscn` 里给节点类型导出赋值时，所属节点必须同时声明 `node_paths=PackedStringArray(...)`，
且路径相对该节点自身。缺任一项都会被**静默忽略**，运行期读到 null（`VirtualList` 曾因此一个列表项都不建）。
手写场景后务必用探针核实一遍这些引用是否真的解析成功。

### 13.3 列表/虚拟列表不得按"格子尺寸"拉伸条目

固定尺寸的立绘/卡面预制体（子节点多为 full-rect 锚点）被按容器宽度 `set_size` 会整体变形。
`VirtualList` 的契约是：**条目尺寸由 `ItemTemplate` 决定，`ItemSize` 只是滚动方向的步长**；
只有"整行文本条"才打开 `StretchItemAcrossAxis`。

### 13.4 流动（换行）布局：`FlowLayout`

需要"排满一行/一列后自动换行"的网格（卡牌网格、角色池、卡组缩略）用 `FlowLayout = true`：
**主轴 = 行内排列方向，横轴 = 换行方向，且横轴恒等于滚动轴**——`IsVertical = true` 时
从左到右排满一行换行（竖向滚动），`false` 时从上到下排满一列换列（横向滚动）。
行容量按可视主轴长度**现算**（`floor((行长度 + Spacing) / (格子主轴尺寸 + Spacing))`），
因此容器变宽/变窄、窗口缩放都会即时改变每行条目数，不需要重新 SetData。

| 配置 | 作用 |
| --- | --- |
| `FlowItemSize` | 单元格尺寸（宽 × 高）；留空（`Vector2.Zero`）则**自动测量一次** `ItemTemplate`（取"自身 Size"与"合并最小尺寸"的较大者，失败退回 `ItemSize` 并告警）。网格类列表建议显式填写，避免主题/字体让模板尺寸漂移后行容量跳变 |
| `Spacing` | 同一条线（行/列）内的条目间距 |
| `LineSpacing` | 相邻两条线（行/列）的间距；`< 0`（默认）表示复用 `Spacing` |
| `ItemSize` | **流动布局下不参与计算**（它只是线性布局的滚动步长） |
| `StretchItemAcrossAxis` | 流动布局下无意义，忽略 |

可见区按"整行/整列"取：行容量不整除时会多取一条线缓冲，绝不出现半行渲染。
行长度取 `ScrollArea` 的**主轴**尺寸（与 `StretchItemAcrossAxis` 同口径），可视区尚未布局完成
（行长度 0）时先按每行 1 个渲染，`Resized` 回调会立刻重算——所以首帧不会错位。
注意滚动条会占用可视区边缘：格子尺寸刚好塞满时建议留 ~14px 余量，否则最后一列会被滚动条压住几像素。
布局计算抽在 `VirtualListFlowLayout`（纯函数，不碰节点），由
`Tests/kemo_card.Ui.Tests/VirtualListFlowLayoutTests.cs` 覆盖；
节点级接线用真实模板 + `ScrollContainer` 的探针核实（位置/内容尺寸/自动测量三处）。
模板/主题尺寸变化后调用 `InvalidateFlowMeasurement()` 重新测量。

### 13.5 内容可能超出设计区：`FitScaleBox` 兜底缩放，禁止最小尺寸溢出屏幕

显示基线是 1280×720 viewport + `canvas_items` + `expand`（见 `project.godot`），可见设计区只会变大、不会变小。
因此"界面装不下"不会表现为窗口裁剪，而是**锚定布局按根容器的合并最小尺寸把整棵子树撑开并居中溢出屏幕**：
2026-09-25 修复前的战斗界面（`CombatWin`）合并最小尺寸为 1360×752（3 敌人时 1482×752），在 1280×720 与
1920×1080 下都左右各裁掉数十像素——左侧队伍栏与右下「确定」按钮缺角。

约定：

- 内容确实可能超出设计区的界面（战斗界面、未来多单位战斗），把根容器包进 `FitScaleBox`：
  空间富余时子节点照常铺满；不足时保持需求尺寸整体等比缩小并居中（只缩不放），任何宽高比都不裁切、不错位；
  子节点 `minimum_size_changed`（战斗中动态增删敌人）会自动重排。
- 安全留白放 `FitScaleBox` 自身的锚点偏移（如战斗界面的 16/12 四边），子节点锚点由组件在运行时接管。
- 仍要把常态内容的合并最小尺寸压在设计区内，缩放只作兜底（战斗界面把常态缩放压到约 0.99）。
- 检查/回归：用探针打印根容器 `get_combined_minimum_size()` 与关键控件 `get_global_rect()`，确认都在可见区内；
  缩放计算抽在 `FitScaleMath`（纯函数，由 `Tests/kemo_card.Ui.Tests/FitScaleMathTests.cs` 覆盖）。

---

## 14. 界面归属与统一订阅生命周期（原 ui-mod-binding 规格 §1–§13）

> **来源**：`Doc/superpowers/specs/2026-09-15-ui-mod-binding-design.md`（原标题《界面与功能 Mod 绑定 + 统一订阅生命周期设计》）。2026-09-21 整篇并入本文，原文归档于 `Doc/archive/superpowers/specs/2026-09-15-ui-mod-binding-design.md`。
> **原状态**：活规格（2026-09-15 起）。
> **影响面**：`Src/frame/ui/`、`Src/mod/*/Ui/`、`Src/MainRoot.cs`、`Src/mod/ModFactory.cs`。
> **权威链（合并后重定向）**：原文开篇的权威链为「服从 ui-manager-design（UI 管理器与层级/状态机，见本文 §1–§13）与 event-dispatcher-design（事件分发器，见本文 §15）；本文只补充**归属**与**订阅生命周期**两条新规则，不重定义层级、状态机与遮罩语义」——这两份规格自 2026-09-21 起已是本文 **§1–§13** 与 **§15**（原相对链接已按合并结果重定向，不再指向已归档文件）。
> **段号约定**：本章内出现的 `§N` / `§N.M` 一律是**原 ui-mod-binding 规格段号**（保留原样，`Doc/AGENT.md` §3 与 `Src/`、`Tests/` 注释按旧段号引用仍可定位）；本章新段号一律写作 `§14.N`。本章提到的「ui-manager 规格」= 本文 §1–§13，「event-dispatcher 规格」= 本文 §15。映射见文首「段号索引」。

---


### 14.1 目标与非目标（原 §1）

#### 14.1.1 目标（原 §1.1）

| 编号 | 目标 |
|------|------|
| **G1 归属** | 每个界面声明**唯一归属的功能 Mod**，并且只能拿到该 Mod 的门面；归属在注册期即校验，漏填=启动报错 |
| **G2 统一订阅生命周期** | 订阅只经一个记账对象登记；框架级生命周期 `OnExitTree` 保证「离场必解绑」；子类不再 override 引擎的 `_ExitTree` |
| **G3 Mod 级界面生命周期** | 可按 Mod 批量关闭/销毁界面并清理其订阅（Run 结束、后续 Mod 卸载都要用） |
| **G4 修掉现存缺陷** | 修复调查发现的 D1–D5（见 §2），它们都是「订阅生命周期没有统一出口」的直接后果 |

#### 14.1.2 非目标（YAGNI）（原 §1.2）

- **内容 Mod 提供界面**：`Config/mods/*` 目前是纯数据（含脚本效果），不含界面声明。若将来要支持，属「脚本驱动 UI 声明」的独立课题，本文不涉及，但接口预留（`IUiOwner` 以 `ModId` 为键，不假设实现是 C# 类）。
- **反射/程序集扫描自动发现 Mod**：违背组合根**纯手工装配**取向（AGENT.md §2），且对 AOT/裁剪不友好。采用**单一显式清单**（§7）。
- **重做 UI 状态机 / 层级 / 遮罩 / 动画**：见 ui-manager 规格。
- **跨 Mod 界面复用**（一个界面同时属于两个功能）：不支持；需要复用的部分下沉到 `frame` 组件。

---

### 14.2 现状与问题（含证据）（原 §2）

#### 14.2.1 两套并行的绑定实现（原 §2.1）

| 实现 | 覆盖对象 | 记账方式 |
|------|----------|----------|
| `BaseUI`（`_clickActions` / `_guiInputHandlers` / `_unbindActions` + `OnClicks` / `Bind` / `ClearLifeCycle`） | `BaseWin` → `BaseDlg`/`BasePge`/`BasePop`；`BaseCmp` → 3 个 `Setting*Row` | ✅ 有记账，`_ExitTree` 统一解绑 |
| **各组件手搓**（自有 `_bound` + `InitEvent` + `Unbind`/`OnUnbind` + 裸 `+=`/`-=`） | `BaseKemoButton : Button`、`BasePager : Control`、`BaseDlgComp : Control`、`BaseCardItem : Control`、`BaseCharacterItem : Control`、`VirtualList : Control`、`KeywordTipService : CanvasLayer` | ❌ 各自为政 |

这些组件**无法继承** `BaseUI`——Godot C# 要求节点类型出现在继承链上（`Button` / `CanvasLayer` 不是 `Control` 的子类关系可复用）。所以问题不是"它们忘了用基类"，而是**框架只提供了基类形式的实现，没有提供可组合的实现**。

#### 14.2.2 D1（真实缺陷，高）：`_eventsBound` 守卫击败框架契约 → 关闭一次后重开，控件失联（原 §2.2）

`CodexDlg:58-65`、`SettingDlg:54-61`、`CardDetailsDlg:34-41`、`CharacterDetailsDlg:34-41`、`StorySelectDlg:33-40` 统一写成：

```csharp
protected override void InitEvent()
{
    if (_eventsBound) return;          // ← 提前返回
    _eventsBound = true;
    OnClicks(_btnAdd, OnAddPressed);            // 走框架记账：_ExitTree 时被解绑
    _obField.ItemSelected += OnFieldSelected;   // 裸订阅：无人解绑
}
```

而 `_eventsBound` 在 5 个文件里**没有任何一处复位**（无 `OnUnbind` override；`SettingDlg:161` / `CardDetailsDlg:58` / `CharacterDetailsDlg:58` 的 `OnClose` 也不碰它）。

失败链：

1. `CacheTime` 取默认 30000 → 关闭走 Cache 分支；
2. `UICloseDoneStateHandler:41` 执行 `RemoveChild` → 节点离树 → `BaseUI._ExitTree` → `ClearLifeCycle()` **解绑全部 `OnClicks`/`Bind` 项**；
3. 30 秒内重开 → 状态机 Load→PreLoad→Create→Open，`UIOpenStateHandler:43` 调 `InvokeInitEvent()`；
4. `InitEvent` 因 `_eventsBound == true` **提前返回** → `OnClicks` 注册的控件**永不重新绑定**。

**结果**：打开设置→关闭→再打开，设置行全部失效；图鉴的「添加/搜索」按钮同理。裸订阅反而存活，造成"一部分控件活着、一部分死了"的不可推理状态。

`UIOpenStateHandler:40-43` 的注释已经写明契约（"进缓存 RemoveChild 会触发 ClearLifeCycle 卸掉 OnClicks；重开也必须重新 InitEvent"）——**契约是对的，但没有强制力**。

#### 14.2.3 D2：约 40 处裸 `+=` 不在框架覆盖范围内（原 §2.3）

`SettingDlg`(14)、`CodexDlg`(7)、`CardDetailsDlg`(2)、`CharacterDetailsDlg`(1)、`StorySelectDlg`(1)、`VirtualList`(1)、`BaseKemoButton`(7)、`BasePager`(6)、`BaseCardItem`/`BaseCharacterItem`(各 2) 等。与 D1 叠加后行为不可推理。

#### 14.2.4 D3：没有"归属"概念（原 §2.4）

`UIRegistration` / `UIRuntimeEntry` / `UIVo` 均无 `OwnerModId`。因此无法"关闭并销毁某功能的全部界面及其订阅"，也无法校验"这个界面是谁的"。

#### 14.2.5 D4：界面取数靠服务定位，"基于功能"只是约定（原 §2.5）

界面通过 `AppRoot.Services.Xxx`（12 处：`CodexDlg:347,404,576,634`、`BaseCardItem:213,514`、`SettingDlg:121,373,430`、`RunMainWin:64`、`StorySelectDlg:60,75`、`CharacterArtLoader:118`）与静态门面（`RunRuntime.Current`）跨模块取数据。`CodexDlg`（global）完全可以摸到 Run 的运行时，反之亦然。

#### 14.2.6 D5（新发现，决策 1 的前置阻塞）：`UIOpenOpt` 合并在 5 个字段上无条件覆盖（原 §2.6）

`UiManager.MergeInto` 对 `CacheTime` / `AnimType` / `HideBelow` / `NoCover` / `Align` 是**无条件赋值**：

```csharp
target.CacheTime = source.CacheTime;   // 只设了 CacheTime 的 source，也会把其余 4 个字段一并覆盖
target.AnimType  = source.AnimType;
target.HideBelow = source.HideBelow;
target.NoCover   = source.NoCover;
target.Align     = source.Align;
```

于是 `UIRegistration.Window(RunMain) with { OpenOpt = new UIOpenOpt { CacheTime = 0 } }` 会**静默把 `BaseOpenOpt` 给的 `HideBelow = true` / `Align = Full` 覆盖成 false / Center**。决策 1（Run 结束销毁 `RunMainWin`）正需要逐界面覆写 `CacheTime`，故必须先修此缺陷。

---

### 14.3 方案总览（原 §3）

```
层 1  BindingScope + 信号糖 + 框架级 OnExitTree      ← G2，可组合，不依赖继承
层 2  IUiOwner 归属契约 + 归属门面解析               ← G1 + D4
层 3  CloseAllByOwner / 逐界面选项覆写               ← G3 + D5
组合根 FeatureModCatalog（唯一显式清单）             ← G1 的装配侧
```

三层互不依赖，可分批落地；层 1 是其余两层的地基。

---

### 14.4 层 1：BindingScope 与框架级生命周期（原 §4）

#### 14.4.1 `BindingScope`（纯 C#，可单测）（原 §4.1）

放在 `Src/frame/ui/BindingScope.cs`，**不引用任何 Godot 类型**，因此可被现有「不依赖 Godot 场景树」的测试直接覆盖。

```csharp
namespace KemoCard.Frame.UI;

/// <summary>
/// 一个界面/组件的订阅登记簿。所有订阅（Godot 信号、事件总线、静态门面事件）
/// 都必须经此登记，离场时一次解绑。
/// </summary>
public sealed class BindingScope
{
    private readonly List<Action> _unbinders = [];

    public bool HasBindings { get; }
    public int Count { get; }

    /// <summary>登记一个解绑动作（不立即订阅）。</summary>
    public void Add(Action unsubscribe);

    /// <summary>登记并立即订阅。</summary>
    public void Bind(Action subscribe, Action unsubscribe);

    /// <summary>逆序执行全部解绑并清空；可重复调用。</summary>
    public void UnbindAll();
}
```

规则：

- **`UnbindAll()` 之后本对象仍可继续登记。** 这是**硬性要求**，不是宽松：关闭走缓存时节点只是 `RemoveChild`（未释放），30 秒内重开会再次 `InvokeInitEvent` 重新订阅——若解绑后禁止订阅，重开必崩。因此 `BindingScope` **不持有"已解绑"状态**，`UnbindAll` 只清空账本。
- `UnbindAll` **逆序**执行；单个解绑抛异常时记录日志（`AppLog.Error`）并继续，避免一个坏解绑阻断其余清理。
- `UnbindAll` 幂等：账本已空时再调用是无操作。
- **不再需要各界面自写 `_eventsBound` / `_bound` 守卫**：D1 的根因就是守卫没复位，而账本式登记不需要守卫。

#### 14.4.2 信号糖：一个扩展方法集，两种宿主共用（原 §4.2）

放在 `Src/frame/ui/BindingScopeSignals.cs`。因为 `BaseUI`（Control）与 `BaseKemoButton`（Button）无法共享基类，**糖必须挂在 `BindingScope` 上而不是基类上**：

```csharp
public static class BindingScopeSignals
{
    public static void OnPressed(this BindingScope b, BaseButton button, Action handler);
    public static void OnItemSelected(this BindingScope b, OptionButton option, Action<long> handler);
    public static void OnTextSubmitted(this BindingScope b, LineEdit edit, Action<string> handler);
    public static void OnTextChanged(this BindingScope b, LineEdit edit, Action<string> handler);
    public static void OnValueChanged(this BindingScope b, Range range, Action<double> handler);
    public static void OnTabChanged(this BindingScope b, TabContainer tabs, Action<long> handler);
    public static void OnTreeItemActivated(this BindingScope b, Tree tree, Action handler);
    public static void OnTreeItemSelected(this BindingScope b, Tree tree, Action handler);
    public static void OnMouseEnterExit(this BindingScope b, Control c, Action enter, Action exit);
    public static void OnResized(this BindingScope b, Control c, Action handler);
    public static void OnFocusChanged(this BindingScope b, Control c, Action<bool> handler);
    public static void OnGuiInputLeftClick(this BindingScope b, Control c, Action handler); // 原 OnClicks 的非 Button 分支
    public static void OnSignal<THandler>(this BindingScope b, THandler h,
        Action<THandler> add, Action<THandler> remove) where THandler : Delegate;  // 兜底
}
```

`BaseUI.OnClicks` 保留为 `OnGuiInputLeftClick` / `OnPressed` 的转发糖（现有调用点不必改），但**实现改为登记进 `BindingScope`**，`_clickActions` / `_guiInputHandlers` 两个字典删除。

#### 14.4.3 框架级生命周期 `OnExitTree`（决策 2）（原 §4.3）

**不改引擎函数语义**：`_ExitTree` 成为框架的**唯一入口且 sealed**，子类改用框架级 `OnExitTree()`。

`BaseUI`：

```csharp
protected BindingScope Binder { get; } = new();

// 唯一入口：子类不得 override（sealed）。引擎回调只做转发。
public sealed override void _ExitTree()
{
    OnExitTree();                    // 1. 子类补充清理（此时订阅仍有效）
    Binder.UnbindAll();              // 2. 框架保证解绑（子类忘了也解）
    OwnerBus?.OffCaller(this);       // 3. 功能总线按 caller 清理
    GlobalEvents.Bus.OffCaller(this);// 4. 跨功能总线
    base._ExitTree();
}

/// <summary>框架级离场生命周期：子类只做非订阅类清理。订阅请一律走 Binder。</summary>
protected virtual void OnExitTree() { }
```

`BaseCmp`、3 个 `Setting*Row`、5 个对话框统一改 override `OnExitTree()`；`BaseCmp.OnUnbind()` 与 5 个对话框的 `_eventsBound` **删除**（职责被 `Binder` 吸收）。

**非 `BaseUI` 家族的 UI 节点**（`Button` / `CanvasLayer` / 独立 `Control`）无法继承，采用**统一 6 行模式**（写进 AGENT.md §3，作为硬性约定）：

```csharp
public partial class BaseKemoButton : Button
{
    private readonly BindingScope _binder = new();
    private BindingScope Binder => _binder;

    // 订阅登记必须挂 _EnterTree：_Ready 每个节点只调用一次，
    // 缓存重开（RemoveChild → AddChild）不会再次触发 _Ready。
    public override void _EnterTree()
    {
        base._EnterTree();
        _binder.OnResized(this, OnResized);   // 订阅一律 _binder.OnXxx(...) / _binder.Bind(...)
    }

    // 唯一入口，sealed：子类改 override OnExitTree
    public sealed override void _ExitTree()
    {
        OnExitTree();
        _binder.UnbindAll();
        base._ExitTree();
    }

    protected virtual void OnExitTree() { }
}
```

> **登记点必须是 `_EnterTree`（2026-09-15 修正）**：Godot 的 `_Ready`「每个节点只被调用一次」，而缓存重开是 `RemoveChild` + `AddChild`——不会再次触发 `_Ready`（需显式 `RequestReady()`，全仓无此调用）。因此把订阅写在 `_Ready` 的组件，会在首次离场被 `Binder` 解绑后**永不重新登记**。`_Ready` 只保留一次性初始化（尺寸 / 样式 / 外部数据同步）。
>
> **2026-09-21 合并补记（`VirtualList`）**：本次评审把 `VirtualList`（`BaseUI` 家族之外、`Control` 子类，采用同一 6 行模式）的订阅登记时机**同样定在 `_EnterTree`**——由 `_Ready` 改为 `_EnterTree`（`Src/mod/global/Ui/VirtualList.cs`：`_EnterTree` → `EnsureInitialized()` → `_binder` 登记订阅；`_Ready` 只留一次性初始化），否则缓存重开后列表的滚动/悬停订阅永久失效。见 §14.12 步骤 4d 与 §14.12.2「组件订阅登记点」行。

> 这类节点若同时也订阅了功能总线，需同 `BaseUI` 一样补 `OffCaller(this)`；由 `BindingScope` 不负责总线，交由各节点显式调用（总线订阅罕见，且 `BaseUI` 已覆盖绝大多数场景）。

#### 14.4.4 解绑职责表（原 §4.4）

| 订阅类型 | 登记方式 | 解绑者 |
|---|---|---|
| Godot 信号（同节点子树内） | `Binder.OnXxx(node, handler)` | `Binder.UnbindAll()`（框架，`_ExitTree`） |
| Godot 信号（跨节点、如 `KeywordTipService._currentAnchor.TreeExited`） | `Binder.Bind(() => x.TreeExited += h, () => x.TreeExited -= h)` | 同上 + 调用方需自行 `IsInstanceValid` 判断 |
| 功能内部总线 | `Binder.OnBusEvent(key, handler)`（caller 固定 this） | `OwnerBus.OffCaller(this)` + `Binder` |
| 跨功能总线 `GlobalEvents.Bus` | `Binder.OnGlobalEvent(key, handler)` | `GlobalEvents.Bus.OffCaller(this)` |
| 静态门面事件（`RedDotService.OnStateChanged` 等） | `Binder.Bind(() => X += h, () => X -= h)` | `Binder.UnbindAll()` |

**用事件总线替代静态 C# 事件**是推荐做法（`RedDotService.OnStateChanged` 已因此类问题被修过一次）；此处只保证**即使用静态事件也能被统一登记**。

---

### 14.5 层 2：归属契约（原 §5）

#### 14.5.1 归属契约：声明与门面**分离**（`IUiOwner` 单接口方案已废弃）（原 §5.1）

**为什么不能用「功能 Mod 实例实现 `IUiOwner`」**：`GlobalMod` 是 bootstrap 期创建的长生命周期对象，而 **`RunMod` 是每次 Run 现造的会话级对象**（`RunRuntime.CreateController()`）。但 `UIRuntimeRegistry` 只在 `MainRoot.InitUIManager()` 装配**一次**——若要实例化 `IUiOwner` 才能拿到界面清单，就必须在启动期先造一个 `RunMod`，于是出现"注册用一个实例、玩法用另一个实例"的双实例语义。

因此把两件事拆开：

**① 声明（启动期，静态即可）** —— `Src/mod/FeatureModCatalog.cs`，全项目唯一列出功能界面的地方：

```csharp
/// <summary>一个功能 Mod 的界面声明：只需要「怎么声明」，不需要实例。</summary>
public sealed record FeatureUiDeclaration(string ModId, Func<IEnumerable<UIRegistration>> Declare);

internal static class FeatureModCatalog
{
    /// <summary>按装配顺序返回全部声明界面的功能 Mod。</summary>
    public static IReadOnlyList<FeatureUiDeclaration> Features { get; } =
    [
        new("global", GlobalMod.GetUIRegistrations),
        new("run", RunMod.GetUIRegistrations),
    ];
}
```

**② 门面（打开期，动态解析）** —— 由组合根实现，因为 Run 的门面是**会变的**（随会话切换）：

```csharp
// KemoCard.Frame.UI
public interface IUiFacadeProvider
{
    /// <summary>按归属 Mod id 解析该功能暴露给自家界面的门面；无此功能返回 null。</summary>
    object? ResolveUiFacade(string ownerModId);
}
```

`ModStartupResult` 实现该接口（`"global" → GlobalController`，`"run" → RunRuntime.Current`），并交给 `UIManager`/`BaseUI` 使用。这样：

- **不需要** `AttachController` 反向引用，也不需要让 `BaseMod` 知道 UI；
- Run 的界面在每次会话中都能拿到**当前**的 `RunController`，而不是启动期那个空壳；
- `frame` 仍然不依赖 `mod`（provider 由组合根注入）。

#### 14.5.2 归属落到运行时（原 §5.2）

```csharp
// UIRegistration
public required string OwnerModId { get; init; }        // 必填

// UIRuntimeEntry / UIVo
public required string OwnerModId { get; init; }        // 由注册项带下来
```

工厂方法改为必传 owner，杜绝漏填：

```csharp
UIRegistration.Window(ownerModId: "global", id, dir, cacheTime: null);
UIRegistration.Dialog(ownerModId: "run",    id, dir);
```

#### 14.5.3 注册期硬校验（`UIRuntimeRegistry.Validate()` 增补）（原 §5.3）

1. `OwnerModId` 非空；
2. 同一 `Id` 不得被两个不同 owner 注册（同 owner 重复注册覆盖，沿用现有语义并告警）；
3. owner 必须出现在组合根的已装配 Mod 清单中（防止拼错 id）。

任一条不满足 → `AppLog.Error` 且**注册视为失败**（沿用现有 `Validate` 的"报告不抛"风格，但新增一条 `HasErrors` 供启动期断言）。

#### 14.5.4 归属门面解析（决策 3：统一修正 D4）（原 §5.4）

`BaseUI` 提供受限取数入口，**取代 `AppRoot.Services.Xxx` 直连**：

```csharp
// BaseUI
protected TFacade Facade<TFacade>() where TFacade : class;
// 实现：由 UIVo.OwnerModId → IUiFacadeProvider.ResolveUiFacade(ownerModId) → 类型校验
// 类型不符（试图拿别的功能的门面）→ 抛 InvalidOperationException，消息含 owner 与期望类型
```

**实测迁移映射**（实施时核对：UI 目录下共 18 处 `AppRoot.Services`，其中**大多数不是跨功能取数**）：

| 界面 / 位置 | 实际访问对象 | 处理 |
|---|---|---|
| `SettingDlg:121,373,430` | `GlobalController`（本就是 global 功能） | ✅ 改 `Facade<GlobalModController>()` |
| `CodexDlg:340,397,569,627`、`CardDetailsDlg:67,100,115`、`CharacterDetailsDlg:67`、`RunMainWin:64`、`StorySelectDlg:68` | `ContentModPipeline.Registry.Store`（**内容定义，属 frame 级共享数据**） | 保留：这不是跨功能取数，而是读内容注册表；强行走门面反而是错的抽象 |
| `BaseCardItem:213,514`、`BaseCharacterItem:188`、`CharacterArtLoader:118` | 同上（资源路径 / 内容注册表） | 保留（组件无 `UIVo`，且性质同上） |
| `StorySelectDlg:53` | `GlobalController` → 构造 `GlobalPersistentCondContext` | ⚠️ **真正的跨功能读**：run 的界面读 global 的解锁账本。需要 frame 级的 `IPersistentCondContext` 提供者才能干净解决，**本轮不做**，已在代码处注明 |

> **结论修正**：决策 3 的实际价值主要在「同一功能的取数改为类型化门面」，而不是"消灭所有 `AppRoot.Services`"。
> 后者中大部分是读内容注册表（frame 级共享），强改会引入错误抽象。跨功能读只有 `StorySelectDlg` 一处，另案处理。

> **不追求一次性删掉 `AppRoot.Services`**：它仍是组合根的合法出口（`MainRoot`、`RunRuntime` 等非界面路径继续用）。本项只约束**界面**不得跨功能直取。

---

### 14.6 层 3：Mod 级界面生命周期（原 §6）

#### 14.6.1 `UIManager` 增补（原 §6.1）

```csharp
/// <summary>关闭某功能 Mod 的全部界面。</summary>
public void CloseByOwner(string ownerModId, bool destroy = false);

/// <summary>注销某功能 Mod 的全部界面（连 UIVo 一并移除，防 Run 结束后残留）。</summary>
public void UnregisterOwner(string ownerModId);
```

`destroy: true` 走 `EUIOpenOpt`/`CacheTime = 0` 语义（关闭即 Destroy）；`false` 走缓存。两者都复用 §4 的 `_ExitTree` 解绑，无需额外清理代码。

#### 14.6.2 决策 1：Run 结束销毁 `RunMainWin`（原 §6.2）

- `RunMod.GetUIRegistrations()` 中 `RunMain` 注册为 `CacheTime = 0`（关闭即销毁）；
- `RunRuntime.Abandon()` / `RunRuntime.CreateNew()` 的 `_current?.Dispose()` 之前，调用 `UIManager.Instance?.CloseByOwner("run", destroy: true)`；
- `StorySelectDlg` **保持缓存**（它是从菜单反复进出的短生命周期弹窗，销毁会丢失滚动位置等）。

#### 14.6.3 决策 3 前置：`UIOpenOpt` 字段显式化（修 D5）（原 §6.3）

把 5 个无条件覆盖字段改为可空，`MergeInto` 改为**仅在 source 显式设置时覆盖**：

```csharp
public int?      CacheTime { get; set; }   // null = 未指定
public EAnimType? AnimType { get; set; }
public bool?     HideBelow { get; set; }
public bool?     NoCover   { get; set; }
public EUIAlign? Align     { get; set; }
```

```csharp
private static void MergeInto(UIOpenOpt target, UIOpenOpt? source)
{
    if (source == null) return;
    if (source.Layer     is { } layer)     target.Layer = layer;
    if (source.CacheTime is { } cacheTime) target.CacheTime = cacheTime;   // ← 其余同理
    ...
}
```

- `DefaultUIOpenOpt.Value` 作为**基线**保持字段全非空（`CacheTime = 30000` 等），保证合并结果永远具体；
- 读取点改用 `vo.OpenOpt.CacheTime ?? 30000` 形式（`UICloseDoneStateHandler`、`UIOpenStateHandler`、`UICloseStateHandler`、`UILayerManager`、`UiManager.IsUITop`）；
- `DefaultUIOpenOpt.ForType` 的 `<remarks>`（"未显式设置的字段须与 Value 同值才对幂等"）随之删除——**这条脆弱约束被根治**。

> 2026-09-21 合并：原 UI 管理器规格 §6.4 的旧表述（`UIOpenOpt` 经 `UiManager.MergeInto` 逐级覆盖，5 个字段无条件赋值 ⇒ 只设 `CacheTime` 的 source 会连带覆盖 `HideBelow` / `Align`）已由本节取代——`UiManager.MergeInto` 已删除，改为 `UIOpenOpt.MergeFrom` + `Effective*`（实现见 §14.12.2「选项合并」行）。

---

### 14.7 组合根（决策 3 的装配侧）（原 §7）

`FeatureModCatalog`（§5.1）是唯一列出功能界面的地方。`MainRoot` 收敛为：

```csharp
foreach (var feature in AppRoot.Services.Features)
    foreach (var reg in feature.Declare())
        registry.Register(reg.ToRuntimeEntry());
```

> 仍是**显式手工装配**（符合 AGENT.md 对组合根的取向），但"新增一个功能 Mod 要改几处"从 **2 处（`MainRoot` + ModFactory）降到 1 处**。
> 门面解析由 `ModStartupResult` 实现的 `IUiFacadeProvider` 提供（§5.1 ②），因此声明与实例生命周期可以完全解耦。

---

### 14.8 生命周期时序（原 §8）

```
Open:   Load → PreLoad → Create(AddToNode/InvokeCreate) → Open(InvokeInitEvent → Binder 订阅)
Close:  Close(动画) → CloseDone(RemoveChild) → [_ExitTree: OnExitTree → Binder.UnbindAll → 总线 OffCaller]
                 ├─ CacheTime > 0 → Cache ─(超时/被顶掉)→ Destroy
                 └─ CacheTime = 0 → Destroy(QueueFree)
重开:   缓存命中 → PreLoad → Create → Open → InvokeInitEvent（Binder 重新订阅，因 UnbindAll 已复位账本）
Mod 卸载: CloseByOwner(destroy) → 各界面走上述 Close 流程 → UnregisterOwner
```

**关键不变量**：`InitEvent` 每次 Open 必被调用且**必能生效**（不再有守卫提前返回）；`UnbindAll` 每次离树必执行；两者配平 => 订阅数不随开关次数增长。

---

### 14.9 测试（原 §9）

现有 UI 测试刻意不依赖 Godot 场景树（`UiManagerDlgSwitchTests` 注释），因此：

| 可纯测（新增） | 说明 |
|---|---|
| `BindingScope` | `Bind` 立即订阅；`UnbindAll` 逆序执行、幂等；解绑后 `Bind` 抛异常；单个解绑抛异常不阻断其余 |
| `UIOpenOpt` 合并幂等 | 只设 `CacheTime` 的 source 不得覆盖 `HideBelow`/`Align`（D5 回归） |
| `UIRuntimeRegistry.Validate` | owner 缺失 / 跨 owner 同 id / owner 不在清单 → `HasErrors` |
| `UIManager` 归属过滤 | `GetByOwner` 只返回该 owner 的条目 |
| `UIVo.OwnerModId` | 由注册项正确带下来 |

| 需 Godot（暂用人工/后续接入 Godot 测试工程） | 说明 |
|---|---|
| D1 守护：打开→关闭→重开→`OnClicks` 控件仍可用 | 最关键的回归，作为手工验收清单第 1 条 |
| `_ExitTree` 后订阅计数归零 | 可用 `EventDispatcher.Has` 间接断言 |

`BindingScope` 可纯测是把它抽成独立类的**主要动机之一**。

---

### 14.10 迁移步骤（每步可独立编译与验证）（原 §10）

| 步 | 内容 | 破坏性 |
|---|---|---|
| **1** | 新增 `BindingScope` + `BindingScopeSignals` + 单测 | 无 |
| **2** | `UIOpenOpt` 字段显式化 + `MergeInto`（修 D5）+ 单测 | 低（改读取点为 `??` 兜底） |
| **3** | `BaseUI` 接 `Binder`、`_ExitTree` sealed → `OnExitTree`；`BaseCmp` 跟进 | 中（框架内） |
| **4** | **修 D1/D2**：5 个对话框删 `_eventsBound`、40 处裸 `+=` 换糖；5 个非 BaseUI 组件接 6 行模式 | 中，收益最大 |
| **5** | 归属契约：`IUiOwner` + `OwnerModId` + `Validate` 三条 + 单测 | 低 |
| **6** | 组合根 `FeatureModCatalog` + `MainRoot` 收敛 | 低 |
| **7** | `CloseByOwner` / `UnregisterOwner` + Run 结束销毁（决策 1） | 低 |
| **8** | 界面取数改 `Facade<T>()`（12 处，分批） | 中，可分批 |
| **9** | 同步 AGENT.md §3 硬性约定与 §5 模块入口；INDEX 增补 | 无 |

> 2026-09-21 合并：上表**步骤 5** 里的 `IUiOwner` 单接口方案已由本章 §14.5.1 废弃（改为静态**声明** `FeatureModCatalog` + 动态**门面** `IUiFacadeProvider`，避免 `RunMod` 会话级对象的双实例语义）；该步的落地形式以 §14.5.1 / §14.5.2 / §14.12.2 为准（`UIRegistration.OwnerModId` 必填 + `Validate` 三条硬校验 + `HasErrors` + `GetByOwner`）。

步骤 4 是**纯缺陷修复**，即使其余方案要调整也建议优先落地。

---

### 14.11 风险与兼容（原 §11）

| 风险 | 处理 |
|---|---|
| `_ExitTree` 收敛 sealed 会让现有 override 编译失败 | **这正是目的**：强制暴露所有绕过框架的清理点。已知受影响 8 处（`BaseCmp`、`VirtualList`、`BaseDlgComp`、`BaseKemoButton`、`BasePager`、`BaseCardItem`、`BaseCharacterItem`、`KeywordTipService`），全部改 `OnExitTree` |
| `BindingScope` 解绑后仍可重新登记（无"已解绑"态） | 这是缓存重开的必要条件；若未来确需"永久离场后禁止订阅"，应作为显式 `Close()`（真销毁）路径的附加状态，而不是 `UnbindAll` 的副作用 |
| `Facade<T>()` 迁移 12 处可能触碰未测路径 | 分批提交，每批跑全量测试；`RunMainWin`/`CodexDlg` 优先 |
| `MergeInto` 显式化改变合并语义 | 由「Default 基线全非空」保证结果不变；补 3 条回归测试（含 D5 场景） |
| `KeywordTipService` 是 `CanvasLayer`（顶层常驻，几乎不离树） | 它的 `TreeExited` 订阅本就带 `IsInstanceValid` 防御，单独保留现状，只补 `Binder` 登记 |

---

### 14.12 自检记录（原 §12）

- [x] 与 ui-manager 规格不冲突：本文不改层级/状态机/遮罩/动画语义，只加归属与订阅生命周期
- [x] 依赖方向：`IUiOwner` / `BindingScope` 均在 `frame/ui`，不引入 `frame` → `mod` 引用
- [x] 不新增 Godot 依赖到可测代码：`BindingScope` 为纯类
- [x] 决策落地：①Run 结束销毁（§6.2）②框架级 `OnExitTree`，不 override 引擎函数（§4.3）③统一修正含归属门面与 D5（§5.4/§6.3）④先规格后代码（本文）
- [x] 步骤 1 `BindingScope` + `BindingScopeSignals` + 单测（8 例）
- [x] 步骤 2 `UIOpenOpt` 字段显式化 + `MergeFrom` + D5 回归测试
- [x] 步骤 3 `BaseUI` / `BaseCmp` / `BaseMask` 接入 `Binder`，`_ExitTree` 收敛 sealed → `OnExitTree`
- [x] 步骤 4a **修 D1**：5 个对话框删除 `_eventsBound` 守卫与字段，24 处订阅迁入 `Binder`（`CodexDlg` 9 / `SettingDlg` 11 / `CardDetailsDlg` 2 / `CharacterDetailsDlg` 1 / `StorySelectDlg` 1）；复核「5 个对话框内零裸订阅」通过
- [x] 步骤 4b 6 个非 `BaseUI` 组件接 6 行模式：`BaseDlgComp`（参考实现）、`BaseKemoButton`、`BasePager`、`BaseCardItem`、`BaseCharacterItem`、`VirtualList`；删除全部 `_bound` 手写守卫与 `EnsureBound`/`Unbind`/`UnbindControls` 簿记成员；并修 `VirtualList` 重入树后滚动失效（`OnExitTree` 复位 `_initialized`）
- [x] 步骤 4d **补齐 D1（审阅发现）**：组件订阅登记从 `_Ready` 移到 `_EnterTree`。`_Ready` 每节点只调用一次，缓存重开（`RemoveChild` → `AddChild`）不再触发，故 4a/4b 只修好了对话框自身；`BaseCmp.InitEvent()` 改由 `_EnterTree` 驱动（覆盖 3 个 `Setting*Row`），`BaseKemoButton` / `BaseCardItem` / `BaseCharacterItem` / `BasePager` 的订阅同样改挂 `_EnterTree`，`_Ready` 只留一次性初始化；`VirtualList` 顺带修掉重入树时重复新建 `ItemContainer` 与 `_scrollBar` 缺失 null 守卫（2026-09-21 合并补记：**`VirtualList` 的订阅登记同样挂在 `_EnterTree`**——它虽在 4b 已接 6 行模式，订阅登记点本次一并从 `_Ready` 移到 `_EnterTree`，见 §14.4.3 的 2026-09-21 补记与 §14.12.2「组件订阅登记点」行）
- [x] 步骤 4c `KeywordTipService` 接账本：按锚点的**动态**订阅用独立 `_anchorBinder`，换锚点只拆这一条；保留原 `IsInstanceValid` 防御语义
- [x] 全 UI 目录复核：**0 处裸订阅**（所有 `+=` 均在 `Binder.Bind(...)` 的 add-lambda 内或为算术运算）
- [x] 步骤 5 归属契约：`UIRegistration.OwnerModId`（必填）+ `UIRuntimeEntry`/`UIVo` 落 owner + `Validate` 三条硬校验（`HasErrors`）+ `GetByOwner`；13 个回归测试
- [x] 步骤 6 组合根：`FeatureModCatalog` 迭代注册，`MainRoot` 不再硬编码 Mod
- [x] 步骤 7 `CloseByOwner` / `UnregisterOwner`；决策 1：`RunMain` 注册为 `CacheTime = 0`，`RunRuntime.CreateNew`/`Abandon` 调 `CloseByOwner(run, destroy: true)`
- [x] 步骤 8（框架侧）`IUiFacadeProvider` + `BaseWin.Facade<T>()` / `BaseMask.Facade<T>()`；`SettingDlg` 3 处改为 `Facade<GlobalModController>()`。**跨功能读仅剩 `StorySelectDlg:53`**（见 §5.4），内容注册表读取按设计保留
- [x] 步骤 9 同步 `Doc/AGENT.md` §3（三条硬性约定）与 §5；`Doc/INDEX.md` 活规格表

---

> 2026-09-21 合并：以下小节沿用原 ui-mod-binding 规格的**文件顺序**（`## 12. 自检记录` → `## 12.2 实现备注` → `### 已知未处理的跨功能读` → `### 12.1 实施中修正的规格问题`），因此 §14.12.1 在正文中出现在 §14.12.2 **之后**；段号映射见文首「段号索引」。

### 14.12.2 实现备注（与当前代码对齐）（原 §12.2）

| 落地项 | 位置 | 说明 |
|---|---|---|
| 订阅账本 | `Src/frame/ui/BindingScope.cs` | 纯 C#，可单测；`UnbindAll` 逆序 + 单条异常不阻断 + 可重复调用 |
| Godot 信号糖 | `Src/frame/ui/BindingScopeSignals.cs` | 扩展方法（`BaseUI` 与非 `BaseUI` 节点共用）；带 `IsInstanceValid` 解绑保护；参数化信号用 Godot 专用委托类型 |
| 框架生命周期 | `BaseUI.cs` / `BaseMask.cs` | `_ExitTree` **sealed** → `OnExitTree()` → `Binder.UnbindAll()` → `GlobalEvents.Bus.OffCaller(this)` |
| 6 行模式 | `BaseDlgComp.cs` 等 6 个组件 | 无法继承 `BaseUI` 的节点自行组合 `BindingScope`；**订阅登记挂 `_EnterTree`** |
| 组件订阅登记点 | `BaseCmp.cs`（`_EnterTree` → `InitEvent`）+ 5 个组件（**2026-09-21 补记：`VirtualList` 同在此列，订阅登记同为 `_EnterTree`**，见 §14.4.3 补记） | `_EnterTree` 每次进树都触发，`_Ready` 只触发一次（缓存重开不再触发） |
| 归属声明 | `UIRegistration.OwnerModId` → `UIRuntimeEntry`/`UIVo` | 工厂方法首参为 `ownerModId` |
| 归属校验 | `UIRuntimeRegistry.Validate(knownOwnerModIds)` | 三条硬校验；`Register` 另检同 id 跨 owner 抢占；结果见 `HasErrors` |
| 组合根登记 | `Src/mod/FeatureModCatalog.cs` | 唯一列出功能界面的地方；`ModFactory` 暴露 `Features` |
| 门面解析 | `Src/frame/ui/IUiFacadeProvider.cs` | `ModStartupResult` 实现；`run` → `RunRuntime.Current`（会话级动态解析） |
| 界面取门面 | `BaseWin.Facade<T>()` / `BaseMask.Facade<T>()` | 类型不符明确抛错 |
| Mod 级生命周期 | `UIManager.CloseByOwner` / `UnregisterOwner` | 决策 1 由 `RunMod` 的 `CacheTime = 0` + `RunRuntime` 调用点共同实现 |
| 选项合并 | `UIOpenOpt.MergeFrom` + `Effective*` | 只覆盖显式指定字段（原 `UiManager.MergeInto` 已删除） |

##### 14.12.2.1 已知未处理的跨功能读（原 §12.2 末节）

`StorySelectDlg.OnOpen` 直接取 `AppRoot.Services.GlobalController` 构造 `GlobalPersistentCondContext`（run 的界面读 global 的解锁账本）。干净解法是 frame 级 `IPersistentCondContext` 提供者；**未实现**，代码处已标注，见 §5.4。

#### 14.12.1 实施中修正的规格问题（记录，避免重蹈）（原 §12.1）

| 原设计 | 问题 | 修正 |
|---|---|---|
| `BindingScope` 在 `UnbindAll` 后禁止再 `Bind`（抛异常） | 缓存重开时节点只是 `RemoveChild`，重开会再次 `InitEvent` → **必然崩溃** | 取消"已解绑"状态，`UnbindAll` 只清账本，对象可复用 |
| 功能 Mod 实例实现 `IUiOwner` | `RunMod` 是会话级对象，而注册只在启动期发生一次 → 双实例语义 | 拆成静态**声明**（`FeatureModCatalog`）+ 动态**门面**（`IUiFacadeProvider`） |
| 组件订阅登记留在 `_Ready`（`BaseCmp.OnReady` → `InitEvent`，其余组件直接写 `_Ready`） | Godot 的 `_Ready` **每个节点只调用一次**；缓存重开是 `RemoveChild` + `AddChild`，**不会**再触发 `_Ready`。于是组件订阅在首次离场被 `Binder` 解绑后**永不重新登记** —— D1 只修好了对话框自身，`SettingSliderRow` / `SettingToggleRow` / `SettingDropdownRow`（以及 `BaseKemoButton` / `BaseCardItem` / `BaseCharacterItem` / `BasePager` 的悬停与点击）依旧失效 | 订阅登记一律改挂 `_EnterTree`（每次进树都触发）；`BaseCmp.InitEvent()` 由 `_EnterTree` 驱动；`_Ready` 只保留一次性初始化。写进 AGENT.md §3 硬性约定 |
| `VirtualList.OnExitTree` 复位 `_initialized` 但不清 `_itemContainer` | 重入树后 `EnsureInitialized()` 会**再建一个** `ItemContainer` 挂到 `ScrollArea`，滚动范围翻倍 | 容器仍有效时复用，仅在失效时新建；并补回 `_scrollBar` 的 null 守卫 |

---

### 14.13 后续步骤（原 §13）

1. 按 §10 顺序实施；每完成一步回写本文「§12 自检记录」与实现备注。
2. 实施完成后：`Doc/AGENT.md` §3 增补两条硬性约定（**界面订阅必须经 `BindingScope`**、**UI 节点不得 override `_ExitTree`，改 override `OnExitTree`**）；§5 增补 `BindingScope` / `IUiOwner` / `FeatureModCatalog` 入口行。
3. `Doc/INDEX.md` 活规格表增补本文。

---

## 15. 事件分发器（原 event-dispatcher 规格 §1–§13）

> **来源**：`Doc/superpowers/specs/2026-07-07-event-dispatcher-design.md`（原标题《事件分发器设计》）。2026-09-21 整篇并入本文，原文归档于 `Doc/archive/superpowers/specs/2026-07-07-event-dispatcher-design.md`。
> **原日期 / 原状态**：2026-07-07；已实现。
> **原范围**：Godot 4.x Mono（C#）下的类型安全事件分发系统，含 `EventDispatcher`、`EventKey<TPayload>`、Source Generator 支持的声明式事件表及错误日志抽象。
> **段号约定**：本章内出现的 `§N` / `§N.M` 一律是**原 event-dispatcher 规格段号**（保留原样）；本章新段号一律写作 `§15.N`。本章与本文 §7（与现有 Frame.Mvc 的关系）互为补充：`EventDispatcher` 由 UI 管理器派发 `UIEvent.Open` / `UIEvent.Close`（见 §15.9.4），层 1 的订阅账本 `BindingScope` 负责把总线订阅也收进统一解绑（见 §14.4.4 解绑职责表）。

---


### 15.1 目标与非目标（原 §1）

#### 15.1.1 目标（原 §1.1）

- **类型安全**：每个事件 Id 绑定唯一的 `TPayload` 类型，编译期 + 运行期双重校验。
- **调用方可追踪**：每个监听器关联 `caller` 对象，支持按 `caller` 批量取消订阅（典型场景：控制器 `Dispose` 时自动清理其所订阅）。
- **低分配**：单监听器 `Send` 零分配且不经装箱；多监听器仅快照数组一次分配。
- **声明式注册**：通过 Source Generator 从枚举 + Attribute 生成 `EventKey` 静态字段及 `OnXxx`/`NotifyXxx` 包装方法。
- **分层隔离**：每 Mod 独立 `InternalBus`（`BaseMod` 内），全局跨功能通信走 `GlobalEvents.Bus`。

#### 15.1.2 非目标（原 §1.2）

- 不提供跨进程 / 跨网络事件总线。
- 不提供事件持久化、重放或 Qos 语义。
- 不做复杂的事件流管道（`filter`/`map`/`throttle` 等），由调用方自行组合。

---

### 15.2 核心架构（原 §2）

#### 15.2.1 组件图（原 §2.1）

```
GlobalEvents.Bus (static, 跨 Mod)
        │
BaseMod.InternalBus ── BaseController.InternalBus (委托到 Model.InternalBus)
        │
        ▼
EventDispatcher ── IEventDispatcherLogger (可插拔日志)
   ├── EventKey<TPayload>   (事件标识)
   ├── EventListener<TPayload>  (监听器实现)
   ├── IEventListener<TPayload> (对外只读视图)
   └── IEventListener             (内部非泛型统一存储)
```

#### 15.2.2 关键类型（原 §2.2）

| 类型 | 职责 | 可见性 |
|---|---|---|
| `EventDispatcher` | 核心分发器，管理监听器注册/派发/取消 | public |
| `EventKey<TPayload>` | 类型安全的事件标识符，包装 `int id` | public readonly struct |
| `EventListener<TPayload>` | 监听器内部实现 | internal |
| `IEventListener<TPayload>` | 监听器对外只读视图（`Key`/`Caller`/`Once`/`Off()`） | public |
| `IEventListener` | 内部非泛型视图，用于统一存储与遍历 | internal |
| `EventConst` | 常量，含 `NoneCaller` 哨兵 | public |
| `EventDispatchFailureMode` | 派发失败策略枚举 | public |

---

### 15.3 EventKey——事件标识（原 §3）

```csharp
public readonly struct EventKey<TPayload>(int id)
{
    public int Id { get; init; } = id;
}
```

- 用 `int` 作事件 Id，由业务枚举的整型值承载。
- 泛型参数 `TPayload` 绑定载荷类型。
- 同一 `EventDispatcher` 实例内，同一个 `int id` 必须对应唯一的 `TPayload`——注册时运行期校验，违规则抛 `InvalidOperationException`。

---

### 15.4 EventDispatcher——核心分发器（原 §4）

#### 15.4.1 内部数据结构（原 §4.1）

```csharp
public sealed class EventDispatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<int, List<IEventListener>> _handlers = [];       // Id → 监听器列表
    private readonly Dictionary<object, Dictionary<int, List<IEventListener>>> _callerMap = []; // caller → Id → 监听器列表
}
```

- `_handlers`：快速按 Id 定位所有监听器。
- `_callerMap`：快速按 `caller` 定位其所有订阅，用于 `OffCaller` 批量取消。
- `_gate`：全局锁，保证注册/取消/派发互斥。

#### 15.4.2 API（原 §4.2）

##### 15.4.2.1 注册（原 §4.2 子目）

```csharp
// 注册监听
IEventListener<TPayload> On<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>> handler, object? caller, bool once = false);

// 注册一次性监听（等价于 On(key, handler, caller, true)）
void Once<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>> handler, object? caller);
```

- 同一 `key + handler + caller` 已存在活跃监听器时返回已有实例（不升级 `Once` 标志）。
- `caller` 为 `null` 时归一化为 `EventConst.NoneCaller`。

##### 15.4.2.2 派发（原 §4.2 子目）

```csharp
void Send<TPayload>(EventKey<TPayload> key, TPayload data);
```

- 锁内快照监听器列表，锁外派发以避免重入死锁。
- 单监听器：直接强类型 `Invoke`——零分配、不经装箱。
- 多监听器：复制一份 `IEventListener[]`——一次数组分配，按序强类型 `Invoke`。

##### 15.4.2.3 取消（原 §4.2 子目）

```csharp
void Off<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>>? handler = null, object? caller = null);  // 精确取消
void OffId(int id);                                                                       // 按 Id 全部取消
void OffCaller(object? caller);                                                           // 按 caller 全部取消
void OffAll();                                                                             // 清空所有
```

##### 15.4.2.4 查询（原 §4.2 子目）

```csharp
bool Has<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>>? handler = null, object? caller = null);
```

#### 15.4.3 线程模型（原 §4.3）

- 设计目标为 **Godot 主线程派发**。
- 所有公开 API 通过 `_gate` 锁保证互斥安全。
- `Send` 在锁内快照、锁外派发以避免重入死锁。
- `EventListener<TPayload>.IsActive` 非 volatile，不承诺跨线程可见性。

#### 15.4.4 派发失败策略（原 §4.4）

```csharp
public enum EventDispatchFailureMode { LogAndContinue, Throw }
```

- 默认 `LogAndContinue`：监听器抛异常时记录日志并继续派发给后续监听器。
- `Throw`：监听器抛异常时立即向上抛出，中断后续派发。

---

### 15.5 日志抽象（原 §5）

```csharp
public interface IEventDispatcherLogger
{
    void LogError(string message);
}
```

- 使 `EventDispatcher` 不直接依赖 Godot，便于单元测试。
- 默认 `NullEventDispatcherLogger`（空实现）。
- Godot 运行时通过 `GodotEventDispatcherLogger` 桥接到 `GD.PushError`。

```csharp
// 启动时配置
EventDispatcher.Configure(new GodotEventDispatcherLogger());
```

---

### 15.6 全局事件总线（原 §6）

```csharp
public static class GlobalEvents
{
    public static EventDispatcher Bus { get; } = new();
}
```

- 跨 Mod 通信的唯一通道。
- 功能内部通信应使用 `BaseMod.InternalBus`，避免全局总线耦合。

---

### 15.7 Mod 内部事件总线（原 §7）

#### 15.7.1 BaseMod（原 §7.1）

```csharp
public abstract class BaseMod
{
    public EventDispatcher InternalBus { get; } = new();

    public virtual void Dispose()
    {
        InternalBus.OffAll();
    }
}
```

#### 15.7.2 BaseController（原 §7.2）

```csharp
public abstract class BaseController<TModel>(TModel model) : IDisposable where TModel : BaseMod
{
    protected EventDispatcher InternalBus => Model.InternalBus;

    public void Dispose()
    {
        InternalBus.OffCaller(this);  // 自动清理该 Controller 的所有订阅
        GC.SuppressFinalize(this);
    }
}
```

- `BaseController.Dispose` 调用 `OffCaller(this)`，利用 `caller` 追踪自动清理——不需逐条 `Off`。

---

### 15.8 声明式事件表（Source Generator）（原 §8）

#### 15.8.1 使用方式（原 §8.1）

**步骤 1**：定义事件枚举并标注载荷类型。

```csharp
public enum ERunEvent
{
    [EventPayload(typeof(RunPhaseChangedPayload))]
    RunPhaseChanged,

    [EventPayload(typeof(RunGoldChangedPayload))]
    RunGoldChanged,
}
```

**步骤 2**：声明事件表。

```csharp
[EventTable(typeof(ERunEvent), typeof(RunMod))]
public static partial class RunModEventTable
{
}
```

**步骤 3**：Source Generator 自动生成：

- `RunModEventTable.EventKeys.g.cs`——每个枚举成员生成 `EventKey<TPayload>` 静态字段：

```csharp
static partial class RunModEventTable
{
    public static readonly EventKey<RunPhaseChangedPayload> RunPhaseChanged = new((int)ERunEvent.RunPhaseChanged);
    public static readonly EventKey<RunGoldChangedPayload> RunGoldChanged = new((int)ERunEvent.RunGoldChanged);
}
```

- `RunModEventTable.RunMod.ModEvents.g.cs`——在 `RunMod` 上生成 `OnXxx`/`NotifyXxx` 包装方法：

```csharp
partial class RunMod
{
    public IEventListener<RunPhaseChangedPayload> OnRunPhaseChanged(
        Action<RunPhaseChangedPayload, IEventListener<RunPhaseChangedPayload>> handler,
        object? caller = null)
        => InternalBus.On(RunModEventTable.RunPhaseChanged, handler, caller);

    public void NotifyRunPhaseChanged(RunPhaseChangedPayload payload)
        => InternalBus.Send(RunModEventTable.RunPhaseChanged, payload);
}
```

#### 15.8.2 编译期校验（原 §8.2）

Source Generator 输出诊断：

| 编号 | 级别 | 含义 |
|---|---|---|
| `KMV001` | Error | 事件表未声明为 `partial` |
| `KMV002` | Error | `EventTable` 的 `EnumType` 参数不是枚举 |
| `KMV003` | Error | `EventTable` 的 `ModType` 参数不派生自 `BaseMod` |
| `KMV004` | Warning | 枚举无任何 `[EventPayload]` 标注的成员 |

---

### 15.9 典型使用模式（原 §9）

#### 15.9.1 声明载荷（原 §9.1）

```csharp
public readonly struct RunGoldChangedPayload
{
    public int PreviousAmount { get; init; }
    public int CurrentAmount { get; init; }
}
```

#### 15.9.2 订阅（原 §9.2）

```csharp
// 在 Controller 中订阅
InternalBus.On(CardEventTable.CardPlayed, (payload, listener) =>
{
    GD.Print($"卡牌 {payload.CardId} 已打出");
}, caller: this);
```

#### 15.9.3 派发（原 §9.3）

```csharp
// 在 Controller 或 Model 中派发
InternalBus.Send(CardEventTable.CardPlayed, new CardPlayedPayload { CardId = "fireball" });
```

#### 15.9.4 UI 事件（原 §9.4）

```csharp
// 在 UiManager 中
public static class UIEvent
{
    public static readonly EventKey<UIOpenPayload> Open = new((int)EUIEvent.Open);
    public static readonly EventKey<UIClosePayload> Close = new((int)EUIEvent.Close);
}

// 派发
manager.EventDispatcher.Send(UIEvent.Open, new UIOpenPayload(vo));

// 订阅
uiManager.EventDispatcher.On(UIEvent.Open, (payload, _) => { /* ... */ }, caller: this);
```

#### 15.9.5 一次性监听（原 §9.5）

```csharp
InternalBus.Once(SomeEvent, (payload, listener) =>
{
    // 仅执行一次，自动取消
}, caller: this);
```

---

### 15.10 设计权衡（原 §10）

| 决策 | 理由 |
|---|---|
| `int` 作 Id，而非 `string` | 枚举整型值零分配比较，无需字符串哈希 |
| `caller` 为 `object?`，非泛型约束 | 保持 `EventDispatcher` 对任意 `caller` 类型开放 |
| `Send` 锁内快照、锁外派发 | 避免监听器回调中发起新的 `Off`/`Send` 导致死锁 |
| 单监听器零分配路径 | 大量场景仅一个订阅者，避免不必要的数组分配 |
| `_callerMap` 维护双重索引 | `OffCaller` O(n) 清理整组订阅，牺牲少量内存换取消便利性 |
| Source Generator 而非反射 | AOT 友好、编译期错误报告、零启动开销 |

---

### 15.11 测试（原 §11）

- 注册/取消未发生异常。
- `Send` 对单/多/零监听器的行为。
- `Once` 仅执行一次后自动取消。
- `OffCaller` 精确清理指定 caller 的所有订阅。
- 同一 `id` 注册不同 `TPayload` 应抛出 `InvalidOperationException`。
- `OffCaller` 后 `off` 的 listener 不会被派发。
- `Dispose` → `OffCaller` 端到端集成验证。

---

### 15.12 文件布局（原 §12）

```
Src/frame/mvc/
├── EventDispatcher.cs           # EventKey + EventListener + EventDispatcher + EventConst
├── EventDispatchFailureMode.cs  # 派发失败策略枚举
├── EventTableAttribute.cs       # EventTable / EventPayload Attribute 定义
├── IEventDispatcherLogger.cs    # 日志接口 + NullEventDispatcherLogger
├── GlobalEvents.cs              # 全局静态总线
├── BaseMod.cs                   # InternalBus 定义
├── BaseController.cs            # OffCaller(this) 自动清理
└── Generators/KemoCard.Mvc.Generators/
    └── EventTableGenerator.cs   # Source Generator

Src/fixed/godot/
└── GodotEventDispatcherLogger.cs  # GD.PushError 桥接

Src/mod/run/events/
└── RunEventBus.cs               # 示例：ERunEvent + EventTable
```

---

### 15.13 后续步骤（原 §13）

1. 本文档反映当前事件系统（`Src/frame/mvc/EventDispatcher.cs` 等）的完整实现。
2. 随代码变更同步更新本文档。

---

## 16. 条件判断系统（原 condition-system 规格 §1–§11）

> **来源**：`Doc/superpowers/specs/2026-07-30-condition-system-design.md`（原标题《条件判断系统（Condition）设计》）。2026-09-21 整篇并入本文，原文归档于 `Doc/archive/superpowers/specs/2026-07-30-condition-system-design.md`。
> **原日期**：2026-07-30；**原文最后修订**：2026-07-31。
> **原状态**：引擎与 Persistent 四件套已实现；首个内容接入 `StoryDto.unlock`（2026-07-31）；Combat CondType / 其余内容 DTO 字段未接。
> **原范围**：可扩展条件求值引擎、Persistent / Combat 双域 CondType 注册、JSON 组合语法、Explain 结构化结果与提示模板约定。
> **原非范围**：扣除/支付（Cost）、具名条件包、脚本动态注册 CondType、引擎内脏标记/订阅、Combat 具体 CondType（v1 仅空表）。
> **段号约定**：本章内出现的 `§N` / `§N.M` 一律是**原 condition-system 规格段号**（保留原样：`Doc/AGENT.md` §5、`Doc/superpowers/specs/2026-06-22-run-mod-design.md`「见条件规格 §8」、内容 Mod 管道规格「见条件规格 §8」等按旧段号引用仍可定位）；本章新段号一律写作 `§16.N`。

---


### 16.1 目标与非目标（原 §1）

#### 16.1.1 目标（原 §1.1）

- 用统一引擎解析并求值内容侧内联的条件表达式，供解锁门槛、UI 灰态/详情等**只读检查**使用。
- 以 `CondType`（字符串）为类型 id，可注册检查逻辑、参数解析、短/长提示模板键。
- Persistent 与 Combat **分域注册**，共享求值与提示管道，避免战斗运行时与持久状态耦合成一张大表。
- 加载/合并期校验未知类型与参数形状，错误带**配置来源路径**。
- 面向用户的提示走翻译键；显示名由 UI 查表，引擎不解析本地化显示名。

#### 16.1.2 非目标（原 §1.2）

- 不负责资源扣除、事务回滚或「检查并通过后支付」。
- 不做具名条件包表（`cond_pack_id`）；v1 仅内联表达式。
- 不做引擎级依赖追踪 / 自动刷新；调用方在适当时机再次 `Evaluate`。
- 不把 NOT 塞进 `{}`/`[]` 组合糖；否定用独立 CondType。
- v1 不实现 Combat 业务 CondType；不实现脚本侧运行时注册。

> 2026-09-21 合并：原 condition-system 规格 的旧表述「v1 不实现 Combat 业务 CondType」已被 [2026-07-21 战斗规格](2026-07-21-combat-system-design.md) §14.7 取代——Combat 域自 2026-09-21 起注册 `CardPlayedThisTurn`（见 §16.7 注）；「不实现脚本侧运行时注册」这一条仍然有效。

---

### 16.2 架构（原 §2）

```
内容 JSON（内联表达式）
        │  Parse + Validate（指定域注册表）
        ▼
ConditionExpression（And / Or / Leaf）
        │  Evaluate(context)
        ▼
ConditionEvalResult
   ├── Passed
   └── Leaves[]（每叶：type, passed, fill, progress?, refs?）
        │
        ▼
UI：按模板键 + fill/progress/refs 自行拼展示（引擎无默认聚合）
```

| 层 | 位置 | 职责 |
|---|---|---|
| 引擎 | `Src/frame/condition/` | JSON 解析、表达式树、求值、结果结构、域注册表容器 |
| Persistent CondType + Context 适配 | `Src/mod/`（如 `global` / `run` 下条件目录） | 具体类型、背包/旗标等只读查询 |
| Combat CondType + Context 适配 | `Src/mod/combat/`（后续） | v1 仅保留空注册表入口 |
| 启动注册 | `ModFactory` / 对应 Mod Bootstrap | 向域表 `Register` 内置类型（同 KeywordCatalog 模式） |

`frame` 不引用 `KemoCard.Mod.*`。Context 为**接口**；游戏侧提供适配器实例，求值时由调用方传入。

---

### 16.3 JSON 组合语法（原 §3）

#### 16.3.1 规则（原 §3.1）

| 节点 | 语义 |
|---|---|
| 对象 `{}` | AND：所有成员通过则通过 |
| 数组 `[]` | OR：任一元素通过则通过 |
| 对象的 **key** | `CondType` 字符串 |
| 对象的 **value** | 该 CondType 的**参数列表**（永远不当子表达式） |
| 数组元素 | 可为对象（AND 子树）或数组（OR 子树） |

递归边界：**仅数组元素可嵌套表达式**；对象 value 一律走该类型的 `TryParse`。

同 CondType 不得在同一 AND 对象中出现两次（JSON key 唯一 + 加载期显式校验更稳妥）。多目标由独立类型消化（如 `HasAllItems` / `HasAnyItem`），不为通用布尔树引入 `$or` 等保留键。

复杂门槛优先**单开 CondType**，而不是堆叠超级表达式。

#### 16.3.2 示例（原 §3.2）

```json
{
  "HasFlag": ["intro_done"],
  "HasAllItems": [["wood", 5], ["stone", 3]]
}
```

```json
[
  { "HasFlag": ["intro_done"] },
  { "HasAnyItem": [["ticket", 1], ["voucher", 1]] }
]
```

#### 16.3.3 空值与缺失（原 §3.3）

| 情况 | 语义 |
|---|---|
| 内容字段**缺失**（未写条件） | 无门槛，视为通过（由读取该字段的业务约定；引擎若收到 `null` 可不建表达式） |
| 表达式节点空对象 `{}` | 空 AND，**真** |
| 表达式节点空数组 `[]` | 空 OR，**假** |
| 叶子参数为空或不合法（如 `"HasFlag": []`） | **加载期** `TryParse` 失败，非运行时空组合语义 |

#### 16.3.4 保留 / 非法 key（原 §3.4）

v1 对象成员 key 必须是已注册 CondType。以 `$` 开头的保留风格 key（如 `$or`）视为**未知 CondType**，加载失败，避免日后语义被野配置占坑。

---

### 16.4 CondType 注册（原 §4）

每个 CondType 注册项至少包含：

| 字段 | 说明 |
|---|---|
| `Id` | 字符串，域内唯一 |
| `TryParse` | 原始 args → 强类型参数；失败返回错误信息 |
| `Check` | `(TArgs, TContext) → LeafEvalData`（passed + fill + 可选 progress/refs） |
| `ShortTipKey` | 短提示翻译键（如「xxx 不足」） |
| `LongTipKey` | 长提示翻译键（如「拥有 {item}（{y}/{z}）」类句式） |

- Persistent 与 Combat **两张注册表**，校验时按字段所属域选用。
- 跨域引用（Persistent 配置写了 Combat 类型）→ 加载期失败。
- 未知 CondType → 加载期失败。
- 错误信息必须带**配置来源路径**（定义文件 / 字段路径）。

---

### 16.5 求值与结果（原 §5）

#### 16.5.1 Context（原 §5.1）

- `IPersistentCondContext` / `ICombatCondContext`（名称以实现为准）：只读查询接口。
- Checker 不访问全局单例；不通过服务定位器偷依赖。

#### 16.5.2 结果结构（原 §5.2）

```
ConditionEvalResult
  Passed: bool
  Leaves: LeafResult[]   // 求值走过的全部叶子（含通过与未通过）

LeafResult
  CondType: string
  Passed: bool
  ShortTipKey / LongTipKey  // 来自注册项
  Fill: 有序参数列表        // 供模板按位/约定键填充
  Progress?: { Current, Required }
  Refs?: { ItemIds?, FlagIds?, ... }  // UI 查显示名，不进引擎本地化
```

- 组合层（AND/OR）**不生成**面向用户文案，只汇总 `Passed` 与叶子列表。
- **无默认聚合策略**（不规定「只取第一条失败」）；展示完全由 UI 决定。
- Explain 路径应评估**全部叶子**，避免 UI 拿不到未求值叶。
- `Progress` 可选；无进度的条件可只靠 `Fill`。

#### 16.5.3 提示约定（原 §5.3）

- 注册时挂模板键；Checker 只产出填充数据与 refs。
- UI 使用 `Localization.Tr` + 查表得到的显示名；引擎不调用 `Tr` 拼最终可见句（测试可不绑语言）。

#### 16.5.4 刷新（原 §5.4）

纯拉取：`Evaluate(expr, context)`。物品/旗标变更后由 UI 或业务在已知事件点再次求值。引擎不提供订阅或脏标记。

---

### 16.6 与 Cost 的边界（原 §6）

条件系统**只读**。支付、扣物、花货币走独立 Cost/Reward 管线：先 `Evaluate` / 展示 → 用户确认 → 外部扣除。CondType 不提供 `Apply` / `consumes`。

---

### 16.7 v1 内置 CondType（Persistent）（原 §7）

| CondType | 参数要点 | 说明 |
|---|---|---|
| `HasFlag` | `[flagId]` | 已拥有旗标 |
| `NotHasFlag` | `[flagId]` | 未拥有旗标（NOT 独立类型，钉死组合糖不做 NOT） |
| `HasAllItems` | `[[itemId, count], ...]` | 列出的道具均达到数量（AND） |
| `HasAnyItem` | `[[itemId, count], ...]` | 列出的道具任一达到数量（OR） |

Combat 域：v1 建立空注册表与 Context 接口占位，**不注册**业务 CondType。

> 2026-09-21 合并：原 condition-system 规格 的旧表述（「Combat 域 v1 …… 不注册业务 CondType」）已被 [2026-07-21 战斗规格](2026-07-21-combat-system-design.md) §14.7 取代——Combat 域现有 `CardPlayedThisTurn`（`Src/mod/combat/Condition/BuiltinCombatConditions.cs`，参数 `{ count, elementAny? }`，短/长提示键 `COND_CARD_PLAYED_THIS_TURN_SHORT` / `..._LONG`），效果（`EffectDto.conditions`）在战斗内求值，未知类型/参数非法一律视为**不通过**。「空注册表入口 + Context 接口占位」这一机制仍然有效。

---

### 16.8 内容接入（原 §8）

- 条件写在各内容定义的内联字段中（字段名由具体 DTO/规格定义，如 `unlock`）；**无**独立「条件包」内容类别。
- 校验时机：在 CondType 已注册之后、内容合并/校验流水线中解析表达式（Bootstrap 顺序：先 Register 条件类型，再校验引用它们的定义）。
- 缺失条件字段 = 该内容无门槛；与空 `{}`/`[]` 字面量区分见 §3.3。

**首个接入（2026-07-31）**：`StoryDto.unlock`（Persistent 域，见[内容规格](./2026-05-17-content-mod-manager-design.md) §3.2）。

- 校验：`ContentDefinitionValidator.ValidateStories` 解析；失败带 `content/stories/<id>.json:unlock` 来源路径，定义移除。
- 运行期：选故事 UI 用 `ConditionEvaluator` 求值，Context 为 `GlobalPersistentCondContext`（`Src/mod/global/Condition/`）——`HasFlag` 映射到全局存档 `Unlocks`（与 `IsContentUnlocked` 同表）；`GetItemCount` 暂恒 0（商店/道具规格未落地）。
- 加载与校验的 Bootstrap 顺序约束由 `ModFactory.Bootstrap` 保证：先 `RegisterBuiltinConditions()`，再 `ContentModPipeline.Rebuild()`。

---

### 16.9 测试要点（原 §9）

- 解析：AND/OR/嵌套数组、对象 value 不当表达式、重复 CondType key、`$or` 失败。
- 空节点：`{}` 真、`[]` 假；叶子空 args Parse 失败。
- 未知类型 / 错域 / 错误路径信息。
- `HasAllItems` vs `HasAnyItem` 语义与 LeafResult（passed、progress、refs）。
- `NotHasFlag` 与组合 AND/OR。
- Evaluate 返回全部叶子；UI 聚合不在引擎断言。

---

### 16.10 有意延后（原 §10）

- `$and` / `$or` 保留键嵌套糖  
- 具名条件包与 `$ref`  
- 内容 Mod 脚本注册 CondType  
- Cost 管线与 Cond 的声明式绑定  
- 引擎脏标记 / 依赖声明  
- Combat 业务 CondType、货币/进度类 Persistent 类型  

---

### 16.11 文档位置（原 §11）

- 权威正文：本文  
- Agent 地图：`Doc/AGENT.md` 仅保留模块入口与权威链指针，不复制本节细节  
- 索引：`Doc/INDEX.md` 活规格表  

---

## 17. Mod 脚本运行时（PuerTS ScriptEnv）（原 jsenv-mod-scripting 规格 §1–§10）

> **来源**：`Doc/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md`（原标题《Mod 脚本运行时（PuerTS ScriptEnv）设计规格》）。2026-09-21 整篇并入本文，原文归档于 `Doc/archive/superpowers/specs/2026-06-17-jsenv-mod-scripting-design.md`。
> **原日期 / 原状态**：2026-06-17；已定稿（brainstorming 确认）。
> **原范围**：单一 VM、Mod 预编译 JS 加载、窄面板沙箱、Rebuild 重建、全部 `scriptPath` 类型。
> **段号约定**：本章内出现的 `§N` / `§N.M` 一律是**原 jsenv-mod-scripting 规格段号**（保留原样：`Src/frame/scripting/HostRng.cs` 等注释按旧段号引用仍可定位）；本章新段号一律写作 `§17.N`。

---


### 17.1 目标与非目标（原 §1）

#### 17.1.1 目标（原 §1.1）

- 全项目维护**唯一** `Puerts.ScriptEnv`（Puerts 3.0；`JsEnv` 已 Obsolete），由 `ModScriptRuntime` 持有。
- Mod 脚本以**预编译 JS** 放在 `user://mods/<folder>/scripts/`；运行期 Lazy 加载 + 模块/入口缓存。
- **窄面板沙箱**：入口函数仅接收 `ScriptContextFacade`（只读 `Contains`、宿主 RNG、`Log`）；纯同步 `return` 结构化结果。
- `ContentModPipeline.Rebuild()` 时**全量销毁重建** VM，并可选预热（仅 import，不执行入口）。
- 覆盖效果、故事、Script 事件、战斗/波次、敌人 AI 等全部 `scriptPath` 引用。

#### 17.1.2 非目标（原 §1.2）

- 不做静态全局 `ScriptEnv.Instance`。
- 运行期不编译 TS（无内嵌 TS 编译器）。
- 业务脚本不支持 async/Promise（无需 `_Process` Tick）。
- 不实现 HybridCLR 可信 Mod 通道（规格保留，另里程碑）。
- 不实现 LLM Agent 专用 VM（与 Mod 脚本分离）。

---

### 17.2 架构（原 §2）

```
ModFactory.Bootstrap
  → ModScriptRuntime (IScriptRuntimeResetter)
  → ModScriptCatalog (modId → folderPath)
  → ModScriptLoader (ILoader)
  → ContentModPipeline.Rebuild → catalog.Rebuild + runtime.Recreate + optional Prewarm

业务调用方
  → PuertsContentEffectScriptHost / *ScriptInvoker
  → ModScriptRuntime.Invoke(modId, scriptPath, entry, ScriptCallContext)
  → ScriptContextFacade → JS execute(ctx)
  → ModScriptResultParser → 宿主校验 → 提交/软失败
```

**组合优先**：Runtime、Loader、Catalog、各 Invoker 为独立类型，通过构造函数注入协作。

---

### 17.3 模块标识与路径（原 §3）

| 概念 | 规则 |
|------|------|
| 模块 specifier | `{modId}/{scriptPath}`，如 `base.game/effects/demo.js` |
| 磁盘路径 | `{FolderPath}/scripts/{scriptPath}` |
| modId vs 文件夹 | `mod.json` 的 `modId`（如 `base.game`）≠ 目录名（如 `base-game`）；由 `ModScriptCatalog` 映射 |

---

### 17.4 加载策略（原 §4）

- **默认 Lazy**：首次 `Invoke` 时 `ExecuteModule` import；PuerTS 缓存模块；C# 缓存 `(modId, scriptPath, entry)` 入口委托。
- **可选预热**：Rebuild 后遍历 Store 中全部 `scriptPath`，仅 `TryLoadModule`（不调用 `execute`）；错误写入 `ContentLoadReport.ScriptLoadErrors`。
- **Owner 映射**：`GameDefinitionRegistry.TryGetOwnerModId(category, id)` 返回合并胜方 modId，供预热与 Invoker 解析 mod。

---

### 17.5 沙箱与通信（原 §5）

#### 17.5.1 ScriptContextFacade（唯一参数）（原 §5.1）

| 成员 | 说明 |
|------|------|
| `Contains(category, id)` | 只读查询七大注册表 |
| `NextInt(min, max)` | 宿主 RNG，由 `RunSeed + StreamKey` 派生，保证可复现 |
| `Log(message)` | 白名单日志 |

#### 17.5.2 入口约定（原 §5.2）

- 默认入口名 `execute`；`EffectDto.scriptEntry` 等可覆盖。
- 签名：`export function execute(ctx) { return { ... }; }`（纯同步）。

#### 17.5.3 各类型返回形状（原 §5.3）

| 类型 | JS 返回 | C# 解析 |
|------|---------|---------|
| 效果 | `{ proposedEffects: [{ kind, params? }] }` | `IContentEffectScriptHost` |
| 故事选项 | `{ options: [{ optionId, labelId, next? }] }` | `StoryScriptInvoker` |
| Script 事件 | `{ pages?, options? }` | `EventScriptInvoker` |
| 战斗/波次 | `{ phase?, hooks? }` 或字典 | `BattleScriptInvoker` |
| 敌人 AI | `{ skillId }` 或 `{ weights: { id: number } }` | `EnemyAiScriptInvoker` |

返回值须经宿主校验（非法 id → 软失败，与设计稿一致）。

#### 17.5.4 沙箱局限（原 §5.4）

PuerTS 3.0 无法在 JS 全局彻底移除 `CS.*`。工程级隔离策略：仅注入 `ctx`、不向脚本传递 C# 实例引用、Mod 评审。真·隔离（独立 isolate）列为后续。

---

### 17.6 生命周期（原 §6）

| 时机 | 行为 |
|------|------|
| `ModFactory.Bootstrap` | 创建 `ModScriptRuntime`；首次 `Rebuild` 填充 Catalog 并 `Recreate` |
| `ContentModPipeline.Rebuild` | 注册表合并 → `catalog.Rebuild(OrderedActiveMods)` → `runtime.Recreate()` → 可选预热 |
| Run 中 | 不提供 Mod 开关；VM 与当前启用集一致 |
| 进程退出 | `Dispose` ScriptEnv |

---

### 17.7 错误处理（原 §7）

- JS 异常：捕获，记录上下文，调用方 `TryExecute`/`TryInvoke` 返回 false，主线程不崩溃。
- 预热/加载错误：`ScriptLoadErrors` 写入 report + `IContentModLogger.LogScriptLoadError`。
- 仅主线程调用 `ModScriptRuntime`（PuerTS 非线程安全）。

---

### 17.8 构建链（原 §8）

- 源 TS：`Config/mods/<mod-folder>/scripts-src/`（**mod 脚本源归属 mod 自己，不放 `Src/typescript/`**）
- esbuild 输出：`Config/mods/<mod-folder>/scripts/`（构建工具在 `Src/typescript/esbuild.mjs`，`npm run build`）
- `ContentModBootstrap.EnsureDefaultModsCopied` 复制到 `user://mods/`

---

### 17.9 测试要点（原 §9）

- Loader 路径解析（modId → folder）
- Owner 映射胜方 modId
- Invoke 结构化返回、JS 异常软失败
- 同种子 RNG 可复现
- Rebuild/Recreate 后脚本行为更新
- 预热报告语法错误

---

### 17.10 自检（原 §10）

- 与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界、可复现 RNG、脚本只提议/宿主校验一致。
- 与 `2026-06-16-content-definition-dto-design.md` 中 `ExecuteScript` / `IContentEffectScriptHost` 一致。
- 无 TBD。
