namespace KemoCard.Frame.Condition;

public sealed class ConditionRegistry<TContext>
{
    private readonly Dictionary<string, ICondTypeHandler<TContext>> _handlers = new(StringComparer.Ordinal);

    public void Register(ICondTypeHandler<TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.Id))
        {
            throw new ArgumentException("CondType id 不能为空。", nameof(handler));
        }

        if (!_handlers.TryAdd(handler.Id, handler))
        {
            throw new InvalidOperationException($"CondType '{handler.Id}' 已注册。");
        }
    }

    public bool Contains(string id) =>
        !string.IsNullOrWhiteSpace(id) && _handlers.ContainsKey(id);

    public bool TryGet(string id, out ICondTypeHandler<TContext>? handler)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            handler = null;
            return false;
        }

        if (_handlers.TryGetValue(id, out var found))
        {
            handler = found;
            return true;
        }

        handler = null;
        return false;
    }

    public void Clear() => _handlers.Clear();
}