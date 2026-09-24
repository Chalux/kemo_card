using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// UI 生命周期调用接口。
/// BaseWin 和 BaseMask 显式实现此接口，确保生命周期方法仅对框架（状态处理器）可见。
/// </summary>
public interface IUILifecycleInvoker
{
    void InvokePreLoad(Action done, Action fail);
    void InvokeCreate();

    /// <summary>
    /// 清空上一轮的订阅账本。<b>框架在每次 <see cref="InvokeInitEvent"/> 之前调用</b>。
    /// </summary>
    /// <remarks>
    /// 订阅的常规解绑时机是节点离场（<c>_ExitTree</c> → <c>BindingScope.UnbindAll</c>），
    /// 但「界面已经处于打开状态时再次 Open」不经过离场，旧订阅仍然活着。
    /// 那时直接 <see cref="InvokeInitEvent"/> 会重复订阅：Godot 报
    /// <c>Signal 'pressed' is already connected</c>，账本里还会多出永远解不掉的条目
    /// （进程退出时再报一批 <c>Attempt to disconnect a nonexistent connection</c>）。
    /// 先解绑再登记，使 <see cref="InvokeInitEvent"/> 成为幂等操作。
    /// </remarks>
    void InvokeResetBindings();

    void InvokeInitEvent();
    void InvokeOpen();
    void InvokeOpenAnimDone();
    void InvokeClose();
    void InvokeLayerVisibleUpdate();
    Action? InvokeOpenAnim(Action done);
    Action? InvokeCloseAnim(Action done);

    void InvokeMaskOpen();
    void InvokeMaskUIOpen();
    void InvokeMaskOpenAnimDone();
    void InvokeMaskClose();
    void InvokeMaskUIClose();
    void InvokeMaskUIDestroy();
    Action? InvokeMaskOpenAnim(Action done);
    Action? InvokeMaskCloseAnim(Action done);

    EUIAnimState AnimState { get; set; }
}