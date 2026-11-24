using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class MarblePhaseTests
{
    // 固定盘面直向右发射可避开撞钉；1.35 弧度的射线正好计一分。
    internal static void ShootQueuedBalls(CombatSimulation sim, float firstAngle = 0f, float bonusAngle = 0f)
    {
        var angle = firstAngle;
        var hasBonusBall = sim.Marble.Queue.Count > 2;
        for (var step = 0; step < 3600 && sim.Phase == ECombatPhase.Marble; step++)
        {
            if (sim.Marble.CanLaunch)
            {
                Assert.That(sim.TryApply(new LaunchMarbleCommand(angle)).Success, Is.True);
                angle = hasBonusBall && sim.Marble.Queue.Count == 1 ? bonusAngle : 0f;
            }
            sim.StepMarble();
        }
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution), "最后一颗球离盘后才锁定结算");
    }

    [Test]
    public void Ending_cards_waits_for_input_and_only_fires_after_all_balls_retire()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        var hp = sim.EnemyTeam.Enemies[0].CurrentHp;
        var turn = sim.TurnNumber;
        sim.TryApply(new EndTurnCommand());
        sim.AdvanceAutomaticPhases();
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Marble));
        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(2));
        Assert.That(sim.NormalAttacks.ExecutionCount, Is.Zero);
        Assert.That(sim.TryApply(new EndTurnCommand()).Success, Is.False);
        Assert.That(sim.TryApply(new LaunchMarbleCommand(float.NaN)).Success, Is.False);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, [])).Success, Is.False);
        Assert.That(sim.TryApply(new LaunchMarbleCommand(1.35f)).Success, Is.True);
        Assert.That(sim.TryApply(new LaunchMarbleCommand(0)).Success, Is.False);
        for (var step = 0; step < 200 && sim.Marble.ActiveBalls.Count > 0; step++)
            sim.StepMarble();
        Assert.That(sim.Marble.Count, Is.EqualTo(1));
        Assert.That(sim.Marble.CanLaunch, Is.True);
        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(1));
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(hp));
        Assert.That(sim.TurnNumber, Is.EqualTo(turn));
        ShootQueuedBalls(sim);
        sim.AdvanceAutomaticPhases();
        Assert.That(sim.NormalAttacks.LastMarbleSettlement?.TotalCount, Is.EqualTo(1));
        Assert.That(sim.TurnNumber, Is.EqualTo(turn + 1));
    }

    [Test]
    public void Early_end_clock_starts_on_launch_and_recall_is_idempotent()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand();
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.NormalAttackCount, 2);
        Assert.That(sim.TryApply(new EndTurnCommand()).Success, Is.True);
        Assert.That(sim.Marble.Queue.Count, Is.EqualTo(4));
        for (var step = 0; step < CombatSimulation.MarbleEarlyEndTicks; step++)
            sim.StepMarble();
        Assert.That(sim.TryApply(new EndMarbleCommand()).Success, Is.False);
        sim.TryApply(new LaunchMarbleCommand(0));
        for (var step = 0; step < CombatSimulation.MarbleEarlyEndTicks - 1; step++)
            sim.StepMarble();
        Assert.That(sim.CanEndMarbleEarly, Is.False);
        sim.StepMarble();
        Assert.That(sim.TryApply(new LaunchMarbleCommand(0)).Success, Is.True);
        Assert.That(sim.TryApply(new EndMarbleCommand()).Success, Is.True);
        Assert.That(sim.TryApply(new EndMarbleCommand()).Success, Is.False);
        Assert.That(sim.Marble.ActiveBalls, Is.Empty);
        Assert.That(sim.Marble.Queue.Count, Is.Zero);
    }
}
