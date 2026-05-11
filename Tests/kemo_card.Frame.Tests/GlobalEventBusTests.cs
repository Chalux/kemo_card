using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class GlobalEventBusTests
{
	[Fact]
	public void RegisterSchema_twice_same_type_succeeds_idempotent()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Test");
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Test");
	}

	[Fact]
	public void RegisterSchema_twice_different_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "A");
		var ex = Assert.Throws<InvalidOperationException>(() =>
			bus.RegisterSchema<TestPongPayload>(GlobalEventId.FrameworkTestPing, "B"));
		Assert.Contains("FrameworkTestPing", ex.Message, StringComparison.Ordinal);
		Assert.Contains("B", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Publish_after_subscribe_delivers_payload()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "Pub");
		TestPingPayload? received = null;
		bus.Subscribe<TestPingPayload>(GlobalEventId.FrameworkTestPing, p => received = p);
		var sent = new TestPingPayload(42);
		bus.Publish(GlobalEventId.FrameworkTestPing, sent);
		Assert.NotNull(received);
		Assert.Equal(42, received.Value);
	}

	[Fact]
	public void Subscribe_mismatched_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "R");
		Assert.Throws<InvalidOperationException>(() =>
			bus.Subscribe<TestPongPayload>(GlobalEventId.FrameworkTestPing, _ => { }));
	}

	[Fact]
	public void Publish_mismatched_type_throws()
	{
		var bus = new GlobalEventBus();
		bus.RegisterSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing, "R");
		Assert.Throws<InvalidOperationException>(() =>
			bus.Publish(GlobalEventId.FrameworkTestPing, new TestPongPayload(1)));
	}

	private sealed record TestPongPayload(int Value);
}
