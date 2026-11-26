using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点系统静态门面。管理节点注册、父子关系树、事件驱动评估和状态分发。
/// </summary>
public static class RedDotService
{
    private static readonly Dictionary<string, RedDotNode> _nodes = new();
    private static readonly HashSet<string> _dirtyIds = new();
    private static readonly Dictionary<string, List<string>> _pendingParents = new();
    private static RedDotUpdateNode? _updateNode;
    private static bool _initialized;
    private static bool _configuring;

    /// <summary>
    /// 红点系统独立的事件总线。状态变化时向自身、祖先（父级）和后代（子级）发布
    /// <see cref="RedDotEvents.Changed"/>；不属于 <see cref="GlobalEvents.Bus"/>，也不属于任何 Mod。
    /// </summary>
    public static EventDispatcher Events { get; } = new();

    /// <summary>
    /// 清理所有注册节点、脏标记和挂起的父子关系。
    /// 不涉及 Godot 原生调用，可在测试环境下安全使用。
    /// </summary>
    /// <remarks>
    /// <b>不清理 <see cref="Events"/>：</b>总线监听由订阅方（<c>BindingScope</c> / <c>EventBinding</c>）
    /// 自己负责解绑；在这里统一 <c>OffAll</c> 会把仍挂在树上的 <see cref="RedDotCmp"/> 静默断开，
    /// 而它不会重新进树、也就不会重新订阅。
    /// </remarks>
    public static void Configure()
    {
        if (_configuring)
        {
            return;
        }

        _configuring = true;
        try
        {
            var nodes = _nodes.Values.ToList();
            var active = nodes.Where(node => node.Active).ToList();

            // 先完成结构性清理，再执行触发器反订阅与 inactive 广播：
            // 回调里若重新 RegisterNode，新节点会进入已经清空的字典，不会被随后清掉。
            _nodes.Clear();
            _dirtyIds.Clear();
            _pendingParents.Clear();

            foreach (var node in nodes)
            {
                RunUnsubscribers(node);
            }

            foreach (var node in active)
            {
                node.Active = false;
            }

            // 触发器的 unsubscribe 可能在广播前用同 id 重新注册：只对仍是当前实例的旧节点发 inactive，
            // 否则会用旧对象的 false 覆盖新对象的真实状态。
            List<RedDotNode>? stillCurrent = null;
            foreach (var node in active)
            {
                // 没有同 id 新节点时才发旧节点的 inactive；已被重注册的 id 由新节点自己广播。
                if (!_nodes.TryGetValue(node.Id, out var current) || ReferenceEquals(current, node))
                {
                    (stillCurrent ??= []).Add(node);
                }
            }
            if (stillCurrent is not null)
            {
                PublishChanges(stillCurrent);
            }

            // 保留仍有效的更新节点：运行期 Configure 后 MarkDirty 仍需要它调度 flush。
            if (_updateNode is not null && !GodotObject.IsInstanceValid(_updateNode))
            {
                _updateNode = null;
            }

            _initialized = true;
        }
        finally
        {
            _configuring = false;
        }
    }

    /// <summary>
    /// 创建 RedDotUpdateNode 并挂到 SceneTree.Root，启用 CallDeferred 批处理。
    /// 仅在 Godot 运行时（非测试环境）调用。
    /// </summary>
    internal static void SetupUpdateNode(SceneTree tree)
    {
        if (tree.Root != null)
        {
            _updateNode = new RedDotUpdateNode();
            // _Ready 期间根节点仍在装载场景子节点，同步 AddChild 会失败，
            // 延迟到帧末再挂载。CallDeferred 不依赖节点已在场景树中，_FlushAll 照常可被排队。
            tree.Root.CallDeferred(Node.MethodName.AddChild, _updateNode);
        }
    }

    /// <summary>
    /// 注册纯聚合节点（无检查函数，仅通过子节点聚合判定）。
    /// </summary>
    public static void RegisterNode(
        string id,
        params (Action subscribe, Action unsubscribe)[] triggers)
    {
        RegisterNodeInternal(id, null, RedDotOverride.None, triggers);
    }

    /// <summary>
    /// 注册叶子节点（默认不重载）。
    /// </summary>
    public static void RegisterNode(
        string id,
        Func<bool>? checkFunc,
        params (Action subscribe, Action unsubscribe)[] triggers)
    {
        RegisterNodeInternal(id, checkFunc, RedDotOverride.None, triggers);
    }

    /// <summary>
    /// 注册节点并指定重载策略。
    /// </summary>
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

        // 重复注册：先解除旧的 EventSubscriptions，并保留旧的父子关系（见下）。
        RedDotNode? previous = null;
        if (_nodes.TryGetValue(id, out var existing))
        {
            previous = existing;
            RunUnsubscribers(existing);
        }

        var node = new RedDotNode(id, checkFunc, @override);

        // 重注册必须继承旧的父子关系：否则旧子节点仍指向被丢弃的旧节点、旧父节点的 Children
        // 里也留着旧节点，向上聚合从此断裂（重注册后红点不再冒泡）。
        if (previous is not null)
        {
            if (previous.Parent is { IsRemoving: true } removingParent)
            {
                // 父节点正在删除：新节点不能被重新挂回幽灵父级。
                removingParent.Children.Remove(previous);
            }
            else if (previous.Parent is not null)
            {
                var siblings = previous.Parent.Children;
                var index = siblings.IndexOf(previous);
                if (index >= 0)
                {
                    siblings[index] = node;
                }
                else
                {
                    siblings.Add(node);
                }

                node.Parent = previous.Parent;
            }

            foreach (var child in previous.Children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            previous.Children.Clear();
            previous.Parent = null;
        }

        _nodes[id] = node;

        // 注册事件触发器
        foreach (var (subscribe, unsubscribe) in triggers)
        {
            subscribe();
            node.EventSubscriptions.Add(unsubscribe);
        }

        // 恢复已挂起的父子关系：只消费已存在的父节点，未解析的保留到父节点注册时再补。
        List<RedDotNode>? detachedParents = null;
        if (_pendingParents.TryGetValue(id, out var pendingParents))
        {
            List<string>? unresolved = null;
            foreach (var parentId in pendingParents)
            {
                if (_nodes.TryGetValue(parentId, out var parentNode))
                {
                    var oldParent = node.Parent;
                    if (TryLink(node, parentNode))
                    {
                        if (oldParent is not null && !ReferenceEquals(oldParent, parentNode))
                        {
                            (detachedParents ??= []).Add(oldParent);
                        }
                    }
                    else
                    {
                        AppLog.Error($"红点挂起父子关系拒绝成环：{id} → {parentId}", "RedDot");
                    }
                }
                else
                {
                    (unresolved ??= []).Add(parentId);
                }
            }

            if (unresolved is null)
            {
                _pendingParents.Remove(id);
            }
            else
            {
                _pendingParents[id] = unresolved;
            }
        }

        // 反向挂起：本节点作为 parentId 时，补上早于它注册的子节点。
        var pendingChildOldParents = ApplyPendingChildren(id);
        if (pendingChildOldParents is not null)
        {
            (detachedParents ??= []).AddRange(pendingChildOldParents);
        }

        var previousActive = previous?.Active ?? false;

        // 首评（同步）：checkFunc 无论当前是否有 Override 都要执行并缓存，
        // 否则 SetOverride(None) 时会用默认 false 把节点永久压成 inactive。
        if (checkFunc != null)
        {
            node.LastEvaluated = checkFunc();
        }

        if (@override != RedDotOverride.None)
        {
            node.Active = @override == RedDotOverride.ForceActive;
        }
        else
        {
            // 纯聚合节点（checkFunc == null）也必须首评：否则继承来的已激活子节点不会体现出来。
            EvaluateActive(node);
        }

        // 同步路径同样要发事件：watch 先于注册 / 注册即激活必须能通知到订阅方。
        var changes = new List<RedDotNode>();
        if (node.Active != previousActive)
        {
            changes.Add(node);
            EvaluateAncestors(node, changes);
        }

        PublishChanges(changes);

        // 被摘掉子节点的旧父级可能从 active 变回 inactive，必须重算。
        if (detachedParents is not null)
        {
            foreach (var parent in detachedParents)
            {
                ReevaluateWithNotifications(parent);
            }
        }
    }

    /// <summary>
    /// 建立父子关系。支持任意注册顺序；自环 / 成环请求会被拒绝。
    /// </summary>
    public static void RegisterParent(string childId, string parentId)
    {
        if (!_initialized)
        {
            Configure();
        }

        if (string.Equals(childId, parentId, StringComparison.Ordinal))
        {
            AppLog.Error($"红点父子关系拒绝自环：{childId}", "RedDot");
            return;
        }

        var childExists = _nodes.TryGetValue(childId, out var child);
        var parentExists = _nodes.TryGetValue(parentId, out var parent);

        if (childExists && parentExists)
        {
            if (child!.Parent == parent)
            {
                // 已有明确父节点：早先挂起的其他关系全部作废，避免后注册时抢回去。
                _pendingParents.Remove(childId);
                return;
            }

            var oldParent = child.Parent;
            if (!TryLink(child, parent!))
            {
                _pendingParents.Remove(childId);
                AppLog.Error($"红点父子关系拒绝成环：{childId} → {parentId}", "RedDot");
                return;
            }

            _pendingParents.Remove(childId);
            if (oldParent is not null && !ReferenceEquals(oldParent, parent))
            {
                ReevaluateWithNotifications(oldParent);
            }
            ReevaluateWithNotifications(parent!);
        }
        else
        {
            // 子节点只能有一个父级：后一次显式请求覆盖之前的挂起请求。
            _pendingParents[childId] = [parentId];
        }
    }

    /// <summary>
    /// 查询节点激活状态。O(1) 字典查询，未注册返回 false。
    /// </summary>
    public static bool IsActive(string id)
    {
        return _nodes.TryGetValue(id, out var node) && node.Active;
    }

    /// <summary>
    /// 运行时修改节点的重载策略。
    /// </summary>
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

    /// <summary>
    /// 级联反注册：递归删除所有后代，解绑事件，从父节点移除，最后从字典移除。
    /// 删除仍处于激活状态的节点会向总线补发一条 <see cref="RedDotChangeScope.Self"/> 的 inactive 通知。
    /// </summary>
    public static void UnregisterNode(string id) =>
        UnregisterNodeCore(id, new HashSet<string>(StringComparer.Ordinal));

    private static void UnregisterNodeCore(string id, HashSet<string> visited)
    {
        if (!visited.Add(id)
            || !_nodes.TryGetValue(id, out var node)
            || node.IsRemoving)
        {
            return; // IsRemoving 同时挡住 unsubscribe / Changed handler 里的同 id 重入
        }

        node.IsRemoving = true;
        try
        {
            ClearPendingFor(id);

            // 子节点在广播前快照：handler 可能重注册同一 id 并替换 Children 槽位。
            var children = node.Children.ToArray();

            // 在拆链前按当前树发通知：自身 inactive + 父级/子级关联，
            // 这样即使父级聚合值不变，订阅父级的 watcher 也能知道后代发生变化。
            if (node.Active)
            {
                node.Active = false;
                PublishChanges([node]);
            }

            foreach (var child in children)
            {
                if (_nodes.TryGetValue(child.Id, out var registered)
                    && ReferenceEquals(registered, child)
                    && ReferenceEquals(child.Parent, node))
                {
                    UnregisterNodeCore(child.Id, visited);
                }
                else if (registered is not null && ReferenceEquals(registered.Parent, node))
                {
                    // handler 重注册了子节点/父节点：旧父链正在删除，新节点保留但必须断开。
                    registered.Parent = null;
                    node.Children.Remove(registered);
                }
            }

            // 先快照并清空再执行：unsubscribe 里再次 UnregisterNode 不会重复消费同一触发器。
            RunUnsubscribers(node);

            var parent = node.Parent;

            // 从父节点移除
            parent?.Children.Remove(node);
            node.Parent = null;
            foreach (var remaining in node.Children)
            {
                if (ReferenceEquals(remaining.Parent, node))
                {
                    remaining.Parent = null;
                }
            }
            node.Children.Clear();

            // 回调里可能已用同一 id 重新注册；只移除本次正在反注册的旧实例。
            if (_nodes.TryGetValue(id, out var current) && ReferenceEquals(current, node))
            {
                _nodes.Remove(id);
            }

            if (parent is not null)
            {
                ReevaluateWithNotifications(parent);
            }
        }
        finally
        {
            node.IsRemoving = false;
        }
    }

    private static void RunUnsubscribers(RedDotNode node)
    {
        var unsubscribers = node.EventSubscriptions.ToArray();
        node.EventSubscriptions.Clear();
        foreach (var unsub in unsubscribers)
        {
            try
            {
                unsub();
            }
            catch (Exception ex)
            {
                AppLog.Error($"红点触发器反订阅失败（已继续清理）：{ex.Message}", "RedDot");
            }
        }
    }

    private static void ClearPendingFor(string id)
    {
        _pendingParents.Remove(id);

        List<string>? emptyChildren = null;
        foreach (var entry in _pendingParents)
        {
            entry.Value.Remove(id);
            if (entry.Value.Count == 0)
            {
                (emptyChildren ??= []).Add(entry.Key);
            }
        }

        if (emptyChildren is not null)
        {
            foreach (var childId in emptyChildren)
            {
                _pendingParents.Remove(childId);
            }
        }
    }

    /// <summary>
    /// 仅断开父子关系，子节点保留。
    /// </summary>
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
        ReevaluateWithNotifications(parent);
    }

    /// <summary>
    /// 将节点标记为脏，暂不实际评估（评估由后续 Task 的 _FlushAll 完成）。
    /// </summary>
    internal static void MarkDirty(RedDotNode node)
    {
        if (_dirtyIds.Add(node.Id))
        {
            _updateNode?.CallDeferred("_FlushAll");
        }
    }

    /// <summary>
    /// 同步评估节点激活状态：有重载优先，否则自身 checkFunc 或任一子节点激活即为激活。
    /// </summary>
    private static void EvaluateActive(RedDotNode node)
    {
        if (node.IsRemoving)
        {
            node.Active = false;
            return;
        }

        if (node.Override == RedDotOverride.ForceActive)
        {
            node.Active = true;
            return;
        }
        if (node.Override == RedDotOverride.ForceInactive)
        {
            node.Active = false;
            return;
        }

        var selfActive = node.CheckFunc != null && node.LastEvaluated;
        var childrenActive = node.Children.Any(c => c.Active);
        node.Active = selfActive || childrenActive;
    }

    /// <summary>
    /// 手动触发重新评估：重新执行 checkFunc 并存 LastEvaluated，标记 Dirty。
    /// </summary>
    public static void Refresh(string id)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            if (node.CheckFunc != null)
            {
                node.LastEvaluated = node.CheckFunc();
            }
            MarkDirty(node);
        }
    }

    /// <summary>
    /// 批处理所有脏节点：重新评估，评估完成后通过 <see cref="Events"/> 通知自身、父级和子级。
    /// </summary>
    /// <remarks>
    /// 同一轮会把父级链一起评估完（而不是每层再排一次 deferred），保证事件里的
    /// <see cref="RedDotChangedPayload.Active"/> 是本轮最终状态；净状态没有变化的节点不发自身事件。
    /// </remarks>
    internal static void InternalFlushAll()
    {
        if (_dirtyIds.Count == 0)
        {
            return;
        }

        var initialStates = new Dictionary<string, bool>();
        var evaluatedOrder = new List<string>();
        var passGuard = Math.Max(1, _nodes.Count * 2 + 1);
        var pass = 0;

        while (_dirtyIds.Count > 0)
        {
            if (++pass > passGuard)
            {
                AppLog.Error($"红点刷新超过 {passGuard} 层，疑似父子关系成环；已丢弃剩余脏标记。", "RedDot");
                _dirtyIds.Clear();
                break;
            }

            var ids = _dirtyIds.ToList();
            _dirtyIds.Clear();

            foreach (var id in ids)
            {
                if (!_nodes.TryGetValue(id, out var node))
                {
                    continue;
                }

                if (initialStates.TryAdd(id, node.Active))
                {
                    evaluatedOrder.Add(id);
                }

                var previous = node.Active;
                EvaluateActive(node);
                if (previous != node.Active && node.Parent is not null)
                {
                    _dirtyIds.Add(node.Parent.Id);
                }
            }
        }

        List<RedDotNode>? changed = null;
        foreach (var id in evaluatedOrder)
        {
            if (_nodes.TryGetValue(id, out var node) && node.Active != initialStates[id])
            {
                (changed ??= []).Add(node);
            }
        }

        if (changed is null)
        {
            return;
        }

        PublishChanges(changed);
    }

    #region 通知与父子关系

    /// <summary>
    /// 先发全部自身变化，再发父级/子级关联通知，保证关联通知读取到的是本轮最终状态。
    /// </summary>
    private static void PublishChanges(List<RedDotNode> changed)
    {
        if (changed.Count == 0)
        {
            return;
        }

        foreach (var node in changed)
        {
            Publish(node, node, RedDotChangeScope.Self);
        }

        foreach (var node in changed)
        {
            PublishRelated(node);
        }
    }

    private static void PublishRelated(RedDotNode node)
    {
        // 子级变化向上通知所有祖先，即使祖先自身状态没有变化。
        var visitedAncestors = new HashSet<string>(StringComparer.Ordinal);
        for (var parent = node.Parent;
             parent is not null && visitedAncestors.Add(parent.Id);
             parent = parent.Parent)
        {
            Publish(parent, node, RedDotChangeScope.Ancestor);
        }

        // 父级变化向下通知所有后代，即使后代自身状态没有变化。
        PublishDescendants(node, node);
    }

    private static void ReevaluateWithNotifications(RedDotNode node)
    {
        var changes = new List<RedDotNode>();
        var previous = node.Active;
        EvaluateActive(node);
        if (previous == node.Active)
        {
            return;
        }

        changes.Add(node);
        EvaluateAncestors(node, changes);
        PublishChanges(changes);
    }

    /// <summary>
    /// 从 <paramref name="node"/> 的父级向上重新聚合，直到某一层状态不再变化或遇到异常父子环。
    /// </summary>
    private static void EvaluateAncestors(RedDotNode node, List<RedDotNode> changes)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { node.Id };
        for (var current = node.Parent; current is not null && visited.Add(current.Id); current = current.Parent)
        {
            var previous = current.Active;
            EvaluateActive(current);
            if (previous == current.Active)
            {
                // 父级没变时，更上层也不可能因本次变化而改变。
                break;
            }

            changes.Add(current);
        }
    }

    /// <summary>
    /// 本节点作为 parentId 时，消费早于它注册的子节点挂起关系。
    /// </summary>
    /// <returns>被摘掉子节点的旧父级（若有），由调用方统一重算。</returns>
    private static List<RedDotNode>? ApplyPendingChildren(string parentId)
    {
        if (!_nodes.TryGetValue(parentId, out var parent))
        {
            return null;
        }

        List<string>? childIds = null;
        foreach (var entry in _pendingParents)
        {
            if (entry.Value.Contains(parentId))
            {
                (childIds ??= []).Add(entry.Key);
            }
        }

        if (childIds is null)
        {
            return null;
        }

        List<RedDotNode>? oldParents = null;
        foreach (var childId in childIds)
        {
            if (!_nodes.TryGetValue(childId, out var child))
            {
                continue;
            }

            var oldParent = child.Parent;
            if (!TryLink(child, parent))
            {
                AppLog.Error($"红点挂起父子关系拒绝成环：{childId} → {parentId}", "RedDot");
                _pendingParents.Remove(childId);
                continue;
            }

            if (_pendingParents.TryGetValue(childId, out var pending))
            {
                pending.Remove(parentId);
                if (pending.Count == 0)
                {
                    _pendingParents.Remove(childId);
                }
            }

            if (oldParent is not null && !ReferenceEquals(oldParent, parent))
            {
                (oldParents ??= []).Add(oldParent);
            }
        }

        return oldParents;
    }

    /// <summary>
    /// 建立父子关系并拒绝自环/成环；成功时返回 true。
    /// </summary>
    private static bool TryLink(RedDotNode child, RedDotNode parent)
    {
        if (child.IsRemoving || parent.IsRemoving)
        {
            return false;
        }

        if (ReferenceEquals(child, parent))
        {
            return false;
        }

        // 从目标父级向上走，若遇到 child 说明 parent 已在 child 的子树里，会成环。
        // visited 防止异常数据已形成父链环时把主线程卡死。
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var ancestor = parent; ancestor is not null && visited.Add(ancestor.Id); ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                return false;
            }
        }

        if (!ReferenceEquals(child.Parent, parent))
        {
            child.Parent?.Children.Remove(child);
            child.Parent = parent;
        }

        if (!parent.Children.Contains(child))
        {
            parent.Children.Add(child);
        }

        return true;
    }

    private static void Publish(RedDotNode target, RedDotNode source, RedDotChangeScope scope)
    {
        Events.Send(RedDotEvents.Changed, new RedDotChangedPayload
        {
            Id = target.Id,
            Active = target.Active,
            SourceId = source.Id,
            Scope = scope,
        });
    }

    private static void PublishDescendants(RedDotNode node, RedDotNode source)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        // 用 id 去重并防护异常父子环；红点树很小，栈分配可忽略。
        var pending = new Stack<RedDotNode>(node.Children);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var child = pending.Pop();
            if (!visited.Add(child.Id))
            {
                continue;
            }

            Publish(child, source, RedDotChangeScope.Descendant);
            foreach (var grandChild in child.Children)
            {
                pending.Push(grandChild);
            }
        }
    }

    #endregion

    /// <summary>
    /// 供外部事件 handler 调用：重新执行 checkFunc，Override==None 时标记 Dirty。
    /// </summary>
    internal static void Nudge(string id)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            if (node.CheckFunc != null)
            {
                node.LastEvaluated = node.CheckFunc();
            }
            if (node.Override == RedDotOverride.None)
            {
                MarkDirty(node);
            }
        }
    }

    /// <summary>
    /// 供测试获取内部节点。
    /// </summary>
    internal static RedDotNode? InternalGetNode(string id)
    {
        _nodes.TryGetValue(id, out var node);
        return node;
    }

    /// <summary>
    /// 供测试绕过 RegisterNode 直接添加节点。
    /// </summary>
    internal static void InternalAddNodeDirect(RedDotNode node)
    {
        _nodes[node.Id] = node;
    }
}