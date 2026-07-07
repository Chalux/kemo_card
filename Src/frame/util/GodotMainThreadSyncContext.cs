using System.Collections.Concurrent;

namespace KemoCard.Frame.Util;

/// <summary>
/// Godot 主线程 SynchronizationContext。
/// 安装后，所有 async/await 续体将通过 _Process 泵送回主线程执行，
/// 避免跨线程操作场景树。
/// </summary>
public sealed class GodotMainThreadSyncContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> _queue = new();
    private readonly SynchronizationContext? _previous;

    public GodotMainThreadSyncContext()
    {
        _previous = Current;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Current == this)
        {
            d(state);
        }
        else
        {
            using var mre = new ManualResetEventSlim(false);
            Post(s =>
            {
                try
                {
                    d(s);
                }
                finally
                {
                    mre.Set();
                }
            }, state);
            mre.Wait();
        }
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }

    /// <summary>
    /// 安装此上下文为当前线程的 SynchronizationContext。
    /// </summary>
    public void Install()
    {
        SetSynchronizationContext(this);
    }

    /// <summary>
    /// 卸载并恢复先前的 SynchronizationContext。
    /// </summary>
    public void Uninstall()
    {
        if (Current == this)
        {
            SetSynchronizationContext(_previous);
        }
    }

    /// <summary>
    /// 从主线程调用，泵送所有排队的回调。
    /// 每次调用最多处理 maxBatch 个回调，防止单帧卡死。
    /// </summary>
    public void Pump(int maxBatch = 32)
    {
        for (int i = 0; i < maxBatch; i++)
        {
            if (!_queue.TryDequeue(out var item))
            {
                break;
            }

            try
            {
                item.callback(item.state);
            }
            catch (Exception e)
            {
                Godot.GD.PushError($"SynchronizationContext 回调异常: {e}");
            }
        }
    }
}