using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 关闭 UI 状态处理器: 播放关闭动画
/// </summary>
public sealed class UICloseStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Close;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    public void OnEnterAction(EUIState state, IUIStateContext context, object? data)
    {
        UIVo vo = context.UIVo;
        vo.CloseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (vo.UI == null || vo.OpenTime == 0)
        {
            vo.StateMachine.TransitionTo(EUIState.Destroy);
            return;
        }

        if (vo.Mask != null)
        {
            vo.ClearMaskAnim();
            vo.Mask.AnimState = EUIAnimState.Close;
            vo.ClearMaskAnimCallback = vo.Mask.InternalCloseAnim(() =>
            {
                if (vo.Mask!.AnimState == EUIAnimState.Close)
                {
                    vo.Mask.AnimState = EUIAnimState.None;
                }

                vo.Mask.InternalClose();
                vo.Mask.QueueFree();
            });
        }

        if (vo.OpenOpt.AnimType != EAnimType.None)
        {
            vo.ClearAnim();
            vo.UI!.AnimState = EUIAnimState.Close;
            vo.ClearAnimCallback = vo.UI.InternalCloseAnim(() =>
            {
                if (vo.UI!.AnimState == EUIAnimState.Close)
                {
                    vo.UI!.AnimState = EUIAnimState.None;
                }

                if (vo.StateMachine.CurrentState != EUIState.Close)
                {
                    return;
                }

                vo.StateMachine.TransitionTo(EUIState.CloseDone);
            });
        }
        else
        {
            vo.StateMachine.TransitionTo(EUIState.CloseDone);
        }
    }
}