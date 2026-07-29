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

    [Test]
    public void Validate_rejects_empty_deck()
    {
        var deck = new DeckPreset("d1", null, []);

        var result = deck.Validate(new HashSet<string>(StringComparer.Ordinal) { "a" });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_accepts_deck_sizes_between_one_and_ten()
    {
        var buildable = new HashSet<string>(
            Enumerable.Range(1, 10).Select(i => $"card_{i}"),
            StringComparer.Ordinal);

        var single = new DeckPreset("d1", null, ["card_1"]);
        var full = new DeckPreset("d2", null, buildable.OrderBy(id => id, StringComparer.Ordinal));

        Assert.That(single.Validate(buildable).IsValid, Is.True);
        Assert.That(full.Validate(buildable).IsValid, Is.True);
    }

    [Test]
    public void Validate_rejects_deck_over_ten_cards()
    {
        var buildable = new HashSet<string>(
            Enumerable.Range(1, 11).Select(i => $"card_{i}"),
            StringComparer.Ordinal);
        var deck = new DeckPreset("d1", null, buildable.OrderBy(id => id, StringComparer.Ordinal));

        Assert.That(deck.Validate(buildable).IsValid, Is.False);
    }
}