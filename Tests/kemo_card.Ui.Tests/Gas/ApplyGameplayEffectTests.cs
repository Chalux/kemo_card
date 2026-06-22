using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class ApplyGameplayEffectTests
{
	[Test]
	public void ApplyGameplayEffect_instant_add_updates_max_health_base()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.MaxHealth, 100f));
		var def = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, 25f);
		var spec = new GameplayEffectSpec(def, targetAsc: asc);

		var result = asc.ApplyGameplayEffect(spec);

		Assert.That(result.Success, Is.True);
		Assert.That(result.Error, Is.Null);
		Assert.That(asc.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(125f));
	}

	[Test]
	public void ApplyGameplayEffect_duration_effect_aggregates_stacks_by_target()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.buff.attack",
			DurationPolicy = EDurationPolicy.HasDuration,
			DurationTurns = 2,
			StackingPolicy = EStackingPolicy.AggregateByTarget,
			MaxStacks = 3,
			Modifiers =
			[
				new AttributeModifierDefDto
				{
					AttributeId = AttributeIds.PhysicalAttack,
					Operation = EAttributeModifierOp.Add,
					Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
				},
			],
		};
		var first = asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));
		var second = asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));

		Assert.That(first.Success, Is.True);
		Assert.That(second.Success, Is.True);
		Assert.That(asc.ActiveEffects, Has.Count.EqualTo(1));
		Assert.That(asc.ActiveEffects[0].Stacks, Is.EqualTo(2));
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(20f));
	}

	[Test]
	public void RemoveActiveEffect_removes_registered_modifiers()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.buff.attack.remove",
			DurationPolicy = EDurationPolicy.Infinite,
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
			Modifiers =
			[
				new AttributeModifierDefDto
				{
					AttributeId = AttributeIds.PhysicalAttack,
					Operation = EAttributeModifierOp.Add,
					Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
				},
			],
		};
		var apply = asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));
		Assert.That(apply.Handle, Is.Not.Null);
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(15f));

		var removed = asc.RemoveActiveEffect(apply.Handle!.Value);

		Assert.That(removed, Is.True);
		Assert.That(asc.ActiveEffects, Is.Empty);
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
	}

	[Test]
	public void ApplyGameplayEffect_rejects_immediately_expired_duration_effect()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.zero.duration",
			DurationPolicy = EDurationPolicy.HasDuration,
			DurationTurns = 0,
			OngoingRequiredTags = ["state.combat"],
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
			Modifiers =
			[
				new AttributeModifierDefDto
				{
					AttributeId = AttributeIds.PhysicalAttack,
					Operation = EAttributeModifierOp.Add,
					Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
				},
			],
		};
		asc.Tags.AddTag("state.combat");
		var result = asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo("Effect expired immediately."));
		Assert.That(asc.ActiveEffects, Is.Empty);
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
	}
}
