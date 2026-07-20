# 红点系统实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `Src/frame/notification/` 下实现事件驱动、支持树状聚合和延迟评估的红点通知系统。

**Architecture:** 静态门面 `RedDotService` 管理节点注册、父子关系、事件触发重评和状态分发；`CallDeferred("_FlushAll")` 延迟一帧批处理所有脏节点；`RedDotCmp` 通过 `WatchRed` 订阅状态变更自动驱动显示/隐藏。

**Tech Stack:** Godot 4.6.1 Mono (C#), Godot.Node/Node2D/TextureRect, NUnit 4, EventDispatcher (frame/mvc)

**Spec:** `Doc/superpowers/specs/2026-07-20-red-dot-system-design.md`

## Global Constraints

- 代码仅使用 C#，不使用 GDScript
- frame 层不得依赖 mod 层（`Src/mod/`）
- 所有面向用户的文本内容使用本地化（翻译键）
- 新增枚举/类型使用独立文件
- 测试框架 NUnit 4，测试项目 `Tests/kemo_card.Ui.Tests/`
- 提交信息使用简体中文
- 改动完成后对改动文件执行格式化（`dotnet format`）

---

### Task 1: 基础类型定义

**Files:**
- Create: `Src/frame/notification/RedDotOverride.cs`
- Create: `Src/frame/notification/RedDotNode.cs`
- Modify: `Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj` (如有需要添加 `notification` 命名空间的 usings 配置)

**Interfaces:**
- Produces: `RedDotOverride` enum (`None`, `ForceActive`, `ForceInactive`)，`RedDotNode` internal class (Id, CheckFunc, Override, Active, LastEvaluated, Children, Parent, EventSubscriptions 字段)

- [ ] **Step 1: 创建 `RedDotOverride.cs`**

```csharp
namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点节点的激活策略重载。
/// </summary>
public enum RedDotOverride
{
    /// <summary>不重载，按 checkFunc 或子节点聚合判定</summary>
    None,

    /// <summary>强制激活</summary>
    ForceActive,

    /// <summary>强制不激活</summary>
    ForceInactive,
}
```

- [ ] **Step 2: 创建 `RedDotNode.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点树节点（内部类型）。持有 id、检查函数、重载策略、父子关系和事件订阅句柄。
/// 仅供 <see cref="RedDotService"/> 使用。
/// </summary>
internal sealed class RedDotNode
{
    public string Id;
    public Func<bool>? CheckFunc;
    public RedDotOverride Override;
    public bool Active;
    public bool LastEvaluated;
    public RedDotNode? Parent;
    public readonly List<RedDotNode> Children = new();

    /// <summary>
    /// 已注册的事件订阅句柄。用于反注册时批量 Off()。
    /// 存储为 <c>object</c> 以避开泛型 (EventKey) 带来的类型封闭问题；
    /// 实际存储的是 <see cref="System.IDisposable"/> 或带 Off() 方法可调用对象。
    /// </summary>
    public readonly List<Action> EventSubscriptions = new();

    public RedDotNode(string id, Func<bool>? checkFunc, RedDotOverride @override)
    {
        Id = id;
        CheckFunc = checkFunc;
        Override = @override;
    }
}
```

- [ ] **Step 3: 运行编译验证**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet build --no-restore
```

Expected: Build SUCCEED (no errors). 新增的枚举和类不产生编译错误。

- [ ] **Step 4: 提交**

```bash
git add "Src/frame/notification/RedDotOverride.cs" "Src/frame/notification/RedDotNode.cs"
git commit -m "新建红点系统基础类型：RedDotOverride 枚举与 RedDotNode 树节点"
```

---

### Task 2: `RedDotService` 注册与查询

**Files:**
- Create: `Src/frame/notification/RedDotService.cs`
- Create: `Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs`

**Interfaces:**
- Consumes: `RedDotOverride` enum, `RedDotNode` class
- Produces: `RedDotService.Configure()`, `RedDotService.RegisterNode(id, checkFunc, triggers)`, `RedDotService.RegisterNode(id, override, checkFunc, triggers)`, `RedDotService.RegisterParent(childId, parentId)`, `RedDotService.IsActive(id)` 方法

- [ ] **Step 5: 编写最小测试文件 `RedDotServiceTests.cs`**

```csharp
using KemoCard.Frame.Notification;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class RedDotServiceTests
{
    [SetUp]
    public void SetUp()
    {
        RedDotService.Configure();
    }

    [TearDown]
    public void TearDown()
    {
        // 测试间清理所有已注册节点（通过反射或暴露内部 Reset 方法）
        RedDotService.UnregisterNode("A");
        RedDotService.UnregisterNode("B");
        RedDotService.UnregisterNode("Menu");
        RedDotService.UnregisterNode("Menu/Codex");
        RedDotService.UnregisterNode("Menu/Settings");
    }

    [Test]
    public void RegisterNode_with_checkFunc_evaluates_immediately()
    {
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void RegisterNode_checkFunc_returns_false()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void IsActive_unknown_id_returns_false()
    {
        Assert.That(RedDotService.IsActive("nonexistent"), Is.False);
    }

    [Test]
    public void RegisterParent_after_RegisterNode_propagates_state_upward()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void RegisterParent_before_RegisterNode_works()
    {
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        RedDotService.RegisterNode("Menu/Codex", () => true);
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void Parent_inactive_when_all_children_inactive()
    {
        RedDotService.RegisterNode("Menu/Codex", () => false);
        RedDotService.RegisterNode("Menu/Settings", () => false);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        RedDotService.RegisterParent("Menu/Settings", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.False);
    }

    [Test]
    public void ReRegister_same_id_replaces()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void Aggregation_node_without_checkFunc_works()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }
}
```

- [ ] **Step 6: 运行测试验证失败**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RedDotServiceTests" -v n
```

Expected: FAIL，`RedDotService` 类还不存在。

- [ ] **Step 7: 实现 `RedDotService.cs`（注册 + 查询 + 父节点聚合）**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点系统静态门面。管理节点注册、父子关系树、事件驱动评估和状态分发。
/// </summary>
public static class RedDotService
{
    private static readonly Dictionary<string, RedDotNode> _nodes = new();
    private static readonly HashSet<string> _dirtyIds = new();
    private static bool _initialized;

    /// <summary>
    /// 状态变更事件。(id, active)
    /// </summary>
    public static event Action<string, bool>? OnStateChanged;

    public static void Configure()
    {
        _nodes.Clear();
        _dirtyIds.Clear();
        _pendingParents.Clear();
        _initialized = true;
    }

    public static void RegisterNode(
        string id,
        Func<bool>? checkFunc,
        params (Action subscribe, Action unsubscribe)[] triggers)
    {
        RegisterNodeInternal(id, checkFunc, RedDotOverride.None, triggers);
    }

    public static void RegisterNode(
        string id,
        RedDotOverride @override,
        Func<bool>? checkFunc,
        params (Action subscribe, Action unsubscribe)[] triggers)
    {
        RegisterNodeInternal(id, checkFunc, @override, triggers);
    }

    private static void RegisterNodeInternal(
        string id,
        Func<bool>? checkFunc,
        RedDotOverride @override,
        (Action subscribe, Action unsubscribe)[] triggers)
    {
        if (!_initialized)
        {
            Configure();
        }

        // 重复注册：清理旧的
        if (_nodes.TryGetValue(id, out var existing))
        {
            foreach (var unsub in existing.EventSubscriptions)
            {
                unsub();
            }
            existing.EventSubscriptions.Clear();
        }

        var node = new RedDotNode(id, checkFunc, @override);
        _nodes[id] = node;

        // 恢复已挂起的父子关系
        if (_pendingParents.TryGetValue(id, out var pendingParents))
        {
            foreach (var parentId in pendingParents)
            {
                if (_nodes.TryGetValue(parentId, out var parentNode))
                {
                    node.Parent = parentNode;
                    parentNode.Children.Add(node);
                }
            }
        }

        // 首评（同步）
        if (@override == RedDotOverride.None && checkFunc != null)
        {
            node.LastEvaluated = checkFunc();
            node.Active = node.LastEvaluated || node.Children.Any(c => c.Active);
            // 首评不触发 OnStateChanged，但触发父节点聚合
            if (node.Parent != null)
            {
                MarkDirty(node.Parent);
            }
        }
        else if (@override != RedDotOverride.None)
        {
            node.Active = @override == RedDotOverride.ForceActive;
        }
    }

    public static void RegisterParent(string childId, string parentId)
    {
        if (!_initialized)
        {
            Configure();
        }

        var childExists = _nodes.TryGetValue(childId, out var child);
        var parentExists = _nodes.TryGetValue(parentId, out var parent);

        if (childExists && parentExists)
        {
            if (child!.Parent == parent)
            {
                return; // 已存在，跳过
            }
            // 从旧父节点移除
            child.Parent?.Children.Remove(child);
            child.Parent = parent;
            parent!.Children.Add(child);
            // 父节点需重新聚合
            MarkDirty(parent);
        }
        else
        {
            // 暂存挂起：至少一方尚未注册
            if (!_pendingParents.ContainsKey(childId))
            {
                _pendingParents[childId] = new List<string>();
            }
            if (!_pendingParents[childId].Contains(parentId))
            {
                _pendingParents[childId].Add(parentId);
            }
        }
    }

    public static bool IsActive(string id)
    {
        return _nodes.TryGetValue(id, out var node) && node.Active;
    }

    // 内部：标记节点为脏
    internal static void MarkDirty(RedDotNode node)
    {
        if (_dirtyIds.Add(node.Id))
        {
            // TODO: CallDeferred 将在 Task 4 中接入
        }
    }

    private static readonly Dictionary<string, List<string>> _pendingParents = new();
}
```

- [ ] **Step 8: 运行测试**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RedDotServiceTests" -v n
```

Expected: 8 PASS, 0 FAIL。

- [ ] **Step 9: 格式化并提交**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/frame/notification/RedDotService.cs Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs
```

```bash
git add "Src/frame/notification/RedDotService.cs" "Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs"
git commit -m "实现 RedDotService 注册与查询核心逻辑`n`n- Configure / RegisterNode / RegisterParent / IsActive`n- 支持 RegisterParent 在 RegisterNode 前后任意顺序调用`n- 首评同步执行，父节点聚合通过脏标记延迟处理`n- 8 个核心测试通过"
```

---

### Task 3: `RedDotService` 状态评估 + `_FlushAll`

**Files:**
- Modify: `Src/frame/notification/RedDotService.cs`
- Modify: `Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs`

**Interfaces:**
- Consumes: `RedDotNode`, `_dirtyIds`
- Produces: `_FlushAll()` 内部方法，`OnStateChanged` 触发逻辑，`Refresh(id)` 方法

- [ ] **Step 10: 添加评估逻辑测试**

在 `RedDotServiceTests.cs` 末尾追加：

```csharp
    [Test]
    public void FlushAll_evaluates_dirty_nodes()
    {
        bool checkReturn = false;
        RedDotService.RegisterNode("A", () => checkReturn);
        Assert.That(RedDotService.IsActive("A"), Is.False);

        checkReturn = true;
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll(); // 暴露内部方法供测试

        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void FlushAll_only_evaluates_dirty()
    {
        RedDotService.RegisterNode("A", () => true);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.True);

        bool checkB = false;
        RedDotService.RegisterNode("B", () => checkB);
        Assert.That(RedDotService.IsActive("B"), Is.False);

        checkB = true;
        RedDotService.Refresh("B");
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("B"), Is.True);
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void OnStateChanged_fires_when_state_changes()
    {
        string? changedId = null;
        bool? changedActive = null;
        RedDotService.OnStateChanged += (id, active) =>
        {
            changedId = id;
            changedActive = active;
        };

        RedDotService.RegisterNode("A", () => false);
        var node = RedDotService.InternalGetNode("A");
        node.LastEvaluated = true;
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll();

        Assert.That(changedId, Is.EqualTo("A"));
        Assert.That(changedActive, Is.True);
    }

    [Test]
    public void OnStateChanged_does_not_fire_when_state_unchanged()
    {
        int fireCount = 0;
        RedDotService.OnStateChanged += (_, _) => fireCount++;

        RedDotService.RegisterNode("A", () => false);
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll();

        Assert.That(fireCount, Is.Zero);
    }

    [Test]
    public void Parent_aggregation_via_flush_all()
    {
        RedDotService.RegisterNode("Menu");
        var parent = RedDotService.InternalGetNode("Menu");
        var child = new RedDotNode("Menu/Codex", () => true, RedDotOverride.None);
        child.Active = true;
        child.LastEvaluated = true;
        parent.Children.Add(child);
        child.Parent = parent;

        // 将 child id 也注册到字典
        typeof(RedDotService)
            .GetMethod("InternalAddNodeDirect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, new object[] { child });

        RedDotService.MarkDirty(parent);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }
```

- [ ] **Step 11: 在 `RedDotService.cs` 中实现 `_FlushAll`、`Refresh`、`InternalFlushAll`**

在 `RedDotService.cs` 追加方法：

```csharp
    public static void Refresh(string id)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            // Refresh 重新执行 checkFunc
            if (node.CheckFunc != null)
            {
                node.LastEvaluated = node.CheckFunc();
            }
            MarkDirty(node);
        }
    }

    /// <summary>
    /// 批处理所有脏节点。通常由 CallDeferred 在下一帧调用。
    /// 测试中可直接调用此方法模拟帧结束。
    /// </summary>
    internal static void InternalFlushAll()
    {
        var ids = new List<string>(_dirtyIds);
        _dirtyIds.Clear();

        foreach (var id in ids)
        {
            if (!_nodes.TryGetValue(id, out var node))
            {
                continue;
            }

            var newActive = EvaluateActive(node);

            if (newActive != node.Active)
            {
                node.Active = newActive;
                OnStateChanged?.Invoke(node.Id, newActive);

                if (node.Parent != null)
                {
                    MarkDirty(node.Parent);
                }
            }
        }
    }

    private static bool EvaluateActive(RedDotNode node)
    {
        if (node.Override == RedDotOverride.ForceActive)
        {
            return true;
        }
        if (node.Override == RedDotOverride.ForceInactive)
        {
            return false;
        }
        return (node.CheckFunc != null && node.LastEvaluated)
            || node.Children.Any(c => c.Active);
    }

    /// <summary>
    /// 供测试通过反射获取内部节点。
    /// </summary>
    internal static RedDotNode? InternalGetNode(string id)
    {
        return _nodes.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>
    /// 供测试绕过 RegisterNode 直接添加节点到字典。
    /// </summary>
    internal static void InternalAddNodeDirect(RedDotNode node)
    {
        _nodes[node.Id] = node;
    }
```

- [ ] **Step 12: 运行测试**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RedDotServiceTests" -v n
```

Expected: 13 PASS, 0 FAIL。

- [ ] **Step 13: 格式化并提交**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/frame/notification/RedDotService.cs Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs && git add "Src/frame/notification/RedDotService.cs" "Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs" && git commit -m "实现 RedDotService 延迟评估与 _FlushAll 批处理`n`n- _dirtyIds 集合记录脏节点，_FlushAll 统一评估`n- EvaluateActive 按 Override > checkFunc > 子节点聚合判定`n- OnStateChanged 仅状态实际变化时触发`n- Refresh 重新执行 checkFunc 后标记脏节点`n- 暴露 InternalFlushAll / InternalGetNode 供测试`n- 新增 5 个评估相关测试"
```

---

### Task 4: `RedDotUpdateNode` + `CallDeferred` 集成 + 事件订阅

**Files:**
- Create: `Src/frame/notification/RedDotUpdateNode.cs`
- Modify: `Src/frame/notification/RedDotService.cs`（添加事件订阅逻辑和 CallDeferred 调用）
- Modify: `Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs`

**Interfaces:**
- Consumes: `RedDotService.InternalFlushAll()`, Godot.Node._Ready, `EventDispatcher.On<object>`
- Produces: `RedDotUpdateNode._FlushAll()` 作为 CallDeferred 载体；`RedDotService` 事件触发时自动订阅/重评

- [ ] **Step 14: 创建 `RedDotUpdateNode.cs`**

```csharp
using Godot;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 内部辅助 Node，挂到 SceneTree.Root 上，提供 <c>_FlushAll</c>
/// 方法作为 <c>CallDeferred</c> 的载体，实现延迟一帧批处理。
/// 无需 _Process，Godot 自动对同一方法名的 CallDeferred 去重。
/// </summary>
internal sealed class RedDotUpdateNode : Node
{
    public override void _Ready()
    {
        // 立即从场景树移除自身的视觉存在（这个 Node 只用于 CallDeferred 调度）
        // 但不能 RemoveChild 因为 CallDeferred 需要它在树上。
    }

    public void _FlushAll()
    {
        RedDotService.InternalFlushAll();
    }
}
```

- [ ] **Step 15: 在 `RedDotService` 中接入 `CallDeferred` 和事件订阅**

修改 `RedDotService.cs`：
- `Configure()` 中创建 `RedDotUpdateNode` 挂到 `SceneTree.Root`
- `MarkDirty()` 中调用 `_updateNode.CallDeferred("_FlushAll")`
- `RegisterNodeInternal` 中处理 `triggers` 参数的事件订阅

```csharp
    private static RedDotUpdateNode? _updateNode;

    public static void Configure()
    {
        _nodes.Clear();
        _dirtyIds.Clear();
        _pendingParents.Clear();
        _initialized = true;

        // 创建 UpdateNode 挂到 SceneTree.Root（仅当 Godot 运行时）
        if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
        {
            _updateNode = new RedDotUpdateNode();
            tree.Root.AddChild(_updateNode);
        }
    }
```

修改 `MarkDirty`：

```csharp
    internal static void MarkDirty(RedDotNode node)
    {
        if (_dirtyIds.Add(node.Id))
        {
            _updateNode?.CallDeferred("_FlushAll");
        }
    }
```

修改 `RegisterNodeInternal`，在末尾添加事件订阅逻辑：

```csharp
        // 事件订阅
        // triggers 为 (Action subscribe, Action unsubscribe) 委托对。
        // subscribe 由调用方提供，内部已绑定好事件触发时调用 RedDotService.Nudge(nodeId)。
        // unsubscribe 用于反注册时解除订阅。
        foreach (var (subscribe, unsubscribe) in triggers)
        {
            subscribe();
            node.EventSubscriptions.Add(unsubscribe);
        }
```

在 `RedDotService.cs` 中添加 `Nudge` 内部方法，供外部事件 handler 调用：

```csharp
    /// <summary>
    /// 由外部事件触发时调用。重新执行 checkFunc，若 Override == None 则标记 Dirty。
    /// </summary>
    internal static void Nudge(string id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return;
        }
        if (node.Override != RedDotOverride.None)
        {
            // 重载期间仍更新 LastEvaluated，但不标记 Dirty
            if (node.CheckFunc != null)
            {
                node.LastEvaluated = node.CheckFunc();
            }
            return;
        }
        if (node.CheckFunc != null)
        {
            node.LastEvaluated = node.CheckFunc();
        }
        MarkDirty(node);
    }
```

在 `RedDotService.cs` 末尾添加用法示例注释（不是实际代码）：

```csharp
/// <summary>
/// 使用示例（在 mod 层调用）：
///
/// <code>
/// IEventListener&lt;SomePayload&gt; listener = null!;
/// listener = GlobalMod.InternalBus.On(
///     new EventKey&lt;SomePayload&gt;(eventId),
///     (payload, l) =&gt; RedDotService.Nudge("Menu/Codex"),
///     this
/// );
/// RedDotService.RegisterNode(
///     "Menu/Codex",
///     () =&gt; GlobalModController.Instance.HasNewCodexEntries(),
///     (subscribe: () =&gt; {}, unsubscribe: () =&gt; listener.Off())
/// );
/// </code>
/// </summary>
```

- [ ] **Step 16: 添加事件触发测试**

在 `RedDotServiceTests.cs` 末尾追加：

```csharp
    [Test]
    public void Event_trigger_re_evaluates_node_when_checkFunc_changes()
    {
        bool val = false;
        RedDotService.RegisterNode("B", () => val);
        Assert.That(RedDotService.IsActive("B"), Is.False);

        val = true;
        // 模拟事件触发后的评估
        var node = RedDotService.InternalGetNode("B");
        node!.LastEvaluated = node.CheckFunc!();
        RedDotService.MarkDirty(node);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("B"), Is.True);
    }
```

- [ ] **Step 17: 运行测试**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RedDotServiceTests" -v n
```

Expected: 14 PASS, 0 FAIL。

- [ ] **Step 18: 格式化并提交**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/frame/notification/ RedDotService.cs RedDotUpdateNode.cs Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs && git add "Src/frame/notification/RedDotUpdateNode.cs" "Src/frame/notification/RedDotService.cs" "Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs" && git commit -m "实现 RedDotUpdateNode 与 CallDeferred 集成`n`n- RedDotUpdateNode 挂到 SceneTree.Root 作为 CallDeferred 载体`n- MarkDirty 中 CallDeferred(_FlushAll) 延迟一帧批处理`n- RegisterNode 支持事件订阅：事件触发时重评 checkFunc`n- 重载期间事件仍更新 LastEvaluated 但不标记 Dirty"
```

---

### Task 5: `SetOverride`、`UnregisterNode`、`UnregisterParent`

**Files:**
- Modify: `Src/frame/notification/RedDotService.cs`
- Modify: `Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs`

**Interfaces:**
- Consumes: 现有 RedDotService 方法
- Produces: `SetOverride(id, override)`, `UnregisterNode(id)`, `UnregisterParent(childId, parentId)` + 对应测试

- [ ] **Step 19: 添加 SetOverride / 反注册测试**

在 `RedDotServiceTests.cs` 末尾追加：

```csharp
    [Test]
    public void SetOverride_ForceActive_overrides_checkFunc()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);

        RedDotService.SetOverride("A", RedDotOverride.ForceActive);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void SetOverride_ForceInactive_overrides_checkFunc()
    {
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);

        RedDotService.SetOverride("A", RedDotOverride.ForceInactive);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void SetOverride_back_to_None_uses_LastEvaluated()
    {
        RedDotService.RegisterNode("A", () => true);
        // 强制不激活
        RedDotService.SetOverride("A", RedDotOverride.ForceInactive);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.False);

        // 切回 None：LastEvaluated 为 true
        RedDotService.SetOverride("A", RedDotOverride.None);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void UnregisterNode_removes_node()
    {
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);

        RedDotService.UnregisterNode("A");
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void UnregisterNode_cascades_to_children()
    {
        RedDotService.RegisterNode("A", () => true);
        RedDotService.RegisterNode("A/Sub");
        RedDotService.RegisterParent("A/Sub", "A");

        RedDotService.UnregisterNode("A");

        Assert.That(RedDotService.IsActive("A"), Is.False);
        Assert.That(RedDotService.IsActive("A/Sub"), Is.False);
    }

    [Test]
    public void UnregisterParent_only_disconnects_relationship()
    {
        RedDotService.RegisterNode("A", () => true);
        RedDotService.RegisterNode("A/Sub", () => true);
        RedDotService.RegisterParent("A/Sub", "A");
        Assert.That(RedDotService.IsActive("A"), Is.True);

        RedDotService.UnregisterParent("A/Sub", "A");
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.True); // A 的 checkFunc 仍为 true
    }
```

- [ ] **Step 20: 实现 `SetOverride`、`UnregisterNode`、`UnregisterParent`**

在 `RedDotService.cs` 追加：

```csharp
    public static void SetOverride(string id, RedDotOverride @override)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return;
        }
        if (node.Override == @override)
        {
            return;
        }
        node.Override = @override;
        MarkDirty(node);
    }

    public static void UnregisterNode(string id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return;
        }

        // 级联：先递归移除所有子节点
        foreach (var child in node.Children.ToList())
        {
            UnregisterNode(child.Id);
        }

        // 解除事件订阅
        foreach (var unsub in node.EventSubscriptions)
        {
            unsub();
        }
        node.EventSubscriptions.Clear();

        // 从父节点移除
        node.Parent?.Children.Remove(node);
        if (node.Parent != null)
        {
            MarkDirty(node.Parent);
        }
        node.Parent = null;

        // 从字典移除
        _nodes.Remove(id);
    }

    public static void UnregisterParent(string childId, string parentId)
    {
        if (!_nodes.TryGetValue(childId, out var child))
        {
            return;
        }
        if (!_nodes.TryGetValue(parentId, out var parent))
        {
            return;
        }
        if (child.Parent != parent)
        {
            return;
        }

        parent.Children.Remove(child);
        child.Parent = null;
        MarkDirty(parent);
    }
```

- [ ] **Step 21: 运行测试**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RedDotServiceTests" -v n
```

Expected: 20 PASS, 0 FAIL。

- [ ] **Step 22: 格式化并提交**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/frame/notification/RedDotService.cs Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs && git add "Src/frame/notification/RedDotService.cs" "Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs" && git commit -m "实现 SetOverride 与反注册方法`n`n- SetOverride 运行时修改重载策略，标记 Dirty 延迟生效`n- UnregisterNode 级联清理节点及所有后代`n- UnregisterParent 仅断开父子关系`n- 新增 6 个测试覆盖重载和反注册场景"
```

---

### Task 6: `RedDotCmp` 视觉组件

**Files:**
- Create: `Src/frame/notification/RedDotCmp.cs`

**Interfaces:**
- Consumes: `RedDotService.OnStateChanged`, `RedDotService.IsActive`
- Produces: `RedDotCmp.WatchRed(string redDotId)` 自驱动视觉组件

- [ ] **Step 23: 创建 `RedDotCmp.cs`**

```csharp
using Godot;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点视觉组件。Node2D + TextureRect 子节点，通过 WatchRed 订阅指定红点 ID，
/// 状态变更时自动驱动显示/隐藏。
/// <para>默认不可见（_Ready 中 Visible = false），仅当 WatchRed 订阅的节点激活时显示。</para>
/// </summary>
public partial class RedDotCmp : Node2D
{
    private string? _watchedId;

    public override void _Ready()
    {
        Visible = false;
    }

    /// <summary>
    /// 订阅指定红点节点。切换 id 时自动取消旧订阅并同步新状态。
    /// </summary>
    public void WatchRed(string? redDotId)
    {
        if (_watchedId == redDotId)
        {
            return;
        }

        if (_watchedId != null)
        {
            RedDotService.OnStateChanged -= OnRedStateChanged;
        }

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
        {
            Visible = active;
        }
    }
}
```

- [ ] **Step 24: 编译验证**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet build --no-restore
```

Expected: Build SUCCEED。

- [ ] **Step 25: 提交**

```bash
git add "Src/frame/notification/RedDotCmp.cs" && git commit -m "实现 RedDotCmp 红点视觉组件`n`n- Node2D + TextureRect 组合`n- WatchRed(id) 订阅指定红点，自动同步显示/隐藏`n- 默认不可见，切换 id 时自动清理旧订阅`n- 依赖 RedDotService.OnStateChanged + IsActive"
```

---

### Task 7: `MainRoot` 初始化接入

**Files:**
- Modify: `Src/MainRoot.cs`

**Interfaces:**
- Consumes: `RedDotService.Configure()`
- Produces: 无新接口，仅接入初始化流程

- [ ] **Step 26: 在 `MainRoot._Ready` 中调用 `RedDotService.Configure()`**

读取 `Src/MainRoot.cs` 找到 `_Ready` 方法中 `UIManager` 初始化附近的位置，追加：

```csharp
// 找到 UIManager 初始化代码段附近
// 在其前或后添加：
RedDotService.Configure();
```

- [ ] **Step 27: 编译验证**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet build --no-restore
```

Expected: Build SUCCEED。

- [ ] **Step 28: 运行全部测试**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj -v n
```

Expected: 全部测试通过（包含新的 20 个 RedDotService 测试和已有的 CardSummaryBuilder 测试）。

- [ ] **Step 29: 格式化并提交**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/MainRoot.cs && git add "Src/MainRoot.cs" && git commit -m "MainRoot 启动时初始化红点系统`n`n- _Ready 中调用 RedDotService.Configure()`n- 创建 RedDotUpdateNode 挂到 SceneTree.Root"
```

---

### Task 8: 收尾验证

- [ ] **Step 30: 运行全部测试确认无回归**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj -v n
```

Expected: 全部通过。

- [ ] **Step 31: 格式化所有新增/修改文件**

```bash
cd "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card" && dotnet format Src/frame/notification/ Src/MainRoot.cs Tests/kemo_card.Ui.Tests/RedDotServiceTests.cs
```

- [ ] **Step 32: 最终提交**

```bash
git add -u && git status
git commit -m "红点系统实现完成：格式化和最终收尾"
```
