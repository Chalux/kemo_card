using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEndConditionTests
{
	[Test]
	public void Shared_hp_zero_triggers_defeat()
	{
		var sim = CombatSimulationTestBuilder.Standard();
		sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
		sim.CheckEndConditions();
		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
	}

	[Test]
	public void All_enemies_dead_triggers_victory_when_no_more_waves()
	{
		var sim = CombatSimulationTestBuilder.Standard();
		foreach (var enemy in sim.EnemyTeam.Enemies)
			enemy.ApplyDamage(enemy.MaxHp);
		sim.CheckEndConditions();
		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
	}
}
