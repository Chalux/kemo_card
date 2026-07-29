using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 销毁 UI 状态处理器: 清理资源
/// </summary>
public sealed class UIDestroyStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Destroy;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext? context, object? data)
    {
        if (context == null) return;
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;

        vo.Load.Cancel();
        vo.Anim.ClearAnim();
        vo.Anim.ClearMaskAnim();

        vo.Runtime.HideBool.Destory();
        ((IUILifecycleInvoker?)vo.Runtime.Mask)?.InvokeMaskUIDestroy();
        vo.Runtime.Mask?.QueueFree();
        vo.Runtime.UI?.QueueFree();

        manager.VoRegistry.Remove(vo.Id);

        if (state is EUIState.Load or EUIState.PreLoad)
        {
            vo.OpenOpt.OnFail?.Invoke();
        }

        vo.OpenTaskSource?.TrySetResult(null);
        vo.OpenTaskSource = null;
    }
}