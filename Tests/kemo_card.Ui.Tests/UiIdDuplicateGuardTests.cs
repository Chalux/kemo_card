using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiIdDuplicateGuardTests
{
	[Test]
	public void Add_same_id_twice_throws()
	{
		var g = new UiIdDuplicateGuard();
		g.Add("a");
		Assert.Throws<InvalidOperationException>(() => g.Add("a"));
	}
}
