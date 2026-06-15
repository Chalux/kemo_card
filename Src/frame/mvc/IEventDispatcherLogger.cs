namespace KemoCard.Frame.Mvc;

/// <summary>
/// 事件分发器错误日志抽象，使 frame 层不依赖 Godot。
/// </summary>
public interface IEventDispatcherLogger
{
    void LogError(string message);
}

/// <summary>
/// 默认空实现；未调用 <see cref="EventDispatcher.Configure"/> 时使用。
/// </summary>
public sealed class NullEventDispatcherLogger : IEventDispatcherLogger
{
    public static NullEventDispatcherLogger Instance { get; } = new();

    public void LogError(string message)
    {
    }
}
