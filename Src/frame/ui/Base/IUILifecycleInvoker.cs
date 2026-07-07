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
