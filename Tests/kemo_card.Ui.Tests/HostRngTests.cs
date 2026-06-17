using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class HostRngTests
{
	[Test]
	public void NextInt_is_deterministic_for_same_seed()
	{
		var a = new HostRng(12345, "effect:fx_a");
		var b = new HostRng(12345, "effect:fx_a");
		Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
		Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
	}
}
