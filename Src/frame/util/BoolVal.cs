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

    public void Destory()
    {
        _sources.Clear();
        OnChange = null;
    }

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