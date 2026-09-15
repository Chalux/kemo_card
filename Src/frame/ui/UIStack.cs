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

    /// <summary>
    /// 移除指定界面的全部记录。关闭界面时调用（幂等：界面不在栈上时无操作，
    /// 因此 <see cref="Back"/> 已经弹过的 id 再被关闭不会误删下层界面）。
    /// </summary>
    public bool Remove(string uiId)
    {
        return _stack.RemoveAll(id => string.Equals(id, uiId, StringComparison.Ordinal)) > 0;
    }

    public bool Contains(string uiId)
    {
        return _stack.Contains(uiId);
    }
}