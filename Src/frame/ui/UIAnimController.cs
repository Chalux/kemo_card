using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI;

/// <summary>
/// 集中管理 UI 与 Mask 的动画状态及回调生命周期。
/// 消除分散在 UIVo 和多个 StateHandler 中的 ClearAnim/ClearMaskAnim 重复逻辑。
/// </summary>
public sealed class UIAnimController
{
    private Action? _animCallback;
    private Action? _maskAnimCallback;

    /// <summary>注册 UI 动画清除回调</summary>
    public void SetAnimCallback(Action? callback)
    {
        ClearAnim();
        _animCallback = callback;
    }

    /// <summary>注册 Mask 动画清除回调</summary>
    public void SetMaskAnimCallback(Action? callback)
    {
        ClearMaskAnim();
        _maskAnimCallback = callback;
    }

    /// <summary>清除 UI 动画并调用清除回调</summary>
    public void ClearAnim()
    {
        _animCallback?.Invoke();
        _animCallback = null;
    }

    /// <summary>清除 Mask 动画并调用清除回调</summary>
    public void ClearMaskAnim()
    {
        _maskAnimCallback?.Invoke();
        _maskAnimCallback = null;
    }

    /// <summary>开始打开动画</summary>
    public void StartOpenAnim(EAnimType animType, OpenTransitionData transition, BaseWin win, Action onDone)
    {
        if (animType == EAnimType.None)
        {
            onDone();
            return;
        }

        if (animType == EAnimType.SkipReOpen && transition.IsReopen)
        {
            onDone();
            return;
        }

        win.AnimState = EUIAnimState.Open;
        SetAnimCallback(((IUILifecycleInvoker)win).InvokeOpenAnim(() =>
        {
            if (win.AnimState == EUIAnimState.Open)
            {
                win.AnimState = EUIAnimState.None;
            }
            ((IUILifecycleInvoker)win).InvokeOpenAnimDone();
            onDone();
        }));
    }

    /// <summary>开始 Mask 打开动画</summary>
    public void StartMaskOpenAnim(BaseMask mask, Action onDone)
    {
        mask.AnimState = EUIAnimState.Open;
        SetMaskAnimCallback(((IUILifecycleInvoker)mask).InvokeMaskOpenAnim(() =>
        {
            if (mask.AnimState == EUIAnimState.Open)
            {
                mask.AnimState = EUIAnimState.None;
            }
            ((IUILifecycleInvoker)mask).InvokeMaskOpenAnimDone();
            onDone();
        }));
    }

    /// <summary>开始关闭动画</summary>
    public void StartCloseAnim(EAnimType animType, BaseWin win, Action onDone)
    {
        if (animType == EAnimType.None)
        {
            onDone();
            return;
        }

        win.AnimState = EUIAnimState.Close;
        SetAnimCallback(((IUILifecycleInvoker)win).InvokeCloseAnim(() =>
        {
            if (win.AnimState == EUIAnimState.Close)
            {
                win.AnimState = EUIAnimState.None;
            }
            onDone();
        }));
    }

    /// <summary>开始 Mask 关闭动画</summary>
    public void StartMaskCloseAnim(BaseMask mask, Action onCloseDone)
    {
        mask.AnimState = EUIAnimState.Close;
        SetMaskAnimCallback(((IUILifecycleInvoker)mask).InvokeMaskCloseAnim(() =>
        {
            if (mask.AnimState == EUIAnimState.Close)
            {
                mask.AnimState = EUIAnimState.None;
            }
            ((IUILifecycleInvoker)mask).InvokeMaskClose();
            mask.QueueFree();
            onCloseDone();
        }));
    }
}