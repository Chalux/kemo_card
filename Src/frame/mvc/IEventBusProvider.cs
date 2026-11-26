namespace KemoCard.Frame.Mvc;

/// <summary>
/// 暴露功能内部事件总线，供 UI 框架把 <see cref="EventSubscribeAttribute"/> 标注的方法
/// 绑定到界面所属功能的总线。
/// </summary>
/// <remarks>
/// <see cref="BaseController{TModel}"/> 已显式实现本接口：界面门面（通常是功能 Controller）
/// 天然可被 <c>BindingScope</c> 识别，无需各功能另写适配。<see cref="EventDispatcher"/> 本身
/// 只在显式实现中可见，避免把总线扩散成任意调用方可写的公共 API。
/// </remarks>
public interface IEventBusProvider
{
    /// <summary>该功能的事件总线（即 Mod 的 <see cref="BaseMod.InternalBus"/>）。</summary>
    EventDispatcher EventBus { get; }
}