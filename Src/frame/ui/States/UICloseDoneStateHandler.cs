using KemoCard.Frame.StateMachine;
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

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    public void OnEnterAction(EUIState state, IUIStateContext context, object? data)
    {
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;

        vo.CloseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        vo.UI!.InternalClose();
        vo.Mask?.InternalUIClose();

        if (vo.StateMachine.CurrentState != EUIState.CloseDone)
        {
            return;
        }

        UILayer? layer = manager.GetLayer(vo.OpenOpt.Layer ?? EUILayer.Dlg);
        layer?.RemoveUI(vo.Id);
        // vo.UI.RemoveFromParent();
        vo.UI.GetParent()?.RemoveChild(vo.UI);

        if (vo.OpenOpt.CacheTime == -1)
        {
            vo.DestroyTime = -1;
            vo.StateMachine.TransitionTo(EUIState.Cache);
        }
        else if (vo.OpenOpt.CacheTime > 0)
        {
            vo.DestroyTime = vo.CloseTime + vo.OpenOpt.CacheTime;
            vo.StateMachine.TransitionTo(EUIState.Cache);
        }
        else
        {
            vo.DestroyTime = vo.CloseTime;
            vo.StateMachine.TransitionTo(EUIState.Destroy);
        }

        manager.UpdateLayers();
        manager.EventDispatcher.Send(UIEvent.Close, new(vo));
    }
}