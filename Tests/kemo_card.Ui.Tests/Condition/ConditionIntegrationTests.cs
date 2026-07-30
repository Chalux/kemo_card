using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionIntegrationTests
{
    private static ConditionRegistry<IPersistentCondContext> CreateRegistry()
    {
        var registry = new ConditionRegistry<IPersistentCondContext>();
        BuiltinPersistentConditions.RegisterAll(registry);
        return registry;
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ConditionEvalResult ParseAndEvaluate(
        string json,
        IPersistentCondContext context,
        ConditionRegistry<IPersistentCondContext> registry)
    {
        var parsed = ConditionParser.TryParse(
            El(json),
            registry,
            "integration.json:condition",
            out var expression,
            out var error);
        Assert.That(parsed, Is.True, error);
        return ConditionEvaluator.Evaluate(expression!, context, registry);
    }

    [Test]
    public void Combined_persistent_conditions_fail_when_items_incomplete()
    {
        var context = new FakePersistentCondContext();
        context.Flags.Add("intro");
        context.Items["wood"] = 2;
        context.Items["stone"] = 0;

        var result = ParseAndEvaluate(
            """{"HasFlag":["intro"],"HasAllItems":[["wood",2],["stone",1]]}""",
            context,
            CreateRegistry());

        Assert.That(result.Passed, Is.False);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }

    [Test]
    public void Combined_persistent_conditions_pass_when_all_requirements_met()
    {
        var context = new FakePersistentCondContext();
        context.Flags.Add("intro");
        context.Items["wood"] = 2;
        context.Items["stone"] = 1;

        var result = ParseAndEvaluate(
            """{"HasFlag":["intro"],"HasAllItems":[["wood",2],["stone",1]]}""",
            context,
            CreateRegistry());

        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }

    [Test]
    public void Empty_or_array_is_false()
    {
        var context = new FakePersistentCondContext();
        var result = ParseAndEvaluate("[]", context, CreateRegistry());

        Assert.That(result.Passed, Is.False);
    }

    [Test]
    public void Empty_and_object_is_true()
    {
        var context = new FakePersistentCondContext();
        var result = ParseAndEvaluate("{}", context, CreateRegistry());

        Assert.That(result.Passed, Is.True);
    }
}
