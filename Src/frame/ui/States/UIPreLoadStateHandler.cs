using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

public sealed class UIPreLoadStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.PreLoad;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterFunc;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    private void OnEnterFunc(EUIState fromState, IUIStateContext context, object? payload = null)
    {
        UIVo vo = context.UIVo;
        BaseWin win = vo.UI!;
        win.Payload = vo.Payload;

        int preloadFlag = ++vo.PreLoadFlag;

        win.InternalPreLoad(() =>
        {
            if (preloadFlag != vo.PreLoadFlag || vo.StateMachine.CurrentState != EUIState.PreLoad)
            {
                return;
            }

            bool reopen = payload != null;
            vo.StateMachine.TransitionTo(reopen ? EUIState.Open : EUIState.Create, context, payload);
        },
        () =>
        {
            GD.PushError($"UI 管理器: 预加载UI<{vo.Id}> 失败。");
            vo.StateMachine.TransitionTo(EUIState.Destroy);
            context.OpenNext();
        });
    }
}