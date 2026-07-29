using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeAggregatorTests
{
    [Test]
    public void Aggregator_applies_add_then_multiply_then_override()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.PhysicalAttack, 10f));
        var agg = new AttributeAggregator(set);
        agg.SetModifiers(AttributeIds.PhysicalAttack,
        [
            new AttributeModifier(EAttributeModifierOp.Add, 5f, order: 0),
            new AttributeModifier(EAttributeModifierOp.Multiply, 0.5f, order: 1),
            new AttributeModifier(EAttributeModifierOp.Override, 99f, order: 2),
        ]);
        agg.Recalculate(AttributeIds.PhysicalAttack);
        Assert.That(set.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(99f));
    }

    [Test]
    public void Aggregator_formula_without_override()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.PhysicalAttack, 10f));
        var agg = new AttributeAggregator(set);
        agg.SetModifiers(AttributeIds.PhysicalAttack,
        [
            new AttributeModifier(EAttributeModifierOp.Add, 5f, order: 0),
            new AttributeModifier(EAttributeModifierOp.Multiply, 0.5f, order: 1),
        ]);
        agg.Recalculate(AttributeIds.PhysicalAttack);
        Assert.That(set.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(7.5f));
    }

    [Test]
    public void Aggregator_applies_divide_modifiers_sequentially()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.PhysicalAttack, 100f));
        var agg = new AttributeAggregator(set);
        agg.SetModifiers(AttributeIds.PhysicalAttack,
        [
            new AttributeModifier(EAttributeModifierOp.Divide, 2f, order: 0),
            new AttributeModifier(EAttributeModifierOp.Divide, 5f, order: 1),
        ]);
        agg.Recalculate(AttributeIds.PhysicalAttack);
        Assert.That(set.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
    }
}