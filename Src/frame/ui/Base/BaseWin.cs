using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

public abstract partial class BaseWin : BaseUI
{
    public UIVo? UIVo { get; set; }
    public object? Payload { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    public override EUIType UIType => EUIType.Win;

    public override UIOpenOpt? BaseOpenOpt => new()
    {
        Layer = EUILayer.Win,
        Align = EUIAlign.Full,
        HideBelow = true,
    };

    #region 生命周期（子类override）
    protected virtual void OnPreLoad(Action done, Action fail)
    {
        done();
    }

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

    #region 内部生命周期（UIVo状态机调用）
    internal void InternalPreLoad(Action done, Action fail) => OnPreLoad(done, fail);
    internal void InternalCreate() => OnCreate();
    internal void InternalInitEvent() => InitEvent();
    internal void InternalOpen() => OnOpen();
    internal Action? InternalOpenAnim(Action done) => OnOpenAnim(done);
    internal Action? InternalCloseAnim(Action done) => OnCloseAnim(done);
    internal void InternalClose() => OnClose();
    internal void InternalLayerVisibleUpdate() => OnLayerVisibleUpdate();
    internal void InternalOpenAnimDone() => OnOpenAnimDone();
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