namespace KemoCard.Frame.FeatureKit;

public interface IInternalEventBus
{
	void Subscribe<TPayload>(Action<TPayload> handler) where TPayload : class;

	void Publish<TPayload>(TPayload payload) where TPayload : class;

	void Shutdown();
}
