using System.Text.Json;
using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionParserTests
{
    private sealed class Ctx;

    private static ConditionRegistry<Ctx> Registry()
    {
        var r = new ConditionRegistry<Ctx>();
        r.Register(CondTypeHandler.Create<Ctx, string>(
            "Tag", "S", "L",
            (JsonElement args, string path, out string? parsed, out string? error) =>
            {
                if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() != 1
                    || args[0].ValueKind != JsonValueKind.String)
                {
                    parsed = null;
                    error = $"{path}: Tag 参数须为单字符串数组";
                    return false;
                }

                parsed = args[0].GetString();
                error = null;
                return true;
            },
            (tag, _) => new LeafEvalData { Passed = true, Fill = [tag] }));
        return r;
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Test]
    public void Parse_empty_object_and_array()
    {
        Assert.That(ConditionParser.TryParse(El("{}"), Registry(), "c.json:unlock", out var and, out _), Is.True);
        Assert.That(and, Is.TypeOf<AndNode>());
        Assert.That(((AndNode)and!).Children, Is.Empty);

        Assert.That(ConditionParser.TryParse(El("[]"), Registry(), "c.json:unlock", out var or, out _), Is.True);
        Assert.That(or, Is.TypeOf<OrNode>());
    }

    [Test]
    public void Parse_unknown_type_includes_source_path()
    {
        var ok = ConditionParser.TryParse(El("""{"$or":[]}"""), Registry(), "mod/a.json:unlock", out _, out var error);
        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("mod/a.json:unlock"));
        Assert.That(error, Does.Contain("$or"));
    }

    [Test]
    public void Parse_bad_args_includes_path()
    {
        var ok = ConditionParser.TryParse(El("""{"Tag":[]}"""), Registry(), "x.json:cond", out _, out var error);
        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("x.json:cond"));
    }

    [Test]
    public void Parse_or_of_and_leaves()
    {
        var json = """[{ "Tag": ["a"] }, { "Tag": ["b"] }]""";
        Assert.That(ConditionParser.TryParse(El(json), Registry(), "p", out var expr, out _), Is.True);
        Assert.That(expr, Is.TypeOf<OrNode>());
        Assert.That(((OrNode)expr!).Children, Has.Count.EqualTo(2));
    }
}
