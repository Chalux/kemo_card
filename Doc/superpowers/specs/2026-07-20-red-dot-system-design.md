# 红点系统设计

**日期**：2026-07-20
**状态**：已定稿
**范围**：`Src/frame/notification/` 下新建静态门面 `RedDotService` + 视觉组件 `RedDotCmp`。运行时注册 + 事件驱动 + 树状聚合的红点通知系统。

---

## 1. 目标与非目标

### 1.1 目标

- **导航引导**：为菜单按钮、页签等 UI 入口提供「此处有新内容或未处理事项」的红点提示。
- **树状聚合**：叶子节点状态变更自动向上冒泡，父节点聚合所有子节点状态（逻辑或）。
- **事件驱动评估**：每个红点节点注册检查函数和关注的事件，事件触发时自动重评；仅状态变化时向上冒泡。
- **自驱视觉组件**：`RedDotCmp` 挂到场景中，调用 `WatchRed(id)` 后完全自管理显示/隐藏，无需外部逻辑介入。
- **运行时注册**：节点和父子关系可以在任意顺序注册，支持动态增删。

### 1.2 非目标（YAGNI）

- **数字角标**：首版不做未读计数，仅支持二态（有红点 / 无红点）。
- **状态落盘**：红点状态为派生数据，不序列化到存档。数据源（如图鉴解锁状态）变更后，重新评估即可。
- **持久「已读」标记**：红点的消除由检查函数返回值决定，不引入「用户已查看」的额外持久化标记。
- **Toast / 横幅通知**：红点系统只管入口角标，不做飞行通知。

---

## 2. 文件结构

全部放在 `Src/frame/notification/`：

| 文件 | 职责 |
|------|------|
| `RedDotService.cs` | 静态门面，管理节点树、注册、评估、事件订阅、状态分发 |
| `RedDotNode.cs` | 内部树节点（`internal`），持有 id、检查函数、父子关系、事件订阅句柄 |
| `RedDotCmp.cs` | 视觉组件（`partial class Node2D` + `TextureRect` 子节点），`WatchRed` 自驱动 |

`RedDotCmp` 放在 frame 层——它只依赖 `RedDotService`，不加业务约束；模组直接在 Godot 编辑器中实例化 + 摆位即可。

---

## 3. 枚举定义

```csharp
public enum RedDotOverride
{
    None,           // 不重载——按子节点聚合 / checkFunc 判定
    ForceActive,    // 强制激活
    ForceInactive   // 强制不激活
}
```

---

## 4. `RedDotService` 公开 API

### 4.1 类型签名

```csharp
public static class RedDotService
{
    public static void Configure();

    // 基础注册（重载策略默认为 None）
    public static void RegisterNode(
        string id,
        Func<bool> checkFunc,
        params (EventDispatcher bus, string eventKey)[] triggers
    );

    // 带初始重载策略的注册
    public static void RegisterNode(
        string id,
        RedDotOverride override,
        Func<bool> checkFunc,
        params (EventDispatcher bus, string eventKey)[] triggers
    );

    public static void SetOverride(string id, RedDotOverride override);

    public static void RegisterParent(string childId, string parentId);

    public static void UnregisterNode(string id);

    public static void UnregisterParent(string childId, string parentId);

    public static bool IsActive(string id);

    public static void Refresh(string id);

    public static event Action<string, bool> OnStateChanged;
}
```

### 4.2 方法语义

| 方法 | 说明 |
|------|------|
| `Configure()` | 初始化内部状态。调用一次（`GlobalMod.Setup()` 或 `MainRoot` 启动阶段）。 |
| `RegisterNode(id, checkFunc, triggers)` | 注册一个红点节点（重载策略默认为 `None`）。同一 id 多次调用视为替换（后者覆盖前者）。`checkFunc` 可为 `null`——此时该节点为纯聚合节点，Active 完全由子节点决定。若提供 `checkFunc`，由注册方以闭包形式提供，捕获所需的外部数据源。`triggers` 为 `(EventDispatcher 总线, string 事件键)` 可变数组，支持零个或多个（纯聚合节点无需 triggers）。注册后若重载为 `None` 且 `checkFunc` 非 null，则**立即执行首评**。 |
| `RegisterNode(id, override, checkFunc, triggers)` | 同上，额外指定初始重载策略。适合注册时即需要强制压制或强制亮起的场景。 |
| `SetOverride(id, override)` | 运行时修改节点的重载策略，**立即生效**（触发 Active 重算和后续冒泡）。设置为 `None` 时恢复自动判定，使用最后一次事件触发时的内部评估结果，无需重新执行 checkFunc。 |
| `RegisterParent(childId, parentId)` | 构建父子关系。可与 `RegisterNode` 任意顺序调用——若一方尚未注册则暂存挂起，另一方注册时自动补建关系。同一对父子关系重复调用无副作用。 |
| `UnregisterNode(id)` | **级联**移除该节点及其所有后代节点，同时解除所有事件订阅。 |
| `UnregisterParent(childId, parentId)` | **仅断开**父子关系，子节点保留（变为根节点或下次被其他父节点收养）。断开后提示原父节点重新聚合。 |
| `IsActive(id)` | 查询节点当前激活状态。未注册的 id 返回 `false`。 |
| `Refresh(id)` | 手动触发重评。执行检查函数，若状态变化则冒泡通知父节点。通常事件触发已自动重评，此方法为补充入口（如初始化后的批量刷新）。 |
| `OnStateChanged` | 状态变更事件。`(string id, bool active)` —— 仅当状态实际变化时触发。 |

---

## 5. 内部行为

### 5.1 树节点（`RedDotNode`）

```csharp
internal class RedDotNode
{
    public string Id;
    public Func<bool> CheckFunc;
    public RedDotOverride Override;
    public bool Active;
    public bool LastEvaluated;     // 最近一次 checkFunc 的评估结果（重载期间保持追踪）
    public List<RedDotNode> Children;
    public RedDotNode Parent;
    public List<IDisposable> EventSubscriptions;
}
```

### 5.2 评估与冒泡

```
事件触发
  → 仅目标节点的 checkFunc() 被重新执行
  → 更新 LastEvaluated（无论重载策略）
  → 若重载为 None: 按下方规则计算最终 Active
  → 若重载非 None: Active 由重载值直接决定
  → 若 Active 与之前不同:
      更新节点 Active
      触发 OnStateChanged
      通知父节点重新聚合（递归向上）
  → 若状态不变: 停止，不冒泡
```

节点最终激活状态的判定规则（按优先级从高到低）：

```
if (Override == ForceActive)   → Active = true
if (Override == ForceInactive) → Active = false
else → Active = (CheckFunc != null && LastEvaluated) || Children.Any(c => c.Active)
```

即：
- 重载策略优先级最高：`ForceActive` 强制为 `true`，`ForceInactive` 强制为 `false`
- 无重载时：检查函数返回 `true` → 激活（无论子节点状态）
- 无重载时：任一子节点激活 → 激活（无论检查函数结果）
- 无检查函数、无子节点、无重载 → 始终 `false`

重载期间的检查函数仍会被事件触发执行，`LastEvaluated` 保持追踪最新结果。当 `SetOverride` 切回 `None` 时，无需重新执行 checkFunc，直接用 `LastEvaluated` 参与判定并通知父节点。

当子节点状态变更冒泡到父节点时，父节点执行上述规则重新判定自身状态；若状态变化则继续向上冒泡。

### 5.3 首评

`RegisterNode` 完成后，若重载为 `None` 且 `checkFunc` 非 null，则**立即同步调用** `checkFunc()`，结果存入 `LastEvaluated` 并作为初始 `Active`。首评不触发 `OnStateChanged`（因为没有「变化」可言），但若该节点有已注册的父节点，则触发父节点聚合和冒泡。

若注册时指定了 `Override != None`，则跳过首评，Active 直接由重载值决定。

### 5.4 重复注册

同一 id 再次调用 `RegisterNode`：
1. 解除旧的 `EventSubscriptions`
2. 替换 `checkFunc`、`Override` 和 `triggers`
3. 重新订阅事件
4. 按首评逻辑重新评估
5. 若状态与之前不同，触发变更流程

### 5.5 状态查询

`IsActive(id)` 为 O(1) 字典查询。未注册 id 返回 `false`。

### 5.6 重载策略

`SetOverride(id, override)` 运行时修改：
1. 更新 `RedDotNode.Override`
2. 按判定规则重新计算 Active
3. 若 Active 变化，触发 `OnStateChanged` 并向上冒泡

设置为 `None` 时：
- 用 `LastEvaluated` 和子节点状态重新判定
- 不重新执行 checkFunc（`LastEvaluated` 在重载期间已被事件持续追踪）
- 若 Active 变化，触发冒泡

重载不改变事件订阅——事件仍会触发并更新 `LastEvaluated`，只是对外表现为重载值。这保证了取消重载时状态的准确性。

---

## 6. `RedDotCmp` 视觉组件

### 6.1 节点结构

```csharp
public partial class RedDotCmp : Node2D
{
    private TextureRect _icon;
    private string _watchedId;

    public override void _Ready()
    {
        Visible = false;  // 默认不可见，覆盖场景树中的标记
        _icon = GetNodeOrNull<TextureRect>("Icon") ?? new TextureRect();
    }

    public void WatchRed(string redDotId)
    {
        if (_watchedId == redDotId) return;

        if (_watchedId != null)
            RedDotService.OnStateChanged -= OnRedStateChanged;

        _watchedId = redDotId;

        if (redDotId != null)
        {
            RedDotService.OnStateChanged += OnRedStateChanged;
            Visible = RedDotService.IsActive(redDotId);
        }
        else
        {
            Visible = false;
        }
    }

    private void OnRedStateChanged(string id, bool active)
    {
        if (id == _watchedId)
            Visible = active;
    }
}
```

### 6.2 场景结构

```
RedDotCmp (Node2D)
  └── Icon (TextureRect)  —— 红点贴图，在 Godot 编辑器中设置 Texture
```

### 6.3 使用方式

在 Godot 编辑器中：
1. 创建 `Node2D` 节点，挂载 `RedDotCmp` 脚本
2. 在其下创建 `TextureRect` 子节点，命名 `Icon`，设置红点贴图
3. 将该节点放到目标按钮/菜单的合适锚点位置
4. 父脚本中调用 `redDotCmp.WatchRed("Menu/Codex")`

红点默认不显示，订阅后由 `RedDotService` 状态自动驱动。

---

## 7. 生命周期与反注册

### 7.1 初始化

```csharp
// 在 GlobalMod.Setup() 或 MainRoot 启动阶段调用
RedDotService.Configure();
```

### 7.2 模组注册

```csharp
// 叶子节点：有检查函数，订阅关注的事件
RedDotService.RegisterNode(
    "Menu/Codex",
    () => GlobalModController.Instance.HasNewCodexEntries(),
    (GlobalMod.InternalBus, "OnCodexUnlockChanged")
);

RedDotService.RegisterNode(
    "Menu/Settings",
    () => GlobalModController.Instance.HasPendingSettings(),
    (GlobalMod.InternalBus, "OnSettingsChanged")
);

// 聚合节点：无检查函数，不订阅事件；Active 由子节点自动聚合
RedDotService.RegisterNode("Menu");

// 构建父子关系
RedDotService.RegisterParent("Menu/Codex", "Menu");
RedDotService.RegisterParent("Menu/Settings", "Menu");
```

### 7.3 动态节点清理

场景切换时，动态红点节点在 `_ExitTree` 中清理：

```csharp
// 战斗场景脚本
public override void _ExitTree()
{
    RedDotService.UnregisterNode("Battle");
    RedDotService.UnregisterNode("Battle/Hand");
    // ...
}
```

由于 `UnregisterNode` 是级联的，`UnregisterNode("Battle")` 会一并清理其所有后代。

### 7.4 父子关系重组

```csharp
// 将 "Codex" 从 "Menu" 下摘除
RedDotService.UnregisterParent("Menu/Codex", "Menu");
// Codex 现在为根节点（除非后续挂到其他父节点下）
```

---

## 8. 与现有系统的关系

### 8.1 依赖方向

```
mod ──→ frame/notification/RedDotService
mod ──→ frame/notification/RedDotCmp
```

`RedDotService` 引用 `Src/frame/mvc/EventDispatcher`，符合 frame 层可引用 Godot 与自身基础设施的规则。

### 8.2 事件总线

- mod 内部事件通过 `BaseMod.InternalBus`（`EventDispatcher`）传递
- 跨 mod 全局事件通过 `GlobalEvents.Bus` 传递
- `RegisterNode` 的 `triggers` 参数直接接受 `(EventDispatcher, string)` 元组，不引入新的事件抽象层

### 8.3 与 UI 层级的关系

红点系统**不占用** `EUILayer.Notice` 层级。红点视觉组件直接挂到目标按钮/菜单的子树中，属于局部 UI 装饰，而非独立通知层。若未来需要独立的通知中心（飞行横幅等），再使用 `Notice` 层。

---

## 9. 测试要点

| 场景 | 预期 |
|------|------|
| 注册节点后立即查询 | `IsActive` 返回首评结果 |
| 事件触发且检查函数返回值变化 | 状态更新，冒泡通知父节点 |
| 事件触发但检查函数返回值不变 | 不冒泡 |
| 重复注册同一 id | 旧订阅解除，新检查函数生效 |
| `RegisterParent` 在子节点注册之前调用 | 挂起，子节点注册时自动建立关系 |
| `UnregisterNode` | 级联清理该节点及所有后代，事件订阅解除 |
| `UnregisterParent` | 仅断关系，子节点保留 |
| `WatchRed` 切换 id | 旧订阅解除，新订阅生效，立即同步新状态 |
| `IsActive` 查询未注册 id | 返回 `false` |
| 父节点所有子节点均 `false` | 父节点 `IsActive = false` |
| 任一子节点为 `true` | 父节点 `IsActive = true` |
| 分支节点同时有检查函数和子节点 | Active = checkFunc 结果 OR 子节点聚合结果 |
| 注册聚合节点（`checkFunc = null`，无 triggers） | 成功，仅通过子节点聚合决定状态 |
| 注册即设 `ForceActive` | `IsActive` 返回 `true`，事件触发不改变 |
| 注册即设 `ForceInactive` | `IsActive` 返回 `false`，事件触发不改变 |
| 运行时 `SetOverride` 切为 `ForceActive` | 立即激活，触发冒泡 |
| 运行时 `SetOverride` 切为 `ForceInactive` | 立即灭活，触发冒泡 |
| 重载期间事件触发 | `LastEvaluated` 被更新，但 Active 仍为重载值 |
| `SetOverride` 从 `ForceActive` 切回 `None` | 用 `LastEvaluated` + 子节点聚合重新判定 |
| `SetOverride` 从 `ForceInactive` 切回 `None` 且 `LastEvaluated = true` | Active 变为 `true`，触发冒泡 |
