using KemoCard.Frame.FeatureKit;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class InternalEventBusTests
{
	private sealed record LocalA(int X);

	[Fact]
	public void Publish_delivers_to_subscriber()
	{
		var bus = new InternalEventBus();
		LocalA? got = null;
		bus.Subscribe<LocalA>(a => got = a);
		bus.Publish(new LocalA(7));
		Assert.NotNull(got);
		Assert.Equal(7, got.X);
	}

	[Fact]
	public void After_shutdown_publish_throws()
	{
		var bus = new InternalEventBus();
		bus.Shutdown();
		Assert.Throws<InvalidOperationException>(() => bus.Publish(new LocalA(1)));
	}

	[Fact]
	public void After_shutdown_subscribe_throws()
	{
		var bus = new InternalEventBus();
		bus.Shutdown();
		Assert.Throws<InvalidOperationException>(() => bus.Subscribe<LocalA>(_ => { }));
	}
}
