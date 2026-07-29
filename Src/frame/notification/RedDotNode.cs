using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点树节点（内部类型）。持有 id、检查函数、重载策略、父子关系和事件订阅句柄。
/// 仅供 RedDotService 使用。
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
    /// 已注册的事件订阅句柄。用于反注册时批量解除。
    /// 存储为 Action 委托，调用时执行 unsubscribe。
    /// </summary>
    public readonly List<Action> EventSubscriptions = new();

    public RedDotNode(string id, Func<bool>? checkFunc, RedDotOverride @override)
    {
        Id = id;
        CheckFunc = checkFunc;
        Override = @override;
    }
}