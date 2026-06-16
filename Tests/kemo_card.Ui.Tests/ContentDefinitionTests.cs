using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentDefinitionTests
{
	[Test]
	public void Load_deserializes_strike_card_chain()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
		var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
		ContentModTestHelper.AddEffect(modDir, "strike_damage", """
			{ "kind": "Damage", "params": { "amount": 6 } }
			""");
		ContentModTestHelper.AddSkill(modDir, "strike_hit", """
			{
			  "displayNameId": "skill.strike_hit.name",
			  "descId": "skill.strike_hit.desc",
			  "effectRefs": [{ "effectId": "strike_damage" }]
			}
			""");
		ContentModTestHelper.AddCard(modDir, "strike", """
			{
			  "displayNameId": "card.strike.name",
			  "costType": "Energy",
			  "cost": 1,
			  "skillRefs": [{ "skillId": "strike_hit" }],
			  "cardType": "Physics",
			  "targetSide": "Enemy",
			  "targetScope": "Single",
			  "rarity": "Common",
			  "artPath": "cards/strike.png"
			}
			""");

		var discovery = new ContentModDiscovery();
		var bundle = ContentModLoader.Load(discovery.Scan(root).ValidMods[0]);
		var registry = new GameDefinitionRegistry();
		registry.Rebuild(new[] { bundle }, out var report);

		Assert.That(report.ValidationErrors, Is.Empty);
		Assert.That(registry.Store.TryGetCard("strike", out var card), Is.True);
		Assert.That(card.SkillRefs[0].SkillId, Is.EqualTo("strike_hit"));
		Assert.That(registry.Store.TryGetSkill("strike_hit", out var skill), Is.True);
		Assert.That(skill.EffectRefs[0].EffectId, Is.EqualTo("strike_damage"));
		Assert.That(registry.Store.TryGetEffect("strike_damage", out var effect), Is.True);
		Assert.That(effect.Kind, Is.EqualTo(EEffectKind.Damage));
	}

	[Test]
	public void CardDto_round_trips_through_json()
	{
		var original = new CardDto
		{
			Id = "strike",
			DisplayNameId = "card.strike.name",
			CostType = ECostType.Energy,
			Cost = 1,
			CardType = ECardType.Physics,
			Rarity = ERarity.Common,
			SkillRefs = [new SkillRefDto { SkillId = "strike_hit" }],
		};

		var json = JsonSerializer.Serialize(original, ContentDefinitionJson.Options);
		var restored = JsonSerializer.Deserialize<CardDto>(json, ContentDefinitionJson.Options);

		Assert.That(restored, Is.Not.Null);
		Assert.That(restored!.Id, Is.EqualTo("strike"));
		Assert.That(restored.CardType, Is.EqualTo(ECardType.Physics));
		Assert.That(restored.SkillRefs[0].SkillId, Is.EqualTo("strike_hit"));
	}

	[Test]
	public void Validation_rejects_unknown_skill_reference()
	{
		var bundle = ContentModTestHelper.EmptyBundle("base") with
		{
			Cards = new[] { "bad_card" },
			Definitions = ModDefinitionsBundle.Empty with
			{
				Cards = new Dictionary<string, CardDto>
				{
					["bad_card"] = new()
					{
						Id = "bad_card",
						SkillRefs = [new SkillRefDto { SkillId = "missing_skill" }],
					},
				},
			},
		};

		var registry = new GameDefinitionRegistry();
		registry.Rebuild(new[] { bundle }, out var report);

		Assert.That(report.ValidationErrors, Has.Count.EqualTo(1));
		Assert.That(registry.Contains(ContentCategory.Card, "bad_card"), Is.False);
	}
}
