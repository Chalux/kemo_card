using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

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

    /// <summary>
    /// 状态变更事件。(id, active)
    /// </summary>
    public static event Action<string, bool>? OnStateChanged;

    /// <summary>
    /// 清理所有注册节点、脏标记和挂起的父子关系。
    /// 不涉及 Godot 原生调用，可在测试环境下安全使用。
    /// </summary>
    public static void Configure()
    {
        _nodes.Clear();
        _dirtyIds.Clear();
        _pendingParents.Clear();
        _updateNode = null;
        _initialized = true;
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
            tree.Root.AddChild(_updateNode);
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

        // 重复注册：清理旧的 EventSubscriptions
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

        // 注册事件触发器
        foreach (var (subscribe, unsubscribe) in triggers)
        {
            subscribe();
            node.EventSubscriptions.Add(unsubscribe);
        }

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
            _pendingParents.Remove(id);
        }

        // 首评（同步）
        if (@override != RedDotOverride.None)
        {
            node.Active = @override == RedDotOverride.ForceActive;
        }
        else if (checkFunc != null)
        {
            node.LastEvaluated = checkFunc();
            node.Active = node.LastEvaluated || node.Children.Any(c => c.Active);
        }

        // 首评后若有 Parent → 触发父节点重新聚合
        if (node.Parent != null)
        {
            EvaluateActive(node.Parent);
        }
    }

    /// <summary>
    /// 建立父子关系。支持任意注册顺序。
    /// </summary>
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
                return; // 已存在相同关系，跳过
            }
            // 从旧父节点移除
            child.Parent?.Children.Remove(child);
            child.Parent = parent;
            parent!.Children.Add(child);
            // 父节点需重新聚合
            EvaluateActive(parent);
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
    /// </summary>
    public static void UnregisterNode(string id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return;
        }

        // 递归移除所有子节点
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
        node.Children.Clear();

        _nodes.Remove(id);
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
        MarkDirty(parent);
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
    /// 批处理所有脏节点：重新评估并触发 OnStateChanged，父节点传播脏标记。
    /// </summary>
    internal static void InternalFlushAll()
    {
        var ids = _dirtyIds.ToList();
        _dirtyIds.Clear();
        foreach (var id in ids)
        {
            if (_nodes.TryGetValue(id, out var node))
            {
                var oldActive = node.Active;
                EvaluateActive(node);
                if (oldActive != node.Active)
                {
                    OnStateChanged?.Invoke(node.Id, node.Active);
                    if (node.Parent != null)
                    {
                        MarkDirty(node.Parent);
                    }
                }
            }
        }
    }

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
