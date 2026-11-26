using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// 一次 <see cref="EventDispatcher.BindDecorated"/> 批量绑定的句柄：
/// <see cref="Off"/> / <see cref="Dispose"/> 取消本次登记的全部监听器，不影响同一 caller 的其他订阅。
/// </summary>
public sealed class EventBinding : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IEventListener> _listeners = [];
    private Action? _offCompleted;
    private bool _active = true;

    internal EventBinding(EventDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
    }

    /// <summary>取消完成后回调（供分发器清理绑定索引）。</summary>
    internal Action? OffCompleted
    {
        get
        {
            lock (_gate)
            {
                return _offCompleted;
            }
        }
        set
        {
            lock (_gate)
            {
                _offCompleted = value;
            }
        }
    }

    /// <summary>本次绑定所属的分发器。</summary>
    public EventDispatcher Dispatcher { get; }

    /// <summary>本次成功登记的监听器数量。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _listeners.Count;
            }
        }
    }

    /// <summary>是否仍处于已登记状态。</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// 本次登记的全部监听器是否仍然激活（用于决定能否复用缓存句柄）。
    /// 已触发的 Once 会让句柄失效，下次 BindDecorated 会重装；部分监听器被移除时也返回 false。
    /// </summary>
    internal bool AllListenersActive
    {
        get
        {
            lock (_gate)
            {
                if (!_active || _listeners.Count == 0)
                {
                    return false;
                }

                foreach (var listener in _listeners)
                {
                    if (!listener.IsActive)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>是否还有任一活跃监听器（用于清理索引时决定是否保留剩余 handler）。</summary>
    internal bool HasActiveListeners
    {
        get
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }

                foreach (var listener in _listeners)
                {
                    if (listener.IsActive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    internal void Add(IEventListener listener)
    {
        lock (_gate)
        {
            if (_active)
            {
                _listeners.Add(listener);
                return;
            }
        }

        // 绑定过程中被并发取消：后到的监听器立即退订，避免漏网。
        listener.Off();
    }

    /// <summary>取消本次登记的全部监听器；可重复调用。</summary>
    public void Off()
    {
        IEventListener[] snapshot;
        Action? offCompleted;
        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            snapshot = [.. _listeners];
            _listeners.Clear();
            offCompleted = _offCompleted;
            _offCompleted = null;
        }

        try
        {
            foreach (var listener in snapshot)
            {
                listener.Off();
            }
        }
        finally
        {
            offCompleted?.Invoke();
        }
    }

    public void Dispose() => Off();
}