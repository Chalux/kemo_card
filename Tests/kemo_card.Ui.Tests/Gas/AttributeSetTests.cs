using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeSetTests
{
    [Test]
    public void OnAttributeChanged_fires_when_base_changes()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.MaxHealth, 10f));
        string? changed = null;
        set.AttributeChanged += (_, e) => changed = e.AttributeId;
        set.SetBaseValue(AttributeIds.MaxHealth, 15f);
        Assert.That(changed, Is.EqualTo(AttributeIds.MaxHealth));
    }
}