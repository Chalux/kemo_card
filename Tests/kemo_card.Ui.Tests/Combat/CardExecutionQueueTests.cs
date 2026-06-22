using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardExecutionQueueTests
{
	[Test]
	public void Dequeue_returns_lowest_priority_first()
	{
		var queue = new CardExecutionQueue(new HostRng(7, "combat.queue"));
		queue.Enqueue(new QueuedCardEntry(0, "a", "rt-a", 5, [], 1));
		queue.Enqueue(new QueuedCardEntry(1, "b", "rt-b", 1, [], 2));
		var first = queue.TryDequeue(out var entry);
		Assert.That(first, Is.True);
		Assert.That(entry!.CardId, Is.EqualTo("b"));
	}

	[Test]
	public void Same_priority_uses_deterministic_rng_tiebreak()
	{
		var q1 = new CardExecutionQueue(new HostRng(99, "combat.queue"));
		var q2 = new CardExecutionQueue(new HostRng(99, "combat.queue"));
		q1.Enqueue(new QueuedCardEntry(0, "x", "rt-x", 3, [], 1));
		q1.Enqueue(new QueuedCardEntry(1, "y", "rt-y", 3, [], 2));
		q2.Enqueue(new QueuedCardEntry(0, "x", "rt-x", 3, [], 1));
		q2.Enqueue(new QueuedCardEntry(1, "y", "rt-y", 3, [], 2));
		q1.TryDequeue(out var e1);
		q2.TryDequeue(out var e2);
		Assert.That(e1!.CardId, Is.EqualTo(e2!.CardId));
	}
}
