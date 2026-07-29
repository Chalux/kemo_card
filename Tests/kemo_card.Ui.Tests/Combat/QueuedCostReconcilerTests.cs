using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>规格 §3.3：已标记卡牌的费用变化在玩家阶段内即时处理。</summary>
[TestFixture]
public sealed class QueuedCostReconcilerTests
{
    private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];

    [Test]
    public void Cheaper_cost_refunds_the_difference_and_updates_paid()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 3, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        Assert.That(character.AvailableEnergy, Is.EqualTo(2));

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 1);
        var autoCancelled = QueuedCostReconciler.Reconcile(sim);

        Assert.That(autoCancelled, Is.Empty);
        Assert.That(character.AvailableEnergy, Is.EqualTo(4), "退还差额 2");
        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Paid, Is.EqualTo(1));
        Assert.That(character.HandSlots[0].IsMarked, Is.True);
    }

    [Test]
    public void More_expensive_cost_tops_up_when_available_energy_is_enough()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        Assert.That(character.AvailableEnergy, Is.EqualTo(4));

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 3);
        var autoCancelled = QueuedCostReconciler.Reconcile(sim);

        Assert.That(autoCancelled, Is.Empty);
        Assert.That(character.AvailableEnergy, Is.EqualTo(2), "补差 2");
        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Paid, Is.EqualTo(3));
        Assert.That(character.HasActed, Is.False);
    }

    [Test]
    public void Insufficient_energy_auto_cancels_the_mark_and_refunds_everything()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 2);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        Assert.That(sim.TryApply(new ConfirmCharacterCommand(0)).Success, Is.True);
        Assert.That(character.AvailableEnergy, Is.Zero);

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 5);
        var autoCancelled = QueuedCostReconciler.Reconcile(sim);

        Assert.That(autoCancelled, Does.Contain(0));
        Assert.That(sim.CardQueue.Count, Is.Zero, "整项自动取消");
        Assert.That(character.AvailableEnergy, Is.EqualTo(2), "全额退还 paid");
        Assert.That(character.HasActed, Is.False, "回退未确认");
        Assert.That(character.HandSlots[0].IsEmpty, Is.False, "牌仍在手");
        Assert.That(character.HandSlots[0].IsMarked, Is.False);
    }

    [Test]
    public void Unchanged_cost_is_a_no_op()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 2, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        var sequenceBefore = sim.CardQueue.PeekAllOrdered().Single().Sequence;

        var autoCancelled = QueuedCostReconciler.Reconcile(sim);

        Assert.That(autoCancelled, Is.Empty);
        Assert.That(character.AvailableEnergy, Is.EqualTo(3));
        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Sequence, Is.EqualTo(sequenceBefore));
    }

    [Test]
    public void Reconcile_preserves_queue_order_when_paid_changes()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);
        Assert.That(sim.TryApply(new PlayCardCommand(1, 0, Enemy0)).Success, Is.True);
        var orderBefore = sim.CardQueue.PeekAllOrdered().Select(e => e.Sequence).ToArray();

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 2);
        QueuedCostReconciler.Reconcile(sim);

        Assert.That(sim.CardQueue.PeekAllOrdered().Select(e => e.Sequence).ToArray(), Is.EqualTo(orderBefore));
        Assert.That(sim.CardQueue.PeekAllOrdered().All(e => e.Paid == 2), Is.True);
    }

    [Test]
    public void Non_energy_cost_entries_are_left_alone()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(
            costType: ECostType.None,
            cost: 0,
            energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 4, costType: ECostType.None);
        var autoCancelled = QueuedCostReconciler.Reconcile(sim);

        Assert.That(autoCancelled, Is.Empty);
        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Paid, Is.Zero);
        Assert.That(character.AvailableEnergy, Is.EqualTo(5));
    }

    [Test]
    public void Player_phase_commands_run_reconciliation_automatically()
    {
        using var sim = CombatSimulationTestBuilder.PlayerPhaseWithHand(cost: 1, energy: 5);
        var character = sim.PlayerTeam.Characters[0];
        Assert.That(sim.TryApply(new PlayCardCommand(0, 0, Enemy0)).Success, Is.True);

        CombatSimulationTestBuilder.RepriceMarkableCard(sim, newCost: 3);
        // 任意一条成功的玩家阶段指令都应触发对账
        Assert.That(sim.TryApply(new UnconfirmCharacterCommand(1)).Success, Is.True);

        Assert.That(sim.CardQueue.PeekAllOrdered().Single().Paid, Is.EqualTo(3));
        Assert.That(character.AvailableEnergy, Is.EqualTo(2));
    }
}