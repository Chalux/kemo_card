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

    /// <summary>
    /// 字典键统一用 <see cref="UIVo.Id"/>（路由 id）：移除侧拿到的是 <c>vo.Id</c>，
    /// 而 <see cref="BaseUI.UIId"/> 只是「约定上」与之一致，组件类（<c>BaseCmp</c>）并不成立。
    /// </summary>
    private static string KeyOf(BaseWin ui) => ui.UIVo?.Id ?? ui.UIId;

    public bool HasUI(BaseWin ui) => _uiMap.ContainsKey(KeyOf(ui));

    public void AddUI(BaseWin ui)
    {
        string key = KeyOf(ui);
        ui.UIVo?.Layer?.RemoveUI(key);

        // 同键重入时先摘掉旧条目，避免 _uiSort 出现重复节点。
        if (_uiMap.TryGetValue(key, out BaseWin? existing))
        {
            _uiSort.Remove(existing);
            _uiMap.Remove(key);
        }

        _uiMap[key] = ui;
        _uiSort.Add(ui);

        // 遮罩由 UIRuntimeData.AddToNode 统一挂载（保证插在界面之前），此处不再处理。
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

        if (GodotObject.IsInstanceValid(ui.UIVo?.Mask))
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