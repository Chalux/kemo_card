using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class DeckPresetTests
{
	[Test]
	public void CreateWithExclusiveCards_takes_first_ten()
	{
		var definition = new CharacterDto
		{
			Id = "hero",
			Cards = Enumerable.Range(1, 12).Select(i => $"card_{i}").ToList(),
		};

		var deck = DeckPreset.CreateWithExclusiveCards(definition);

		Assert.That(deck.CardIds, Has.Count.EqualTo(10));
		Assert.That(deck.CardIds[0], Is.EqualTo("card_1"));
		Assert.That(deck.CardIds[9], Is.EqualTo("card_10"));
	}

	[Test]
	public void Validate_rejects_card_not_in_buildable_pool()
	{
		var deck = new DeckPreset("d1", null, ["unknown"]);
		var buildable = new HashSet<string>(StringComparer.Ordinal) { "strike" };

		var result = deck.Validate(buildable);

		Assert.That(result.IsValid, Is.False);
		Assert.That(result.InvalidCardIds, Does.Contain("unknown"));
	}

	[Test]
	public void TryAddCard_rejects_duplicate()
	{
		var deck = new DeckPreset("d1", null, ["a"]);
		var buildable = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };

		Assert.That(deck.TryAddCard("b", buildable), Is.True);
		Assert.That(deck.TryAddCard("b", buildable), Is.False);
	}
}
