using KemoCard.Frame.FeatureKit.Contracts;

namespace KemoCard.Frame.FeatureKit;

public interface IGlobalEventBus
{
	void RegisterSchema<TPayload>(GlobalEventId id, string registrantName) where TPayload : class;

	void Publish<TPayload>(GlobalEventId id, TPayload payload) where TPayload : class;

	void Subscribe<TPayload>(GlobalEventId id, Action<TPayload> handler) where TPayload : class;

	void ClearAllSubscriptionsForTests();
}
