using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;

namespace kemo_card.Frame.Tests.Fakes;

public sealed class FakeCompositionContext : IFeatureCompositionContext
{
	public FakeCompositionContext(IGlobalEventBus global, IFeatureManagerReadOnly manager)
	{
		GlobalBus = global;
		Manager = manager;
	}

	public IGlobalEventBus GlobalBus { get; }

	public IFeatureManagerReadOnly Manager { get; }

	public string InstallingFeatureName => "Fake";

	public IInternalEventBus CreateInternalBus() => new InternalEventBus();

	public void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class
	{
		if (GlobalBus is GlobalEventBus concrete)
		{
			concrete.RegisterSchema<TPayload>(id, InstallingFeatureName);
			return;
		}
		throw new InvalidOperationException("FakeCompositionContext requires GlobalEventBus instance for schema registration tests.");
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
	}
}
