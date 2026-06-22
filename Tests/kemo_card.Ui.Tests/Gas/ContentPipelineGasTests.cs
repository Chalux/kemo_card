using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class ContentPipelineGasTests
{
	[Test]
	public void Load_registry_with_attribute_gameplay_effect_and_skill_action()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
		var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
		var attrDir = Path.Combine(modDir, "content", "attributes");
		var geDir = Path.Combine(modDir, "content", "gameplay_effects");
		var actionDir = Path.Combine(modDir, "content", "skill_actions");
		Directory.CreateDirectory(attrDir);
		Directory.CreateDirectory(geDir);
		Directory.CreateDirectory(actionDir);

		File.WriteAllText(Path.Combine(attrDir, "Health.json"), """
			{
			  "displayNameId": "attr.health.name",
			  "defaultBase": 100,
			  "allowNegative": false
			}
			""");
		File.WriteAllText(Path.Combine(geDir, "weak.json"), """
			{
			  "displayNameId": "ge.weak.name",
			  "durationPolicy": "HasDuration",
			  "durationTurns": 2,
			  "stackingPolicy": "AggregateByTarget",
			  "maxStacks": 3,
			  "modifiers": [
			    { "attributeId": "Health", "operation": "Add", "magnitude": { "kind": "Scalar", "scalar": -10 } }
			  ]
			}
			""");
		File.WriteAllText(Path.Combine(actionDir, "draw_2.json"), """
			{
			  "kind": "Draw",
			  "params": { "count": 2 }
			}
			""");

		var discovery = new ContentModDiscovery();
		var bundle = ContentModLoader.Load(discovery.Scan(root).ValidMods[0]);
		var registry = new GameDefinitionRegistry();
		registry.Rebuild([bundle], out var report);

		Assert.That(report.RemovedValidationErrors, Is.Empty);
		Assert.That(registry.Contains(EContentCategory.Attribute, "Health"), Is.True);
		Assert.That(registry.Contains(EContentCategory.GameplayEffect, "weak"), Is.True);
		Assert.That(registry.Contains(EContentCategory.SkillAction, "draw_2"), Is.True);
		Assert.That(registry.Store.TryGetAttribute("Health", out var attribute), Is.True);
		Assert.That(attribute.DefaultBase, Is.EqualTo(100f));
		Assert.That(registry.Store.TryGetGameplayEffect("weak", out var gameplayEffect), Is.True);
		Assert.That(gameplayEffect.Modifiers, Has.Count.EqualTo(1));
		Assert.That(registry.Store.TryGetSkillAction("draw_2", out var action), Is.True);
		Assert.That(action.Kind, Is.EqualTo(ESkillActionKind.Draw));
	}

	[Test]
	public void Validation_rejects_gameplay_effect_with_unknown_attribute_modifier()
	{
		var bundle = ContentModTestHelper.EmptyBundle("base") with
		{
			GameplayEffects = ["bad_ge"],
			Definitions = ModDefinitionsBundle.Empty with
			{
				GameplayEffects = new Dictionary<string, GameplayEffectDefDto>
				{
					["bad_ge"] = new()
					{
						Id = "bad_ge",
						DurationPolicy = EDurationPolicy.Instant,
						Modifiers =
						[
							new AttributeModifierDefDto
							{
								AttributeId = "MissingAttribute",
								Operation = EAttributeModifierOp.Add,
								Magnitude = new MagnitudeDefDto
								{
									Kind = EMagnitudeKind.Scalar,
									Scalar = 1f,
								},
							},
						],
					},
				},
			},
		};

		var registry = new GameDefinitionRegistry();
		registry.Rebuild([bundle], out var report);

		Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
		Assert.That(registry.Contains(EContentCategory.GameplayEffect, "bad_ge"), Is.False);
	}

	[Test]
	public void Validation_rejects_unknown_gameplay_tag_when_tag_definitions_exist()
	{
		var bundle = ContentModTestHelper.EmptyBundle("base") with
		{
			GameplayEffects = ["bad_tag_ge"],
			GameplayTags = ["debuff.poison"],
			Definitions = ModDefinitionsBundle.Empty with
			{
				GameplayEffects = new Dictionary<string, GameplayEffectDefDto>
				{
					["bad_tag_ge"] = new()
					{
						Id = "bad_tag_ge",
						DurationPolicy = EDurationPolicy.Instant,
						GrantedTags = ["debuff.weak"],
					},
				},
				GameplayTags = new Dictionary<string, GameplayTagDefDto>
				{
					["debuff.poison"] = new()
					{
						Id = "debuff.poison",
					},
				},
			},
		};

		var registry = new GameDefinitionRegistry();
		registry.Rebuild([bundle], out var report);

		Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
		Assert.That(registry.Contains(EContentCategory.GameplayEffect, "bad_tag_ge"), Is.False);
	}
}
