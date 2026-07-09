using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
using KemoCard.Frame.Util;
using static Godot.Control;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 管理器接口
/// </summary>
public interface IUIManager
{
    Task<UIVo?> OpenAsync(string id, object? payload = null, UIOpenOpt? openOpt = null);
    Task<UIVo?> OpenAsync<TPayload>(UiId<TPayload> id, TPayload payload, UIOpenOpt? openOpt = null);
    Task<UIVo?> OpenChildAsync(string childId, object? payload = null, UIOpenOpt? openOpt = null);
    void Close(string id);
    void CloseAllPop();
    void CloseAllByType(EUIType type);
    void CloseAllExclude(IReadOnlyList<string>? excludeIds = null);
    UIVo? GetUIVo(string id);
    BaseWin? GetWin(string id);
    T? GetWin<T>(string id) where T : BaseWin;
    UILayer? GetLayer(EUILayer layer);
    bool IsUITop(string Id);
    string GetPath(string id);

    /// <summary>UI 导航栈</summary>
    UIStack NavStack { get; }
    /// <summary>返回上一级 UI（关闭当前，恢复上一个）</summary>
    Task<UIVo?> BackAsync();
}

/// <summary>
/// UI 管理器：门面角色，委托给 UILayerManager / UIVoRegistry / UIOpenCoordinator 等子组件。
/// </summary>
public partial class UIManager : Node, IUIManager
{
    public static UIManager? Instance { get; private set; }

    public EventDispatcher EventDispatcher { get; } = new();
    public UIStack NavStack { get; } = new();

    internal UILayerManager LayerManager { get; } = new();
    internal UIVoRegistry VoRegistry { get; private set; } = null!;
    internal UIOpenCoordinator OpenCoordinator { get; private set; } = null!;

    private UIRuntimeRegistry _registry = null!;
    private bool _inited;
    private EUILayer[] _layers = [];
    private GodotMainThreadSyncContext? _syncContext;

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        _syncContext?.Uninstall();
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        _syncContext?.Pump();
        if (!_inited) return;

        OpenCoordinator.CheckLoadTimeout();
        VoRegistry.TickCacheDestroy(delta);
    }

    public void Init(UIManagerInitOpt opt)
    {
        if (_inited)
        {
            GD.PushError("UI 管理器已初始化，请勿重复初始化.");
            return;
        }

        _inited = true;
        Instance = this;

        _syncContext = new GodotMainThreadSyncContext();
        _syncContext.Install();

        _registry = opt.Registry;
        _layers = [.. opt.Layers];

        var handlers = opt.StateHandlers ?? CreateDefaultStateHandlers();
        VoRegistry = new UIVoRegistry(this, handlers);
        OpenCoordinator = new UIOpenCoordinator(this);

        Control uiRoot = opt.UIRoot ?? CreateFullScreenRoot("UIRoot");
        Control uiTopRoot = opt.UITopRoot ?? CreateFullScreenRoot("UITopRoot");

        if (uiRoot.GetParent() == null) opt.StageRoot?.AddChild(uiRoot);
        if (uiTopRoot.GetParent() == null) opt.StageRoot?.AddChild(uiTopRoot);

        LayerManager.Init(uiRoot, uiTopRoot, [.. opt.Layers], [.. opt.TopLayers], opt.LayerOpenOpts);

        _registry.Validate();
    }

    #region 打开/关闭
    public Task<UIVo?> OpenAsync<TPayload>(UiId<TPayload> id, TPayload? payload = default, UIOpenOpt? openOpt = null)
    {
        return OpenAsync(id.Value, payload, openOpt);
    }

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

        UIVo vo = VoRegistry.GetOrCreate(id, entry.Type, payload);

        // 重用已有 VO：优雅结束上一轮未完成的打开任务
        vo.OpenTaskSource?.TrySetResult(vo);
        vo.OpenTaskSource = null;

        vo.Payload = payload;
        vo.OpenTaskSource = tcs;

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

        OpenCoordinator.EnqueueOpen(vo);
        return tcs.Task;
    }

    public Task<UIVo?> OpenChildAsync(string childId, object? payload = null, UIOpenOpt? openOpt = null)
    {
        string? parent = _registry.GetParentId(childId);
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

            UIRouteMeta? curMeta = _registry.Get(cur)?.RouteMeta;
            string? curParent = _registry.GetParentId(cur);
            if (curParent == null)
            {
                return OpenAsync(cur, voForCur, optForRoot);
            }

            Dictionary<string, object?> voDict = curMeta?.ParentOpenPayload as Dictionary<string, object?> ?? [];
            if (voDict.ContainsKey("pgeId"))
            {
                voDict["pgeId"] = cur;
            }

            string? grand = _registry.GetParentId(curParent);
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
        UIVo? vo = VoRegistry.Get(id);
        if (vo == null) return;

        OpenCoordinator.ResetCurrentOpening(vo);
        OpenCoordinator.RemoveFromQueue(vo);

        if (vo.Lifecycle.OpenTime == 0)
        {
            vo.OpenTaskSource?.TrySetResult(null);
            vo.OpenTaskSource = null;
        }

        if (!vo.IsClose)
        {
            vo.StateMachine.TransitionTo(EUIState.Close, new UIStateContext(vo, this));
        }

        OpenCoordinator.OpenNext();
    }

    public void CloseAllPop()
    {
        foreach (var vo in VoRegistry.Map.Values)
        {
            if (vo.IsOpen && vo.Type == EUIType.Pop)
                Close(vo.Id);
        }
    }

    public void CloseAllByType(EUIType type)
    {
        foreach (var vo in VoRegistry.Map.Values)
        {
            if (vo.IsOpen && vo.Type == type)
                Close(vo.Id);
        }
    }

    public void CloseAllExclude(IReadOnlyList<string>? excludeIds = null)
    {
        excludeIds ??= [];
        foreach (var vo in VoRegistry.Map.Values)
        {
            if (!vo.IsOpen || vo.Runtime.Layer?.IsTop == true) continue;
            if (excludeIds.Contains(vo.Id)) continue;
            Close(vo.Id);
        }
    }

    public async Task<UIVo?> BackAsync()
    {
        if (NavStack.Count < 2) return null;

        string? currentId = NavStack.Top;
        string? prevId = NavStack.Back();

        if (currentId != null) Close(currentId);

        UIVo? existing = VoRegistry.Get(prevId!);
        if (existing != null && existing.IsOpen) return existing;

        UIRuntimeEntry? entry = _registry.Get(prevId!);
        if (entry == null) return null;

        return await OpenAsync(prevId!, null, null);
    }
    #endregion

    #region 查询
    public UIVo? GetUIVo(string id) => VoRegistry.Get(id);

    public BaseWin? GetWin(string id)
    {
        UIVo? vo = VoRegistry.Get(id);
        return vo != null && vo.IsOpen ? vo.Runtime.UI : null;
    }

    public T? GetWin<T>(string id) where T : BaseWin
    {
        UIVo? vo = VoRegistry.Get(id);
        return vo != null && vo.IsOpen ? vo.Runtime.UI as T : null;
    }

    public UILayer? GetLayer(EUILayer id) => LayerManager.GetLayer(id);

    public bool IsUITop(string Id)
    {
        UIVo? vo = VoRegistry.Get(Id);
        if (vo?.Runtime.Layer == null) return false;

        int layerIdx = Array.IndexOf(_layers, vo.Runtime.Layer.Type);
        for (int i = layerIdx + 1; i < _layers.Length; i++)
        {
            UILayer? layer = LayerManager.GetLayer(_layers[i]);
            if (layer != null && layer.UISort.Any(ui => ui.UIVo?.OpenOpt.NoCover != true))
                return false;
        }

        IReadOnlyList<BaseWin> layerUIs = [.. vo.Runtime.Layer.UISort.Where(ui => ui.UIVo?.OpenOpt.NoCover != true)];
        return layerUIs.Count > 0 && layerUIs[^1].UIId == Id;
    }

    public string GetPath(string id) => _registry.Get(id)?.ScenePath ?? "";
    #endregion

    #region 内部方法
    internal void OpenNext() => OpenCoordinator.OpenNext();
    internal void UpdateLayers() => CallDeferred(MethodName.UpdateLayersDeferred);

    private void UpdateLayersDeferred() => LayerManager.UpdateLayers();

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

    private UILayer? ResolveLayer(UIRuntimeEntry entry, UIOpenOpt? openOpt, out string? failReason)
    {
        failReason = null;
        switch (entry.Type)
        {
            case EUIType.Win:
            case EUIType.Dlg:
                {
                    EUILayer? layerId = openOpt?.Layer ?? entry.OpenOpt?.Layer ?? entry.BaseOpenOpt?.Layer ?? DefaultUIOpenOpt.Value.Layer!;
                    UILayer? layer = LayerManager.GetLayer((EUILayer)layerId);
                    if (layer == null)
                        failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，层级<{layerId}> 不存在。";
                    return layer;
                }
            case EUIType.Pge:
                if (openOpt?.Parent == null)
                    failReason = $"UI 管理器: 打开UI<{entry.Id}> 失败，父UI不存在。";
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
                        return LayerManager.GetLayer((EUILayer)layerId);
                    return UILayerManager.FindLayerForNode(openOpt.Pop.Target) ?? LayerManager.GetLayer(EUILayer.Pop);
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
        if (layer != null) result.Layer = layer.Type;
        return result;
    }

    private static void MergeInto(UIOpenOpt target, UIOpenOpt? source)
    {
        if (source == null) return;
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

    private static Control CreateFullScreenRoot(string name)
    {
        var root = new Control
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // C# 中赋值 AnchorsPreset 属性无效，必须用 SetAnchorsAndOffsetsPreset
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return root;
    }
    #endregion
}

public sealed class UIManagerInitOpt
{
    public required UIRuntimeRegistry Registry { get; init; }
    public Control? StageRoot { get; init; }
    public Control? UIRoot { get; init; }
    public Control? UITopRoot { get; init; }
    public required IEnumerable<EUILayer> Layers { get; init; }
    public required IEnumerable<EUILayer> TopLayers { get; init; }
    public Dictionary<EUILayer, UIOpenOpt>? LayerOpenOpts { get; init; }
    public IEnumerable<IStateHandler<EUIState, IUIStateContext>>? StateHandlers { get; init; }
}

public sealed class UIStateContext(UIVo vo, UIManager manager) : IUIStateContext
{
    public UIVo UIVo { get; } = vo;
    public UIManager UIManager { get; } = manager;
    public void OpenNext() => UIManager.OpenNext();
}

public static class UIEvent
{
    public static readonly EventKey<UIOpenPayload> Open = new((int)EUIEvent.Open);
    public static readonly EventKey<UIClosePayload> Close = new((int)EUIEvent.Close);
}
