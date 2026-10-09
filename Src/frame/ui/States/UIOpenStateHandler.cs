using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI.States;

public readonly struct UIOpenPayload(UIVo vo)
{
    public UIVo Vo { get; } = vo;
}

/// <summary>
/// 打开 UI 状态处理器: 初始化事件、播放动画、派发事件
/// </summary>
public sealed class UIOpenStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Open;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext? context, object? data)
    {
        if (context == null) return;
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;
        BaseWin win = vo.Runtime.UI!;
        int requestFlag = vo.Load.PreLoadFlag;
        var openOpt = vo.OpenOpt;
        var task = vo.OpenTaskSource;
        bool IsCurrentRequest() => vo.StateMachine.CurrentState == EUIState.Open && requestFlag == vo.Load.PreLoadFlag;

        try
        {
            vo.Lifecycle.OpenTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 规格 ui-manager §HideBelow：全屏界面（HideBelow = true）打开时自动压入导航栈，
            // 供 BackAsync 关闭当前并恢复上一层。此前 NavStack.Push 全仓无调用者，BackAsync 恒返回 null。
            if (vo.OpenOpt.EffectiveHideBelow && !manager.NavStack.Contains(vo.Id))
            {
                manager.NavStack.Push(vo.Id);
            }

            if (data != null)
            {
                vo.Runtime.AddToNode();
            }

            win.Payload = vo.Payload;
            var onOpenBefore = openOpt.OnOpenBefore;
            openOpt.OnOpenBefore = null;
            UIManager.InvokeCallback(() => onOpenBefore?.Invoke(vo), vo.Id, "打开前回调");
            if (!IsCurrentRequest()) return;

            // 进缓存时 RemoveChild 会触发 _ExitTree → Binder.UnbindAll 卸掉 OnClicks；重开也必须重新 InitEvent。
            // 先 ResetBindings 再 InitEvent：**已打开状态下的再次 Open** 不经过离场，旧订阅还活着，
            // 不清账就会重复订阅（Godot: "Signal 'pressed' is already connected"，账本还会留下解不掉的条目）。
            if (vo.Runtime.Mask is IUILifecycleInvoker maskInvoker)
            {
                maskInvoker.InvokeResetBindings();
                maskInvoker.InvokeInitEvent();
            }

            IUILifecycleInvoker winInvoker = (IUILifecycleInvoker)win;
            winInvoker.InvokeResetBindings();
            winInvoker.InvokeInitEvent();

            if (!IsCurrentRequest())
            {
                return;
            }

            if (vo.Runtime.Mask != null)
            {
                vo.Anim.StartMaskOpenAnim(vo.Runtime.Mask, () => { });
                ((IUILifecycleInvoker)vo.Runtime.Mask).InvokeMaskOpen();
            }

            ((IUILifecycleInvoker)win).InvokeOpen();
            ((IUILifecycleInvoker?)vo.Runtime.Mask)?.InvokeMaskUIOpen();

            if (!IsCurrentRequest())
            {
                return;
            }

            OpenTransitionData transitionData = data as OpenTransitionData? ?? default;
            vo.Runtime.UpdateVisible();
            if (!IsCurrentRequest()) return;
            vo.Anim.StartOpenAnim(vo.OpenOpt.EffectiveAnimType, transitionData, win,
                () => manager.LayerManager.UpdateLayers());

            if (!IsCurrentRequest()) return;
            // 成功状态先交付；事件/观察回调重入不能把已经打开的请求改成失败。
            vo.CompleteOpen(vo, task);
            manager.EventDispatcher.Send(UIEvent.Open, new(vo));
            openOpt.OnOpen?.Invoke(vo);
        }
        catch (Exception ex)
        {
            AppLog.Error($"UI<{vo.Id}> 打开生命周期失败：{ex.Message}", "UI");
            if (IsCurrentRequest()) vo.StateMachine.TransitionTo(EUIState.Destroy, context);
        }
        finally { context.OpenNext(); }
    }
}