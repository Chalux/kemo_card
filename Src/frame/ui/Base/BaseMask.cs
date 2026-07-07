using Godot;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 遮罩基类，管理遮罩的动画和事件。
/// </summary>
public abstract partial class BaseMask : Control, IUILifecycleInvoker
{
    public UIVo? UIVo { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    public virtual void Init(UIVo uiVo) { }
    public virtual void InitEvent() { }

    protected virtual void OnOpen() { }
    protected virtual void OnUIOpen() { }
    protected virtual Action? OnOpenAnim(Action done) { done(); return null; }
    protected virtual void OnOpenAnimDone() { }
    protected virtual void OnClose() { }
    protected virtual void OnUIClose() { }
    protected virtual Action? OnCloseAnim(Action done) { done(); return null; }
    protected virtual void OnUIDestroy() { }

    #region IUILifecycleInvoker 显式实现
    void IUILifecycleInvoker.InvokePreLoad(Action done, Action fail) => done();
    void IUILifecycleInvoker.InvokeCreate() { }
    void IUILifecycleInvoker.InvokeInitEvent() => InitEvent();
    void IUILifecycleInvoker.InvokeOpen() => OnOpen();
    Action? IUILifecycleInvoker.InvokeOpenAnim(Action done) => OnOpenAnim(done);
    Action? IUILifecycleInvoker.InvokeCloseAnim(Action done) => OnCloseAnim(done);
    void IUILifecycleInvoker.InvokeClose() => OnClose();
    void IUILifecycleInvoker.InvokeLayerVisibleUpdate() { }
    void IUILifecycleInvoker.InvokeOpenAnimDone() => OnOpenAnimDone();
    void IUILifecycleInvoker.InvokeMaskOpen() => OnOpen();
    void IUILifecycleInvoker.InvokeMaskUIOpen() => OnUIOpen();
    void IUILifecycleInvoker.InvokeMaskOpenAnimDone() => OnOpenAnimDone();
    void IUILifecycleInvoker.InvokeMaskClose() => OnClose();
    void IUILifecycleInvoker.InvokeMaskUIClose() => OnUIClose();
    void IUILifecycleInvoker.InvokeMaskUIDestroy() => OnUIDestroy();
    Action? IUILifecycleInvoker.InvokeMaskOpenAnim(Action done) => OnOpenAnim(done);
    Action? IUILifecycleInvoker.InvokeMaskCloseAnim(Action done) => OnCloseAnim(done);
    #endregion
}
