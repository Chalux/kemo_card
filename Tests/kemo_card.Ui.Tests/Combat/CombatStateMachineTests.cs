using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatStateMachineTests
{
    [Test]
    public void Card_queue_empty_after_execution_moves_to_enemy_phase()
    {
        var sim = CombatSimulationTestBuilder.WithQueuedCard();

        sim.AdvancePhase();

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Enemy));
    }
}