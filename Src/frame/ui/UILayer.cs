using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Util;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 层级管理器
/// </summary>
public partial class UILayer : Control
{
    public EUILayer Type { get; }
    private readonly Dictionary<string, BaseWin> _uiMap = [];
    private readonly List<BaseWin> _uiSort = [];

    public string LayerName { get; }
    public UIOpenOpt OpenOpt { get; }
    public bool IsTop { get; }
    public BoolVal HideBool { get; } = new();
    public IReadOnlyList<BaseWin> UISort => _uiSort;

    public UILayer(string layerName, EUILayer type, UIOpenOpt openOpt, bool isTop)
    {
        LayerName = layerName;
        Type = type;
        OpenOpt = openOpt;
        IsTop = isTop;
        Name = $"Layer-{layerName}";
        MouseFilter = MouseFilterEnum.Ignore;

        HideBool.OnChange = () => Visible = !HideBool.Value;
    }

    public bool HasUI(BaseWin ui) => _uiMap.ContainsKey(ui.UIId);

    public void AddUI(BaseWin ui)
    {
        ui.UIVo?.Layer?.RemoveUI(ui.UIId);

        _uiMap[ui.UIId] = ui;
        _uiSort.Add(ui);

        if (ui.UIVo?.Mask != null)
        {
            AddChild(ui.UIVo.Mask);
        }

        AddChild(ui);
        if (ui.UIVo != null)
        {
            ui.UIVo.Layer = this;
        }
    }

    public void RemoveUI(string id)
    {
        if (!_uiMap.TryGetValue(id, out var ui)) return;

        _uiMap.Remove(id);
        _uiSort.Remove(ui);

        if (ui.UIVo?.Mask != null && ui.UIVo.Mask.AnimState == EUIAnimState.None)
        {
            ui.UIVo.Mask.GetParent()?.RemoveChild(ui.UIVo.Mask);
        }

        if (ui.UIVo?.Layer == this)
        {
            ui.UIVo.Layer = null;
        }

        ui.GetParent()?.RemoveChild(ui);
    }
}