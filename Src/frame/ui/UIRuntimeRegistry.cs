using Godot;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 运行时路由元数据（嵌套路由）
/// </summary>
public sealed class UIRouteMeta
{
    public string? ParentId { get; set; }
    public UIOpenOpt? ParentOpenOpt { get; set; }
    public object? ParentOpenPayload { get; set; }
}

/// <summary>
/// UI 运行时注册项
/// </summary>
public sealed class UIRuntimeEntry
{
    /// <summary>归属功能 Mod 的 id（必填，见 ui-mod-binding 规格 §5.2）。</summary>
    public required string OwnerModId { get; init; }
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required EUIType Type { get; init; }
    public UIOpenOpt? BaseOpenOpt { get; init; }
    public UIOpenOpt? OpenOpt { get; init; }
    /// <summary>路由元数据（可选，用于父子嵌套路由）</summary>
    public UIRouteMeta? RouteMeta { get; init; }

    public string ScenePath => $"res://{Dir}/{Id}.tscn";
}

/// <summary>
/// UI 运行时注册表，含路由注册、父子路由解析与校验。
/// </summary>
public sealed class UIRuntimeRegistry
{
    private readonly Dictionary<string, UIRuntimeEntry> _map = [];
    private readonly Dictionary<string, string> _parentMap = [];
    private Dictionary<string, List<string>>? _childrenMap;

    /// <summary>
    /// 上次 <see cref="Validate"/> 是否发现致命问题（owner 缺失 / 跨 owner 抢注同名界面 / owner 未登记）。
    /// 启动期应据此断言，避免"注册漏了归属"拖到运行期才暴露。
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>已登记的归属 Mod id 集合（由注册项带下来，供批量关闭与门面解析校验）。</summary>
    public IReadOnlyCollection<string> OwnerModIds =>
        _map.Values.Select(entry => entry.OwnerModId).Distinct(StringComparer.Ordinal).ToList();

    public void Register(UIRuntimeEntry entry)
    {
        // 同一界面 id 被不同功能抢占属于装配错误：后者会静默覆盖前者，双方的生命周期管理都会错乱。
        if (_map.TryGetValue(entry.Id, out var existing) &&
            !string.Equals(existing.OwnerModId, entry.OwnerModId, StringComparison.Ordinal))
        {
            AppLog.Error(
                $"UIRoute: 界面 id {entry.Id} 已被归属 {existing.OwnerModId} 注册，"
                + $"又被 {entry.OwnerModId} 覆盖；同一 id 只能属于一个功能 Mod",
                "UI");
            HasErrors = true;
        }

        _map[entry.Id] = entry;

        var route = entry.RouteMeta;
        if (route != null && !string.IsNullOrEmpty(route.ParentId))
        {
            _parentMap[entry.Id] = route.ParentId;
            _childrenMap = null;
        }
    }

    /// <summary>取某功能 Mod 名下的全部注册项。</summary>
    public IReadOnlyList<UIRuntimeEntry> GetByOwner(string ownerModId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModId);
        return [.. _map.Values.Where(entry => string.Equals(entry.OwnerModId, ownerModId, StringComparison.Ordinal))];
    }

    public void RegisterRange(IEnumerable<UIRuntimeEntry> entries)
    {
        foreach (var entry in entries)
        {
            Register(entry);
        }
    }

    public UIRuntimeEntry? Get(string id)
    {
        return _map.TryGetValue(id, out var entry) ? entry : null;
    }

    public IEnumerable<UIRuntimeEntry> GetAll()
    {
        return _map.Values;
    }

    public IReadOnlyDictionary<string, UIRuntimeEntry> GetAllMap()
    {
        return _map;
    }

    public string? GetParentId(string id)
    {
        return _parentMap.TryGetValue(id, out var parentId) ? parentId : null;
    }

    public IReadOnlyList<string> GetChildren(string id)
    {
        EnsureChildrenMap();
        return (_childrenMap?.TryGetValue(id, out var children) ?? false) ? children : [];
    }

    /// <summary>
    /// 校验全部注册项：归属合法性（ui-mod-binding 规格 §5.3 三条硬校验）+ 路由循环引用与注册一致性。
    /// </summary>
    /// <param name="knownOwnerModIds">
    /// 组合根已装配的功能 Mod id 清单（<c>FeatureModCatalog</c>）。传 null 表示跳过该项校验。
    /// </param>
    /// <returns>是否发现致命问题（同时见 <see cref="HasErrors"/>）。</returns>
    public bool Validate(IReadOnlyCollection<string>? knownOwnerModIds = null)
    {
        HashSet<string> registeredIds = [.. _map.Keys];

        foreach (var (id, entry) in _map)
        {
            // ① 归属必须声明
            if (string.IsNullOrWhiteSpace(entry.OwnerModId))
            {
                AppLog.Error($"UIRoute: 界面 {id} 未声明归属 Mod（OwnerModId），无法按功能管理其生命周期", "UI");
                HasErrors = true;
            }
            // ② 归属必须是已装配的功能 Mod（防拼写错误）
            else if (knownOwnerModIds is not null && !knownOwnerModIds.Contains(entry.OwnerModId))
            {
                AppLog.Error(
                    $"UIRoute: 界面 {id} 声明的归属 Mod {entry.OwnerModId} 未在 FeatureModCatalog 登记",
                    "UI");
                HasErrors = true;
            }

            var route = entry.RouteMeta;
            if (route == null) continue;

            if (!string.IsNullOrEmpty(route.ParentId) && !registeredIds.Contains(route.ParentId))
            {
                AppLog.Error($"UIRoute: 路由 {id} 的父路由 {route.ParentId} 未注册", "UI");
            }
        }

        foreach (string startId in _parentMap.Keys)
        {
            HashSet<string> idSet = [];
            string? cur = startId;
            while (cur != null)
            {
                if (!idSet.Add(cur))
                {
                    AppLog.Error($"UIRoute: 路由 {cur} 存在循环引用", "UI");
                    break;
                }
                cur = GetParentId(cur);
            }
        }

        return HasErrors;
    }

    private void EnsureChildrenMap()
    {
        if (_childrenMap != null) return;

        _childrenMap = [];
        foreach (var (id, parent) in _parentMap)
        {
            if (!_childrenMap.TryGetValue(parent, out List<string>? children))
            {
                children = [];
                _childrenMap[parent] = children;
            }
            children.Add(id);
        }
    }
}