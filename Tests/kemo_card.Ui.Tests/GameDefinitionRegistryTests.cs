using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class GameDefinitionRegistryTests
{
	[Test]
	public void Contains_returns_false_for_unknown_card()
	{
		var reg = new GameDefinitionRegistry();
		reg.Rebuild(Array.Empty<ModContentBundle>(), out _);

		Assert.That(reg.Contains(ContentCategory.Card, "missing"), Is.False);
	}

	[Test]
	public void Contains_returns_true_after_registering_card()
	{
		var reg = new GameDefinitionRegistry();
		var bundle = new ModContentBundle(
			ModId: "base.game",
			Characters: Array.Empty<string>(),
			Battles: Array.Empty<string>(),
			Events: Array.Empty<string>(),
			Cards: new[] { "strike" },
			Items: Array.Empty<string>(),
			Skills: Array.Empty<string>(),
			Buffs: Array.Empty<string>());

		reg.Rebuild(new[] { bundle }, out var report);

		Assert.That(reg.Contains(ContentCategory.Card, "strike"), Is.True);
		Assert.That(report.IdConflicts, Is.Empty);
		Assert.That(reg.DefinitionVersion, Is.EqualTo(1));
	}
}
