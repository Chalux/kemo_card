using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

public sealed class UIDestroyStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Destroy;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    public void OnEnterAction(EUIState state, IUIStateContext context, object? data)
    {
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;

        vo.LoadToken.Cancel();
        vo.ClearAnim();
        vo.ClearMaskAnim();

        vo.HideBool.Destory();
        vo.Mask?.InternalUIDestroy();
        vo.Mask?.QueueFree();
        vo.UI?.QueueFree();

        manager.UIVoMap.Remove(vo.Id);
        vo.OpenOpt.OnFail?.Invoke();
    }
}