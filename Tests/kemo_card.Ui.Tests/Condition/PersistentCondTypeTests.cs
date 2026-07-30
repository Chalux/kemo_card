using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class PersistentCondTypeTests
{
    private static ConditionRegistry<IPersistentCondContext> CreateRegistry()
    {
        var registry = new ConditionRegistry<IPersistentCondContext>();
        BuiltinPersistentConditions.RegisterAll(registry);
        return registry;
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [TestCase("HasFlag", true)]
    [TestCase("NotHasFlag", false)]
    public void Flag_conditions_parse_and_evaluate_single_flag(string condType, bool hasFlag)
    {
        var context = new FakePersistentCondContext();
        if (hasFlag)
        {
            context.Flags.Add("x");
        }

        var parsed = ConditionParser.TryParse(
            El($"{{\"{condType}\":[\"x\"]}}"),
            CreateRegistry(),
            "persistent.json:condition",
            out var expression,
            out _);
        var result = ConditionEvaluator.Evaluate(expression!, context, CreateRegistry());

        Assert.That(parsed, Is.True);
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves[0].Progress, Is.EqualTo(new ConditionProgress(1, 1)));
        Assert.That(result.Leaves[0].Refs!.FlagIds, Is.EqualTo(new[] { "x" }));
        Assert.That(result.Leaves[0].Fill, Is.EqualTo(new[] { "x" }));
    }

    [TestCase("HasFlag")]
    [TestCase("NotHasFlag")]
    public void Flag_conditions_reject_empty_args_with_path(string condType)
    {
        var ok = ConditionParser.TryParse(
            El($"{{\"{condType}\":[]}}"),
            CreateRegistry(),
            "persistent.json:condition",
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("persistent.json:condition"));
    }

    [Test]
    public void Has_all_items_reports_missing_items_and_progress()
    {
        var context = new FakePersistentCondContext();
        context.Items["wood"] = 5;

        var parsed = ConditionParser.TryParse(
            El("""{"HasAllItems":[["wood",5],["stone",3]]}"""),
            CreateRegistry(),
            "persistent.json:condition",
            out var expression,
            out _);
        var result = ConditionEvaluator.Evaluate(expression!, context, CreateRegistry());

        Assert.That(parsed, Is.True);
        Assert.That(result.Passed, Is.False);
        Assert.That(result.Leaves[0].Progress, Is.EqualTo(new ConditionProgress(1, 2)));
        Assert.That(result.Leaves[0].Refs!.ItemIds, Is.EqualTo(new[] { "wood", "stone" }));
    }

    [Test]
    public void Has_any_item_passes_when_one_requirement_is_satisfied()
    {
        var context = new FakePersistentCondContext();
        context.Items["stone"] = 3;

        var parsed = ConditionParser.TryParse(
            El("""{"HasAnyItem":[["wood",5],["stone",3]]}"""),
            CreateRegistry(),
            "persistent.json:condition",
            out var expression,
            out _);
        var result = ConditionEvaluator.Evaluate(expression!, context, CreateRegistry());

        Assert.That(parsed, Is.True);
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves[0].Progress, Is.EqualTo(new ConditionProgress(1, 1)));
        Assert.That(result.Leaves[0].Refs!.ItemIds, Is.EqualTo(new[] { "wood", "stone" }));
    }

    [Test]
    public void Combined_persistent_conditions_parse_and_evaluate()
    {
        var context = new FakePersistentCondContext();
        context.Flags.Add("intro");
        context.Items["wood"] = 1;

        var parsed = ConditionParser.TryParse(
            El("""{"HasFlag":["intro"],"HasAllItems":[["wood",1]]}"""),
            CreateRegistry(),
            "persistent.json:condition",
            out var expression,
            out _);
        var result = ConditionEvaluator.Evaluate(expression!, context, CreateRegistry());

        Assert.That(parsed, Is.True);
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }
}