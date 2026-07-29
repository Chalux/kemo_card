using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardStatBlockTests
{
    [Test]
    public void CardDto_deserializes_stats_block_with_attribute_dictionary()
    {
        const string json = """
			{
			  "id": "strike",
			  "displayNameId": "card.strike",
			  "costType": "Energy",
			  "cost": 1,
			  "cardType": "Physics",
			  "targetSide": "Enemy",
			  "targetScope": "Single",
			  "rarity": "Common",
			  "stats": {
			    "attributes": {
			      "MaxHealth": 7,
			      "PhysicalAttack": 3
			    },
			    "hpCap": 5,
			    "physicalAttack": 2,
			    "maxEnergy": 3,
			    "initialEnergy": 1
			  }
			}
			""";

        var card = JsonSerializer.Deserialize<CardDto>(json, ContentDefinitionJson.Options)!;

        Assert.That(card.Stats, Is.Not.Null);
        Assert.That(card.Stats!.Attributes[AttributeIds.MaxHealth], Is.EqualTo(7f));
        Assert.That(card.Stats.Attributes[AttributeIds.PhysicalAttack], Is.EqualTo(3f));
        Assert.That(card.Stats!.HpCap, Is.EqualTo(5));
        Assert.That(card.Stats.PhysicalAttack, Is.EqualTo(2));
        Assert.That(card.Stats.MaxEnergy, Is.EqualTo(3));
        Assert.That(card.Stats.InitialEnergy, Is.EqualTo(1));
    }
}