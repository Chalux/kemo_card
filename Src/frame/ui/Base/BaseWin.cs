using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

public abstract partial class BaseWin : BaseUI, IUILifecycleInvoker
{
    public UIVo? UIVo { get; set; }
    public object? Payload { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    public override EUIType UIType => EUIType.Win;

    /// <summary>
    /// 取「本界面所属功能」的门面（通常是该功能的 Controller）。
    /// </summary>
    /// <remarks>
    /// 界面必须通过这里取数，不得直接访问 <c>AppRoot.Services</c> 跨功能取数
    /// （见 ui-mod-binding 规格 §5.4）。类型不符时明确抛错，而不是静默拿到别的功能的对象。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未绑定 UIVo / 组合根未注入提供者 / 功能无门面 / 类型不匹配。</exception>
    protected TFacade Facade<TFacade>() where TFacade : class
    {
        if (UIVo is null || string.IsNullOrEmpty(UIVo.OwnerModId))
        {
            throw new InvalidOperationException($"界面<{UIId}> 尚未绑定 UIVo，无法解析归属门面。");
        }

        var provider = UIVo.Manager.FacadeProvider;
        if (provider is null)
        {
            throw new InvalidOperationException($"界面<{UIId}> 取门面失败：组合根未注入 IUiFacadeProvider。");
        }

        var facade = provider.ResolveUiFacade(UIVo.OwnerModId);
        if (facade is null)
        {
            throw new InvalidOperationException(
                $"界面<{UIId}> 取门面失败：功能 '{UIVo.OwnerModId}' 未提供门面。");
        }

        if (facade is not TFacade typed)
        {
            throw new InvalidOperationException(
                $"界面<{UIId}> 取门面类型不匹配：功能 '{UIVo.OwnerModId}' 提供的是 "
                + $"{facade.GetType().Name}，期望 {typeof(TFacade).Name}；界面只能访问自己归属功能的门面。");
        }

        return typed;
    }

    /// <summary>
    /// 以强类型读取 Payload。不使用泛型 Godot 子类，避免 ScriptManagerBridge 热重载重复注册。
    /// </summary>
    protected TPayload GetTypedPayload<TPayload>()
    {
        if (Payload is TPayload typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"UI<{UIId}> 的 Payload 类型与期望不匹配，期望 {typeof(TPayload).Name}，实际 {Payload?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// 以强类型写入 Payload。
    /// </summary>
    protected void SetTypedPayload<TPayload>(TPayload value) => Payload = value;

    /// <summary>
    /// 基类默认打开参数，按 <see cref="UIType"/> 从 <see cref="DefaultUIOpenOpt.ForType"/> 取得。
    /// 与 <c>UIRegistration</c> 工厂共用同一份定义，避免两处各写一份而漂移。
    /// </summary>
    public override UIOpenOpt? BaseOpenOpt => DefaultUIOpenOpt.ForType(UIType);

    #region 生命周期（子类 override）
    protected virtual void OnPreLoad(Action done, Action fail) => done();
    protected virtual void OnCreate() { }
    protected virtual void InitEvent() { }
    protected abstract void OnOpen();
    protected abstract void UpdateView();

    protected virtual Action? OnOpenAnim(Action done)
    {
        done();
        return null;
    }

    protected virtual void OnOpenAnimDone() { }

    protected virtual Action? OnCloseAnim(Action done)
    {
        done();
        return null;
    }

    protected virtual void OnClose() { }
    protected virtual void OnLayerVisibleUpdate() { }
    #endregion

    #region IUILifecycleInvoker 显式实现
    void IUILifecycleInvoker.InvokePreLoad(Action done, Action fail) => OnPreLoad(done, fail);
    void IUILifecycleInvoker.InvokeCreate() => OnCreate();
    void IUILifecycleInvoker.InvokeResetBindings() => ResetBindings();
    void IUILifecycleInvoker.InvokeInitEvent() => InitEvent();
    void IUILifecycleInvoker.InvokeOpen() => OnOpen();
    Action? IUILifecycleInvoker.InvokeOpenAnim(Action done) => OnOpenAnim(done);
    Action? IUILifecycleInvoker.InvokeCloseAnim(Action done) => OnCloseAnim(done);
    void IUILifecycleInvoker.InvokeClose() => OnClose();
    void IUILifecycleInvoker.InvokeLayerVisibleUpdate() => OnLayerVisibleUpdate();
    void IUILifecycleInvoker.InvokeOpenAnimDone() => OnOpenAnimDone();

    // Mask 生命周期（Win 不支持，空实现）
    void IUILifecycleInvoker.InvokeMaskOpen() { }
    void IUILifecycleInvoker.InvokeMaskUIOpen() { }
    void IUILifecycleInvoker.InvokeMaskOpenAnimDone() { }
    void IUILifecycleInvoker.InvokeMaskClose() { }
    void IUILifecycleInvoker.InvokeMaskUIClose() { }
    void IUILifecycleInvoker.InvokeMaskUIDestroy() { }
    Action? IUILifecycleInvoker.InvokeMaskOpenAnim(Action done) { done(); return null; }
    Action? IUILifecycleInvoker.InvokeMaskCloseAnim(Action done) { done(); return null; }
    #endregion

    public void Close()
    {
        if (!string.IsNullOrEmpty(UIId))
        {
            UIManager.Instance?.Close(UIId);
        }
    }

    public bool IsUITop() => UIManager.Instance?.IsUITop(UIId) ?? false;
}