using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AbilitySystemComponentTests
{
	[Test]
	public void Asc_exposes_attribute_set_and_reads_current()
	{
		var asc = new AbilitySystemComponent();
		asc.Attributes.InitAttribute(AttributeIds.MaxHealth, 20f);
		Assert.That(asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(20f));
	}
}
