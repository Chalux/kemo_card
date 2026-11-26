using KemoCard.Frame.Mvc;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点系统的事件枚举。成员上的 <see cref="EventPayloadAttribute"/> 同时供
/// <see cref="EventSubscribeAttribute"/> 反射解析 id 与载荷类型。
/// </summary>
public enum ERedDotEvent
{
    [EventPayload(typeof(RedDotChangedPayload))]
    Changed,
}

/// <summary>
/// 通知目标与真实变化节点之间的关系。
/// </summary>
public enum RedDotChangeScope
{
    /// <summary>通知目标自身状态发生变化。</summary>
    Self,

    /// <summary>通知目标是变化节点的祖先（子级变化向上通知父级）。</summary>
    Ancestor,

    /// <summary>通知目标是变化节点的后代（父级变化向下通知子级）。</summary>
    Descendant,
}

/// <summary>
/// 红点状态变更通知。
/// </summary>
/// <remarks>
/// <see cref="Id"/> / <see cref="Active"/> 是<b>收到通知的节点</b>及其当前状态；
/// <see cref="SourceId"/> 是真正发生状态变化的节点。父级/子级通知里目标自身状态可能没有变化，
/// 订阅方应按 <see cref="Id"/> 过滤，并按需结合 <see cref="Scope"/> 区分通知来源。
/// </remarks>
public readonly struct RedDotChangedPayload
{
    /// <summary>收到通知的节点 id（自身 / 祖先 / 后代）。</summary>
    public string Id { get; init; }

    /// <summary>收到通知的节点当前是否激活。</summary>
    public bool Active { get; init; }

    /// <summary>实际发生状态变化的节点 id；自身变化时与 <see cref="Id"/> 相同。</summary>
    public string SourceId { get; init; }

    /// <summary>通知目标与变化节点的关系。</summary>
    public RedDotChangeScope Scope { get; init; }
}

/// <summary>
/// 红点系统的事件键。独立于 <see cref="GlobalEvents.Bus"/> 与各 Mod 的
/// <see cref="BaseMod.InternalBus"/>，挂在 <see cref="RedDotService.Events"/> 上。
/// </summary>
public static class RedDotEvents
{
    /// <summary>红点状态发生变化（自身变化 + 父级/子级关联通知）。</summary>
    public static readonly EventKey<RedDotChangedPayload> Changed = new((int)ERedDotEvent.Changed);
}