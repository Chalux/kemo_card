using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionRegistryTests
{
    private sealed class DummyCtx;

    [Test]
    public void Register_then_TryGet_returns_handler()
    {
        var registry = new ConditionRegistry<DummyCtx>();
        var handler = CondTypeHandler.Create<DummyCtx, string>(
            id: "Ping",
            shortTipKey: "S",
            longTipKey: "L",
            tryParse: (System.Text.Json.JsonElement _, string _, out string? parsed, out string? error) =>
            {
                parsed = "x";
                error = null;
                return true;
            },
            check: (parsed, _) => new LeafEvalData { Passed = true, Fill = [parsed] });

        registry.Register(handler);

        Assert.That(registry.Contains("Ping"), Is.True);
        Assert.That(registry.TryGet("Ping", out var found), Is.True);
        Assert.That(found!.Id, Is.EqualTo("Ping"));
    }

    [Test]
    public void Register_duplicate_id_throws()
    {
        var registry = new ConditionRegistry<DummyCtx>();
        var a = CondTypeHandler.Create<DummyCtx, string>(
            "Ping", "S", "L",
            (System.Text.Json.JsonElement _, string _, out string? p, out string? e) => { p = "a"; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true });
        var b = CondTypeHandler.Create<DummyCtx, string>(
            "Ping", "S", "L",
            (System.Text.Json.JsonElement _, string _, out string? p, out string? e) => { p = "b"; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true });

        registry.Register(a);
        Assert.Throws<InvalidOperationException>(() => registry.Register(b));
    }
}