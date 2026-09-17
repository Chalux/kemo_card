# 界面与功能 Mod 绑定 + 统一订阅生命周期设计

**状态**：活规格（2026-09-15 起）
**权威链**：服从 [ui-manager-design](2026-05-15-ui-manager-design.md)（UI 管理器与层级/状态机）与 [event-dispatcher-design](2026-07-07-event-dispatcher-design.md)（事件分发器）；本文只补充**归属**与**订阅生命周期**两条新规则，不重定义层级、状态机与遮罩语义。
**影响面**：`Src/frame/ui/`、`Src/mod/*/Ui/`、`Src/MainRoot.cs`、`Src/mod/ModFactory.cs`

---

## 1. 目标与非目标

### 1.1 目标

| 编号 | 目标 |
|------|------|
| **G1 归属** | 每个界面声明**唯一归属的功能 Mod**，并且只能拿到该 Mod 的门面；归属在注册期即校验，漏填=启动报错 |
| **G2 统一订阅生命周期** | 订阅只经一个记账对象登记；框架级生命周期 `OnExitTree` 保证「离场必解绑」；子类不再 override 引擎的 `_ExitTree` |
| **G3 Mod 级界面生命周期** | 可按 Mod 批量关闭/销毁界面并清理其订阅（Run 结束、后续 Mod 卸载都要用） |
| **G4 修掉现存缺陷** | 修复调查发现的 D1–D5（见 §2），它们都是「订阅生命周期没有统一出口」的直接后果 |

### 1.2 非目标（YAGNI）

- **内容 Mod 提供界面**：`Config/mods/*` 目前是纯数据（含脚本效果），不含界面声明。若将来要支持，属「脚本驱动 UI 声明」的独立课题，本文不涉及，但接口预留（`IUiOwner` 以 `ModId` 为键，不假设实现是 C# 类）。
- **反射/程序集扫描自动发现 Mod**：违背组合根**纯手工装配**取向（AGENT.md §2），且对 AOT/裁剪不友好。采用**单一显式清单**（§7）。
- **重做 UI 状态机 / 层级 / 遮罩 / 动画**：见 ui-manager 规格。
- **跨 Mod 界面复用**（一个界面同时属于两个功能）：不支持；需要复用的部分下沉到 `frame` 组件。

---

## 2. 现状与问题（含证据）

### 2.1 两套并行的绑定实现

| 实现 | 覆盖对象 | 记账方式 |
|------|----------|----------|
| `BaseUI`（`_clickActions` / `_guiInputHandlers` / `_unbindActions` + `OnClicks` / `Bind` / `ClearLifeCycle`） | `BaseWin` → `BaseDlg`/`BasePge`/`BasePop`；`BaseCmp` → 3 个 `Setting*Row` | ✅ 有记账，`_ExitTree` 统一解绑 |
| **各组件手搓**（自有 `_bound` + `InitEvent` + `Unbind`/`OnUnbind` + 裸 `+=`/`-=`） | `BaseKemoButton : Button`、`BasePager : Control`、`BaseDlgComp : Control`、`BaseCardItem : Control`、`BaseCharacterItem : Control`、`VirtualList : Control`、`KeywordTipService : CanvasLayer` | ❌ 各自为政 |

这些组件**无法继承** `BaseUI`——Godot C# 要求节点类型出现在继承链上（`Button` / `CanvasLayer` 不是 `Control` 的子类关系可复用）。所以问题不是"它们忘了用基类"，而是**框架只提供了基类形式的实现，没有提供可组合的实现**。

### 2.2 D1（真实缺陷，高）：`_eventsBound` 守卫击败框架契约 → 关闭一次后重开，控件失联

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

### 2.3 D2：约 40 处裸 `+=` 不在框架覆盖范围内

`SettingDlg`(14)、`CodexDlg`(7)、`CardDetailsDlg`(2)、`CharacterDetailsDlg`(1)、`StorySelectDlg`(1)、`VirtualList`(1)、`BaseKemoButton`(7)、`BasePager`(6)、`BaseCardItem`/`BaseCharacterItem`(各 2) 等。与 D1 叠加后行为不可推理。

### 2.4 D3：没有"归属"概念

`UIRegistration` / `UIRuntimeEntry` / `UIVo` 均无 `OwnerModId`。因此无法"关闭并销毁某功能的全部界面及其订阅"，也无法校验"这个界面是谁的"。

### 2.5 D4：界面取数靠服务定位，"基于功能"只是约定

界面通过 `AppRoot.Services.Xxx`（12 处：`CodexDlg:347,404,576,634`、`BaseCardItem:213,514`、`SettingDlg:121,373,430`、`RunMainWin:64`、`StorySelectDlg:60,75`、`CharacterArtLoader:118`）与静态门面（`RunRuntime.Current`）跨模块取数据。`CodexDlg`（global）完全可以摸到 Run 的运行时，反之亦然。

### 2.6 D5（新发现，决策 1 的前置阻塞）：`UIOpenOpt` 合并在 5 个字段上无条件覆盖

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

## 3. 方案总览

```
层 1  BindingScope + 信号糖 + 框架级 OnExitTree      ← G2，可组合，不依赖继承
层 2  IUiOwner 归属契约 + 归属门面解析               ← G1 + D4
层 3  CloseAllByOwner / 逐界面选项覆写               ← G3 + D5
组合根 FeatureModCatalog（唯一显式清单）             ← G1 的装配侧
```

三层互不依赖，可分批落地；层 1 是其余两层的地基。

---

## 4. 层 1：BindingScope 与框架级生命周期

### 4.1 `BindingScope`（纯 C#，可单测）

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

### 4.2 信号糖：一个扩展方法集，两种宿主共用

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

### 4.3 框架级生命周期 `OnExitTree`（决策 2）

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

> 这类节点若同时也订阅了功能总线，需同 `BaseUI` 一样补 `OffCaller(this)`；由 `BindingScope` 不负责总线，交由各节点显式调用（总线订阅罕见，且 `BaseUI` 已覆盖绝大多数场景）。

### 4.4 解绑职责表

| 订阅类型 | 登记方式 | 解绑者 |
|---|---|---|
| Godot 信号（同节点子树内） | `Binder.OnXxx(node, handler)` | `Binder.UnbindAll()`（框架，`_ExitTree`） |
| Godot 信号（跨节点、如 `KeywordTipService._currentAnchor.TreeExited`） | `Binder.Bind(() => x.TreeExited += h, () => x.TreeExited -= h)` | 同上 + 调用方需自行 `IsInstanceValid` 判断 |
| 功能内部总线 | `Binder.OnBusEvent(key, handler)`（caller 固定 this） | `OwnerBus.OffCaller(this)` + `Binder` |
| 跨功能总线 `GlobalEvents.Bus` | `Binder.OnGlobalEvent(key, handler)` | `GlobalEvents.Bus.OffCaller(this)` |
| 静态门面事件（`RedDotService.OnStateChanged` 等） | `Binder.Bind(() => X += h, () => X -= h)` | `Binder.UnbindAll()` |

**用事件总线替代静态 C# 事件**是推荐做法（`RedDotService.OnStateChanged` 已因此类问题被修过一次）；此处只保证**即使用静态事件也能被统一登记**。

---

## 5. 层 2：归属契约

### 5.1 归属契约：声明与门面**分离**（`IUiOwner` 单接口方案已废弃）

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

### 5.2 归属落到运行时

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

### 5.3 注册期硬校验（`UIRuntimeRegistry.Validate()` 增补）

1. `OwnerModId` 非空；
2. 同一 `Id` 不得被两个不同 owner 注册（同 owner 重复注册覆盖，沿用现有语义并告警）；
3. owner 必须出现在组合根的已装配 Mod 清单中（防止拼错 id）。

任一条不满足 → `AppLog.Error` 且**注册视为失败**（沿用现有 `Validate` 的"报告不抛"风格，但新增一条 `HasErrors` 供启动期断言）。

### 5.4 归属门面解析（决策 3：统一修正 D4）

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

## 6. 层 3：Mod 级界面生命周期

### 6.1 `UIManager` 增补

```csharp
/// <summary>关闭某功能 Mod 的全部界面。</summary>
public void CloseByOwner(string ownerModId, bool destroy = false);

/// <summary>注销某功能 Mod 的全部界面（连 UIVo 一并移除，防 Run 结束后残留）。</summary>
public void UnregisterOwner(string ownerModId);
```

`destroy: true` 走 `EUIOpenOpt`/`CacheTime = 0` 语义（关闭即 Destroy）；`false` 走缓存。两者都复用 §4 的 `_ExitTree` 解绑，无需额外清理代码。

### 6.2 决策 1：Run 结束销毁 `RunMainWin`

- `RunMod.GetUIRegistrations()` 中 `RunMain` 注册为 `CacheTime = 0`（关闭即销毁）；
- `RunRuntime.Abandon()` / `RunRuntime.CreateNew()` 的 `_current?.Dispose()` 之前，调用 `UIManager.Instance?.CloseByOwner("run", destroy: true)`；
- `StorySelectDlg` **保持缓存**（它是从菜单反复进出的短生命周期弹窗，销毁会丢失滚动位置等）。

### 6.3 决策 3 前置：`UIOpenOpt` 字段显式化（修 D5）

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

---

## 7. 组合根（决策 3 的装配侧）

`FeatureModCatalog`（§5.1）是唯一列出功能界面的地方。`MainRoot` 收敛为：

```csharp
foreach (var feature in AppRoot.Services.Features)
    foreach (var reg in feature.Declare())
        registry.Register(reg.ToRuntimeEntry());
```

> 仍是**显式手工装配**（符合 AGENT.md 对组合根的取向），但"新增一个功能 Mod 要改几处"从 **2 处（`MainRoot` + ModFactory）降到 1 处**。
> 门面解析由 `ModStartupResult` 实现的 `IUiFacadeProvider` 提供（§5.1 ②），因此声明与实例生命周期可以完全解耦。

---

## 8. 生命周期时序

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

## 9. 测试

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

## 10. 迁移步骤（每步可独立编译与验证）

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

步骤 4 是**纯缺陷修复**，即使其余方案要调整也建议优先落地。

---

## 11. 风险与兼容

| 风险 | 处理 |
|---|---|
| `_ExitTree` 收敛 sealed 会让现有 override 编译失败 | **这正是目的**：强制暴露所有绕过框架的清理点。已知受影响 8 处（`BaseCmp`、`VirtualList`、`BaseDlgComp`、`BaseKemoButton`、`BasePager`、`BaseCardItem`、`BaseCharacterItem`、`KeywordTipService`），全部改 `OnExitTree` |
| `BindingScope` 解绑后仍可重新登记（无"已解绑"态） | 这是缓存重开的必要条件；若未来确需"永久离场后禁止订阅"，应作为显式 `Close()`（真销毁）路径的附加状态，而不是 `UnbindAll` 的副作用 |
| `Facade<T>()` 迁移 12 处可能触碰未测路径 | 分批提交，每批跑全量测试；`RunMainWin`/`CodexDlg` 优先 |
| `MergeInto` 显式化改变合并语义 | 由「Default 基线全非空」保证结果不变；补 3 条回归测试（含 D5 场景） |
| `KeywordTipService` 是 `CanvasLayer`（顶层常驻，几乎不离树） | 它的 `TreeExited` 订阅本就带 `IsInstanceValid` 防御，单独保留现状，只补 `Binder` 登记 |

---

## 12. 自检记录

- [x] 与 ui-manager 规格不冲突：本文不改层级/状态机/遮罩/动画语义，只加归属与订阅生命周期
- [x] 依赖方向：`IUiOwner` / `BindingScope` 均在 `frame/ui`，不引入 `frame` → `mod` 引用
- [x] 不新增 Godot 依赖到可测代码：`BindingScope` 为纯类
- [x] 决策落地：①Run 结束销毁（§6.2）②框架级 `OnExitTree`，不 override 引擎函数（§4.3）③统一修正含归属门面与 D5（§5.4/§6.3）④先规格后代码（本文）
- [x] 步骤 1 `BindingScope` + `BindingScopeSignals` + 单测（8 例）
- [x] 步骤 2 `UIOpenOpt` 字段显式化 + `MergeFrom` + D5 回归测试
- [x] 步骤 3 `BaseUI` / `BaseCmp` / `BaseMask` 接入 `Binder`，`_ExitTree` 收敛 sealed → `OnExitTree`
- [x] 步骤 4a **修 D1**：5 个对话框删除 `_eventsBound` 守卫与字段，24 处订阅迁入 `Binder`（`CodexDlg` 9 / `SettingDlg` 11 / `CardDetailsDlg` 2 / `CharacterDetailsDlg` 1 / `StorySelectDlg` 1）；复核「5 个对话框内零裸订阅」通过
- [x] 步骤 4b 6 个非 `BaseUI` 组件接 6 行模式：`BaseDlgComp`（参考实现）、`BaseKemoButton`、`BasePager`、`BaseCardItem`、`BaseCharacterItem`、`VirtualList`；删除全部 `_bound` 手写守卫与 `EnsureBound`/`Unbind`/`UnbindControls` 簿记成员；并修 `VirtualList` 重入树后滚动失效（`OnExitTree` 复位 `_initialized`）
- [x] 步骤 4d **补齐 D1（审阅发现）**：组件订阅登记从 `_Ready` 移到 `_EnterTree`。`_Ready` 每节点只调用一次，缓存重开（`RemoveChild` → `AddChild`）不再触发，故 4a/4b 只修好了对话框自身；`BaseCmp.InitEvent()` 改由 `_EnterTree` 驱动（覆盖 3 个 `Setting*Row`），`BaseKemoButton` / `BaseCardItem` / `BaseCharacterItem` / `BasePager` 的订阅同样改挂 `_EnterTree`，`_Ready` 只留一次性初始化；`VirtualList` 顺带修掉重入树时重复新建 `ItemContainer` 与 `_scrollBar` 缺失 null 守卫
- [x] 步骤 4c `KeywordTipService` 接账本：按锚点的**动态**订阅用独立 `_anchorBinder`，换锚点只拆这一条；保留原 `IsInstanceValid` 防御语义
- [x] 全 UI 目录复核：**0 处裸订阅**（所有 `+=` 均在 `Binder.Bind(...)` 的 add-lambda 内或为算术运算）
- [x] 步骤 5 归属契约：`UIRegistration.OwnerModId`（必填）+ `UIRuntimeEntry`/`UIVo` 落 owner + `Validate` 三条硬校验（`HasErrors`）+ `GetByOwner`；13 个回归测试
- [x] 步骤 6 组合根：`FeatureModCatalog` 迭代注册，`MainRoot` 不再硬编码 Mod
- [x] 步骤 7 `CloseByOwner` / `UnregisterOwner`；决策 1：`RunMain` 注册为 `CacheTime = 0`，`RunRuntime.CreateNew`/`Abandon` 调 `CloseByOwner(run, destroy: true)`
- [x] 步骤 8（框架侧）`IUiFacadeProvider` + `BaseWin.Facade<T>()` / `BaseMask.Facade<T>()`；`SettingDlg` 3 处改为 `Facade<GlobalModController>()`。**跨功能读仅剩 `StorySelectDlg:53`**（见 §5.4），内容注册表读取按设计保留
- [x] 步骤 9 同步 `Doc/AGENT.md` §3（三条硬性约定）与 §5；`Doc/INDEX.md` 活规格表

---

## 12.2 实现备注（与当前代码对齐）

| 落地项 | 位置 | 说明 |
|---|---|---|
| 订阅账本 | `Src/frame/ui/BindingScope.cs` | 纯 C#，可单测；`UnbindAll` 逆序 + 单条异常不阻断 + 可重复调用 |
| Godot 信号糖 | `Src/frame/ui/BindingScopeSignals.cs` | 扩展方法（`BaseUI` 与非 `BaseUI` 节点共用）；带 `IsInstanceValid` 解绑保护；参数化信号用 Godot 专用委托类型 |
| 框架生命周期 | `BaseUI.cs` / `BaseMask.cs` | `_ExitTree` **sealed** → `OnExitTree()` → `Binder.UnbindAll()` → `GlobalEvents.Bus.OffCaller(this)` |
| 6 行模式 | `BaseDlgComp.cs` 等 6 个组件 | 无法继承 `BaseUI` 的节点自行组合 `BindingScope`；**订阅登记挂 `_EnterTree`** |
| 组件订阅登记点 | `BaseCmp.cs`（`_EnterTree` → `InitEvent`）+ 5 个组件 | `_EnterTree` 每次进树都触发，`_Ready` 只触发一次（缓存重开不再触发） |
| 归属声明 | `UIRegistration.OwnerModId` → `UIRuntimeEntry`/`UIVo` | 工厂方法首参为 `ownerModId` |
| 归属校验 | `UIRuntimeRegistry.Validate(knownOwnerModIds)` | 三条硬校验；`Register` 另检同 id 跨 owner 抢占；结果见 `HasErrors` |
| 组合根登记 | `Src/mod/FeatureModCatalog.cs` | 唯一列出功能界面的地方；`ModFactory` 暴露 `Features` |
| 门面解析 | `Src/frame/ui/IUiFacadeProvider.cs` | `ModStartupResult` 实现；`run` → `RunRuntime.Current`（会话级动态解析） |
| 界面取门面 | `BaseWin.Facade<T>()` / `BaseMask.Facade<T>()` | 类型不符明确抛错 |
| Mod 级生命周期 | `UIManager.CloseByOwner` / `UnregisterOwner` | 决策 1 由 `RunMod` 的 `CacheTime = 0` + `RunRuntime` 调用点共同实现 |
| 选项合并 | `UIOpenOpt.MergeFrom` + `Effective*` | 只覆盖显式指定字段（原 `UiManager.MergeInto` 已删除） |

### 已知未处理的跨功能读

`StorySelectDlg.OnOpen` 直接取 `AppRoot.Services.GlobalController` 构造 `GlobalPersistentCondContext`（run 的界面读 global 的解锁账本）。干净解法是 frame 级 `IPersistentCondContext` 提供者；**未实现**，代码处已标注，见 §5.4。

### 12.1 实施中修正的规格问题（记录，避免重蹈）

| 原设计 | 问题 | 修正 |
|---|---|---|
| `BindingScope` 在 `UnbindAll` 后禁止再 `Bind`（抛异常） | 缓存重开时节点只是 `RemoveChild`，重开会再次 `InitEvent` → **必然崩溃** | 取消"已解绑"状态，`UnbindAll` 只清账本，对象可复用 |
| 功能 Mod 实例实现 `IUiOwner` | `RunMod` 是会话级对象，而注册只在启动期发生一次 → 双实例语义 | 拆成静态**声明**（`FeatureModCatalog`）+ 动态**门面**（`IUiFacadeProvider`） |
| 组件订阅登记留在 `_Ready`（`BaseCmp.OnReady` → `InitEvent`，其余组件直接写 `_Ready`） | Godot 的 `_Ready` **每个节点只调用一次**；缓存重开是 `RemoveChild` + `AddChild`，**不会**再触发 `_Ready`。于是组件订阅在首次离场被 `Binder` 解绑后**永不重新登记** —— D1 只修好了对话框自身，`SettingSliderRow` / `SettingToggleRow` / `SettingDropdownRow`（以及 `BaseKemoButton` / `BaseCardItem` / `BaseCharacterItem` / `BasePager` 的悬停与点击）依旧失效 | 订阅登记一律改挂 `_EnterTree`（每次进树都触发）；`BaseCmp.InitEvent()` 由 `_EnterTree` 驱动；`_Ready` 只保留一次性初始化。写进 AGENT.md §3 硬性约定 |
| `VirtualList.OnExitTree` 复位 `_initialized` 但不清 `_itemContainer` | 重入树后 `EnsureInitialized()` 会**再建一个** `ItemContainer` 挂到 `ScrollArea`，滚动范围翻倍 | 容器仍有效时复用，仅在失效时新建；并补回 `_scrollBar` 的 null 守卫 |

---

## 13. 后续步骤

1. 按 §10 顺序实施；每完成一步回写本文「§12 自检记录」与实现备注。
2. 实施完成后：`Doc/AGENT.md` §3 增补两条硬性约定（**界面订阅必须经 `BindingScope`**、**UI 节点不得 override `_ExitTree`，改 override `OnExitTree`**）；§5 增补 `BindingScope` / `IUiOwner` / `FeatureModCatalog` 入口行。
3. `Doc/INDEX.md` 活规格表增补本文。