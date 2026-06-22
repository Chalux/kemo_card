using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed class CardExecutionQueue
{
	private readonly HostRng _rng;
	private readonly SortedSet<(int Priority, int TieBreak, long Sequence, QueuedCardEntry Entry)> _heap = [];

	public CardExecutionQueue(HostRng rng) => _rng = rng;

	public void Enqueue(QueuedCardEntry entry)
	{
		var tieBreak = _rng.NextInt(0, int.MaxValue);
		_heap.Add((entry.Priority, tieBreak, entry.Sequence, entry));
	}

	public bool TryDequeue(out QueuedCardEntry? entry)
	{
		if (_heap.Count == 0)
		{
			entry = null;
			return false;
		}

		var first = _heap.Min;
		_heap.Remove(first);
		entry = first.Entry;
		return true;
	}

	public int Count => _heap.Count;

	public IEnumerable<QueuedCardEntry> PeekAllOrdered() =>
		_heap.Select(x => x.Entry);

	public bool TryRemove(Predicate<QueuedCardEntry> predicate, out QueuedCardEntry? removed)
	{
		ArgumentNullException.ThrowIfNull(predicate);

		foreach (var node in _heap)
		{
			if (!predicate(node.Entry))
				continue;
			_heap.Remove(node);
			removed = node.Entry;
			return true;
		}

		removed = null;
		return false;
	}
}
