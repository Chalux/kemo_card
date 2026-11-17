using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Util;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 运行时引用数据（Node、遮罩、层级），从 UIVo 中拆出的组合对象。
/// </summary>
public sealed class UIRuntimeData
{
    private readonly UIVo _owner;

    public UIRuntimeData(UIVo owner)
    {
        _owner = owner;
        HideBool.OnChange = UpdateVisible;
    }

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
            MountMask(parent);
            return;
        }

        if (UI.GetParent() is UILayer oldLayer)
        {
            oldLayer.RemoveUI(_owner.Id);
        }
        else
        {
            UI.GetParent()?.RemoveChild(UI);
        }

        if (_owner.OpenOpt.Parent != null || layer == null)
        {
            parent.AddChild(UI);
        }
        else
        {
            layer.AddUI(UI);
        }

        MountMask(parent);
    }

    /// <summary>
    /// 遮罩统一在这里挂载（<see cref="UILayer.AddUI"/> 不再处理），且始终排在界面之前，
    /// 保证遮罩绘制在界面下方。已挂在同一父节点时只校正顺序，不重复 AddChild。
    /// </summary>
    private void MountMask(Control parent)
    {
        BaseMask? mask = Mask;
        if (mask == null || !GodotObject.IsInstanceValid(mask) || UI == null) return;

        if (mask.GetParent() != parent)
        {
            mask.GetParent()?.RemoveChild(mask);
            parent.AddChild(mask);
        }

        int uiIndex = UI.GetIndex();
        if (mask.GetIndex() > uiIndex)
        {
            parent.MoveChild(mask, uiIndex);
        }
    }

    public void UpdateVisible()
    {
        var ui = UI;
        if (ui == null || !GodotObject.IsInstanceValid(ui)) return;

        ui.Visible = !HideBool.Value;
        var mask = Mask;
        if (mask != null && GodotObject.IsInstanceValid(mask))
        {
            mask.Visible = ui.Visible;
        }

        UIManager.InvokeCallback(() => ((IUILifecycleInvoker)ui).InvokeLayerVisibleUpdate(), _owner.Id, "可见性回调");
    }

    public void RemoveFromNode()
    {
        Layer?.RemoveUI(_owner.Id);
        if (GodotObject.IsInstanceValid(Mask)) Mask!.GetParent()?.RemoveChild(Mask);
        if (GodotObject.IsInstanceValid(UI)) UI!.GetParent()?.RemoveChild(UI);
    }
}