# UI 管理器与界面基类（`BaseUI` → `BaseDlg` / `BasePopup`）设计规格

**日期**：2026-05-12  
**状态**：待你评审（评审通过后进入 `writing-plans` 实现计划）  
**范围**：`Src/frame/ui/`（名称可微调）内 `IUIManager` 契约与默认实现、`BaseUI` / `BaseDlg` / `BasePopup` 生命周期、与 `FeatureKit` 的组合方式；不含具体业务界面实现。

---

## 1. 目标与约束

### 1.1 目标

- 提供 **`IUIManager`**：异步打开/关闭、请求队列、对象池与预加载、过渡与表现层动画的触发点（与完成语义分离，见 4.3）。
- 提供 **`BaseUI` → `BaseDlg` / `BasePopup`**：`partial class`，继承 `Control`，与 Godot 场景脚本一致；子类定义并暴露**各自的数据载荷类型**（`record` / `interface` 等），打开入口**强类型**绑定载荷。
- **渲染与输入分层**：采用 **`CanvasLayer` 粗分层**（固定层枚举，业务禁止随意改层号），栈内顺序表达叠层细节。
- **与 `FeatureKit` 衔接**：`IUIManager` 经 **`RegisterFacade<IUIManager>`** 暴露；组合根向 `IFeatureCompositionContext` 注入 **UI 挂载根**（见 2.4）。

### 1.2 已确认的交互规则

- **同一时间只有一个处于「活动」状态的 `BaseDlg`**（`Dialog` 层单槽）。新开 Dlg 时默认：**先异步关闭当前 Dlg（含关闭侧过渡钩子）再挂新实例**，避免两个 Dlg 同时抢输入。
- **`BasePopup` 可多实例叠放**：显式栈维护；**上层 Popup 阻塞下层 Popup**；**`Popup` 层整体高于 `Dialog` 层**，因此 **任意 Popup 都会阻塞 `BaseDlg`**。

### 1.3 约束（工程）

- **仅使用 C#**；代码归属 `Src/frame/ui/`（或评审后更名目录）。
- **组合优先于继承**：管理器内部可用小服务类拆分队列、池、预加载；基类以模板方法为主，避免过深继承链。

---

## 2. 架构与组件

### 2.1 分层与目录（建议）

| 位置 | 内容 |
|------|------|
| `Src/frame/ui/` | `IUIManager`、`UiLayerKind`（或等价枚举）、`UiOpenArgs`、`DefaultUIManager`（或同名实现）、`IUIPrefabPool`（可选）、`BaseUI`、`BaseDlg`、`BasePopup` |
| `Src/frame/featurekit/`（增量） | `IFeatureCompositionContext` 增加 **UI 挂载根** 只读访问（见 2.4） |
| 组合根 | `MainRoot`（或专用启动类）构造 `CanvasLayer` 子树并传入 `FeatureCompositionContext` |
| 注册 | 某一功能包（可为专用「框架 UI 包」）在 `Install` 中构造 `DefaultUIManager` 并 `RegisterFacade<IUIManager>` |

### 2.2 `CanvasLayer` 与层枚举

- 至少三层（层号递增）：**`Hud`（或 `Game`）**、**`Dialog`**、**`Popup`**。
- **`BaseDlg`**：仅挂载于 **`Dialog`** 层；管理器保证该层**最多一个活动** Dlg 实例。
- **`BasePopup`**：挂载于 **`Popup`** 层；**同级兄弟顺序或子层偏移**表达栈内上下关系；栈顶获得输入，下层被阻塞（实现计划选定：`MouseFilter` / 统一命中代理 / 引擎事件顺序之一，并说明调试要点）。

### 2.3 `IUIManager`（门面能力）

- **异步 API**：`OpenDialogAsync`、`PushPopupAsync`、`CloseDialogAsync`、`PopPopupAsync` 等（具体签名在实现计划中定稿），支持 **`CancellationToken`**。
- **队列**：对「打开/关闭」引起的结构性操作 **FIFO 串行**，避免同父节点上并发抢挂载与动画状态机打架。
- **池与预加载**：按键（如 `PackedScene` 资源路径或稳定 id）存取实例；`PreloadAsync` / `WarmPool`；关闭时 **归还池或 `QueueFree`** 的策略在实现计划中二选一并文档化默认行为。
- **关闭侧过渡**：子类可选钩子；管理器在 `Shutdown` 时逆序关闭所有 Popup 与 Dlg 并清空队列与池。

### 2.4 与 `FeatureKit` 的组合

- **`IFeatureCompositionContext` 增加只读 `Control UiMountRoot { get; }`**（若需更窄类型可评审为 `Node` + 运行时校验）。由 **`MainRoot` 在构造 `FeatureCompositionContext` 时传入** 已摆好 `CanvasLayer` 的子树。
- **某功能包** `Install`：`new DefaultUIManager(ctx.UiMountRoot, ...)` → `ctx.RegisterFacade<IUIManager>(...)`。
- **`FeatureManager.ShutdownPackages`**：该包 `Shutdown` 时释放管理器（关闭 UI、取消进行中的 `CancellationTokenSource` 等）。

---

## 3. 基类与载荷

### 3.1 类型关系

- **`BaseUI : Control`（`partial`）** → **`BaseDlg : BaseUI`**、**`BasePopup : BaseUI`**（二者并列，不互相继承）。
- **子类定义载荷**：例如 `public sealed record Payload(...)` 或对外 **`interface IShopDlgPayload`**；子类以 **`BaseUI<TPayload>`**（或等价约束）绑定，使 **`Open*` 泛型 API** 在编译期关联 **`TUi : BaseUI<TPayload>`** 与 **`TPayload`**。
- **管理器侧**：`OpenDialogAsync<TUi, TPayload>(..., TPayload payload)` 等形式，避免默认路径上的 **`object` + 强转**。

### 3.2 管理器校验

- **Dlg 预制体**不得通过 `PushPopup` 打开；**Popup 预制体**不得通过 `OpenDialog` 打开。开发配置下 **`GD.PushWarning` 或抛异常** 二选一，在实现计划中写死。

---

## 4. 打开生命周期、动画与 `Task` 完成语义（**选项 B**）

### 4.1 顺序（固定）

1. **实例进入场景树**（含引擎 `_Ready` 等规则；若基类需统一 `await` 首帧或等待特定 `Notification`，在实现计划中列最小集合）。
2. **应用载荷**：子类实现 **`Task OnBindPayloadAsync(TPayload payload, CancellationToken cancellationToken)`**（或评审后等价名称）：根据数据刷新控件；可在此异步加载子资源、等待图标、**等待布局稳定**（例如 `await ToSignal(GetTree().CreateTimer(0), SceneTreeTimer.SignalName.Timeout)` 一帧或 `await` 子 `Task`）。
3. **交互就绪闸门**：基类在绑定 `Task` **成功完成后** 将界面标为 **可交互**（例如恢复 `MouseFilter`、启用按钮、或解除「加载中」遮罩）。**`Open*` 返回的 `Task` / `Task<TResult>` 在此刻进入已完成状态（RanToCompletion）。**
4. **开窗动画（纯表现）**：**紧接在步骤 3 之后** 调用虚方法 **`PlayOpenAnimationAsync(CancellationToken cancellationToken)`**；**不得**将动画 `await` 并进 **`Open*` 的完成路径（选项 B）**。
5. **动画结束**：可选钩子 **`OnOpenPresentationFinished()`**（无 `Task` 契约保证）；若某业务必须等待动画结束，应显式 **`await ui.WaitForOpenAnimationAsync()`** 一类**单独 API**（可选、YAGNI 时可在二期再加）。

### 4.2 与「先数据后动画」的关系

- **仍然保证**：**数据绑定与交互就绪发生在开窗动画启动之前**，避免出现「空壳播动画、数据后到再跳变」。
- **与选项 B 的关系**：**交互与 `Open*` 的 `Task` 完成不等待动画**；动画是**附加表现**，可继续播放而不阻塞调用方继续逻辑。

### 4.3 取消与失败

- **`CancellationToken`**：在绑定阶段取消 → `Open*` 的 `Task` 以取消结果结束，**不启动**开窗动画（或启动前检查并 no-op）。
- **绑定失败**：`Open*` 的 `Task` 以异常结束；管理器负责 detach / 回池或释放，细节在实现计划中写清。

---

## 5. 数据流（概要）

- **调用方** → `IUIManager.OpenDialogAsync<TUi, TPayload>(scene, payload, ct)` → 管理器入队 → 实例化/取池 → 挂树 → **`BaseUI` 内部模板**：`OnBindPayloadAsync` → 标记可交互 → **完成 `Task`** → `PlayOpenAnimationAsync`（不阻塞 `Task`）。
- **跨包协作**：业务仍优先通过 **`GlobalEventBus`** 与契约 DTO；`IUIManager` 不替代事件总线。

---

## 6. 错误处理与测试

- **非法层、错误 API 打开错误预制体类型、`Shutdown` 后调用**：明确抛错或日志策略（实现计划定稿）。
- **测试**：队列/栈与 **`Task` 完成语义** 可在无头逻辑层单测；与 Godot 节点绑定的部分以最小集成或手工验证为主。

---

## 7. 规格自检摘要

- **载荷**：子类定义并暴露契约；`BaseUI<TPayload>` + 管理器泛型 API。
- **动画**：**数据绑定完成 → 可交互 → `Open*` `Task` 完成 → 再启动开窗动画**；动画**不**阻塞 `Open*` `Task`（**选项 B**）。
- **层级**：单活动 Dlg；Popup 栈；Popup 层高于 Dialog 层；上层阻塞下层。
- **组合根**：`UiMountRoot` 注入 `IFeatureCompositionContext`；`RegisterFacade<IUIManager>`。

---

## 8. 待实现计划承接点

- `FeatureCompositionContext` / `IFeatureCompositionContext` 的 **签名级增量** 与 `MainRoot` 场景子树示例。
- `DefaultUIManager` 与 Godot 4.6 C# API 对齐（`PackedScene.Instantiate`、输入吞没策略等）。
- 是否提供 **`WaitForOpenAnimationAsync`**：默认 **不提供**（YAGNI），若首版业务强依赖再在计划中加。
