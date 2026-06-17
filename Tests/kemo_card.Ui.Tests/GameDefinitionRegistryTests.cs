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

		Assert.That(reg.Contains(EContentCategory.Card, "missing"), Is.False);
	}

	[Test]
	public void Contains_returns_true_after_registering_card()
	{
		var reg = new GameDefinitionRegistry();
		var bundle = ContentModTestHelper.EmptyBundle("base.game");
		bundle = bundle with
		{
			Cards = new[] { "strike" },
			Definitions = bundle.Definitions with
			{
				Cards = new Dictionary<string, KemoCard.Frame.Content.Definitions.CardDto>
				{
					["strike"] = new() { Id = "strike", DisplayNameId = "card.strike.name" },
				},
			},
		};

		reg.Rebuild(new[] { bundle }, out var report);

		Assert.That(reg.Contains(EContentCategory.Card, "strike"), Is.True);
		Assert.That(reg.Store.TryGetCard("strike", out var card), Is.True);
		Assert.That(card.DisplayNameId, Is.EqualTo("card.strike.name"));
		Assert.That(report.IdConflicts, Is.Empty);
		Assert.That(reg.DefinitionVersion, Is.EqualTo(1));
	}
}
