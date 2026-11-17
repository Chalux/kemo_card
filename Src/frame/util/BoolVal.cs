namespace KemoCard.Frame.Util;

public sealed class BoolVal
{
    private readonly Dictionary<object, bool> _sources = new();
    private bool _value;
    public bool Value => _value;
    public Action? OnChange { get; set; }

    public void Set(object from, bool value)
    {
        if (_sources.TryGetValue(from, out var existing) && existing == value)
        {
            return;
        }
        _sources[from] = value;
        UpdateValue();
    }

    public void Remove(object from)
    {
        _sources.Remove(from);
        UpdateValue();
    }

    /// <summary>清空全部来源并解除变更回调。</summary>
    public void Destroy()
    {
        _sources.Clear();
        OnChange = null;
    }

    /// <summary>拼写错误的旧名，保留以兼容既有调用点。</summary>
    [Obsolete("拼写错误，请改用 Destroy()。")]
    public void Destory() => Destroy();

    private void UpdateValue()
    {
        bool b = _sources.Values.Any(v => v);
        if (_value == b)
        {
            return;
        }
        _value = b;
        OnChange?.Invoke();
    }
}