using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

public abstract partial class BaseWin : BaseUI, IUILifecycleInvoker
{
    public UIVo? UIVo { get; set; }
    public object? Payload { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    public override EUIType UIType => EUIType.Win;

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

    public override UIOpenOpt? BaseOpenOpt => new()
    {
        Layer = EUILayer.Win,
        Align = EUIAlign.Full,
        HideBelow = true,
    };

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
