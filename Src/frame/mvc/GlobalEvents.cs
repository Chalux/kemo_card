namespace KemoCard.Frame.Mvc;

/// <summary>
/// 跨功能全局事件总线。功能内部请使用各自 <see cref="BaseMod.InternalBus"/>。
/// </summary>
public static class GlobalEvents
{
    public static EventDispatcher Bus { get; } = new();
}