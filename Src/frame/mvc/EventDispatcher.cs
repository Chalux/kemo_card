using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// 事件句柄：同时携带事件 ID 与载荷类型 TPayload。
/// 等价于 TS 中 IEvtVo 的一条 [evt]: Payload 的映射。
/// </summary>
public readonly struct EventKey<TPayload>(int id)
{
    public int Id { get; init; } = id;
}

/// <summary>
/// 非泛型监听器视图，供 <see cref="EventDispatcher"/> 以统一方式存储、卸载与派发，
/// 避免按 <c>EventListener&lt;object&gt;</c> 强转导致的运行时异常。
/// </summary>
internal interface IEventListener
{
    EventDispatcher Owner { get; }
    object Caller { get; }
    int EventId { get; }
    void Deactivate();
    void InvokeUntyped(object? payload);
}

/// <summary>
/// 对外暴露的监听器只读视图。外部通过此接口访问 Key、Caller、IsActive 及调用 Off() 取消订阅。
/// </summary>
public interface IEventListener<TPayload>
{
    EventKey<TPayload> Key { get; }
    object Caller { get; }
    bool Once { get; }
    bool IsActive { get; }
    void Off();
}

internal sealed class EventListener<TPayload> : IEventListener, IEventListener<TPayload>
{
    public EventDispatcher Owner { get; init; }
    public EventKey<TPayload> Key { get; init; }
    public Action<TPayload, IEventListener<TPayload>> Handler { get; init; }
    public object Caller { get; init; }
    public bool Once { get; init; }
    public bool IsActive { get; internal set; }

    int IEventListener.EventId => Key.Id;

    internal EventListener(EventDispatcher owner, EventKey<TPayload> key, Action<TPayload, IEventListener<TPayload>> handler, object caller, bool once)
    {
        Owner = owner;
        Key = key;
        Handler = handler;
        Caller = caller;
        Once = once;
        IsActive = true;
    }

    internal void Invoke(TPayload payload)
    {
        if (Once)
        {
            if (!Owner.TryClaimOnce(this))
            {
                return;
            }
        }
        else if (!IsActive)
        {
            return;
        }

        try
        {
            Handler.Invoke(payload, this);
        }
        catch (Exception e)
        {
            EventDispatcher.LogError($"Event listener {Key} failed to invoke: {e}");
            if (EventDispatcher.FailureMode == EventDispatchFailureMode.Throw)
            {
                throw;
            }
        }

        if (Once)
        {
            Owner.OffListener(this);
        }
    }

    void IEventListener.Deactivate() => IsActive = false;

    void IEventListener.InvokeUntyped(object? payload)
    {
        if (payload is null)
        {
            Invoke(default!);
            return;
        }
        if (payload is TPayload typed)
        {
            Invoke(typed);
            return;
        }
        EventDispatcher.LogError($"Event payload type mismatch for {Key}: expected {typeof(TPayload)}, got {payload.GetType()}");
    }

    public void Off() => Owner.OffListener(this);
}

/// <summary>
/// 基于事件 CLR 类型的轻量分发器。同一实例可用于功能内部总线；全局总线使用 <see cref="GlobalEvents"/>。
/// 所有公开方法都通过 <see cref="_gate"/> 加锁，<see cref="Send{TPayload}"/> 在锁内快照、锁外派发以避免重入死锁。
/// </summary>
/// <remarks>
/// <para><b>线程模型：</b>设计目标为 Godot 主线程派发；公开 API 通过锁保证互斥安全。
/// <see cref="EventListener{TPayload}.IsActive"/> 非 volatile，不承诺跨线程可见性语义。</para>
/// <para><b>Event id 契约：</b>同一实例内，<c>int id</c> 应对应唯一 <typeparamref name="TPayload"/>。
/// 生成器以枚举整型值作为 id；每 Mod 独立 <see cref="BaseMod.InternalBus"/>，勿将不同载荷类型复用到同一 id。</para>
/// <para><b>Send 分配：</b>单监听器零分配且不经 boxing；多监听器快照数组分配一次，经强类型 <c>Invoke</c> 派发。</para>
/// </remarks>
public sealed class EventDispatcher
{
    private static IEventDispatcherLogger _logger = NullEventDispatcherLogger.Instance;

    public static EventDispatchFailureMode FailureMode { get; set; } = EventDispatchFailureMode.LogAndContinue;

    private readonly object _gate = new();
    private readonly Dictionary<int, List<IEventListener>> _handlers = [];
    private readonly Dictionary<object, Dictionary<int, List<IEventListener>>> _callerMap = [];

    /// <summary>
    /// 配置全局错误日志实现；应在应用启动时调用一次。
    /// </summary>
    public static void Configure(IEventDispatcherLogger logger)
    {
        _logger = logger ?? NullEventDispatcherLogger.Instance;
    }

    internal static void LogError(string message) => _logger.LogError(message);

    /// <summary>
    /// 注册监听器。<paramref name="caller"/> 为 <c>null</c> 时归一化为 <see cref="EventConst.NoneCaller"/>。
    /// 同一 key + handler + caller 已存在活跃 listener 时返回已有实例（不升级 Once 标志）。
    /// </summary>
    public IEventListener<TPayload> On<TPayload>(
        EventKey<TPayload> key,
        Action<TPayload, IEventListener<TPayload>> handler,
        object? caller,
        bool once = false)
    {
        ArgumentNullException.ThrowIfNull(handler);
        caller ??= EventConst.NoneCaller;

        lock (_gate)
        {
            EnsurePayloadTypeContractLocked(key.Id, typeof(TPayload));

            if (TryFindActiveListener(key.Id, handler, caller, out EventListener<TPayload>? existing))
            {
                if (existing.Once != once)
                {
                    LogError(
                        $"Event listener duplicate registration with mismatched once flag for {key}: " +
                        $"existing Once={existing.Once}, requested Once={once}");
                }
                return existing;
            }

            var listener = new EventListener<TPayload>(this, key, handler, caller, once);

            if (!_handlers.TryGetValue(key.Id, out var handlers))
            {
                _handlers[key.Id] = handlers = [];
            }
            handlers.Add(listener);

            if (!_callerMap.TryGetValue(caller, out var callerHandlers))
            {
                _callerMap[caller] = callerHandlers = [];
            }
            if (!callerHandlers.TryGetValue(key.Id, out var callerList))
            {
                callerHandlers[key.Id] = callerList = [];
            }
            callerList.Add(listener);

            return listener;
        }
    }

    public void Once<TPayload>(
        EventKey<TPayload> key,
        Action<TPayload, IEventListener<TPayload>> handler,
        object? caller)
        => On(key, handler, caller, true);

    public void Send<TPayload>(EventKey<TPayload> key, TPayload data)
    {
        IEventListener[]? snapshot = null;
        EventListener<TPayload>? singleListener = null;

        lock (_gate)
        {
            if (!_handlers.TryGetValue(key.Id, out var handlers) || handlers.Count == 0)
            {
                return;
            }

            if (handlers.Count == 1 && handlers[0] is EventListener<TPayload> single)
            {
                singleListener = single;
            }
            else
            {
                snapshot = [.. handlers];
            }
        }

        if (singleListener != null)
        {
            singleListener.Invoke(data);
            return;
        }

        for (var i = 0; i < snapshot!.Length; i++)
        {
            if (snapshot[i] is EventListener<TPayload> typed)
            {
                typed.Invoke(data);
            }
            else
            {
                snapshot[i].InvokeUntyped(data);
            }
        }
    }

    /// <summary>
    /// 检查是否存在匹配的 listener。
    /// <paramref name="caller"/> 为 <c>null</c> 表示不按 caller 过滤；
    /// 要匹配 <see cref="EventConst.NoneCaller"/> 注册的 listener，需显式传入 <see cref="EventConst.NoneCaller"/>。
    /// </summary>
    public bool Has<TPayload>(
        EventKey<TPayload> key,
        Action<TPayload, IEventListener<TPayload>>? handler = null,
        object? caller = null)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(key.Id, out var handlers) || handlers.Count == 0)
            {
                return false;
            }

            if (handler == null && caller == null)
            {
                foreach (var obj in handlers)
                {
                    if (obj is EventListener<TPayload> e && e.IsActive)
                    {
                        return true;
                    }
                }
                return false;
            }

            foreach (var obj in handlers)
            {
                if (obj is not EventListener<TPayload> e)
                {
                    continue;
                }
                if (!e.IsActive)
                {
                    continue;
                }
                if (handler != null && !ReferenceEquals(handler, e.Handler))
                {
                    continue;
                }
                if (caller != null && !ReferenceEquals(caller, e.Caller))
                {
                    continue;
                }
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 移除 listener，语义与 <see cref="Has"/> 对齐：
    /// 无 handler/caller 时移除该 key 下全部 listener；
    /// 仅 handler 时移除所有 caller 上匹配的 handler；
    /// 指定 caller 时仅在该 caller 范围内移除。
    /// </summary>
    public void Off<TPayload>(
        EventKey<TPayload> key,
        Action<TPayload, IEventListener<TPayload>>? handler = null,
        object? caller = null)
    {
        lock (_gate)
        {
            if (handler == null && caller == null)
            {
                OffIdCore(key.Id);
                return;
            }

            if (caller == null)
            {
                if (!_handlers.TryGetValue(key.Id, out var handlers) || handlers.Count == 0)
                {
                    return;
                }

                foreach (var obj in handlers.ToArray())
                {
                    if (obj is not EventListener<TPayload> e)
                    {
                        continue;
                    }
                    if (handler != null && !ReferenceEquals(handler, e.Handler))
                    {
                        continue;
                    }
                    RemoveListenerInternal(e, key.Id);
                }
                return;
            }

            if (!_callerMap.TryGetValue(caller, out var callerHandlers) || callerHandlers.Count == 0)
            {
                return;
            }
            if (!callerHandlers.TryGetValue(key.Id, out var callerList) || callerList.Count == 0)
            {
                return;
            }

            foreach (var obj in callerList.ToArray())
            {
                if (obj is not EventListener<TPayload> e)
                {
                    continue;
                }
                if (handler != null && !ReferenceEquals(e.Handler, handler))
                {
                    continue;
                }
                RemoveListenerInternal(e, key.Id);
            }
        }
    }

    public void OffId(int id)
    {
        lock (_gate)
        {
            OffIdCore(id);
        }
    }

    public void OffCaller(object? caller)
    {
        caller ??= EventConst.NoneCaller;
        lock (_gate)
        {
            if (!_callerMap.TryGetValue(caller, out var callerHandlers) || callerHandlers.Count == 0)
            {
                return;
            }
            foreach (var entry in callerHandlers)
            {
                var id = entry.Key;
                foreach (var listener in entry.Value.ToArray())
                {
                    listener.Deactivate();
                    if (_handlers.TryGetValue(id, out var handlers) && handlers.Count > 0)
                    {
                        handlers.Remove(listener);
                        if (handlers.Count == 0)
                        {
                            _handlers.Remove(id);
                        }
                    }
                }
            }
            _callerMap.Remove(caller);
        }
    }

    internal void OffListener<TPayload>(EventListener<TPayload> listener)
    {
        if (listener.Owner != this)
        {
            return;
        }
        lock (_gate)
        {
            RemoveListenerInternal(listener, listener.Key.Id);
        }
    }

    public void OffAll()
    {
        lock (_gate)
        {
            foreach (var handlers in _handlers.Values)
            {
                foreach (var listener in handlers)
                {
                    listener.Deactivate();
                }
            }
            _handlers.Clear();
            _callerMap.Clear();
        }
    }

    #region private methods
    private void EnsurePayloadTypeContractLocked(int eventId, Type payloadType)
    {
        if (!_handlers.TryGetValue(eventId, out var handlers) || handlers.Count == 0)
        {
            return;
        }

        foreach (var obj in handlers)
        {
            var listenerType = obj.GetType();
            if (!listenerType.IsGenericType || listenerType.GetGenericTypeDefinition() != typeof(EventListener<>))
            {
                continue;
            }

            var registeredPayload = listenerType.GetGenericArguments()[0];
            if (registeredPayload == payloadType)
            {
                continue;
            }

            var isActiveProperty = listenerType.GetProperty(nameof(EventListener<object>.IsActive));
            if (isActiveProperty?.GetValue(obj) is not true)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Event id {eventId} is already bound to payload type {registeredPayload.Name}, cannot register {payloadType.Name}.");
        }
    }

    internal bool TryClaimOnce<TPayload>(EventListener<TPayload> listener)
    {
        lock (_gate)
        {
            if (!listener.IsActive)
            {
                return false;
            }
            listener.IsActive = false;
            return true;
        }
    }

    private bool TryFindActiveListener<TPayload>(
        int id,
        Action<TPayload, IEventListener<TPayload>> handler,
        object caller,
        out EventListener<TPayload> existing)
    {
        existing = null!;
        if (!_handlers.TryGetValue(id, out var handlers) || handlers.Count == 0)
        {
            return false;
        }

        foreach (var obj in handlers)
        {
            if (obj is not EventListener<TPayload> e)
            {
                continue;
            }
            if (!e.IsActive)
            {
                continue;
            }
            if (!ReferenceEquals(e.Handler, handler))
            {
                continue;
            }
            if (!ReferenceEquals(e.Caller, caller))
            {
                continue;
            }
            existing = e;
            return true;
        }

        return false;
    }

    private void OffIdCore(int id)
    {
        if (!_handlers.TryGetValue(id, out var handlers) || handlers.Count == 0)
        {
            return;
        }
        foreach (var listener in handlers.ToArray())
        {
            listener.Deactivate();
            CleanCallerMap(listener.Caller, id, listener);
        }
        handlers.Clear();
        _handlers.Remove(id);
    }

    private void RemoveListenerInternal(IEventListener listener, int id)
    {
        listener.Deactivate();

        if (_handlers.TryGetValue(id, out var handlers) && handlers.Count > 0)
        {
            handlers.Remove(listener);
            if (handlers.Count == 0)
            {
                _handlers.Remove(id);
            }
        }
        CleanCallerMap(listener.Caller, id, listener);
    }

    private void CleanCallerMap(object caller, int id, IEventListener listener)
    {
        caller ??= EventConst.NoneCaller;
        if (!_callerMap.TryGetValue(caller, out var callerHandlers) || callerHandlers.Count == 0)
        {
            return;
        }
        if (callerHandlers.TryGetValue(id, out var callerList) && callerList.Count > 0)
        {
            callerList.Remove(listener);
            if (callerList.Count == 0)
            {
                callerHandlers.Remove(id);
            }
        }
        if (callerHandlers.Count == 0)
        {
            _callerMap.Remove(caller);
        }
    }
    #endregion
}

public static class EventConst
{
    public static readonly object NoneCaller = new();
}