using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class GameplayEffectDefTests
{
    [Test]
    public void GameplayEffectDef_deserializes_weak_like_json()
    {
        const string json = """
			{
			  "displayNameId": "ge.weak.name",
			  "durationPolicy": "HasDuration",
			  "durationTurns": 2,
			  "stackingPolicy": "AggregateByTarget",
			  "maxStacks": 3,
			  "modifiers": [
			    { "attributeId": "DamageTakenScale", "operation": "Add", "magnitude": { "kind": "Scalar", "scalar": 1 } }
			  ],
			  "grantedTags": ["debuff.weak"],
			  "hooks": { "onApply": [{ "actionId": "weak_apply_marker" }] }
			}
			""";
        var dto = JsonSerializer.Deserialize<GameplayEffectDefDto>(json, ContentDefinitionJson.Options);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.DisplayNameId, Is.EqualTo("ge.weak.name"));
        Assert.That(dto.DurationPolicy, Is.EqualTo(EDurationPolicy.HasDuration));
        Assert.That(dto.DurationTurns, Is.EqualTo(2));
        Assert.That(dto.StackingPolicy, Is.EqualTo(EStackingPolicy.AggregateByTarget));
        Assert.That(dto.MaxStacks, Is.EqualTo(3));
        Assert.That(dto.Modifiers, Has.Count.EqualTo(1));
        Assert.That(dto.Modifiers[0].AttributeId, Is.EqualTo("DamageTakenScale"));
        Assert.That(dto.Modifiers[0].Operation, Is.EqualTo(EAttributeModifierOp.Add));
        Assert.That(dto.Modifiers[0].Magnitude.Kind, Is.EqualTo(EMagnitudeKind.Scalar));
        Assert.That(dto.Modifiers[0].Magnitude.Scalar, Is.EqualTo(1f));
        Assert.That(dto.GrantedTags, Is.EquivalentTo(new[] { "debuff.weak" }));
        Assert.That(dto.Hooks.OnApply, Has.Count.EqualTo(1));
        Assert.That(dto.Hooks.OnApply[0].ActionId, Is.EqualTo("weak_apply_marker"));
    }
}