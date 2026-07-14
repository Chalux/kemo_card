using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Util;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 运行时引用数据（Node、遮罩、层级），从 UIVo 中拆出的组合对象。
/// </summary>
public sealed class UIRuntimeData(UIVo owner)
{
    private readonly UIVo _owner = owner;

    public BaseWin? UI { get; set; }
    public BaseMask? Mask { get; set; }
    public UILayer? Layer { get; set; }
    public BoolVal HideBool { get; } = new();

    public void AddToNode()
    {
        if (UI == null) return;

        UILayer? layer = _owner.Manager.GetLayer(_owner.OpenOpt.Layer ?? EUILayer.Dlg);
        Control? parent = _owner.OpenOpt.Parent ?? layer;

        if (parent == null)
        {
            AppLog.Error($"界面<{_owner.Id}>挂载失败：无有效父节点", "UI");
            return;
        }

        if (UI.GetParent() == parent)
        {
            if (Mask != null && Mask.GetParent() == null && _owner.OpenOpt.Parent == null && layer != null)
            {
                int idx = UI.GetIndex();
                layer.AddChild(Mask);
                layer.MoveChild(Mask, idx);
            }
            return;
        }

        if (UI.GetParent() is UILayer oldLayer)
        {
            oldLayer.RemoveUI(_owner.Id);
        }

        if (_owner.OpenOpt.Parent != null)
        {
            parent.AddChild(UI);
            if (Mask != null && Mask.GetParent() == null)
            {
                int idx = UI.GetIndex();
                parent.AddChild(Mask);
                parent.MoveChild(Mask, idx);
            }
        }
        else
        {
            layer?.AddUI(UI);
        }
    }

    public void UpdateVisible()
    {
        if (UI == null) return;

        UI.Visible = !HideBool.Value;
        if (Mask != null)
        {
            Mask.Visible = UI.Visible;
        }

        ((IUILifecycleInvoker)UI).InvokeLayerVisibleUpdate();
    }
}
