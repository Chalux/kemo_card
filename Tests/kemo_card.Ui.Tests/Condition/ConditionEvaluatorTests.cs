using KemoCard.Frame.Condition;
using NUnit.Framework;
using System.Text.Json;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionEvaluatorTests
{
    private sealed class Ctx;

    private static ConditionRegistry<Ctx> CreateRegistry()
    {
        var registry = new ConditionRegistry<Ctx>();
        registry.Register(CondTypeHandler.Create<Ctx, object?>(
            "Always", "S", "L",
            (JsonElement _, string _, out object? p, out string? e) => { p = null; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true }));
        registry.Register(CondTypeHandler.Create<Ctx, object?>(
            "Never", "S", "L",
            (JsonElement _, string _, out object? p, out string? e) => { p = null; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = false }));
        return registry;
    }

    [Test]
    public void Empty_and_is_true()
    {
        var result = ConditionEvaluator.Evaluate(new AndNode([]), new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Is.Empty);
    }

    [Test]
    public void Empty_or_is_false()
    {
        var result = ConditionEvaluator.Evaluate(new OrNode([]), new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.False);
    }

    [Test]
    public void And_evaluates_all_leaves()
    {
        var root = new AndNode([
            new LeafNode("Always", null!),
            new LeafNode("Never", null!),
        ]);
        var result = ConditionEvaluator.Evaluate(root, new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.False);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }

    [Test]
    public void Or_evaluates_all_leaves_even_when_passed()
    {
        var root = new OrNode([
            new LeafNode("Always", null!),
            new LeafNode("Never", null!),
        ]);
        var result = ConditionEvaluator.Evaluate(root, new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }
}
