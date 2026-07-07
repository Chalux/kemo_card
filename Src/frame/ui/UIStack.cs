namespace KemoCard.Frame.UI;

/// <summary>
/// UI 导航栈：支持层级式打开/返回操作。
/// 当 FullScreen UI（HideBelow = true）打开时自动压栈，Back() 时关闭并恢复下层。
/// </summary>
public sealed class UIStack
{
    private readonly List<string> _stack = [];

    public int Count => _stack.Count;
    public bool IsEmpty => _stack.Count == 0;
    public string? Top => _stack.Count > 0 ? _stack[^1] : null;

    public void Push(string uiId)
    {
        _stack.Add(uiId);
    }

    public string? Pop()
    {
        if (_stack.Count == 0) return null;
        string id = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return id;
    }

    public string? Back()
    {
        if (_stack.Count < 2) return null;
        _stack.RemoveAt(_stack.Count - 1);
        return _stack[^1];
    }

    public void Clear()
    {
        _stack.Clear();
    }

    public bool Contains(string uiId)
    {
        return _stack.Contains(uiId);
    }
}
