using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;

namespace KemoCard.Frame.UI;

/// <summary>
/// 界面数据，作为多个子对象的组合容器，管理界面流程和状态机。
/// 改进后拆分为: Lifecycle / Runtime / Load / Anim 四个职责单一的子对象。
/// </summary>
public sealed class UIVo : IUIStateContext, IUIVoHandle
{
    public readonly StateMachine<EUIState, IUIStateContext> StateMachine = new();

    public string Id { get; }

    /// <summary>归属功能 Mod 的 id：界面只允许通过它取自己功能的门面（见 ui-mod-binding 规格 §5.4）。</summary>
    public string OwnerModId { get; }

    public EUIType Type { get; }
    public object? Payload { get; set; }
    public UIOpenOpt OpenOpt { get; set; }
    public UIManager Manager { get; }

    /// <summary>生命周期时间戳</summary>
    public UILifecycleState Lifecycle { get; } = new();

    /// <summary>运行时引用（UI节点、遮罩、层级）</summary>
    public UIRuntimeData Runtime { get; }

    /// <summary>异步加载上下文</summary>
    public UILoadContext Load { get; } = new();

    /// <summary>动画管理</summary>
    public UIAnimController Anim { get; } = new();

    /// <summary>UI 节点（委托到 Runtime.UI）</summary>
    public BaseWin? UI { get => Runtime.UI; set => Runtime.UI = value; }
    /// <summary>遮罩节点（委托到 Runtime.Mask）</summary>
    public BaseMask? Mask { get => Runtime.Mask; set => Runtime.Mask = value; }
    /// <summary>所在层级（委托到 Runtime.Layer）</summary>
    public UILayer? Layer { get => Runtime.Layer; set => Runtime.Layer = value; }

    internal TaskCompletionSource<UIVo?>? OpenTaskSource { get; set; }

    UIVo IUIStateContext.UIVo => this;
    UIManager IUIStateContext.UIManager => Manager;

    public bool IsOpen => StateMachine.CurrentState is >= EUIState.Create and <= EUIState.Open;
    public bool IsClose => StateMachine.CurrentState >= EUIState.Close;

    public UIVo(string id, EUIType type, string ownerModId, object? payload, UIManager manager,
        IEnumerable<IStateHandler<EUIState, IUIStateContext>> handlers, UIOpenOpt? openOpt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);
        Id = id;
        OwnerModId = ownerModId;
        Type = type;
        Payload = payload;
        Manager = manager;
        OpenOpt = openOpt ?? DefaultUIOpenOpt.Value;
        Runtime = new UIRuntimeData(this);

        foreach (var handler in handlers)
        {
            StateMachine.Configure(handler.State, handler.OnEnter, handler.OnExit);
        }
    }

    public void OpenNext()
    {
        Manager.OpenNext();
    }
}