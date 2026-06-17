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

		merger.Merge(bundles, tables, out var report, out _);

		Assert.That(tables[EContentCategory.Card], Does.Contain("strike"));
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

		merger.Merge(bundles, tables, out var report, out _);

		Assert.That(tables[EContentCategory.Card], Does.Contain("foo"));
		Assert.That(tables[EContentCategory.Skill], Does.Contain("foo"));
		Assert.That(report.IdConflicts, Is.Empty);
	}

	private static Dictionary<EContentCategory, HashSet<string>> CreateEmptyTables() =>
		new()
		{
			[EContentCategory.Character] = new(StringComparer.Ordinal),
			[EContentCategory.Enemy] = new(StringComparer.Ordinal),
			[EContentCategory.Battle] = new(StringComparer.Ordinal),
			[EContentCategory.Event] = new(StringComparer.Ordinal),
			[EContentCategory.Card] = new(StringComparer.Ordinal),
			[EContentCategory.Item] = new(StringComparer.Ordinal),
			[EContentCategory.Skill] = new(StringComparer.Ordinal),
			[EContentCategory.Buff] = new(StringComparer.Ordinal),
			[EContentCategory.Effect] = new(StringComparer.Ordinal),
		};
}
