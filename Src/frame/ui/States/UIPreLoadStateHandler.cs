using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Logging;

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

        int preloadFlag = vo.Load.IncrementPreLoadFlag();
        bool completed = false;
        bool TryComplete()
        {
            if (completed || preloadFlag != vo.Load.PreLoadFlag || vo.StateMachine.CurrentState != EUIState.PreLoad)
                return false;
            completed = true;
            return true;
        }

        void Fail()
        {
            if (!TryComplete()) return;
            AppLog.Error($"UI 管理器: 预加载UI<{vo.Id}> 失败。", "UI");
            try { vo.StateMachine.TransitionTo(EUIState.Destroy, context); }
            finally { context?.OpenNext(); }
        }

        void Abort(Exception ex)
        {
            if (preloadFlag != vo.Load.PreLoadFlag) return;
            // 已真正打开（OpenTime > 0）之后业务再抛异常，不能把界面回滚销毁；
            // 但 completed 之后仍停在 Create（OpenTime == 0）说明确实没打开成功，必须回收。
            if (completed && vo.Lifecycle.OpenTime > 0) return;
            if (vo.IsClose) return;
            AppLog.Error($"UI<{vo.Id}> 预加载/创建异常：{ex.Message}", "UI");
            try { vo.StateMachine.TransitionTo(EUIState.Destroy, context); }
            finally { context?.OpenNext(); }
        }

        BaseWin? win = vo.Runtime.UI;
        if (win == null || !GodotObject.IsInstanceValid(win))
        {
            Fail();
            return;
        }

        try
        {
            // 必须在 try 内：窗口可能已被业务释放（缓存重开），赋值会抛 ObjectDisposedException。
            win.Payload = vo.Payload;

            ((IUILifecycleInvoker)win).InvokePreLoad(() =>
            {
                if (!TryComplete()) return;

                bool reopen = payload is OpenTransitionData { IsReopen: true };
                try { vo.StateMachine.TransitionTo(reopen ? EUIState.Open : EUIState.Create, context, payload); }
                catch (Exception ex) { Abort(ex); }
            },
            Fail);
        }
        catch (Exception ex)
        {
            Abort(ex);
        }
    }
}