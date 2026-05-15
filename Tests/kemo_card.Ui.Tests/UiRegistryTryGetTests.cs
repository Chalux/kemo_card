using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiRegistryTryGetTests
{
	[Test]
	public void TryGet_unknown_id_returns_false()
	{
		var reg = new UiRegistry();
		Assert.That(reg.TryGet("missing", out _, out _, out _, out _), Is.False);
	}
}
