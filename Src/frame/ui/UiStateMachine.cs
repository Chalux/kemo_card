namespace KemoCard.Frame.Ui;

public sealed class UiStateMachine
{
	public UiLifecycleState Current { get; private set; } = UiLifecycleState.Created;

	public bool TryTransitionTo(UiLifecycleState next)
	{
		if (!IsAllowed(Current, next))
		{
			return false;
		}

		Current = next;
		return true;
	}

	private static bool IsAllowed(UiLifecycleState from, UiLifecycleState to)
	{
		return (from, to) switch
		{
			(UiLifecycleState.Created, UiLifecycleState.Opening) => true,
			(UiLifecycleState.Opening, UiLifecycleState.Opened) => true,
			(UiLifecycleState.Opened, UiLifecycleState.Closing) => true,
			(UiLifecycleState.Closing, UiLifecycleState.Closed) => true,
			_ => false,
		};
	}
}
