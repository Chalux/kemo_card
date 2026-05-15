# UI 管理器与 BaseUI 体系设计

**日期**：2026-05-15  
**状态**：已定稿待实现评审  
**范围**：Godot 4.x Mono（C#）下，除 `MainRoot` 启动场景外，以 **UI 管理器** 为唯一 UI 打开入口；`BaseUI` / `BaseDlg` / `BasePopup` 生命周期、层级、注册表与强类型载荷。

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

- 在 **编辑器** 中预先摆放层级容器（如 `CanvasLayer` / `Control`）：至少包含 **Dlg 容器**、**Popup 栈容器**（一个垂直栈父节点，子节点顺序表示从底到顶）。
- `MainRoot`（或等价启动脚本）在 `_Ready` 将上述节点 **引用注入** UI 管理器（或 `[Export]` 绑定）。

### 2.2 绘制与输入顺序（推荐方案：单 Popup 容器 + 子顺序）

- **Dlg**：`DlgLayer` 下 **同一时间最多一个** 子实例。
- **Popup**：`PopupStack` 容器下多个子节点；**列表末尾 = 栈顶**（最上绘制与输入优先，与 Godot 默认行为一致）。
- **不推荐**首版采用「每个 Popup 动态递增 `CanvasLayer.layer`」方案，以免与「槽位均在场景中预摆」冲突；若未来有特殊全屏特效再评估。

---

## 3. BaseDlg 行为

- **全局最多一个** `BaseDlg` 实例处于「已挂上 Dlg 层且处于打开流程/已打开」状态。
- **无遮罩**（不挡全屏点击；业务若需要局部遮罩在子类中自行实现，但不作为 Dlg 默认语义）。
- **替换策略**：当已存在 Dlg 且要打开 **另一个** Dlg 时：**先异步准备新实例至可展示状态**，再 **切换**：旧实例保持显示直至新实例就绪，然后移除旧、挂上新，避免中间「无 Dlg」空窗。
- **快速重复打开**：若同一帧或短时间内多次请求打开 Dlg，须在实现计划中明确 **取消/合并** 进行中的异步加载策略（建议：**以最后一次有效请求为准**，取消未完成的旧加载任务）。

---

## 4. BasePopup 行为

### 4.1 栈与遮罩

- **可无限叠加**（仅受性能与业务约束）。
- **仅栈顶** Popup 显示 **全屏或大块遮罩**，并拦截对下层（含其它 Popup 与 Dlg）的点击。
- **默认行为**：点击栈顶遮罩 **关闭该栈顶 Popup**；子类或打开参数可提供 **`bool` 关闭此默认行为**。

### 4.2 同 Id 再次打开：枚举策略

打开函数携带枚举（名称实现时定义，语义如下）：

| 值 | 语义 |
|----|------|
| **0** | 不重建实例、不重播打开动画；但若该实例 **被其它 Popup 遮挡**，则 **提升到栈顶**，并 **刷新各层遮罩归属**（仅栈顶有遮罩与默认可点关）。 |
| **1** | 仍使用 **原实例**，**重播打开动画**（并视需要置顶，语义与栈顶遮罩一致）。 |
| **2** | **关闭当前实例**并 **新建** 一个实例再入栈（新实例成为栈顶，遮罩规则同上）。 |

**说明**：枚举 **0** 与「置顶」不冲突：**0 不包含重播与重建，但包含「若被遮挡则置顶」**。

### 4.3 与 BaseDlg 的相对顺序

- Popup **始终在 Dlg 之上**（通过预摆的层容器保证）；栈顶遮罩可拦截对 Dlg 的点击。

---

## 5. BaseUI 与状态机

### 5.1 职责划分

- **UI 管理器**：注册校验、`Open`/`Close` 调度、挂载父节点、Dlg 替换顺序、Popup 栈 `MoveChild` 置顶、栈顶遮罩与默认点遮罩关闭的启用/禁用协调。
- **BaseUI**：自身 **状态** 与 **可覆盖钩子**（打开动画、关闭动画、数据应用）；**不**自行决定挂在哪个层（由管理器传入或查询已注入的层引用）。

### 5.2 建议状态（实现可微调命名）

覆盖从「未创建」到「已释放」的可见区间，例如：

`Created → Opening → Opened → Closing → Closed`（最终 `QueueFree` 或池回收）

若实例在管理器外预先存在，可增加 `Loading` 或在 `Opening` 前合并异步资源阶段。**状态转移须可测试**（纯 C# 辅助类或可被注入的时钟/工厂）。

### 5.3 载荷（强类型）

- 每个具体 UI 类型在其类文件内定义 **载荷类型**（如嵌套 `record` / `readonly struct` / `class`），表达 **开窗一次** 所需数据。
- **注册 API** 将 **UI 脚本类型** 与 **`TPayload`** 绑定到 **字符串 Id**。
- **打开 API** 使用 **泛型** 或 **强类型委托**，使 **编译器** 保证：给定 Id 路径下，传入的载荷类型与注册的 UI 一致。
- **无参窗体**：使用 **空载荷类型**（如 `EmptyUiPayload` 单例结构或 `readonly record struct Empty`），仍走同一套强类型注册，避免 `null` 语义泛滥。

---

## 6. UI 管理器 API 形态（概念层）

以下为实现导向的「能力列表」，具体方法名与异步签名在实现计划中敲定。

- `RegisterDlg<TDlg, TPayload>(string id, …)` / `RegisterPopup<TPopup, TPayload>(string id, …)`  
  - 绑定：`PackedScene` **或** `Func<TPayload, TDlg>` 工厂（二选一或统一为工厂包装场景实例化）。
- `OpenDlg<TPayload>(string id, TPayload payload)`（或等价强类型入口）
- `OpenPopup<TPayload>(string id, TPayload payload, PopupReopenBehavior behavior)`
- `CloseTopPopup()`、`ClosePopup(…)`、`CloseDlg()` 等按需暴露。
- **未注册 Id**：不创建节点，`GD.PushError` 或项目统一日志；可选 `Debug` 下断言。

**原则**：业务功能模块在初始化阶段完成注册；**禁止**通过字符串反射绕过注册表打开（代码评审约束）。

---

## 7. 与现有 Frame.Mvc 的关系

- **弱耦合**：`FeatureControllerBase` 等仅依赖 **UI 管理器接口**，不引用具体界面类。
- 可选用现有 `GlobalEvents` / `EventDispatcher` 派发「某 Id 已关闭」等事件（可选，非首版阻塞项）。

---

## 8. 错误处理与边界

- **未注册 Id**：拒绝打开，记录错误。
- **异步竞争**：Dlg 连续打开见 §3；Popup 异步若首版同步 `Instantiate`，仍须文档化后续异步资源扩展点。
- **遮罩点击关闭被禁用**：栈顶仍拦截输入，但点击遮罩不关闭（由参数/子类控制）。

---

## 9. 测试建议

- **注册表**：未注册拒绝；重复注册策略（报错或覆盖）在实现计划中二选一并写入。
- **Popup 栈**：子节点顺序与栈顶遮罩切换；枚举 0/1/2 行为；**被遮挡时置顶**。
- **Dlg 替换**：新实例就绪后再卸载旧实例的顺序（黑盒或轻量集成测）。
- **强类型载荷**：编译期已覆盖大部分错误；可补充运行时「场景根脚本类型与注册泛型不一致」的启动校验（可选）。

---

## 10. 自检记录

- **占位**：无 TBD；未决细项（如确切枚举名、异步 API 用 `Task` 还是 Godot 信号）留在 **实现计划** 阶段，本 spec 不阻塞。
- **一致性**：Dlg 替换 B、Popup 遮罩与枚举 0 置顶、强类型载荷与字符串 Id 共存，无矛盾。
- **范围**：单 spec 可支撑一份实现计划；对象池与多 Viewport 明确排除在非目标外。

---

## 11. 后续步骤

1. 评审本文件；变更请求更新本文日期或版本说明。  
2. ~~通过后使用 **writing-plans** 技能生成实现计划~~（已实现，见 `Doc/superpowers/plans/2026-05-15-ui-manager-implementation-plan.md`）。  
3. 实现阶段再写代码；首版可不 `git commit` 文档，除非维护者要求纳入版本库。

---

## 12. 实现备注（与当前代码对齐）

以下描述仓库内 **首版实现** 相对上文规格的细节与命名，便于评审与迭代。

### 12.1 代码与场景位置

- **框架代码**：`Src/frame/ui/`（`UiManager`、`UiRegistry`、`UiIdDuplicateGuard`、`BaseUI` / `BaseDlg` / `BasePopup`、`IUiManager`、`UiStateMachine` 等）。
- **启动场景**：`Src/MainRoot.tscn` 下挂 `DlgCanvas`（`layer=10`）→ `DlgHost`，`PopupCanvas`（`layer=20`）→ `PopupStack`，以及同级的 `UiManager`（`Node` + `UiManager.cs`）。

### 12.2 MainRoot 与 UiManager 接线

- `MainRoot._Ready` 通过固定路径 **`UiManager`**、**`DlgCanvas/DlgHost`**、**`PopupCanvas/PopupStack`** 取节点并调用 `UiManager.Configure(dlgHost, popupStack)`。  
- 与规格初稿中「三个 `[Export]` 由检查器拖拽」不同：当前以 **约定子节点路径** 为主，避免漏绑 Export；若后续要支持多根场景，可再增加 Export 覆盖路径。

### 12.3 异步与 Dlg 切换

- **API**：`OpenDlgAsync<TPayload>(string id, TPayload payload, CancellationToken cancellationToken = default)`，基于 **`Task`**。  
- **取消**：每次打开新 Dlg 会 **Cancel 并 Dispose** 上一份专用于本次加载链路的 `CancellationTokenSource`，再以 `CreateLinkedTokenSource(cancellationToken)` 生成新令牌；在关键步骤后检查 `IsCancellationRequested`，必要时 **`QueueFree` 未挂上 DlgHost 的新实例**。  
- **顺序**：新实例在 **`UiManager` 下且 `Visible = false`** 完成 `ApplyPayload`、生命周期 `Opening` → `await PlayOpenAsync()` → `Opened`，再 **`RemoveChild` 后挂到 `DlgHost`**，最后对旧 Dlg（若曾挂在 `DlgHost`）执行关闭动画与释放。

### 12.4 Popup 与遮罩

- **API**：`OpenPopupAsync<TPayload>(string id, TPayload payload, PopupReopenBehavior behavior, bool maskClickClosesPopup = true, CancellationToken cancellationToken = default)`。  
- **枚举实现名**：`PopupReopenBehavior.None`（0）、`ReplayOpenAnimation`（1）、`ReplaceInstance`（2），语义与 §4.2 表一致。  
- **`BasePopup`**：`[Export] ColorRect? ModalMask`；`MaskClickClosesPopup`（默认 `true`）；`SetMaskVisible`；遮罩 `GuiInput` 左键按下时触发 **`CloseRequested`**。  
- **`UiManager.RefreshPopupMasks`**：仅栈顶 Popup 显示遮罩；若 `MaskClickClosesPopup` 为真，为栈顶订阅 **`CloseRequested` → `CloseTopPopup`**（切换栈顶时会先取消旧订阅，避免重复绑定）。

### 12.5 注册与重复 Id

- **重复注册**：`UiIdDuplicateGuard` + `UiRegistry`，重复 Id 抛 **`InvalidOperationException`**（与实现计划中「抛异常」策略一致）。

### 12.6 测试范围

- **自动化**：`Tests/kemo_card.Ui.Tests` 覆盖 `UiStateMachine`、`UiIdDuplicateGuard`、`UiRegistry.TryGet` 等 **无 Godot 节点实例化** 的逻辑。  
- **Godot 树与输入**：Dlg/Popup 叠放、遮罩点击关闭等以 **编辑器运行 + 手测** 为主（见实现计划 Task 4/5 手测清单）。
