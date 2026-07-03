using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.Route;
using KemoCard.Frame.UI.States;
using static Godot.Control;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 管理器接口
/// </summary>
public interface IUIManager
{
    Task<UIVo?> OpenAsync(string id, object? payload = null, UIOpenOpt? openOpt = null);
    Task<UIVo?> OpenChildAsync(string childId, object? payload = null, UIOpenOpt? openOpt = null);
    void Close(string id);
    void CloseAllPop();
    void CloseAllByType(EUIType type);
    void CloseAllExclude(IReadOnlyList<string>? excludeIds = null);
    UIVo? GetUIVo(string id);
    BaseWin? GetWin(string id);
    UILayer? GetLayer(EUILayer layer);
    bool IsUITop(string Id);
    string GetPath(string id);
}

/// <summary>
/// UI 管理器
/// </summary>
public partial class UIManager : Node, IUIManager
{
    public static UIManager? Instance { get; private set; }

    public EventDispatcher EventDispatcher { get; } = new();

    internal Dictionary<string, UIVo> UIVoMap { get; } = [];
    internal readonly Dictionary<EUILayer, UILayer> LayerMap = [];

    private Control _uiRoot = null!;
    private Control _uiTopRoot = null!;
    private UIRuntimeRegistry _registry = null!;
    private readonly Queue<UIVo> _openQueue = [];
    private UIVo? _currOpening = null;
    private EUILayer[] _layers = [];
    private EUILayer[] _topLayers = [];
    private EUILayer[] _allLayers = [];
    private bool _inited = false;
    private double _cacheCheckAccumulator = 0;
    private List<IStateHandler<EUIState, IUIStateContext>> _handlers = null!;

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!_inited) return;

        _cacheCheckAccumulator += delta;
        if (_cacheCheckAccumulator < 1) return;

        _cacheCheckAccumulator = 0;
        TickCacheDestroy();
        OpenNext();
    }

    /// <summary>
    /// 初始化 UI 管理器
    /// </summary>
    /// <param name="opt">初始化选项</param>
    public void Init(UIManagerInitOpt opt)
    {
        if (_inited)
        {
            GD.PushError("UI 管理器已初始化，请勿重复初始化.");
            return;
        }

        _inited = true;
        Instance = this;

        _registry = opt.Registry;

        _handlers = [.. opt.StateHandlers ?? CreateDefaultStateHandlers()];

        _uiRoot = opt.UIRoot ?? CreateFullScreenRoot("UIRoot");
        _uiTopRoot = opt.UITopRoot ?? CreateFullScreenRoot("UITopRoot");

        if (_uiRoot.GetParent() == null)
        {
            opt.StageRoot?.AddChild(_uiRoot);
        }

        if (_uiTopRoot.GetParent() == null)
        {
            opt.StageRoot?.AddChild(_uiTopRoot);
        }

        _layers = [.. opt.Layers];
        _topLayers = [.. opt.TopLayers];
        _allLayers = [.. _layers, .. _topLayers];

        BuildLayers(_uiRoot, _layers, opt.LayerOpenOpts, false);
        BuildLayers(_uiTopRoot, _topLayers, opt.LayerOpenOpts, true);

        UIRouteRegistry.Validate([.. _registry.GetAllMap().Keys]);
    }

    #region 打开/关闭
    public Task<UIVo?> OpenAsync(string id, object? payload = null, UIOpenOpt? openOpt = null)
    {
        TaskCompletionSource<UIVo?> tcs = new();

        UIRuntimeEntry? entry = _registry.Get(id);
        if (entry == null)
        {
            GD.PushError($"UI 管理器: 打开UI<{id}> 失败, 路由未注册");
            openOpt?.OnFail?.Invoke();
            tcs.SetResult(null);
            return tcs.Task;
        }

        UILayer? layer = ResolveLayer(entry, openOpt, out string? failReason);
        if (failReason != null)
        {
            GD.PushError(failReason);
            openOpt?.OnFail?.Invoke();
            tcs.SetResult(null);
            return tcs.Task;
        }

        UIOpenOpt finalOpt = MergeOpenOpt(entry, layer, openOpt);

        if (finalOpt.SkipOpenCheck?.Invoke(payload) == true)
        {
            finalOpt.OnFail?.Invoke();
            tcs.SetResult(null);
            return tcs.Task;
        }

        if (!UIVoMap.TryGetValue(id, out UIVo? vo))
        {
            vo = new UIVo(id, entry.Type, payload, this, _handlers);
            UIVoMap[id] = vo;
        }

        vo.OpenOpt?.OnFail?.Invoke();
        vo.Payload = payload;

        Action<IUIVoHandle>? userOnOpen = finalOpt.OnOpen;
        Action? userOnFail = finalOpt.OnFail;
        finalOpt.OnOpen = opened =>
        {
            finalOpt.OnOpen = null;
            finalOpt.OnFail = null;
            userOnOpen?.Invoke(opened);
            tcs.TrySetResult(vo);
        };
        finalOpt.OnFail = () =>
        {
            finalOpt.OnOpen = null;
            finalOpt.OnFail = null;
            userOnFail?.Invoke();
            tcs.TrySetResult(null);
        };

        if (entry.Type is EUIType.Pge or EUIType.Pop)
        {
            vo.StateMachine.TransitionTo(EUIState.Load, new UIStateContext(vo, this));
            return tcs.Task;
        }

        EnqueueOpen(vo);
        return tcs.Task;
    }

    public Task<UIVo?> OpenChildAsync(string childId, object? payload = null, UIOpenOpt? openOpt = null)
    {
        string? parent = UIRouteRegistry.GetParent(childId);
        if (parent == null)
        {
            GD.PushError($"UI 管理器: 打开子UI<{childId}> 失败，无父路由。回退为普通 OpenAsync");
            return OpenAsync(childId, payload, openOpt);
        }

        HashSet<string> visited = [];
        string cur = childId;
        object? voForCur = payload ?? new object();
        UIOpenOpt optForRoot = openOpt ?? DefaultUIOpenOpt.Value;

        while (true)
        {
            if (!visited.Add(cur))
            {
                GD.PushError($"UI 管理器: 路由存在环，终止于：<{childId}> 。");
                return Task.FromResult<UIVo?>(null);
            }

            UIRouteMeta? curMeta = UIRouteRegistry.GetMeta(cur);
            string? curParent = UIRouteRegistry.GetParent(cur);
            if (curParent == null)
            {
                return OpenAsync(cur, voForCur, optForRoot);
            }

            Dictionary<string, object?> voDict = curMeta?.ParentOpenPayload as Dictionary<string, object?> ?? [];
            if (voDict.ContainsKey("pgeId"))
            {
                voDict["pgeId"] = cur;
            }

            string? grand = UIRouteRegistry.GetParent(curParent);
            if (grand == null)
            {
                UIOpenOpt finalOpt = curMeta?.ParentOpenOpt?.Clone() ?? DefaultUIOpenOpt.Value;
                MergeInto(finalOpt, optForRoot);
                return OpenAsync(curParent, voForCur, finalOpt);
            }

            cur = curParent;
            voForCur = voDict;
        }
    }

    public void Close(string id)
    {
        if (!UIVoMap.TryGetValue(id, out UIVo? vo))
        {
            return;
        }

        if (_currOpening == vo)
        {
            _currOpening = null;
        }

        RemoveFromOpenQueue(vo);

        if (!vo.IsClose)
        {
            vo.StateMachine.TransitionTo(EUIState.Close, new UIStateContext(vo, this));
        }

        OpenNext();
    }

    public void CloseAllPop()
    {
        foreach (var vo in UIVoMap.Values)
        {
            if (vo.IsOpen && vo.Type == EUIType.Pop)
            {
                Close(vo.Id);
            }
        }
    }

    public void CloseAllByType(EUIType type)
    {
        foreach (var vo in UIVoMap.Values)
        {
            if (vo.IsOpen && vo.Type == type)
            {
                Close(vo.Id);
            }
        }
    }

    public void CloseAllExclude(IReadOnlyList<string>? excludeIds = null)
    {
        excludeIds ??= [];
        foreach (var vo in UIVoMap.Values)
        {
            if (!vo.IsOpen || vo.Layer?.IsTop == true)
            {
                continue;
            }

            if (excludeIds.Contains(vo.Id))
            {
                continue;
            }

            Close(vo.Id);
        }
    }
    #endregion

    #region 查询
    public UIVo? GetUIVo(string id)
    {
        return UIVoMap.TryGetValue(id, out UIVo? vo) ? vo : null;
    }

    public BaseWin? GetWin(string id)
    {
        UIVo? vo = GetUIVo(id);
        return vo != null && vo.IsOpen ? vo.UI : null;
    }

    public UILayer? GetLayer(EUILayer id)
    {
        return LayerMap.TryGetValue(id, out UILayer? layer) ? layer : null;
    }

    public bool IsUITop(string Id)
    {
        UIVo? vo = GetUIVo(Id);
        if (vo?.Layer == null)
        {
            return false;
        }

        int layerIdx = Array.IndexOf(_layers, vo.Layer.Type);
        for (int i = layerIdx + 1; i < _layers.Length; i++)
        {
            UILayer? layer = GetLayer(_layers[i]);
            if (layer != null && layer.UISort.Any(ui => ui.UIVo?.OpenOpt.NoCover != true))
            {
                return false;
            }
        }

        IReadOnlyList<BaseWin> layerUIs = [.. vo.Layer.UISort.Where(ui => ui.UIVo?.OpenOpt.NoCover != true)];

        return layerUIs.Count > 0 && layerUIs[^1].UIId == Id;
    }

    public string GetPath(string id)
    {
        return _registry.Get(id)?.ScenePath ?? "";
    }

    #endregion

    #region 内部方法 队列/层级/缓存
    internal void OpenNext()
    {
        if (_currOpening != null)
        {
            switch (_currOpening.StateMachine.CurrentState)
            {
                case EUIState.Load:
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now - _currOpening.LoadTime > UIConsts.UI_LOAD_TIMEOUT)
                    {
                        _currOpening.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(_currOpening, this));
                        _currOpening = null;
                        OpenNext();
                    }

                    return;
                case EUIState.PreLoad:
                    long now2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now2 - _currOpening.LoadTime > UIConsts.UI_LOAD_TIMEOUT)
                    {
                        _currOpening.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(_currOpening, this));
                        _currOpening = null;
                        OpenNext();
                    }

                    return;
                default:
                    _currOpening = null;
                    OpenNext();
                    return;
            }
        }

        while (_openQueue.Count > 0)
        {
            UIVo vo = _openQueue.Peek();
            _currOpening = vo;
            vo.StateMachine.TransitionTo(EUIState.Load, new UIStateContext(vo, this));
            return;
        }
    }

    internal void UpdateLayers()
    {
        CallDeferred(MethodName.UpdateLayersDeferred);
    }

    private void UpdateLayersDeferred()
    {
        bool hide = false;
        object hideKey = "hideBelow";
        for (int i = _allLayers.Length - 1; i >= 0; i--)
        {
            UILayer? layer = GetLayer(_allLayers[i]);
            if (layer == null)
            {
                continue;
            }

            layer.HideBool.Set(hideKey, hide);

            IReadOnlyList<BaseWin> uis = layer.UISort;
            for (int j = uis.Count - 1; j >= 0; j--)
            {
                UIVo? vo = uis[j].UIVo;
                if (vo == null)
                {
                    continue;
                }

                vo.HideBool.Set(hideKey, hide);
                if (!hide)
                {
                    hide = vo.OpenOpt.HideBelow;
                }
            }
        }
    }

    private void TickCacheDestroy()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<string> toDestroy = new();

        foreach (var (id, vo) in UIVoMap)
        {
            if (vo.StateMachine.CurrentState != EUIState.Cache || vo.DestroyTime == -1)
            {
                continue;
            }

            if (now >= vo.DestroyTime)
            {
                toDestroy.Add(id);
            }
        }

        foreach (var id in toDestroy)
        {
            UIVoMap[id].StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(UIVoMap[id], this));
        }
    }

    private void EnqueueOpen(UIVo vo)
    {
        UIVo[] arr = _openQueue.ToArray();
        _openQueue.Clear();

        foreach (var item in arr)
        {
            if (item != vo)
            {
                _openQueue.Enqueue(item);
            }
        }

        if (_currOpening == vo)
        {
            _currOpening = null;
        }

        _openQueue.Enqueue(vo);
        OpenNext();
    }

    private void RemoveFromOpenQueue(UIVo vo)
    {
        UIVo[] arr = _openQueue.ToArray();
        _openQueue.Clear();

        foreach (var item in arr)
        {
            if (item != vo)
            {
                _openQueue.Enqueue(item);
            }
        }
    }
    #endregion

    #region 初始化辅助
    private static IEnumerable<IStateHandler<EUIState, IUIStateContext>> CreateDefaultStateHandlers()
    {
        yield return new UILoadStateHandler();
        yield return new UIPreLoadStateHandler();
        yield return new UICreateStateHandler();
        yield return new UIOpenStateHandler();
        yield return new UICloseStateHandler();
        yield return new UICloseDoneStateHandler();
        yield return new UIDestroyStateHandler();
    }

    private void BuildLayers(
        Control root,
        IEnumerable<EUILayer> layers,
        Dictionary<EUILayer, UIOpenOpt>? openOpts,
        bool isTop)
    {
        foreach (EUILayer l in layers)
        {
            openOpts ??= [];
            openOpts.TryGetValue(l, out UIOpenOpt? opt);
            UILayer layer = new(l.ToString(), l, opt ?? DefaultUIOpenOpt.Value, isTop);
            LayerMap[l] = layer;
            root.AddChild(layer);
            layer.SetAnchorsPreset(LayoutPreset.FullRect);
        }
    }

    private static Control CreateFullScreenRoot(string name)
    {
        Control root = new()
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorsPreset = (int)LayoutPreset.FullRect,
        };
        return root;
    }

    private UILayer? ResolveLayer(UIRuntimeEntry entry, UIOpenOpt? openOpt, out string? failReason)
    {
        failReason = null;
        switch (entry.Type)
        {
            case EUIType.Win:
            case EUIType.Dlg:
                {
                    EUILayer? layerId = openOpt?.Layer ?? entry.OpenOpt?.Layer ?? entry.BaseOpenOpt?.Layer ?? DefaultUIOpenOpt.Value.Layer!;
                    UILayer? layer = GetLayer((EUILayer)layerId);
                    if (layer == null)
                    {
                        failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，层级<{layerId}> 不存在。";
                    }
                    return layer;
                }
            case EUIType.Pge:
                if (openOpt?.Parent == null)
                {
                    failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，父UI不存在。";
                }
                return null;
            case EUIType.Pop:
                {
                    if (openOpt?.Pop?.Target == null)
                    {
                        failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，气泡参数Target不存在。";
                        return null;
                    }

                    EUILayer? layerId = openOpt.Layer ?? entry.OpenOpt?.Layer ?? entry.BaseOpenOpt?.Layer;
                    if (layerId != null)
                    {
                        return GetLayer((EUILayer)layerId);
                    }

                    return FindLayer(openOpt.Pop.Target) ?? GetLayer(EUILayer.Pop);
                }
            default:
                failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，类型<{entry.Type}> 不支持打开。";
                return null;
        }
    }

    private static UIOpenOpt MergeOpenOpt(UIRuntimeEntry entry, UILayer? layer, UIOpenOpt? openOpt)
    {
        UIOpenOpt result = DefaultUIOpenOpt.Value.Clone();
        MergeInto(result, entry.BaseOpenOpt);
        MergeInto(result, layer?.OpenOpt);
        MergeInto(result, entry.OpenOpt);
        MergeInto(result, openOpt);

        if (layer != null)
        {
            result.Layer = layer.Type;
        }

        return result;
    }

    private static void MergeInto(UIOpenOpt target, UIOpenOpt? source)
    {
        if (source == null)
        {
            return;
        }
        if (source.Layer != null) target.Layer = source.Layer;
        if (source.Parent != null) target.Parent = source.Parent;
        target.CacheTime = source.CacheTime;
        target.AnimType = source.AnimType;
        target.HideBelow = source.HideBelow;
        target.NoCover = source.NoCover;
        target.Align = source.Align;
        if (source.Mask != null) target.Mask = source.Mask;
        if (source.Pop != null) target.Pop = source.Pop;
        if (source.SkipOpenCheck != null) target.SkipOpenCheck = source.SkipOpenCheck;
        if (source.OnOpenBefore != null) target.OnOpenBefore = source.OnOpenBefore;
        if (source.OnOpen != null) target.OnOpen = source.OnOpen;
        if (source.OnFail != null) target.OnFail = source.OnFail;
        if (source.PreLoadResList != null) target.PreLoadResList = source.PreLoadResList;
    }

    private static UILayer? FindLayer(Node node)
    {
        Node? cur = node;
        while (cur != null)
        {
            if (cur is UILayer layer)
            {
                return layer;
            }
            cur = cur.GetParent();
        }
        return null;
    }
    #endregion
}

/// <summary>
/// UI 管理器初始化选项
/// </summary>
public sealed class UIManagerInitOpt
{
    public required UIRuntimeRegistry Registry { get; init; }
    public Control? StageRoot { get; init; }
    public Control? UIRoot { get; init; }
    public Control? UITopRoot { get; init; }
    public required IEnumerable<EUILayer> Layers { get; init; }
    public required IEnumerable<EUILayer> TopLayers { get; init; }
    public Dictionary<EUILayer, UIOpenOpt>? LayerOpenOpts { get; init; }
    public EventDispatcher? EventDispatcher { get; init; }
    public IEnumerable<IStateHandler<EUIState, IUIStateContext>>? StateHandlers { get; init; }
}

public sealed class UIStateContext(UIVo vo, UIManager manager) : IUIStateContext
{
    public UIVo UIVo { get; } = vo;
    public UIManager UIManager { get; } = manager;
    public void OpenNext() => OpenNextFunc();

    private void OpenNextFunc()
    {
        manager.OpenNext();
    }
}

public static class UIEvent
{
    public static readonly EventKey<UIOpenPayload> Open = new((int)EUIEvent.Open);
    public static readonly EventKey<UIClosePayload> Close = new((int)EUIEvent.Close);
}