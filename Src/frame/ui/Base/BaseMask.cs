using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 遮罩基类，管理遮罩的动画和事件。
/// </summary>
/// <remarks>
/// 遮罩不是 <see cref="BaseUI"/> 子类（需继承 <c>Control</c>），因此按 ui-mod-binding 规格 §4.3
/// 自行组合 <see cref="BindingScope"/> 并收敛 <c>_ExitTree</c>，语义与 <see cref="BaseUI"/> 一致。
/// </remarks>
public abstract partial class BaseMask : Control, IUILifecycleInvoker
{
    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑。</summary>
    protected BindingScope Binder { get; } = new();

    public UIVo? UIVo { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    /// <summary>
    /// 取「本遮罩所属功能」的门面。语义与 <c>BaseWin.Facade&lt;T&gt;()</c> 一致（见 ui-mod-binding 规格 §5.4）。
    /// </summary>
    protected TFacade Facade<TFacade>() where TFacade : class
    {
        if (UIVo is null || string.IsNullOrEmpty(UIVo.OwnerModId))
        {
            throw new InvalidOperationException("遮罩尚未绑定 UIVo，无法解析归属门面。");
        }

        var provider = UIVo.Manager.FacadeProvider;
        if (provider is null)
        {
            throw new InvalidOperationException("遮罩取门面失败：组合根未注入 IUiFacadeProvider。");
        }

        var facade = provider.ResolveUiFacade(UIVo.OwnerModId);
        if (facade is null)
        {
            throw new InvalidOperationException($"遮罩取门面失败：功能 '{UIVo.OwnerModId}' 未提供门面。");
        }

        if (facade is not TFacade typed)
        {
            throw new InvalidOperationException(
                $"遮罩取门面类型不匹配：功能 '{UIVo.OwnerModId}' 提供的是 {facade.GetType().Name}，"
                + $"期望 {typeof(TFacade).Name}。");
        }

        return typed;
    }

    public virtual void Init(UIVo uiVo) { }

    /// <summary>
    /// 每次打开都会调用，用于（重新）登记订阅。离场解绑由 <see cref="Binder"/> 负责。
    /// </summary>
    public virtual void InitEvent() { }

    /// <summary>框架唯一离场入口。<b>sealed：子类不得 override</b> — 请改 override <see cref="OnExitTree"/>。</summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();
        Binder.UnbindAll();
        GlobalEvents.Bus.OffCaller(this);
        base._ExitTree();
    }

    /// <summary>框架级离场生命周期：子类只做非订阅类清理。</summary>
    protected virtual void OnExitTree() { }

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
    void IUILifecycleInvoker.InvokeResetBindings() => Binder.UnbindAll();
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