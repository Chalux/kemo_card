using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatSimulationIntegrationTests
{
	private readonly record struct CombatOutcome(int SharedHp, int EnemyHp, ECombatPhase Phase);

	[Test]
	public void Same_seed_and_commands_produce_identical_outcome()
	{
		CombatOutcome RunOnce()
		{
			var sim = CombatSimulationTestBuilder.FullBattle(seed: 12345);
			sim.TryApply(new ConfirmCharacterCommand(0));
			sim.TryApply(new ConfirmCharacterCommand(1));
			sim.TryApply(new ConfirmCharacterCommand(2));
			sim.TryApply(new ConfirmCharacterCommand(3));
			sim.AdvancePhase();
			return new CombatOutcome(
				sim.PlayerTeam.SharedHp,
				sim.EnemyTeam.Enemies[0].CurrentHp,
				sim.Phase);
		}

		Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
	}
}
