using Godot;
using KemoCard.Frame.UI.Def;

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

    public void Register(UIRuntimeEntry entry)
    {
        _map[entry.Id] = entry;

        var route = entry.RouteMeta;
        if (route != null && !string.IsNullOrEmpty(route.ParentId))
        {
            _parentMap[entry.Id] = route.ParentId;
            _childrenMap = null;
        }
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
    /// 校验所有路由：检查循环引用、注册一致性。
    /// </summary>
    public void Validate()
    {
        HashSet<string> registeredIds = [.. _map.Keys];

        foreach (var (id, entry) in _map)
        {
            var route = entry.RouteMeta;
            if (route == null) continue;

            if (!string.IsNullOrEmpty(route.ParentId) && !registeredIds.Contains(route.ParentId))
            {
                GD.PushError($"UIRoute: 路由 {id} 的父路由 {route.ParentId} 未注册");
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
                    GD.PushError($"UIRoute: 路由 {cur} 存在循环引用");
                    break;
                }
                cur = GetParentId(cur);
            }
        }
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