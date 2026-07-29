using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatStateMachineTests
{
    [Test]
    public void All_characters_confirmed_transitions_to_card_execution()
    {
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();

        for (var i = 0; i < 4; i++)
            sim.TryApply(new ConfirmCharacterCommand(i));

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    [Test]
    public void Card_queue_empty_after_execution_moves_to_enemy_phase()
    {
        var sim = CombatSimulationTestBuilder.WithQueuedCard();

        sim.AdvancePhase();

        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Enemy));
    }
}