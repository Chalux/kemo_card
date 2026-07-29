namespace KemoCard.Mod.Combat.StateMachine;

public sealed class CardExecutionQueue
{
    private readonly SortedSet<QueuedCardEntry> _heap = new(ExecutionOrderComparer.Instance);

    public void Enqueue(QueuedCardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _heap.Add(entry);
    }

    public bool TryDequeue(out QueuedCardEntry? entry)
    {
        if (_heap.Count == 0)
        {
            entry = null;
            return false;
        }

        entry = _heap.Min;
        _heap.Remove(entry!);
        return true;
    }

    public int Count => _heap.Count;

    public IEnumerable<QueuedCardEntry> PeekAllOrdered() => _heap;

    public bool TryRemove(Predicate<QueuedCardEntry> predicate, out QueuedCardEntry? removed)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var node in _heap)
        {
            if (!predicate(node))
                continue;
            _heap.Remove(node);
            removed = node;
            return true;
        }

        removed = null;
        return false;
    }

    /// <summary>
    /// 执行顺序：<see cref="QueuedCardEntry.Priority"/> 降序（越大越先），同优先级按
    /// <see cref="QueuedCardEntry.Sequence"/> 升序（先标记先执行）。无随机破平。
    /// </summary>
    private sealed class ExecutionOrderComparer : IComparer<QueuedCardEntry>
    {
        public static ExecutionOrderComparer Instance { get; } = new();

        public int Compare(QueuedCardEntry? x, QueuedCardEntry? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var byPriority = y.Priority.CompareTo(x.Priority);
            return byPriority != 0 ? byPriority : x.Sequence.CompareTo(y.Sequence);
        }
    }
}