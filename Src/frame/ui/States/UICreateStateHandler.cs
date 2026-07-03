using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 创建 UI 状态处理器: 首次打开，初始化界面
/// </summary>
public sealed class UICreateStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Create;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext context, object? data)
    {
        UIVo vo = context.UIVo;
        vo.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        vo.Mask?.Init(vo);
        vo.AddToNode();

        vo.UI!.InternalCreate();

        if (vo.StateMachine.CurrentState != EUIState.Create)
        {
            return;
        }

        vo.StateMachine.TransitionTo(EUIState.Open);
    }
}