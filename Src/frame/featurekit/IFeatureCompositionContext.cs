using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public interface IFeatureCompositionContext
{
	IGlobalEventBus GlobalBus { get; }

	IFeatureManagerReadOnly Manager { get; }

	string InstallingFeatureName { get; }

	IInternalEventBus CreateInternalBus();

	void RegisterGlobalEventSchema<TPayload>(GlobalEventId id) where TPayload : class;

	void RegisterFacade<TFacade>(TFacade facade) where TFacade : class;
}
