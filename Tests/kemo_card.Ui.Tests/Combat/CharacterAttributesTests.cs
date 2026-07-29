using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterAttributesTests
{
    [Test]
    public void Plus_sums_all_fields()
    {
        var a = CharacterAttributes.FromCardStats(new CardStatBlockDto { HpCap = 3, PhysicalAttack = 2 });
        var b = CharacterAttributes.FromCardStats(new CardStatBlockDto { HpCap = 5, PhysicalAttack = 1 });

        var sum = a + b;

        Assert.That(sum.HpCap, Is.EqualTo(8));
        Assert.That(sum.PhysicalAttack, Is.EqualTo(3));
    }
}