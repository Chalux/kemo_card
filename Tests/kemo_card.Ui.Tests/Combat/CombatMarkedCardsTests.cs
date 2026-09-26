using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 已标记卡牌列表的展示口径：按角色分桶，桶内顺序 = 结算顺序（priority 降序 → 入队序号升序），
/// 越界角色索引丢弃（数据源是只读的出牌队列）。
/// </summary>
[TestFixture]
public sealed class CombatMarkedCardsTests
{
    [Test]
    public void Resolve_buckets_marked_cards_by_character_in_execution_order()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 0,
            CardId: "card.low",
            RuntimeInstanceId: "rt1",
            Priority: 1,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 1,
            CardId: "card.other",
            RuntimeInstanceId: "rt2",
            Priority: 5,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 0,
            CardId: "card.high",
            RuntimeInstanceId: "rt3",
            Priority: 9,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));

        var buckets = CombatMarkedCards.Resolve(sim);

        Assert.That(buckets, Has.Count.EqualTo(4), "索引与 PlayerTeam.Characters 对齐");
        Assert.That(buckets[0], Is.EqualTo(new[] { "card.high", "card.low" }), "同角色按结算顺序（priority 降序）");
        Assert.That(buckets[1], Is.EqualTo(new[] { "card.other" }));
        Assert.That(buckets[2], Is.Empty);
        Assert.That(buckets[3], Is.Empty);
    }

    [Test]
    public void Resolve_orders_same_priority_by_sequence()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 2,
            CardId: "card.first",
            RuntimeInstanceId: "rt1",
            Priority: 100,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 2,
            CardId: "card.second",
            RuntimeInstanceId: "rt2",
            Priority: 100,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));

        var buckets = CombatMarkedCards.Resolve(sim);

        Assert.That(buckets[2], Is.EqualTo(new[] { "card.first", "card.second" }), "同优先级先标记先结算");
    }

    [Test]
    public void Resolve_ignores_out_of_range_character_index()
    {
        using var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        sim.CardQueue.Enqueue(new QueuedCardEntry(
            CharacterIndex: 99,
            CardId: "card.ghost",
            RuntimeInstanceId: "rt1",
            Priority: 1,
            Targets: [],
            Sequence: sim.AllocateQueueSequence()));

        var buckets = CombatMarkedCards.Resolve(sim);

        Assert.That(buckets.SelectMany(bucket => bucket), Is.Empty);
    }

    [Test]
    public void Resolve_throws_on_null()
    {
        Assert.That(() => CombatMarkedCards.Resolve(null!), Throws.ArgumentNullException);
    }
}
