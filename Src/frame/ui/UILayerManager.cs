using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 层级管理器：负责层级创建、查找、可见性刷新。
/// 从 UIManager 中拆分出的独立组件。
/// </summary>
public sealed class UILayerManager
{
    private readonly Dictionary<EUILayer, UILayer> _layerMap = [];
    private EUILayer[] _layers = [];
    private EUILayer[] _topLayers = [];
    private EUILayer[] _allLayers = [];

    public IReadOnlyDictionary<EUILayer, UILayer> LayerMap => _layerMap;

    public void Init(Control uiRoot, Control uiTopRoot, EUILayer[] layers, EUILayer[] topLayers,
        Dictionary<EUILayer, UIOpenOpt>? layerOpenOpts)
    {
        _layers = layers;
        _topLayers = topLayers;
        _allLayers = [.. _layers, .. _topLayers];

        BuildLayers(uiRoot, _layers, layerOpenOpts, false);
        BuildLayers(uiTopRoot, _topLayers, layerOpenOpts, true);
    }

    public UILayer? GetLayer(EUILayer id)
    {
        return _layerMap.TryGetValue(id, out var layer) ? layer : null;
    }

    public EUILayer[] GetAllLayers() => _allLayers;
    public EUILayer[] GetLayers() => _layers;

    public void UpdateLayers()
    {
        bool hide = false;
        const string hideKey = "hideBelow";

        for (int i = _allLayers.Length - 1; i >= 0; i--)
        {
            UILayer? layer = GetLayer(_allLayers[i]);
            if (layer == null) continue;

            layer.HideBool.Set(hideKey, hide);

            IReadOnlyList<BaseWin> uis = layer.UISort;
            for (int j = uis.Count - 1; j >= 0; j--)
            {
                UIVo? vo = uis[j].UIVo;
                if (vo == null) continue;

                vo.Runtime.HideBool.Set(hideKey, hide);
                if (!hide)
                {
                    hide = vo.OpenOpt.HideBelow;
                }
            }
        }
    }

    public static UILayer? FindLayerForNode(Node node)
    {
        Node? cur = node;
        while (cur != null)
        {
            if (cur is UILayer layer) return layer;
            cur = cur.GetParent();
        }
        return null;
    }

    private void BuildLayers(Control root, EUILayer[] layers,
        Dictionary<EUILayer, UIOpenOpt>? openOpts, bool isTop)
    {
        foreach (EUILayer l in layers)
        {
            openOpts ??= [];
            openOpts.TryGetValue(l, out UIOpenOpt? opt);
            UILayer layer = new(l.ToString(), l, opt ?? DefaultUIOpenOpt.Value, isTop);
            _layerMap[l] = layer;
            root.AddChild(layer);
            layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        }
    }
}
