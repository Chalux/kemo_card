using KemoCard.Mod.Combat.Commands;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatCommandTests
{
    [Test]
    public void ConfirmCharacter_marks_has_acted_in_player_phase()
    {
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        var result = sim.TryApply(new ConfirmCharacterCommand(0));
        Assert.That(result.Success, Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].HasActed, Is.True);
    }
}