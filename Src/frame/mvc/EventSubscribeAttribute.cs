using System;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// 声明式事件订阅：标记的方法由 <see cref="EventDispatcher.BindDecorated"/> 扫描并注册。
/// </summary>
/// <remarks>
/// <para>参数是事件枚举成员，例如 <c>[EventSubscribe(ERunEvent.RunPhaseChanged)]</c>。
/// 事件 id 取枚举整型值，载荷类型取该成员上的 <see cref="EventPayloadAttribute"/>——
/// 与 <c>[EventTable]</c> 源生成器使用同一份契约，不另建映射。</para>
/// <para>方法签名支持：<c>void M()</c>、<c>void M(TPayload)</c>、
/// <c>void M(IEventListener&lt;TPayload&gt;)</c>、<c>void M(TPayload, IEventListener&lt;TPayload&gt;)</c>；
/// 返回类型必须为 <c>void</c>。绑定失败只记日志并跳过该方法，不影响其余订阅。</para>
/// <para>只扫描实例方法（<c>private</c> / <c>protected</c> / <c>public</c> 均可）；静态方法不参与绑定。
/// 基类的私有方法不会被派生类型的扫描发现，请把标注声明在实际使用的类型上。</para>
/// <para>UI 家族（<see cref="KemoCard.Frame.UI.Base.BaseUI"/> / <see cref="KemoCard.Frame.UI.Base.BaseMask"/>）
/// 在每次 <c>InitEvent</c> 后由框架自动扫描：<see cref="EventBusScope.Owner"/> 绑定到归属功能总线，
/// <see cref="EventBusScope.Global"/> 绑定到 <see cref="GlobalEvents.Bus"/>；都登记进
/// <see cref="KemoCard.Frame.UI.BindingScope"/>，离场时统一解绑。</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class EventSubscribeAttribute : Attribute
{
    /// <param name="eventValue">事件枚举成员（装箱值）；其运行期类型即事件枚举类型。</param>
    public EventSubscribeAttribute(object eventValue)
    {
        EventValue = eventValue ?? throw new ArgumentNullException(nameof(eventValue));
    }

    /// <summary>事件枚举成员（装箱值）。</summary>
    public object EventValue { get; }

    /// <summary>是否只触发一次后自动取消订阅。</summary>
    public bool Once { get; init; }

    /// <summary>
    /// 目标总线作用域。默认 <see cref="EventBusScope.Owner"/>（归属功能总线）；
    /// 跨功能公共事件标为 <see cref="EventBusScope.Global"/>，自动绑定会投递到 <see cref="GlobalEvents.Bus"/>。
    /// </summary>
    public EventBusScope Bus { get; init; } = EventBusScope.Owner;
}

/// <summary>装饰器事件的目标总线作用域。</summary>
public enum EventBusScope
{
    /// <summary>界面所属功能 Mod 的内部总线。</summary>
    Owner,

    /// <summary>跨功能全局总线 <see cref="GlobalEvents.Bus"/>。</summary>
    Global,
}