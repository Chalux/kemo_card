using System.Collections.Concurrent;
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public sealed class GlobalEventBus : IGlobalEventBus
{
	private readonly ConcurrentDictionary<GlobalEventId, Type> _schemaTypes = new();
	private readonly ConcurrentDictionary<GlobalEventId, ConcurrentBag<Delegate>> _handlers = new();

	public void RegisterSchema<TPayload>(GlobalEventId id, string registrantName) where TPayload : class
	{
		var t = typeof(TPayload);
		_schemaTypes.AddOrUpdate(
			id,
			_ => t,
			(_, existing) =>
			{
				if (existing != t)
				{
					throw new InvalidOperationException(
						$"GlobalEventId '{id}' schema conflict: existing '{existing.FullName}', " +
						$"new '{t.FullName}' from '{registrantName}'.");
				}
				return existing;
			});
	}

	public void Publish<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class
	{
		AssertPayloadAssignable(id, payload);
		if (!_handlers.TryGetValue(id, out var bag))
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

	public void Subscribe<TPayload>(GlobalEventId id, Action<TPayload> handler) where TPayload : class
	{
		AssertSchemaMatches<TPayload>(id);
		var bag = _handlers.GetOrAdd(id, _ => new ConcurrentBag<Delegate>());
		bag.Add(handler);
	}

	public void ClearAllSubscriptionsForTests()
	{
		_handlers.Clear();
	}

	private void AssertSchemaMatches<TPayload>(GlobalEventId id) where TPayload : class
	{
		if (!_schemaTypes.TryGetValue(id, out var registered))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' has no registered schema; cannot subscribe as '{typeof(TPayload).FullName}'.");
		}
		if (registered != typeof(TPayload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' schema is '{registered.FullName}'; subscribe type '{typeof(TPayload).FullName}' is invalid.");
		}
	}

	private void AssertPayloadAssignable<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class
	{
		if (!_schemaTypes.TryGetValue(id, out var registered))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' has no registered schema; cannot publish '{typeof(TPayload).FullName}'.");
		}
		if (registered != typeof(TPayload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' schema is '{registered.FullName}'; publish type '{typeof(TPayload).FullName}' is invalid.");
		}
		if (!registered.IsInstanceOfType(payload))
		{
			throw new InvalidOperationException(
				$"GlobalEventId '{id}' payload runtime type '{payload.GetType().FullName}' is not assignable to '{registered.FullName}'.");
		}
	}
}
