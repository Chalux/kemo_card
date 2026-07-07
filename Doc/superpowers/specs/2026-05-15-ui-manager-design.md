# UI 管理器与 BaseUI 体系设计

**日期**：2026-05-15  
**状态**：已定稿待实现评审  
**范围**：Godot 4.x Mono（C#）下，除 `MainRoot` 启动场景外，以 **UI 管理器** 为唯一 UI 打开入口；`BaseUI` / `BaseWin` / `BaseDlg` / `BasePge` / `BasePop` 生命周期、层级、注册表与强类型载荷。

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

- **Load**：异步加载场景资源（`ResourceLoader.LoadThreadedRequest`）+ 遮罩资源。
- **PreLoad**：调用 `OnPreLoad(done, fail)`，业务可在此进行数据准备。
- **Create**：首次打开，初始化界面和遮罩，挂入场景。
- **Open**：播放打开动画，派发 `UIEvent.Open` 事件。
- **Close**：播放关闭动画。
- **CloseDone**：从层级移除、决定缓存或销毁。
- **Cache**：节点保持存在但移出场景树，`CacheTime = -1` 为永久缓存，`> 0` 为定时销毁，`0` 为立即销毁。
- **Destroy**：清理资源、`QueueFree`、从 `UIVoRegistry` 移除。

每个 `UIVo` 持有一个 `StateMachine<EUIState, IUIStateContext>`，通过注入的 `IStateHandler` 列表驱动流转。

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
| `UILoadStateHandler` | `Load` | 异步加载场景 + 遮罩资源，命中缓存直接跳到 PreLoad |
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
| `UIOpenCoordinator` | 打开队列管理、当前打开项 `_currOpening`、队列去重、加载超时（10s）检测 |
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
