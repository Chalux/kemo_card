using KemoCard.Frame.FeatureKit;
using KemoCard.Frame.FeatureKit.Contracts;
using Xunit;

namespace kemo_card.Frame.Tests;

public sealed class FeatureBootstrapIntegrationTests
{
	public FeatureBootstrapIntegrationTests()
	{
		PackageB.ReceiveCount = 0;
		PackageB.Last = null;
	}

	[Fact]
	public void Two_packages_A_schema_B_subscribe_then_manual_publish_delivers()
	{
		var global = new GlobalEventBus();
		var packages = new IFeaturePackage[] { new PackageA(), new PackageB() };
		var manager = new FeatureManager(packages);
		var ctx = new FeatureCompositionContext(global, manager, packages);
		FeatureBootstrap.Run(packages, ctx, manager);
		global.Publish(GlobalEventId.FrameworkTestPing, new TestPingPayload(99));
		Assert.Equal(1, PackageB.ReceiveCount);
		Assert.NotNull(PackageB.Last);
		Assert.Equal(99, PackageB.Last.Value);
		FeatureBootstrap.Shutdown(manager, ctx);
	}

	[Fact]
	public void Two_packages_conflicting_schema_throws_on_second_register()
	{
		var global = new GlobalEventBus();
		var packages = new IFeaturePackage[] { new PackageBadA(), new PackageBadB() };
		var manager = new FeatureManager(packages);
		var ctx = new FeatureCompositionContext(global, manager, packages);
		Assert.Throws<InvalidOperationException>(() => FeatureBootstrap.Run(packages, ctx, manager));
	}

	private sealed class PackageA : IFeaturePackage
	{
		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageB : IFeaturePackage
	{
		public static int ReceiveCount;
		public static TestPingPayload? Last;

		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.GlobalBus.Subscribe<TestPingPayload>(GlobalEventId.FrameworkTestPing, p =>
			{
				ReceiveCount++;
				Last = p;
			});
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageBadA : IFeaturePackage
	{
		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<TestPingPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}

	private sealed class PackageBadB : IFeaturePackage
	{
		private sealed record OtherPayload(int X);

		public void Install(IFeatureCompositionContext ctx)
		{
			ctx.RegisterGlobalEventSchema<OtherPayload>(GlobalEventId.FrameworkTestPing);
		}

		public void Shutdown(IFeatureCompositionContext ctx)
		{
		}
	}
}
