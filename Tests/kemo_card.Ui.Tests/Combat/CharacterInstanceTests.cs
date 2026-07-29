using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterInstanceTests
{
    private static CharacterDto CreateDefinition(params string[] exclusiveCards) => new()
    {
        Id = "kemo",
        Cards = exclusiveCards.ToList(),
    };

    [Test]
    public void Constructor_from_dto_starts_with_one_exclusive_deck()
    {
        var instance = new CharacterInstance(CreateDefinition("c1", "c2"));

        Assert.That(instance.Decks, Has.Count.EqualTo(1));
        Assert.That(instance.Decks[0].CardIds, Is.EquivalentTo(new[] { "c1", "c2" }));
        Assert.That(instance.CurrentDeckIndex, Is.EqualTo(0));
    }

    [Test]
    public void TryCreateDeck_appends_until_cap()
    {
        var instance = new CharacterInstance(CreateDefinition("c1"));

        for (var i = 1; i < CombatConstants.MaxDecksPerCharacter; i++)
            Assert.That(instance.TryCreateDeck(), Is.True);

        Assert.That(instance.TryCreateDeck(), Is.False);
        Assert.That(instance.Decks, Has.Count.EqualTo(CombatConstants.MaxDecksPerCharacter));
    }

    [Test]
    public void TryEditDeck_blocked_when_locked()
    {
        var instance = new CharacterInstance(CreateDefinition("c1"));
        instance.SetDeckLocked(true);

        var edited = instance.TryEditDeck(0, deck => deck.TryAddCard("x", new HashSet<string> { "x" }));

        Assert.That(edited, Is.False);
    }

    [Test]
    public void ComputeAttributes_sums_current_deck_card_stats()
    {
        var registry = CombatTestHelper.CreateRegistry(new CardDto
        {
            Id = "strike",
            Stats = new CardStatBlockDto { HpCap = 4, PhysicalAttack = 2 },
        });
        var instance = new CharacterInstance(CreateDefinition("strike"));

        var attrs = instance.ComputeAttributes(registry);

        Assert.That(attrs.HpCap, Is.EqualTo(4));
        Assert.That(attrs.PhysicalAttack, Is.EqualTo(2));
    }
}