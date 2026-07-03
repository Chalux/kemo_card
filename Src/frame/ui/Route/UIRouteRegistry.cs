using Godot;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Route;

/// <summary>
/// UI 路由元数据
/// </summary>
public class UIRouteMeta
{
    public string? Id { get; set; }
    public string? Parent { get; set; }
    public UIOpenOpt? ParentOpenOpt { get; set; }
    public object? ParentOpenPayload { get; set; }
}

public static class UIRouteRegistry
{
    private static readonly Dictionary<string, UIRouteMeta> _routeMap = [];
    private static readonly Dictionary<string, string> _parentMap = [];
    private static Dictionary<string, List<string>>? _childrenMap;

    public static void Register(string id, UIRouteMeta meta)
    {
        meta.Id = id;
        _routeMap[id] = meta;
        if (!string.IsNullOrEmpty(meta.Parent))
        {
            _parentMap[id] = meta.Parent!;
            _childrenMap = null;
        }
    }

    public static UIRouteMeta? GetMeta(string id)
    {
        return _routeMap.TryGetValue(id, out var meta) ? meta : null;
    }

    public static string? GetParent(string id)
    {
        return _parentMap.TryGetValue(id, out var parent) ? parent : null;
    }

    public static IReadOnlyList<string> GetChildren(string id)
    {
        EnsureChildrenMap();
        return (_childrenMap?.TryGetValue(id, out var children) ?? false) ? children : [];
    }

    public static IReadOnlyDictionary<string, UIRouteMeta> GetRouteMap()
    {
        return _routeMap;
    }

    public static void Validate(IReadOnlyCollection<string> registeredIds)
    {
        HashSet<string> visited = registeredIds as HashSet<string> ?? [.. registeredIds];
        foreach (var (id, meta) in _routeMap)
        {
            if (!visited.Contains(id))
            {
                GD.PushError($"UIRoute: 路由 {id} 未注册");
            }
            if (!string.IsNullOrEmpty(meta.Parent) && !registeredIds.Contains(meta.Parent!))
            {
                GD.PushError($"UIRoute: 路由 {id} 的父路由 {meta.Parent!} 未注册");
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
                cur = GetParent(cur);
            }
        }
    }

    private static void EnsureChildrenMap()
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