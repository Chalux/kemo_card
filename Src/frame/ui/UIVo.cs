using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
using KemoCard.Frame.Util;

namespace KemoCard.Frame.UI;

/// <summary>
/// 界面数据，管理界面流程和状态机
/// </summary>
public sealed class UIVo : IUIStateContext, IUIVoHandle
{
    public readonly StateMachine<EUIState, IUIStateContext> StateMachine = new();

    public string Id { get; }
    public EUIType Type { get; }
    public object? Payload { get; set; }
    public UIOpenOpt OpenOpt { get; set; }
    public BaseWin? UI { get; set; }
    public BaseMask? Mask { get; set; }
    public UILayer? Layer { get; set; }
    public BoolVal HideBool { get; } = new();

    public long LoadTime { get; set; }
    public long CreateTime { get; set; }
    public long OpenTime { get; set; }
    public long CloseTime { get; set; }
    public long DestroyTime { get; set; }

    public int LoadFlag { get; set; }
    public int PreLoadFlag { get; set; }
    public bool HasMaskLoaded { get; set; }
    public CancellationTokenSource LoadToken { get; } = new();

    internal Action? ClearAnimCallback { get; set; }
    internal Action? ClearMaskAnimCallback { get; set; }

    UIVo IUIStateContext.UIVo => this;
    public UIManager Manager { get; }
    UIManager IUIStateContext.UIManager => Manager;

    public UIVo(string id, EUIType type, object? payload, UIManager manager, IEnumerable<IStateHandler<EUIState, IUIStateContext>> handlers, UIOpenOpt? openOpt = null)
    {
        Id = id;
        Type = type;
        Payload = payload;
        Manager = manager;
        OpenOpt = openOpt ?? DefaultUIOpenOpt.Value;
        foreach (var handler in handlers)
        {
            StateMachine.Configure(handler.State, handler.OnEnter, handler.OnExit);
        }
    }

    public void OpenNext()
    {
        Manager.OpenNext();
    }

    public bool IsOpen => StateMachine.CurrentState >= EUIState.Create && StateMachine.CurrentState <= EUIState.Open;

    public bool IsClose => StateMachine.CurrentState >= EUIState.Close;

    internal void ClearAnim()
    {
        ClearAnimCallback?.Invoke();
        ClearAnimCallback = null;
    }

    internal void ClearMaskAnim()
    {
        ClearMaskAnimCallback?.Invoke();
        ClearMaskAnimCallback = null;
    }

    internal void UpdateVisible()
    {
        if (UI == null) return;

        UI.Visible = !HideBool.Value;
        if (Mask != null)
        {
            Mask.Visible = UI.Visible;
        }

        UI.InternalLayerVisibleUpdate();
    }

    internal void AddToNode()
    {
        if (UI == null) return;

        UILayer? layer = Manager.GetLayer(OpenOpt.Layer ?? EUILayer.Dlg);
        Control? parent = OpenOpt.Parent ?? layer;

        if (parent == null)
        {
            GD.PushError($"界面<{Id}>挂载失败：无有效父节点");
            return;
        }

        if (UI.GetParent() == parent)
        {
            if (Mask != null && Mask.GetParent() == null && OpenOpt.Parent == null && layer != null)
            {
                int idx = UI.GetIndex();
                layer.AddChild(Mask);
                layer.MoveChild(Mask, idx);
            }

            return;
        }

        if (UI.GetParent() is UILayer oldLayer)
        {
            oldLayer.RemoveUI(Id);
        }
    }
}