namespace KemoCard.Frame.Content.Keywords;

/// <summary>
/// 词条注册表。同 id 后写覆盖，并通过 <see cref="WarningHandler"/> 发出警告。
/// </summary>
public sealed class KeywordCatalog
{
    private static readonly KeywordCatalog SharedInstance = new();

    private readonly Dictionary<string, KeywordEntry> _entries = new(StringComparer.Ordinal);

    public static KeywordCatalog Shared => SharedInstance;

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

        if (_entries.ContainsKey(entry.Id))
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

    public void Clear() => _entries.Clear();
}