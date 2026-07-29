using KemoCard.Frame.Content.Keywords;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class KeywordCatalogTests
{
    [Test]
    public void Register_then_TryGet_returns_entry()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("exhaust", "KW_EXHAUST_TITLE", "KW_EXHAUST_DESC"));
        Assert.That(catalog.TryGet("exhaust", out var e), Is.True);
        Assert.That(e!.TitleKey, Is.EqualTo("KW_EXHAUST_TITLE"));
    }

    [Test]
    public void Register_same_id_overwrites()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("exhaust", "A", "B"));
        catalog.Register(new KeywordEntry("exhaust", "C", "D"));
        Assert.That(catalog.TryGet("exhaust", out var e), Is.True);
        Assert.That(e!.TitleKey, Is.EqualTo("C"));
        Assert.That(e.DescKey, Is.EqualTo("D"));
    }

    [Test]
    public void Register_same_id_invokes_warning_handler()
    {
        var catalog = new KeywordCatalog();
        string? warned = null;
        catalog.WarningHandler = msg => warned = msg;
        catalog.Register(new KeywordEntry("exhaust", "A", "B"));
        catalog.Register(new KeywordEntry("exhaust", "C", "D"));
        Assert.That(warned, Is.Not.Null);
        Assert.That(warned, Does.Contain("exhaust"));
    }

    [Test]
    public void Unregister_removes_entry()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("retain", "T", "D"));
        catalog.Unregister("retain");
        Assert.That(catalog.TryGet("retain", out _), Is.False);
    }

    [Test]
    public void Clear_removes_all()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("a", "t", "d"));
        catalog.Clear();
        Assert.That(catalog.TryGet("a", out _), Is.False);
    }
}

[TestFixture]
public sealed class KeywordTextFormatterTests
{
    [Test]
    public void ApplyParams_replaces_named_placeholders()
    {
        var text = KeywordTextFormatter.ApplyParams(
            "造成 {amount} 点伤害",
            new Dictionary<string, string> { ["amount"] = "5" });
        Assert.That(text, Is.EqualTo("造成 5 点伤害"));
    }

    [Test]
    public void ApplyParams_missing_keeps_placeholder()
    {
        var text = KeywordTextFormatter.ApplyParams(
            "造成 {amount} 点伤害",
            new Dictionary<string, string>());
        Assert.That(text, Is.EqualTo("造成 {amount} 点伤害"));
    }

    [Test]
    public void ApplyParams_null_or_empty_params_keeps_text()
    {
        Assert.That(KeywordTextFormatter.ApplyParams("无参数", null), Is.EqualTo("无参数"));
    }
}