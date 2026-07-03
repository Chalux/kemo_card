using Godot;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 遮罩基类，管理遮罩的动画和事件
/// </summary>
public abstract partial class BaseMask : Control
{
    public UIVo? UIVo { get; set; }
    public EUIAnimState AnimState { get; set; } = EUIAnimState.None;

    public virtual void Init(UIVo uiVo) { }
    public virtual void InitEvent() { }

    protected virtual void OnOpen() { }
    protected virtual void OnUIOpen() { }
    protected virtual Action? OnOpenAnim(Action done)
    {
        done();
        return null;
    }

    protected virtual void OnOpenAnimDone() { }

    protected virtual void OnClose() { }
    protected virtual void OnUIClose() { }
    protected virtual Action? OnCloseAnim(Action done)
    {
        done();
        return null;
    }

    protected virtual void OnUIDestroy() { }

    internal void InternalInitEvent() => InitEvent();
    internal void InternalOpen() => OnOpen();
    internal void InternalUIOpen() => OnUIOpen();
    internal void InternalOpenAnimDone() => OnOpenAnimDone();
    internal void InternalClose() => OnClose();
    internal void InternalUIClose() => OnUIClose();
    internal void InternalUIDestroy() => OnUIDestroy();
    internal Action? InternalOpenAnim(Action done) => OnOpenAnim(done);
    internal Action? InternalCloseAnim(Action done) => OnCloseAnim(done);
}