namespace KemoCard.Frame.Content.Keywords;

/// <summary>
/// 词条注册表。同 id 后写覆盖，并通过 <see cref="WarningHandler"/> 发出警告。
/// </summary>
public sealed class KeywordCatalog
{
    private static readonly KeywordCatalog SharedInstance = new();

    private readonly Dictionary<string, KeywordEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>注册顺序（词典按机制引入的先后展示，比字典序可读）。</summary>
    private readonly List<string> _order = [];

    public static KeywordCatalog Shared => SharedInstance;

    /// <summary>
    /// 全部词条，按注册顺序（词典/图鉴等只读展示用；覆盖注册保持首次出现的位置）。
    /// </summary>
    public IReadOnlyList<KeywordEntry> Entries
    {
        get
        {
            var entries = new List<KeywordEntry>(_order.Count);
            foreach (var id in _order)
            {
                if (_entries.TryGetValue(id, out var entry))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
    }

    /// <summary>冲突覆盖等警告回调；运行时可接到 AppLog.Warning。</summary>
    public Action<string>? WarningHandler { get; set; }

    public void Register(KeywordEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            WarningHandler?.Invoke("KeywordCatalog: 拒绝注册空 id 的词条。");
            return;
        }

        if (!_entries.ContainsKey(entry.Id))
        {
            _order.Add(entry.Id);
        }
        else
        {
            WarningHandler?.Invoke($"KeywordCatalog: 词条 id '{entry.Id}' 已存在，将被覆盖。");
        }

        _entries[entry.Id] = entry;
    }

    public bool TryRegister(KeywordEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return false;
        }

        var existed = _entries.ContainsKey(entry.Id);
        Register(entry);
        return !existed;
    }

    public void Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _entries.Remove(id);
        _order.Remove(id);
    }

    public bool TryGet(string id, out KeywordEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            entry = null;
            return false;
        }

        if (_entries.TryGetValue(id, out var found))
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    public void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }
}