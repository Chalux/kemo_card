using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEffectExecutorTests
{
	[Test]
	public void Damage_preserves_fractional_amount()
	{
		var registry = CombatTestHelper.CreateFullRegistry(
			effects: new Dictionary<string, EffectDto>
			{
				["hit"] = new()
				{
					Id = "hit",
					Kind = EEffectKind.Damage,
					Params = new() { ["amount"] = 2.5f },
				},
			});
		var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
		var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: []);
		var executor = new CombatEffectExecutor(registry, sim.Rules);
		var source = new CombatTargetRef(ECombatSide.Player, 0);
		var target = new CombatTargetRef(ECombatSide.Enemy, 0);

		executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit" }, sim, source, [target]);

		Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(17.5f));
	}

	[Test]
	public void Damage_reduces_enemy_hp_through_rule_pipeline()
	{
		var registry = CombatTestHelper.CreateFullRegistry(
			effects: new Dictionary<string, EffectDto>
			{
				["hit"] = new() { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 5 } },
			});
		var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
		var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: [new SharedHpDefeatRule()]);
		var executor = new CombatEffectExecutor(registry, sim.Rules);
		var source = new CombatTargetRef(ECombatSide.Player, 0);
		var target = new CombatTargetRef(ECombatSide.Enemy, 0);

		executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit" }, sim, source, [target]);

		Assert.That(enemy.CurrentHp, Is.EqualTo(15));
	}
}
