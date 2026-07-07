using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 预加载 UI 状态处理器: OnPreLoad(done, fail) 业务逻辑
/// </summary>
public sealed class UIPreLoadStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.PreLoad;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterFunc;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    private void OnEnterFunc(EUIState fromState, IUIStateContext? context, object? payload = null)
    {
        UIVo? vo = context?.UIVo;
        if (vo == null) return;

        BaseWin win = vo.Runtime.UI!;
        win.Payload = vo.Payload;

        int preloadFlag = vo.Load.IncrementPreLoadFlag();

        ((IUILifecycleInvoker)win).InvokePreLoad(() =>
        {
            if (preloadFlag != vo.Load.PreLoadFlag || vo.StateMachine.CurrentState != EUIState.PreLoad)
            {
                return;
            }

            bool reopen = payload is OpenTransitionData { IsReopen: true };
            vo.StateMachine.TransitionTo(reopen ? EUIState.Open : EUIState.Create, context, payload);
        },
        () =>
        {
            GD.PushError($"UI 管理器: 预加载UI<{vo.Id}> 失败。");
            vo.StateMachine.TransitionTo(EUIState.Destroy, context);
            context?.OpenNext();
        });
    }
}
