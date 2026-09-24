using KemoCard.Frame.Logging;
using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// 表现队列调度（Run 规格 §14.4）：把模拟器 <c>Drain()</c> 出来的事件按顺序交给
/// <see cref="ICombatEventPlayer"/> 逐条播放；播放中可继续追加（追加到尾），全部播完后触发 <see cref="Drained"/>。
/// 纯 C#，不引用 Godot，便于单测。
/// </summary>
public sealed class CombatPresentationDirector
{
    private readonly Queue<CombatPresentationEvent> _queue = new();

    /// <summary>是否正在播放（<see cref="PlayAsync"/> 尚未返回）。</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>尚未播放的事件数。</summary>
    public int PendingCount => _queue.Count;

    /// <summary>队列排空、播放循环退出时触发（每次 <see cref="PlayAsync"/> 结束一次）。</summary>
    public event Action? Drained;

    public void Enqueue(IEnumerable<CombatPresentationEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var presentationEvent in events)
            _queue.Enqueue(presentationEvent);
    }

    /// <summary>
    /// 顺序播放直到队列为空。已在播放时直接返回（新事件已被 <see cref="Enqueue"/> 追加到当前循环的尾部）。
    /// 播放器抛出的异常会被吞掉（记 <c>AppLog</c> 警告）并继续下一条：一个动画出错不能让整场战斗卡死。
    /// </summary>
    public async Task PlayAsync(ICombatEventPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (IsPlaying)
            return;

        IsPlaying = true;
        try
        {
            while (_queue.Count > 0)
            {
                var presentationEvent = _queue.Dequeue();
                try
                {
                    // 不加 ConfigureAwait(false)：Godot 的动画 await 必须回到主线程继续操作节点。
                    await player.PlayAsync(presentationEvent);
                }
                catch (Exception ex)
                {
                    // 动画失败不影响逻辑：界面播完后以 SyncFromState 对账。但必须留痕：
                    // 静默吞掉会让"某个动作从没播过"这种问题完全无从排查。
                    AppLog.Warning(
                        $"战斗表现播放失败（{presentationEvent.GetType().Name}）：{ex.Message}",
                        "CombatPresentation");
                }
            }
        }
        finally
        {
            IsPlaying = false;
            Drained?.Invoke();
        }
    }

    /// <summary>丢弃全部待播放事件（界面关闭时调用）。</summary>
    public void Clear() => _queue.Clear();
}