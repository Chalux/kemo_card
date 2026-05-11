using System.Collections.Concurrent;

namespace KemoCard.Frame.FeatureKit;

public sealed class InternalEventBus : IInternalEventBus
{
	private readonly ConcurrentDictionary<Type, ConcurrentBag<Delegate>> _handlers = new();
	private int _shutdown;

	public void Subscribe<TPayload>(Action<TPayload> handler) where TPayload : class
	{
		EnsureNotShutdown();
		var t = typeof(TPayload);
		var bag = _handlers.GetOrAdd(t, _ => new ConcurrentBag<Delegate>());
		bag.Add(handler);
	}

	public void Publish<TPayload>(TPayload payload) where TPayload : class
	{
		EnsureNotShutdown();
		var t = typeof(TPayload);
		if (!_handlers.TryGetValue(t, out var bag))
		{
			return;
		}
		foreach (var d in bag)
		{
			if (d is Action<TPayload> typed)
			{
				typed(payload);
			}
		}
	}

	public void Shutdown()
	{
		if (Interlocked.Exchange(ref _shutdown, 1) == 1)
		{
			return;
		}
		_handlers.Clear();
	}

	private void EnsureNotShutdown()
	{
		if (Volatile.Read(ref _shutdown) == 1)
		{
			throw new InvalidOperationException("InternalEventBus is shut down.");
		}
	}
}
