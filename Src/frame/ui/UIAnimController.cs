using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// 集中管理 UI 与 Mask 的动画状态及回调生命周期。
/// 消除分散在 UIVo 和多个 StateHandler 中的 ClearAnim/ClearMaskAnim 重复逻辑。
/// </summary>
public sealed class UIAnimController
{
    private readonly AnimationSlot _uiAnimation = new();
    private readonly AnimationSlot _maskAnimation = new();

    #region 动画入口

    /// <summary>注册 UI 动画清除回调</summary>
    public void SetAnimCallback(Action? callback) => _uiAnimation.SetCallback(callback);

    /// <summary>注册 Mask 动画清除回调</summary>
    public void SetMaskAnimCallback(Action? callback) => _maskAnimation.SetCallback(callback);

    /// <summary>清除 UI 动画并调用清除回调</summary>
    public void ClearAnim() => _uiAnimation.Clear();

    /// <summary>清除 Mask 动画并调用清除回调</summary>
    public void ClearMaskAnim() => _maskAnimation.Clear();

    /// <summary>开始打开动画</summary>
    public void StartOpenAnim(EAnimType animType, OpenTransitionData transition, BaseWin win, Action onDone)
    {
        if (animType == EAnimType.None || (animType == EAnimType.SkipReOpen && transition.IsReopen))
        {
            ClearAnim();
            win.AnimState = EUIAnimState.None;
            onDone();
            return;
        }

        _uiAnimation.Play(done =>
        {
            win.AnimState = EUIAnimState.Open;
            return ((IUILifecycleInvoker)win).InvokeOpenAnim(done);
        }, () =>
        {
            win.AnimState = EUIAnimState.None;
            ((IUILifecycleInvoker)win).InvokeOpenAnimDone();
            onDone();
        });
    }

    /// <summary>开始 Mask 打开动画</summary>
    public void StartMaskOpenAnim(BaseMask mask, Action onDone)
    {
        _maskAnimation.Play(done =>
        {
            mask.AnimState = EUIAnimState.Open;
            return ((IUILifecycleInvoker)mask).InvokeMaskOpenAnim(done);
        }, () =>
        {
            mask.AnimState = EUIAnimState.None;
            ((IUILifecycleInvoker)mask).InvokeMaskOpenAnimDone();
            onDone();
        });
    }

    /// <summary>开始关闭动画</summary>
    public void StartCloseAnim(EAnimType animType, BaseWin win, Action onDone)
    {
        if (animType == EAnimType.None)
        {
            ClearAnim();
            win.AnimState = EUIAnimState.None;
            onDone();
            return;
        }

        _uiAnimation.Play(done =>
        {
            win.AnimState = EUIAnimState.Close;
            return ((IUILifecycleInvoker)win).InvokeCloseAnim(done);
        }, () =>
        {
            win.AnimState = EUIAnimState.None;
            onDone();
        });
    }

    /// <summary>开始 Mask 关闭动画</summary>
    public void StartMaskCloseAnim(BaseMask mask, Action onCloseDone)
    {
        _maskAnimation.Play(done =>
        {
            mask.AnimState = EUIAnimState.Close;
            return ((IUILifecycleInvoker)mask).InvokeMaskCloseAnim(done);
        }, () =>
        {
            mask.AnimState = EUIAnimState.None;
            UIManager.InvokeCallback(() => ((IUILifecycleInvoker)mask).InvokeMaskClose(), mask.UIVo?.Id ?? "Mask", "遮罩关闭回调");
            onCloseDone();
        });
    }

    #endregion

    /// <summary>一次动画的取消账本；回调只能完成当前动画且只完成一次。</summary>
    private sealed class AnimationSlot
    {
        private Action? _cancel;
        private int _version;

        public void SetCallback(Action? callback)
        {
            int version = Clear();
            if (version == _version) _cancel = callback;
        }

        public int Clear()
        {
            int version = ++_version;
            var cancel = _cancel;
            _cancel = null;
            try { cancel?.Invoke(); }
            catch (Exception ex) { AppLog.Error($"UI 动画取消失败：{ex.Message}", "UI"); }
            return version;
        }

        public void Play(Func<Action, Action?> play, Action onDone)
        {
            int version = Clear();
            if (version != _version) return;
            bool completed = false;
            Action? cancel = play(() =>
            {
                if (version != _version || completed) return;
                completed = true;
                onDone();
            });
            if (version == _version)
            {
                _cancel = cancel;
            }
            else
            {
                // 同步完成回调可能启动另一轮动画；旧取消句柄不能覆盖它。
                try { cancel?.Invoke(); }
                catch (Exception ex) { AppLog.Error($"UI 动画取消失败：{ex.Message}", "UI"); }
            }
        }
    }
}