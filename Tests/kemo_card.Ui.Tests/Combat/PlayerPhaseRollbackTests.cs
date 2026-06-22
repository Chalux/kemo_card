using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class PlayerPhaseRollbackTests
{
	[Test]
	public void Instant_skill_killing_queued_target_marks_holder_unacted()
	{
		var sim = CombatSimulationTestBuilder.WithQueuedCardTargetingEnemy(index: 0);
		sim.TryApply(new CastInstantSkillCommand(
			1,
			"execute",
			[new CombatTargetRef(ECombatSide.Enemy, 0)]));
		Assert.That(sim.PlayerTeam.Characters[0].HasActed, Is.False);
	}
}
