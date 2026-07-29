using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardExecutionQueueTests
{
    [Test]
    public void Dequeue_returns_highest_priority_first()
    {
        var queue = new CardExecutionQueue();
        queue.Enqueue(new QueuedCardEntry(0, "low", "rt-low", 100, [], 1));
        queue.Enqueue(new QueuedCardEntry(1, "high", "rt-high", 200, [], 2));

        Assert.That(queue.TryDequeue(out var entry), Is.True);
        Assert.That(entry!.CardId, Is.EqualTo("high"));
    }

    [Test]
    public void Same_priority_dequeues_in_enqueue_sequence_order()
    {
        var queue = new CardExecutionQueue();
        queue.Enqueue(new QueuedCardEntry(0, "first", "rt-first", 100, [], 1));
        queue.Enqueue(new QueuedCardEntry(1, "second", "rt-second", 100, [], 2));
        queue.Enqueue(new QueuedCardEntry(2, "third", "rt-third", 100, [], 3));

        Assert.That(DrainCardIds(queue), Is.EqualTo(new[] { "first", "second", "third" }));
    }

    [Test]
    public void Mixed_priority_and_sequence_produces_total_order()
    {
        var queue = new CardExecutionQueue();
        queue.Enqueue(new QueuedCardEntry(0, "s1", "rt-s1", 100, [], 1));
        queue.Enqueue(new QueuedCardEntry(1, "s2", "rt-s2", 200, [], 2));
        queue.Enqueue(new QueuedCardEntry(2, "s3", "rt-s3", 100, [], 3));

        Assert.That(DrainCardIds(queue), Is.EqualTo(new[] { "s2", "s1", "s3" }));
    }

    [Test]
    public void PeekAllOrdered_matches_dequeue_order_without_consuming()
    {
        var queue = new CardExecutionQueue();
        queue.Enqueue(new QueuedCardEntry(0, "s1", "rt-s1", 50, [], 1));
        queue.Enqueue(new QueuedCardEntry(1, "s2", "rt-s2", 300, [], 2));
        queue.Enqueue(new QueuedCardEntry(2, "s3", "rt-s3", 300, [], 3));

        var peeked = queue.PeekAllOrdered().Select(e => e.CardId).ToArray();

        Assert.That(peeked, Is.EqualTo(new[] { "s2", "s3", "s1" }));
        Assert.That(queue.Count, Is.EqualTo(3));
        Assert.That(DrainCardIds(queue), Is.EqualTo(peeked));
    }

    [Test]
    public void Ordering_is_independent_of_enqueue_call_order()
    {
        var ascending = new CardExecutionQueue();
        ascending.Enqueue(new QueuedCardEntry(0, "s1", "rt-s1", 100, [], 1));
        ascending.Enqueue(new QueuedCardEntry(1, "s2", "rt-s2", 200, [], 2));

        var descending = new CardExecutionQueue();
        descending.Enqueue(new QueuedCardEntry(1, "s2", "rt-s2", 200, [], 2));
        descending.Enqueue(new QueuedCardEntry(0, "s1", "rt-s1", 100, [], 1));

        Assert.That(DrainCardIds(ascending), Is.EqualTo(new[] { "s2", "s1" }));
        Assert.That(DrainCardIds(descending), Is.EqualTo(new[] { "s2", "s1" }));
    }

    [Test]
    public void TryRemove_takes_out_matching_entry_and_keeps_remaining_order()
    {
        var queue = new CardExecutionQueue();
        queue.Enqueue(new QueuedCardEntry(0, "s1", "rt-s1", 100, [], 1));
        queue.Enqueue(new QueuedCardEntry(1, "s2", "rt-s2", 200, [], 2));
        queue.Enqueue(new QueuedCardEntry(2, "s3", "rt-s3", 100, [], 3));

        Assert.That(queue.TryRemove(e => e.CardId == "s2", out var removed), Is.True);
        Assert.That(removed!.CardId, Is.EqualTo("s2"));
        Assert.That(DrainCardIds(queue), Is.EqualTo(new[] { "s1", "s3" }));
    }

    [Test]
    public void TryDequeue_on_empty_queue_returns_false()
    {
        var queue = new CardExecutionQueue();

        Assert.That(queue.TryDequeue(out var entry), Is.False);
        Assert.That(entry, Is.Null);
    }

    private static string[] DrainCardIds(CardExecutionQueue queue)
    {
        var ids = new List<string>();
        while (queue.TryDequeue(out var entry) && entry is not null)
            ids.Add(entry.CardId);
        return [.. ids];
    }
}