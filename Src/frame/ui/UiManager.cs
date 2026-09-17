using Godot;
using KemoCard.Frame.Logging;
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

    /// <summary>
    /// 归属门面提供者：界面通过它取「自己功能」的门面（见 ui-mod-binding 规格 §5.4）。
    /// 由组合根注入；未注入时 <see cref="UIVo.OwnerModId"/> 相关取数会明确失败而非静默返回 null。
    /// </summary>
    public IUiFacadeProvider? FacadeProvider { get; private set; }

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
            AppLog.Error("UI 管理器已初始化，请勿重复初始化.", "UI");
            return;
        }

        _inited = true;
        Instance = this;

        _syncContext = new GodotMainThreadSyncContext();
        _syncContext.Install();

        _registry = opt.Registry;
        _layers = [.. opt.Layers];
        FacadeProvider = opt.FacadeProvider;

        var handlers = opt.StateHandlers ?? CreateDefaultStateHandlers();
        VoRegistry = new UIVoRegistry(this, handlers);
        OpenCoordinator = new UIOpenCoordinator(this);

        Control uiRoot = opt.UIRoot ?? CreateFullScreenRoot("UIRoot");
        Control uiTopRoot = opt.UITopRoot ?? CreateFullScreenRoot("UITopRoot");

        if (uiRoot.GetParent() == null) opt.StageRoot?.AddChild(uiRoot);
        if (uiTopRoot.GetParent() == null) opt.StageRoot?.AddChild(uiTopRoot);

        LayerManager.Init(uiRoot, uiTopRoot, [.. opt.Layers], [.. opt.TopLayers], opt.LayerOpenOpts);

        // 归属校验需要组合根的已装配功能 Mod 清单（见 ui-mod-binding 规格 §5.3）。
        _registry.Validate(opt.KnownOwnerModIds);
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
            AppLog.Error($"UI 管理器: 打开UI<{id}> 失败, 路由未注册", "UI");
            openOpt?.OnFail?.Invoke();
            tcs.SetResult(null);
            return tcs.Task;
        }

        UILayer? layer = ResolveLayer(entry, openOpt, out string? failReason);
        if (failReason != null)
        {
            AppLog.Error(failReason, "UI");
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

        UIVo vo = VoRegistry.GetOrCreate(id, entry.Type, entry.OwnerModId, payload);

        // 必须写回：状态处理器（层级挂载 / 遮罩 / 动画 / 缓存 / 回调）统一读 vo.OpenOpt，
        // 不写回会让 MergeOpenOpt 的结果被丢弃，整个 UIOpenOpt 参数体系失效，且 await OpenAsync 永不返回。
        vo.OpenOpt = finalOpt;

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
            AppLog.Error($"UI 管理器: 打开子UI<{childId}> 失败，无父路由。回退为普通 OpenAsync", "UI");
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
                AppLog.Error($"UI 管理器: 路由存在环，终止于：<{childId}> 。", "UI");
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
                // 必须 Clone：DefaultUIOpenOpt.Value 是静态共享实例，直接当合并 target 会跨次打开污染全局默认值。
                UIOpenOpt finalOpt = curMeta?.ParentOpenOpt?.Clone() ?? DefaultUIOpenOpt.Value.Clone();
                finalOpt.MergeFrom(optForRoot);
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
        // 快照：Close → Destroy 会同步从 VoRegistry 删除条目，直接枚举 Map.Values 会抛
        // InvalidOperationException: Collection was modified。
        foreach (var vo in SnapshotOpenVos())
        {
            if (vo.Type == EUIType.Pop)
                Close(vo.Id);
        }
    }

    public void CloseAllByType(EUIType type)
    {
        foreach (var vo in SnapshotOpenVos())
        {
            if (vo.Type == type)
                Close(vo.Id);
        }
    }

    public void CloseAllExclude(IReadOnlyList<string>? excludeIds = null)
    {
        excludeIds ??= [];
        foreach (var vo in SnapshotOpenVos())
        {
            if (vo.Runtime.Layer?.IsTop == true) continue;
            if (excludeIds.Contains(vo.Id)) continue;
            Close(vo.Id);
        }
    }

    /// <summary>
    /// 取出当前已打开界面的快照，供批量关闭使用（关闭过程中注册表会被修改）。
    /// </summary>
    private List<UIVo> SnapshotOpenVos()
    {
        List<UIVo> result = [];
        foreach (var vo in VoRegistry.Map.Values)
        {
            if (vo.IsOpen)
            {
                result.Add(vo);
            }
        }
        return result;
    }

    /// <summary>
    /// 取出某功能 Mod 名下全部界面（含已关闭仍在缓存的），供批量关闭 / 注销使用。
    /// </summary>
    private List<UIVo> SnapshotVosByOwner(string ownerModId)
    {
        List<UIVo> result = [];
        foreach (var vo in VoRegistry.Map.Values)
        {
            if (string.Equals(vo.OwnerModId, ownerModId, StringComparison.Ordinal))
            {
                result.Add(vo);
            }
        }
        return result;
    }

    /// <summary>
    /// 关闭某功能 Mod 的全部界面（见 ui-mod-binding 规格 §6.1）。
    /// </summary>
    /// <param name="ownerModId">功能 Mod id。</param>
    /// <param name="destroy">
    /// <c>true</c> 表示关闭即销毁、不留缓存（决策 1：Run 结束时销毁其界面）；
    /// <c>false</c> 走常规缓存语义。
    /// </param>
    public void CloseByOwner(string ownerModId, bool destroy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);

        foreach (var vo in SnapshotOpenVos())
        {
            if (!string.Equals(vo.OwnerModId, ownerModId, StringComparison.Ordinal))
            {
                continue;
            }

            if (destroy)
            {
                // OpenOpt 是每个 UIVo 自己的克隆（见 OpenAsync 的写回），改它不会污染注册表默认值。
                vo.OpenOpt.CacheTime = 0;
            }

            Close(vo.Id);
        }
    }

    /// <summary>
    /// 注销某功能 Mod 的全部界面（连 <see cref="UIVo"/> 一并移除），防止 Mod 卸载后残留缓存实例。
    /// 解绑订阅由各节点离场时的 <c>BindingScope</c> 负责，此处无需额外清理。
    /// </summary>
    public void UnregisterOwner(string ownerModId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);

        foreach (var vo in SnapshotVosByOwner(ownerModId))
        {
            if (vo.IsOpen)
            {
                vo.OpenOpt.CacheTime = 0;
                Close(vo.Id);
                continue;
            }

            if (vo.StateMachine.CurrentState != EUIState.Destroy)
            {
                vo.StateMachine.TransitionTo(EUIState.Destroy, new UIStateContext(vo, this));
            }
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
            if (layer != null && layer.UISort.Any(ui => ui.UIVo?.OpenOpt.EffectiveNoCover != true))
                return false;
        }

        IReadOnlyList<BaseWin> layerUIs = [.. vo.Runtime.Layer.UISort.Where(ui => ui.UIVo?.OpenOpt.EffectiveNoCover != true)];
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
        var result = DefaultUIOpenOpt.Value.Clone();
        result.MergeFrom(entry.BaseOpenOpt);
        result.MergeFrom(layer?.OpenOpt);
        result.MergeFrom(entry.OpenOpt);
        result.MergeFrom(openOpt);
        if (layer != null) result.Layer = layer.Type;
        return result;
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

    /// <summary>归属门面提供者（组合根注入；见 ui-mod-binding 规格 §5.4）。</summary>
    public IUiFacadeProvider? FacadeProvider { get; init; }

    /// <summary>组合根已装配的功能 Mod id 清单，用于归属校验（见规格 §5.3 ②）。传 null 跳过该项校验。</summary>
    public IReadOnlyCollection<string>? KnownOwnerModIds { get; init; }
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