using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 关闭 UI 状态处理器: 播放关闭动画
/// </summary>
public sealed class UICloseStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Close;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext? context, object? data)
    {
        UIVo? vo = context?.UIVo;
        if (vo == null) return;

        vo.Lifecycle.CloseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (vo.Runtime.UI == null || vo.Lifecycle.OpenTime == 0)
        {
            vo.StateMachine.TransitionTo(EUIState.Destroy, context);
            return;
        }

        if (vo.Runtime.Mask != null)
        {
            vo.Anim.StartMaskCloseAnim(vo.Runtime.Mask, () => { });
        }

        vo.Anim.StartCloseAnim(vo.OpenOpt.AnimType, vo.Runtime.UI,
            () =>
            {
                if (vo.StateMachine.CurrentState != EUIState.Close) return;
                vo.StateMachine.TransitionTo(EUIState.CloseDone, context);
            });
    }
}