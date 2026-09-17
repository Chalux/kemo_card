using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

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
        vo.OpenOpt.OnOpenBefore?.Invoke(vo);
        vo.OpenOpt.OnOpenBefore = null;

        // 进缓存时 RemoveChild 会触发 ClearLifeCycle 卸掉 OnClicks；重开也必须重新 InitEvent
        if (vo.Runtime.Mask is IUILifecycleInvoker maskInvoker)
            maskInvoker.InvokeInitEvent();
        ((IUILifecycleInvoker)win).InvokeInitEvent();

        if (vo.StateMachine.CurrentState != EUIState.Open)
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

        if (vo.StateMachine.CurrentState != EUIState.Open)
        {
            return;
        }

        OpenTransitionData transitionData = data as OpenTransitionData? ?? default;
        vo.Anim.StartOpenAnim(vo.OpenOpt.EffectiveAnimType, transitionData, win,
            () => manager.LayerManager.UpdateLayers());

        manager.EventDispatcher.Send(UIEvent.Open, new(vo));
        vo.OpenOpt.OnOpen?.Invoke(vo);
        context.OpenNext();
    }
}