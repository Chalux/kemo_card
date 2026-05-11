using System.Collections.Concurrent;
using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public sealed class FeatureCompositionContext : IFeatureCompositionContext
{
	private readonly GlobalEventBus _global;
	private readonly FeatureManager _manager;
	private readonly IReadOnlyList<IFeaturePackage> _packages;
	private readonly ConcurrentDictionary<int, IInternalEventBus> _internalByIndex = new();
	private int _installIndex = -1;
	private readonly ConcurrentDictionary<int, byte> _internalCreated = new();

	public FeatureCompositionContext(
		GlobalEventBus global,
		FeatureManager manager,
		IReadOnlyList<IFeaturePackage> packages)
	{
		_global = global;
		_manager = manager;
		_packages = packages;
	}

	public IGlobalEventBus GlobalBus => _global;

	public IFeatureManagerReadOnly Manager => _manager;

	public string InstallingFeatureName =>
		_installIndex >= 0 && _installIndex < _packages.Count
			? _packages[_installIndex].GetType().Name
			: string.Empty;

	public IInternalEventBus CreateInternalBus()
	{
		if (_installIndex < 0 || _installIndex >= _packages.Count)
		{
			throw new InvalidOperationException("CreateInternalBus is only valid during Install.");
		}
		if (!_internalCreated.TryAdd(_installIndex, 0))
		{
			throw new InvalidOperationException(
				$"Internal bus already created for feature index {_installIndex} ({InstallingFeatureName}).");
		}
		var bus = new InternalEventBus();
		_internalByIndex[_installIndex] = bus;
		return bus;
	}

	public void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class
	{
		_global.RegisterSchema<TPayload>(id, InstallingFeatureName);
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
		_manager.RegisterFacade(facade);
	}

	internal void BeginInstallIndex(int index)
	{
		_installIndex = index;
	}

	internal void EndInstallIndex()
	{
		_installIndex = -1;
	}

	internal void ShutdownInternalBuses()
	{
		for (var i = _packages.Count - 1; i >= 0; i--)
		{
			if (_internalByIndex.TryRemove(i, out var bus))
			{
				bus.Shutdown();
			}
		}
	}

	internal void ClearGlobalSubscriptions()
	{
		_global.ClearAllSubscriptionsForTests();
	}
}
