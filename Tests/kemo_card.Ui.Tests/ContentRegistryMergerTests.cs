using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentRegistryMergerTests
{
	[Test]
	public void Merge_skips_later_duplicate_in_same_category()
	{
		var tables = CreateEmptyTables();
		var merger = new ContentRegistryMerger();
		var bundles = new[]
		{
			ContentModTestHelper.EmptyBundle("a") with { Cards = new[] { "strike" } },
			ContentModTestHelper.EmptyBundle("b") with { Cards = new[] { "strike" } },
		};

		merger.Merge(bundles, tables, out var report);

		Assert.That(tables[ContentCategory.Card], Does.Contain("strike"));
		Assert.That(report.IdConflicts, Has.Count.EqualTo(1));
		Assert.That(report.IdConflicts[0].WinnerModId, Is.EqualTo("a"));
		Assert.That(report.IdConflicts[0].LoserModId, Is.EqualTo("b"));
	}

	[Test]
	public void Merge_allows_same_id_in_different_categories()
	{
		var tables = CreateEmptyTables();
		var merger = new ContentRegistryMerger();
		var bundles = new[]
		{
			ContentModTestHelper.EmptyBundle("a") with
			{
				Cards = new[] { "foo" },
				Skills = new[] { "foo" },
			},
		};

		merger.Merge(bundles, tables, out var report);

		Assert.That(tables[ContentCategory.Card], Does.Contain("foo"));
		Assert.That(tables[ContentCategory.Skill], Does.Contain("foo"));
		Assert.That(report.IdConflicts, Is.Empty);
	}

	private static Dictionary<ContentCategory, HashSet<string>> CreateEmptyTables() =>
		new()
		{
			[ContentCategory.Character] = new(StringComparer.Ordinal),
			[ContentCategory.Battle] = new(StringComparer.Ordinal),
			[ContentCategory.Event] = new(StringComparer.Ordinal),
			[ContentCategory.Card] = new(StringComparer.Ordinal),
			[ContentCategory.Item] = new(StringComparer.Ordinal),
			[ContentCategory.Skill] = new(StringComparer.Ordinal),
			[ContentCategory.Buff] = new(StringComparer.Ordinal),
			[ContentCategory.Effect] = new(StringComparer.Ordinal),
		};
}
