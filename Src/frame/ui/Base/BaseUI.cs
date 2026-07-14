using Godot;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 基础 UI 类，所有 UI 都应该继承自此类
/// </summary>
public abstract partial class BaseUI : Control, IUIMeta
{
    /// <summary>
    /// 点击事件的回调函数
    /// </summary>
    private readonly Dictionary<Node, Action> _clickActions = [];
    /// <summary>
    /// Control 节点的 GuiInput 事件处理器映射，用于反订阅
    /// </summary>
    private readonly Dictionary<Node, Control.GuiInputEventHandler> _guiInputHandlers = [];
    private readonly List<Action> _unbindActions = [];

    /// <summary>
    /// UI 的 ID
    /// </summary>
    public abstract string UIId { get; }
    /// <summary>
    /// UI 的目录
    /// </summary>
    public abstract string UIDir { get; }
    /// <summary>
    /// UI 的类型
    /// </summary>
    public abstract EUIType UIType { get; }
    /// <summary>
    /// 基础打开选项
    /// </summary>
    public virtual UIOpenOpt? BaseOpenOpt => null;
    /// <summary>
    /// 打开选项
    /// </summary>
    public virtual UIOpenOpt? OpenOpt => null;

    /// <summary>
    /// 获取 UI 的场景路径
    /// </summary>
    public string GetScenePath()
    {
        return $"res://{UIDir}/{UIId}.tscn";
    }

    public override void _Ready()
    {
        OnReady();
    }

    public override void _ExitTree()
    {
        ClearLifeCycle();
        base._ExitTree();
    }

    protected virtual void OnReady()
    {
    }

    #region 生命周期
    protected void ClearLifeCycle()
    {
        foreach (var (node, action) in _clickActions)
        {
            if (IsInstanceValid(node))
            {
                if (node is Button btn)
                {
                    btn.Pressed -= action;
                }
                else if (node is Control ctrl)
                {
                    if (_guiInputHandlers.TryGetValue(node, out var guiHandler))
                    {
                        ctrl.GuiInput -= guiHandler;
                    }
                }
            }
        }
        _clickActions.Clear();
        _guiInputHandlers.Clear();
        for (var i = _unbindActions.Count - 1; i >= 0; i--)
        {
            try { _unbindActions[i](); }
            catch { /* 离开树时忽略反订阅异常 */ }
        }
        _unbindActions.Clear();
    }

    protected void Bind(Action subscribe, Action unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        subscribe();
        _unbindActions.Add(unsubscribe);
    }

    protected void OnClicks(params (Node node, Action action)[] clicks)
    {
        foreach (var (node, action) in clicks)
        {
            OnClicks(node, action);
        }
    }

    protected void OnClicks(Node node, Action callback)
    {
        if (_clickActions.TryGetValue(node, out Action? existingAction))
        {
            if (node is Button oldBtn)
            {
                oldBtn.Pressed -= existingAction;
            }
            else if (node is Control oldCtrl)
            {
                if (_guiInputHandlers.TryGetValue(node, out var oldGuiHandler))
                {
                    oldCtrl.GuiInput -= oldGuiHandler;
                }
            }
            _clickActions.Remove(node);
            _guiInputHandlers.Remove(node);
        }

        _clickActions[node] = callback;

        // Godot.Button（含项目 BaseButton）走 Pressed；进缓存 RemoveChild 后需在重开时再次 InitEvent 绑定
        if (node is Button button)
        {
            button.Pressed += callback;
        }
        else if (node is Control ctrl)
        {
            void GuiHandler(InputEvent @event)
            {
                if (@event is InputEventMouseButton mouseBtn && mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
                {
                    callback();
                }
            }
            ctrl.GuiInput += GuiHandler;
            _guiInputHandlers[node] = GuiHandler;
        }
    }
    #endregion
}