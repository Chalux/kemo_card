using KemoCard.Frame.Mvc;
using KemoCard.Mod.Global;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class BaseControllerDisposeTests
{
	private readonly struct SamplePayload(int value)
	{
		public int Value { get; } = value;
	}

	[Test]
	public void Dispose_removes_controller_listeners_from_internal_bus()
	{
		var mod = new GlobalMod();
		var controller = new TestGlobalController(mod);
		var key = new EventKey<SamplePayload>(901);
		var hits = 0;

		mod.InternalBus.On(key, (_, _) => hits++, controller);
		Assert.That(mod.InternalBus.Has(key, caller: controller), Is.True);

		controller.Dispose();
		Assert.That(mod.InternalBus.Has(key, caller: controller), Is.False);

		mod.InternalBus.Send(key, new SamplePayload(1));
		Assert.That(hits, Is.EqualTo(0));
	}

	private sealed class TestGlobalController(GlobalMod model) : BaseController<GlobalMod>(model)
	{
	}
}
