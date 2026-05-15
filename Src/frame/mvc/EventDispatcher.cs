using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// 基于事件 CLR 类型的轻量分发器。同一实例可用于功能内部总线；全局总线使用 <see cref="GlobalEvents"/>。
/// </summary>
public sealed class EventDispatcher
{
	private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

	public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
	{
		ArgumentNullException.ThrowIfNull(handler);
		var key = typeof(TEvent);
		if (!_subscribers.TryGetValue(key, out var list))
		{
			list = new List<Delegate>();
			_subscribers[key] = list;
		}

		list.Add(handler);
	}

	public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
	{
		ArgumentNullException.ThrowIfNull(handler);
		var key = typeof(TEvent);
		if (!_subscribers.TryGetValue(key, out var list))
		{
			return;
		}

		list.Remove(handler);
		if (list.Count == 0)
		{
			_subscribers.Remove(key);
		}
	}

	public void Publish<TEvent>(TEvent evt) where TEvent : class
	{
		ArgumentNullException.ThrowIfNull(evt);
		var key = typeof(TEvent);
		if (!_subscribers.TryGetValue(key, out var list) || list.Count == 0)
		{
			return;
		}

		var snapshot = list.ToArray();
		for (var i = 0; i < snapshot.Length; i++)
		{
			((Action<TEvent>)snapshot[i]).Invoke(evt);
		}
	}

	public void Clear()
	{
		_subscribers.Clear();
	}
}
