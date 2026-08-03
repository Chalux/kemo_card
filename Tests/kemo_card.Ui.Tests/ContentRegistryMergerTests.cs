using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentRegistryMergerTests
{
    [Test]
    public void Merge_skips_later_duplicate_in_same_category()
    {
        var store = new GameDefinitionStore();
        var merger = new ContentRegistryMerger();
        var strike = new CardDto { Id = "strike" };
        var bundles = new[]
        {
            ContentModTestHelper.Bundle(
                "a",
                ModDefinitionsBundle.Empty with
                {
                    Cards = new Dictionary<string, CardDto>(StringComparer.Ordinal) { ["strike"] = strike },
                }),
            ContentModTestHelper.Bundle(
                "b",
                ModDefinitionsBundle.Empty with
                {
                    Cards = new Dictionary<string, CardDto>(StringComparer.Ordinal) { ["strike"] = strike },
                }),
        };

        var result = merger.Merge(bundles, store);

        Assert.That(store.Contains(EContentCategory.Card, "strike"), Is.True);
        Assert.That(result.IdConflicts, Has.Count.EqualTo(1));
        Assert.That(result.IdConflicts[0].WinnerModId, Is.EqualTo("a"));
        Assert.That(result.IdConflicts[0].LoserModId, Is.EqualTo("b"));
    }

    [Test]
    public void Merge_allows_same_id_in_different_categories()
    {
        var store = new GameDefinitionStore();
        var merger = new ContentRegistryMerger();
        var bundles = new[]
        {
            ContentModTestHelper.Bundle(
                "a",
                ModDefinitionsBundle.Empty with
                {
                    Cards = new Dictionary<string, CardDto>(StringComparer.Ordinal)
                    {
                        ["foo"] = new() { Id = "foo" },
                    },
                    Skills = new Dictionary<string, SkillDto>(StringComparer.Ordinal)
                    {
                        ["foo"] = new() { Id = "foo" },
                    },
                }),
        };

        var result = merger.Merge(bundles, store);

        Assert.That(store.Contains(EContentCategory.Card, "foo"), Is.True);
        Assert.That(store.Contains(EContentCategory.Skill, "foo"), Is.True);
        Assert.That(result.IdConflicts, Is.Empty);
    }
}