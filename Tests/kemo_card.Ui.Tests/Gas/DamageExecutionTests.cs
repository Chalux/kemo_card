using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class DamageExecutionTests
{
	[Test]
	public void DamageExecution_reads_set_by_caller_and_attack_defense_then_reduces_health()
	{
		var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var target = GasTestHelper.CreateAscWithAttributes(
			(AttributeIds.PhysicalDefense, 3f),
			(AttributeIds.Health, 20f));
		var spec = new GameplayEffectSpec(
			CreateDamageExecutionGe(),
			sourceAsc: source,
			targetAsc: target,
			setByCaller: new Dictionary<string, float> { ["Amount"] = 6f });

		var runner = new ExecutionRunner();
		runner.Run(spec, target);

		Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(7f));
	}

	[Test]
	public void ApplyGameplayEffect_instant_executes_execution_list()
	{
		var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var target = GasTestHelper.CreateAscWithAttributes(
			(AttributeIds.PhysicalDefense, 3f),
			(AttributeIds.Health, 20f));
		var spec = new GameplayEffectSpec(
			CreateDamageExecutionGe(),
			sourceAsc: source,
			targetAsc: target,
			setByCaller: new Dictionary<string, float> { ["Amount"] = 6f });

		var result = target.ApplyGameplayEffect(spec);

		Assert.That(result.Success, Is.True);
		Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(7f));
	}

	[Test]
	public void DamageExecution_applies_damage_taken_scale_multiplier()
	{
		var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 4f));
		var target = GasTestHelper.CreateAscWithAttributes(
			(AttributeIds.PhysicalDefense, 0f),
			(AttributeIds.DamageTakenScale, 1f),
			(AttributeIds.Health, 20f));
		var spec = new GameplayEffectSpec(
			CreateDamageExecutionGe(),
			sourceAsc: source,
			targetAsc: target,
			setByCaller: new Dictionary<string, float> { ["Amount"] = 3f });

		var runner = new ExecutionRunner();
		runner.Run(spec, target);

		Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(6f));
	}

	private static GameplayEffectDefDto CreateDamageExecutionGe() => new()
	{
		Id = "ge.test.damage.execution",
		DurationPolicy = EDurationPolicy.Instant,
		Executions =
		[
			new ExecutionDefDto
			{
				Kind = "Damage",
				DamageType = "Physical",
			},
		],
	};
}
