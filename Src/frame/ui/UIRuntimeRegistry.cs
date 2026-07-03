using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI;

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

    public string ScenePath => $"res://{Dir}/{Id}.tscn";
}

/// <summary>
/// UI 运行时注册表
/// </summary>
public sealed class UIRuntimeRegistry
{
    private readonly Dictionary<string, UIRuntimeEntry> _map = [];

    public void Register(UIRuntimeEntry entry)
    {
        _map[entry.Id] = entry;
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
}