using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardStatBlockTests
{
	[Test]
	public void CardDto_deserializes_stats_block()
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
			    "hpCap": 5,
			    "physicalAttack": 2,
			    "maxEnergy": 3,
			    "initialEnergy": 1
			  }
			}
			""";

		var card = JsonSerializer.Deserialize<CardDto>(json, ContentDefinitionJson.Options)!;

		Assert.That(card.Stats, Is.Not.Null);
		Assert.That(card.Stats!.HpCap, Is.EqualTo(5));
		Assert.That(card.Stats.PhysicalAttack, Is.EqualTo(2));
		Assert.That(card.Stats.MaxEnergy, Is.EqualTo(3));
		Assert.That(card.Stats.InitialEnergy, Is.EqualTo(1));
	}
}
