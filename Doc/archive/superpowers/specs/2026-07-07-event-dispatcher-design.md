# 事件分发器设计

**日期**：2026-07-07  
**状态**：已实现  
**范围**：Godot 4.x Mono（C#）下的类型安全事件分发系统，含 `EventDispatcher`、`EventKey<TPayload>`、Source Generator 支持的声明式事件表及错误日志抽象。

---

## 1. 目标与非目标

### 1.1 目标

- **类型安全**：每个事件 Id 绑定唯一的 `TPayload` 类型，编译期 + 运行期双重校验。
- **调用方可追踪**：每个监听器关联 `caller` 对象，支持按 `caller` 批量取消订阅（典型场景：控制器 `Dispose` 时自动清理其所订阅）。
- **低分配**：单监听器 `Send` 零分配且不经装箱；多监听器仅快照数组一次分配。
- **声明式注册**：通过 Source Generator 从枚举 + Attribute 生成 `EventKey` 静态字段及 `OnXxx`/`NotifyXxx` 包装方法。
- **分层隔离**：每 Mod 独立 `InternalBus`（`BaseMod` 内），全局跨功能通信走 `GlobalEvents.Bus`。

### 1.2 非目标

- 不提供跨进程 / 跨网络事件总线。
- 不提供事件持久化、重放或 Qos 语义。
- 不做复杂的事件流管道（`filter`/`map`/`throttle` 等），由调用方自行组合。

---

## 2. 核心架构

### 2.1 组件图

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

### 2.2 关键类型

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

## 3. EventKey——事件标识

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

## 4. EventDispatcher——核心分发器

### 4.1 内部数据结构

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

### 4.2 API

#### 注册

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

#### 派发

```csharp
void Send<TPayload>(EventKey<TPayload> key, TPayload data);
```

- 锁内快照监听器列表，锁外派发以避免重入死锁。
- 单监听器：直接强类型 `Invoke`——零分配、不经装箱。
- 多监听器：复制一份 `IEventListener[]`——一次数组分配，按序强类型 `Invoke`。

#### 取消

```csharp
void Off<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>>? handler = null, object? caller = null);  // 精确取消
void OffId(int id);                                                                       // 按 Id 全部取消
void OffCaller(object? caller);                                                           // 按 caller 全部取消
void OffAll();                                                                             // 清空所有
```

#### 查询

```csharp
bool Has<TPayload>(EventKey<TPayload> key,
    Action<TPayload, IEventListener<TPayload>>? handler = null, object? caller = null);
```

### 4.3 线程模型

- 设计目标为 **Godot 主线程派发**。
- 所有公开 API 通过 `_gate` 锁保证互斥安全。
- `Send` 在锁内快照、锁外派发以避免重入死锁。
- `EventListener<TPayload>.IsActive` 非 volatile，不承诺跨线程可见性。

### 4.4 派发失败策略

```csharp
public enum EventDispatchFailureMode { LogAndContinue, Throw }
```

- 默认 `LogAndContinue`：监听器抛异常时记录日志并继续派发给后续监听器。
- `Throw`：监听器抛异常时立即向上抛出，中断后续派发。

---

## 5. 日志抽象

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

## 6. 全局事件总线

```csharp
public static class GlobalEvents
{
    public static EventDispatcher Bus { get; } = new();
}
```

- 跨 Mod 通信的唯一通道。
- 功能内部通信应使用 `BaseMod.InternalBus`，避免全局总线耦合。

---

## 7. Mod 内部事件总线

### 7.1 BaseMod

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

### 7.2 BaseController

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

## 8. 声明式事件表（Source Generator）

### 8.1 使用方式

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

### 8.2 编译期校验

Source Generator 输出诊断：

| 编号 | 级别 | 含义 |
|---|---|---|
| `KMV001` | Error | 事件表未声明为 `partial` |
| `KMV002` | Error | `EventTable` 的 `EnumType` 参数不是枚举 |
| `KMV003` | Error | `EventTable` 的 `ModType` 参数不派生自 `BaseMod` |
| `KMV004` | Warning | 枚举无任何 `[EventPayload]` 标注的成员 |

---

## 9. 典型使用模式

### 9.1 声明载荷

```csharp
public readonly struct RunGoldChangedPayload
{
    public int PreviousAmount { get; init; }
    public int CurrentAmount { get; init; }
}
```

### 9.2 订阅

```csharp
// 在 Controller 中订阅
InternalBus.On(CardEventTable.CardPlayed, (payload, listener) =>
{
    GD.Print($"卡牌 {payload.CardId} 已打出");
}, caller: this);
```

### 9.3 派发

```csharp
// 在 Controller 或 Model 中派发
InternalBus.Send(CardEventTable.CardPlayed, new CardPlayedPayload { CardId = "fireball" });
```

### 9.4 UI 事件

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

### 9.5 一次性监听

```csharp
InternalBus.Once(SomeEvent, (payload, listener) =>
{
    // 仅执行一次，自动取消
}, caller: this);
```

---

## 10. 设计权衡

| 决策 | 理由 |
|---|---|
| `int` 作 Id，而非 `string` | 枚举整型值零分配比较，无需字符串哈希 |
| `caller` 为 `object?`，非泛型约束 | 保持 `EventDispatcher` 对任意 `caller` 类型开放 |
| `Send` 锁内快照、锁外派发 | 避免监听器回调中发起新的 `Off`/`Send` 导致死锁 |
| 单监听器零分配路径 | 大量场景仅一个订阅者，避免不必要的数组分配 |
| `_callerMap` 维护双重索引 | `OffCaller` O(n) 清理整组订阅，牺牲少量内存换取消便利性 |
| Source Generator 而非反射 | AOT 友好、编译期错误报告、零启动开销 |

---

## 11. 测试

- 注册/取消未发生异常。
- `Send` 对单/多/零监听器的行为。
- `Once` 仅执行一次后自动取消。
- `OffCaller` 精确清理指定 caller 的所有订阅。
- 同一 `id` 注册不同 `TPayload` 应抛出 `InvalidOperationException`。
- `OffCaller` 后 `off` 的 listener 不会被派发。
- `Dispose` → `OffCaller` 端到端集成验证。

---

## 12. 文件布局

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

## 13. 后续步骤

1. 本文档反映当前事件系统（`Src/frame/mvc/EventDispatcher.cs` 等）的完整实现。
2. 随代码变更同步更新本文档。
