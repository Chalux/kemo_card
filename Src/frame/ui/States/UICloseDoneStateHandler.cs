using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

public readonly struct UIClosePayload(UIVo vo)
{
    public UIVo Vo { get; } = vo;
}

/// <summary>
/// 关闭完成状态处理器: 进入缓存或销毁
/// </summary>
public sealed class UICloseDoneStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.CloseDone;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext? context, object? data)
    {
        UIVo? vo = context?.UIVo;
        if (vo == null) return;

        UIManager? manager = context?.UIManager;
        if (manager == null) return;

        vo.Lifecycle.CloseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ((IUILifecycleInvoker)vo.Runtime.UI!).InvokeClose();
        ((IUILifecycleInvoker?)vo.Runtime.Mask)?.InvokeMaskUIClose();

        if (vo.StateMachine.CurrentState != EUIState.CloseDone)
        {
            return;
        }

        UILayer? layer = manager.LayerManager.GetLayer(vo.OpenOpt.Layer ?? EUILayer.Dlg);
        layer?.RemoveUI(vo.Id);
        vo.Runtime.UI!.GetParent()?.RemoveChild(vo.Runtime.UI);

        if (vo.OpenOpt.CacheTime == -1)
        {
            vo.Lifecycle.DestroyTime = -1;
            vo.StateMachine.TransitionTo(EUIState.Cache, context);
        }
        else if (vo.OpenOpt.CacheTime > 0)
        {
            vo.Lifecycle.DestroyTime = vo.Lifecycle.CloseTime + vo.OpenOpt.CacheTime;
            vo.StateMachine.TransitionTo(EUIState.Cache, context);
        }
        else
        {
            vo.Lifecycle.DestroyTime = vo.Lifecycle.CloseTime;
            vo.StateMachine.TransitionTo(EUIState.Destroy, context);
        }

        manager.LayerManager.UpdateLayers();
        manager.EventDispatcher.Send(UIEvent.Close, new UIClosePayload(vo));
    }
}
