using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class PresentationAnimListTests
{
    [Test]
    public void Null_whitelist_returns_all_sorted()
    {
        Assert.That(
            PresentationAnimList.Resolve(["hurt", "idle"], null),
            Is.EqualTo(new[] { "hurt", "idle" }));
    }

    [Test]
    public void Whitelist_intersects()
    {
        Assert.That(
            PresentationAnimList.Resolve(["idle", "hurt", "cast"], ["idle", "cast", "missing"]),
            Is.EqualTo(new[] { "cast", "idle" }));
    }
}