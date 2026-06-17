using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiStateMachineTests
{
	[Test]
	public void Initial_state_is_Created()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Created));
	}

	[Test]
	public void Created_to_Opening_to_Opened_is_valid()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opening), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opened), Is.True);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Opened));
	}

	[Test]
	public void Opened_to_Closing_to_Closed_is_valid()
	{
		var sm = new UiStateMachine();
		sm.TryTransitionTo(UiLifecycleState.Opening);
		sm.TryTransitionTo(UiLifecycleState.Opened);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closing), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closed), Is.True);
	}

	[Test]
	public void Closed_to_Created_allows_reopen()
	{
		var sm = new UiStateMachine();
		sm.TryTransitionTo(UiLifecycleState.Opening);
		sm.TryTransitionTo(UiLifecycleState.Opened);
		sm.TryTransitionTo(UiLifecycleState.Closing);
		sm.TryTransitionTo(UiLifecycleState.Closed);

		Assert.That(sm.TryTransitionTo(UiLifecycleState.Created), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opening), Is.True);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Opening));
	}

	[Test]
	public void Created_to_Opened_is_invalid()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opened), Is.False);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Created));
	}
}
