using kemo_card.Frame.Tests.Fakes;
using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class FakeContextPackageTests
{
	private sealed class SamplePackage : IFeaturePackage
	{
		public IInternalEventBus? Bus;

		public void Install(IFeatureCompositionContext ctx)
		{
			Bus = ctx.CreateInternalBus();
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
			TestPingPayload? got = null;
			Bus.Subscribe<TestPingPayload>(p => got = p);
			Bus.Publish(new TestPingPayload(3));
			Assert.NotNull(got);
			Assert.Equal(3, got.Value);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	[Fact]
	public void Install_with_fake_context_uses_internal_bus()
	{
		var global = new GlobalEventBus();
		var manager = new FeatureManager(Array.Empty<IFeaturePackage>());
		var fake = new FakeCompositionContext(global, manager);
		var pkg = new SamplePackage();
		pkg.Install(fake);
	}
}
