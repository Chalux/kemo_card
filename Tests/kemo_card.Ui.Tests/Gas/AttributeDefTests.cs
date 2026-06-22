using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeDefTests
{
	[Test]
	public void AttributeDef_deserializes_from_json()
	{
		const string json = """
			{
			  "displayNameId": "attr.health.name",
			  "defaultBase": 0,
			  "allowNegative": false,
			  "isMeta": false
			}
			""";
		var dto = JsonSerializer.Deserialize<AttributeDefDto>(json, ContentDefinitionJson.Options);
		Assert.That(dto!.DefaultBase, Is.EqualTo(0));
		Assert.That(dto.IsMeta, Is.False);
	}

	[Test]
	public void AttributeIds_has_well_known_constants()
	{
		Assert.That(AttributeIds.Health, Is.EqualTo("Health"));
		Assert.That(AttributeIds.MaxHealth, Is.EqualTo("MaxHealth"));
		Assert.That(AttributeIds.Damage, Is.EqualTo("Damage"));
	}
}
